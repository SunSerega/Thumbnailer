using System;

using System.IO;

using System.Linq;
using System.Collections.Generic;

using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

using SunSharpUtils;
using SunSharpUtils.WPF;

using Dashboard.Settings;

namespace Dashboard;

public partial class FileChooser : Window
{
    public FileChooser(Action<IEnumerable<String>> on_confirm)
    {
        InitializeComponent();
        Owner = WPFCommon.CurrentApp!.MainWindow;

        void reset_dialog_location()
        {
            var currentScreen = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            var workingArea = currentScreen.WorkingArea;
            Left = workingArea.Left + (workingArea.Width - ActualWidth) / 2;
            Top = workingArea.Top + (workingArea.Height - ActualHeight) / 2;
        }

        SizeChanged += (o, e) => Err.Handle(reset_dialog_location);
        LocationChanged += (o, e) => Err.Handle(reset_dialog_location);

        if (!File.Exists(GlobalSettings.Instance.LastComparedFile))
            GlobalSettings.Instance.LastComparedFile = null;
        if (GlobalSettings.Instance.LastComparedFile != null)
        {
            tb_choise.Text = GlobalSettings.Instance.LastComparedFile;
            tb_choise.SelectAll();
        }
        tb_choise.Focus();

        void confirm(IEnumerable<String> res)
        {
            on_confirm(res);
            Close();
        }

        b_open_system_chooser.Click += (o, e) => Err.Handle(() =>
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Open File",
                Filter = $"Supported files|{GlobalSettings.Instance.AllowedExts.Select(ext => "*."+ext).JoinToString(';')}|All files|*.*",
                Multiselect = true,
            };

            {
                var path = tb_choise.Text;
                while (true)
                {
                    path = Path.GetDirectoryName(path);
                    if (path is null)
                    {
                        openFileDialog.InitialDirectory = Path.GetDirectoryName(GlobalSettings.Instance.LastComparedFile);
                        break;
                    }
                    if (Directory.Exists(path))
                    {
                        openFileDialog.InitialDirectory = path;
                        break;
                    }
                }
            }

            if (openFileDialog.ShowDialog() != true)
                return;

            confirm(openFileDialog.FileNames);
            e.Handled = true;
        });

        KeyDown += (o, e) => Err.Handle(() =>
        {
            if (e.Key == Key.Enter)
            {
                var path = tb_choise.Text;

                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    CustomMessageBox.ShowOK("File/Dir does not exist", path, owner: this);
                    return;
                }

                confirm(Enumerable.Repeat(path, 1));
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        });

    }
}

public sealed class SquareDecorator : Decorator
{

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null)
            return default;
        var len = Math.Min(constraint.Width, constraint.Height);

        Child.Measure(new(len, len));
        len = Math.Max(Child.DesiredSize.Width, Child.DesiredSize.Height);

        return new(len, len);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var len = Math.Min(arrangeSize.Width, arrangeSize.Height);

        Child?.Arrange(new(
            (arrangeSize.Width-len)/2, (arrangeSize.Height-len)/2,
            len, len
        ));

        return new(len, len);
    }

}
