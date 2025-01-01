using System;

using System.Linq;
using System.Collections.Generic;

using SunSharpUtils;
using SunSharpUtils.Settings;

namespace Dashboard.Settings;

public readonly struct FileExtList : ICollection<string>, IEquatable<FileExtList>, ISettingsSaveable<FileExtList>
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

    public bool Contains(string ext) => l.Contains(ext);
    public bool MatchesFile(string? fname)
    {
        var ext = System.IO.Path.GetExtension(fname);
        if (ext is null) return false;
        if (!ext.StartsWith('.')) return false;
        return Contains(ext.Remove(0, 1));
    }

    public void CopyTo(string[] array, int arrayIndex) => l.CopyTo(array, arrayIndex);

    public IEnumerator<string> GetEnumerator() => l.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => l.GetEnumerator();

    public override string ToString() => l.Order().JoinToString(';');

    static string ISettingsSaveable<FileExtList>.SerializeSetting(FileExtList setting) => setting.ToString();
    static FileExtList ISettingsSaveable<FileExtList>.DeserializeSetting(string setting) => Parse(setting);

}