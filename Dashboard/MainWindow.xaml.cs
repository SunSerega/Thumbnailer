using System;

using System.Linq;
using System.Collections.Generic;

using System.IO;
using System.Text;
using System.Globalization;

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Dashboard;

public partial class MainWindow : Window
{
    public readonly TrayIcon tray_icon;
    public bool shutdown_triggered = false;

    public MainWindow()
    {
        try
        {
            InitializeComponent();

            tray_icon = new(this);

            var (main_thr_pool, pipe) = LogicInit();

            if (shutdown_triggered)
                App.Current!.Shutdown(0);
            if (App.Current!.IsShuttingDown)
                return;

            SetUpJobCount(main_thr_pool);

            SetUpCacheCapInfo();

            LoadThumbGenerator(main_thr_pool, thumb_gen =>
            {
                pipe.AddThumbGen(thumb_gen);

                SetUpCacheFillInfo(thumb_gen, main_thr_pool);

                SetUpAllowedExt(thumb_gen);

                SetUpThumbCompare(thumb_gen, pipe);

                pipe.StartAccepting();
            });

        }
        catch (LoadCanceledException)
        {
            Environment.Exit(-1);
        }
        catch (Exception e)
        {
            Utils.HandleException(e);
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
        App.Current!.Exit += (o, e) => Utils.HandleException(pipe.Shutdown);

        return (main_thr_pool, pipe);
    }

    private void SetUpJobCount(CustomThreadPool main_thr_pool)
    {
        slider_want_job_count.ValueChanged += (o, e) =>
            Utils.HandleException(() => main_thr_pool.SetJobCount((int)e.NewValue));
        slider_want_job_count.Value = Settings.Root.MaxJobCount;

        slider_active_job_count.Maximum = main_thr_pool.MaxJobCount;
        main_thr_pool.ActiveJobsCountChanged += () =>
            Dispatcher.BeginInvoke(() => Utils.HandleException(() =>
                slider_active_job_count.Value = main_thr_pool.ActiveJobsCount
            ));

        var pending_jobs_count_updater = new DelayedUpdater(() =>
        {
            var c2 = main_thr_pool.PendingUniqueJobCount;
            var c1 = main_thr_pool.PendingJobCount;
            Dispatcher.Invoke(() => tb_pending_jobs_count.Text = $"{c1} ({c2})");
        }, "Pending jobs count update");
        main_thr_pool.PendingJobCountChanged += () =>
            pending_jobs_count_updater.Trigger(TimeSpan.FromSeconds(1.0/60), false);

        b_view_jobs.Click += (o, e) =>
            Utils.HandleException(() => new JobList(main_thr_pool).Show());

        void update_log_count() => Dispatcher.BeginInvoke(() => Utils.HandleException(() =>
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
            Utils.HandleException(Log.Show);

    }

    private void LoadThumbGenerator(CustomThreadPool main_thr_pool, Action<ThumbGenerator> on_load)
    {
        main_thr_pool.AddJob("Init ThumbGenerator", change_subjob =>
        {
            var thumb_gen = new ThumbGenerator(main_thr_pool, "cache", change_subjob);
            if (App.Current!.IsShuttingDown) return;

            change_subjob($"Apply ThumbGenerator");
            Dispatcher.Invoke(() =>
            {
                App.Current.Exit += (o, e) => Utils.HandleException(thumb_gen.Shutdown);
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

        bool cache_cap_v_edited = false;
        bool recalculating_cache_cap = false;
        void recalculate_cache_cap()
        {
            try
            {
                recalculating_cache_cap = true;
                var (size_v, size_scale_ind) = Settings.Root.MaxCacheSize.Split();
                if (!cache_cap_v_edited)
                    tb_cache_cap_v.Text = Math.Round(size_v, 2, MidpointRounding.AwayFromZero).ToString("N2");
                cb_cache_cap_scale.SelectedIndex = size_scale_ind;
            }
            finally
            {
                recalculating_cache_cap = false;
            }
        }
        recalculate_cache_cap();

        static bool try_parse_cache_cap(string s, out double v) =>
            double.TryParse(s, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out v);
        tb_cache_cap_v.TextChanged += (o, e) => Utils.HandleException(() =>
        {
            if (recalculating_cache_cap) return;
            tb_cache_cap_v.Background = try_parse_cache_cap(tb_cache_cap_v.Text, out _) ?
                Brushes.YellowGreen : Brushes.Coral;
            cache_cap_v_edited = true;
        });
        tb_cache_cap_v.KeyDown += (o, e) => Utils.HandleException(() =>
        {
            if (e.Key != Key.Enter) return;

            if (!try_parse_cache_cap(tb_cache_cap_v.Text, out var v))
            {
                CustomMessageBox.Show("Invalid size", "Expected a non-negative float", this);
                return;
            }

            Settings.Root.MaxCacheSize = ByteCount.Compose(v, cb_cache_cap_scale.SelectedIndex);
            tb_cache_cap_v.Background = Brushes.Transparent;
            cache_cap_v_edited = false;
            recalculate_cache_cap();
        });
        cb_cache_cap_scale.SelectionChanged += (o, e) => Utils.HandleException(() =>
        {
            Settings.Root.MaxCacheSize = ByteCount.Compose(Settings.Root.MaxCacheSize.Split().v, cb_cache_cap_scale.SelectedIndex);
            recalculate_cache_cap();
        });
    }

    private void SetUpCacheFillInfo(ThumbGenerator thumb_gen, CustomThreadPool main_thr_pool)
    {
        b_cache_clear.Click += (o, e) => Utils.HandleException(() => main_thr_pool.AddJob("Clearing cache", thumb_gen.ClearAll));

        b_cache_regen.Click += (o, e) => Utils.HandleException(() => thumb_gen.RegenAll(true));

        ByteCount cache_fill = 0;
        void update_cache_info() =>
            tb_cache_filled.Text = cache_fill.ToString();

        var cache_info_updater = new DelayedUpdater(() =>
        {
            static long get_cache_fill() =>
                new DirectoryInfo("cache")
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
            cache_fill = get_cache_fill();
            Dispatcher.Invoke(update_cache_info);

            if (cache_fill > Settings.Root.MaxCacheSize)
            {
                if (thumb_gen.ClearInvalid()!=0) return;
                if (thumb_gen.ClearExtraFiles()!=0) return;
                // recalc needed size change here, in case it changed
                cache_fill = get_cache_fill();
                thumb_gen.ClearOldest(size_to_clear: cache_fill-Settings.Root.MaxCacheSize);
            }

        }, $"cache size recalculation");
        IsVisibleChanged += (o, e) => Utils.HandleException(() =>
        {
            if (!IsVisible) return;
            cache_info_updater.Trigger(TimeSpan.Zero, false);
        });
        thumb_gen.CacheSizeChanged += byte_change => Dispatcher.BeginInvoke(() => Utils.HandleException(() =>
        {
            cache_fill += byte_change;
            update_cache_info();
            cache_info_updater.Trigger(TimeSpan.FromSeconds(0.5), true);
        }));
        cache_info_updater.Trigger(TimeSpan.Zero, false);

    }

    private void SetUpAllowedExt(ThumbGenerator thumb_gen)
    {
        AllowedExt.Changed += any_change =>
            b_check_n_commit.IsEnabled = any_change;

        tb_new_ext.PreviewTextInput += (o, e) => Utils.HandleException(() =>
            e.Handled = !FileExtList.Validate(e.Text)
        );
        CommandManager.AddPreviewExecutedHandler(tb_new_ext, (o, e) =>
        {
            if (e.Command != ApplicationCommands.Paste) return;
            if (!Clipboard.ContainsText()) return;
            e.Handled = !FileExtList.Validate(Clipboard.GetText());
        });

        void add_ext()
        {
            if (tb_new_ext.Text=="") return;
            _=new AllowedExt(tb_new_ext.Text, allowed_ext_container);
            tb_new_ext.Clear();
        }
        tb_new_ext.KeyDown += (o, e) => Utils.HandleException(() =>
        {
            if (e.Key == Key.Enter)
                add_ext();
        });
        b_add_ext.Click += (_, _) => Utils.HandleException(add_ext);

        var delayed_commit_reset_exts = new Dictionary<string, string>();
        var delayed_commit_reset = new DelayedUpdater(() =>
        {
            Dictionary<string, string[]> reset_kind_to_exts;
            lock (delayed_commit_reset_exts)
            {
                reset_kind_to_exts = delayed_commit_reset_exts
                    .GroupBy(kvp => kvp.Value)
                    .ToDictionary(g => g.Key, g => g.Select(kvp => kvp.Key).ToArray());
                delayed_commit_reset_exts.Clear();
            }

            foreach (var (reset_kind, exts) in reset_kind_to_exts)
            {
                var q = new ESQuary("ext:"+string.Join(';', exts));
                switch (reset_kind)
                {
                    case "Reset":
                        foreach (var fname in q)
                            COMManip.ResetThumbFor(fname, TimeSpan.Zero);
                        break;
                    case "Generate":
                        thumb_gen.MassGenerate(q, true);
                        break;
                    default: throw new NotImplementedException(reset_kind);
                }
            }

        }, $"Thumb reset after AllowedExt commit");
        b_check_n_commit.Click += (o, e) => Utils.HandleException(()=>
        {

            var sb = new StringBuilder();
            var (add, rem) = AllowedExt.GetChanges();

            if (add.Length!=0)
            {
                sb.AppendLine($"Added: ");
                foreach (var ext in add)
                    sb.AppendLine(ext);
                sb.Append('~', 30);
                sb.AppendLine();
                sb.AppendLine();
            }

            if (rem.Length!=0)
            {
                sb.AppendLine($"Removed: ");
                foreach (var ext in rem)
                    sb.AppendLine(ext);
                sb.Append('~', 30);
                sb.AppendLine();
                sb.AppendLine();
            }

            sb.AppendLine($"Commit?");

            if (CustomMessageBox.ShowYesNo("Confirm changes", sb.ToString(), this))
            {
                var gen_type = add.Length==0 ? null : CustomMessageBox.Show("What to do with files of added extensions?", "Press Escape to do nothing", "Reset", "Generate");

                var reg_ext_args = new List<string>();
                if (add.Length!=0) reg_ext_args.Add("add:"+string.Join(';', add));
                if (rem.Length!=0) reg_ext_args.Add("rem:"+string.Join(';', rem));
                var psi = new System.Diagnostics.ProcessStartInfo(@"RegExtController.exe", string.Join(' ', reg_ext_args))
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = Environment.CurrentDirectory,
                    CreateNoWindow = true,
                };
                var p = System.Diagnostics.Process.Start(psi)!;
                p.WaitForExit();
                if (p.ExitCode != 0)
                    throw new Exception($"ExitCode={p.ExitCode}");
                
                COMManip.NotifyRegExtChange();
                AllowedExt.CommitChanges();

                if (gen_type is null) return;
                lock (delayed_commit_reset_exts)
                    foreach (var ext in add)
                    {
                        static int gen_type_ord(string gen_type) => gen_type switch
                        {
                            "Reset" => 1,
                            "Generate" => 2,
                            _ => throw new NotImplementedException()
                        };
                        if (!delayed_commit_reset_exts.TryGetValue(ext, out var old_gen_type) || gen_type_ord(old_gen_type)<gen_type_ord(gen_type))
                            delayed_commit_reset_exts[ext] = gen_type;
                    }
                delayed_commit_reset.Trigger(TimeSpan.Zero, false);
            }
            else
            {
                foreach (var ext in rem)
                    _=new AllowedExt(ext, allowed_ext_container);
                foreach (var ext in add)
                    allowed_ext_container.Children.Cast<AllowedExt>().Single(ae => ae.tb_name.Text == ext).Delete(allowed_ext_container);
            }
        });

        foreach (var ext in Settings.Root.AllowedExts)
            _=new AllowedExt(ext, allowed_ext_container);
    }

    private void SetUpThumbCompare(ThumbGenerator thumb_gen, CommandsPipe pipe)
    {
        var thumb_compare_all = new[] { thumb_compare_org, thumb_compare_gen };

        Action<double>? vid_timestamp_handler = null;
        slider_vid_timestamp.ValueChanged += (_, _) =>
            vid_timestamp_handler?.Invoke(slider_vid_timestamp.Value);

        void set_pregen_progress(double progress)
        {
            if (progress>1) progress = 1;
            if (progress<0) progress = 0;
            Dispatcher.BeginInvoke(() => Utils.HandleException(() =>
                pb_vid_pregen.Value = progress
            ));
        }

        Action? next_thumb_compare_update = null;
        var thump_compare_updater = new DelayedUpdater(
            () => next_thumb_compare_update?.Invoke(),
            "thumb compare update"
        );

        int current_compare_id = 0;
        string? curr_compare_fname = null;
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
        void begin_thumb_compare(string fname) => Utils.HandleException(() =>
        {
            clear_thumb_compare_file();
            // Increament in the clear_thumb_compare_file
            var compare_id = current_compare_id;

            thumb_compare_org.Set(COMManip.GetExistingThumbFor(fname));
            using var cfi_use = thumb_gen.Generate(fname, nameof(begin_thumb_compare), ()=>false, cfi =>
            {
                bool is_outdated() => compare_id != current_compare_id;
                if (is_outdated()) return;
                var new_cache_use = cfi.BeginUse("continuous thumb compare", is_outdated);
                Dispatcher.BeginInvoke(() => Utils.HandleException(() =>
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
                                thump_compare_updater.Trigger(TimeSpan.Zero, false);
                            };
                        }
                        sp_vid_stream_buttons.Children.Add(bts[i]);
                    }

                    void select_source(int new_ind)
                    {
                        vid_timestamp_handler = pos =>
                        {
                            tb_vid_timestamp.Text = (slider_vid_timestamp.Value * sources[new_ind].Length).ToString();
                            next_thumb_compare_update = () =>
                            {
                                cfi.ApplySourceAt(false, _ => { }, new_ind, pos, out _, set_pregen_progress);
                                // invoke sync in thump_compare_updater thread
                                Dispatcher.Invoke(() =>
                                    thumb_compare_gen.Set(cfi.CurrentThumbBmp)
                                );
                            };
                            thump_compare_updater.Trigger(TimeSpan.Zero, false);
                        };
                        var old_ind = cfi.ChosenThumbOptionInd;
                        cfi.ApplySourceAt(true, _ => { }, new_ind, null, out var initial_pos, set_pregen_progress);
                        // invoke sync in thump_compare_updater thread
                        Dispatcher.Invoke(() => Utils.HandleException(() =>
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
            Settings.Root.LastComparedFile = fname;
            curr_compare_fname = fname;
            b_reload_compare.IsEnabled = true;
        });

        var awaiting_mass_gen_lst = new List<string>();
        var delayed_mass_gen = new DelayedUpdater(() =>
        {
            string[] lst;
            lock (awaiting_mass_gen_lst)
            {
                lst = [.. awaiting_mass_gen_lst];
                awaiting_mass_gen_lst.Clear();
            }
            thumb_gen.MassGenerate(lst, true);
        }, "Thumb compare => mass gen");
        void apply_file_lst(string[] lst)
        {
            if (lst.Length==1)
            {
                tray_icon.ShowWin();
                begin_thumb_compare(lst.Single());
                return;
            }

            lock (awaiting_mass_gen_lst)
                awaiting_mass_gen_lst.AddRange(lst);
            delayed_mass_gen.Trigger(TimeSpan.Zero, false);
        }

        string[] extract_file_lst(IEnumerable<string> inp)
        {
            static T execute_any<T>(string descr, params Func<Action, T>[] funcs)
            {
                var wh = new System.Threading.ManualResetEventSlim(false);
                var excs = new List<Exception>();
                var left = funcs.Length;

                T? res = default;
                bool res_set = false;

                for (int i=0; i<funcs.Length; ++i)
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

            var exts = Settings.Root.AllowedExts;
            var es_arg = "ext:" + string.Join(';', exts);

            return inp.AsParallel().SelectMany(path =>
            {
                if (File.Exists(path))
                    return Enumerable.Repeat(path, exts.MatchesFile(path)?1:0);
                if (Directory.Exists(path))
                    return execute_any($"get files in [{path}]",
                        try_cancel =>
                        {
                            var res = new List<string>();
                            void on_dir(string dir)
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
                            var res = new List<string>();
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

        pipe.AddLoadCompareHandler(inp =>
        {
            var lst = extract_file_lst(inp);
            Dispatcher.BeginInvoke(() => Utils.HandleException(() => apply_file_lst(lst)));
        });

        foreach (var tcv in thumb_compare_all)
            tcv.MouseDown += (o, e) => Utils.HandleException(() =>
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    new FileChooser(inp => apply_file_lst(extract_file_lst(inp))).ShowDialog();
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

        b_swap_compare.Click += (o, e) => Utils.HandleException(() =>
        {

            var t = (c_thumb_compare_2.Child, c_thumb_compare_1.Child);
            (c_thumb_compare_1.Child, c_thumb_compare_2.Child) = (null, null);
            (c_thumb_compare_1.Child, c_thumb_compare_2.Child) = t;

            (tb_thumb_compare_1.Text, tb_thumb_compare_2.Text) = (tb_thumb_compare_2.Text, tb_thumb_compare_1.Text);

        });

        b_reload_compare.Click += (o, e) => Utils.HandleException(() =>
        {
            if (curr_compare_fname is null) return;
            begin_thumb_compare(curr_compare_fname);
        });

        (string[] inp, string[] lst)? drag_cache = null;
        void drag_handler(object o, DragEventArgs e)
        {
            e.Handled = true;
            e.Effects = DragDropEffects.None;

            var inp = (string[])e.Data.GetData(DataFormats.FileDrop);
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

        grid_thumb_compare.Drop += (o, e) => Utils.HandleException(() =>
        {
            var lst = drag_cache!.Value.lst;
            apply_file_lst(lst);
            e.Handled = true;
        });

        Closing += (o, e) => Utils.HandleException(clear_thumb_compare_file);
        grid_thumb_compare.AllowDrop = true;
    }

}
