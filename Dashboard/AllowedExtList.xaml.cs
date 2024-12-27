using System;

using System.Linq;
using System.Collections.Generic;

using System.Windows.Media;
using System.Windows.Controls;

using SunSharpUtils;
using SunSharpUtils.WPF;

namespace Dashboard;

public partial class AllowedExtList : UserControl
{
    private readonly FileExtList selected_exts = [];
    private readonly Dictionary<string, AllowedExt> ext_vis_map = [];

    private static readonly Brush b_ext_commited = Brushes.Transparent;
    private static readonly Brush b_ext_added = Brushes.YellowGreen;
    private static readonly Brush b_ext_removed = Brushes.Coral;
    private static readonly Brush b_ext_broken = Brushes.Violet;

    private static bool CheckInstalled(string ext)
    {
        var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey('.'+ext+@"\ShellEx\{e357fccd-a995-4576-b01f-234630154e96}");
        if (key is null) return false;
        return key.GetValue(null) is string guid && guid == "{E7CBDB01-06C9-4C8F-A061-2EDCE8598F99}";
    }

    public AllowedExtList()
    {
        InitializeComponent();

        static bool parse_exts(string text, out string[] exts)
        {
            exts = [];
            if (text.Contains('.')) return false;
            exts = text.Split(';');
            return exts.All(FileExtList.Validate);
        }

        var tb_new_ext = new FilteredTextBox<string[]>(
            parse_exts,
            exts =>
            {
                foreach (var ext in exts)
                {
                    if (selected_exts.Contains(ext)) continue;
                    AddExt(ext);
                }
            },
            ("Invalid extension", "Extension must be non-empty and contain only letters and digits, excluding the dot")
        );
        c_new_ext.Content = tb_new_ext;
        tb_new_ext.Commited += () => tb_new_ext.ResetContent("");

        b_add_ext.Click += (o,e)=> Err.Handle(() => tb_new_ext.TryCommit());

    }

    private Action MakeExtReinstall(string ext) => () =>
    {
        var gen_type = AskRegenType(
            "What to do with files of reinstalled extension?", [ext],
            ExtRegenType.Skip, ExtRegenType.Reset, ExtRegenType.Generate
        );
        if (gen_type is null) return;
        AllowedExtInstaller.Install(ext, gen_type.Value);
        ext_vis_map[ext].b_body.Background = b_ext_commited;
    };
    private Action MakeExtRemove(string ext) => () =>
    {
        if (selected_exts.Contains(ext))
            RemoveExt(ext);
        else
            AddExt(ext);
    };
    private AllowedExt MakeExtVis(string ext) =>
        new(ext, MakeExtReinstall(ext), MakeExtRemove(ext));

    public void AddFromSettings()
    {
        foreach (var ext in GlobalSettings.Instance.AllowedExts)
        {
            if (!selected_exts.Add(ext))
                throw new InvalidOperationException();
            var vis = MakeExtVis(ext);
            allowed_ext_container.Children.Add(vis);
            ext_vis_map.Add(ext, vis);
            vis.b_body.Background = CheckInstalled(ext) ? b_ext_commited : b_ext_broken;
        }
    }

    private AllowedExt AddVisExt(string ext)
    {
        var vis = MakeExtVis(ext);
        ext_vis_map.Add(ext, vis);

        {
            var i1 = 0;
            var i2 = allowed_ext_container.Children.Count;
            while (i1 != i2)
            {
                var m = (i1+i2) / 2;
                var m_el = (AllowedExt)allowed_ext_container.Children[m];
                if (string.CompareOrdinal(m_el.ExtName, ext) > 0)
                    i2 = m;
                else
                    i1 = m+1;
            }
            allowed_ext_container.Children.Insert(i1, vis);
        }

        return vis;
    }
    public void AddExt(string ext)
    {
        if (!selected_exts.Add(ext))
            throw new InvalidOperationException();
        b_check_n_commit.IsEnabled = selected_exts != GlobalSettings.Instance.AllowedExts;

        if (GlobalSettings.Instance.AllowedExts.Contains(ext))
        {
            var vis = ext_vis_map[ext];
            vis.b_body.Background = b_ext_commited;
        }
        else
        {
            var vis = AddVisExt(ext);
            vis.b_body.Background = b_ext_added;
        }

    }

    private void RemoveVisExt(string ext)
    {
        if (!ext_vis_map.Remove(ext, out var vis))
            throw new InvalidOperationException();
        allowed_ext_container.Children.Remove(vis);
    }
    public void RemoveExt(string ext)
    {
        if (!selected_exts.Remove(ext))
            throw new InvalidOperationException();
        b_check_n_commit.IsEnabled = selected_exts != GlobalSettings.Instance.AllowedExts;

        if (GlobalSettings.Instance.AllowedExts.Contains(ext))
        {
            var vis = ext_vis_map[ext];
            vis.b_body.Background = b_ext_removed;
        }
        else
        {
            RemoveVisExt(ext);
        }

    }

    public enum ExtRegenType
    {
        Skip = 0,
        Reset = 1,
        Generate = 2,
    }

    public static ExtRegenType? AskRegenType(string title, string[] exts, params ExtRegenType[] options)
    {
        if (exts.Length == 0)
            return ExtRegenType.Skip;
        return CustomMessageBox.Show(
            title,
            string.Join(';', exts) + "\n\nPress Escape to cancel",
            options
        );
    }

    public void CommitAll()
    {
        var add = selected_exts.Where(ext => !GlobalSettings.Instance.AllowedExts.Contains(ext)).ToArray();
        var rem = GlobalSettings.Instance.AllowedExts.Where(ext => !selected_exts.Contains(ext)).ToArray();

        if (add.Length==0 && rem.Length == 0)
            throw new InvalidOperationException();

        var sb = new System.Text.StringBuilder();

        if (add.Length!=0)
        {
            sb.AppendLine($"Added: ");
            foreach (var ext in add)
                sb.AppendLine(ext);
            sb.Append('~', 30);
            sb.AppendLine();
            sb.AppendLine();
        }

        if (rem.Length!=0)
        {
            sb.AppendLine($"Removed: ");
            foreach (var ext in rem)
                sb.AppendLine(ext);
            sb.Append('~', 30);
            sb.AppendLine();
            sb.AppendLine();
        }

        sb.AppendLine($"Commit?");

        if (!CustomMessageBox.ShowYesNo("Confirm changes", sb.ToString(), WPFCommon.CurrentApp?.MainWindow))
            return;

        var add_gen_type = AskRegenType(
            "What to do with files of added extensions?", add,
            ExtRegenType.Skip, ExtRegenType.Reset, ExtRegenType.Generate
        );
        if (add_gen_type is null) return;

        var rem_gen_type = AskRegenType(
            "What to do with files of removed extensions?", rem,
            ExtRegenType.Skip, ExtRegenType.Reset
        );
        if (rem_gen_type is null) return;

        foreach (var ext in rem)
        {
            AllowedExtInstaller.Uninstall(ext, add_gen_type.Value, trigger: false);
            RemoveVisExt(ext);
        }
        foreach (var ext in add)
        {
            AllowedExtInstaller.Install(ext, rem_gen_type.Value, trigger: false);
            ext_vis_map[ext].b_body.Background = b_ext_commited;
        }
        AllowedExtInstaller.Trigger();

        GlobalSettings.Instance.AllowedExts = new(selected_exts);
        b_check_n_commit.IsEnabled = false;
    }

}
