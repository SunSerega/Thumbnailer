using System;

using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

namespace Dashboard;

public partial class CustomMessageBox : Window
{
    public sealed class OwnerWindowContainer
    {
        private readonly Window? owner;

        public OwnerWindowContainer() => owner = App.Current?.MainWindow.IsVisible??false ? App.Current.MainWindow : null;
        public OwnerWindowContainer(Window? owner) => this.owner = owner;

        public static implicit operator OwnerWindowContainer(Window? owner) => new(owner);

        public Window? Owner => owner;

    }

    public CustomMessageBox(string title, string? content, OwnerWindowContainer cont, params string[] button_names)
    {
        try
        {
            InitializeComponent();
        }
        catch when (App.Current?.IsShuttingDown??true)
        {
            MessageBox.Show(content??"", title);
            return;
        }

        if (cont.Owner != null && cont.Owner.IsVisible)
            Owner = cont.Owner;

        KeyDown += (o, e) => Utils.HandleException(() =>
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
        });

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
            b.Click += (o, e) => Utils.HandleException(() =>
            {
                ChosenOption = button_name;
                Close();
            });
            sp_buttons.Children.Add(b);
        }

    }

    public string? ChosenOption { get; private set; }

    private static readonly OwnerWindowContainer no_own = new(null);

    public static string? Show(string title, string? content, OwnerWindowContainer owner, params string[] button_names)
    {
        if (System.Threading.Thread.CurrentThread.GetApartmentState() != System.Threading.ApartmentState.STA)
        {
            if (owner.Owner != null)
                throw new InvalidOperationException();
            string? res = null;
            var thr = new System.Threading.Thread(() => Utils.HandleException(() =>
                res = Show(title, content, owner, button_names)
            ))
            {
                IsBackground = true,
                Name = $"STA thread for {nameof(CustomMessageBox)}.{nameof(Show)}",
            };
            thr.SetApartmentState(System.Threading.ApartmentState.STA);
            thr.Start();
            thr.Join();
            return res;
        }
        var mb = new CustomMessageBox(title, content, owner, button_names);
        mb.ShowDialog();
        return mb.ChosenOption;
    }
    public static string? Show(string title, string? content, params string[] button_names) => Show(title, content, no_own, button_names);

    public static TRes? Show<TRes>(string title, string? content, OwnerWindowContainer owner, params TRes[] options) where TRes : struct, Enum
    {
        if (options.Length == 0) throw new ArgumentException("options.Length == 0");
        var res_name = Show(title, content, owner, Array.ConvertAll(options, e => e.ToString()));
        if (res_name is null) return null;
        return (TRes)Enum.Parse(typeof(TRes), res_name);
    }
    public static TRes? Show<TRes>(string title, string? content, params TRes[] options) where TRes : struct, Enum => Show<TRes>(title, content, no_own, options);

    public static string? Show(string title, string? content, OwnerWindowContainer owner) => Show(title, content, owner, "OK");
    public static string? Show(string title, string? content) => Show(title, content, no_own);

    public static string? Show(string title, OwnerWindowContainer owner) => Show(title, null, owner);
    public static string? Show(string title) => Show(title, no_own);

    public static bool ShowYesNo(string title, string content, OwnerWindowContainer owner) => "Yes"==Show(title, content, owner, "Yes", "No");
    public static bool ShowYesNo(string title, string content) => ShowYesNo(title, content, no_own);

}
