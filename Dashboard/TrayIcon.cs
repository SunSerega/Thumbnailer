﻿using System;

using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

using Hardcodet.Wpf.TaskbarNotification;
using System.Windows.Media.Imaging;

namespace Dashboard
{
	public sealed class TrayIcon : TaskbarIcon
	{
		readonly Window w;

		public TrayIcon(Window w)
		{
			Visibility = Visibility.Hidden;
			this.w = w;

			ToolTipText = w.Title;

			var icon = new BitmapImage(new("pack://application:,,,/Icon.ico"));
			IconSource = icon;

			ContextMenu = new();
			{
				var mi = new MenuItem()
				{
					Header = "Shutdown",
				};
				mi.Click += (o, e) => { Application.Current.Shutdown(); };
				ContextMenu.Items.Add(mi);
			}

			w.KeyDown += (o, e) =>
			{
				if (e.Key==Key.Escape) w.Close();
			};
			w.Closing += (o, e) =>
			{
				if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
				ShowIco();
				e.Cancel = true;
			};

			NoLeftClickDelay = true;
			LeftClickCommand = new DummyCommand(ShowWin);
		}

		public void ShowWin()
		{
			w.Show();
			Visibility = Visibility.Hidden;
		}

		public void ShowIco()
		{
			w.Hide();
			Visibility = Visibility.Visible;
		}

		private sealed class DummyCommand : ICommand
		{
			private readonly Action body;

			public DummyCommand(Action body) => this.body=body;

			event EventHandler? ICommand.CanExecuteChanged { add { } remove { } }

			bool ICommand.CanExecute(object? parameter) => true;

			void ICommand.Execute(object? parameter) => body();
		}


	}
}