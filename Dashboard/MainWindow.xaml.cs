using System;

using System.Linq;
using System.Collections.Generic;

using System.IO;
using System.Globalization;

using System.Windows;
using System.Windows.Input;

using SunSharpUtils;
using SunSharpUtils.WPF;
using SunSharpUtils.Settings;
using SunSharpUtils.Threading;

using Dashboard.Settings;

namespace Dashboard;

public partial class MainWindow : Window
{
    public readonly TrayIcon tray_icon;
    public Boolean shutdown_triggered = false;

    public MainWindow()
    {
        try
        {
            InitializeComponent();

            tray_icon = new(this);

            var (main_thr_pool, pipe) = LogicInit();

            if (shutdown_triggered)
                Common.Shutdown();
            if (Common.IsShuttingDown)
                return;

            SetUpJobCount(main_thr_pool);

            SetUpCacheCapInfo();

            allowed_ext_list.AddFromSettings();

            LoadThumbGenerator(main_thr_pool, thumb_gen =>
            {
                pipe.AddThumbGen(thumb_gen);

                AllowedExtInstaller.SetThumbGen(thumb_gen);

                SetUpCacheFillInfo(thumb_gen, main_thr_pool);

                SetUpThumbCompare(thumb_gen, pipe);

                pipe.StartThrowingUndefinedCommand();
            });

        }
        catch (Exception e)
        {
            Err.Handle(e);
            Environment.Exit(-1);
        }

    }

    private (CustomThreadPool, CommandsPipe) LogicInit()
    {

        CLArgs.Load(this);

        var main_thr_pool = new CustomThreadPool(Environment.ProcessorCount+1);
        /**
        main_thr_pool.AddJob("inf inc", change_subjob =>
        {
            Thread.CurrentThread.IsBackground = true;
            var i = 0;
            while (true)
                change_subjob($"{++i}");
        });
        /**/
        /**
        for (int i = 0; i < 11; i++)
        {
            var id = i;
            void job(Action<string?> change_subjob)
            {
                System.Threading.Thread.CurrentThread.IsBackground = true;
                change_subjob("waiting...");
                System.Threading.Thread.Sleep(1000);
                change_subjob(null);
                main_thr_pool.AddJob($"phoenix job [{id}]", job);
            }
            main_thr_pool.AddJob($"phoenix job [{id}]", job);
        }
        /**/

        var pipe = new CommandsPipe();

        // After initing pipe, to make sure older process is already dead
        {
            const String file_lock_name = @".lock";
            var file_lock = File.Create(file_lock_name);
            Common.OnShutdown += _ => Err.Handle(() =>
            {
                file_lock.Close();
                File.Delete(file_lock_name);
            });
        }

        return (main_thr_pool, pipe);
    }

    private void SetUpJobCount(CustomThreadPool main_thr_pool)
    {
        slider_want_job_count.ValueChanged += (o, e) => Err.Handle(() =>
        {
            var c = (Int32)e.NewValue;
            main_thr_pool.SetJobCount(c);
            GlobalSettings.Instance.MaxJobCount = c;
        });
        slider_want_job_count.Value = GlobalSettings.Instance.MaxJobCount;

        slider_active_job_count.Maximum = main_thr_pool.MaxJobCount;
        main_thr_pool.ActiveJobsCountChanged += () =>
            Dispatcher.BeginInvoke(() => Err.Handle(() =>
                slider_active_job_count.Value = main_thr_pool.ActiveJobsCount
            ));

        var pending_jobs_count_updater = new DelayedUpdater(() =>
        {
            var c2 = main_thr_pool.PendingUniqueJobCount;
            var c1 = main_thr_pool.PendingJobCount;
            Dispatcher.Invoke(() => tb_pending_jobs_count.Text = $"{c1} ({c2})");
        }, "Pending jobs count update", is_background: true);
        main_thr_pool.PendingJobCountChanged += () =>
            pending_jobs_count_updater.TriggerUrgent(TimeSpan.FromSeconds(1.0/60));

        b_view_jobs.Click += (o, e) =>
            Err.Handle(() => new JobList(main_thr_pool).Show());

        void update_log_count() => Dispatcher.BeginInvoke(() => Err.Handle(() =>
        {
            var s = "Log";
            var c = Log.Count;
            if (c != 0)
                s += $" ({c})";
            b_view_log.Content = s;
            b_view_log.IsEnabled = c != 0;
        }));
        Log.CountUpdated += update_log_count;
        update_log_count();
        b_view_log.Click += (o, e) =>
            Err.Handle(Log.Show);

    }

