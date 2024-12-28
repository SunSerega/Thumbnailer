using System;

using System.Linq;

using SunSharpUtils;
using SunSharpUtils.Settings;

namespace Dashboard.Settings;

public readonly struct TempsListInfo : IEquatable<TempsListInfo>, ISettingsSaveable<TempsListInfo>
{
    private readonly (string name, string path)[] temps;

    public TempsListInfo((string name, string path)[] temps)
    {
        this.temps = temps;
        ForEach((name, path) =>
        {
            if ("=;".Any(name.Contains))
                throw new FormatException($"Invalid symbol in temp name: {name}");
            if (";".Any(path.Contains))
                throw new FormatException($"Invalid symbol in temp path: {path}");
        });
    }
    public static TempsListInfo Empty { get; } = new([]);

    public void ForEach(Action<string, string> act)
    {
        foreach (var (name, path) in temps)
            act(name, path);
    }

    public static bool operator ==(TempsListInfo a, TempsListInfo b)
    {
        if (ReferenceEquals(a.temps, b.temps))
            return true;
        if (a.temps.Length != b.temps.Length)
            return false;
        for (var i = 0; i < a.temps.Length; ++i)
            if (a.temps[i] != b.temps[i])
                return false;
        return true;
    }
    public static bool operator !=(TempsListInfo a, TempsListInfo b) => !(a==b);
    public bool Equals(TempsListInfo other) => this==other;
    public override bool Equals(object? obj) => obj is TempsListInfo other && this==other;
    public override int GetHashCode() => HashCode.Combine(temps);

    public static TempsListInfo Parse(string s) =>
        new(s.Split(';').ConvertAll(p =>
        {
            var parts = p.Split('=');
            if (parts.Length != 2)
                throw new FormatException(p);
            return (parts[0], parts[1]);
        }));
    public override string ToString() => temps.Select(t => $"{t.name}={t.path}").JoinToString(';');

    static string ISettingsSaveable<TempsListInfo>.SerializeSetting(TempsListInfo setting) => setting.ToString();
    static TempsListInfo ISettingsSaveable<TempsListInfo>.DeserializeSetting(string setting) => Parse(setting);

}
