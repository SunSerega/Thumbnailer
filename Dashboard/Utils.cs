using System;

using System.Threading;

using System.Collections;
using System.Collections.Generic;

using System.Runtime.CompilerServices;

using Color = System.Windows.Media.Color;

namespace Dashboard
{

	public sealed class OneToManyLock
	{
		private readonly object sync_lock = new();
		private readonly ManualResetEventSlim one_wh = new(true);
		private readonly ManualResetEventSlim many_wh = new(true);
		private volatile int doing_one = 0;
		private volatile int doing_many = 0;

		public OneToManyLock() { }

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T OneLocked<T>(Func<T> act)
		{
			one_wh.Reset();
			// Set before fully locking so "ManyLocked"
			// will not continue incrementing "doing_many"
			// In effect gives priority to "OneLocked"
			Interlocked.Increment(ref doing_one);
			Monitor.Enter(sync_lock);
			try
			{
				while (doing_many != 0)
				{
					Monitor.Exit(sync_lock);
					many_wh.Wait();
					Monitor.Enter(sync_lock);
				}
				return act();
			}
			finally
			{
				Interlocked.Decrement(ref doing_one);
				one_wh.Set();
				Monitor.Exit(sync_lock);
			}
		}
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void OneLocked(Action act) => OneLocked(() => { act(); return 0; });

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

	public static class Utils
	{

		public static void HandleExtension(Exception e)
		{
			CustomMessageBox.Show("ERROR", e.ToString());
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

		public static T? HandleExtension<T>(Func<T> act, T? no_res = default)
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
