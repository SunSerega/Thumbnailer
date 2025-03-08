using System;

using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;

using System.IO;

using Thread = System.Threading.Thread;
using Interlocked = System.Threading.Interlocked;

using System.Xml.Linq;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using SunSharpUtils;
using SunSharpUtils.Settings;
using SunSharpUtils.Threading;

using Dashboard.Settings;

namespace Dashboard;

//TODO Split all this nesting
public class ThumbGenerator
{
    private readonly CustomThreadPool thr_pool;
    private readonly String internal_files_base;
    private readonly DirectoryInfo cache_dir;
    private readonly FileStream lock_file;

    private readonly ConcurrentDictionary<String, CachedFileInfo> files = new();

    #region Load

    private sealed class CacheFileLoadCanceledException(String? message) : Exception(message) { }

    public ThumbGenerator(CustomThreadPool thr_pool, String cache_dir, Action<String?> change_subjob)
    {
        this.thr_pool = thr_pool;
        this.cache_dir = Directory.CreateDirectory(cache_dir);
        this.internal_files_base = Path.GetDirectoryName(this.cache_dir.FullName) + Path.DirectorySeparatorChar;
        this.lock_file = File.Create(Path.Combine(this.cache_dir.FullName, ".lock"));

        var dirs = this.cache_dir.GetDirectories();

        var all_file_paths = new HashSet<String>();
        var purge_acts = new List<Action>();
        var failed_load = new List<(String id, String message)>();
        var conflicting_caches = new Dictionary<String, List<UInt32>>();

        for (var i = 0; i < dirs.Length; ++i)
        {
            if (Common.IsShuttingDown) return;
            change_subjob($"Loaded {i}/{dirs.Length}");
            var dir = dirs[i];
            Action? purge_act = () => dir.Delete(true);
            try
            {
                if (!UInt32.TryParse(dir.Name, out var id))
                    throw new CacheFileLoadCanceledException($"1!Invalid ID");
                var cfi = new CachedFileInfo(this, id, dir.FullName, InvokeCacheSizeChanged);
                purge_act = cfi.Erase;
                var path = cfi.InpPath ??
                    throw new CacheFileLoadCanceledException($"2!No file is assigned");
                // will be deleted by cleanup
                //if (!File.Exists(path))
                //    throw new CacheFileLoadCanceledException($"3!File [{path}] does not exist");
                if (path.StartsWith(internal_files_base))
                    throw new CacheFileLoadCanceledException($"4!Cache referes to cache internals: [{path}]");
                if (!all_file_paths.Add(path))
                {
                    if (!conflicting_caches.TryGetValue(path, out var l))
                    {
                        if (!files.TryRemove(path, out var old_cfi))
                            throw new InvalidOperationException();
                        purge_acts.Add(old_cfi.Erase);
                        l = [old_cfi.Id];
                        conflicting_caches[path] = l;
                    }
                    l.Add(id);
                    throw new CacheFileLoadCanceledException("");
                }
                if (!files.TryAdd(path, cfi))
                    throw new InvalidOperationException();
                // skip if thumb was already fully generated
                // even if it got deleted later, to save space
                if (!cfi.CurrentThumbIsFinal)
                    cfi.GenerateThumb("Init check", null, null, false, false);
                purge_act = null;
            }
            catch (SettingsLoadUserAbortedException)
            {
                Environment.Exit(-1);
            }
            catch (CacheFileLoadCanceledException e)
            {
                if (purge_act is null)
                    throw new InvalidOperationException();
                purge_acts.Add(purge_act);
                if (!String.IsNullOrEmpty(e.Message))
                    failed_load.Add((dir.Name, e.Message));
            }
        }
        change_subjob(null);

        Action? before_cleanup_loop = null;
        if (purge_acts.Count!=0)
        {
            change_subjob("Purge");

            var lns = new List<String>();
            void header(String h)
            {
                if (lns.Count!=0)
                    lns.Add("");
                lns.Add(h);
            }

            if (failed_load.Count!=0)
            {
                header("Id-s failed to load");
                foreach (var g in failed_load.GroupBy(t => t.message).OrderBy(g => g.Key))
                {
                    var message = g.Key;
                    var ids = g.Select(t => t.id).Order();
                    lns.Add($"{ids.JoinToString(',')}: {message}");
                }
                lns.Add(new String('=', 30));
            }

            if (conflicting_caches.Count!=0)
            {
                header("Id-s referred to the same file");
                foreach (var (path, ids) in conflicting_caches)
                    lns.Add($"[{path}]: {ids.JoinToString(',')}");
                lns.Add(new String('=', 30));
            }

            header("Purge all the deviants?");

            if (!Prompt.AskYesNo("Settings load had issues", lns.JoinToString(Environment.NewLine)))
            {
                Common.Shutdown(-1);
                return;
            }

            // File deletion can be suspended by the system,
            // while explorer is waiting for thumb
            before_cleanup_loop += () => Err.Handle(() =>
            {
                foreach (var purge_act in purge_acts) purge_act();
            });

            change_subjob(null);
        }

        new Thread(() =>
        {
            before_cleanup_loop?.Invoke();
            while (true)
                try
                {
                    Thread.Sleep(TimeSpan.FromMinutes(1));
                    ClearInvalid();
                    ClearExtraFiles();
                }
                catch (Exception e)
                {
                    Err.Handle(e);
                }
        })
        {
            IsBackground = true,
            Name = $"Cleanup of [{cache_dir}]",
        }.Start();

    }

    #endregion

    #region ThumbSource

    public sealed class ThumbSource(
        String name,
        TimeSpan len,
        Func<Double, Action<String?>, Action<Double>?, String> extract
    ) {
        public String Name => name;

        public TimeSpan Length => len;

        private Double last_pos = Double.NaN;
        private String? last_otp_fname = null;

        public String Extract(Boolean force_regen, Double pos, Action<String?> change_subjob, Action<Double>? on_pre_extract_progress)
        {
            if (pos<0 || pos>1)
                throw new InvalidOperationException();

            if (!force_regen && pos==last_pos)
                return last_otp_fname ?? throw null!;

            var otp_fname = extract(pos, change_subjob, on_pre_extract_progress);

            last_otp_fname = otp_fname;
            last_pos = pos;

            return otp_fname;
        }

    }

    #endregion

    #region CachedFileInfo

    public interface ICachedFileInfo
    {

        public String? InpPath { get; }
        public String CurrentThumbPath { get; }

        public BitmapImage CurrentThumbBmp {
            get
            {
                using var this_locker = new ObjectLocker(this);
                return BitmapUtils.LoadUncached(CurrentThumbPath);
            }
        }

