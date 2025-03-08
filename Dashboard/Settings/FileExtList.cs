using System;

using System.Linq;
using System.Collections.Generic;

using SunSharpUtils;
using SunSharpUtils.Settings;

namespace Dashboard.Settings;

public readonly struct FileExtList : ICollection<String>, IEquatable<FileExtList>, ISettingsSaveable<FileExtList>
{
    private readonly HashSet<String> l = new(StringComparer.OrdinalIgnoreCase);

    public Int32 Count => l.Count;

    Boolean ICollection<String>.IsReadOnly => false;

    public FileExtList() { }

    public FileExtList(IEnumerable<String> items) => l = new(items);
    public static FileExtList Parse(String s) => new(s.Split(';'));

    public static Boolean operator ==(FileExtList l1, FileExtList l2) => l1.l.SetEquals(l2.l);
    public static Boolean operator !=(FileExtList l1, FileExtList l2) => !(l1==l2);
    public Boolean Equals(FileExtList other) => this==other;
    public override Boolean Equals(Object? obj) => obj is FileExtList other && this==other;
    public override Int32 GetHashCode() => l.GetHashCode();

    public static Boolean Validate(String ext)
    {
        // implicit in the next check
        //if (ext.Contains(';'))
        //    return false;
        //if (ext.Contains('.'))
        //    return false;
        return ext.Length!=0 && ext.All(Char.IsLetterOrDigit);
    }
    private static String Validated(String ext) =>
        Validate(ext) ? ext : throw new FormatException(ext);

    public Boolean Add(String ext) => l.Add(Validated(ext));
    public Boolean Remove(String ext) => l.Remove(Validated(ext));

    void ICollection<String>.Add(String ext) => Add(ext);

    public void Clear() => l.Clear();

    public Boolean Contains(String ext) => l.Contains(ext);
    public Boolean MatchesFile(String? fname)
    {
        var ext = System.IO.Path.GetExtension(fname);
        if (ext is null) return false;
        if (!ext.StartsWith('.')) return false;
        return Contains(ext[1..]);
    }

    public void CopyTo(String[] array, Int32 arrayIndex) => l.CopyTo(array, arrayIndex);

    public IEnumerator<String> GetEnumerator() => l.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => l.GetEnumerator();

    public override String ToString() => l.Order().JoinToString(';');

    static String ISettingsSaveable<FileExtList>.SerializeSetting(FileExtList setting) => setting.ToString();
    static FileExtList ISettingsSaveable<FileExtList>.DeserializeSetting(String setting) => Parse(setting);

}