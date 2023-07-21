using System;

using System.IO;

using System.Linq;
using System.Collections.Generic;

using System.Windows;

namespace Dashboard
{

	public sealed class RootSettings : Settings
	{
		public RootSettings() : base(@"Settings (Dashboard)") { }

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

		public string? LastFileChoosePath
		{
			get => GetSetting(nameof(LastFileChoosePath), null as string);
			set => SetSetting(nameof(LastFileChoosePath), value);
		}

	}

	public sealed class FileSettings : Settings
	{
		public FileSettings(string hash) : base(Path.Combine("cache", hash, @"Settings")) { }

		public string? FilePath
		{
			get => GetSetting(nameof(FilePath), null as string);
			set => SetSetting(nameof(FilePath), value);
		}

		public DateTime LastUpdate
		{
			get => GetSetting(nameof(LastUpdate), default(DateTime));
			set => SetSetting(nameof(LastUpdate), value);
		}

		public string? ThumbPath
		{
			get => GetSetting(nameof(ThumbPath), null as string);
			set => SetSetting(nameof(ThumbPath), value);
		}

		public string LastRecalcTime
		{
			//get => GetSetting(nameof(LastRecalcTime), null as string);
			set => SetSetting(nameof(LastRecalcTime), value);
		}

	}

	public abstract class Settings
	{
		private static readonly System.Text.UTF8Encoding enc = new(true);

		private readonly string main_save_fname, back_save_fname;
		private readonly DelayedUpdater resaver;

		private bool is_shut_down = false;
		public Settings(string path)
		{
			if (path.Contains('\\'))
				Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			main_save_fname = Path.GetFullPath($"{path}.dat");
			back_save_fname = Path.GetFullPath($"{path}-Backup.dat");

			resaver = new(() =>
			{
				lock (settings)
				{
					if (is_shut_down) return;
					File.Copy(main_save_fname, back_save_fname, false);
					var sw = new StreamWriter(main_save_fname, false, enc);
					foreach (var prop in this.GetType().GetProperties())
					{
						if (!settings.TryGetValue(prop.Name, out var val)) continue;
						if (!setting_type_converter.TryGetValue(prop.PropertyType, out var conv))
							throw new NotImplementedException(prop.PropertyType.ToString());
						var s = conv.save(val);
						sw.WriteLine($"{prop.Name}={s}");
					}
					sw.Close();
					File.Delete(back_save_fname);
				}
			});

			if (File.Exists(back_save_fname))
			{
				if (MessageBoxResult.Yes != MessageBox.Show("Try diff main and backup settings?", "Backup settings file exists", MessageBoxButton.YesNo))
					Environment.Exit(-1);

				System.Diagnostics.Process.Start(
					"meld", $"\"{Path.GetFullPath(main_save_fname)}\" \"{Path.GetFullPath(back_save_fname)}\""
				).WaitForExit();

				if (!File.Exists(main_save_fname))
				{
					MessageBox.Show("Settings file was not created while meld-ing", "error");
					Environment.Exit(-1);
				}

				File.Delete(back_save_fname);
			}
			if (!File.Exists(main_save_fname)) return;

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
					continue;
				}

				var prop = this.GetType().GetProperty(key) ??
					throw new InvalidOperationException($"Settings property [{key}] not found");

				if (s_val is null)
					settings.Remove(key);
				else if (setting_type_converter.TryGetValue(prop.PropertyType, out var conv))
					settings[key] = conv.load(s_val);
				else
					throw new NotImplementedException(prop.PropertyType.ToString());

			}

		}

		public string GetDir() => Path.GetDirectoryName(main_save_fname)!;

		private static KeyValuePair<Type, (Func<object, string> save, Func<string, object> load)> MakeSettingTypeConverter<T>(Func<T, string> save, Func<string, T> load) where T : notnull =>
			new(typeof(T), new( o => save((T)o), s => load(s) ));
		private static readonly Dictionary<Type, (Func<object, string> save, Func<string, object> load)> setting_type_converter = new[]
		{
			MakeSettingTypeConverter<string>		(x => x,					s => s),
			MakeSettingTypeConverter<int>			(x => x.ToString(),			Convert.ToInt32),
			MakeSettingTypeConverter<DateTime>		(x => x.Ticks.ToString(),	s => new DateTime(Convert.ToInt64(s))),
			MakeSettingTypeConverter<FileExtList>	(x => x.ToString(),			FileExtList.Parse),		
		}.ToDictionary(kvp=>kvp.Key, kvp=>kvp.Value);

		private readonly Dictionary<string, object> settings = new(StringComparer.OrdinalIgnoreCase);
		protected T? GetSetting<T>(string key, T? missing_value = default)
		{
			lock (settings)
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
			lock (settings)
			{
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
					var s = setting_type_converter[typeof(T)].save(value);
					file_line = $"{key}={s}";
				}

				if (!File.Exists(main_save_fname))
					File.WriteAllText(main_save_fname, "", enc);
				File.Copy(main_save_fname, back_save_fname, false);
				File.AppendAllLines(main_save_fname, new[] { file_line });
				File.Delete(back_save_fname);

				resaver.Trigger(TimeSpan.FromSeconds(10), true);
			}
		}

		public static RootSettings Root { get; } = new();

		public void Shutdown()
		{
			lock (settings) is_shut_down = true;
			resaver.Shutdown();
		}

	}

}
