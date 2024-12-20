using System;

using System.IO;

using System.Threading;
using System.Threading.Tasks;

using System.Linq;
using System.Collections;
using System.Collections.Generic;

using System.Runtime.CompilerServices;

using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dashboard;

public readonly struct ByteCount(long in_bytes) : IEquatable<ByteCount>
{
    private readonly long in_bytes = in_bytes;

    private static readonly string[] byte_scales = [ "B", "KB", "MB", "GB" ];
    private const int scale_factor = 1024;
    private const int scale_up_threshold = 5000;

    public static ByteCount Compose(double v, int scale_ind)
    {
        v *= Math.Pow(scale_factor, scale_ind);
        return (long)v;
    }

    public static void ForEachScale(Action<string> act) => Array.ForEach(byte_scales, act);

    public static implicit operator ByteCount(long in_bytes) => new(in_bytes);

    public static ByteCount Parse(string s)
    {
        var spl = s.Split([' '], 2);
        if (spl.Length==1) return long.Parse(s);

        var c = double.Parse(spl[0]);
        var scale_i = byte_scales.AsReadOnly().IndexOf(spl[1]);
        if (scale_i==-1) throw new FormatException($"[{spl[1]}] is not a byte scale");
        for (var i = 0; i< scale_i; ++i)
            c *= scale_factor;

        return (long)c;
    }

    public static bool operator ==(ByteCount c1, ByteCount c2) => c1.in_bytes == c2.in_bytes;
    public static bool operator !=(ByteCount c1, ByteCount c2) => c1.in_bytes != c2.in_bytes;
    public bool Equals(ByteCount other) => this == other;
    public override bool Equals(object? other_obj) => other_obj is ByteCount other && this == other;

    public static bool operator <(ByteCount c1, ByteCount c2) => c1.in_bytes < c2.in_bytes;
    public static bool operator >(ByteCount c1, ByteCount c2) => c1.in_bytes > c2.in_bytes;

    public static long operator +(ByteCount c1, ByteCount c2) => c1.in_bytes + c2.in_bytes;
    public static long operator -(ByteCount c1, ByteCount c2) => c1.in_bytes - c2.in_bytes;

    public (double v, int scale_ind) Split()
    {
        var v = (double)in_bytes;
        
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

    public override string ToString()
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

    public override int GetHashCode() => in_bytes.GetHashCode();

}

public sealed class OneToManyLock
{
    private readonly object sync_lock = new();
    private readonly ManualResetEventSlim one_wh = new(true);
    private readonly ManualResetEventSlim many_wh = new(true);
    private volatile int doing_one = 0;
    private volatile int doing_many = 0;

    public OneToManyLock() { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T OneLocked<T>(Func<T> act, bool with_priority)
    {
        one_wh.Reset();
        var need_dec = false;
        if (with_priority)
        {
            Interlocked.Increment(ref doing_one);
            need_dec = true;
        }
        Monitor.Enter(sync_lock);
        try
        {
            while (doing_many != 0)
            {
                Monitor.Exit(sync_lock);
                many_wh.Wait();
                Monitor.Enter(sync_lock);
            }
            if (!with_priority)
            {
                Interlocked.Increment(ref doing_one);
                need_dec = true;
            }
            return act();
        }
        finally
        {
            if (need_dec)
                Interlocked.Decrement(ref doing_one);
            one_wh.Set();
            Monitor.Exit(sync_lock);
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OneLocked(Action act, bool with_priority) => OneLocked(() => { act(); return 0; }, with_priority);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T ManyLocked<T>(Func<T> act)
    {
        //TODO can possibly avoid locking in most cases
        // - Need another bool, to separate want_one and actually doing_one
        // - The optimistically Interlocked.Increment
        // - And if it turns out it was wrong - then actually wait and lock
        Monitor.Enter(sync_lock);
        var need_exit = true;
        try
        {
            while (doing_one != 0)
            {
                Monitor.Exit(sync_lock);
                one_wh.Wait();
                Monitor.Enter(sync_lock);
            }
            many_wh.Reset();
            Interlocked.Increment(ref doing_many);
            Monitor.Exit(sync_lock);
            need_exit = false;
            try
            {
                return act();
            }
            finally
            {
                if (0==Interlocked.Decrement(ref doing_many))
                    many_wh.Set();
            }
        }
        finally
        {
            if (need_exit)
                Monitor.Exit(sync_lock);
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ManyLocked(Action act) => ManyLocked(() => { act(); return 0; });

}

public readonly struct FileExtList : ICollection<string>, IEquatable<FileExtList>
{
    private readonly HashSet<string> l = new(StringComparer.OrdinalIgnoreCase);

    public int Count => l.Count;

    bool ICollection<string>.IsReadOnly => false;

    public FileExtList() { }

    public FileExtList(IEnumerable<string> items) => l = new(items);
    public static FileExtList Parse(string s) => new(s.Split(';'));

    public static bool operator ==(FileExtList l1, FileExtList l2) => l1.l.SetEquals(l2.l);
    public static bool operator !=(FileExtList l1, FileExtList l2) => !(l1==l2);
    public bool Equals(FileExtList other) => this==other;
    public override bool Equals(object? obj) => obj is FileExtList other && this==other;
    public override int GetHashCode() => l.GetHashCode();

    public static bool Validate(string ext)
    {
        // implicit in the next check
        //if (ext.Contains(';'))
        //    return false;
        //if (ext.Contains('.'))
        //    return false;
        return ext.Length!=0 && ext.All(char.IsLetterOrDigit);
    }
    private static string Validated(string ext) =>
        Validate(ext) ? ext : throw new FormatException(ext);

    public bool Add(string ext) => l.Add(Validated(ext));
    public bool Remove(string ext) => l.Remove(Validated(ext));

    void ICollection<string>.Add(string ext) => Add(ext);

    public void Clear() => l.Clear();

    public bool Contains(string ext) => l.Contains(Validated(ext));
    public bool MatchesFile(string? fname)
    {
        var ext = Path.GetExtension(fname);
        if (ext is null) return false;
        if (!ext.StartsWith('.')) return false;
        return Contains(ext.Remove(0, 1));
    }

    public void CopyTo(string[] array, int arrayIndex) => l.CopyTo(array, arrayIndex);

    public IEnumerator<string> GetEnumerator() => l.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => l.GetEnumerator();

    public override string ToString() => string.Join(';', l.Order());

}

public static class ColorExtensions
{

    public static Color FromAhsb(byte a, double h, double s, double b)
    {
        //h %= 1;
        if (h < 0 || 1 < h) throw new ArgumentOutOfRangeException(nameof(h), "hue must be between 0 and 1");

        if (s < 0 || 1 < s) throw new ArgumentOutOfRangeException(nameof(s), "saturation must be between 0 and 1");
        if (b < 0 || 1 < b) throw new ArgumentOutOfRangeException(nameof(b), "brightness must be between 0 and 1");

        double hueSector = h * 6;
        int hueSectorIntegerPart = (int)hueSector;
        double hueSectorFractionalPart = hueSector - hueSectorIntegerPart;

        double
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
        Task<(string? otp, string? err)> t
    ) {
        private bool been_killed = false;

        public void Kill()
        {
            if (p.HasExited) return;
            been_killed = true;
            p.Kill();
            var (otp, err) = t.Result;
            CustomMessageBox.Show(
                $"[{p.StartInfo.FileName} {p.StartInfo.Arguments}] hanged. Output:",
                otp + "\n\n===================\n\n" + err
            );
        }

        public void Wait() => t.Wait();

        public string? Output => t.Result.otp;

        public bool BeenKilled => been_killed;

    }

    private static readonly DelayedMultiUpdater<InvokeState> delayed_kill_switch =
        new(state => state.Kill(), "FFmpeg kill switch");

    public static InvokeState Invoke(string args, Func<bool> verify_res,
        string? execute_in = null, string exe = "mpeg",
        Func<StreamWriter, Task>? handle_inp = null,
        Func<StreamReader, Task<string?>>? handle_otp = null,
        Func<StreamReader, Task<string?>>? handle_err = null
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

        var t = Task.Run(async ()=>
        {
            await t_inp;
            p.StandardInput.Close();
            await p.WaitForExitAsync();
            var res = (otp: await t_otp, err: await t_err);
            if (!verify_res())
                throw new InvalidOperationException($"{execute_in}> [{p.StartInfo.FileName} {p.StartInfo.Arguments}]\notp=[{res.otp}]\nerr=[{res.err}]");
            return res;
        });

        var res = new InvokeState(p, t);
        delayed_kill_switch.Trigger(res, TimeSpan.FromSeconds(600), null);
        return res;
    }

}

public sealed class ESQuary : IEnumerable<string>
{
    private readonly string args;

    public ESQuary(string args) => this.args = args;
    public ESQuary(string path, string arg)
    {
        if (!Directory.Exists(path))
            throw new InvalidOperationException();
        args = $"\"{path}\" {arg}";
    }

    private sealed class ESProcess : IEnumerator<string>
    {
        private static readonly string es_path = Path.GetFullPath("Dashboard-es.exe");
        private readonly System.Diagnostics.Process p;
        private readonly Task<string> t_err;

        public ESProcess(string args)
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

        private string? l;
        public string Current => l ?? throw new InvalidOperationException();
        object IEnumerator.Current => Current;

        public sealed class ESException(string message) : Exception(message) { }

        public bool MoveNext()
        {
            l = p.StandardOutput.ReadLine();
            if (l is null && p.ExitCode!=0)
                throw new ESException($"ES[{p.ExitCode}]: {t_err.Result.Trim()}");
            return l != null;
        }

        public void Reset() => throw new NotImplementedException();

        public void Dispose()
        {
            p?.Kill();
            GC.SuppressFinalize(this);
        }

        ~ESProcess() => Dispose();

    }

    public IEnumerator<string> GetEnumerator() => new ESProcess(args);
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

}

public readonly struct ObjectLocker : IDisposable
{
    private readonly object o;

    public ObjectLocker(object o)
    {
        this.o = o;
        Monitor.Enter(o);
    }

    public static ObjectLocker? TryLock(object o)
    {
        bool got_lock = false;
        Monitor.TryEnter(o, ref got_lock);
        if (!got_lock) return null;
        try
        {
            return new ObjectLocker(o);
        }
        finally
        {
            Monitor.Exit(o);
        }
    }

    public void Dispose() => Monitor.Exit(o);

}

public static class Log
{
    private const string log_fname = "Dashboard.log";
    private static readonly object log_lock = new();
    private static readonly System.Text.Encoding enc = new System.Text.UTF8Encoding(true);

    public static int Count { get; private set; } =
        !File.Exists(log_fname) ? 0 : File.ReadLines(log_fname, enc).Count();

    private static readonly string[] line_separators = [ "\r\n", "\n", "\r" ];

    public static event Action? CountUpdated;

    public static void Append(string s)
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
    });

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

    public static void Speak(string s) => speaker.Speak(s);

}

public sealed class LoadCanceledException : Exception { }

public static class Handler<TErr>
    where TErr: Exception
{

    public static T Try<T>(Func<T> body, Func<TErr,T> handle, Func<TErr, bool>? cond = null)
    {
        try
        {
            return body();
        }
        catch (TErr e) when (cond is null || cond(e))
        {
            return handle(e);
        }
    }

}

public static class Utils
{

    public static void HandleException(Exception e)
    {
        CustomMessageBox.Show("ERROR", e.ToString());
    }

    public static void HandleException(Action act)
    {
        try
        {
            act();
        }
        catch (Exception e)
        {
            HandleException(e);
        }
    }

    public static T? HandleException<T>(Func<T> act, T? no_res = default)
    {
        try
        {
            return act();
        }
        catch (Exception e)
        {
            HandleException(e);
            return no_res;
        }
    }

    public static void HandleException(this Task t) =>
        t.ContinueWith(
            t=>HandleException(t.Exception?.InnerException ??
                new Exception("Task faulted, but no exception exists")),
            TaskContinuationOptions.OnlyOnFaulted
        );

    public static BitmapImage LoadUncachedBitmap(string fname)
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
