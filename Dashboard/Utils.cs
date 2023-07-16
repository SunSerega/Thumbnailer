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

		public static void HandleExtension(Action act)
		{
			try
			{
				act();
			}
			catch (Exception e)
			{
				HandleExtension(e);
			}
		}

		public static T? HandleExtension<T>(Func<T> act, T? no_res = default) where T : notnull
		{
			try
			{
				return act();
			}
			catch (Exception e)
			{
				HandleExtension(e);
				return no_res;
			}
		}

	}
}
