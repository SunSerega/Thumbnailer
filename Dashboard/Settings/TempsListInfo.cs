using System;

using System.Linq;

using SunSharpUtils.Ext.Linq;
using SunSharpUtils.Settings;

namespace Dashboard.Settings;

public readonly struct TempsListInfo : IEquatable<TempsListInfo>, ISettingsSaveable<TempsListInfo>
{
    private readonly (String name, String path)[] temps;

    public TempsListInfo((String name, String path)[] temps)
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

    public void ForEach(Action<String, String> act)
    {
        foreach (var (name, path) in temps)
            act(name, path);
    }

    public static Boolean operator ==(TempsListInfo a, TempsListInfo b)
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
    public static Boolean operator !=(TempsListInfo a, TempsListInfo b) => !(a==b);
    public Boolean Equals(TempsListInfo other) => this==other;
    public override Boolean Equals(Object? obj) => obj is TempsListInfo other && this==other;
    public override Int32 GetHashCode() => HashCode.Combine(temps);

    public static TempsListInfo Parse(String s) =>
        new(s.Split(';').ConvertAll(p =>
        {
            var parts = p.Split('=');
            if (parts.Length != 2)
                throw new FormatException(p);
            return (parts[0], parts[1]);
        }));
    public override String ToString() => temps.Select(t => $"{t.name}={t.path}").JoinToString(';');

    static String ISettingsSaveable<TempsListInfo>.SerializeSetting(TempsListInfo setting) => setting.ToString();
    static TempsListInfo ISettingsSaveable<TempsListInfo>.DeserializeSetting(String setting) => Parse(setting);

}
