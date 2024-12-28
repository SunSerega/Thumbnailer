using System;

using System.Linq;

using SunSharpUtils;
using SunSharpUtils.Settings;

namespace Dashboard.Settings;

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