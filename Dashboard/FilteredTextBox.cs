using System;

using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Controls;

namespace Dashboard;

public class FilteredTextBox<T> : ContentControl
{
    private readonly FilterFunc filter;
    private readonly Action<T> valid_enter;
    private readonly Action invalid_enter;

    private readonly TextBox tb = new();
    private string uncommited_text = "";

    private static readonly Brush b_unedited = Brushes.Transparent;
    private static readonly Brush b_valid = Brushes.YellowGreen;
    private static readonly Brush b_invalid = Brushes.Coral;

    public delegate bool FilterFunc(string text, out T value);
    public FilteredTextBox(FilterFunc filter, Action<T> valid_enter, Action invalid_enter)
    {
        this.filter = filter;
        this.valid_enter = valid_enter;
        this.invalid_enter = invalid_enter;
        Content = tb;

        tb.TextChanged += (o, e) => Utils.HandleException(() =>
        {
            Edited = tb.Text != uncommited_text;
            if (!Edited)
                tb.Background = b_unedited;
            else if (filter(tb.Text, out _))
                tb.Background = b_valid;
            else
                tb.Background = b_invalid;
        });

        tb.KeyDown += (o, e) => Utils.HandleException(() =>
        {
            if (e.Key != Key.Escape) return;
            ResetContent(uncommited_text);
        });

        tb.KeyDown += (o, e) => Utils.HandleException(() =>
        {
            if (e.Key != Key.Enter) return;
            TryCommit();
        });

    }
    public FilteredTextBox(FilterFunc filter, Action<T> valid_enter, (string title, string content) invalid_enter_tb)
        : this(filter, valid_enter, () => CustomMessageBox.Show(invalid_enter_tb.title, invalid_enter_tb.content, App.Current?.MainWindow))
    { }

    public event Action? Commited;

    public bool Edited { get; private set; } = false;

    public void ResetContent(string content)
    {
        tb.Text = content;
        tb.Background = Brushes.Transparent;
        Edited = false;
        uncommited_text = content;
    }

    public void TryCommit()
    {
        if (!filter(tb.Text, out var v))
        {
            invalid_enter();
            return;
        }

        tb.Background = Brushes.Transparent;
        Edited = false;
        valid_enter(v);
        Commited?.Invoke();

        ResetContent("");
    }

}
