﻿using System;

using System.IO;

using System.Threading.Tasks;

using System.Linq;
using System.Collections;
using System.Collections.Generic;

using System.Windows.Media;
using System.Windows.Media.Imaging;

using SunSharpUtils;
using SunSharpUtils.Settings;
using SunSharpUtils.Threading;

namespace Dashboard;

public readonly struct ByteCount(Int64 in_bytes) : IEquatable<ByteCount>, ISettingsSaveable<ByteCount>
{
    private readonly Int64 in_bytes = in_bytes;

    private static readonly String[] byte_scales = [ "B", "KB", "MB", "GB" ];
    private const Int32 scale_factor = 1024;
    private const Int32 scale_up_threshold = 5000;

    public static ByteCount Compose(Double v, Int32 scale_ind)
    {
        v *= Math.Pow(scale_factor, scale_ind);
        return (Int64)v;
    }

    public static void ForEachScale(Action<String> act) => Array.ForEach(byte_scales, act);

    public static implicit operator ByteCount(Int64 in_bytes) => new(in_bytes);

    public static ByteCount Parse(String s)
    {
        var spl = s.Split([' '], 2);
        if (spl.Length==1) return Int64.Parse(s);

        var c = Double.Parse(spl[0]);
        var scale_i = byte_scales.AsReadOnly().IndexOf(spl[1]);
        if (scale_i==-1) throw new FormatException($"[{spl[1]}] is not a byte scale");
        for (var i = 0; i< scale_i; ++i)
            c *= scale_factor;

        return (Int64)c;
    }

    public static Boolean operator ==(ByteCount c1, ByteCount c2) => c1.in_bytes == c2.in_bytes;
    public static Boolean operator !=(ByteCount c1, ByteCount c2) => c1.in_bytes != c2.in_bytes;
    public Boolean Equals(ByteCount other) => this == other;
    public override Boolean Equals(Object? other_obj) => other_obj is ByteCount other && this == other;

    public static Boolean operator <(ByteCount c1, ByteCount c2) => c1.in_bytes < c2.in_bytes;
    public static Boolean operator >(ByteCount c1, ByteCount c2) => c1.in_bytes > c2.in_bytes;

    public static Int64 operator +(ByteCount c1, ByteCount c2) => c1.in_bytes + c2.in_bytes;
    public static Int64 operator -(ByteCount c1, ByteCount c2) => c1.in_bytes - c2.in_bytes;

    public (Double v, Int32 scale_ind) Split()
    {
        var v = (Double)in_bytes;
        
        if (byte_scales.Length==0)
            throw new NotImplementedException();
        var scale_ind = 0;

        while (true)
        {
            if (Math.Abs(v) < scale_up_threshold) break;
            if (scale_ind+1 == byte_scales.Length) break;
            scale_ind += 1;
            v /= scale_factor;
        }

        return (v, scale_ind);
    }

    public override String ToString()
    {
        var (v, scale_ind) = Split();

        var sign = "";
        if (v<0)
        {
            v *= -1;
            sign = "-";
        }

        return $"{sign}{v:0.##} {byte_scales[scale_ind]}";
    }

    public override Int32 GetHashCode() => in_bytes.GetHashCode();

    static String ISettingsSaveable<ByteCount>.SerializeSetting(ByteCount setting) => setting.ToString();
    static ByteCount ISettingsSaveable<ByteCount>.DeserializeSetting(String setting) => Parse(setting);

}

public static class ColorExtensions
{

    public static Color FromAhsb(Byte a, Double h, Double s, Double b)
    {
        //h %= 1;
        if (h < 0 || 1 < h) throw new ArgumentOutOfRangeException(nameof(h), "hue must be between 0 and 1");

        if (s < 0 || 1 < s) throw new ArgumentOutOfRangeException(nameof(s), "saturation must be between 0 and 1");
        if (b < 0 || 1 < b) throw new ArgumentOutOfRangeException(nameof(b), "brightness must be between 0 and 1");

        Double hueSector = h * 6;
        Int32 hueSectorIntegerPart = (Int32)hueSector;
        Double hueSectorFractionalPart = hueSector - hueSectorIntegerPart;

        Double
            p = b * (1 - s),
            q = b * (1 - hueSectorFractionalPart * s),
            t = b * (1 - (1 - hueSectorFractionalPart) * s);

        var iq = Convert.ToByte(q*255);
        var ib = Convert.ToByte(b*255);
        var ip = Convert.ToByte(p*255);
        var it = Convert.ToByte(t*255);
        return hueSectorIntegerPart switch
        {
            0 => Color.FromArgb(a, ib, it, ip),
            1 => Color.FromArgb(a, iq, ib, ip),
            2 => Color.FromArgb(a, ip, ib, it),
            3 => Color.FromArgb(a, ip, iq, ib),
            4 => Color.FromArgb(a, it, ip, ib),
            5 => Color.FromArgb(a, ib, ip, iq),
            _ => throw new InvalidOperationException()
        };

    }
}

public static class FFmpeg
{

    public sealed class InvokeState(
        System.Diagnostics.Process p,
        Task<(String? otp, String? err)> t
    )
    {

        public void Kill()
        {
            if (!Dispose(expect_kill: true))
                return;
            if (p.HasExited) return;
            BeenKilled = true;
            p.Kill();
            var (otp, err) = t.Result;
            Prompt.Notify(
                $"[{p.StartInfo.FileName} {p.StartInfo.Arguments}] hanged. Output:",
                otp + "\n\n===================\n\n" + err
            );
        }

        private Int32 disposed = 0;
        public Boolean Dispose(Boolean? expect_kill)
        {
            if (System.Threading.Interlocked.Exchange(ref disposed, 1) == 1)
                return false;

            var kill = !p.HasExited;
            if (expect_kill == !kill)
                Err.Handle($"Expected to kill ffmpeg: {expect_kill}");
            if (kill)
                p.Kill();

            p.Dispose();
            return true;
        }

        public void Wait() => t.Wait();

        public String? Output => t.Result.otp;

        public Boolean BeenKilled { get; private set; }

    }

