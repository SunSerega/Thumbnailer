using System;

using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

namespace Dashboard
{

	public partial class CustomMessageBox : Window
	{

		public CustomMessageBox(string title, string? content, Window? owner, params string[] button_names)
		{
			InitializeComponent();

			//if (Application.Current.Dispatcher.Thread == System.Threading.Thread.CurrentThread)
			owner ??= Application.Current.MainWindow;
			Owner = owner;

			KeyDown += (o,e)=>
			{
				if (e.Key == Key.Escape)
					Close();
				if (e.Key == Key.Enter && button_names.Length<2)
				{
					if (button_names.Length != 0)
						ChosenOption = button_names[0];
					Close();
				}
			};

			Title = title;

			if (content is null)
				tb_body.Visibility = Visibility.Collapsed;
			else
				tb_body.Text = content;

			if (button_names.Length == 0)
			{
				sp_buttons.Visibility = Visibility.Collapsed;
				return;
			}
			foreach (var button_name in button_names)
			{
				var b = new Button
				{
					Content = button_name,
				};
				b.Click += (o, e) =>
				{
					ChosenOption = button_name;
					Close();
				};
				sp_buttons.Children.Add(b);
			}

		}

		public string? ChosenOption { get; private set; }

		public static string? Show(string title, string? content, Window? owner=null, params string[] button_names)
		{
			var mb = new CustomMessageBox(title, content, owner, button_names);
			mb.ShowDialog();
			return mb.ChosenOption;
		}
		public static string? Show(string title, string? content, params string[] button_names) => Show(title, content, null, button_names);
		public static string? Show(string title, string? content = null, Window? owner = null) => Show(title, content, owner, "OK");

		public static bool ShowYesNo(string title, string content, Window? owner = null) => "Yes"==Show(title, content, owner, "Yes", "No");

	}

}
