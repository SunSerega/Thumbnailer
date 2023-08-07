using System;

using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;

using System.IO;

using System.Xml.Linq;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dashboard
{
	public class ThumbGenerator
	{
		private readonly CustomThreadPool thr_pool;
		private readonly DirectoryInfo cache_dir;
		private readonly FileStream lock_file;
		private readonly System.Timers.Timer cleanup_timer;

		private readonly ConcurrentDictionary<string, CachedFileInfo> files = new();

		private sealed class CacheFileLoadCanceledException : Exception
		{
			public CacheFileLoadCanceledException(string? message)
				: base(message) { }
		}

		private static async System.Threading.Tasks.Task<(string otp, string err)> RunFFmpeg(string args, string? execute_in = null, string exe = "mpeg")
		{

			var p = new System.Diagnostics.Process
			{
				StartInfo = new("ff"+exe, args)
				{
					UseShellExecute = false,
					RedirectStandardInput = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					CreateNoWindow = true,
				}
			};

			if (execute_in != null)
				p.StartInfo.WorkingDirectory = execute_in;

			p.Start();

			var t_otp = p.StandardOutput.ReadToEndAsync();
			var t_err = p.StandardError.ReadToEndAsync();

			_=System.Threading.Tasks.Task.Run(() => Utils.HandleException(() =>
			{
				if (p.WaitForExit(TimeSpan.FromSeconds(10)))
					return;
				p.Kill();
				CustomMessageBox.Show(
					$"[{p.StartInfo.FileName} {p.StartInfo.Arguments}] hanged. Output:",
					t_otp.Result + "\n\n===================\n\n" + t_err.Result
				);
			}));

			return (await t_otp, await t_err);
		}

		#region ThumbSource

		public sealed class ThumbSource
		{
			private readonly string name;
			private readonly TimeSpan len;
			private readonly Func<double, Action<string?>, string> extract;

			public ThumbSource(string name, TimeSpan len, Func<double, Action<string?>, string> extract)
			{
				this.name = name;
				this.len = len;
				this.extract = extract;
			}

			public string Name => name;

			public TimeSpan Length => len;

			public string Extract(double pos, Action<string?> change_subjob)
			{
				if (pos<0 || pos>1) throw new InvalidOperationException();
				return extract(pos, change_subjob);
			}

		}

		#endregion

		#region CachedFileInfo

		public interface ICachedFileInfo
		{

			public string CurrentThumbPath { get; }
			public BitmapImage CurrentThumbBmp {
				get {
					lock (this)
						return Utils.LoadUncachedBitmap(CurrentThumbPath);
				}
			}

			public IReadOnlyList<ThumbSource> ThumbSources { get; }

			public int ChosenThumbOptionInd { get; }

			public void ApplySourceAt(bool force_regen, Action<string?> change_subjob, int ind, in double? in_pos, out double out_pos);

		}

		private sealed class CachedFileInfo : ICachedFileInfo
		{
			private readonly uint id;
			private readonly FileSettings settings;

			public event Action<long>? CacheSizeChanged;
			private void InvokeCacheSizeChanged(long change) =>
				CacheSizeChanged?.Invoke(change);

			public CachedFileInfo(uint id, string cache_path, Action<long> on_cache_changed)
			{
				if (!Path.IsPathRooted(cache_path))
					throw new ArgumentException($"Path [{cache_path}] was not rooted", nameof(cache_path));
				if (!Directory.Exists(cache_path))
					throw new InvalidOperationException();
				this.id = id;
				CacheSizeChanged += on_cache_changed;
				settings = new(cache_path);
				temps = new(this);
				temps.AddSettings();
			}

			public CachedFileInfo(uint id, string cache_path, Action<long> on_cache_changed, string target_fname)
				: this(id, cache_path, on_cache_changed)
			{
				if (!Path.IsPathRooted(target_fname))
					throw new ArgumentException($"Path [{target_fname}] was not rooted", nameof(target_fname));
				settings.InpPath = target_fname;
			}

			public uint Id => id;
			public string? InpPath => settings.InpPath;
			public DateTime LastCacheUseTime => settings.LastCacheUseTime;
			public string CurrentThumbPath => settings.CurrentThumb ??
				throw new InvalidOperationException("Should not have been called before exiting .GenerateThumb");
			public int ChosenThumbOptionInd => settings.ChosenThumbOptionInd ??
				throw new InvalidOperationException("Should not have been called before exiting .GenerateThumb");
			public bool IsDeletable => !File.Exists(InpPath);

			#region ThumbSources

			private ThumbSource[]? thumb_sources;

			public IReadOnlyList<ThumbSource> ThumbSources => thumb_sources ??
				throw new InvalidOperationException("Should not have been called before exiting .GenerateThumb");

			private void SetTempSource(ThumbSource source)
			{
				thumb_sources = new[] { source };
				settings.CurrentThumb = source.Extract(0.3, null!);
			}
			private void SetSources(Action<string?> change_subjob, ThumbSource[] thumb_sources)
			{
				var chosen_poss = settings.ChosenStreamPositions;
				if (chosen_poss != null && chosen_poss.Count != thumb_sources.Length)
					chosen_poss = null;
				chosen_poss ??= new FileSettings.ChosenStreamPositionsInfo(thumb_sources.Length);
				settings.ChosenStreamPositions = chosen_poss;
				this.thumb_sources = thumb_sources;
				ApplySourceAt(true, change_subjob, settings.ChosenThumbOptionInd??thumb_sources.Length-1, null, out _);
			}

			public void ApplySourceAt(bool force_regen, Action<string?> change_subjob, int ind, in double? in_pos, out double out_pos)
			{
				lock (this)
				{
					if (settings.ChosenStreamPositions is null)
						throw new InvalidOperationException();
					var source = ThumbSources[ind];

					double pos = in_pos ?? settings.ChosenStreamPositions[ind];
					out_pos = pos;
					if (!force_regen && ind == settings.ChosenThumbOptionInd && pos == settings.ChosenStreamPositions[ind])
						return;

					var res = source.Extract(pos, change_subjob);

					settings.ChosenThumbOptionInd = ind;
					if (settings.ChosenStreamPositions[ind] != pos)
					{
						var poss = settings.ChosenStreamPositions;
						poss[ind] = pos;
						settings.ChosenStreamPositions = null;
						settings.ChosenStreamPositions = poss;
					}
					settings.CurrentThumb = res;
					COMManip.ResetThumbFor(InpPath);

				}
			}

			#endregion

			#region Temps

			private sealed class GenerationTemp : IDisposable
			{
				private readonly string path;
				private readonly Action<string> on_unload;

				public GenerationTemp(string path, Action<string> on_unload)
				{
					this.path = path;
					this.on_unload = on_unload;
				}

				public string Path => path;

				private bool dispose_disabled = false;
				public void DisableDispose() => dispose_disabled = true;

				public void Dispose()
				{
					if (dispose_disabled) return;
					//TODO still broken sometimes
					// - Reworked generation since then
					//on_unload(path);
					for (int i = 1; ; ++i)
						try
						{
							on_unload(path);
							break;
						}
						catch (Exception e)
						{
							if (i%100 == 0)
								CustomMessageBox.Show("Struggling to delete [{path}]", e.ToString());
							System.Threading.Thread.Sleep(10);
						}
				}

			}

			private bool DeleteFile(string fname)
			{
				if (!fname.StartsWith(settings.GetSettingsDir()))
					throw new InvalidOperationException(fname);
				lock (this)
				{
					if (!File.Exists(fname))
						return false;
					var info = new FileInfo(fname);
					var sz = info.Length;
					info.Delete();
					InvokeCacheSizeChanged(-sz);
				}
				return true;
			}
			private bool DeleteDir(string dir)
			{
				if (!dir.StartsWith(settings.GetSettingsDir()))
					throw new InvalidOperationException(dir);
				lock (this)
				{
					if (!Directory.Exists(dir))
						return false;
					var info = new DirectoryInfo(dir);
					var sz = info.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
					info.Delete(true);
					InvokeCacheSizeChanged(-sz);
				}
				return true;
			}

			private sealed class LocalTempsList : IDisposable
			{
				private readonly CachedFileInfo cfi;
				private readonly Dictionary<string, GenerationTemp> d = new();

				public LocalTempsList(CachedFileInfo cfi) => this.cfi = cfi;

				private GenerationTemp AddNew(string temp_name, GenerationTemp temp)
				{
					// delete last gen leftovers
					temp.Dispose();
					d.Add(temp_name, temp);
					return temp;
				}
				public void AddSettings() => d.Add("settings", new GenerationTemp(cfi.settings.GetSettingsFile(), _ => { }));
				public GenerationTemp AddFile(string temp_name, string fname) => AddNew(temp_name, new GenerationTemp(
					Path.Combine(cfi.settings.GetSettingsDir(), fname),
					fname => cfi.DeleteFile(fname)
				));
				public GenerationTemp AddDir(string temp_name, string dir) => AddNew(temp_name, new GenerationTemp(
					Path.Combine(cfi.settings.GetSettingsDir(), dir),
					dir => cfi.DeleteDir(dir)
				));

				public GenerationTemp? TryRemove(string temp_name)
				{
					if (!d.Remove(temp_name, out var t))
						return null;
					t.Dispose();
					return t;
				}

				//TODO Use to clean up unused files
				// - Make a log file to report this kind of stuff without messageboxes
				// - Also announce with TTS
				public IEnumerable<string> EnumerateExtraFiles()
				{
					throw new NotImplementedException();
				}

				public void GiveToCFI(string temp_name)
				{
					if (!d.Remove(temp_name, out var t))
						throw new InvalidOperationException(temp_name);
					cfi.temps.d.Add(temp_name, t);
				}

				public void VerifyEmpty()
				{
					if (d.Count == (d.ContainsKey("settings")?1:0)) return;
					throw new InvalidOperationException();
				}

				public void Dispose()
				{
					try
					{
						foreach (var temp in d.Values)
							temp.Dispose();
					}
					catch (Exception e)
					{
						Utils.HandleException(e);
						// unrecoverable, but also unimaginable
						Console.Beep(); Console.Beep(); Console.Beep();
						App.Current.Dispatcher.Invoke(() => App.Current.Shutdown(-1));
					}
				}

			}
			private readonly LocalTempsList temps;

			#endregion

			#region Generate

			private static class CommonThumbSources
			{
				private static ThumbSource Make(string name) {
					var full_path = Path.GetFullPath($"Dashboard-Default.{name}.bmp");
					if (!File.Exists(full_path))
						throw new InvalidOperationException(name);
					return new(name, TimeSpan.Zero, (_,_)=>full_path);
				}

				public static ThumbSource Ungenerated { get; } = Make("Ungenerated");
				public static ThumbSource Waiting { get; } = Make("Waiting");
				public static ThumbSource Locked { get; } = Make("Locked");
				public static ThumbSource SoundOnly { get; } = Make("SoundOnly");

			}

			public void GenerateThumb(Action<CustomThreadPool.JobWork> add_job, Action<ICachedFileInfo> on_regenerated, bool force_regen)
			{
				lock (this)
				{
					if (is_erased)
						return;
				}

				settings.LastCacheUseTime = DateTime.UtcNow;
				var inp_fname = settings.InpPath ?? throw new InvalidOperationException();
				var write_time = new[]{
					File.GetLastWriteTimeUtc(inp_fname),
					File.GetLastAccessTimeUtc(inp_fname),
					new FileInfo(inp_fname).CreationTimeUtc,
				}.Min();

				{
					var total_wait = TimeSpan.FromSeconds(5);
					var waited = DateTime.UtcNow-write_time;
					if (waited < total_wait)
						lock (this)
						{
							SetTempSource(CommonThumbSources.Waiting);
							System.Threading.Tasks.Task.Delay(total_wait-waited)
								.ContinueWith(t => Utils.HandleException(
									() => GenerateThumb(add_job, on_regenerated, force_regen)
								));
							return;
						}
				}

				var otp_temp_name = "thumb file";
				lock (this)
				{
					if (!force_regen && settings.LastInpChangeTime == write_time)
						return;

					_=temps.TryRemove(otp_temp_name);
					//_=temps.TryRemove("embeds dir");
					temps.VerifyEmpty();
					SetTempSource(CommonThumbSources.Ungenerated);
				}

				add_job(change_subjob =>
				{
					//change_subjob("copy");
					//var temp_fname = state.AddFile("inp").Path;
					//try
					//{
					//	var f = Microsoft.CopyOnWrite.CopyOnWriteFilesystemFactory.GetInstance();
					//	if (f.CopyOnWriteLinkSupportedBetweenPaths(FilePath!, temp_fname))
					//	{
					//		if (File.Exists(temp_fname))
					//			File.Delete(temp_fname);
					//		f.CloneFile(FilePath!, temp_fname);
					//	}
					//	else
					//		File.Copy(FilePath!, temp_fname, true);
					//}
					//catch when (!File.Exists(FilePath!))
					//{
					//	return;
					//}
					//// https://learn.microsoft.com/en-us/dotnet/standard/io/handling-io-errors#handling-ioexception
					//catch (IOException e) when ((e.HResult & 0xFFFF) == 32) // ERROR_SHARING_VIOLATION
					//{
					//	var res = locked_fname;
					//	settings.ThumbPath = res;
					//	ret(res);
					//	//settings.LastUpdate = write_time;
					//	return;
					//}

					lock (this)
					{
						// For now have removed the setting for this
						//var sw = System.Diagnostics.Stopwatch.StartNew();

						var sources = new List<ThumbSource>();

						change_subjob("getting metadata");
						var metadata_s = RunFFmpeg($"-i \"{inp_fname}\" -hide_banner -show_format -show_streams -print_format xml", exe: "probe").Result.otp;
						change_subjob(null);

						try
						{
							change_subjob("parsing metadata XML");
							var metadata_xml = XDocument.Parse(metadata_s).Root!;
							change_subjob(null);

							var format_xml = metadata_xml.Descendants("format").Single();

							var dur_s = "";
							if (format_xml.Attribute("duration") is XAttribute global_dur_xml)
							{
								change_subjob("make dur string");
								var global_dur = TimeSpan.FromSeconds(double.Parse(global_dur_xml.Value));
								if (dur_s!="" || global_dur.TotalHours>=1)
									dur_s += Math.Truncate(global_dur.TotalHours).ToString() + ':';
								if (dur_s!="" || global_dur.Minutes!=0)
									dur_s += global_dur.Minutes.ToString("00") + ':';
								if (dur_s!="" || global_dur.Seconds!=0)
									dur_s += global_dur.Seconds.ToString("00");
								if (dur_s.Length<5)
								{
									var s = global_dur.TotalSeconds;
									s -= Math.Truncate(s);
									dur_s += '_'+s.ToString("N"+(5-dur_s.Length))[2..].TrimEnd('0');
								}
								change_subjob(null);
							}

							foreach (var stream_xml in metadata_xml.Descendants("streams").Single().Descendants("stream"))
							{
								var ind = int.Parse(stream_xml.Attribute("index")!.Value);
								change_subjob($"checking stream#{ind}");

								string? get_tag(string key) =>
									stream_xml.Descendants("tag").SingleOrDefault(n => n.Attribute("key")!.Value == key)?.Attribute("value")!.Value;

								var tag_filename = get_tag("filename");
								var tag_mimetype = get_tag("mimetype");

								var stream_is_image = tag_mimetype!=null && tag_mimetype.StartsWith("image/");

								var l_dur_s1 = stream_xml.Attribute("duration")?.Value;
								var l_dur_s2 = get_tag("DURATION") ?? get_tag("DURATION-eng");
								// torrent subs stream can have boths
								//if ((l_dur_s1 != null) && (l_dur_s2 != null))
								//	throw new NotImplementedException($"[{inp_fname}]: [{metadata_s}]");

								var codec_type_s = stream_xml.Attribute("codec_type")!.Value;

								var is_attachment = codec_type_s == "attachment";
								if (is_attachment)
								{
									if (tag_filename == null)
										throw new InvalidOperationException();
									if (tag_mimetype == null)
										throw new InvalidOperationException();
								}
								else // !is_attachment
								{
									if (stream_is_image)
										// Should work, but throw to find such file
										throw new NotImplementedException();
								}

								if (codec_type_s switch // skip if
								{
									"video" => false,
									"audio" => true,
									"subtitle" => true,
									"attachment" => !stream_is_image,
									_ => throw new FormatException(codec_type_s),
								}) continue;

								string source_name;
								TimeSpan l_dur;
								if (stream_is_image)
								{
									source_name = $"Image:{ind}";
									l_dur = TimeSpan.Zero;
								}
								else if (l_dur_s1 != null)
								{
									source_name = $"FStream:{ind}";
									l_dur = TimeSpan.FromSeconds(double.Parse(l_dur_s1));
								}
								else if (l_dur_s2 != null)
								{
									source_name = $"TStream:{ind}";
									if (!l_dur_s2.Contains('.'))
										throw new FormatException();
									l_dur_s2 = l_dur_s2.TrimEnd('0').TrimEnd('.'); // Otherwise TimeSpan.Parse breaks
									l_dur = TimeSpan.Parse(l_dur_s2);
								}
								else
								{
									source_name = $"?:{ind}";
									l_dur = TimeSpan.Zero;
								}

								sources.Add(new(source_name, l_dur, (pos, change_subjob) =>
								{
									lock (this)
									{
										using var l_temps = new LocalTempsList(this);
										var args = new List<string>();

										_=temps.TryRemove(otp_temp_name);
										var otp_fname = l_temps.AddFile(otp_temp_name, "thump.png").Path;

										if (l_dur != TimeSpan.Zero)
											args.Add($"-ss {pos*l_dur.TotalSeconds}");

										//TODO https://trac.ffmpeg.org/ticket/10506
										var attachments_dir_temp_name = "attachments dir";
										if (is_attachment)
										{
											change_subjob("dump attachment");
											if (l_dur != TimeSpan.Zero)
												throw new NotImplementedException();

											var attachments_dir = l_temps.AddDir(attachments_dir_temp_name, "attachments").Path;
											Directory.CreateDirectory(attachments_dir);

											RunFFmpeg($"-nostdin -dump_attachment:t \"\" -i \"{inp_fname}\"", execute_in: attachments_dir).Wait();
											var attachment_fname = Path.Combine(attachments_dir, tag_filename!);
											if (!File.Exists(attachment_fname)) throw new InvalidOperationException();

											args.Add($"-i \"{attachment_fname}\"");
											change_subjob(null);
										}
										else
										{
											args.Add($"-i \"{inp_fname}\"");
											args.Add($"-map 0:{ind}");
										}

										args.Add($"-vframes 1");
										args.Add($"-vf scale=256:256:force_original_aspect_ratio=decrease");
										args.Add($"\"{otp_fname}\"");
										args.Add($"-y");
										args.Add($"-nostdin");

										change_subjob("extract thumb");
										var (extract_otp, extract_err) = RunFFmpeg(string.Join(' ', args)).Result;
										if (!File.Exists(otp_fname))
											throw new InvalidOperationException($"{extract_otp}\n\n===\n\n{extract_err}");
										if (is_attachment)
											if (l_temps.TryRemove(attachments_dir_temp_name) is null)
												throw new InvalidOperationException();
										change_subjob(null);

										if (dur_s!="")
										{
											change_subjob("load bg image");
											Size sz;
											var bg_im = new Image();
											{
												var bg_im_source = Utils.LoadUncachedBitmap(otp_fname);
												sz = new(bg_im_source.Width, bg_im_source.Height);
												if (sz.Width>256 || sz.Height>256)
													throw new InvalidOperationException();
												bg_im.Source = bg_im_source;
											}
											File.Delete(otp_fname);
											change_subjob(null);

											change_subjob("compose output");
											var res_c = new Grid();
											res_c.Children.Add(bg_im);
											res_c.Children.Add(new Viewbox
											{
												Opacity = 0.6,
												Width = sz.Width,
												Height = sz.Height*0.2,
												HorizontalAlignment = HorizontalAlignment.Right,
												VerticalAlignment = VerticalAlignment.Center,
												Child = new Image
												{
													Source = new DrawingImage
													{
														Drawing = new GeometryDrawing
														{
															Brush = Brushes.Black,
															Pen = new Pen(Brushes.White, 0.08),
															Geometry = new FormattedText(
														dur_s,
														System.Globalization.CultureInfo.InvariantCulture,
														FlowDirection.LeftToRight,
														new Typeface(
															new TextBlock().FontFamily,
															FontStyles.Normal,
															FontWeights.ExtraBold,
															FontStretches.Normal
														),
														1,
														Brushes.Black,
														96
													).BuildGeometry(default)
														}
													}
												},
											});
											change_subjob(null);

											change_subjob("render output");
											var bitmap = new RenderTargetBitmap((int)sz.Width, (int)sz.Height, 96, 96, PixelFormats.Pbgra32);
											res_c.Measure(sz);
											res_c.Arrange(new(sz));
											bitmap.Render(res_c);
											change_subjob(null);

											change_subjob("save output");
											var enc = new PngBitmapEncoder();
											enc.Frames.Add(BitmapFrame.Create(bitmap));
											using (Stream fs = File.Create(otp_fname))
												enc.Save(fs);
											change_subjob(null);

										}

										InvokeCacheSizeChanged(0);

										l_temps.GiveToCFI(otp_temp_name);
										l_temps.VerifyEmpty();
										return otp_fname;
									}
								}));

							}

							change_subjob(null);
						}
						catch (InvalidOperationException e)
						{
							throw new FormatException(metadata_s, e);
						}
						catch (NullReferenceException e)
						{
							throw new FormatException(metadata_s, e);
						}
						catch (FormatException e)
						{
							throw new FormatException(metadata_s, e);
						}

						if (sources.Count==0)
							sources.Add(CommonThumbSources.SoundOnly);

						settings.LastInpChangeTime = write_time;
						SetSources(change_subjob, sources.ToArray());
						on_regenerated(this);
					}

				});
			}

			#endregion

			private bool is_erased = false;
			public void Erase()
			{
				lock (this)
				{
					if (is_erased)
						throw new InvalidOperationException();
					is_erased = true;
					Shutdown();
					DeleteDir(settings.GetSettingsDir());
					COMManip.ResetThumbFor(InpPath);
				}
			}

			// Shutdown without erasing (when exiting)
			public void Shutdown() => settings.Shutdown();

		}

		#endregion

		public ThumbGenerator(CustomThreadPool thr_pool, string cache_dir, Action<string?> change_subjob)
		{
			this.thr_pool = thr_pool;
			this.cache_dir = Directory.CreateDirectory(cache_dir);
			this.lock_file = File.Create(Path.Combine(this.cache_dir.FullName, ".lock"));

			var dirs = this.cache_dir.GetDirectories();

			var all_file_paths = new HashSet<string>();
			var purge_acts = new List<Action>();
			var failed_load = new List<(string id, string message)>();
			var conflicting_caches = new Dictionary<string, List<uint>>();

			for (var i = 0; i < dirs.Length; ++i)
			{
				change_subjob($"Loaded {i}/{dirs.Length}");
				var dir = dirs[i];
				Action? purge_act = () => dir.Delete(true);
				try
				{
					if (!uint.TryParse(dir.Name, out var id))
						throw new CacheFileLoadCanceledException($"1!Invalid ID");
					var cfi = new CachedFileInfo(id, dir.FullName, InvokeCacheSizeChanged);
					purge_act = cfi.Erase;
					var path = cfi.InpPath ?? 
						throw new CacheFileLoadCanceledException($"2!No file is assigned");
					if (!File.Exists(path))
						throw new CacheFileLoadCanceledException($"3!File [{path}] does not exist");
					if (!all_file_paths.Add(path))
					{
						if (!conflicting_caches.TryGetValue(path, out var l))
						{
							if (!files.TryRemove(path, out var old_cfi))
								throw new InvalidOperationException();
							purge_acts.Add(old_cfi.Erase);
							l = new() { old_cfi.Id };
							conflicting_caches[path] = l;
						}
						l.Add(id);
						throw new CacheFileLoadCanceledException("");
					}
					if (!files.TryAdd(path, cfi))
						throw new InvalidOperationException();
					purge_act = null;
				}
				catch (CacheFileLoadCanceledException e)
				{
					if (purge_act is null)
						throw new InvalidOperationException();
					purge_acts.Add(purge_act);
					if (!string.IsNullOrEmpty(e.Message))
						failed_load.Add((dir.Name, e.Message));
				}
			}

			if (purge_acts.Count!=0)
			{
				var lns = new List<string>();
				void header(string h)
				{
					if (lns.Count!=0)
						lns.Add("");
					lns.Add(h);
				}

				if (failed_load.Count!=0)
				{
					header("Id-s failed to load");
					foreach (var g in failed_load.GroupBy(t=>t.message).OrderBy(g=>g.Key))
					{
						var message = g.Key;
						var ids = g.Select(t => t.id).Order();
						lns.Add($"{string.Join(',',ids)}: {message}");
					}
					lns.Add(new string('=', 30));
				}

				if (conflicting_caches.Count!=0)
				{
					header("Id-s referred to the same file");
					foreach (var (path, ids) in conflicting_caches)
						lns.Add($"[{path}]: {string.Join(',', ids)}");
					lns.Add(new string('=', 30));
				}

				header("Purge all the deviants?");

				if (CustomMessageBox.ShowYesNo("Settings load failed",
					string.Join(Environment.NewLine, lns)
				))
					foreach (var purge_act in purge_acts) purge_act();
				else
					App.Current.Dispatcher.Invoke(()=>App.Current.Shutdown(-1));

				foreach (var purge_act in purge_acts) purge_act();
			}

			//TODO Change delay, when there is more of a cache
			cleanup_timer = new System.Timers.Timer(TimeSpan.FromSeconds(1));
			cleanup_timer.Elapsed += (_,_) => ClearInvalid();
			cleanup_timer.Start();

		}

		public event Action<long>? CacheSizeChanged;
		private void InvokeCacheSizeChanged(long byte_change) =>
			CacheSizeChanged?.Invoke(byte_change);

		private readonly OneToManyLock purge_lock = new();

		private volatile uint last_used_id = 0;
		public ICachedFileInfo Generate(string fname, Action<ICachedFileInfo> on_regenerated, bool force_regen) => purge_lock.ManyLocked(() =>
		{

			if (!files.TryGetValue(fname, out var cfi))
				// Cannot add concurently, because .GetOrAdd can create
				// multiple instances of cfi for the same fname in different threads
				lock (files)
				{
					if (!files.TryGetValue(fname, out cfi))
						while (true)
						{
							var id = System.Threading.Interlocked.Increment(ref last_used_id);
							var cache_file_dir = cache_dir.CreateSubdirectory(id.ToString());
							if (cache_file_dir.EnumerateFileSystemInfos().Any()) continue;
							cfi = new(id, cache_file_dir.FullName, InvokeCacheSizeChanged, fname);
							if (!files.TryAdd(fname, cfi))
								throw new InvalidOperationException();
						}
				}

			lock (cfi) cfi.GenerateThumb(w => thr_pool.AddJob($"Generating thumb for: {fname}", w), on_regenerated, force_regen);

			return cfi;
		});

		public int ClearInvalid() => purge_lock.OneLocked(() =>
		{
			var to_remove = new List<KeyValuePair<string, CachedFileInfo>>(files.Count);
			foreach (var kvp in files)
				if (kvp.Value.IsDeletable)
					to_remove.Add(kvp);

			foreach (var kvp in to_remove)
			{
				if (!files.TryRemove(kvp))
					throw new InvalidOperationException();
				kvp.Value.Erase();
			}

			if (to_remove.Count!=0)
			{
				last_used_id = 0;
				InvokeCacheSizeChanged(0);
			}

			return to_remove.Count;
		}, false);

		public void ClearOne() => purge_lock.OneLocked(() =>
		{
			if (files.IsEmpty) return;
			var kvp = files.MinBy(kvp => kvp.Value.LastCacheUseTime);
			if (!files.TryRemove(kvp))
				throw new InvalidOperationException();
			kvp.Value.Erase();
			last_used_id = 0;
			InvokeCacheSizeChanged(0);
		}, false);

		public void ClearAll(Action<string?> change_subjob) => purge_lock.OneLocked(() =>
		{
			while (!files.IsEmpty)
				try
				{
					change_subjob($"left: {files.Count}");
					if (!files.TryRemove(files.Keys.First(), out var cfi))
						continue;
					cfi.Erase();
				}
				catch (Exception e)
				{
					Utils.HandleException(e);
				}
			last_used_id = 0;
			InvokeCacheSizeChanged(0);
			Console.Beep();
			//App.Current.Dispatcher.Invoke(() =>
			//	CustomMessageBox.Show("Done clearing cache!", App.Current.MainWindow)
			//);
		});

		public void Shutdown()
		{
			foreach (var cfi in files.Values)
				cfi.Shutdown();
			lock_file.Close();
			File.Delete(Path.Combine(cache_dir.FullName, ".lock"));
		}

	}
}