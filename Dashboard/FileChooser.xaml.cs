using System;

using System.IO;

using System.Linq;

using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

namespace Dashboard
{

	public partial class FileChooser : Window
	{
		public FileChooser(string? old_fname, Action<string> on_confirm)
		{
			InitializeComponent();
			Owner = App.Current.MainWindow;

			void reset_dialog_location()
			{
				var currentScreen = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
				var workingArea = currentScreen.WorkingArea;
				Left = workingArea.Left + (workingArea.Width - ActualWidth) / 2;
				Top = workingArea.Top + (workingArea.Height - ActualHeight) / 2;
			}

			SizeChanged += (o, e) => reset_dialog_location();
			LocationChanged += (o, e) => reset_dialog_location();

			if (old_fname != null)
			{
				tb_choise.Text = old_fname;
				tb_choise.SelectAll();
			}
			tb_choise.Focus();

			void confirm(string fname)
			{
				on_confirm.Invoke(fname);
				Settings.Root.LastFileChoosePath = Path.GetDirectoryName(fname);
				Close();
			}

			b_open_system_chooser.Click += (o, e) =>
			{
				var openFileDialog = new Microsoft.Win32.OpenFileDialog
				{
					Title = "Open File",
					Filter = $"Supported files|{string.Join(';', Settings.Root.AllowedExts.Select(ext => "*."+ext))}|All files|*.*",
				};

				{
					var path = tb_choise.Text;
					while (true)
					{
						path = Path.GetDirectoryName(path);
						if (path is null)
						{
							openFileDialog.InitialDirectory = Settings.Root.LastFileChoosePath;
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

				confirm(openFileDialog.FileName);
				e.Handled = true;
			};

			KeyDown += (o, e) =>
			{
				if (e.Key == Key.Enter)
				{
					var fname = tb_choise.Text;

					if (!File.Exists(fname))
					{
						CustomMessageBox.Show("File does not exist", fname, owner: this);
						return;
					}

					confirm(fname);
					e.Handled = true;
				}
				else if (e.Key == Key.Escape)
				{
					Close();
					e.Handled = true;
				}
			};

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

}