    private void LoadThumbGenerator(CustomThreadPool main_thr_pool, Action<ThumbGenerator> on_load)
    {
        main_thr_pool.AddJob("Init ThumbGenerator", change_subjob =>
        {
            var thumb_gen = new ThumbGenerator(main_thr_pool, "cache", change_subjob);
            if (Common.IsShuttingDown) return;

            change_subjob($"Apply ThumbGenerator");
            Dispatcher.Invoke(() =>
            {
                Common.OnShutdown += _ => Err.Handle(thumb_gen.Shutdown);
                on_load(thumb_gen);
            });
            change_subjob(null);

            Console.Beep();
        });
    }

    private void SetUpCacheCapInfo()
    {
        ByteCount.ForEachScale(scale_name =>
        {
            cb_cache_cap_scale.Items.Add(scale_name);
        });

        static Boolean try_parse_cache_cap(String s, out Double v) =>
            Double.TryParse(s, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out v);
        var tb_cache_cap_v = new FilteredTextBox<Double>(
            try_parse_cache_cap,
            v => GlobalSettings.Instance.MaxCacheSize = ByteCount.Compose(v, cb_cache_cap_scale.SelectedIndex),
            ("Invalid size", "Expected a non-negative float")
        );
        c_cache_cap_v.Content = tb_cache_cap_v;

        void recalculate_cache_cap()
        {
            var (size_v, size_scale_ind) = GlobalSettings.Instance.MaxCacheSize.Split();
            if (!tb_cache_cap_v.Edited)
            {
                var cap_v = Math.Round(size_v, 2, MidpointRounding.AwayFromZero).ToString("N2");
                tb_cache_cap_v.ResetContent(cap_v);
            }
            cb_cache_cap_scale.SelectedIndex = size_scale_ind;
        }
        tb_cache_cap_v.Commited += recalculate_cache_cap;
        recalculate_cache_cap();

        cb_cache_cap_scale.SelectionChanged += (o, e) => Err.Handle(() =>
        {
            GlobalSettings.Instance.MaxCacheSize = ByteCount.Compose(GlobalSettings.Instance.MaxCacheSize.Split().v, cb_cache_cap_scale.SelectedIndex);
            recalculate_cache_cap();
        });
    }

    private void SetUpCacheFillInfo(ThumbGenerator thumb_gen, CustomThreadPool main_thr_pool)
    {
        b_cache_clear.Click += (o, e) => Err.Handle(() => main_thr_pool.AddJob("Clearing cache", thumb_gen.ClearAll));

        b_cache_regen.Click += (o, e) => Err.Handle(() => thumb_gen.RegenAll(true));

        ByteCount cache_fill = 0;
        void update_cache_info() =>
            tb_cache_filled.Text = cache_fill.ToString();

        var cache_info_updater = new DelayedUpdater(() =>
        {
            static Int64 get_cache_fill() =>
                new DirectoryInfo("cache")
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
            cache_fill = get_cache_fill();
            Dispatcher.Invoke(update_cache_info);

            if (cache_fill > GlobalSettings.Instance.MaxCacheSize)
            {
                if (thumb_gen.ClearInvalid()!=0) return;
                if (thumb_gen.ClearExtraFiles()!=0) return;
                // recalc needed size change here, in case it changed
                cache_fill = get_cache_fill();
                ThreadingCommon.RunWithBackgroundReset(() =>
                    thumb_gen.ClearOldest(size_to_clear: cache_fill-GlobalSettings.Instance.MaxCacheSize),
                    new_is_background: false
                );
            }

        }, $"cache size recalculation", is_background: true);
        IsVisibleChanged += (o, e) => Err.Handle(() =>
        {
            if (!IsVisible) return;
            cache_info_updater.TriggerNow();
        });
        thumb_gen.CacheSizeChanged += byte_change => Dispatcher.BeginInvoke(() => Err.Handle(() =>
        {
            cache_fill += byte_change;
            update_cache_info();
            cache_info_updater.TriggerPostpone(TimeSpan.FromSeconds(0.5));
        }));
        cache_info_updater.TriggerNow();

    }

