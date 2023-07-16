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

	}

	public sealed class FileSettings : Settings
	{
		public FileSettings(string hash) : base(Path.Combine(hash, @"Settings")) { }



	}

	public abstract class Settings
	{
		private static readonly System.Text.UTF8Encoding enc = new(true);

		private readonly System.Threading.ManualResetEventSlim ev_need_resave = new(false);
		private DateTime? save_time;
		private readonly string main_save_fname, back_save_fname;
		public Settings(string path)
		{
			if (path.Contains('\\'))
				Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			main_save_fname = Path.GetFullPath($"{path}.dat");
			back_save_fname = Path.GetFullPath($"{path}-Backup.dat");

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
				if (string.IsNullOrWhiteSpace(l)) continue;
				var ind = l.IndexOf('=');
				if (ind == -1) throw new FormatException(l);

				var full_key = l.Remove(ind);
				var s_val = l.Remove(0, ind+1);

				var prop = this.GetType().GetProperty(full_key) ??
					throw new InvalidOperationException($"Settings property [{full_key}] not found");

				if (setting_type_converter.TryGetValue(prop.PropertyType, out var conv))
					settings[full_key] = conv.load(s_val);
				else
					throw new NotImplementedException(prop.PropertyType.ToString());

			}

			new System.Threading.Thread(() =>
			{
				while (true)
					try
					{
						ev_need_resave.Wait();

						while (true)
						{
							var wait_left = save_time!.Value - DateTime.Now;
							if (wait_left <= TimeSpan.Zero) break;
							System.Threading.Thread.Sleep(wait_left);
						}

						ev_need_resave.Reset();
						//Console.Beep();

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
					catch (Exception e) {
						Utils.HandleExtension(e);
					}
			}) {
				IsBackground=true
			}.Start();

		}

		private static KeyValuePair<Type, (Func<object, string> save, Func<string, object> load)> MakeSettingTypeConverter<T>(Func<T, string> save, Func<string, T> load) where T : notnull =>
			new(typeof(T), new( o => save((T)o), s => load(s) ));
		private static readonly Dictionary<Type, (Func<object, string> save, Func<string, object> load)> setting_type_converter = new[]
		{
			MakeSettingTypeConverter<string>	(x => x,            s => s),
			MakeSettingTypeConverter<int>		(x => x.ToString(),	int.Parse),
		}.ToDictionary(kvp=>kvp.Key, kvp=>kvp.Value);

		private readonly Dictionary<string, object> settings = new(StringComparer.OrdinalIgnoreCase);
		protected T GetSetting<T>(string key, T? missing_value = default) where T : notnull
		{
			lock (settings)
				if (settings.TryGetValue(key, out var value))
					return (T)value; else
				{
					if (missing_value == null)
						throw new InvalidOperationException(key);
					SetSetting(key, missing_value);
					return missing_value;
				}
		}
		protected void SetSetting<T>(string key, T value) where T: notnull
		{
			if (key.Contains('='))
				throw new FormatException(key);
			lock (settings)
			{
				settings[key] = value;
				var s = setting_type_converter[typeof(T)].save(value);
				var file_line = $"{key}={s}";

				if (!File.Exists(main_save_fname))
					File.WriteAllText(main_save_fname, "", enc);
				File.Copy(main_save_fname, back_save_fname, false);
				File.AppendAllLines(main_save_fname, new[] { $"{key}={s}" });
				File.Delete(back_save_fname);

				save_time = DateTime.Now + TimeSpan.FromSeconds(10);
				ev_need_resave.Set();
			}
		}

		public static RootSettings Root { get; } = new();
	}

}
