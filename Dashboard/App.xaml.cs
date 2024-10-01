using System;

using System.Windows;

namespace Dashboard;

public partial class App : Application
{

    public bool IsShuttingDown { get; private set; } = false;

    public static new App? Current { get; private set; }

    public new void Shutdown(int ec)
    {
        IsShuttingDown = true;
        base.Shutdown(ec);
    }
    public new void Shutdown() => Shutdown(0);

    public App()
    {
        if (Current != null)
            throw new InvalidOperationException();
        Current = this;
        SessionEnding += (o, e) =>
        {
            if (IsShuttingDown) return;
            if (e.ReasonSessionEnding != ReasonSessionEnding.Shutdown)
                MessageBox.Show($"Application.Shutdown instead of App.Shutdown");
            IsShuttingDown = true;
        };
    }

}
