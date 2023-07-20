using System;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Dashboard
{

	public partial class ThumbCompareViewer : UserControl
	{
		public ThumbCompareViewer()
		{
			InitializeComponent();
		}
		public void Set(Func<BitmapSource?> make_im) =>
			Dispatcher.InvokeAsync(() =>
				Utils.HandleExtension(() =>
				{
					b.Child = new Image()
					{
						Source = make_im(),
					};
					HorizontalAlignment = HorizontalAlignment.Center;
					VerticalAlignment = VerticalAlignment.Center;
				})
			);

		public void Reset()
		{
			b.Child = null;
			HorizontalAlignment = HorizontalAlignment.Stretch;
			VerticalAlignment = VerticalAlignment.Stretch;
		}

	}

}
