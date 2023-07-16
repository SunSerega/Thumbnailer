﻿using System;

using System.Windows;
using System.Windows.Input;

namespace Dashboard
{

	public partial class MainWindow : Window
	{
		public readonly TrayIcon tray_icon;

		public MainWindow()
		{
			InitializeComponent();
			
			tray_icon = new(this);
			Application.Current.Exit += (o, e) => tray_icon.Dispose();

			CLArgs.Load(this);

			var main_thr_pool = new CustomThreadPool(Environment.ProcessorCount+1);
			slider_want_job_count.Value = Settings.Root.MaxJobCount;
			slider_want_job_count.ValueChanged += (o, e) =>
				main_thr_pool.SetJobCount((int)e.NewValue);

			slider_active_job_count.Maximum = main_thr_pool.MaxJobCount;
			main_thr_pool.ActiveJobsCountChanged += get_act_job_count =>
				Dispatcher.BeginInvoke(() =>
				{
					try
					{
						slider_active_job_count.Value = get_act_job_count();
					}
					catch (Exception e)
					{
						MessageBox.Show(e.ToString());
					}
				});

		}

	}

}