        public IReadOnlyList<ThumbSource> ThumbSources { get; }
        public Int32 ChosenThumbOptionInd { get; }

        public void ApplySourceAt(Boolean force_regen, Action<String?> change_subjob, Int32 ind, in Double? in_pos, out Double out_pos, Action<Double>? on_pre_extract_progress);

        public sealed class CacheUse(
            ICachedFileInfo cfi,
            String cause,
            Func<Boolean> is_freed_check
        ) : IDisposable
        {
            public CacheUse Dupe() => cfi.BeginUse(cause, is_freed_check);

            public ICachedFileInfo CFI => cfi;
            public String Cause => cause;

            public Boolean IsFreed => disposed || is_freed_check();

            private Boolean disposed = false;
            public void Dispose() => cfi.EndUse(this, () =>
            {
                if (disposed)
                    throw new InvalidOperationException($"{this} was disposed too much");
                disposed = true;
            });

            public Boolean TryLetGoUndisposed()
            {
                if (disposed) throw new InvalidOperationException();
                if (!IsFreed) return false;
                Log.Append($"{this} was free, but not properly disposed");
                return true;
            }

            public override String ToString() => $"CacheUse[{cause}] for [{cfi.InpPath}]";

        }
        public CacheUse BeginUse(String cause, Func<Boolean> is_freed_check);
        public void EndUse(CacheUse use, Action? finish_while_locked);

    }

    private sealed class IdentityCFI(String fname) : ICachedFileInfo
    {
        public String? InpPath => fname;
        public String CurrentThumbPath => fname;

        public IReadOnlyList<ThumbSource> ThumbSources => [new ThumbSource(fname, default, (_,_,_)=>fname)];
        public Int32 ChosenThumbOptionInd => 0;

        public void ApplySourceAt(Boolean force_regen, Action<String?> change_subjob, Int32 ind, in Double? in_pos, out Double out_pos, Action<Double>? on_pre_extract_progress) =>
            throw new NotImplementedException();

        public ICachedFileInfo.CacheUse BeginUse(String cause, Func<Boolean> is_freed_check) => new(this, cause, is_freed_check);
        public void EndUse(ICachedFileInfo.CacheUse use, Action? finish_while_locked) => finish_while_locked?.Invoke();

    }

    private sealed class CachedFileInfo : ICachedFileInfo
    {
        private readonly ThumbGenerator gen;
        private readonly UInt32 id;
        private readonly FileSettings settings;

        #region Constructors

        public CachedFileInfo(ThumbGenerator gen, UInt32 id, String cache_path, Action<Int64> on_cache_changed)
        {
            if (!Path.IsPathRooted(cache_path))
                throw new ArgumentException($"Path [{cache_path}] was not rooted", nameof(cache_path));
            if (!Directory.Exists(cache_path))
                throw new InvalidOperationException();
            this.gen = gen;
            this.id = id;
            CacheSizeChanged += on_cache_changed;
            settings = new(cache_path);

            temps = new(this);
            temps.InitRoot();
        }

        public CachedFileInfo(ThumbGenerator gen, UInt32 id, String cache_path, Action<Int64> on_cache_changed, String target_fname)
            : this(gen, id, cache_path, on_cache_changed)
        {
            if (!Path.IsPathRooted(target_fname))
                throw new ArgumentException($"Path [{target_fname}] was not rooted", nameof(target_fname));
            settings.InpPath = target_fname;
        }

        #endregion

        #region Properties

        public UInt32 Id => id;
        public String? InpPath => settings.InpPath;
        public Boolean CanClearTemps => temps.CanClear;
        public DateTime LastCacheUseTime => settings.LastCacheUseTime;
        public String CurrentThumbPath => Path.Combine(settings.GetSettingsDir(), settings.CurrentThumb ??
            throw new InvalidOperationException("Should not have been called before exiting .GenerateThumb")
        );
        public Boolean CurrentThumbIsFinal => settings.CurrentThumbIsFinal;
        public Int32 ChosenThumbOptionInd => settings.ChosenThumbOptionInd ??
            throw new InvalidOperationException("Should not have been called before exiting .GenerateThumb");
        private Boolean error_generating = false;
        public Boolean IsDeletable =>
            !File.Exists(InpPath) ||
            !GlobalSettings.Instance.AllowedExts.MatchesFile(InpPath) ||
            (error_generating && LastCacheUseTime+TimeSpan.FromSeconds(30) < DateTime.UtcNow);

        #endregion

        #region ThumbSources

        private ThumbSource[]? thumb_sources;
        public IReadOnlyList<ThumbSource> ThumbSources => thumb_sources ??
            throw new InvalidOperationException("Should not have been called before handling GenerateThumb::on_regenerated");

        private void SetTempSource(ThumbSource source)
        {
            settings.CurrentThumb = source.Extract(false,0.3, null!, null);
            COMManip.ResetThumbFor(InpPath, TimeSpan.Zero);
        }
        private void SetSources(Action<String?> change_subjob, ThumbSource[] thumb_sources)
        {
            if (settings.ChosenStreamPositions.Count != 0 && settings.ChosenStreamPositions.Count != thumb_sources.Length)
                settings.ChosenStreamPositions = ChosenStreamPositionsInfo.Empty;
            this.thumb_sources = thumb_sources;
            ApplySourceAt(true, change_subjob, settings.ChosenThumbOptionInd??thumb_sources.Length-1, null, out _, null);
        }
        private void ResetSources()
        {
            thumb_sources = null;
            //if (run_gc) GC.Collect();
        }

        public void ApplySourceAt(Boolean force_regen, Action<String?> change_subjob, Int32 ind, in Double? in_pos, out Double out_pos, Action<Double>? on_pre_extract_progress)
        {
            using var this_locker = new ObjectLocker(this);
            var source = ThumbSources[ind];

            Double pos = in_pos ?? settings.ChosenStreamPositions[ind];
            out_pos = pos;

            String res;
            try
            {
                res = source.Extract(force_regen, pos, change_subjob, on_pre_extract_progress);
            }
            catch when (this.IsDeletable)
            {
                return;
            }

            settings.ChosenThumbOptionInd = ind;
            if (settings.ChosenStreamPositions[ind] != pos)
                settings.ChosenStreamPositions = settings.ChosenStreamPositions.WithPos(ThumbSources.Count, ind, pos);

            var base_path = settings.GetSettingsDir() + @"\";
            if (res.StartsWith(base_path))
                res = res[base_path.Length..];

            settings.CurrentThumb = res;
            COMManip.ResetThumbFor(InpPath, on_pre_extract_progress is null ? TimeSpan.Zero : TimeSpan.FromSeconds(0.1));
        }

