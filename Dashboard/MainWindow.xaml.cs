using System;

using System.Windows;

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
				Application.Current.Exit += (o, e) => tray_icon.Dispose();

				CLArgs.Load(this);

				var pipe = new CommandsPipe();

				Application.Current.Exit += (o, e) => pipe.Shutdown();

				if (shutdown_triggered)
					Application.Current.Shutdown(0);

				var main_thr_pool = new CustomThreadPool(Environment.ProcessorCount+1);
				slider_want_job_count.Value = Settings.Root.MaxJobCount;
				slider_want_job_count.ValueChanged += (o, e) =>
					main_thr_pool.SetJobCount((int)e.NewValue);

				slider_active_job_count.Maximum = main_thr_pool.MaxJobCount;
				main_thr_pool.ActiveJobsCountChanged += get_act_job_count =>
					Dispatcher.BeginInvoke(() => Utils.HandleExtension(() =>
						slider_active_job_count.Value = get_act_job_count()
					));
			}
			catch (Exception e)
			{
				Utils.HandleExtension(e);
				Environment.Exit(-1);
			}

		}

	}

}
