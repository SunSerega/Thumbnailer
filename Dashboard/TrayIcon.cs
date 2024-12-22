﻿using System;

using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

using Hardcodet.Wpf.TaskbarNotification;

using SunSharpUtils;

namespace Dashboard;

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
            mi.Click += (o, e) => Err.Handle(Common.Shutdown);
            ContextMenu.Items.Add(mi);
        }

        w.KeyDown += (o, e) => Err.Handle(() =>
        {
            if (e.Key==Key.Escape) w.Close();
        });
        w.Closing += (o, e) => Err.Handle(() =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                Common.Shutdown();
                return;
            }
            ShowIco();
            e.Cancel = true;
        });
        // On Win10, the icon remains in the tray after the process shuts down
        // On Win11 this is no longer an issue, but might as well dispose properly
        Common.OnShutdown += _ => Err.Handle(Dispose);

        NoLeftClickDelay = true;
        LeftClickCommand = new DummyCommand(ShowWin);
    }

    public void ShowWin()
    {
        w.Show();
        Visibility = Visibility.Hidden;
        if (w.WindowState == WindowState.Minimized)
            w.WindowState = WindowState.Maximized;
        w.Focus();
    }

    public void ShowIco()
    {
        w.Hide();
        Visibility = Visibility.Visible;
    }

    private sealed class DummyCommand(Action body) : ICommand
    {
        event EventHandler? ICommand.CanExecuteChanged { add { } remove { } }

        bool ICommand.CanExecute(object? parameter) => true;

        void ICommand.Execute(object? parameter) => body();
    }

}
