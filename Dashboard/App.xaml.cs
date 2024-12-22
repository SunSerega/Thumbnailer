using System;

using System.Windows;

using SunSharpUtils.WPF;

namespace Dashboard;

public partial class App : Application
{
    public App()
    {
        WPFCommon.Init(this);
    }
}