        #endregion

        #region UseList

        private ConcurrentDictionary<ICachedFileInfo.CacheUse, Byte> use_list = new();
        private readonly OneToManyLock use_list_lock = new();

        public ICachedFileInfo.CacheUse BeginUse(String cause, Func<Boolean> is_freed_check) => use_list_lock.ManyLocked(() =>
        {
            var use = new ICachedFileInfo.CacheUse(this, cause, is_freed_check);
            if (!use_list.TryAdd(use, 0))
                throw new InvalidOperationException();
            return use;
        });
        public void EndUse(ICachedFileInfo.CacheUse use, Action? finish_while_locked) => use_list_lock.ManyLocked(() =>
        {
            if (!use_list.TryRemove(use, out _))
                throw new InvalidOperationException($"Cache[{Id}] use for [{InpPath}] ({use.Cause}) was already freed");
            finish_while_locked?.Invoke();
            if (use_list.IsEmpty)
            {
                // No .TrimExcess
                // But should be fine, because
                // .BeginUse would be invalid after this .EndUse
                use_list = new();
                ResetSources();
            }
        });

        private Boolean TryEmptyUseList(Action act) => use_list_lock.OneLocked(() =>
        {

            var use_list = new List<ICachedFileInfo.CacheUse>(this.use_list.Count);
            var undisposed = 0;
            foreach (var use in this.use_list.Keys)
            {
                if (use.TryLetGoUndisposed())
                {
                    ++undisposed;
                    continue;
                }
                use_list.Add(use);
            };
            if (undisposed != 0)
                TTS.Speak($"ThumbDashboard: {undisposed} uses of cache were not properly disposed");

            this.use_list.Clear();
            if (use_list.Count != 0)
            {
                foreach (var u in use_list)
                    if (!this.use_list.TryAdd(u, 0))
                        throw new InvalidOperationException();
                return false;
            }

            act();
            return true;
        }, with_priority: true);

        #endregion

        #region Temps

        public event Action<Int64>? CacheSizeChanged;
        private void InvokeCacheSizeChanged(Int64 change) =>
            CacheSizeChanged?.Invoke(change);

        private sealed class GenerationTemp(String path, Action<String>? on_unload) : IDisposable
        {
            public String Path => path;

            public Boolean IsDeletable => on_unload != null;

            public void Dispose()
            {
                if (on_unload is null) return;
                for (Int32 i = 1; ; ++i)
                    try
                    {
                        on_unload(path);
                        break;
                    }
                    catch (Exception e)
                    {
                        if (i%100 == 0)
                            Prompt.Notify($"Struggling to delete [{path}]", e.ToString());
                        Thread.Sleep(10);
                    }
            }

        }

        private static Int64 FileSize(String fname) => new FileInfo(fname).Length;
        private Boolean DeleteFile(String fname)
        {
            if (!fname.StartsWith(settings.GetSettingsDir()))
                throw new InvalidOperationException(fname);
            using var this_locker = new ObjectLocker(this);
            if (!File.Exists(fname))
                return false;
            var sz = FileSize(fname);
            File.Delete(fname);
            InvokeCacheSizeChanged(-sz);
            return true;
        }

        private static Int64 DirSize(String dir) => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(FileSize);
        private Boolean DeleteDir(String dir)
        {
            if (!dir.StartsWith(settings.GetSettingsDir()))
                throw new InvalidOperationException(dir);
            using var this_locker = new ObjectLocker(this);
            if (!Directory.Exists(dir))
                return false;
            var sz = DirSize(dir);
            Directory.Delete(dir, true);
            InvokeCacheSizeChanged(-sz);
            return true;
        }

        private sealed class LocalTempsList(CachedFileInfo cfi) : IDisposable
        {
            private readonly Dictionary<String, GenerationTemp> d = [];

            private Boolean IsRoot => cfi.temps == this;
            private void OnChanged()
            {
                if (!IsRoot) return;
                var common_path = cfi.settings.GetSettingsDir() + @"\";
                cfi.settings.TempsList = new(d
                    .Where(kvp => kvp.Value.IsDeletable)
                    .Select(kvp =>
                    {
                        var path = kvp.Value.Path;
                        if (!path.StartsWith(common_path))
                            throw new InvalidOperationException(path);
                        path = path[common_path.Length..];
                        return (kvp.Key, path);
                    })
                    .ToArray()
                );
                //var has_thumb_temp = false;
                //cfi.settings.TempsList.ForEach((name,path) =>
                //{
                //    if (name != "thumb file") return;
                //    has_thumb_temp = true;
                //});
                //if (!has_thumb_temp && File.Exists(Path.Combine(cfi.settings.GetSettingsDir(), "thumb.png")))
                //{
                //    throw new InvalidOperationException("Thumb temp was deleted for some reason");
                //}
            }

            private GenerationTemp AddExisting(String temp_name, GenerationTemp temp)
            {
                if ("=;".Any(temp_name.Contains))
                    throw new FormatException(temp_name);
                if (";".Any(temp.Path.Contains))
                    throw new FormatException(temp.Path);
                d.Add(temp_name, temp);
                OnChanged();
                return temp;
            }
            private GenerationTemp AddNew(String temp_name, GenerationTemp temp)
            {
                // delete last gen leftovers
                temp.Dispose();
                return AddExisting(temp_name, temp);
            }
            public void InitRoot()
            {
                if (!IsRoot) throw new InvalidOperationException();

                if (d.Count!=0) throw new InvalidOperationException();
                d.Add("settings", new GenerationTemp(cfi.settings.GetSettingsFile(), null));
                d.Add("settings backup", new GenerationTemp(cfi.settings.GetSettingsBackupFile(), null));

                cfi.settings.TempsList.ForEach((temp_name, temp_path) =>
                {
                    temp_path = Path.Combine(cfi.settings.GetSettingsDir(), temp_path);
                    if (d.TryGetValue(temp_name, out var old_temp))
                    {
                        if (old_temp.IsDeletable || old_temp.Path != temp_path)
                            throw new InvalidOperationException($"{cfi.settings.GetSettingsDir()}: {temp_name}");
                        return;
                    }
                    GenerationTemp temp;
                    if (File.Exists(temp_path))
                        temp = new(temp_path, fname => cfi.DeleteFile(fname));
                    else if (Directory.Exists(temp_path))
                        temp = new(temp_path, dir => cfi.DeleteDir(dir));
                    else
                        return;
                    d.Add(temp_name, temp);
                });

                //if (!d.ContainsKey("thumb file"))
                //{
                //    var fname = Path.Combine(cfi.settings.GetSettingsDir(), "thumb.png");
                //    if (File.Exists(fname))
                //        //throw new InvalidOperationException($"Thumb temp was deleted for some reason:\n{cfi.settings.GetSettingsFile()}");
                //        d.Add("thumb file", new GenerationTemp(fname, fname => cfi.DeleteFile(fname)));
                //}

                OnChanged();
            }
            public GenerationTemp AddFile(String temp_name, String fname) => AddNew(temp_name, new GenerationTemp(
                Path.Combine(cfi.settings.GetSettingsDir(), fname),
                fname => cfi.DeleteFile(fname)
            ));
            public GenerationTemp AddDir(String temp_name, String dir) => AddNew(temp_name, new GenerationTemp(
                Path.Combine(cfi.settings.GetSettingsDir(), dir),
                dir => cfi.DeleteDir(dir)
            ));

