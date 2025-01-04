﻿using System;

using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;

using SunSharpUtils;
using SunSharpUtils.Threading;

using static Dashboard.AllowedExtList;

namespace Dashboard;

public static class AllowedExtInstaller
{

    private static ThumbGenerator? thumb_gen = null;
    public static void SetThumbGen(ThumbGenerator tg) => thumb_gen = tg;

    private record struct ExtChangeInfo(string Ext, ExtRegenType GenType);

    private static readonly ConcurrentBag<ExtChangeInfo> waiting_add = [];
    private static readonly ConcurrentBag<ExtChangeInfo> waiting_rem = [];
    private static readonly DelayedUpdater updater = new(() =>
    {
        while (thumb_gen is null)
            System.Threading.Thread.Sleep(1);

        static ExtChangeInfo[] collect_from_bag(ConcurrentBag<ExtChangeInfo> bag)
        {
            var res = new Dictionary<string, ExtRegenType>();
            while (bag.TryTake(out var info))
            {
                if (res.TryAdd(info.Ext, info.GenType))
                    continue;
                if (res[info.Ext] < info.GenType)
                    res[info.Ext] = info.GenType;
            }
            return res.Select(kvp => new ExtChangeInfo(kvp.Key, kvp.Value)).ToArray();
        }

        var add = collect_from_bag(waiting_add);
        var rem = collect_from_bag(waiting_rem);

        var reg_ext_args = new List<string>();
        if (rem.Length!=0) reg_ext_args.Add("rem:"+rem.Select(info => info.Ext).JoinToString(';'));
        if (add.Length!=0) reg_ext_args.Add("add:"+add.Select(info => info.Ext).JoinToString(';'));
        if (reg_ext_args.Count == 0)
            return;
        var psi = new System.Diagnostics.ProcessStartInfo(@"RegExtController.exe", reg_ext_args.JoinToString(' '))
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Environment.CurrentDirectory,
            CreateNoWindow = true,
        };
        var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new Exception($"ExitCode={p.ExitCode}");

        COMManip.NotifyRegExtChange();
        Console.Beep();

        var reset_exts = new List<string>();
        var regen_exts = new List<string>();

        foreach (var info in add)
            switch (info.GenType)
            {
                case ExtRegenType.Skip:
                    break;
                case ExtRegenType.Reset:
                    reset_exts.Add(info.Ext);
                    break;
                case ExtRegenType.Generate:
                    regen_exts.Add(info.Ext);
                    break;
                default:
                    throw new NotImplementedException();
            }

        foreach (var info in rem)
            switch (info.GenType)
            {
                case ExtRegenType.Skip:
                    break;
                case ExtRegenType.Reset:
                    reset_exts.Add(info.Ext);
                    break;
                default:
                    throw new NotImplementedException();
            }

        static IEnumerable<string> QueryExts(List<string> exts) =>
            exts.Count == 0 ? [] : new ESQuary("ext:"+exts.JoinToString(';'));

        foreach (var fname in QueryExts(reset_exts))
        {
            thumb_gen.ClearOne(fname);
            COMManip.ResetThumbFor(fname, TimeSpan.Zero);
        }

        thumb_gen.MassGenerate(QueryExts(regen_exts).Select(fname=>(fname, force_regen: true)));
    }, $"{nameof(AllowedExtInstaller)}: Install/uninstall of extension handlers in registry", is_background: false);

    public static void Install(string ext, ExtRegenType gen_type, bool trigger = true)
    {
        waiting_add.Add(new(ext, gen_type));
        if (trigger) Trigger();
    }

    public static void Uninstall(string ext, ExtRegenType gen_type, bool trigger = true)
    {
        waiting_rem.Add(new(ext, gen_type));
        if (trigger) Trigger();
    }

    public static void Trigger() => updater.TriggerNow();

}
