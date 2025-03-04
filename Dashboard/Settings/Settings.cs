using System;

using SunSharpUtils.Settings;

namespace Dashboard.Settings;

public sealed class GlobalSettings() : SettingsContainer<GlobalSettings, GlobalSettings.Data>("Settings (Dashboard)", save_all: true)
{
    static GlobalSettings() { }

    public struct Data
    {
        public Int32 MaxJobCount;
        public FileExtList AllowedExts;
        public String? LastComparedFile;
        public ByteCount MaxCacheSize;
    }

    private static readonly FieldToken<Int32> TMaxJobCount = MakeFieldToken(d => d.MaxJobCount, 1);
    public Int32 MaxJobCount
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

    private static readonly FieldToken<String?> TLastComparedFile = MakeFieldToken(d => d.LastComparedFile, null);
    public String? LastComparedFile
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

public sealed class FileSettings(String cache_path) : SettingsContainer<FileSettings, FileSettings.Data>(System.IO.Path.Combine(cache_path, "Settings"), save_all: false)
{
    static FileSettings()
    {
        RegisterUpgradeAct("TempsListStr", (ref FieldUpgradeContext ctx) => ctx.Set(TTempsList,
            ctx.Value is null ? TempsListInfo.Empty : TempsListInfo.Parse(ctx.Value)
        ));
    }

    public struct Data
    {
        public TempsListInfo TempsList;
        public String? InpPath;
        public DateTime LastInpChangeTime;
        public DateTime LastCacheUseTime;
        public String? CurrentThumb;
        public Boolean CurrentThumbIsFinal;
        public ChosenStreamPositionsInfo ChosenStreamPositions;
        public SettingsNullable<Int32> ChosenThumbOptionInd;
    }

    private static readonly SettingsFieldSaver<DateTime> date_time_saver = (dt => dt.Ticks.ToString(), s => new(Int64.Parse(s)));

    private static readonly FieldToken<TempsListInfo> TTempsList = MakeFieldToken(d => d.TempsList, TempsListInfo.Empty);
    public TempsListInfo TempsList
    {
        get => TTempsList.Get(this);
        set => TTempsList.Set(this, value);
    }

    private static readonly FieldToken<String?> TInpPath = MakeFieldToken(d => d.InpPath, null);
    public String? InpPath
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

    private static readonly FieldToken<String?> TCurrentThumb = MakeFieldToken(d => d.CurrentThumb, null);
    public String? CurrentThumb
    {
        get => TCurrentThumb.Get(this);
        set => TCurrentThumb.Set(this, value);
    }

    private static readonly FieldToken<Boolean> TCurrentThumbIsFinal = MakeFieldToken(d => d.CurrentThumbIsFinal, false);
    public Boolean CurrentThumbIsFinal
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

    private static readonly FieldToken<SettingsNullable<Int32>> TChosenThumbOptionInd = MakeFieldToken(d => d.ChosenThumbOptionInd, null);
    public Int32? ChosenThumbOptionInd
    {
        get => TChosenThumbOptionInd.Get(this);
        set => TChosenThumbOptionInd.Set(this, value);
    }

}
