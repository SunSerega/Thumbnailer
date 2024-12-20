using System;

using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

using Hardcodet.Wpf.TaskbarNotification;
using System.Windows.Media.Imaging;

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
            mi.Click += (o, e) => Utils.HandleException(App.Current!.Shutdown);
            ContextMenu.Items.Add(mi);
        }

        w.KeyDown += (o, e) => Utils.HandleException(() =>
        {
            if (e.Key==Key.Escape) w.Close();
        });
        w.Closing += (o, e) => Utils.HandleException(() =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                App.Current!.Shutdown();
                return;
            }
            ShowIco();
            e.Cancel = true;
        });
        App.Current!.Exit += (o, e) => Utils.HandleException(Dispose);

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
