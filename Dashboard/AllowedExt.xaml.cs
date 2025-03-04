using System;

using System.Windows.Controls;

using SunSharpUtils;

namespace Dashboard;

public partial class AllowedExt : UserControl
{
    [Obsolete("Only for designer")]
    public AllowedExt() => InitializeComponent();

    public AllowedExt(String ext, Action on_reinstalled, Action on_removed)
    {
        InitializeComponent();
        tb_name.Text = ext;
        b_reinstall.Click += (o, e) => Err.Handle(on_reinstalled);

        b_delete.Click += (o, e) => Err.Handle(on_removed);

    }

    public String ExtName => tb_name.Text;

}