            public GenerationTemp? TryRemove(String temp_name)
            {
                if (!d.Remove(temp_name, out var t))
                    return null;
                t.Dispose();
                OnChanged();
                return t;
            }

            public Int32 DeleteExtraFiles()
            {
                if (!IsRoot) throw new InvalidOperationException();
                d.TrimExcess();
                var non_del = new HashSet<String>(d.Count);
                foreach (var t in d.Values)
                    if (!non_del.Add(t.Path))
                        throw new InvalidOperationException();

                Int32 on_dir(String dir)
                {
                    var res = 0;

                    foreach (var subdir in Directory.EnumerateDirectories(dir))
                    {
                        if (non_del.Contains(subdir)) continue;
                        res += on_dir(subdir);
                        if (!Directory.EnumerateFileSystemEntries(subdir).Any())
                            Directory.Delete(subdir, recursive: false);
                    }

                    foreach (var fname in Directory.EnumerateFiles(dir))
                    {
                        if (non_del.Contains(fname)) continue;
                        res += 1;
                        File.Delete(fname);
                        Log.Append($"Deleted extra file [{fname}]");
                    }

                    return res;
                }
                return on_dir(cfi.settings.GetSettingsDir());
            }

            public void GiveToCFI(String temp_name)
            {
                if (IsRoot) throw new InvalidOperationException();
                if (!d.Remove(temp_name, out var t))
                    throw new InvalidOperationException(temp_name);
                cfi.temps.AddExisting(temp_name, t);
            }

            public Boolean CanClear => d.Values.Any(t=>t.IsDeletable);
            public void Clear()
            {
                foreach (var (temp_name,_) in d.Where(kvp=>kvp.Value.IsDeletable).ToArray())
                {
                    if (TryRemove(temp_name) is null)
                        throw new InvalidOperationException();
                }
            }
            public void VerifyEmpty()
            {
                if (!CanClear) return;
                throw new InvalidOperationException();
            }

            public void Dispose()
            {
                if (IsRoot)
                    throw new InvalidOperationException();
                if (d.Values.Any(t => !t.IsDeletable))
                    throw new InvalidOperationException();
                try
                {
                    foreach (var temp in d.Values)
                        temp.Dispose();
                }
                catch (Exception e)
                {
                    Err.Handle(e);
                    // unrecoverable, but also unimaginable
                    Console.Beep(); Console.Beep(); Console.Beep();
                    Common.Shutdown(-1);
                }
            }

        }
        private readonly LocalTempsList temps;

        public Int32 DeleteExtraFiles()
        {
            using var this_locker = ObjectLocker.TryLock(this);
            if (this_locker is null) return 0;
            return temps.DeleteExtraFiles();
        }

        public Boolean TryClearTemps()
        {
            using var this_locker = ObjectLocker.TryLock(this);
            if (this_locker is null) return false;
            return TryEmptyUseList(() =>
            {
                settings.LastInpChangeTime = DateTime.MinValue;
                settings.LastCacheUseTime = DateTime.MinValue;
                settings.CurrentThumb = null;
                temps.Clear();
            });
        }

        #endregion

        #region Generate

        private static class CommonThumbSources
        {
            private static ThumbSource Make(String name) {
                var full_path = Path.GetFullPath($"Dashboard-Default.{name}.bmp");
                if (!File.Exists(full_path))
                    throw new InvalidOperationException(name);
                return new(name, TimeSpan.Zero, (_,_,_)=>full_path);
            }

            public static ThumbSource Ungenerated { get; } = Make("Ungenerated");
            public static ThumbSource Waiting { get; } = Make("Waiting");
            //public static ThumbSource Locked { get; } = Make("Locked");
            public static ThumbSource SoundOnly { get; } = Make("SoundOnly");
            public static ThumbSource Broken { get; } = Make("Broken");

        }

        private (String? cause, Action<ICachedFileInfo>? on_regenerated, Boolean force_regen) delayed_gen_args = default;
        private static readonly DelayedMultiUpdater<CachedFileInfo> delayed_gen = new(cfi =>
        {
            var (cause, on_regenerated, force_regen) = cfi.delayed_gen_args;
            if (cause is null) return;
            cfi.delayed_gen_args = default;
            cfi.GenerateThumb($"{cause} (delayed)", null, on_regenerated, force_regen, false);
        }, "Thumb gen wait (input recently modified)", is_background: false);

        private volatile Int32 gen_jobs_assigned = 0;
        private CustomThreadPool.ThreadPoolJobHeader? gen_job_obj = null;
        private readonly ConcurrentQueue<Action<Action<String?>>> gen_acts = new();

