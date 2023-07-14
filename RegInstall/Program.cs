


using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

static partial class RegexUtils
{

    [GeneratedRegex(@"Common extensions: ([\w,]+)\.")]
    public static partial Regex ExtractFFmpegCommonExt();

}

static class RegInstall
{

    static readonly Dictionary<string, bool> all_ext = new(StringComparer.OrdinalIgnoreCase);
    static void AddExt(string ext, bool need)
    {
        if (all_ext.TryGetValue(ext, out var old_need))
        {
            if (old_need != need)
                throw new InvalidOperationException($"ext={ext}");
            return;
        }
        all_ext[ext] = need;

        Console.WriteLine(ext);
        if (!need)
            Microsoft.Win32.Registry.ClassesRoot.DeleteSubKey('.'+ext+@"\ShellEx\{e357fccd-a995-4576-b01f-234630154e96}", false);
        else
        {
            var key = Microsoft.Win32.Registry.ClassesRoot.CreateSubKey('.'+ext+@"\ShellEx\{e357fccd-a995-4576-b01f-234630154e96}");
            Console.WriteLine($"old={key.GetValue(null)??"nil"}");
            key.SetValue(null, "{E7CBDB01-06C9-4C8F-A061-2EDCE8598F99}");
        }

    }

    static void AddAllExt(string otp, bool need)
    {
        foreach (var m in RegexUtils.ExtractFFmpegCommonExt().Matches(otp).Cast<Match>())
            foreach (var ext in m.Groups[1].Value.Split(','))
                AddExt(ext, need);
    }

    static void Main()
    {
        var log = File.CreateText("reg install.log");

        AddExt("thumb_test", true);
        AddExt("gif", true);

        var psi = new System.Diagnostics.ProcessStartInfo("ffmpeg", "-formats")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var p = System.Diagnostics.Process.Start(psi)!;
        p.StandardError.ReadToEndAsync().ContinueWith(t => log.WriteLine(t.Result));

        var started = false;
        p.OutputDataReceived += (o, e) =>
        {
            var format_s = e.Data;
            if (format_s == null) return;
            //Console.WriteLine($"[{format_s}]: {started}");

            if (!started)
            {
                if (format_s == " --")
                    started = true;
                return;
            }

            if (format_s[3] != ' ')
                throw new FormatException(format_s);

            if (format_s[1]==' ')
                return;
            else
            if (format_s[1]!='D')
                throw new FormatException(format_s);

            var wds = format_s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (wds.Length < 2)
                throw new FormatException(format_s);

            var psi2 = new System.Diagnostics.ProcessStartInfo("ffmpeg", $"ffmpeg -h demuxer={wds[1]}")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var p2 = System.Diagnostics.Process.Start(psi2)!;
            p2.StandardError.ReadToEndAsync().ContinueWith(t => log.WriteLine(t.Result));
            var p2_otp = p2.StandardOutput.ReadToEnd();
            p2.WaitForExit();

            if (new[] { "subtitle", "typewriter", "DAT", "Draw File", "Tracker formats", "ModPlug" }.Any(p2_otp.Contains))
            {
                AddAllExt(p2_otp, false);
                return;
            }

            Console.WriteLine($"{wds[1]}: [{p2_otp}]");
            AddAllExt(p2_otp, true);


            //foreach (var ext in wds[1].Split('*'))
            //{
            //    Console.WriteLine(ext);

            //    //var key = Microsoft.Win32.Registry.ClassesRoot.CreateSubKey('.'+ext+@"\ShellEx\{e357fccd-a995-4576-b01f-234630154e96}");
            //    //Console.WriteLine($"old={key.GetValue(null)??"nil"}");
            //    //key.SetValue(null, "{E7CBDB01-06C9-4C8F-A061-2EDCE8598F99}");

            //}

            //Console.WriteLine(new string('=', 50));
        };
        p.BeginOutputReadLine();

        p.WaitForExit();

        log.Close();
        Console.WriteLine(new string('=', 30));
        foreach (var g in all_ext.Keys.GroupBy(ext => all_ext[ext]))
            Console.WriteLine($"{g.Key}: " + string.Join(',', g.Order()));

    }
}


