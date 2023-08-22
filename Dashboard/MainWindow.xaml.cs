using System;

using System.Linq;
using System.Collections.Generic;

using System.IO;
using System.Text;

using System.Windows;
using System.Windows.Input;

namespace Dashboard
{

	public partial class MainWindow : Window
	{
		public readonly TrayIcon tray_icon;
		public bool shutdown_triggered = false;

		public MainWindow()
		{
			try
			{
				InitializeComponent();

				tray_icon = new(this);

				var (main_thr_pool, pipe) = LogicInit();

				if (shutdown_triggered)
					App.Current!.Shutdown(0);
				if (App.Current!.IsShuttingDown)
					return;

				SetUpJobCount(main_thr_pool);

				LoadThumbGenerator(main_thr_pool, thumb_gen =>
				{
					pipe.AddThumbGen(thumb_gen);

					SetUpCacheInfo(thumb_gen, main_thr_pool);

					SetUpAllowedExt(thumb_gen);

					SetUpThumbCompare(thumb_gen, pipe);

					pipe.StartAccepting();
				});

			}
			catch (LoadCanceledException)
			{
				Environment.Exit(-1);
			}
			catch (Exception e)
			{
				Utils.HandleException(e);
				Environment.Exit(-1);
			}

		}

		private (CustomThreadPool, CommandsPipe) LogicInit()
		{

			CLArgs.Load(this);

			var main_thr_pool = new CustomThreadPool(Environment.ProcessorCount+1);
			/**
			main_thr_pool.AddJob("inf inc", change_subjob =>
			{
				Thread.CurrentThread.IsBackground = true;
				var i = 0;
				while (true)
					change_subjob($"{++i}");
			});
			/**/
			/**
			for (int i = 0; i < 11; i++)
			{
				var id = i;
				void job(Action<string?> change_subjob)
				{
					System.Threading.Thread.CurrentThread.IsBackground = true;
					change_subjob("waiting...");
					System.Threading.Thread.Sleep(1000);
					change_subjob(null);
					main_thr_pool.AddJob($"phoenix job [{id}]", job);
				}
				main_thr_pool.AddJob($"phoenix job [{id}]", job);
			}
			/**/

			var pipe = new CommandsPipe();
			App.Current!.Exit += (o, e) => Utils.HandleException(pipe.Shutdown);

			return (main_thr_pool, pipe);
		}

		private void SetUpJobCount(CustomThreadPool main_thr_pool)
		{
			slider_want_job_count.ValueChanged += (o, e) =>
				Utils.HandleException(() => main_thr_pool.SetJobCount((int)e.NewValue));
			slider_want_job_count.Value = Settings.Root.MaxJobCount;

			slider_active_job_count.Maximum = main_thr_pool.MaxJobCount;
			main_thr_pool.ActiveJobsCountChanged += () =>
				Dispatcher.BeginInvoke(() => Utils.HandleException(() =>
					slider_active_job_count.Value = main_thr_pool.ActiveJobsCount
				));

			main_thr_pool.PendingJobCountChanged += () =>
				Dispatcher.BeginInvoke(() => Utils.HandleException(() =>
					 tb_pending_jobs_count.Text = main_thr_pool.PendingJobCount.ToString()
				));

			b_view_jobs.Click += (o, e) =>
				Utils.HandleException(() => new JobList(main_thr_pool).Show());

			void update_log_count() => Dispatcher.BeginInvoke(() => Utils.HandleException(() =>
			{
				var s = "Log";
				var c = Log.Count;
				if (c != 0)
					s += $" ({c})";
				b_view_log.Content = s;
				b_view_log.IsEnabled = c != 0;
			}));
			Log.CountUpdated += update_log_count;
			update_log_count();
			b_view_log.Click += (o, e) =>
				Utils.HandleException(Log.Show);

		}

		private void LoadThumbGenerator(CustomThreadPool main_thr_pool, Action<ThumbGenerator> on_load)
		{
			main_thr_pool.AddJob("Init ThumbGenerator", change_subjob =>
			{
				var thumb_gen = new ThumbGenerator(main_thr_pool, "cache", change_subjob);
				if (App.Current!.IsShuttingDown) return;

				change_subjob($"Apply ThumbGenerator");
				Dispatcher.Invoke(() =>
				{
					App.Current.Exit += (o, e) => Utils.HandleException(thumb_gen.Shutdown);
					on_load(thumb_gen);
				});
				change_subjob(null);

				Console.Beep();
			});
		}