        public ICachedFileInfo.CacheUse? GenerateThumb(
            String cause, Func<Boolean>? is_unused_check,
            Action<ICachedFileInfo>? on_regenerated,
            Boolean force_regen, Boolean set_cache_use_time)
        {
            ObjectLocker? TryLockThis()
            {
                if (on_regenerated is not null)
                    return new ObjectLocker(this);
                while (true)
                {
                    var l = ObjectLocker.TryLock(this);
                    if (l is not null) return l;
                    if (gen_jobs_assigned != 0) return null;
                }
            }

            ICachedFileInfo.CacheUse? make_cache_use()
            {
                if (is_unused_check is null)
                {
                    //ResetSources(false);
                    return null;
                }
                return BeginUse(cause, is_unused_check);
            }

            if (is_erased) return null;
            using var this_locker = TryLockThis();
            if (is_erased) return null;
            if (this_locker is null) return make_cache_use();

            var inp_fname = settings.InpPath ?? throw new InvalidOperationException();
            var otp_temp_name = "thumb file";

            if (!File.Exists(inp_fname))
            {
                Log.Append($"Asked thumb for missing file [{inp_fname}]");
                SetTempSource(CommonThumbSources.Broken);
                return make_cache_use();
            }

            if (set_cache_use_time)
                settings.LastCacheUseTime = DateTime.UtcNow;
            var write_time = new[]{
                File.GetLastWriteTimeUtc(inp_fname),
                // this can get stuck accessing input file to make thumb,
                // then resetting it to make it update - which triggers another regen
                //File.GetLastAccessTimeUtc(inp_fname),
                new FileInfo(inp_fname).CreationTimeUtc,
            }.Max();

            if (settings.CurrentThumbIsFinal && (settings.CurrentThumb==null || !File.Exists(CurrentThumbPath)))
                settings.CurrentThumbIsFinal = false;

            if (!force_regen && settings.LastInpChangeTime == write_time && settings.CurrentThumbIsFinal)
                return make_cache_use();

            {
                var total_wait = TimeSpan.FromSeconds(5);
                var waited = DateTime.UtcNow-write_time;
                // Too recently updated, wait for more updates
                // (to avoid trying to regen file currently being torrented)
                if (total_wait > waited)
                {
                    temps.TryRemove(otp_temp_name);
                    temps.VerifyEmpty();
                    settings.CurrentThumbIsFinal = false;
                    SetTempSource(CommonThumbSources.Waiting);
                    delayed_gen_args = (cause, on_regenerated, force_regen);
                    delayed_gen.TriggerPostpone(this, total_wait-waited);
                    return make_cache_use();
                }
            }
            
            temps.TryRemove(otp_temp_name);
            temps.VerifyEmpty();
            settings.CurrentThumbIsFinal = false;
            SetTempSource(CommonThumbSources.Ungenerated);

            if (on_regenerated!=null || gen_acts.IsEmpty)
                gen_acts.Enqueue(change_subjob =>
                {
                    // For now have removed the setting for this
                    //var sw = System.Diagnostics.Stopwatch.StartNew();

                    var sources = new List<ThumbSource>();
                    ThumbSource[]? curr_gen_thumb_sources = null;

                    change_subjob("getting metadata");
                    var ffmpeg = FFmpeg.Invoke($"-i \"{inp_fname}\" -hide_banner -show_format -show_streams -count_packets -print_format xml", () => true, exe: "probe");
                    var metadata_s = ffmpeg.Output!;
                    change_subjob(null);

                    // Set below, after processing all streams
                    // Because sanity checks of format xml rely on sources list
                    TimeSpan? global_dur = null;
                    var global_dur_s = "";
                    void apply_dur(String inp_fname, Action? inp_dispose, String otp_fname, Action<String?> change_subjob)
                    {
                        change_subjob("apply dur: load bg image");
                        Size sz;
                        var bg_im = new Image();
                        {
                            var bg_im_source = BitmapUtils.LoadUncached(inp_fname);
                            sz = new(bg_im_source.Width, bg_im_source.Height);
                            if (sz.Width>256 || sz.Height>256)
                                throw new InvalidOperationException();
                            bg_im.Source = bg_im_source;
                        }
                        inp_dispose?.Invoke();
                        change_subjob(null);

                        change_subjob("apply dur: compose output");
                        var res_c = new Grid();
                        res_c.Children.Add(bg_im);
                        res_c.Children.Add(new Viewbox
                        {
                            Opacity = 0.6,
                            Width = sz.Width,
                            Height = sz.Height*0.2,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            VerticalAlignment = VerticalAlignment.Center,
                            Child = new Image
                            {
                                Source = new DrawingImage
                                {
                                    Drawing = new GeometryDrawing
                                    {
                                        Brush = Brushes.Black,
                                        Pen = new Pen(Brushes.White, 0.08),
                                        Geometry = new FormattedText(
                                            global_dur_s,
                                            System.Globalization.CultureInfo.InvariantCulture,
                                            FlowDirection.LeftToRight,
                                            new Typeface(
                                                new TextBlock().FontFamily,
                                                FontStyles.Normal,
                                                FontWeights.ExtraBold,
                                                FontStretches.Normal
                                            ),
                                            1,
                                            Brushes.Black,
                                            96
                                        ).BuildGeometry(default)
                                    }
                                }
                            },
                        });
                        change_subjob(null);

                        change_subjob("apply dur: render output");
                        var bitmap = new RenderTargetBitmap((Int32)sz.Width, (Int32)sz.Height, 96, 96, PixelFormats.Pbgra32);
                        res_c.Measure(sz);
                        res_c.Arrange(new(sz));
                        bitmap.Render(res_c);
                        change_subjob(null);

                        change_subjob("apply dur: save output");
                        var enc = new PngBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(bitmap));
                        using (Stream fs = File.Create(otp_fname))
                            enc.Save(fs);
                        InvokeCacheSizeChanged(+FileSize(otp_fname));
                        change_subjob(null);

                    }

                    try
                    {
                        change_subjob("parsing metadata XML");
                        var metadata_xml = XDocument.Parse(metadata_s).Root!;
                        change_subjob(null);

                        var any_dur = false; // be it video or audio
                        if (metadata_xml.Descendants("streams").SingleOrDefault() is XElement streams_xml)
                            foreach (var stream_xml in streams_xml.Descendants("stream"))
                            {
                                var stream_ind = Int32.Parse(stream_xml.Attribute("index")!.Value);
                                change_subjob($"checking stream#{stream_ind}");

                                var codec_type_s = stream_xml.Attribute("codec_type")!.Value;

                                String? get_tag(String key) =>
                                    stream_xml.Descendants("tag").SingleOrDefault(n => n.Attribute("key")!.Value == key)?.Attribute("value")!.Value;

                                var tag_mimetype = get_tag("mimetype");

                                var is_attachment = codec_type_s == "attachment";
                                if (is_attachment)
                                {
                                    if (tag_mimetype == null)
                                        throw new InvalidOperationException();
                                }
                                else // !is_attachment
                                {
                                    // C:\0\Music\3Sort\!fix\Selulance (soundcloud)\[20150103] Selulance - What.mkv
                                    //if (stream_is_image)
                                    //    // Should work, but throw to find such file
                                    //    throw new NotImplementedException(inp_fname);
                                }

                                var mimetype_is_image = tag_mimetype?.StartsWith("image/");
                                if (mimetype_is_image==false && is_attachment) continue;

                                var frame_count_s = stream_xml.Attribute("nb_read_packets")?.Value;
                                Int32 frame_count;
                                if (is_attachment)
                                {
                                    if (frame_count_s != null)
                                        throw new NotImplementedException();
                                    frame_count = 1;
                                }
                                else
                                {
                                    if (frame_count_s is null)
                                        continue;
                                    frame_count = Int32.Parse(frame_count_s);
                                }

                                var stream_is_image = frame_count == 1;
                                if (!stream_is_image && mimetype_is_image==true)
                                    throw new NotImplementedException();

                                var l_dur_s1 = stream_xml.Attribute("duration")?.Value;
                                var l_dur_s2 = get_tag("DURATION") ?? get_tag("DURATION-eng");
                                // torrent subs stream can have boths
                                //if ((l_dur_s1 != null) && (l_dur_s2 != null))
                                //    throw new NotImplementedException($"[{inp_fname}]: [{metadata_s}]");
                                String source_name;
                                TimeSpan l_dur;
                                if (stream_is_image)
                                {
                                    source_name = $"Image:{stream_ind}";
                                    l_dur = TimeSpan.Zero;
                                }
                                else if (l_dur_s1 != null)
                                {
                                    source_name = $"FStream:{stream_ind}";
                                    l_dur = TimeSpan.FromSeconds(Double.Parse(l_dur_s1));
                                }
                                else if (l_dur_s2 != null)
                                {
                                    source_name = $"TStream:{stream_ind}";
                                    if (!l_dur_s2.Contains('.'))
                                        throw new FormatException();
                                    l_dur_s2 = l_dur_s2.TrimEnd('0').TrimEnd('.'); // Otherwise TimeSpan.Parse breaks
                                    l_dur = TimeSpan.Parse(l_dur_s2);
                                }
                                else
                                {
                                    source_name = $"?:{stream_ind}";
                                    l_dur = TimeSpan.Zero;
                                }

                                var frame_len = l_dur / frame_count;
                                l_dur -= frame_len;
                                if (l_dur < TimeSpan.Zero) throw new InvalidOperationException();
                                if (l_dur != TimeSpan.Zero) any_dur = true;

                                if (codec_type_s switch // skip if
                                {
                                    "video" => false,
                                    "attachment" => false,
                                    "audio" => true,
                                    "subtitle" => true,
                                    "data" => true, // some Core Media metadata thing
                                    _ => throw new FormatException(codec_type_s),
                                }) continue;

                                var pre_extracted_strong_ref = default(Byte[]?[]?);
                                var pre_extracted_weak_ref = new WeakReference<Byte[]?[]?>(pre_extracted_strong_ref);
                                var can_pre_extract = !stream_is_image;

                                var last_pos = Double.NaN;
                                var last_otp_fname = default(String);
                                sources.Add(new(source_name, l_dur, (pos, change_subjob, pre_extract_progress) =>
                                {
                                    using var this_locker = new ObjectLocker(this);
                                    try
                                    {
                                        Byte[]?[]? pre_extracted = null;
                                        #region Pre-extraction
                                        if (can_pre_extract && pre_extract_progress!=null && (!pre_extracted_weak_ref.TryGetTarget(out pre_extracted) || pre_extracted is null))
                                        {
                                            pre_extracted_strong_ref = new Byte[]?[frame_count];
                                            pre_extracted_weak_ref.SetTarget(pre_extracted_strong_ref);

                                            gen.thr_pool.AddJob($"pre extract [{stream_ind}] for [{inp_fname}]", change_subjob =>
                                            {
                                                var res = pre_extracted_strong_ref;
                                                pre_extracted_strong_ref = null;

                                                var res_i = 0;
                                                var buff_initial_size = 1024*1024;
                                                var buff = new Byte[buff_initial_size];
                                                var buff_fill = 0;
                                                void flush_frame(Int32 size)
                                                {
                                                    if (res_i < frame_count)
                                                        res[res_i] = buff[0..size];
                                                    var new_fill = buff_fill-size;
                                                    if (new_fill != 0)
                                                        Array.Copy(buff, size, buff, 0, new_fill);
                                                    buff_fill = new_fill;

                                                    res_i += 1;
                                                    var p = res_i/(Double)frame_count;
                                                    pre_extract_progress(p);
                                                    change_subjob($"done: {res_i}/{frame_count} ({p:P2})");
                                                }

                                                var frame_str = default(Stream);
                                                FFmpeg.Invoke($"-nostdin -i \"{inp_fname}\" -map 0:{stream_ind} -vf scale=256:256:force_original_aspect_ratio=decrease -c mjpeg -q:v 1 -f image2pipe -", () => true,
                                                    handle_otp: sr =>
                                                    {
                                                        frame_str = sr.BaseStream;
                                                        return System.Threading.Tasks.Task.FromResult<String?>(null);
                                                    }
                                                );
                                                if (frame_str is null) throw new InvalidOperationException();

                                                while (true)
                                                {
                                                    if (this.thumb_sources != curr_gen_thumb_sources) return;
                                                    var last_read_c = frame_str.Read(buff, buff_fill, buff.Length-buff_fill);
                                                    if (last_read_c==0) break;
                                                    buff_fill += last_read_c;
                                                    if (buff_fill == buff.Length)
                                                        Array.Resize(ref buff, buff_fill*2);

                                                    for (var len = Math.Max(2, buff_fill-last_read_c+1); len<=buff_fill; ++len)
                                                    {
                                                        // mjpeg end signature
                                                        if (buff[len-2] != 0xFF) continue;
                                                        if (buff[len-1] != 0xD9) continue;
                                                        flush_frame(len);
                                                        len = 2;
                                                    }

                                                }

                                                if (buff_fill != 0) Log.Append($"[{inp_fname}] had an unfinished frame");
                                                if (res_i != frame_count) Log.Append($"[{inp_fname}] was expected to have [{frame_count}] frames, but got [{res_i}]");
                                                if (buff_initial_size != buff.Length) Log.Append($"[{inp_fname}] needed >MB for a size=256 frame???");
                                            });
                                        }
                                        #endregion

                                        using var l_temps = new LocalTempsList(this);
                                        var args = new List<String>();

                                        temps.TryRemove(otp_temp_name);
                                        var otp_temp = l_temps.AddFile(otp_temp_name, "thumb.png");
                                        var otp_fname = otp_temp.Path;

                                        //TODO https://trac.ffmpeg.org/ticket/10512
                                        // - Need to cd into input folder for conversion to work
                                        String? ffmpeg_path = null;

                                        Func<StreamWriter, System.Threading.Tasks.Task>? handle_inp = null;

                                        if (pre_extracted != null && pre_extracted[(Int32)(pos * (pre_extracted.Length-1))] is Byte[] data)
                                        {
                                            handle_inp = sw => sw.BaseStream.WriteAsync(data, 0, data.Length);
                                            args.Add($"-i -");
                                        }
                                        else
                                        {
                                            if (l_dur != TimeSpan.Zero)
                                                args.Add($"-ss {pos*l_dur.TotalSeconds}");

                                            //TODO https://trac.ffmpeg.org/ticket/10506
                                            // - Need to first extract the attachments, before they can be used as input
                                            if (is_attachment)
                                            {
                                                if (l_dur != TimeSpan.Zero)
                                                    throw new NotImplementedException();

                                                FFmpeg.Invoke($"-dump_attachment:{stream_ind} pipe:1 -i \"{inp_fname}\" -nostdin", () => true,
                                                    handle_otp: sr =>
                                                    {
                                                        handle_inp = sw => sr.BaseStream.CopyToAsync(sw.BaseStream);
                                                        return System.Threading.Tasks.Task.FromResult<String?>(null);
                                                    }
                                                );
                                                if (handle_inp is null) throw new InvalidOperationException();

                                                args.Add($"-i -");
                                            }
                                            else
                                            {
                                                ffmpeg_path = Path.GetDirectoryName(inp_fname)!;
                                                args.Add($"-i \"{Path.GetFileName(inp_fname)}\"");
                                                args.Add($"-map 0:{stream_ind}");
                                                args.Add($"-vframes 1");
                                            }

                                            args.Add($"-vf scale=256:256:force_original_aspect_ratio=decrease");
                                        }

                                        args.Add($"\"{otp_fname}\"");
                                        args.Add($"-y");
                                        args.Add($"-nostdin");

                                        change_subjob("extract thumb");
                                        FFmpeg.Invoke(args.JoinToString(' '), () => File.Exists(otp_fname),
                                            execute_in: ffmpeg_path,
                                            handle_inp: handle_inp
                                        ).Wait();
                                        InvokeCacheSizeChanged(+FileSize(otp_fname));
                                        change_subjob(null);

                                        if (global_dur_s!="")
                                            apply_dur(otp_fname, otp_temp.Dispose, otp_fname, change_subjob);

                                        l_temps.GiveToCFI(otp_temp_name);
                                        l_temps.VerifyEmpty();

                                        last_otp_fname = otp_fname;
                                        last_pos = pos;
                                        return otp_fname;
                                    }
                                    catch (Exception e)
                                    {
                                        if (File.Exists(inp_fname))
                                            Log.Append($"Error making thumb for [{inp_fname}]: {e}");
                                        //Err.Handle(e);
                                        error_generating = true;
                                        return CommonThumbSources.Broken.Extract(false, 0, null!, null);
                                    }
                                }));

                                change_subjob(null);
                            }

                        var format_xml = metadata_xml.Descendants("format").SingleOrDefault();
                        if (format_xml is null)
                        {
                            if (sources.Count != 0)
                                throw new NotImplementedException(inp_fname);
                            if (File.Exists(inp_fname))
                                Log.Append($"No format data for [{inp_fname}]: {metadata_s}");
                            sources.Add(CommonThumbSources.Broken);
                        }
                        else if (any_dur && format_xml.Attribute("duration") is XAttribute global_dur_xml)
                        {
                            change_subjob("make dur string");
                            global_dur = TimeSpan.FromSeconds(Double.Parse(global_dur_xml.Value));

                            if (global_dur_s!="" || global_dur.Value.TotalHours>=1)
                                global_dur_s += Math.Truncate(global_dur.Value.TotalHours).ToString() + ':';
                            if (global_dur_s!="" || global_dur.Value.Minutes!=0)
                                global_dur_s += global_dur.Value.Minutes.ToString("00") + ':';
                            if (global_dur_s!="" || global_dur.Value.Seconds!=0)
                                global_dur_s += global_dur.Value.Seconds.ToString("00");
                            if (global_dur_s.Length<5)
                            {
                                var s = global_dur.Value.TotalSeconds;
                                s -= Math.Truncate(s);
                                var frac_s = s.ToString("N"+(5-global_dur_s.Length))[2..].TrimEnd('0');
                                if (frac_s!="") global_dur_s += '_'+frac_s;
                            }

                            change_subjob(null);
                        }

                    }
                    catch when (ffmpeg.BeenKilled)
                    {
                        return;
                    }
                    catch (InvalidOperationException e)
                    {
                        throw new FormatException(metadata_s, e);
                    }
                    catch (NullReferenceException e)
                    {
                        throw new FormatException(metadata_s, e);
                    }
                    catch (FormatException e)
                    {
                        throw new FormatException(metadata_s, e);
                    }

                    if (sources.Count==0)
                    {
                        if (global_dur_s=="")
                            sources.Add(CommonThumbSources.SoundOnly);
                        else
                        {
                            sources.Add(new("sound+dur", TimeSpan.Zero, (pos, change_subjob, pre_extract_progress) =>
                            {
                                var inp_fname = CommonThumbSources.SoundOnly.Extract(false, 0, null!, null);

                                using var l_temps = new LocalTempsList(this);
                                var args = new List<String>();

                                temps.TryRemove(otp_temp_name);
                                var otp_temp = l_temps.AddFile(otp_temp_name, "thumb.png");
                                var otp_fname = otp_temp.Path;

                                apply_dur(inp_fname, null, otp_fname, change_subjob);

                                l_temps.GiveToCFI(otp_temp_name);
                                l_temps.VerifyEmpty();

                                return otp_fname;
                            }));
                        }

                    }

                    using var init_source_use = BeginUse("Initial source use", () => false);
                    curr_gen_thumb_sources = [.. sources];
                    SetSources(change_subjob, curr_gen_thumb_sources);

                    settings.LastInpChangeTime = write_time;
                    settings.CurrentThumbIsFinal = true;

                    on_regenerated?.Invoke(this);
                });

