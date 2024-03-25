using System;

using System.IO;

using System.Linq;
using System.Collections.Generic;

namespace Dashboard
{

	public sealed class RootSettings : Settings
	{
		public RootSettings()
			: base("Settings (Dashboard)") { }

		protected override string SettingsDescription() => $"Root settings";

		public int MaxJobCount
		{
			get => GetSetting(nameof(MaxJobCount), 0);
			set => SetSetting(nameof(MaxJobCount), value);
		}

		public FileExtList AllowedExts
		{
			get => GetSetting(nameof(AllowedExts), new FileExtList());
			set => SetSetting(nameof(AllowedExts), value);
		}

		public string? LastComparedFile
		{
			get => GetSetting(nameof(LastComparedFile), null as string);
			set => SetSetting(nameof(LastComparedFile), value);
		}

		private static readonly ByteCount default_max_cache_size = ByteCount.Parse("700 MB");
		public ByteCount MaxCacheSize
		{
			get => GetSetting(nameof(MaxCacheSize), default_max_cache_size);
			set => SetSetting(nameof(MaxCacheSize), value);
		}

	}

	public sealed class FileSettings(string cache_file_path)
		: Settings(Path.Combine(cache_file_path, "Settings"))
	{
		protected override string SettingsDescription() => $"Settings of [{InpPath}]";

		public string? TempsListStr
		{
			get => GetSetting(nameof(TempsListStr), null as string);
			set => SetSetting(nameof(TempsListStr), value);
		}

		public string? InpPath
		{
			get => GetSetting(nameof(InpPath), null as string);
			set => SetSetting(nameof(InpPath), value);
		}

		public DateTime LastInpChangeTime
		{
			get => GetSetting(nameof(LastInpChangeTime), DateTime.MinValue);
			set => SetSetting(nameof(LastInpChangeTime), value);
		}

		public DateTime LastCacheUseTime
		{
			get => GetSetting(nameof(LastCacheUseTime), DateTime.MinValue);
			set => SetSetting(nameof(LastCacheUseTime), value);
		}
		
		public string? CurrentThumb
		{
			get => GetSetting(nameof(CurrentThumb), null as string);
			set => SetSetting(nameof(CurrentThumb), value);
		}

		public bool CurrentThumbIsFinal
		{
			get => GetSetting(nameof(CurrentThumbIsFinal), false);
			set => SetSetting(nameof(CurrentThumbIsFinal), value);
		}

		public sealed class ChosenStreamPositionsInfo
		{
			private readonly double[] pos;
			private const double default_pos = 0.3;

			private ChosenStreamPositionsInfo(double[] pos) => this.pos=pos;
			public ChosenStreamPositionsInfo(int c)
			{
				pos = new double[c];
				Array.Fill(pos, default_pos);
			}

			public int Count => pos.Length;
			public double this[int ind] { get => pos[ind]; set => pos[ind] = value; }

			public ChosenStreamPositionsInfo Resized(int c)
			{
				var res = this.pos;
				Array.Resize(ref res, c);
				if (c > this.Count)
					Array.Fill(res, default_pos, this.Count, c-this.Count);
				return new(res);
			}

			public static ChosenStreamPositionsInfo Parse(string s) =>
				new(Array.ConvertAll(s.Split(';'), double.Parse));

			public override string ToString() => string.Join(';', pos);

		}
		public ChosenStreamPositionsInfo? ChosenStreamPositions
		{
			get => GetSetting(nameof(ChosenStreamPositions), null as ChosenStreamPositionsInfo);
			set => SetSetting(nameof(ChosenStreamPositions), value);
		}

		public int? ChosenThumbOptionInd
		{
			get => GetSetting(nameof(ChosenThumbOptionInd), null as int?);
			set => SetSetting(nameof(ChosenThumbOptionInd), value);
		}

	}

	public abstract class Settings
	{
		private static readonly System.Text.UTF8Encoding enc = new(true);

		private readonly string main_save_fname, back_save_fname;

