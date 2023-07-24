﻿using System;

using System.Linq;
using System.Collections.Concurrent;

using System.IO;

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

		private class CachedFileInfo
		{
			private (uint u, string s) hash;
			private readonly FileSettings settings;
			//private WeakReference<Bitmap?> cached_bitmap = new(null);

			public event Action<long>? CacheSizeChanged;

			public (uint u, string s) Hash => hash;

			public CachedFileInfo(string fname, Func<uint, bool> validate_new_hash, Action<long> on_cache_changed)
			{
				CacheSizeChanged += on_cache_changed;

				if (!Path.IsPathRooted(fname))
					throw new ArgumentException($"Path [{fname}] was not rooted", nameof(fname));

				{
					var hash = 17u;
					foreach (var ch in fname)
						hash = unchecked(hash*23 + ch);
					while (!validate_new_hash(hash))
						hash = unchecked(hash * 59);
					this.hash = (hash, hash.ToString("X8"));
				}

				settings = new(hash.s)
				{
					FilePath = fname
				};

			}

			public CachedFileInfo(DirectoryInfo dir, (uint u, string s) hash)
			{
				this.hash = hash;
				settings = new(hash.s);
				if (settings.FilePath == null)
					throw new InvalidOperationException(dir.FullName);
			}

			private const string ungenerated_fname	= @"Dashboard-Default.Ungenerated.bmp";
			private const string waiting_fname		= @"Dashboard-Default.Waiting.bmp";
			private const string locked_fname		= @"Dashboard-Default.Locked.bmp";
			private const string sound_only_fname	= @"Dashboard-Default.SoundOnly.bmp";

			public void GenerateThumb(Action<Action> addJob, Action<string> ret, bool force_regen)
			{
				var write_time = File.GetLastWriteTimeUtc(FilePath!);
				if (force_regen || settings.LastUpdate != write_time) lock (this)
				{
					if (File.Exists(settings.ThumbPath) && Path.GetDirectoryName(settings.ThumbPath) != Environment.CurrentDirectory)
					{
						CacheSizeChanged?.Invoke(new FileInfo(settings.ThumbPath).Length);
						File.Delete(settings.ThumbPath);
					}
					settings.ThumbPath = null;
				}
				if (settings.ThumbPath != null) return;

				{
					var total_wait = TimeSpan.FromSeconds(5);
					var waited = DateTime.UtcNow-write_time;
					if (waited < total_wait)
					{
						settings.ThumbPath = Path.GetFullPath(waiting_fname);
						System.Threading.Tasks.Task.Delay(total_wait-waited)
							.ContinueWith(t => Utils.HandleExtension(
								() => GenerateThumb(addJob, ret, true)
							));
						return;
					}
				}

				addJob(() =>
				{
					lock (this)
					{
						if (settings.ThumbPath != null) return;

						static DirectoryInfo make_dir(string path)
						{
							if (Directory.Exists(path))
								Directory.Delete(path, true);
							return Directory.CreateDirectory(path);
						}

						static System.Threading.Tasks.Task<string> use_ffmpeg(string args, string? execute_in = null)
						{
							var p = new System.Diagnostics.Process
							{
								StartInfo = new("ffmpeg", "-nostdin "+args)
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

							var otp = new System.Text.StringBuilder();
							p.OutputDataReceived += (o, e) =>
							{
								if (e.Data is null) return;
								otp.AppendLine(e.Data);
							};
							p.BeginOutputReadLine();

							p.Start();

							var res = p.StandardError.ReadToEndAsync();
							_=System.Threading.Tasks.Task.Run(() => Utils.HandleExtension(() =>
							{
								if (p.WaitForExit(TimeSpan.FromSeconds(10)))
									return;
								p.Kill();
								//MessageBox.Show(otp.ToString(), "FFmpeg hanged. Output:");
								//MessageBox.Show(res.Result, $"[{p.StartInfo.FileName} {p.StartInfo.Arguments}] hanged. Output:");
								MessageBox.Show(otp.ToString() + "\n\n===================\n\n" + res.Result, $"[{p.StartInfo.FileName} {p.StartInfo.Arguments}] hanged. Output:");
							}));

							return res;
						}

						//TODO FFmpeg something continues to hold onto one of files???
						static void delete_loop(string path, Action<string> del)
						{
							for (int i = 1; ; ++i)
								try
								{
									del(path);
									break;
								}
								catch (Exception e)
								{
									if (i%100 == 0)
										MessageBox.Show($"Struggling to delete [{path}]: {e}");
									System.Threading.Thread.Sleep(10);
								}
						}

						var temp_fname = Path.Combine(settings.GetDir()!, "inp");
						try
						{
							var f = Microsoft.CopyOnWrite.CopyOnWriteFilesystemFactory.GetInstance();
							if (f.CopyOnWriteLinkSupportedBetweenPaths(FilePath!, temp_fname))
							{
								if (File.Exists(temp_fname))
									File.Delete(temp_fname);
								f.CloneFile(FilePath!, temp_fname);
							}
							else
								File.Copy(FilePath!, temp_fname, true);
						}
						catch when (!File.Exists(FilePath!))
						{
							return;
						}
						// https://learn.microsoft.com/en-us/dotnet/standard/io/handling-io-errors#handling-ioexception
						catch (IOException e) when ((e.HResult & 0xFFFF) == 32) // ERROR_SHARING_VIOLATION
						{
							var res = Path.GetFullPath(locked_fname);
							settings.ThumbPath = res;
							ret(res);
							//settings.LastUpdate = write_time;
							return;
						}

						try
						{
							var sw = System.Diagnostics.Stopwatch.StartNew();

							var metadata = use_ffmpeg($"-i \"{temp_fname}\" -f ffmetadata").Result;

							var has_vid = KnownRegexes.MetadataVideoStreamHead().IsMatch(metadata);
							var dur_m = KnownRegexes.MetadataDuration().Matches(metadata).SingleOrDefault()?.Groups[1].Value;
							if (metadata.Contains(temp_fname+@": Invalid data found when processing input"))
							{
								if (dur_m is null)
									dur_m = @"N/A";
								else
									throw new Exception($"{FilePath}:\n\n{metadata}");
							}
							if (dur_m is null) throw new Exception($"Cannot find duration of [{FilePath}] in:\n\n{metadata}");
							var dur = dur_m==@"N/A" ? default(TimeSpan?) : TimeSpan.Parse(dur_m);

							var attachments_dir = make_dir(Path.Combine(settings.GetDir()!, "attachments"));
							use_ffmpeg($"-dump_attachment:t \"\" -i \"{temp_fname}\"", attachments_dir.FullName).Wait();

							var attachment_converions = attachments_dir.EnumerateFiles().Select(f =>
							{
								var inp = f.FullName;
								var otp = Path.ChangeExtension(inp, ".bmp");
								if (File.Exists(otp))
									throw new NotImplementedException(otp);
								return (inp, otp, t: use_ffmpeg($"-i \"{inp}\" -vf scale=256:256:force_original_aspect_ratio=decrease \"{otp}\""));
							}).ToArray();

							var valid_embeds_dir = make_dir(Path.Combine(settings.GetDir()!, "valid_embeds"));
							var valid_embeds = attachment_converions.Where(r =>
							{
								var ffmpeg_res = r.t.Result;

								var vid_stream_found = KnownRegexes.MetadataVideoStreamHead().IsMatch(ffmpeg_res);
								var otp_created = File.Exists(r.otp);
								if (vid_stream_found != otp_created)
									throw new NotImplementedException(r.inp);

								return otp_created;
							}).Select(r=>{
								var fname1 = r.otp;
								var fname2 = Path.Combine(valid_embeds_dir.FullName, Path.GetFileName(fname1));
								File.Copy(fname1, fname2, false);
								return fname2;
							}).ToArray();

							delete_loop(attachments_dir.FullName, path=>Directory.Delete(path,true));
							if (valid_embeds.Length==0)
								valid_embeds_dir.Delete();

							var frame_fname = Path.Combine(settings.GetDir(), "frame.png");
							string bg_file;
							if (valid_embeds.Length != 0)
								bg_file = valid_embeds[0];
							else if (!has_vid || dur is null)
								bg_file = Path.GetFullPath(sound_only_fname);
							else
							{
								if (File.Exists(frame_fname))
									File.Delete(frame_fname);
								var frame_at = dur.Value * 0.3;
								string ffmpeg_res;
								while (true)
								{
									ffmpeg_res = use_ffmpeg($"-skip_frame nokey -ss {Math.Truncate(frame_at.TotalSeconds)} -i \"{temp_fname}\" -vframes 1 -vf scale=256:256:force_original_aspect_ratio=decrease \"{frame_fname}\"").Result;
									if (!ffmpeg_res.Contains("File ended prematurely")) break;
									frame_at /= 2;
									if (frame_at != default) continue;
									frame_fname = Path.GetFullPath(sound_only_fname);
									break;
								}
								if (!File.Exists(frame_fname))
								{
									MessageBox.Show($"> -skip_frame nokey -ss {Math.Truncate(frame_at.TotalSeconds)} -i \"{temp_fname}\" -vframes 1 -vf scale=256:256:force_original_aspect_ratio=decrease \"{frame_fname}\"\n\n"+ffmpeg_res, FilePath!);
									return;
								}
								bg_file = frame_fname;
							}

							var bg_im = new Image();
							try
							{
								using var str = File.OpenRead(bg_file);
								var bg_im_source = new BitmapImage();
								bg_im_source.BeginInit();
								bg_im_source.CacheOption = BitmapCacheOption.OnLoad;
								bg_im_source.StreamSource = str;
								bg_im_source.EndInit();
								bg_im.Source = bg_im_source;
							}
							catch (System.Runtime.InteropServices.COMException e)
							{
								//TODO bg_file is null or "" sometimes?????
								MessageBox.Show(e.Message, $"File: [{settings.FilePath}] Image: [{bg_file}]");
							}

							bg_im.Measure(new(256,256));
							var sz = bg_im.DesiredSize;

							var dur_s = "";
							if (dur != null)
							{
								if (dur_s!="" || dur.Value.TotalHours>=1)
									dur_s += Math.Truncate(dur.Value.TotalHours).ToString() + ':';
								if (dur_s!="" || dur.Value.Minutes!=0)
									dur_s += dur.Value.Minutes.ToString("00") + ':';
								if (dur_s!="" || dur.Value.Seconds!=0)
									dur_s += dur.Value.Seconds.ToString("00");
								if (dur_s.Length<5)
								{
									var s = dur.Value.TotalSeconds;
									s -= Math.Truncate(s);
									dur_s += '('+s.ToString("N"+(5-dur_s.Length))[2..].TrimEnd('0')+')';
								}
							}

							var res_c = new Grid();
							res_c.Children.Add(bg_im);
							res_c.Children.Add(new Viewbox
							{
								Opacity = 0.6,
								Width = sz.Width,
								Height = sz.Height*0.2,
								HorizontalAlignment = HorizontalAlignment.Right,
								VerticalAlignment = VerticalAlignment.Center,
								Child = new Image { Source = new DrawingImage { Drawing = new GeometryDrawing
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
								} } },
							});

							var bitmap = new RenderTargetBitmap((int)sz.Width, (int)sz.Height, 96, 96, PixelFormats.Pbgra32);

							res_c.Measure(sz);
							res_c.Arrange(new(sz));
							bitmap.Render(res_c);

							var res_path = Path.Combine(settings.GetDir(), "thumb.png");

							var enc = new PngBitmapEncoder();
							enc.Frames.Add(BitmapFrame.Create(bitmap));
							using (Stream fs = File.Create(res_path))
								enc.Save(fs);

							// Make sure this is not mixed with .Generate call
							//lock (this)
							// Instead, this whole job is locked
							{
								settings.LastRecalcTime = sw.Elapsed.ToString();
								settings.ThumbPath = res_path;
								settings.LastUpdate = write_time;
								ret(res_path);
								CacheSizeChanged?.Invoke(0);
								COMManip.ResetThumbFor(settings.FilePath!);
							}

							if (File.Exists(frame_fname))
								delete_loop(frame_fname, File.Delete);
						}
						finally
						{
							delete_loop(temp_fname, File.Delete);
						}

					}
				});
			}

			public string? FilePath => settings.FilePath;
			public string ThumbPath => settings.ThumbPath ?? Path.GetFullPath(ungenerated_fname);

			public void Erase()
			{
				lock (this)
				{
					Shutdown();
					COMManip.ResetThumbFor(settings.FilePath!);
					var cache_dir = new DirectoryInfo( settings.GetDir() );
					var sz = cache_dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
					cache_dir.Delete(true);
					CacheSizeChanged?.Invoke(-sz);
				}
			}

			public void Shutdown() => settings.Shutdown();

			//public Bitmap ThumbBitmap {
			//	get {
			//		if (!cached_bitmap.TryGetTarget(out var res))
			//			using (var str = File.OpenRead(ready_thumb_fname ?? ungenerated_fname))
			//			{
			//				res = new Bitmap(str);
			//				cached_bitmap.SetTarget(res);
			//			}
			//		return res;
			//	}
			//}

		}
		private readonly ConcurrentDictionary<string, CachedFileInfo> files = new();
		private readonly ConcurrentDictionary<uint, uint> used_hashes = new();

		public ThumbGenerator(CustomThreadPool thr_pool, string cache_dir)
		{
			this.thr_pool = thr_pool;
			this.cache_dir = Directory.CreateDirectory(cache_dir);
			this.lock_file = File.Create(Path.Combine(this.cache_dir.FullName, ".lock"));

			foreach (var dir in this.cache_dir.EnumerateDirectories())
			{
				var hash = Convert.ToUInt32(dir.Name, 16);
				var cfi = new CachedFileInfo(dir, (hash, dir.Name));
				if (!File.Exists(cfi.FilePath))
				{
					dir.Delete(true);
					continue;
				}
				if (!used_hashes.TryAdd(hash, hash))
					throw new InvalidOperationException(dir.FullName);
				if (!files.TryAdd(cfi.FilePath, cfi))
					throw new InvalidOperationException(dir.FullName);
			}

		}

		public event Action<long>? CacheSizeChanged;
		private void InvokeCacheSizeChanged(long byte_change) =>
			CacheSizeChanged?.Invoke(byte_change);

		private readonly System.Threading.ManualResetEventSlim ev_purge_finished = new(true);

		public void Generate(string fname, Action<string> ret, bool force_regen)
		{
			ev_purge_finished.Wait();

			var cfi = files.GetOrAdd(fname,
				fname => new(fname,
					hash => used_hashes.TryAdd(hash, hash),
					InvokeCacheSizeChanged
				)
			);

			lock (cfi)
			{
				cfi.GenerateThumb(thr_pool.AddJob, ret, force_regen);
				ret(cfi.ThumbPath);
			}

		}

		public void ClearAll()
		{
			ev_purge_finished.Reset();
			while (files.Any())
				try
				{
					var fname = files.Keys.First();
					if (!files.TryRemove(fname, out var cfi))
						continue;
					if (!used_hashes.TryRemove(cfi.Hash.u, out _))
						throw new InvalidOperationException();
					cfi.Erase();
				}
				catch (Exception e)
				{
					Utils.HandleExtension(e);
				}
			ev_purge_finished.Set();
			InvokeCacheSizeChanged(0);
			MessageBox.Show("Done clearing cache!");
		}

		public void Shutdown()
		{
			foreach (var cfi in files.Values)
				cfi.Shutdown();
			lock_file.Close();
			File.Delete(Path.Combine(cache_dir.FullName, ".lock"));
		}

	}
}