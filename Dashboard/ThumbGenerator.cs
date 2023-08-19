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

		private readonly ConcurrentDictionary<string, CachedFileInfo> files = new();

		private sealed class CacheFileLoadCanceledException : Exception
		{
			public CacheFileLoadCanceledException(string? message)
				: base(message) { }
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
				get
				{
					using var _ = new ObjectLock(this);
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
				temps.InitRoot();
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
			public bool CanClearTemps => temps.CanClear;
			public DateTime LastCacheUseTime => settings.LastCacheUseTime;
			public string CurrentThumbPath => Path.Combine(settings.GetSettingsDir(), settings.CurrentThumb ??
				throw new InvalidOperationException("Should not have been called before exiting .GenerateThumb")
			);
			public int ChosenThumbOptionInd => settings.ChosenThumbOptionInd ??
				throw new InvalidOperationException("Should not have been called before exiting .GenerateThumb");
			public bool IsDeletable => !File.Exists(InpPath) || !Settings.Root.AllowedExts.MatchesFile(InpPath);

			#region ThumbSources

			private ThumbSource[]? thumb_sources;

			public IReadOnlyList<ThumbSource> ThumbSources => thumb_sources ??
				throw new InvalidOperationException("Should not have been called before exiting .GenerateThumb");

			private void SetTempSource(ThumbSource source)
			{
				thumb_sources = new[] { source };
				settings.CurrentThumb = source.Extract(0.3, null!);
				COMManip.ResetThumbFor(InpPath);
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
				using var _ = new ObjectLock(this);
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

				var base_path = settings.GetSettingsDir() + @"\";
				if (res.StartsWith(base_path))
					res = res.Remove(0, base_path.Length);

				settings.CurrentThumb = res;
				COMManip.ResetThumbFor(InpPath);
			}

			#endregion

			#region Temps

			private sealed class GenerationTemp : IDisposable
			{
				private readonly string path;
				private readonly Action<string>? on_unload;

				public GenerationTemp(string path, Action<string>? on_unload)
				{
					this.path = path;
					this.on_unload = on_unload;
				}

				public string Path => path;

				public bool IsDeletable => on_unload != null;

				private bool dispose_disabled = false;
				public void DisableDispose() => dispose_disabled = true;

				public void Dispose()
				{
					if (dispose_disabled) return;
					if (on_unload is null) return;
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

			private long FileSize(string fname) => new FileInfo(fname).Length;
			private bool DeleteFile(string fname)
			{
				if (!fname.StartsWith(settings.GetSettingsDir()))
					throw new InvalidOperationException(fname);
				using var _ = new ObjectLock(this);
				if (!File.Exists(fname))
					return false;
				var sz = FileSize(fname);
				File.Delete(fname);
				InvokeCacheSizeChanged(-sz);
				return true;
			}

			private long DirSize(string dir) => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(FileSize);
			private bool DeleteDir(string dir)
			{
				if (!dir.StartsWith(settings.GetSettingsDir()))
					throw new InvalidOperationException(dir);
				using var _ = new ObjectLock(this);
				if (!Directory.Exists(dir))
					return false;
				var sz = DirSize(dir);
				Directory.Delete(dir, true);
				InvokeCacheSizeChanged(-sz);
				return true;
			}

			private sealed class LocalTempsList : IDisposable
			{
				private readonly CachedFileInfo cfi;
				private readonly Dictionary<string, GenerationTemp> d = new();

				public LocalTempsList(CachedFileInfo cfi) => this.cfi = cfi;

				private bool IsRoot => cfi.temps == this;
				private void OnChanged()
				{
					if (!IsRoot) return;
					var common_path = cfi.settings.GetSettingsDir() + @"\";
					cfi.settings.TempsListStr = string.Join(';',
						d.Select(kvp =>
						{
							var path = kvp.Value.Path;
							if (!path.StartsWith(common_path))
								throw new InvalidOperationException(path);
							path = path.Remove(0, common_path.Length);
							return $"{kvp.Key}={path}";
						})
					);
				}

				private GenerationTemp AddExisting(string temp_name, GenerationTemp temp)
				{
					if ("=;".Any(temp_name.Contains))
						throw new FormatException(temp_name);
					d.Add(temp_name, temp);
					OnChanged();
					return temp;
				}
				private GenerationTemp AddNew(string temp_name, GenerationTemp temp)
				{
					// delete last gen leftovers
					temp.Dispose();
					return AddExisting(temp_name, temp);
				}
				public void InitRoot()
				{
					if (!IsRoot) throw new InvalidOperationException();
					if (d.Count!=0) throw new InvalidOperationException();
					d.Add("settings", new GenerationTemp(cfi.settings.GetSettingsFile(), null));
					d.Add("settings backup", new GenerationTemp(cfi.settings.GetSettingsBackupFile(), null));
					var tls = cfi.settings.TempsListStr;
					if (tls is null) return;
					foreach (var tle in tls.Split(';'))
					{
						var tle_spl = tle.Split(new[] { '=' }, 2);
						if (tle_spl.Length!=2) throw new FormatException(tle);
						var temp_name = tle_spl[0];
						var temp_path = Path.Combine(cfi.settings.GetSettingsDir(), tle_spl[1]);
						if (d.TryGetValue(temp_name, out var old_temp))
						{
							if (old_temp.IsDeletable || old_temp.Path != temp_path)
								throw new InvalidOperationException($"{cfi.settings.GetSettingsDir()}: {temp_name}");
							continue;
						}
						GenerationTemp temp;
						if (File.Exists(temp_path))
							temp = new(temp_path, fname => cfi.DeleteFile(fname));
						else if (Directory.Exists(temp_path))
							temp = new(temp_path, dir => cfi.DeleteDir(dir));
						else
							continue;
						d.Add(temp_name, temp);
					}
					OnChanged();
				}
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
					OnChanged();
					return t;
				}

				public int DeleteExtraFiles()
				{
					if (!IsRoot) throw new InvalidOperationException();
					var non_del = new HashSet<string>(d.Count);
					foreach (var t in d.Values)
						if (!non_del.Add(t.Path))
							throw new InvalidOperationException();

					int on_dir(string dir)
					{
						var res = 0;

						foreach (var subdir in Directory.EnumerateDirectories(dir))
						{
							if (non_del.Contains(subdir)) continue;
							res += on_dir(subdir);
							if (!Directory.EnumerateFileSystemEntries(subdir).Any())
								Directory.Delete(subdir, recursive: false);
						}

						foreach (var fname in Directory.EnumerateFiles(dir))
						{
							if (non_del.Contains(fname)) continue;
							res += 1;
							File.Delete(fname);
							Log.Append($"Deleted extra file [{fname}]");
						}

						return res;
					}
					return on_dir(cfi.settings.GetSettingsDir());
				}

				public void GiveToCFI(string temp_name)
				{
					if (IsRoot) throw new InvalidOperationException();
					if (!d.Remove(temp_name, out var t))
						throw new InvalidOperationException(temp_name);
					cfi.temps.AddExisting(temp_name, t);
				}

				public bool CanClear => d.Values.Any(t=>t.IsDeletable);
				public void Clear()
				{
					foreach (var (temp_name,_) in d.Where(kvp=>kvp.Value.IsDeletable).ToArray())
					{
						if (TryRemove(temp_name) is null)
							throw new InvalidOperationException();
					}
				}
				public void VerifyEmpty()
				{
					if (!CanClear) return;
					throw new InvalidOperationException();
				}

				public void Dispose()
				{
					if (IsRoot)
						throw new InvalidOperationException();
					if (d.Values.Any(t => !t.IsDeletable))
						throw new InvalidOperationException();
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
						App.Current!.Dispatcher.Invoke(() => App.Current.Shutdown(-1));
					}
				}

			}
			private readonly LocalTempsList temps;

			public int DeleteExtraFiles()
			{
				using var _ = new ObjectLock(this);
				return temps.DeleteExtraFiles();
			}

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
				//public static ThumbSource Locked { get; } = Make("Locked");
				public static ThumbSource SoundOnly { get; } = Make("SoundOnly");
				public static ThumbSource Broken { get; } = Make("Broken");

			}

			public void GenerateThumb(Action<string, CustomThreadPool.JobWork> add_job, Action<ICachedFileInfo> on_regenerated, bool force_regen)
			{
				using var _ = new ObjectLock(this);

				if (is_erased)
					return;

				var inp_fname = settings.InpPath ?? throw new InvalidOperationException();
				var otp_temp_name = "thumb file";

				if (!File.Exists(inp_fname))
				{
					Log.Append($"Asked thumb for missing file [{inp_fname}]");
					SetTempSource(CommonThumbSources.Broken);
					return;
				}

				settings.LastCacheUseTime = DateTime.UtcNow;
				var write_time = new[]{
					File.GetLastWriteTimeUtc(inp_fname),
					// this can get stuck accessing input file to make thumb,
					// then resetting it to make it update - which triggers another regen
					//File.GetLastAccessTimeUtc(inp_fname),
					new FileInfo(inp_fname).CreationTimeUtc,
				}.Max();

				{
					var total_wait = TimeSpan.FromSeconds(5);
					var waited = DateTime.UtcNow-write_time;
					if (waited < total_wait)
					{
						temps.TryRemove(otp_temp_name);
						temps.VerifyEmpty();
						settings.CurrentThumbIsFinal = false;
						SetTempSource(CommonThumbSources.Waiting);
						System.Threading.Tasks.Task.Delay(total_wait-waited)
							.ContinueWith(t => Utils.HandleException(
								() => GenerateThumb(add_job, on_regenerated, force_regen)
							));
						return;
					}
				}

				if (!force_regen && settings.LastInpChangeTime == write_time && settings.CurrentThumbIsFinal)
					return;

				temps.TryRemove(otp_temp_name);
				temps.VerifyEmpty();
				settings.CurrentThumbIsFinal = false;
				SetTempSource(CommonThumbSources.Ungenerated);

				add_job($"Generating thumb for: {inp_fname}", change_subjob =>
				{
					using var _ = new ObjectLock(this);

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

					// For now have removed the setting for this
					//var sw = System.Diagnostics.Stopwatch.StartNew();

					var sources = new List<ThumbSource>();

					change_subjob("getting metadata");
					var metadata_s = FFmpeg.Invoke($"-i \"{inp_fname}\" -hide_banner -show_format -show_streams -print_format xml", ()=>true, exe: "probe").Result.otp;
					change_subjob(null);

					try
					{
						change_subjob("parsing metadata XML");
						var metadata_xml = XDocument.Parse(metadata_s).Root!;
						change_subjob(null);

						var dur_s = "";
						var max_frame_len = double.NaN;
						if (metadata_xml.Descendants("streams").SingleOrDefault() is XElement streams_xml)
							foreach (var stream_xml in streams_xml.Descendants("stream"))
							{
								var ind = int.Parse(stream_xml.Attribute("index")!.Value);
								change_subjob($"checking stream#{ind}");

								var codec_type_s = stream_xml.Attribute("codec_type")!.Value;

								string? get_tag(string key) =>
									stream_xml.Descendants("tag").SingleOrDefault(n => n.Attribute("key")!.Value == key)?.Attribute("value")!.Value;

								var tag_filename = get_tag("filename");
								var tag_mimetype = get_tag("mimetype");

								var stream_is_image = tag_mimetype!=null && tag_mimetype.StartsWith("image/");

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
									// G:\0Music\3Sort\!fix\Selulance (soundcloud)\[20150103] Selulance - What.mkv
									//if (stream_is_image)
									//	// Should work, but throw to find such file
									//	throw new NotImplementedException(inp_fname);
								}

								var frame_rate_spl = stream_xml.Attribute("r_frame_rate")!.Value.Split(new[] { '/' }, 2);
								var frame_len = stream_is_image || is_attachment ? double.NaN :
									int.Parse(frame_rate_spl[0]) / (double)int.Parse(frame_rate_spl[1]);
								if (double.IsNaN(max_frame_len) || max_frame_len < frame_len)
									max_frame_len = frame_len;

								var l_dur_s1 = stream_xml.Attribute("duration")?.Value;
								var l_dur_s2 = get_tag("DURATION") ?? get_tag("DURATION-eng");
								// torrent subs stream can have boths
								//if ((l_dur_s1 != null) && (l_dur_s2 != null))
								//	throw new NotImplementedException($"[{inp_fname}]: [{metadata_s}]");

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

								if (!double.IsNaN(frame_len))
								{
									l_dur -= TimeSpan.FromSeconds(frame_len);
									if (l_dur < TimeSpan.Zero) l_dur = TimeSpan.Zero;
								}
								else if (l_dur != TimeSpan.Zero)
									throw new NotImplementedException();

								sources.Add(new(source_name, l_dur, (pos, change_subjob) =>
								{
									using var _ = new ObjectLock(this);
									try
									{
										using var l_temps = new LocalTempsList(this);
										var args = new List<string>();

										temps.TryRemove(otp_temp_name);
										var otp_temp = l_temps.AddFile(otp_temp_name, "thump.png");
										var otp_fname = otp_temp.Path;

										if (l_dur != TimeSpan.Zero)
											args.Add($"-ss {pos*l_dur.TotalSeconds}");

										//TODO https://trac.ffmpeg.org/ticket/10512
										// - Need to cd into input folder for conversion to work
										string ffmpeg_path;

										//TODO https://trac.ffmpeg.org/ticket/10506
										// - Need to first extract the attachments, before they can be used as input
										var attachments_dir_temp_name = "attachments dir";
										if (is_attachment)
										{
											change_subjob("dump attachment");
											if (l_dur != TimeSpan.Zero)
												throw new NotImplementedException();

											var attachments_dir = l_temps.AddDir(attachments_dir_temp_name, "attachments").Path;
											Directory.CreateDirectory(attachments_dir);

											var arg_dump_attachments = $"-nostdin -dump_attachment:t \"\" -i \"{inp_fname}\"";
											var attachment_fname = Path.Combine(attachments_dir, tag_filename!);
											FFmpeg.Invoke(arg_dump_attachments, ()=>File.Exists(attachment_fname), execute_in: attachments_dir).Wait();
											InvokeCacheSizeChanged(+DirSize(attachments_dir));

											ffmpeg_path = Path.GetDirectoryName(attachment_fname)!;
											args.Add($"-i \"{Path.GetFileName(attachment_fname)}\"");
											change_subjob(null);
										}
										else
										{
											ffmpeg_path = Path.GetDirectoryName(inp_fname)!;
											args.Add($"-i \"{Path.GetFileName(inp_fname)}\"");
											args.Add($"-map 0:{ind}");
										}

										args.Add($"-vframes 1");
										args.Add($"-vf scale=256:256:force_original_aspect_ratio=decrease");
										args.Add($"\"{otp_fname}\"");
										args.Add($"-y");
										args.Add($"-nostdin");

										change_subjob("extract thumb");
										FFmpeg.Invoke(string.Join(' ', args), ()=>File.Exists(otp_fname), execute_in: ffmpeg_path).Wait();
										InvokeCacheSizeChanged(+FileSize(otp_fname));
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
											otp_temp.Dispose();
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
											InvokeCacheSizeChanged(+FileSize(otp_fname));
											change_subjob(null);

										}

										l_temps.GiveToCFI(otp_temp_name);
										l_temps.VerifyEmpty();
										return otp_fname;
									}
									catch (Exception e)
									{
										Log.Append($"Error making thumb for [{inp_fname}]: {e}");
										//Utils.HandleException(e);
										return CommonThumbSources.Broken.Extract(0, null!);
									}
								}));

								change_subjob(null);
							}

						var format_xml = metadata_xml.Descendants("format").SingleOrDefault();
						if (format_xml is null)
						{
							if (sources.Count != 0)
								throw new NotImplementedException(inp_fname);
							Log.Append($"No format data for [{inp_fname}]: {metadata_s}");
							sources.Add(CommonThumbSources.Broken);
						}
						else if (format_xml.Attribute("duration") is XAttribute global_dur_xml)
						{
							change_subjob("make dur string");
							var global_dur = TimeSpan.FromSeconds(double.Parse(global_dur_xml.Value));
							if (!double.IsNaN(max_frame_len) && global_dur < TimeSpan.FromSeconds(max_frame_len))
								global_dur = TimeSpan.Zero;

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
								var frac_s = s.ToString("N"+(5-dur_s.Length))[2..].TrimEnd('0');
								if (frac_s!="") dur_s += '_'+frac_s;
							}
							change_subjob(null);
						}

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
					settings.CurrentThumbIsFinal = true;
					on_regenerated(this);

				});
			}

			#endregion

			public void ClearTemps()
			{
				using var _ = new ObjectLock(this);
				settings.LastInpChangeTime = DateTime.MinValue;
				settings.LastCacheUseTime = DateTime.MinValue;
				settings.CurrentThumb = null;
				temps.Clear();
			}

			private bool is_erased = false;
			public void Erase()
			{
				using var _ = new ObjectLock(this);
				if (is_erased)
					throw new InvalidOperationException();
				is_erased = true;
				Shutdown();
				DeleteDir(settings.GetSettingsDir());
				COMManip.ResetThumbFor(InpPath);
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
					// will be deleted by cleanup
					//if (!File.Exists(path))
					//	throw new CacheFileLoadCanceledException($"3!File [{path}] does not exist");
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
					App.Current!.Dispatcher.Invoke(()=>App.Current.Shutdown(-1));

			}

			new System.Threading.Thread(() =>
			{
				while (true)
					try
					{
						System.Threading.Thread.Sleep(TimeSpan.FromMinutes(1));
						ClearInvalid();
						ClearExtraFiles();
					}
					catch (Exception e)
					{
						Utils.HandleException(e);
					}
			})
			{
				IsBackground = true,
				Name = $"Cleanup of [{cache_dir}]",
			}.Start();

		}

		public event Action<long>? CacheSizeChanged;
		private void InvokeCacheSizeChanged(long byte_change) =>
			CacheSizeChanged?.Invoke(byte_change);

		private readonly OneToManyLock purge_lock = new();

		private volatile uint last_used_id = 0;
		private CachedFileInfo GetCFI(string fname)
		{
			// Cannot add concurently, because .GetOrAdd can create
			// multiple instances of cfi for the same fname in different threads
			if (files.TryGetValue(fname, out var cfi)) return cfi;
			using var _ = new ObjectLock(files);
			if (files.TryGetValue(fname, out cfi)) return cfi;
			while (true)
			{
				var id = System.Threading.Interlocked.Increment(ref last_used_id);
				var cache_file_dir = cache_dir.CreateSubdirectory(id.ToString());
				if (cache_file_dir.EnumerateFileSystemInfos().Any()) continue;
				cfi = new(id, cache_file_dir.FullName, InvokeCacheSizeChanged, fname);
				if (!files.TryAdd(fname, cfi))
					throw new InvalidOperationException();
				return cfi;
			}
		}

		public ICachedFileInfo Generate(string fname, Action<ICachedFileInfo> on_regenerated, bool force_regen) => purge_lock.ManyLocked(() =>
		{
			var cfi = GetCFI(fname);
			cfi.GenerateThumb(thr_pool.AddJob, on_regenerated, force_regen);
			return cfi;
		});

		public void MassGenerate(IEnumerable<string> fnames, bool force_regen) => purge_lock.ManyLocked(() =>
		{
			foreach (var fname in fnames)
				GetCFI(fname).GenerateThumb(thr_pool.AddJob, _ => { }, force_regen);
		});

		public int ClearInvalid() {
			var c = purge_lock.OneLocked(() =>
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
			}, with_priority: false);
			// this... may get annoying
			//if (c!=0) TTS.Speak($"ThumbDashboard: Invalid entries: {c}");
			return c;
		}

		public int ClearExtraFiles()
		{
			var c = purge_lock.OneLocked(() =>
			{
				var c = 0;
				foreach (var cfi in files.Values)
					c += cfi.DeleteExtraFiles();
				return c;
			}, with_priority: false);
			if (c!=0) TTS.Speak($"ThumbDashboard: Extra files: {c}");
			return c;
		}

		public void ClearOne() => purge_lock.OneLocked(() =>
		{
			if (files.IsEmpty) return;
			var cfi = files.Values.Where(cfi=>cfi.CanClearTemps).MinBy(cfi => cfi.LastCacheUseTime);
			cfi?.ClearTemps();
		}, with_priority: false);

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
		}, with_priority: true);

		public void Shutdown()
		{
			foreach (var cfi in files.Values)
				cfi.Shutdown();
			lock_file.Close();
			File.Delete(Path.Combine(cache_dir.FullName, ".lock"));
		}

	}

}