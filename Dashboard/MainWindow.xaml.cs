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
				{
					App.Current.Shutdown(0);
					return;
				}

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

				change_subjob($"Apply ThumbGenerator");
				Dispatcher.Invoke(()=>on_load(thumb_gen));

				Console.Beep();
			});
		}

		private void SetUpCacheInfo(ThumbGenerator thumb_gen, CustomThreadPool main_thr_pool)
		{
			long cache_byte_count = 0;
			var byte_scales = new[] { "KB", "MB", "GB" };
			void update_cache_info()
			{
				var c = (double)cache_byte_count;

				string chosen_byte_scale = "B";
				foreach (var byte_scale in byte_scales)
				{
					if (c < 5000) break;
					c /= 1024;
					chosen_byte_scale = byte_scale;
				}

				c = Math.Round(c, 2);
				tb_cache_info.Text = $"Cache size: {c} {chosen_byte_scale}";
			}
			var cache_info_updater = new DelayedUpdater(() => Dispatcher.Invoke(() =>
			{
				cache_byte_count =
					new DirectoryInfo("cache")
					.EnumerateFiles("*", SearchOption.AllDirectories)
					.Sum(f => f.Length);
				update_cache_info();
			}), $"cache size recalculation");
			IsVisibleChanged += (o, e) =>
			{
				if (!IsVisible) return;
				cache_info_updater.Trigger(TimeSpan.Zero, false);
			};
			thumb_gen.CacheSizeChanged += byte_change => Dispatcher.Invoke(() =>
			{
				cache_byte_count += byte_change;
				update_cache_info();
				cache_info_updater.Trigger(TimeSpan.FromSeconds(0.1), true);
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
			}
			void begin_thumb_compare(string fname) => Utils.HandleExtension(() =>
			{
				clear_thumb_compare_file();
				var compare_id = ++current_compare_id;

				thumb_compare_org.Set(COMManip.GetExistingThumbFor(fname));
				thumb_gen.Generate(fname, thumb_fname => Dispatcher.InvokeAsync(() => Utils.HandleExtension(() =>
				{
					if (compare_id != current_compare_id) return;
					thumb_compare_gen.Set(Utils.LoadUncachedBitmap(thumb_fname));
				})), true);
				grid_thumb_compare.HorizontalAlignment = HorizontalAlignment.Center;
				c_thumb_compare_1.VerticalAlignment = VerticalAlignment.Bottom;
				c_thumb_compare_2.VerticalAlignment = VerticalAlignment.Top;
				Settings.Root.LastComparedFile = fname;
				curr_compare_fname = fname;
			});
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
					thumb_gen.Generate(fname, _ => { }, true);

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
				e.Handled = true;

				if (lst.Length==1)
				{
					begin_thumb_compare(lst.Single());
					return;
				}

				foreach (var fname in lst)
					thumb_gen.Generate(fname, _ => { }, true);

			};

			Closing += (o, e) => clear_thumb_compare_file();
		}

	}

}
