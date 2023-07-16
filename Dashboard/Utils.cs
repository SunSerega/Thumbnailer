using System;

using System.Windows;

namespace Dashboard
{
	internal static class Utils
	{
		public static void HandleExtension(Exception e)
		{
			MessageBox.Show(e.ToString());
		}

	}
}
