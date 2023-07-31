using System.Windows;

namespace Dashboard
{

	public partial class App : Application
	{

		public bool IsShuttingDown { get; private set; } = false;

		private static App? inst;
		public static new App Current => inst!;

		public new void Shutdown(int ec)
		{
			IsShuttingDown = true;
			base.Shutdown(ec);
		}
		public new void Shutdown() => Shutdown(0);

		public App()
		{
			inst = this;
			SessionEnding += (o, e) =>
			{
				if (IsShuttingDown) return;
                MessageBox.Show($"Application.Shutdown instead of App.Shutdown");
				IsShuttingDown = true;
			};
		}

	}

}
