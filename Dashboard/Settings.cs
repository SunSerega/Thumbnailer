using System;
using System.Linq;
using SunSharpUtils;
using SunSharpUtils.Settings;

namespace Dashboard;

public sealed class GlobalSettings() : SettingsContainer<GlobalSettings.Data>("Settings (Dashboard)")
{
    public struct Data
    {
        public int MaxJobCount;
        public FileExtList AllowedExts;
        public string? LastComparedFile;
        public ByteCount MaxCacheSize;
    }

    private static readonly FieldToken<int> TMaxJobCount = MakeFieldToken(d => d.MaxJobCount, 1);
    public int MaxJobCount
    {
        get => TMaxJobCount.Get(this);
        set => TMaxJobCount.Set(this, value);
    }

    private static readonly FieldToken<FileExtList> TAllowedExts = MakeFieldToken(d => d.AllowedExts, []);
    public FileExtList AllowedExts
    {
        get => TAllowedExts.Get(this);
        set => TAllowedExts.Set(this, value);
    }

    private static readonly FieldToken<string?> TLastComparedFile = MakeFieldToken(d => d.LastComparedFile, null);
    public string? LastComparedFile
    {
        get => TLastComparedFile.Get(this);
        set => TLastComparedFile.Set(this, value);
    }

    private static readonly FieldToken<ByteCount> TMaxCacheSize = MakeFieldToken(d => d.MaxCacheSize, ByteCount.Parse("700 MB"));
    public ByteCount MaxCacheSize
    {
        get => TMaxCacheSize.Get(this);
        set => TMaxCacheSize.Set(this, value);
    }

    public static GlobalSettings Instance { get; } = new();
}

public sealed class FileSettings(string cache_path) : SettingsContainer<FileSettings.Data>(System.IO.Path.Combine(cache_path, "Settings"))
{
    // Generated static constructor doesn't run when creating instances,
    // so we need to define it explicitly, otherwise tokens will not be allocated in time
    static FileSettings() { }

    public readonly struct ChosenStreamPositionsInfo : IEquatable<ChosenStreamPositionsInfo>, ISettingsSaveable<ChosenStreamPositionsInfo>
    {
        private readonly double[] pos;
        private const double default_pos = 0.3;

        private ChosenStreamPositionsInfo(double[] pos) => this.pos=pos;
        public static readonly ChosenStreamPositionsInfo Empty = new([]);

        public int Count => pos.Length;
        public double this[int ind] => Count==0 ? default_pos : pos[ind];

        public ChosenStreamPositionsInfo WithPos(int c, int ind, double val)
        {
            var res = pos.ToArray();
            if (Count == 0)
            {
                res = new double[c];
                Array.Fill(res, default_pos);
            }
            res[ind] = val;
            return new(res);
        }

        public static ChosenStreamPositionsInfo Parse(string s) =>
            new(s.Split(';').ConvertAll(double.Parse));
        public override string ToString() => pos.JoinToString(';');

        public static bool operator ==(ChosenStreamPositionsInfo a, ChosenStreamPositionsInfo b)
        {
            if (ReferenceEquals(a.pos, b.pos))
                return true;
            if (a.Count != b.Count)
            {
                if (b.Count == 0)
                    return a.pos.All(p => p == default_pos);
                if (a.Count == 0)
                    return b.pos.All(p => p == default_pos);
                return false;
            }
            for (var i = 0; i < a.Count; ++i)
                if (a[i] != b[i])
                    return false;
            return true;

        }
        public static bool operator !=(ChosenStreamPositionsInfo left, ChosenStreamPositionsInfo right) => !(left==right);
        public bool Equals(ChosenStreamPositionsInfo other) => this == other;
        public override bool Equals(object? obj) => obj is ChosenStreamPositionsInfo other && Equals(other);

        static string ISettingsSaveable<ChosenStreamPositionsInfo>.SerializeSetting(ChosenStreamPositionsInfo setting) => setting.ToString();
        static ChosenStreamPositionsInfo ISettingsSaveable<ChosenStreamPositionsInfo>.DeserializeSetting(string setting) => Parse(setting);

        public override int GetHashCode() => HashCode.Combine(pos);

    }

    public struct Data
    {
        public string? TempsListStr; //TODO Declare new type for this
        public string? InpPath;
        public DateTime LastInpChangeTime;
        public DateTime LastCacheUseTime;
        public string? CurrentThumb;
        public bool CurrentThumbIsFinal;
        public ChosenStreamPositionsInfo ChosenStreamPositions;
        public SettingsNullable<int> ChosenThumbOptionInd;
    }

    private static readonly SettingsFieldSaver<DateTime> date_time_saver = (dt => dt.Ticks.ToString(), s => new(Int64.Parse(s)));

    private static readonly FieldToken<string?> TTempsListStr = MakeFieldToken(d => d.TempsListStr, null);
    public string? TempsListStr
    {
        get => TTempsListStr.Get(this);
        set => TTempsListStr.Set(this, value);
    }

    private static readonly FieldToken<string?> TInpPath = MakeFieldToken(d => d.InpPath, null);
    public string? InpPath
    {
        get => TInpPath.Get(this);
        set => TInpPath.Set(this, value);
    }

    private static readonly FieldToken<DateTime> TLastInpChangeTime = MakeFieldToken(d => d.LastInpChangeTime, DateTime.MinValue, date_time_saver);
    public DateTime LastInpChangeTime
    {
        get => TLastInpChangeTime.Get(this);
        set => TLastInpChangeTime.Set(this, value);
    }

    private static readonly FieldToken<DateTime> TLastCacheUseTime = MakeFieldToken(d => d.LastCacheUseTime, DateTime.MinValue, date_time_saver);
    public DateTime LastCacheUseTime
    {
        get => TLastCacheUseTime.Get(this);
        set => TLastCacheUseTime.Set(this, value);
    }

    private static readonly FieldToken<string?> TCurrentThumb = MakeFieldToken(d => d.CurrentThumb, null);
    public string? CurrentThumb
    {
        get => TCurrentThumb.Get(this);
        set => TCurrentThumb.Set(this, value);
    }

    private static readonly FieldToken<bool> TCurrentThumbIsFinal = MakeFieldToken(d => d.CurrentThumbIsFinal, false);
    public bool CurrentThumbIsFinal
    {
        get => TCurrentThumbIsFinal.Get(this);
        set => TCurrentThumbIsFinal.Set(this, value);
    }

    private static readonly FieldToken<ChosenStreamPositionsInfo> TChosenStreamPositions = MakeFieldToken(d => d.ChosenStreamPositions, ChosenStreamPositionsInfo.Empty);
    public ChosenStreamPositionsInfo ChosenStreamPositions
    {
        get => TChosenStreamPositions.Get(this);
        set => TChosenStreamPositions.Set(this, value);
    }

    private static readonly FieldToken<SettingsNullable<int>> TChosenThumbOptionInd = MakeFieldToken(d => d.ChosenThumbOptionInd, null);
    public int? ChosenThumbOptionInd
    {
        get => TChosenThumbOptionInd.Get(this);
        set => TChosenThumbOptionInd.Set(this, value);
    }

}