            if (1==Interlocked.Increment(ref gen_jobs_assigned))
                this.gen_job_obj = gen.thr_pool.AddJob($"Generating thumb for [{inp_fname}] because {cause}", change_subjob =>
                {
                    do
                    {
                        try
                        {
                            if (!gen_acts.TryDequeue(out var act))
                                continue;
                            if (this.has_shut_down) return;
                            using var this_locker = new ObjectLocker(this);
                            if (this.has_shut_down) return;
                            act(change_subjob);
                        }
                        catch (Exception e)
                        {
                            if (File.Exists(inp_fname))
                                Err.Handle(e);
                        }
                    } while (0!=Interlocked.Decrement(ref gen_jobs_assigned));
                    this.gen_job_obj = null;
                });
            else if (this.gen_job_obj is CustomThreadPool.ThreadPoolJobHeader gen_job_obj)
                gen.thr_pool.BumpJob(gen_job_obj);

            return make_cache_use();
        }

        #endregion

        #region Shutdown

        private Boolean is_erased = false;
        public void Erase()
        {
            using var this_locker = new ObjectLocker(this);
            if (is_erased)
                throw new InvalidOperationException();
            is_erased = true;

            Shutdown(erase: true);
            DeleteDir(settings.GetSettingsDir());
            COMManip.ResetThumbFor(InpPath, TimeSpan.Zero);
        }

