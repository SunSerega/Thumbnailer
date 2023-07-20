using System;

using System.Collections;
using System.Collections.Generic;

namespace Dashboard
{

	public readonly struct FileExtList : ICollection<string>, IEquatable<FileExtList>
	{
		private readonly HashSet<string> l = new();

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
			if (ext.Contains('.'))
				return false;
			if (ext.Contains(';'))
				return false;
			return true;
		}
		private static string Validated(string ext) =>
			Validate(ext) ? ext : throw new FormatException(ext);

		public bool Add(string ext) => l.Add(Validated(ext));
		public bool Remove(string ext) => l.Remove(Validated(ext));

		void ICollection<string>.Add(string ext) => Add(ext);

		public void Clear() => l.Clear();

		public bool Contains(string ext) => l.Contains(Validated(ext));

		public void CopyTo(string[] array, int arrayIndex) => l.CopyTo(array, arrayIndex);

		public IEnumerator<string> GetEnumerator() => l.GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => l.GetEnumerator();

		public override string ToString() => string.Join(';', l);

	}

	public static class Utils
	{

		public static void HandleExtension(Exception e)
		{
			System.Windows.MessageBox.Show(e.ToString());
		}

		public static void HandleExtension(Action act)
		{
			try
			{
				act();
			}
			catch (Exception e)
			{
				HandleExtension(e);
			}
		}

		public static T? HandleExtension<T>(Func<T> act, T? no_res = default) where T : notnull
		{
			try
			{
				return act();
			}
			catch (Exception e)
			{
				HandleExtension(e);
				return no_res;
			}
		}

	}

}
