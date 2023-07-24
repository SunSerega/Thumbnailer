using System;

using System.Linq;

using System.IO;
using System.Text;

using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

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

				#region Logic init

				tray_icon = new(this);
				Application.Current.Exit += (o, e) => tray_icon.Dispose();

				CLArgs.Load(this);

				var main_thr_pool = new CustomThreadPool(Environment.ProcessorCount+1);

				var pipe = new CommandsPipe();
				Application.Current.Exit += (o, e) => pipe.Shutdown();

				var thumb_gen = new ThumbGenerator(main_thr_pool, "cache");
				Application.Current.Exit += (o, e) => thumb_gen.Shutdown();
				pipe.AddThumbGen(thumb_gen);

				if (shutdown_triggered)
					Application.Current.Shutdown(0);

				pipe.StartAccepting();

				#endregion

				SizeChanged += (o, e) =>
				{
					Measure(default);
					var sz = DesiredSize;
					MinWidth = sz.Width;
					MinHeight = sz.Height;
					Measure(new(ActualWidth, ActualHeight));
				};

				#region Job count

				slider_want_job_count.ValueChanged += (o, e) =>
					main_thr_pool.SetJobCount((int)e.NewValue);
				slider_want_job_count.Value = Settings.Root.MaxJobCount;

				slider_active_job_count.Maximum = main_thr_pool.MaxJobCount;
				main_thr_pool.ActiveJobsCountChanged += get_act_job_count =>
					Dispatcher.InvokeAsync(() => Utils.HandleExtension(() =>
						slider_active_job_count.Value = get_act_job_count()
					));

				main_thr_pool.PendingJobCountChanged += get_count =>
					Dispatcher.InvokeAsync(() => Utils.HandleExtension(() =>
						 tb_pending_jobs_count.Text = get_count().ToString()
					));

				/**
				for (int i = 0; i < 8; i++)
				{
					void job()
					{
						System.Threading.Thread.Sleep(1000);
						main_thr_pool.AddJob(job);
					}
					main_thr_pool.AddJob(job);
				}
				/**/

				#endregion

				#region Cache info

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
				}));
				cache_info_updater.Trigger(TimeSpan.Zero, false);
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

				b_cache_clear.Click += (o, e) => main_thr_pool.AddJob(thumb_gen.ClearAll);

				b_cache_rebuild.Click += (o, e) =>
				{
					throw new NotImplementedException();
				};

				#endregion

				#region Allowed extensions

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

					if (CustomMessageBox.ShowYesNo("Confirm changes", sb.ToString()))
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

				#endregion

				#region Thumbnail compare

				var thumb_compare_all = new[] { thumb_compare_org, thumb_compare_gen };

				string? last_thumb_compare_fname = null;
				void choose_thumb_compare_file(string fname) => Utils.HandleExtension(() =>
				{
					thumb_compare_org.Set(() => COMManip.GetExistingThumbFor(fname));
					thumb_gen.Generate(fname, thumb_fname =>
						thumb_compare_gen.Set(() =>
						{
							using var str = File.OpenRead(thumb_fname);
							var res = new BitmapImage();
							res.BeginInit();
							res.CacheOption = BitmapCacheOption.OnLoad;
							res.StreamSource = str;
							res.EndInit();
							return res;
						}),
						true
					);
					grid_thumb_compare.HorizontalAlignment = HorizontalAlignment.Center;
					c_thumb_compare_1.VerticalAlignment = VerticalAlignment.Bottom;
					c_thumb_compare_2.VerticalAlignment = VerticalAlignment.Top;
					last_thumb_compare_fname = fname;
				});
				void clear_thumb_compare_file()
				{
					foreach (var tcv in thumb_compare_all)
						tcv.Reset();
					grid_thumb_compare.HorizontalAlignment = HorizontalAlignment.Stretch;
					c_thumb_compare_1.VerticalAlignment = VerticalAlignment.Stretch;
					c_thumb_compare_2.VerticalAlignment = VerticalAlignment.Stretch;
					last_thumb_compare_fname = null;
				}

				foreach (var tcv in thumb_compare_all)
					tcv.MouseDown += (o, e) =>
					{
						if (e.ChangedButton == MouseButton.Left)
						{
							new FileChooser(last_thumb_compare_fname, choose_thumb_compare_file).ShowDialog();
							e.Handled = true;
						}
						else if (e.ChangedButton == MouseButton.Right)
						{
							clear_thumb_compare_file();
							e.Handled = true;
						}
						else if (e.ChangedButton == MouseButton.Middle)
						{
							if (last_thumb_compare_fname != null)
								System.Diagnostics.Process.Start("explorer", $"/select,\"{last_thumb_compare_fname}\"");
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

				static void drag_handler(object o, DragEventArgs e)
				{
					var files = (string[])e.Data.GetData(DataFormats.FileDrop);
					if (files==null || files.Length!=1 || !File.Exists(files.Single()))
						e.Effects = DragDropEffects.None;
					e.Handled = true;
				}
				grid_thumb_compare.DragEnter += drag_handler;
				grid_thumb_compare.DragOver += drag_handler;

				grid_thumb_compare.Drop += (o, e) =>
				{
					var fname = ((string[])e.Data.GetData(DataFormats.FileDrop)).Single();
					choose_thumb_compare_file(fname);
					e.Handled = true;
				};

				Closing += (o, e) => clear_thumb_compare_file();

				#endregion

			}
			catch (Exception e)
			{
				Utils.HandleExtension(e);
				Environment.Exit(-1);
			}

		}

	}

}