		private void SetUpCacheInfo(ThumbGenerator thumb_gen, CustomThreadPool main_thr_pool)
		{
			ByteCount cache_size = 0;
			void update_cache_info() =>
				tb_cache_info.Text = $"Cache size: {cache_size}";

			var cache_info_updater = new DelayedUpdater(() =>
			{
				Dispatcher.Invoke(() =>
				{
					cache_size =
						new DirectoryInfo("cache")
						.EnumerateFiles("*", SearchOption.AllDirectories)
						.Sum(f => f.Length);
					update_cache_info();
				});

				if (cache_size > Settings.Root.MaxCacheSize)
				{
					if (thumb_gen.ClearInvalid()!=0) return;
					if (thumb_gen.ClearExtraFiles()!=0) return;
					thumb_gen.ClearOneOldest();
				}

			}, $"cache size recalculation");
			IsVisibleChanged += (o, e) => Utils.HandleException(() =>
			{
				if (!IsVisible) return;
				cache_info_updater.Trigger(TimeSpan.Zero, false);
			});
			thumb_gen.CacheSizeChanged += byte_change => Dispatcher.BeginInvoke(() => Utils.HandleException(()=>
			{
				cache_size += byte_change;
				update_cache_info();
				cache_info_updater.Trigger(TimeSpan.FromSeconds(0.5), true);
			}));
			cache_info_updater.Trigger(TimeSpan.Zero, false);

			b_cache_clear.Click += (o, e) => Utils.HandleException(() => main_thr_pool.AddJob("Clearing cache", thumb_gen.ClearAll));

			b_cache_regen.Click += (o, e) => Utils.HandleException(() => thumb_gen.RegenAll(true));

		}

		private void SetUpAllowedExt(ThumbGenerator thumb_gen)
		{
			AllowedExt.Changed += any_change =>
				b_check_n_commit.IsEnabled = any_change;

			tb_new_ext.PreviewTextInput += (o, e) => Utils.HandleException(() =>
				e.Handled = !FileExtList.Validate(e.Text)
			);
			CommandManager.AddPreviewExecutedHandler(tb_new_ext, (o, e) =>
			{
				if (e.Command != ApplicationCommands.Paste) return;
				if (!Clipboard.ContainsText()) return;
				e.Handled = !FileExtList.Validate(Clipboard.GetText());
			});

			void add_ext()
			{
				if (tb_new_ext.Text=="") return;
				_=new AllowedExt(tb_new_ext.Text, allowed_ext_container);
				tb_new_ext.Clear();
			}
			tb_new_ext.KeyDown += (o, e) => Utils.HandleException(() =>
			{
				if (e.Key == Key.Enter)
					add_ext();
			});
			b_add_ext.Click += (_, _) => Utils.HandleException(add_ext);

			b_check_n_commit.Click += (o, e) => Utils.HandleException(()=>
			{

				var sb = new StringBuilder();
				var (add, rem) = AllowedExt.GetChanges();

				if (add.Any())
				{
					sb.AppendLine($"Added: ");
					foreach (var ext in add)
						sb.AppendLine(ext);
					sb.Append('~', 30);
					sb.AppendLine();
					sb.AppendLine();
				}

				if (rem.Any())
				{
					sb.AppendLine($"Removed: ");
					foreach (var ext in rem)
						sb.AppendLine(ext);
					sb.Append('~', 30);
					sb.AppendLine();
					sb.AppendLine();
				}

				sb.AppendLine($"Commit?");

				if (CustomMessageBox.ShowYesNo("Confirm changes", sb.ToString(), this))
				{
					var gen_type = !add.Any() ? null : CustomMessageBox.Show("What to do with files of added extensions?", "Press Escape to do nothing", "Reset", "Generate");

					var reg_ext_args = new List<string>();
					if (add.Any()) reg_ext_args.Add("add:"+string.Join(';', add));
					if (rem.Any()) reg_ext_args.Add("rem:"+string.Join(';', rem));
					var psi = new System.Diagnostics.ProcessStartInfo(@"RegExtController.exe", string.Join(' ', reg_ext_args))
					{
						UseShellExecute = true,
						Verb = "runas",
						WorkingDirectory = Environment.CurrentDirectory,
						CreateNoWindow = true,
						//UseShellExecute = false,
						//RedirectStandardOutput = true,
						//RedirectStandardError = true,
					};

					var p = System.Diagnostics.Process.Start(psi)!;

					//var err = p.StandardError.ReadToEnd();
					p.WaitForExit();
					if (p.ExitCode != 0)
						//throw new Exception($"ExitCode={p.ExitCode}; err:\n{err}");
						throw new Exception($"ExitCode={p.ExitCode}");
					
					COMManip.NotifyRegExtChange();
					AllowedExt.CommitChanges();

					if (gen_type != null)
						System.Threading.Tasks.Task.Run(() => Utils.HandleException(() =>
						{
							var only_reset = gen_type switch
							{
								"Reset" => true,
								"Generate" => false,
								_ => throw new NotImplementedException(gen_type),
							};
							var q = new ESQuary("ext:"+string.Join(';',add));
							if (only_reset)
							{
								foreach (var fname in q)
									COMManip.ResetThumbFor(fname);
							}
							else
								thumb_gen.MassGenerate(q, true);
						}));
				}
				else
				{
					foreach (var ext in rem)
						_=new AllowedExt(ext, allowed_ext_container);
					foreach (var ext in add)
						allowed_ext_container.Children.Cast<AllowedExt>().Single(ae => ae.tb_name.Text == ext).Delete(allowed_ext_container);
				}
			});

			foreach (var ext in Settings.Root.AllowedExts)
				_=new AllowedExt(ext, allowed_ext_container);
		}

