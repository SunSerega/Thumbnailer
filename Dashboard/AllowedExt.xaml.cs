using System;

using System.Windows.Controls;

using static Dashboard.AllowedExtList;

namespace Dashboard;

public partial class AllowedExt : UserControl
{
    [Obsolete("Only for designer")]
    public AllowedExt() => InitializeComponent();

    public AllowedExt(string ext, Action on_removed)
    {
        InitializeComponent();
        tb_name.Text = ext;
        b_reinstall.Click += (o, e) => Utils.HandleException(() =>
        {
            var gen_type = AskRegenType(
                "What to do with files to reinstalled extension?", [ext],
                ExtRegenType.Skip, ExtRegenType.Reset, ExtRegenType.Generate
            );
            if (gen_type is null) return;
            AllowedExtInstaller.Install(ext, gen_type.Value);
        });

        b_delete.Click += (o, e) => Utils.HandleException(on_removed);

    }

    public string ExtName => tb_name.Text;

}
