using System;

using System.Linq;
using System.Collections.Concurrent;

using System.IO;

namespace Dashboard
{
	public class ThumbGenerator
	{
		private readonly CustomThreadPool thr_pool;
		private readonly DirectoryInfo cache_dir;
		private readonly FileStream lock_file;

		private class CachedFileInfo
		{
			private (uint u, string s) hash;
			private readonly FileSettings settings;
			//private WeakReference<Bitmap?> cached_bitmap = new(null);

			public event Action<long>? CacheSizeChanged;

			public (uint u, string s) Hash => hash;

			public CachedFileInfo(string fname, Func<uint, bool> validate_new_hash, Action<long> on_cache_changed)
			{
				CacheSizeChanged += on_cache_changed;

				if (!Path.IsPathRooted(fname))
					throw new ArgumentException($"Path [{fname}] was not rooted", nameof(fname));

				{
					var hash = 17u;
					foreach (var ch in fname)
						hash = unchecked(hash*23 + ch);
					while (!validate_new_hash(hash))
						hash = unchecked(hash * 59);
					this.hash = (hash, hash.ToString("X8"));
				}

				settings = new(hash.s)
				{
					FilePath = fname
				};

			}

			public CachedFileInfo(DirectoryInfo dir, (uint u, string s) hash)
			{
				this.hash = hash;
				settings = new(hash.s);
				if (settings.FilePath == null)
					throw new InvalidOperationException(dir.FullName);
			}

			//private const string locked_fname = @"Dashboard-Default.Locked.bmp"; // only useful in COM lib
			private const string ungenerated_fname = @"Dashboard-Default.Ungenerated.bmp";
			private const string sound_only_fname = @"Dashboard-Default.SoundOnly.bmp";

			internal void GenerateThumb(Action<Action> addJob, Action<string> ret)
			{
				var write_time = File.GetLastWriteTimeUtc(FilePath!);
				if (settings.LastUpdate != write_time)
				{
					if (File.Exists(settings.ThumbPath))
					{
						CacheSizeChanged?.Invoke(new FileInfo(settings.ThumbPath).Length);
						File.Delete(settings.ThumbPath);
					}
					settings.ThumbPath = null;
				}
				if (settings.ThumbPath != null) return;

				addJob(() =>
				{



					//TODO
					System.Threading.Thread.Sleep(1000);
					var res = Path.GetFullPath(sound_only_fname);
					// Make sure this is not mixed with .Generate call
					lock (this)
					{
						settings.ThumbPath = res;
						settings.LastUpdate = write_time;
						ret(res);
						CacheSizeChanged?.Invoke(0);
					}



				});
			}

			public string? FilePath => settings.FilePath;
			public string ThumbPath => settings.ThumbPath ?? Path.GetFullPath(ungenerated_fname);

			public void Erase()
			{
				Shutdown();
				COMManip.DeleteThumbFor(settings.FilePath!);
				var cache_dir = new DirectoryInfo( Path.GetDirectoryName(settings.GetSettingsFileName())! );
				var sz = cache_dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
				cache_dir.Delete(true);
				CacheSizeChanged?.Invoke(-sz);
			}

			public void Shutdown() => settings.Shutdown();

			//public Bitmap ThumbBitmap {
			//	get {
			//		if (!cached_bitmap.TryGetTarget(out var res))
			//			using (var str = File.OpenRead(ready_thumb_fname ?? ungenerated_fname))
			//			{
			//				res = new Bitmap(str);
			//				cached_bitmap.SetTarget(res);
			//			}
			//		return res;
			//	}
			//}

		}
		private readonly ConcurrentDictionary<string, CachedFileInfo> files = new();
		private readonly ConcurrentDictionary<uint, uint> used_hashes = new();

		public ThumbGenerator(CustomThreadPool thr_pool, string cache_dir)
		{
			this.thr_pool = thr_pool;
			this.cache_dir = Directory.CreateDirectory(cache_dir);
			this.lock_file = File.Create(Path.Combine(this.cache_dir.FullName, ".lock"));

			foreach (var dir in this.cache_dir.EnumerateDirectories())
			{
				var hash = Convert.ToUInt32(dir.Name, 16);
				var cfi = new CachedFileInfo(dir, (hash, dir.Name));
				if (!File.Exists(cfi.FilePath))
				{
					dir.Delete(true);
					continue;
				}
				if (!used_hashes.TryAdd(hash, hash))
					throw new InvalidOperationException(dir.FullName);
				if (!files.TryAdd(cfi.FilePath, cfi))
					throw new InvalidOperationException(dir.FullName);
			}

		}

		public event Action<long>? CacheSizeChanged;
		private void InvokeCacheSizeChanged(long byte_change) =>
			CacheSizeChanged?.Invoke(byte_change);

		public void Generate(string fname, Action<string> ret)
		{
			var cfi = files.GetOrAdd(fname,
				fname => new(fname,
					hash => used_hashes.TryAdd(hash, hash),
					InvokeCacheSizeChanged
				)
			);

			lock (cfi)
			{
				cfi.GenerateThumb(thr_pool.AddJob, ret);
				ret(cfi.ThumbPath);
			}

		}

		public void ClearAll()
		{
			while (files.Any())
			{
				var fname = files.Keys.First();
				if (!files.TryRemove(fname, out var cfi))
					continue;
				if (!used_hashes.TryRemove(cfi.Hash.u, out _))
					throw new InvalidOperationException();
				cfi.Erase();
			}
		}

		public void Shutdown()
		{
			foreach (var cfi in files.Values)
				cfi.Shutdown();
			lock_file.Close();
			File.Delete(Path.Combine(cache_dir.FullName, ".lock"));
		}

	}
}