    private void SetUpThumbCompare(ThumbGenerator thumb_gen, CommandsPipe pipe)
    {
        var thumb_compare_all = new[] { thumb_compare_org, thumb_compare_gen };

        Action<Double>? vid_timestamp_handler = null;
        slider_vid_timestamp.ValueChanged += (_, _) =>
            vid_timestamp_handler?.Invoke(slider_vid_timestamp.Value);

        void set_pregen_progress(Double progress)
        {
            if (progress>1) progress = 1;
            if (progress<0) progress = 0;
            Dispatcher.BeginInvoke(() => Err.Handle(() =>
                pb_vid_pregen.Value = progress
            ));
        }

        Action? next_thumb_compare_update = null;
        var thumb_compare_updater = new DelayedUpdater(
            () => next_thumb_compare_update?.Invoke(),
            "thumb compare update",
            is_background: false
        );

        Int32 current_compare_id = 0;
        String? curr_compare_fname = null;
        ThumbGenerator.ICachedFileInfo.CacheUse? last_cache_use = null;
        void clear_thumb_compare_file()
        {
            foreach (var tcv in thumb_compare_all)
                tcv.Reset();
            grid_thumb_compare.HorizontalAlignment = HorizontalAlignment.Stretch;
            c_thumb_compare_1.VerticalAlignment = VerticalAlignment.Stretch;
            c_thumb_compare_2.VerticalAlignment = VerticalAlignment.Stretch;
            b_reload_compare.IsEnabled = false;
            sp_gen_controls.Visibility = Visibility.Hidden;

            ++current_compare_id;
            curr_compare_fname = null;
            System.Threading.Interlocked.Exchange(ref last_cache_use, null)?.Dispose();
        }
        void begin_thumb_compare(String fname) => Err.Handle(() =>
        {
            clear_thumb_compare_file();
            // Increament in the clear_thumb_compare_file
            var compare_id = current_compare_id;

            thumb_compare_org.Set(COMManip.GetExistingThumbFor(fname));
            using var cfi_use = thumb_gen.Generate(fname, nameof(begin_thumb_compare), ()=>false, cfi =>
            {
                Boolean is_outdated() => compare_id != current_compare_id;
                if (is_outdated()) return;
                var new_cache_use = cfi.BeginUse("continuous thumb compare", is_outdated);
                Dispatcher.BeginInvoke(() => Err.Handle(() =>
                {
                    System.Threading.Interlocked.Exchange(ref last_cache_use, new_cache_use)?.Dispose();
                    if (is_outdated()) return;

                    thumb_compare_gen.Set(cfi.CurrentThumbBmp);

                    var sources = cfi.ThumbSources;
                    if (sources.Count==1 && sources[0].Length==TimeSpan.Zero) return;
                    sp_gen_controls.Visibility = Visibility.Visible;

                    var bts = new System.Windows.Controls.Button[sources.Count];
                    sp_vid_stream_buttons.Children.Clear();
                    for (var i = 0; i<sources.Count; i++)
                    {
                        bts[i] = new()
                        {
                            Margin = new Thickness(0, 0, 5, 0),
                            Content = sources[i].Name,
                        };
                        {
                            var ind = i;
                            bts[i].Click += (_, _) =>
                            {
                                next_thumb_compare_update = () => select_source(ind);
                                thumb_compare_updater.TriggerNow();
                            };
                        }
                        sp_vid_stream_buttons.Children.Add(bts[i]);
                    }

                    void select_source(Int32 new_ind)
                    {
                        vid_timestamp_handler = pos =>
                        {
                            tb_vid_timestamp.Text = (slider_vid_timestamp.Value * sources[new_ind].Length).ToString();
                            next_thumb_compare_update = () =>
                            {
                                cfi.ApplySourceAt(false, _ => { }, new_ind, pos, out _, set_pregen_progress);
                                // invoke blocking in thumb_compare_updater thread
                                Dispatcher.Invoke(() =>
                                    thumb_compare_gen.Set(cfi.CurrentThumbBmp)
                                );
                            };
                            thumb_compare_updater.TriggerNow();
                        };
                        var old_ind = cfi.ChosenThumbOptionInd;
                        cfi.ApplySourceAt(true, _ => { }, new_ind, null, out var initial_pos, set_pregen_progress);
                        // invoke blocking in thumb_compare_updater thread
                        Dispatcher.Invoke(() => Err.Handle(() =>
                        {
                            slider_vid_timestamp.Value = initial_pos;
                            thumb_compare_gen.Set(cfi.CurrentThumbBmp);
                            tb_vid_timestamp.Visibility = pb_vid_pregen.Visibility = slider_vid_timestamp.Visibility =
                                sources[new_ind].Length != TimeSpan.Zero ? Visibility.Visible : Visibility.Hidden;
                            bts[old_ind].IsEnabled = true;
                            bts[new_ind].IsEnabled = false;
                        }));
                    }
                    select_source(cfi.ChosenThumbOptionInd);

                }));
            }, true);
            if (cfi_use is null) return;
            var cfi = cfi_use.CFI;

            thumb_compare_gen.Set(cfi.CurrentThumbBmp);
            grid_thumb_compare.HorizontalAlignment = HorizontalAlignment.Center;
            c_thumb_compare_1.VerticalAlignment = VerticalAlignment.Bottom;
            c_thumb_compare_2.VerticalAlignment = VerticalAlignment.Top;
            GlobalSettings.Instance.LastComparedFile = fname;
            curr_compare_fname = fname;
            b_reload_compare.IsEnabled = true;
        });

        var awaiting_mass_gen_lst = new List<(String fname, Boolean force_regen)>();
        var delayed_mass_gen = new DelayedUpdater(() =>
        {
            (String, Boolean)[] gen_lst;
            lock (awaiting_mass_gen_lst)
            {
                gen_lst = [.. awaiting_mass_gen_lst];
                awaiting_mass_gen_lst.Clear();
            }
            thumb_gen.MassGenerate(gen_lst);
        }, "Thumb compare => mass gen", is_background: false);
        void apply_file_lst(Boolean force_regen, String[] lst)
        {
            if (lst.Length==1 && force_regen)
            {
                tray_icon.ShowWin();
                begin_thumb_compare(lst.Single());
                return;
            }

            lock (awaiting_mass_gen_lst)
                awaiting_mass_gen_lst.AddRange(lst.Select(fname=>(fname, force_regen)));
            delayed_mass_gen.TriggerNow();
        }

        String[] extract_file_lst(IEnumerable<String> inp)
        {
            static T execute_any<T>(String descr, params Func<Action, T>[] funcs)
            {
                var wh = new System.Threading.ManualResetEventSlim(false);
                var excs = new List<Exception>();
                var left = funcs.Length;

                T? res = default;
                Boolean res_set = false;

                for (Int32 i =0; i<funcs.Length; ++i)
                {
                    var f = funcs[i];
                    new System.Threading.Thread(()=>
                    {
                        try
                        {
                            res = f(() => {
                                if (wh.IsSet)
                                    throw new System.Threading.Tasks.TaskCanceledException();
                            });
                            res_set = true;
                            wh.Set();
                        }
                        catch (Exception e)
                        {
                            using var excs_locker = new ObjectLocker(excs);
                            excs.Add(e);
                            left -= 1;
                            if (left == 0) wh.Set();
                        }
                    })
                    {
                        IsBackground = true,
                        Name = $"{descr} #{i}",
                    }.Start();
                }

                wh.Wait();
                if (!res_set)
                    throw new AggregateException(excs);

                return res!;
            }

            var exts = GlobalSettings.Instance.AllowedExts;
            var es_arg = "ext:" + exts.JoinToString(';');

            return inp.AsParallel().SelectMany(path =>
            {
                if (File.Exists(path))
                    return Enumerable.Repeat(path, exts.MatchesFile(path)?1:0);
                if (Directory.Exists(path))
                    return execute_any($"get files in [{path}]",
                        try_cancel =>
                        {
                            var res = new List<String>();
                            void on_dir(String dir)
                            {
                                foreach (var subdir in Directory.EnumerateDirectories(dir))
                                    on_dir(subdir);
                                foreach (var fname in Directory.EnumerateFiles(dir))
                                {
                                    try_cancel();
                                    if (!exts.MatchesFile(fname)) continue;
                                    res.Add(fname);
                                }
                            }
                            on_dir(path);
                            return res.AsEnumerable();
                        },
                        try_cancel =>
                        {
                            var res = new List<String>();
                            foreach (var fname in new ESQuary(path, es_arg))
                            {
                                try_cancel();
                                res.Add(fname);
                            }
                            return res.AsEnumerable();
                        }
                    );
                return [];
            }).ToArray();
        }

        pipe.AddRefreshAndCompareHandler((force_regen, inp) =>
        {
            var lst = extract_file_lst(inp);
            Dispatcher.BeginInvoke(() => Err.Handle(() => apply_file_lst(force_regen, lst)));
        });

        foreach (var tcv in thumb_compare_all)
            tcv.MouseDown += (o, e) => Err.Handle(() =>
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    new FileChooser(inp => apply_file_lst(force_regen: true, extract_file_lst(inp))).ShowDialog();
                    e.Handled = true;
                }
                else if (e.ChangedButton == MouseButton.Right)
                {
                    clear_thumb_compare_file();
                    e.Handled = true;
                }
                else if (e.ChangedButton == MouseButton.Middle)
                {
                    if (curr_compare_fname != null)
                        System.Diagnostics.Process.Start("explorer", $"/select,\"{curr_compare_fname}\"");
                    e.Handled = true;
                }
            });

        b_swap_compare.Click += (o, e) => Err.Handle(() =>
        {

            var t = (c_thumb_compare_2.Child, c_thumb_compare_1.Child);
            (c_thumb_compare_1.Child, c_thumb_compare_2.Child) = (null, null);
            (c_thumb_compare_1.Child, c_thumb_compare_2.Child) = t;

            (tb_thumb_compare_1.Text, tb_thumb_compare_2.Text) = (tb_thumb_compare_2.Text, tb_thumb_compare_1.Text);

        });

        b_reload_compare.Click += (o, e) => Err.Handle(() =>
        {
            if (curr_compare_fname is null) return;
            begin_thumb_compare(curr_compare_fname);
        });

        (String[] inp, String[] lst)? drag_cache = null;
        void drag_handler(Object o, DragEventArgs e)
        {
            e.Handled = true;
            e.Effects = DragDropEffects.None;

            var inp = (String[])e.Data.GetData(DataFormats.FileDrop);
            if (inp is null) return;

            var lst = (drag_cache.HasValue && inp.SequenceEqual(drag_cache.Value.inp) ? drag_cache :
                (drag_cache = (inp, extract_file_lst(inp)))
            ).Value.lst;
            if (lst.Length==0) return;

            e.Effects = lst.Length==1 ?
                DragDropEffects.Link :
                DragDropEffects.Move;

        }
        grid_thumb_compare.DragEnter += drag_handler;
        grid_thumb_compare.DragOver += drag_handler;

        grid_thumb_compare.Drop += (o, e) => Err.Handle(() =>
        {
            var lst = drag_cache!.Value.lst;
            apply_file_lst(force_regen: true, lst);
            e.Handled = true;
        });

        Closing += (o, e) => Err.Handle(clear_thumb_compare_file);
        grid_thumb_compare.AllowDrop = true;
    }

}
