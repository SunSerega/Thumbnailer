using System;

using System.IO;

using System.Linq;
using System.Collections.Generic;

namespace Dashboard;

public static class CLArgs
{

    private static readonly Dictionary<String, Action<String?, MainWindow>> arg_handlers = new(StringComparer.OrdinalIgnoreCase)
    {
        { "DebugLaunchIn", (data,_)=>Environment.CurrentDirectory=data! },
        { "NoWindow",
            (data, w) =>
            {
                if (data !=null)
                    throw new InvalidOperationException();
                w.tray_icon.ShowIco();
            }
        },
        { "Shutdown",
            (data, w) =>
            {
                if (data !=null)
                    throw new InvalidOperationException();
                w.Hide();
                w.shutdown_triggered = true;
            }
        },
    };

    public static void Load(MainWindow w)
    {
        Environment.CurrentDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;

        foreach (var arg in Environment.GetCommandLineArgs().Skip(1))
        {
            var ind = arg.IndexOf('=');
            var command = ind==-1 ? arg : arg.Remove(ind);
            var data = ind==-1 ? null : arg.Remove(0, ind+1);

            if (!arg_handlers.TryGetValue(command, out var arg_handler))
                throw new InvalidOperationException($"Unknown command [{command}]");

            arg_handler(data, w);
        }

        if (w.Visibility != System.Windows.Visibility.Hidden)
            w.tray_icon.ShowWin();
    }

}