		private bool is_shut_down = false;
		public Settings(string path)
		{
			main_save_fname = Path.GetFullPath($"{path}.dat");
			back_save_fname = Path.GetFullPath($"{path}-Backup.dat");
			Directory.CreateDirectory(Path.GetDirectoryName(main_save_fname)!);
			RectifyBackup();

			var need_resave = false;
			if (File.Exists(main_save_fname))
				foreach (var l in File.ReadLines(main_save_fname, enc).Select(l => l.Trim()))
				{
					if (l == "") continue;

					var ind = l.IndexOf('=');
					if (ind == -1) throw new FormatException(l);

					var key = l.Remove(ind);
					var s_val = l.Remove(0, ind+1);

					if (key.EndsWith('!'))
					{
						key = key.Remove(key.Length-1);
						if (s_val != "")
							throw new FormatException(l);
						s_val = null;
					}

					var prop = this.GetType().GetProperty(key);
					if (prop is null)
					{
						if (!CustomMessageBox.ShowYesNo($"Settings property [{key}] not found", "Continue without it?"))
							App.Current!.Shutdown();
						continue;
					}

					need_resave = need_resave || settings.ContainsKey(key);

					var t = prop.PropertyType;
					t = Nullable.GetUnderlyingType(t) ?? t;

					if (!setting_type_converter.TryGetValue(t, out var conv))
						throw new NotImplementedException(t.ToString());

					if (s_val is null)
					{
						settings.Remove(key);
						continue;
					}

					object o_val;
					try
					{
						o_val = conv.load(s_val);
					}
					catch (FormatException)
					{
						if (CustomMessageBox.ShowYesNo($"Key=[{key}], val=[{s_val}] could not be loaded as [{t}]", $"Continue anyway?"))
							continue;
						App.Current!.Shutdown();
						throw new LoadCanceledException();
					}
					settings[key] = o_val;
				}
			// at the end, to make sure settings are fully loaded first
			if (need_resave) RequestResave(TimeSpan.Zero);

		}

		private void RectifyBackup()
		{
			if (!File.Exists(back_save_fname)) return;

			if (!File.Exists(main_save_fname) || !File.ReadLines(main_save_fname).SequenceEqual(File.ReadLines(back_save_fname)))
			{
				if (!CustomMessageBox.ShowYesNo("Backup settings file exists", $"{GetSettingsDir()}\n\nTry meld main and backup settings?"))
					throw new InvalidOperationException();

				System.Diagnostics.Process.Start(
					"meld", $"\"{Path.GetFullPath(main_save_fname)}\" \"{Path.GetFullPath(back_save_fname)}\""
				).WaitForExit();

				if (!File.Exists(main_save_fname))
				{
					CustomMessageBox.Show("Error!", "Settings file was not created while meld-ing");
					throw new InvalidOperationException();
				}
			}

			File.Delete(back_save_fname);
		}

		private void ResaveAll()
		{
			using var settings_locker = new ObjectLocker(settings);
			if (is_shut_down) return;
			RectifyBackup();
			File.Copy(main_save_fname, back_save_fname, false);

			var sw = new StreamWriter(main_save_fname, false, enc);
			foreach (var prop in this.GetType().GetProperties())
			{
				var t = prop.PropertyType;
				t = Nullable.GetUnderlyingType(t) ?? t;

				if (!settings.TryGetValue(prop.Name, out var val)) continue;
				if (!setting_type_converter.TryGetValue(t, out var conv))
					throw new NotImplementedException(t.ToString());

				var s = conv.save(val);
				sw.WriteLine($"{prop.Name}={s}");
			}
			sw.Close();

			File.Delete(back_save_fname);
			settings.TrimExcess();
		}
		private static readonly DelayedMultiUpdater<Settings> resave_updater = new(s =>
		{
			var thr_name = System.Threading.Thread.CurrentThread.Name;
			try
			{
				System.Threading.Thread.CurrentThread.Name = $"{thr_name}: {s.main_save_fname}";
				s.ResaveAll();
			}
			finally
			{
				System.Threading.Thread.CurrentThread.Name = thr_name;
			}
		}, TimeSpan.FromSeconds(0.1), "Settings resaving");
		private void RequestResave(TimeSpan delay)
		{
			resave_updater.Trigger(this, delay, true);
		}