		private void SetUpThumbCompare(ThumbGenerator thumb_gen, CommandsPipe pipe)
		{
			var thumb_compare_all = new[] { thumb_compare_org, thumb_compare_gen };

			Action<double>? vid_timestamp_handler = null;
			slider_vid_timestamp.ValueChanged += (_, _) =>
				vid_timestamp_handler?.Invoke(slider_vid_timestamp.Value);

			void set_pregen_progress(double progress)
			{
				if (progress>1) progress = 1;
				if (progress<0) progress = 0;
				Dispatcher.BeginInvoke(() => Utils.HandleException(() =>
					pb_vid_pregen.Value = progress
				));
			}

			Action? next_thumb_compare_update = null;
			var thump_compare_updater = new DelayedUpdater(
				() => next_thumb_compare_update?.Invoke(),
				"thumb compare update"
			);

			int current_compare_id = 0;
			string? curr_compare_fname = null;
			ThumbGenerator.ICachedFileInfo.CacheUse? last_cache_use = null;
			void clear_thumb_compare_file()
			{
				foreach (var tcv in thumb_compare_all)
					tcv.Reset();
				grid_thumb_compare.HorizontalAlignment = HorizontalAlignment.Stretch;
				c_thumb_compare_1.VerticalAlignment = VerticalAlignment.Stretch;
				c_thumb_compare_2.VerticalAlignment = VerticalAlignment.Stretch;
				b_reload_compare.IsEnabled = false;
				sp_gen_controls.Visibility = Visibility.Hidden;

				++current_compare_id;
				curr_compare_fname = null;
				System.Threading.Interlocked.Exchange(ref last_cache_use, null)?.Dispose();
			}
			void begin_thumb_compare(string fname) => Utils.HandleException(() =>
			{
				clear_thumb_compare_file();
				// Increament in the clear_thumb_compare_file
				var compare_id = current_compare_id;

				thumb_compare_org.Set(COMManip.GetExistingThumbFor(fname));
				using var cfi_use = thumb_gen.Generate(fname, nameof(begin_thumb_compare), ()=>false, cfi =>
				{
					bool is_outdated() => compare_id != current_compare_id;
					if (is_outdated()) return;
					var new_cache_use = cfi.BeginUse("continuous thumb compare", is_outdated);
					Dispatcher.BeginInvoke(() => Utils.HandleException(() =>
					{
						System.Threading.Interlocked.Exchange(ref last_cache_use, new_cache_use)?.Dispose();
						if (is_outdated()) return;

						thumb_compare_gen.Set(cfi.CurrentThumbBmp);

						var sources = cfi.ThumbSources;
						if (sources.Count==1 && sources[0].Length==TimeSpan.Zero) return;
						sp_gen_controls.Visibility = Visibility.Visible;

						var bts = new System.Windows.Controls.Button[sources.Count];
						sp_vid_stream_buttons.Children.Clear();
						for (var i = 0; i<sources.Count; i++)
						{
							bts[i] = new()
							{
								Margin = new Thickness(0, 0, 5, 0),
								Content = sources[i].Name,
							};
							{
								var ind = i;
								bts[i].Click += (_, _) =>
								{
									next_thumb_compare_update = () => select_source(ind);
									thump_compare_updater.Trigger(TimeSpan.Zero, false);
								};
							}
							sp_vid_stream_buttons.Children.Add(bts[i]);
						}

						void select_source(int new_ind)
						{
							vid_timestamp_handler = pos =>
							{
								tb_vid_timestamp.Text = (slider_vid_timestamp.Value * sources[new_ind].Length).ToString();
								next_thumb_compare_update = () =>
								{
									cfi.ApplySourceAt(false, _ => { }, new_ind, pos, out _, set_pregen_progress);
									// invoke sync in thump_compare_updater thread
									Dispatcher.Invoke(() =>
										thumb_compare_gen.Set(cfi.CurrentThumbBmp)
									);
								};
								thump_compare_updater.Trigger(TimeSpan.Zero, false);
							};
							var old_ind = cfi.ChosenThumbOptionInd;
							cfi.ApplySourceAt(false, _ => { }, new_ind, null, out var initial_pos, set_pregen_progress);
							// invoke sync in thump_compare_updater thread
							Dispatcher.Invoke(() => Utils.HandleException(() =>
							{
								slider_vid_timestamp.Value = initial_pos;
								thumb_compare_gen.Set(cfi.CurrentThumbBmp);
								tb_vid_timestamp.Visibility = pb_vid_pregen.Visibility = slider_vid_timestamp.Visibility =
									sources[new_ind].Length != TimeSpan.Zero ? Visibility.Visible : Visibility.Hidden;
								bts[old_ind].IsEnabled = true;
								bts[new_ind].IsEnabled = false;
							}));
						}
						select_source(cfi.ChosenThumbOptionInd);

					}));
				}, true);
				if (cfi_use is null) return;
				var cfi = cfi_use.CFI;

				thumb_compare_gen.Set(cfi.CurrentThumbBmp);
				grid_thumb_compare.HorizontalAlignment = HorizontalAlignment.Center;
				c_thumb_compare_1.VerticalAlignment = VerticalAlignment.Bottom;
				c_thumb_compare_2.VerticalAlignment = VerticalAlignment.Top;
				Settings.Root.LastComparedFile = fname;
				curr_compare_fname = fname;
				b_reload_compare.IsEnabled = true;
			});
			string[] extract_file_lst(IEnumerable<string> inp)
			{
				static T execute_any<T>(string descr, params Func<Action, T>[] funcs)
				{
					var wh = new System.Threading.ManualResetEventSlim(false);
					var excs = new List<Exception>();
					var left = funcs.Length;

					T? res = default;
					bool res_set = false;

					for (int i=0; i<funcs.Length; ++i)
					{
						var f = funcs[i];
						new System.Threading.Thread(()=>
						{
							try
							{
								res = f(() => {
									if (wh.IsSet)
										throw new System.Threading.Tasks.TaskCanceledException();
								});
								res_set = true;
								wh.Set();
							}
							catch (Exception e)
							{
								using var excs_locker = new ObjectLocker(excs);
								excs.Add(e);
								left -= 1;
								if (left == 0) wh.Set();
							}
						})
						{
							IsBackground = true,
							Name = $"{descr} #{i}",
						}.Start();
					}

					wh.Wait();
					if (!res_set)
						throw new AggregateException(excs);

					return res!;
				}

				var exts = Settings.Root.AllowedExts;
				var es_arg = "ext:" + string.Join(';', exts);

				return inp.AsParallel().SelectMany(path =>
				{
					if (File.Exists(path))
						return Enumerable.Repeat(path, exts.MatchesFile(path)?1:0);
					if (Directory.Exists(path))
						return execute_any($"get files in [{path}]",
							try_cancel =>
							{
								var res = new List<string>();
								void on_dir(string dir)
								{
									foreach (var subdir in Directory.EnumerateDirectories(dir))
										on_dir(subdir);
									foreach (var fname in Directory.EnumerateFiles(dir))
									{
										try_cancel();
										if (!exts.MatchesFile(fname)) continue;
										res.Add(fname);
									}
								}
								on_dir(path);
								return res.AsEnumerable();
							},
							try_cancel =>
							{
								var res = new List<string>();
								foreach (var fname in new ESQuary(path, es_arg))
								{
									try_cancel();
									res.Add(fname);
								}
								return res.AsEnumerable();
							}
						);
					return Enumerable.Empty<string>();
				}).ToArray();
			}
			void apply_file_lst(string[] lst)
			{
				if (lst.Length==1)
				{
					tray_icon.ShowWin();
					begin_thumb_compare(lst.Single());
					return;
				}

				System.Threading.Tasks.Task.Run(() => Utils.HandleException(() =>
					thumb_gen.MassGenerate(lst, true)
				));

			}
			pipe.AddLoadCompareHandler(inp =>
			{
				var lst = extract_file_lst(inp);
				Dispatcher.BeginInvoke(() => Utils.HandleException(() => apply_file_lst(lst)));
			});

			foreach (var tcv in thumb_compare_all)
				tcv.MouseDown += (o, e) => Utils.HandleException(() =>
				{
					if (e.ChangedButton == MouseButton.Left)
					{
						new FileChooser(inp => apply_file_lst(extract_file_lst(inp))).ShowDialog();
						e.Handled = true;
					}
					else if (e.ChangedButton == MouseButton.Right)
					{
						clear_thumb_compare_file();
						e.Handled = true;
					}
					else if (e.ChangedButton == MouseButton.Middle)
					{
						if (curr_compare_fname != null)
							System.Diagnostics.Process.Start("explorer", $"/select,\"{curr_compare_fname}\"");
						e.Handled = true;
					}
				});

			b_swap_compare.Click += (o, e) => Utils.HandleException(() =>
			{

				var t = (c_thumb_compare_2.Child, c_thumb_compare_1.Child);
				(c_thumb_compare_1.Child, c_thumb_compare_2.Child) = (null, null);
				(c_thumb_compare_1.Child, c_thumb_compare_2.Child) = t;

				(tb_thumb_compare_1.Text, tb_thumb_compare_2.Text) = (tb_thumb_compare_2.Text, tb_thumb_compare_1.Text);

			});

			b_reload_compare.Click += (o, e) => Utils.HandleException(() =>
			{
				if (curr_compare_fname is null) return;
				begin_thumb_compare(curr_compare_fname);
			});

			(string[] inp, string[] lst)? drag_cache = null;
			void drag_handler(object o, DragEventArgs e)
			{
				e.Handled = true;
				e.Effects = DragDropEffects.None;

				var inp = (string[])e.Data.GetData(DataFormats.FileDrop);
				if (inp is null) return;

				var lst = (drag_cache.HasValue && inp.SequenceEqual(drag_cache.Value.inp) ? drag_cache :
					(drag_cache = (inp, extract_file_lst(inp)))
				).Value.lst;
				if (lst.Length==0) return;

				e.Effects = lst.Length==1 ?
					DragDropEffects.Link :
					DragDropEffects.Move;

			}
			grid_thumb_compare.DragEnter += drag_handler;
			grid_thumb_compare.DragOver += drag_handler;

			grid_thumb_compare.Drop += (o, e) => Utils.HandleException(() =>
			{
				var lst = drag_cache!.Value.lst;
				apply_file_lst(lst);
				e.Handled = true;
			});

			Closing += (o, e) => Utils.HandleException(clear_thumb_compare_file);
			grid_thumb_compare.AllowDrop = true;
		}

	}

}
