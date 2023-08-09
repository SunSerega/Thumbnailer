using System;

using Shell32;
using System.IO;
using System.Runtime.InteropServices;

using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;

using System.Threading;

namespace Dashboard
{

	public class ShortcutManager
	{
		private static readonly ManualResetEventSlim wh = new(false);
		private static readonly ConcurrentBag<string> targets_waiting = new();

		static ShortcutManager()
		{
			var thr = new Thread(() =>
			{
				static DateTime get_lnk_change_time(string lnk)
				{
					try
					{
						return File.GetLastWriteTimeUtc(lnk);
					}
					catch (UnauthorizedAccessException)
					{
						return DateTime.MinValue;
					}
				}

				var lnk_target_cache = new Dictionary<string, (string? target, DateTime dt)>();
				bool try_get_cached_lnk_target(string lnk, out string? target)
				{
					target = default;
					if (!lnk_target_cache.TryGetValue(lnk, out var cached))
						return false;
					if (cached.dt != get_lnk_change_time(lnk))
						return false;
					target = cached.target;
					return true;
				}

				while (true)
					try
					{
						foreach (var lnk in lnk_target_cache.Keys.Where(lnk => !File.Exists(lnk)).ToArray())
							if (!lnk_target_cache.Remove(lnk)) throw new InvalidOperationException();

						if (targets_waiting.IsEmpty)
						{
							wh.Wait();
							wh.Reset();
							continue;
						}

						var upd = new HashSet<string?>();
						while (targets_waiting.TryTake(out var target))
							upd.Add(target);

						//var lnk_upd = new List<string>();
						var shell = new Shell();
						foreach (var lnk in new ESQuary("ext:lnk"))
						{
							string? lnk_target = null;
							try
							{
								if (try_get_cached_lnk_target(lnk, out lnk_target) && !upd.Contains(lnk_target))
									continue;
								var file_item =
									shell.NameSpace(Path.GetDirectoryName(lnk))
									?.Items().Item(Path.GetFileName(lnk));
								if (file_item != null)
								{

									ShellLinkObject? lnk_item = null;
									try { lnk_item = file_item.GetLink; }
									catch (UnauthorizedAccessException) { }
									catch (COMException) { }

									if (lnk_item != null)
									{
										lnk_target = lnk_item.Path;
										if (upd.Contains(lnk_target))
										{
											lnk_item.Path = "";
											lnk_item.Save();
											lnk_item.Path = lnk_target;
											lnk_item.Save();
											//lnk_upd.Add(lnk);
										}
									}
								}
							}
							catch when (!File.Exists(lnk))
							{
								continue;
							}
							lnk_target_cache[lnk] = (lnk_target, get_lnk_change_time(lnk));
						}

						//Console.Beep();
						//TTS.Speak($"Updated {lnk_upd.Count} shortcuts: {string.Join("; ", lnk_upd)}");
					}
					catch (Exception e)
					{
						Utils.HandleException(e);
					}

			})
			{
				IsBackground = true,
				Name = $"ShortcutManager: update loop",
			};
			thr.SetApartmentState(ApartmentState.STA);
			thr.Start();
		}

		public static void UpdateFor(string target)
		{
			targets_waiting.Add(target);
			wh.Set();
		}

	}

}