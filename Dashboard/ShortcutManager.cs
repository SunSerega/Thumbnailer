﻿using System;

using System.IO;

using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;

using Shell32;
using System.Runtime.InteropServices;

using SunSharpUtils;
using SunSharpUtils.Threading;

namespace Dashboard;

public class ShortcutManager
{
    private static readonly Dictionary<String, (String? target, DateTime dt)> lnk_target_cache = [];
    private static readonly ConcurrentBag<String> targets_waiting = [];

    private static readonly DelayedUpdater lnk_updater = new(() =>
    {
        foreach (var lnk in lnk_target_cache.Keys.Where(lnk => !File.Exists(lnk)).ToArray())
            if (!lnk_target_cache.Remove(lnk)) throw new InvalidOperationException();
        // prev iteration took out all in wait after extra .Trigger call
        if (targets_waiting.IsEmpty) return;

        static DateTime get_lnk_change_time(String lnk)
        {
            try
            {
                return File.GetLastWriteTimeUtc(lnk);
            }
            catch (UnauthorizedAccessException)
            {
                return DateTime.MinValue;
            }
        }

        static Boolean try_get_cached_lnk_target(String lnk, out String? target)
        {
            target = null;
            if (!lnk_target_cache.TryGetValue(lnk, out var cached))
                return false;
            if (cached.dt != get_lnk_change_time(lnk))
                return false;
            target = cached.target;
            return true;
        }

        var upd = new HashSet<String?>();
        while (targets_waiting.TryTake(out var target))
            upd.Add(target);

        //var lnk_upd = new List<string>();
        var shell = new Shell();
        foreach (var lnk in new ESQuary("ext:lnk"))
        {
            if (lnk.Contains('$')) continue;
            String? lnk_target = null;
            try
            {
                if (try_get_cached_lnk_target(lnk, out lnk_target) && !upd.Contains(lnk_target))
                    continue;
                var file_item =
                    shell.NameSpace(Path.GetDirectoryName(lnk))
                    ?.Items().Item(Path.GetFileName(lnk));
                if (file_item != null)
                {

                    ShellLinkObject? lnk_item = null;
                    try { lnk_item = file_item.GetLink; }
                    catch (UnauthorizedAccessException) { }
                    catch (COMException) { }

                    if (lnk_item != null)
                    {
                        lnk_target = lnk_item.Path;
                        if (upd.Contains(lnk_target))
                        {
                            lnk_item.Path = "";
                            lnk_item.Save();
                            lnk_item.Path = lnk_target;
                            lnk_item.Save();
                            //lnk_upd.Add(lnk);
                        }
                    }
                }
            }
            catch when (!File.Exists(lnk))
            {
                continue;
            }
            catch (COMException e)
            {
                Err.Handle($"Failed to update shortcut {lnk}\n\n{e}");
                throw;
            }
            lnk_target_cache[lnk] = (lnk_target, get_lnk_change_time(lnk));
        }

        //Console.Beep();
        //TTS.Speak($"Updated {lnk_upd.Count} shortcuts: {lnk_upd.JoinToString("; ")}");
    }, nameof(ShortcutManager), is_background: false);

    public static void ResetFor(String target)
    {
        targets_waiting.Add(target);
        lnk_updater.TriggerNow();
    }

}