using System.Windows;

namespace Dashboard
{

	public partial class App : Application
	{

		public bool IsShuttingDown { get; private set; } = false;

		public static new App? Current { get; private set; }

		public App()
		{
			Current = this;
			SessionEnding += (o, e) =>
				IsShuttingDown = true;
		}

	}

}