        // Shutdown without erasing is when exiting
        private Boolean has_shut_down = false;
        public void Shutdown(Boolean erase)
        {
            using var this_locker = new ObjectLocker(this);
            if (has_shut_down)
                throw new InvalidOperationException();
            has_shut_down = true;

            if (erase)
                settings.Shutdown();
        }

        #endregion

    }

    #endregion

    public event Action<Int64>? CacheSizeChanged;
    private void InvokeCacheSizeChanged(Int64 byte_change) =>
        CacheSizeChanged?.Invoke(byte_change);

    private readonly OneToManyLock purge_lock = new();

    #region Generate

    private volatile UInt32 last_used_id = 0;
    private CachedFileInfo GetCFI(String fname)
    {
        // Cannot add concurently, because .GetOrAdd can create
        // multiple instances of cfi for the same fname in different threads
        if (files.TryGetValue(fname, out var cfi)) return cfi;
        using var files_locker = new ObjectLocker(files);
        if (files.TryGetValue(fname, out cfi)) return cfi;
        while (true)
        {
            var id = Interlocked.Increment(ref last_used_id);
            var cache_file_dir = cache_dir.CreateSubdirectory(id.ToString());
            if (cache_file_dir.EnumerateFileSystemInfos().Any()) continue;
            cfi = new(this, id, cache_file_dir.FullName, InvokeCacheSizeChanged, fname);
            if (!files.TryAdd(fname, cfi))
                throw new InvalidOperationException();
            return cfi;
        }
    }

    private ICachedFileInfo.CacheUse? InternalGen(
        String fname,
        String cause, Func<Boolean>? is_unused_check,
        Action<ICachedFileInfo>? on_regenerated,
        Boolean force_regen
    )
    {
        fname = Path.GetFullPath(fname);
        if (!fname.StartsWith(internal_files_base))
            return GetCFI(fname).GenerateThumb(cause, is_unused_check, on_regenerated, force_regen, true);
        if (is_unused_check!=null && Path.GetExtension(fname) == ".png")
            return new IdentityCFI(fname).BeginUse(cause, is_unused_check);
        return null;
    }

    public ICachedFileInfo.CacheUse? Generate(
        String fname,
        String cause, Func<Boolean> is_unused_check,
        Action<ICachedFileInfo>? on_regenerated,
        Boolean force_regen
    ) => purge_lock.ManyLocked(() => InternalGen(fname, cause, is_unused_check, on_regenerated, force_regen));

    public void MassGenerate(IEnumerable<(String fname, Boolean force_regen)> lst) => purge_lock.ManyLocked(() =>
    {
        foreach (var (fname, force_regen) in lst)
            InternalGen(fname, nameof(MassGenerate), null, null, force_regen);
    });

    public void RegenAll(Boolean force_regen) => thr_pool.AddJob(nameof(RegenAll), change_subjob => purge_lock.ManyLocked(() =>
    {
        var cfis = files.Values.ToArray();
        for (var i = 0; i<cfis.Length; i++)
        {
            change_subjob($"{i}/{cfis.Length} ({i/(Double)cfis.Length:P2})");
            var cfi = cfis[i];
            if (cfi.CurrentThumbIsFinal && cfi.LastCacheUseTime==default) continue;
            cfi.GenerateThumb("Regen", null, null, force_regen, false);
        }
    }));

    #endregion

    #region Clear

    public Boolean ClearOne(String fname)
    {
        if (!files.TryRemove(fname, out var cfi)) return false;
        cfi.Erase();
        return true;
    }

    public Int32 ClearInvalid() {
        var c = purge_lock.OneLocked(() =>
        {
            var to_remove = new List<KeyValuePair<String, CachedFileInfo>>(files.Count);
            foreach (var kvp in files)
                if (kvp.Value.IsDeletable)
                    to_remove.Add(kvp);

            foreach (var kvp in to_remove)
            {
                if (!files.TryRemove(kvp))
                    throw new InvalidOperationException();
                kvp.Value.Erase();
            }

            if (to_remove.Count!=0)
            {
                last_used_id = 0;
                InvokeCacheSizeChanged(0);
            }

            return to_remove.Count;
        }, with_priority: false);
        // this... may get annoying
        //if (c!=0) TTS.Speak($"ThumbDashboard: Invalid entries: {c}");
        return c;
    }

    public Int32 ClearExtraFiles()
    {
        var c = purge_lock.OneLocked(() =>
        {
            var c = 0;
            foreach (var cfi in files.Values)
                c += cfi.DeleteExtraFiles();
            return c;
        }, with_priority: false);
        if (c!=0) TTS.Speak($"ThumbDashboard: Extra files: {c}");
        return c;
    }

    public void ClearOldest(Int64 size_to_clear) => purge_lock.OneLocked(() =>
    {
        void size_change_handler(Int64 size_change) => size_to_clear += size_change;
        CacheSizeChanged += size_change_handler;
        try
        {

            foreach (var cfi in files.Values.AsParallel().Where(cfi => cfi.CanClearTemps).OrderBy(cfi => cfi.LastCacheUseTime))
            {
                if (Common.IsShuttingDown)
                    break;
                if (cfi.TryClearTemps() && size_to_clear<=0)
                    break;
            }

        }
        finally
        {
            CacheSizeChanged -= size_change_handler;
        }
    }, with_priority: false);

    public void ClearAll(Action<String?> change_subjob) => purge_lock.OneLocked(() =>
    {
        while (!files.IsEmpty)
            try
            {
                change_subjob($"left: {files.Count}");
                if (!files.TryRemove(files.Keys.First(), out var cfi))
                    continue;
                cfi.Erase();
            }
            catch (Exception e)
            {
                Err.Handle(e);
            }
        last_used_id = 0;
        InvokeCacheSizeChanged(0);
        Console.Beep();
        //Common.CurrentApp.Dispatcher.Invoke(() =>
        //    CustomMessageBox.ShowOK("Done clearing cache!", Common.CurrentApp.MainWindow)
        //);
    }, with_priority: true);

    #endregion

    public void Shutdown()
    {
        foreach (var cfi in files.Values)
            cfi.Shutdown(erase: false);
        lock_file.Close();
        File.Delete(Path.Combine(cache_dir.FullName, ".lock"));
    }

}