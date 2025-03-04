using System;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Dashboard;

public partial class ThumbCompareViewer : UserControl
{

    public ThumbCompareViewer() => InitializeComponent();

    public void Set(BitmapSource? source)
    {
        if (source is null)
            b.Child = null;
        else
        {
            b.Child = new Image() { Source = source };
            b.MaxWidth = source.Width;
            b.MaxHeight = source.Height;
        }
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public void Reset()
    {
        b.Child = null;
        b.MaxWidth = Double.PositiveInfinity;
        b.MaxHeight = Double.PositiveInfinity;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
    }

}
