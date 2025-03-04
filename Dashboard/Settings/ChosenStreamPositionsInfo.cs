using System;

using System.Linq;

using SunSharpUtils;
using SunSharpUtils.Settings;

namespace Dashboard.Settings;

public readonly struct ChosenStreamPositionsInfo : IEquatable<ChosenStreamPositionsInfo>, ISettingsSaveable<ChosenStreamPositionsInfo>
{
    private readonly Double[] pos;
    private const Double default_pos = 0.3;

    private ChosenStreamPositionsInfo(Double[] pos) => this.pos=pos;
    public static readonly ChosenStreamPositionsInfo Empty = new([]);

    public Int32 Count => pos.Length;
    public Double this[Int32 ind] => Count==0 ? default_pos : pos[ind];

    public ChosenStreamPositionsInfo WithPos(Int32 c, Int32 ind, Double val)
    {
        var res = pos.ToArray();
        if (Count == 0)
        {
            res = new Double[c];
            Array.Fill(res, default_pos);
        }
        res[ind] = val;
        return new(res);
    }

    public static ChosenStreamPositionsInfo Parse(String s) =>
        new(s.Split(';').ConvertAll(Double.Parse));
    public override String ToString() => pos.JoinToString(';');

    public static Boolean operator ==(ChosenStreamPositionsInfo a, ChosenStreamPositionsInfo b)
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
    public static Boolean operator !=(ChosenStreamPositionsInfo left, ChosenStreamPositionsInfo right) => !(left==right);
    public Boolean Equals(ChosenStreamPositionsInfo other) => this == other;
    public override Boolean Equals(Object? obj) => obj is ChosenStreamPositionsInfo other && Equals(other);

    static String ISettingsSaveable<ChosenStreamPositionsInfo>.SerializeSetting(ChosenStreamPositionsInfo setting) => setting.ToString();
    static ChosenStreamPositionsInfo ISettingsSaveable<ChosenStreamPositionsInfo>.DeserializeSetting(String setting) => Parse(setting);

    public override Int32 GetHashCode() => HashCode.Combine(pos);

}