		protected static (T1, T2) ParseSep<T1,T2>(string s, Func<string, T1> parse1, Func<string, T2> parse2, char sep = ':')
		{
			var ind = s.IndexOf(sep);
			if (ind == -1) throw new FormatException(s);
			return (parse1(s.Remove(ind)), parse2(s.Remove(0, ind+1)));
		}

		protected abstract string SettingsDescription();

		public string GetSettingsFile() => main_save_fname;
		public string GetSettingsBackupFile() => back_save_fname;
		public string GetSettingsDir() => Path.GetDirectoryName(main_save_fname)!;

		private static KeyValuePair<Type, (Func<object, string> save, Func<string, object> load)> MakeSettingTypeConverter<T>(Func<T, string> save, Func<string, T> load) where T : notnull =>
			new(typeof(T), new( o => save((T)o), s => load(s) ));
		private static readonly Dictionary<Type, (Func<object, string> save, Func<string, object> load)> setting_type_converter = new[]
		{
			MakeSettingTypeConverter(x => x,					s => s),
			MakeSettingTypeConverter(x => x?"1":"0",			s => s!="0"),
			MakeSettingTypeConverter(x => x.ToString(),         Convert.ToInt32),
			MakeSettingTypeConverter(x => x.Ticks.ToString(),   s => new DateTime(Convert.ToInt64(s))),
			MakeSettingTypeConverter(x => x.Ticks.ToString(),   s => new TimeSpan(Convert.ToInt64(s))),
			MakeSettingTypeConverter(x => x.ToString(),         FileExtList.Parse),
			MakeSettingTypeConverter(x => x.ToString(),         ByteCount.Parse),
			MakeSettingTypeConverter(x => x.ToString(),         FileSettings.ChosenStreamPositionsInfo.Parse),
		}.ToDictionary(kvp=>kvp.Key, kvp=>kvp.Value);

		private readonly Dictionary<string, object> settings = new(StringComparer.OrdinalIgnoreCase);
		protected T? GetSetting<T>(string key, T? missing_value = default)
		{
			using var settings_locker = new ObjectLocker(settings);
			if (settings.TryGetValue(key, out var value))
				return (T)value;
			else
			{
				if (missing_value != null)
					SetSetting(key, missing_value);
				return missing_value;
			}
		}
		protected void SetSetting<T>(string key, T? value)
		{
			if (key.Contains('='))
				throw new FormatException(key);
			using var settings_locker = new ObjectLocker(settings);

			// Allow generation to finish after shutdown
			//if (is_shut_down) throw new System.Threading.Tasks.TaskCanceledException();

#pragma warning disable CS8620 // settings value type is "object" instead of "object?"
			var old_value = settings.GetValueOrDefault(key, null);
#pragma warning restore CS8620
			if (old_value is null ? value is null : EqualityComparer<T>.Default.Equals((T)old_value, value))
				return;

			string file_line;
			if (value is null)
			{
				settings.Remove(key);
				file_line = $"{key}!=";
			}
			else
			{
				settings[key] = value;
				var t = typeof(T);
				var s = setting_type_converter[Nullable.GetUnderlyingType(t)??t].save(value);
				file_line = $"{key}={s}";
			}

			if (!File.Exists(main_save_fname))
				File.WriteAllText(main_save_fname, "", enc);
			RectifyBackup();
			File.Copy(main_save_fname, back_save_fname, false);
			File.AppendAllLines(main_save_fname, [file_line]);
			File.Delete(back_save_fname);

			RequestResave(TimeSpan.FromSeconds(10));
		}

		public static RootSettings Root { get; } = new();

		public void Shutdown()
		{
			//using var settings_locker = new ObjectLocker(settings);
			is_shut_down = true;
		}

	}

}
