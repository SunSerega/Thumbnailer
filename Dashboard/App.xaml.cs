using System.Windows;

namespace Dashboard
{

	public partial class App : Application
	{

		public bool IsShuttingDown { get; private set; } = false;

		private static App? inst;
		public static new App Current => inst!;

		public App()
		{
			inst = this;
			SessionEnding += (o, e) =>
				IsShuttingDown = true;
		}

	}

}