    private static readonly DelayedMultiUpdater<InvokeState> delayed_kill_switch =
        new(state => state.Kill(), "FFmpeg kill switch", is_background: false);

    public static InvokeState Invoke(String args, Func<Boolean> verify_res,
        String? execute_in = null, String exe = "mpeg",
        Func<StreamWriter, Task>? handle_inp = null,
        Func<StreamReader, Task<String?>>? handle_otp = null,
        Func<StreamReader, Task<String?>>? handle_err = null
    )
    {
        var p = new System.Diagnostics.Process
        {
            StartInfo = new("ff"+exe, args)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            }
        };
        try
        {
            if (execute_in != null)
                p.StartInfo.WorkingDirectory = execute_in;

            p.Start();

            handle_inp ??= sw => Task.CompletedTask;
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type.
            handle_otp ??= sr => sr.ReadToEndAsync();
            handle_err ??= sr => sr.ReadToEndAsync();
#pragma warning restore CS8619 // Nullability of reference types in value doesn't match target type.

            var t_inp = handle_inp(p.StandardInput);
            var t_otp = handle_otp(p.StandardOutput);
            var t_err = handle_err(p.StandardError);

            InvokeState? state = null;
            var t_p = p;
            var t = Task.Run(async () =>
            {
                try
                {
                    await t_inp;
                    t_p.StandardInput.Close();
                    await t_p.WaitForExitAsync();
                    var res = (otp: await t_otp, err: await t_err);
                    if (!verify_res())
                    {
                        throw new InvalidOperationException($"""
                            {execute_in}> [{t_p.StartInfo.FileName} {t_p.StartInfo.Arguments}]
                            otp=[{res.otp}]
                            err=[{res.err}]
                        """);
                    }
                    return res;
                }
                finally
                {
                    while (state is null) ;
                    state.Dispose(expect_kill: false);
                }
            });
            state = new InvokeState(p, t);
            p = null;

            delayed_kill_switch.TriggerUrgent(state, TimeSpan.FromSeconds(600));
            return state;
        }
        finally
        {
            p?.Dispose();
        }
    }

}

public sealed class ESQuary : IEnumerable<String>
{
    private readonly String args;

    public ESQuary(String args) => this.args = args;
    public ESQuary(String path, String arg)
    {
        if (!Directory.Exists(path))
            throw new InvalidOperationException();
        args = $"\"{path}\" {arg}";
    }

    private sealed class ESProcess : IEnumerator<String>
    {
        private static readonly String es_path = Path.GetFullPath("Dashboard-es.exe");
        private readonly System.Diagnostics.Process p;
        private readonly Task<String> t_err;

        public ESProcess(String args)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(es_path, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            p = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException();
            t_err = p.StandardError.ReadToEndAsync();
        }

        private String? l;
        public String Current => l ?? throw new InvalidOperationException();
        Object IEnumerator.Current => Current;

        public sealed class ESException(String message) : Exception(message) { }

        public Boolean MoveNext()
        {
            l = p.StandardOutput.ReadLine();
            if (l is null && p.ExitCode!=0)
                throw new ESException($"ES[{p.ExitCode}]: {t_err.Result.Trim()}");
            return l != null;
        }

        public void Reset() => throw new NotImplementedException();

        public void Dispose()
        {
            p.Kill();
            p.Dispose();
            GC.SuppressFinalize(this);
        }

        ~ESProcess() => Dispose();

    }

    public IEnumerator<String> GetEnumerator() => new ESProcess(args);
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

}

public static class Log
{
    private const String log_fname = "Dashboard.log";
    private static readonly Object log_lock = new();
    private static readonly System.Text.Encoding enc = new System.Text.UTF8Encoding(true);

    public static Int32 Count { get; private set; } =
        !File.Exists(log_fname) ? 0 : File.ReadLines(log_fname, enc).Count();

    private static readonly String[] line_separators = [ "\r\n", "\n", "\r" ];

    public static event Action? CountUpdated;

    public static void Append(String s)
    {
        using var log_locker = new ObjectLocker(log_lock);
        var lns = s.Split(line_separators, StringSplitOptions.None);
        File.AppendAllLines(log_fname, lns, enc);
        Count += lns.Length;
        CountUpdated?.Invoke();
        Console.Beep(2000, 30);
    }

    public static void Show() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    {
        FileName = log_fname,
        UseShellExecute = true,
        Verb = "open",
    })?.Dispose();

}

public static class TTS
{
    private static readonly System.Speech.Synthesis.SpeechSynthesizer speaker;

    static TTS()
    {
        speaker = new();
        speaker.SetOutputToDefaultAudioDevice();
        speaker.SelectVoiceByHints(System.Speech.Synthesis.VoiceGender.Female);
    }

    public static void Speak(String s) => speaker.Speak(s);

}

public static class BitmapUtils
{

    public static BitmapImage LoadUncached(String fname)
    {
        using var str = File.OpenRead(fname);
        var res = new BitmapImage();
        res.BeginInit();
        res.CacheOption = BitmapCacheOption.OnLoad;
        res.StreamSource = str;
        res.EndInit();
        return res;
    }

}
