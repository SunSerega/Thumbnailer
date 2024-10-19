using System;

using System.Windows.Controls;

using static Dashboard.AllowedExtList;

namespace Dashboard;

public partial class AllowedExt : UserControl
{
    [Obsolete("Only for designer")]
    public AllowedExt() => InitializeComponent();

    public AllowedExt(string ext, Action on_reinstalled, Action on_removed)
    {
        InitializeComponent();
        tb_name.Text = ext;
        b_reinstall.Click += (o, e) => Utils.HandleException(on_reinstalled);

        b_delete.Click += (o, e) => Utils.HandleException(on_removed);

    }

    public string ExtName => tb_name.Text;

}
