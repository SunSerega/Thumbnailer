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
					App.Current.Shutdown(0);
				if (App.Current.IsShuttingDown)
					return;

				SetUpJobCount(main_thr_pool);

				SetUpAllowedExt();

				LoadThumbGenerator(main_thr_pool, thumb_gen =>
				{
					App.Current.Exit += (o, e) => thumb_gen.Shutdown();

					pipe.AddThumbGen(thumb_gen);
					pipe.StartAccepting();

					SetUpCacheInfo(thumb_gen, main_thr_pool);

					SetUpThumbCompare(thumb_gen);

				});

			}
			catch (LoadCanceledException)
			{
				Environment.Exit(-1);
			}
			catch (Exception e)
			{
				Utils.HandleExtension(e);
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
			App.Current.Exit += (o, e) => pipe.Shutdown();

			return (main_thr_pool, pipe);
		}

		private void SetUpJobCount(CustomThreadPool main_thr_pool)
		{
			slider_want_job_count.ValueChanged += (o, e) =>
				main_thr_pool.SetJobCount((int)e.NewValue);
			slider_want_job_count.Value = Settings.Root.MaxJobCount;

			slider_active_job_count.Maximum = main_thr_pool.MaxJobCount;
			main_thr_pool.ActiveJobsCountChanged += () =>
				Dispatcher.InvokeAsync(() => Utils.HandleExtension(() =>
					slider_active_job_count.Value = main_thr_pool.ActiveJobsCount
				));

			main_thr_pool.PendingJobCountChanged += () =>
				Dispatcher.InvokeAsync(() => Utils.HandleExtension(() =>
					 tb_pending_jobs_count.Text = main_thr_pool.PendingJobCount.ToString()
				));

			b_view_jobs.Click += (o, e) =>
				new JobList(main_thr_pool).Show();
		}

		private void SetUpAllowedExt()
		{
			AllowedExt.Changed += any_change =>
				b_check_n_commit.IsEnabled = any_change;

			tb_new_ext.PreviewTextInput += (o, e) =>
				e.Handled = !FileExtList.Validate(e.Text);
			CommandManager.AddPreviewExecutedHandler(tb_new_ext, (o, e) =>
			{
				if (e.Command != ApplicationCommands.Paste) return;
				if (!Clipboard.ContainsText()) return;
				e.Handled = !FileExtList.Validate(Clipboard.GetText());
			});

			b_add_ext.Click += (o, e) =>
			{
				_=new AllowedExt(tb_new_ext.Text, allowed_ext_container);
				tb_new_ext.Clear();
			};

			b_check_n_commit.Click += (o, e) =>
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
					var reg_ext_args = new System.Collections.Generic.List<string>();
					if (add.Any()) reg_ext_args.Add("add:"+string.Join(';', add));
					if (rem.Any()) reg_ext_args.Add("rem:"+string.Join(';', rem));
					var psi = new System.Diagnostics.ProcessStartInfo(@"RegExtController.exe", string.Join(' ', reg_ext_args))
					{
						CreateNoWindow = true,
						UseShellExecute = false,
						//RedirectStandardOutput = true,
						RedirectStandardError = true,
					};

					var p = System.Diagnostics.Process.Start(psi)!;

					var err = p.StandardError.ReadToEnd();
					if (p.ExitCode != 0)
						throw new Exception($"ExitCode={p.ExitCode}; err:\n{err}");

					AllowedExt.CommitChanges();
				}
				else
				{
					foreach (var ext in rem)
						_=new AllowedExt(ext, allowed_ext_container);
					foreach (var ext in add)
						allowed_ext_container.Children.Cast<AllowedExt>().Single(ae => ae.tb_name.Text == ext).Delete(allowed_ext_container);
				}
			};

			foreach (var ext in Settings.Root.AllowedExts)
				_=new AllowedExt(ext, allowed_ext_container);
		}

		private void LoadThumbGenerator(CustomThreadPool main_thr_pool, Action<ThumbGenerator> on_load)
		{
			main_thr_pool.AddJob("Init ThumbGenerator", change_subjob =>
			{
				var thumb_gen = new ThumbGenerator(main_thr_pool, "cache", change_subjob);
				if (App.Current.IsShuttingDown) return;

				change_subjob($"Apply ThumbGenerator");
				Dispatcher.Invoke(()=>on_load(thumb_gen));

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
					if (thumb_gen.ClearInvalid()!=0)
						return;
					thumb_gen.ClearOne();
				}

			}, $"cache size recalculation");
			IsVisibleChanged += (o, e) =>
			{
				if (!IsVisible) return;
				cache_info_updater.Trigger(TimeSpan.Zero, false);
			};
			thumb_gen.CacheSizeChanged += byte_change => Dispatcher.Invoke(() =>
			{
				cache_size += byte_change;
				update_cache_info();
				cache_info_updater.Trigger(TimeSpan.FromSeconds(0.5), true);
			});
			cache_info_updater.Trigger(TimeSpan.Zero, false);

			b_cache_clear.Click += (o, e) => main_thr_pool.AddJob("Clearing cache", thumb_gen.ClearAll);

			b_cache_rebuild.Click += (o, e) =>
			{
				throw new NotImplementedException();
			};
		}

		private void SetUpThumbCompare(ThumbGenerator thumb_gen)
		{
			var thumb_compare_all = new[] { thumb_compare_org, thumb_compare_gen };

			Action<double>? vid_timestamp_handler = null;
			slider_vid_timestamp.ValueChanged += (_, _) =>
				vid_timestamp_handler?.Invoke(slider_vid_timestamp.Value);

			Action? next_thumb_compare_update = null;
			var thump_compare_updater = new DelayedUpdater(
				() => next_thumb_compare_update?.Invoke(),
				"thumb compare update"
			);

			int current_compare_id = 0;
			string? curr_compare_fname = null;
			void clear_thumb_compare_file()
			{
				foreach (var tcv in thumb_compare_all)
					tcv.Reset();
				grid_thumb_compare.HorizontalAlignment = HorizontalAlignment.Stretch;
				c_thumb_compare_1.VerticalAlignment = VerticalAlignment.Stretch;
				c_thumb_compare_2.VerticalAlignment = VerticalAlignment.Stretch;
				curr_compare_fname = null;
				b_reload_compare.IsEnabled = false;
				sp_gen_controls.Visibility = Visibility.Hidden;
			}
			void begin_thumb_compare(string fname) => Utils.HandleExtension(() =>
			{
				clear_thumb_compare_file();
				var compare_id = ++current_compare_id;

				thumb_compare_org.Set(COMManip.GetExistingThumbFor(fname));
				var cfi = thumb_gen.Generate(fname, cfi => Dispatcher.InvokeAsync(() => Utils.HandleExtension(() =>
				{
					if (compare_id != current_compare_id) return;
					thumb_compare_gen.Set(cfi.CurrentThumbBmp);

					var sources = cfi.ThumbSources;
					if (sources.Count==1 && sources[0].Length==TimeSpan.Zero) return;
					sp_gen_controls.Visibility = Visibility.Visible;

					var bts = new System.Windows.Controls.Button[sources.Count];
					sp_vid_stream_buttons.Children.Clear();
					for (var i=0; i<sources.Count; i++)
					{
						bts[i] = new()
						{
							Margin = new Thickness(0,0,5,0),
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
								cfi.ApplySourceAt(false, _ => { }, new_ind, pos, out _);
								Dispatcher.Invoke(() =>
									thumb_compare_gen.Set(cfi.CurrentThumbBmp)
								);
							};
							thump_compare_updater.Trigger(TimeSpan.Zero, false);
						};
						var old_ind = cfi.ChosenThumbOptionInd;
						cfi.ApplySourceAt(false, _ => { }, new_ind, null, out var initial_pos);
						Dispatcher.Invoke(() =>
						{
							//slider_vid_timestamp.Value = -1;
							slider_vid_timestamp.Value = initial_pos;
							thumb_compare_gen.Set(cfi.CurrentThumbBmp);
							tb_vid_timestamp.Visibility = slider_vid_timestamp.Visibility =
								sources[new_ind].Length != TimeSpan.Zero ? Visibility.Visible : Visibility.Hidden;
							bts[old_ind].IsEnabled = true;
							bts[new_ind].IsEnabled = false;
						});
					}
					select_source(cfi.ChosenThumbOptionInd);

					//next_thumb_compare_update = () =>
					//{
					//	select_source(cfi.ChosenThumbOptionInd);
					//	foreach (var b in bts)
					//		sp_vid_stream_buttons.Children.Add(b);
					//};
					//thump_compare_updater.Trigger(TimeSpan.Zero, false);

				})), true);
				thumb_compare_gen.Set(cfi.CurrentThumbBmp);
				grid_thumb_compare.HorizontalAlignment = HorizontalAlignment.Center;
				c_thumb_compare_1.VerticalAlignment = VerticalAlignment.Bottom;
				c_thumb_compare_2.VerticalAlignment = VerticalAlignment.Top;
				Settings.Root.LastComparedFile = fname;
				curr_compare_fname = fname;
				b_reload_compare.IsEnabled = true;
			});
			//TODO Use everything search
			string[] extract_file_lst(IEnumerable<string> inp) =>
				inp.SelectMany(path =>
				{
					if (File.Exists(path))
						return Enumerable.Repeat(path, 1);
					if (Directory.Exists(path))
						return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories);
					return Enumerable.Empty<string>();
				}).Where(Settings.Root.AllowedExts.MatchesFile).ToArray();
			void apply_file_lst(string[] lst)
			{
				if (lst.Length==1)
				{
					begin_thumb_compare(lst.Single());
					return;
				}

				foreach (var fname in lst)
					_ = thumb_gen.Generate(fname, _ => { }, true);

			}

			foreach (var tcv in thumb_compare_all)
				tcv.MouseDown += (o, e) =>
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
				};

			b_swap_compare.Click += (o, e) =>
			{

				var t = (c_thumb_compare_2.Child, c_thumb_compare_1.Child);
				(c_thumb_compare_1.Child, c_thumb_compare_2.Child) = (null, null);
				(c_thumb_compare_1.Child, c_thumb_compare_2.Child) = t;

				(tb_thumb_compare_1.Text, tb_thumb_compare_2.Text) = (tb_thumb_compare_2.Text, tb_thumb_compare_1.Text);

			};

			b_reload_compare.Click += (o, e) =>
			{
				if (curr_compare_fname is null) return;
				begin_thumb_compare(curr_compare_fname);
			};

			(string[] inp, string[] lst)? drag_cache = null;
			void drag_handler(object o, DragEventArgs e)
			{
				e.Handled = true;
				e.Effects = DragDropEffects.None;

				var inp = (string[])e.Data.GetData(DataFormats.FileDrop);
				if (inp is null) return;

				var lst = (inp==drag_cache?.inp ? drag_cache :
					(drag_cache = (inp, extract_file_lst(inp)))
				).Value.lst;
				if (lst.Length==0) return;

				e.Effects = lst.Length==1 ?
					DragDropEffects.Link :
					DragDropEffects.Move;

			}
			grid_thumb_compare.DragEnter += drag_handler;
			grid_thumb_compare.DragOver += drag_handler;

			grid_thumb_compare.Drop += (o, e) =>
			{
				var lst = drag_cache!.Value.lst;
				apply_file_lst(lst);
				e.Handled = true;
			};

			Closing += (o, e) => clear_thumb_compare_file();
			grid_thumb_compare.AllowDrop = true;
		}

	}

}
