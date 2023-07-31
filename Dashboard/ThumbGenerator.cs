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

			public CachedFileInfo((uint u, string s) hash)
			{
				this.hash = hash;
				settings = new(hash.s);
			}

			private static readonly string ungenerated_fname	= Path.GetFullPath(@"Dashboard-Default.Ungenerated.bmp");
			private static readonly string waiting_fname		= Path.GetFullPath(@"Dashboard-Default.Waiting.bmp");
			//private static readonly string locked_fname			= Path.GetFullPath(@"Dashboard-Default.Locked.bmp");
			private static readonly string sound_only_fname		= Path.GetFullPath(@"Dashboard-Default.SoundOnly.bmp");

			private sealed class GenerationState : IDisposable
			{
				private readonly CachedFileInfo cfi;

				public GenerationState(CachedFileInfo cfi)
				{
					this.cfi = cfi;
					System.Threading.Monitor.Enter(cfi);
				}

				public sealed class GenerationTemp : IDisposable
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
						on_unload(path);
						//for (int i = 1; ; ++i)
						//	try
						//	{
						//		on_unload(path);
						//		break;
						//	}
						//	catch (Exception e)
						//	{
						//		if (i%100 == 0)
						//			CustomMessageBox.Show("Struggling to delete [{path}]", e.ToString());
						//		System.Threading.Thread.Sleep(10);
						//	}
						dispose_disabled = true;
					}

				}

				private readonly System.Collections.Generic.List<GenerationTemp> temps = new();
				private GenerationTemp Add(GenerationTemp temp)
				{
					// delete last gen leftovers
					temp.Dispose();
					temps.Add(temp);
					return temp;
				}
				public GenerationTemp AddFile(string fname) => Add(new GenerationTemp(Path.Combine(cfi.settings.GetDir(), fname),
					fname =>
					{
						if (!File.Exists(fname)) return;
						File.Delete(fname);
					}
				));
				public GenerationTemp AddDir(string path) => Add(new GenerationTemp(Path.Combine(cfi.settings.GetDir(), path),
					path =>
					{
						if (!Directory.Exists(path)) return;
						Directory.Delete(path, true);
					}
				));

				public void Dispose()
				{
					try
					{
						foreach (var temp in temps)
							temp.Dispose();
						System.Threading.Monitor.Exit(cfi);
					}
					catch (Exception e)
					{
						Utils.HandleExtension(e);
						// unrecoverable, but also unimaginable
						Console.Beep(); Console.Beep(); Console.Beep();
						App.Current.Shutdown();
					}
				}

			}

			public void GenerateThumb(Action<CustomThreadPool.JobWork> add_job, Action<string> ret, bool force_regen)
			{
				var write_time = new[]{
					File.GetLastWriteTimeUtc(FilePath!),
					File.GetLastAccessTimeUtc(FilePath!),
				}.Min();
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
						settings.ThumbPath = waiting_fname;
						System.Threading.Tasks.Task.Delay(total_wait-waited)
							.ContinueWith(t => Utils.HandleExtension(
								() => GenerateThumb(add_job, ret, true)
							));
						return;
					}
				}

				add_job(change_subjob =>
				{
					var sw = System.Diagnostics.Stopwatch.StartNew();

					if (settings.ThumbPath != null) return;
					using var state = new GenerationState(this);
					if (settings.ThumbPath != null) return;

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
							CustomMessageBox.Show(
								$"[{p.StartInfo.FileName} {p.StartInfo.Arguments}] hanged. Output:",
								otp.ToString() + "\n\n===================\n\n" + res.Result
							);
						}));

						return res;
					}

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
					var temp_fname = FilePath!;

					change_subjob("get metadata");
					var metadata = use_ffmpeg($"-i \"{temp_fname}\" -f ffmetadata").Result;
					change_subjob(null);

					change_subjob("parse metadata");
					const string NA_dur_str = @"N/A";
					bool had_invalid_data = metadata.Contains(temp_fname+@": Invalid data found when processing input");

					TimeSpan? full_dur = null;
					foreach (var m in KnownRegexes.MetadataDuration().Matches(metadata).Cast<System.Text.RegularExpressions.Match>())
					{
						if (full_dur != null)
							CustomMessageBox.Show($"Multiple full dur in {FilePath}", metadata);

						var dur_gr = m.Groups[1].Value;
						full_dur = dur_gr == NA_dur_str ? null : TimeSpan.Parse(dur_gr);

						if (had_invalid_data && full_dur != null)
							CustomMessageBox.Show($"{FilePath}: dur found despite wrong input", metadata);

					}

					bool has_vid = false;
					TimeSpan? vid_dur = null;
					foreach (var m in KnownRegexes.MetadataVideoStreamHead().Matches(metadata).Cast<System.Text.RegularExpressions.Match>())
					{
						if (has_vid)
							CustomMessageBox.Show($"Multiple video streams in {FilePath}", metadata);
						has_vid = true;

						vid_dur = KnownRegexes.MetadataVideoDuration()
							.Matches(metadata, m.Index+m.Length)
							.Select(m => (TimeSpan?)TimeSpan.Parse(m.Groups[1].Value))
							.DefaultIfEmpty(full_dur).First();

						if (had_invalid_data && vid_dur != null)
							CustomMessageBox.Show($"{FilePath}: dur found despite wrong input", metadata);

						break; //TODO some files have their thumb as a secondary video stream, instead of attachment
					}
					// can be N/A in webm, when it's dynamic
					//if (has_vid && full_dur is null)
					//	CustomMessageBox.Show($"{FilePath}: cannot find full dur", metadata);
					if (has_vid && vid_dur is null)
						CustomMessageBox.Show($"{FilePath}: cannot find vid dur", metadata);
					change_subjob(null);

					change_subjob("get attachments");
					var attachments_dir = state.AddDir("attachments").Path;
					Directory.CreateDirectory(attachments_dir);
					use_ffmpeg($"-dump_attachment:t \"\" -i \"{temp_fname}\"", attachments_dir).Wait();
					change_subjob(null);

					change_subjob("get embmeded images");
					var valid_embeds_temp = state.AddDir("valid_embeds");
					var valid_embeds_dir = valid_embeds_temp.Path;
					Directory.CreateDirectory(valid_embeds_dir);
					var valid_embeds =
						Directory.EnumerateFiles(attachments_dir).Select(inp =>
						{
							var otp = Path.Combine(valid_embeds_dir, Path.GetFileNameWithoutExtension(inp)+".png");
							if (File.Exists(otp)) throw new NotImplementedException(otp);
							return (inp, otp, t: use_ffmpeg($"-i \"{inp}\" -vf scale=256:256:force_original_aspect_ratio=decrease \"{otp}\""));
						}).ToArray()
						.Where(r =>
						{
							var ffmpeg_res = r.t.Result;

							var vid_stream_found = KnownRegexes.MetadataVideoStreamHead().IsMatch(ffmpeg_res);
							var otp_created = File.Exists(r.otp);
							if (vid_stream_found != otp_created)
								throw new NotImplementedException(r.inp);

							return otp_created;
						}).Select(r => r.otp).ToArray();
					if (valid_embeds.Length > 1)
						valid_embeds_temp.DisableDispose();
					change_subjob(null);

					var frame_fname = state.AddFile("frame.png").Path;
					string bg_file;
					//TODO embed chooser
					if (valid_embeds.Length != 0)
						bg_file = valid_embeds[0];
					else if (!has_vid || vid_dur is null)
						bg_file = sound_only_fname;
					else
					{
						change_subjob("get frame");
						var frame_at = vid_dur.Value * 0.3;
						var ffmpeg_arg_seek = "";
						if (frame_at.TotalSeconds>=1)
							ffmpeg_arg_seek = $" -ss {Math.Truncate(frame_at.TotalSeconds)}";
						var ffmpeg_arg = $"-skip_frame nokey{ffmpeg_arg_seek} -i \"{temp_fname}\" -vframes 1 -vf scale=256:256:force_original_aspect_ratio=decrease \"{frame_fname}\"";
						string ffmpeg_res;
						while (true)
						{
							ffmpeg_res = use_ffmpeg(ffmpeg_arg).Result;
							if (!ffmpeg_res.Contains("File ended prematurely")) break;
							if (File.Exists(frame_fname))
								throw new InvalidOperationException(frame_fname+"\n\n\n"+ffmpeg_res);
							frame_at /= 2;
							if (frame_at != default) continue;
							frame_fname = sound_only_fname;
							break;
						}
						if (!File.Exists(frame_fname))
						{
							CustomMessageBox.Show(FilePath!, $"> {ffmpeg_arg}\n\n"+ffmpeg_res);
							return;
						}
						bg_file = frame_fname;
						change_subjob(null);
					}

					change_subjob("load bg image");
					Size sz;
					var bg_im = new Image();
					try
					{
						var bg_im_source = Utils.LoadUncachedBitmap(bg_file);
						sz = new(bg_im_source.Width, bg_im_source.Height);
						bg_im.Source = bg_im_source;
					}
					catch (System.Runtime.InteropServices.COMException e)
					{
						//TODO bg_file is null or "" sometimes?????
						CustomMessageBox.Show($"File: [{settings.FilePath}] Image: [{bg_file??"<null>"}]", e.ToString());
					}
					change_subjob(null);

					change_subjob("make dur string");
					var dur_s = "";
					if (full_dur != null)
					{
						if (dur_s!="" || full_dur.Value.TotalHours>=1)
							dur_s += Math.Truncate(full_dur.Value.TotalHours).ToString() + ':';
						if (dur_s!="" || full_dur.Value.Minutes!=0)
							dur_s += full_dur.Value.Minutes.ToString("00") + ':';
						if (dur_s!="" || full_dur.Value.Seconds!=0)
							dur_s += full_dur.Value.Seconds.ToString("00");
						if (dur_s.Length<5)
						{
							var s = full_dur.Value.TotalSeconds;
							s -= Math.Truncate(s);
							dur_s += '('+s.ToString("N"+(5-dur_s.Length))[2..].TrimEnd('0')+')';
						}
					}
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
					change_subjob(null);

					change_subjob("render output");
					var bitmap = new RenderTargetBitmap((int)sz.Width, (int)sz.Height, 96, 96, PixelFormats.Pbgra32);
					res_c.Measure(sz);
					res_c.Arrange(new(sz));
					bitmap.Render(res_c);
					change_subjob(null);

					var res_temp = state.AddFile("thumb.png");
					var res_path = res_temp.Path;

					change_subjob("save output");
					var enc = new PngBitmapEncoder();
					enc.Frames.Add(BitmapFrame.Create(bitmap));
					using (Stream fs = File.Create(res_path))
						enc.Save(fs);
					change_subjob(null);

					change_subjob("pass output out");
					// Make sure this is not mixed with .Generate call
					//lock (this)
					// Instead, this whole job is locked
					{
						settings.LastRecalcTime = sw.Elapsed.ToString();
						res_temp.DisableDispose();
						settings.ThumbPath = res_path;
						settings.LastUpdate = write_time;
						ret(res_path);
						CacheSizeChanged?.Invoke(0);
						COMManip.ResetThumbFor(settings.FilePath!);
					}

				});
			}

			public string? FilePath => settings.FilePath;
			public string ThumbPath => settings.ThumbPath ?? ungenerated_fname;

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

		public ThumbGenerator(CustomThreadPool thr_pool, string cache_dir, Action<string?> change_subjob)
		{
			this.thr_pool = thr_pool;
			this.cache_dir = Directory.CreateDirectory(cache_dir);
			this.lock_file = File.Create(Path.Combine(this.cache_dir.FullName, ".lock"));

			var dirs = this.cache_dir.GetDirectories();
			for (var i = 0; i < dirs.Length; ++i)
			{
				change_subjob($"Loaded {i}/{dirs.Length}");
				var dir = dirs[i];
				var hash = Convert.ToUInt32(dir.Name, 16);
				var cfi = new CachedFileInfo((hash, dir.Name));
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
				cfi.GenerateThumb(w=>thr_pool.AddJob($"Generating thumb for: {fname}", w), ret, force_regen);
				ret(cfi.ThumbPath);
			}

		}

		public void ClearAll(Action<string?> change_subjob)
		{
			ev_purge_finished.Reset();
			while (files.Any())
				try
				{
					change_subjob($"left: {files.Count}");
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
			App.Current.Dispatcher.Invoke(() =>
				CustomMessageBox.Show("Done clearing cache!", App.Current.MainWindow)
			);
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