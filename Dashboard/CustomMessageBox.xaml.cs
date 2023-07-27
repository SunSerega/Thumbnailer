using System;

using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

namespace Dashboard
{

	public partial class CustomMessageBox : Window
	{
		public sealed class OwnerWindowContainer
		{
			private readonly Window? owner;

			public OwnerWindowContainer() => owner = Application.Current.MainWindow.IsVisible ? Application.Current.MainWindow : null;
			public OwnerWindowContainer(Window? owner) => this.owner = owner;

			public static implicit operator OwnerWindowContainer(Window? owner) => new(owner);

			public Window? Owner => owner;

		}

		public CustomMessageBox(string title, string? content, OwnerWindowContainer cont, params string[] button_names)
		{
			InitializeComponent();

			if (cont.Owner != null)
				Owner = cont.Owner;

			KeyDown += (o,e)=>
			{
				if (e.Key == Key.Escape)
					Close();
				else if (e.Key == Key.Enter && button_names.Length<2)
				{
					if (button_names.Length != 0)
						ChosenOption = button_names[0];
					Close();
				}
				else if (e.Key == Key.C &&  Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
				{
					var sb = new System.Text.StringBuilder();
					sb.Append(title);
					if (content != null)
					{
						sb.Append("\n\n");
						sb.Append(content);
					}
					Clipboard.SetText(sb.ToString());
					Console.Beep();
				}
				else
					return;
				e.Handled = true;
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

		private static readonly OwnerWindowContainer no_own = new(null);

		public static string? Show(string title, string? content, OwnerWindowContainer owner, params string[] button_names)
		{
			var mb = new CustomMessageBox(title, content, owner, button_names);
			mb.ShowDialog();
			return mb.ChosenOption;
		}
		public static string? Show(string title, string? content, params string[] button_names) => Show(title, content, no_own, button_names);

		public static string? Show(string title, string? content, OwnerWindowContainer owner) => Show(title, content, owner, "OK");
		public static string? Show(string title, string? content) => Show(title, content, no_own);

		public static string? Show(string title, OwnerWindowContainer owner) => Show(title, null, owner);
		public static string? Show(string title) => Show(title, no_own);

		public static bool ShowYesNo(string title, string content, OwnerWindowContainer owner) => "Yes"==Show(title, content, owner, "Yes", "No");
		public static bool ShowYesNo(string title, string content) => ShowYesNo(title, content, no_own);

	}

}
