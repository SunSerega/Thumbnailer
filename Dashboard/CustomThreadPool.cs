using System;

using System.Threading;

using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Dashboard;

public sealed class CustomThreadPool
{

    #region Observing

    public delegate void JobWork(Action<string?> change_subjob);
    public abstract class ThreadPoolJobHeader { }
    private sealed class ThreadPoolJobDescription : ThreadPoolJobHeader
    {
        public DateTime EnqTime { get; } = DateTime.Now;

        public required string Name { get; init; }
        public required JobWork Work { get; init; }

        public int WorkerIndex { get; set; } = -1;
        public bool IsPending => WorkerIndex == -1;

        public string? CurrentSubJob { get; set; } = null;

        public bool HasFinished { get; set; } = false;

    }

    public abstract class ThreadPoolObserver
    {

        public abstract void GetChanges(
            Action<ThreadPoolJobHeader, string, JobWork> on_add_pending,
            Action<ThreadPoolJobHeader, int> on_rem_pending,
            Action<int, string, Thread> on_worker_accepted,
            Action<int, string?> on_subjob_changed,
            Action<int> on_finished
        );

    }

    private sealed class ThreadPoolObserverImpl : ThreadPoolObserver
    {
        private readonly Action update;
        private readonly Thread[] threads;
        private readonly OneToManyLock change_lock = new();

        public ThreadPoolObserverImpl(CustomThreadPool root, Action update)
        {
            this.update = update;
            threads = Array.ConvertAll(root.items, item => item.Thread);

            new_pending_jobs = new(root.pending_jobs);
            if (!new_pending_jobs.IsEmpty)
                update();

            newly_started_jobs = Array.ConvertAll(root.items, item => item.CurrentJob);

            changed_subjobs = Array.ConvertAll(newly_started_jobs, job =>
            {
                if (job?.CurrentSubJob is null) return default(ValueTuple<string?>?);
                this.update();
                return ValueTuple.Create(job.CurrentSubJob);
            });

            newly_finished_jobs = new bool[root.items.Length];

        }

        private void ValidateAccess(int worker_ind)
        {
            if (disposed)
                throw new InvalidOperationException();
            if (threads[worker_ind] != Thread.CurrentThread)
                throw new InvalidOperationException();
        }

        private void Update(Action change)
        {
            change_lock.ManyLocked(change);
            update();
        }

        private readonly ConcurrentStack<ThreadPoolJobDescription> new_pending_jobs;
        public void MarkJobAdded(ThreadPoolJobDescription job) => Update(() =>
        {
            if (disposed)
                throw new InvalidOperationException();
            new_pending_jobs.Push(job);
        });

        private readonly ConcurrentDictionary<ThreadPoolJobDescription, int> old_pending_job_counts = new();
        public void MarkJobDupRemoved(ThreadPoolJobDescription job) => Update(() =>
        {
            old_pending_job_counts.AddOrUpdate(job, 1, (_, c) => c+1);
        });

        private readonly ThreadPoolJobDescription?[] newly_started_jobs;
        public void MarkJobStarted(ThreadPoolJobDescription job) => Update(() =>
        {
            var worker_ind = job.WorkerIndex;
            ValidateAccess(worker_ind);

            if (newly_started_jobs[worker_ind] != null)
                throw new InvalidOperationException();
            newly_started_jobs[worker_ind] = job;
            newly_finished_jobs[worker_ind] = false;

            old_pending_job_counts.AddOrUpdate(job, 1, (_, c) => c+1);
        });

        private readonly ValueTuple<string?>?[] changed_subjobs;
        public void MarkSubJobChanged(ThreadPoolJobDescription job) => Update(() =>
        {
            var worker_ind = job.WorkerIndex;
            ValidateAccess(worker_ind);
            changed_subjobs[worker_ind] = new(job.CurrentSubJob);
        });

        private readonly bool[] newly_finished_jobs;
        public void MarkJobFinished(ThreadPoolJobDescription job) => Update(() =>
        {
            var worker_ind = job.WorkerIndex;
            ValidateAccess(worker_ind);

            if (newly_started_jobs[worker_ind] != null)
            {
                if (newly_started_jobs[worker_ind] != job)
                    throw new InvalidOperationException();
                newly_started_jobs[worker_ind] = null;
                return;
            }

            if (newly_finished_jobs[worker_ind])
                throw new InvalidOperationException();
            newly_finished_jobs[worker_ind] = true;

        });

        public override void GetChanges(
            Action<ThreadPoolJobHeader, string, JobWork> on_add_pending,
            Action<ThreadPoolJobHeader, int> on_rem_pending,
            Action<int, string, Thread> on_worker_accepted,
            Action<int, string?> on_subjob_changed,
            Action<int> on_finished
        ) => change_lock.OneLocked(() =>
        {

            var old_pending_job_counts = this.old_pending_job_counts.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            this.old_pending_job_counts.Clear();

            while (new_pending_jobs.TryPop(out var job))
            {
                old_pending_job_counts.TryGetValue(job, out var rem_c);
                if (rem_c == 0)
                    on_add_pending(job, job.Name, job.Work);
                else
                    old_pending_job_counts[job] = rem_c - 1;
            }

            foreach (var kvp in old_pending_job_counts)
                for (var i = 0; i < kvp.Value; ++i)
                    on_rem_pending(kvp.Key, kvp.Key.WorkerIndex);

            for (var i = 0; i < newly_started_jobs.Length; ++i)
            {
                if (newly_started_jobs[i] is null) continue;
                on_worker_accepted(i, newly_started_jobs[i]!.Name, threads[i]);
                newly_started_jobs[i] = null;
            }

            for (var i = 0; i < changed_subjobs.Length; ++i)
            {
                var sj = changed_subjobs[i];
                if (sj is null) continue;
                on_subjob_changed(i, sj.Value.Item1);
                changed_subjobs[i] = null;
            }

            for (var i = 0; i < newly_finished_jobs.Length; ++i)
            {
                if (!newly_finished_jobs[i]) continue;
                on_finished(i);
                newly_finished_jobs[i] = false;
            }

        }, with_priority: true);

        private bool disposed = false;
        public void Dispose()
        {
            if (disposed)
                throw new InvalidOperationException();
            disposed = true;
        }
        ~ThreadPoolObserverImpl()
        {
            if (disposed) return;
            CustomMessageBox.Show("ThreadPoolObserver wasn't properly disposed off");
        }

    }

    private readonly OneToManyLock observers_lock = new();
    private readonly Dictionary<Thread, WeakReference<ThreadPoolObserverImpl>> observers = [];

    public ThreadPoolObserver BeginObserving(Action update) => observers_lock.OneLocked(() =>
    {
        var thr = Thread.CurrentThread;
        ThreadPoolObserverImpl res = new(this, update);
        if (!observers.TryAdd(thr, new(res)))
        {
            res.Dispose();
            throw new InvalidOperationException();
        }
        return res;
    }, with_priority: true);

    public void EndObserving(ThreadPoolObserver o) => observers_lock.OneLocked(() =>
    {
        var thr = Thread.CurrentThread;
        if (!observers.Remove(thr, out var r))
            throw new InvalidOperationException();
        if (!r.TryGetTarget(out var stored_o))
            throw new InvalidOperationException();
        if (stored_o != o)
            throw new InvalidOperationException();
        stored_o.Dispose();
    }, with_priority: true);

    public void ObserveLoop(Action update, Action<ThreadPoolObserver> loop)
    {
        var observer = BeginObserving(update);
        try
        {
            loop(observer);
        }
        finally
        {
            EndObserving(observer);
        }
    }

    private void ObservableAct(Action<ThreadPoolObserverImpl> on_observer/*, Action act*/) => observers_lock.ManyLocked(() =>
    {
        foreach (var r in observers.Values)
        {
            if (!r.TryGetTarget(out var o))
                throw new InvalidOperationException();
            on_observer(o);
        }
        //act();
    });

    #endregion

    #region Pending

    private readonly BlockingCollection<ThreadPoolJobDescription> pending_jobs = new(new ConcurrentStack<ThreadPoolJobDescription>());
    public int PendingJobCount => pending_jobs.Count;
    public int PendingUniqueJobCount => pending_jobs.Where(job=>job.IsPending).Distinct().Count();
    public event Action? PendingJobCountChanged;
    private void InvokePendingJobCountChanged() =>
        PendingJobCountChanged?.Invoke();

    public ThreadPoolJobHeader AddJob(string name, JobWork work)
    {
        var job = new ThreadPoolJobDescription { Name=name, Work=work };
        ObservableAct(o => o.MarkJobAdded(job));
        pending_jobs.Add(job);
        InvokePendingJobCountChanged();
        return job;
    }

    private readonly ConcurrentDictionary<ThreadPoolJobDescription, int> pending_job_dups = new();
    private void PendingJobUnduped(ThreadPoolJobDescription job)
    {
        var dup_c = pending_job_dups.AddOrUpdate(job,
            _ => throw new InvalidOperationException(),
            (_, c) => c - 1
        );
        if (dup_c==0)
            pending_job_dups.TryRemove(job, out _);
        ObservableAct(o => o.MarkJobDupRemoved(job));
    }
    public void BumpJob(ThreadPoolJobHeader job_obj)
    {
        var job = (ThreadPoolJobDescription)job_obj;
        if (!job.IsPending) return;
        pending_job_dups.AddOrUpdate(job, 1, (_, c) => c + 1);
        pending_jobs.Add(job);
        ObservableAct(o => o.MarkJobAdded(job));
        InvokePendingJobCountChanged();
    }

    #endregion

    #region Items

    private sealed class ThreadPoolItem
    {
        private readonly CustomThreadPool root;
        private readonly int ind;

        private readonly Thread thr;
        public Thread Thread => thr;

        private ThreadPoolJobDescription? current_job = null;
        public ThreadPoolJobDescription? CurrentJob => current_job;

        public readonly ManualResetEventSlim suspended_wh = new(false);
        public ThreadPoolItem(CustomThreadPool root, int ind)
        {
            this.root = root;
            this.ind = ind;
            thr = new(JobConsumingLoop) {
                IsBackground = true,
                Name = $"ThreadPoolItem[{ind}]",
            };
            thr.SetApartmentState(ApartmentState.STA);
            thr.Start();
        }

        private bool ChangeState(ThreadPoolJobDescription? new_job)
        {
            if ((current_job is null) == (new_job is null))
                throw new InvalidOperationException();

            if (current_job is null)
            {
                if (new_job is null) throw null!;

                bool should_execute;
                using (var new_job_locker = new ObjectLocker(new_job))
                {
                    should_execute = new_job.IsPending;
                    if (should_execute)
                        new_job.WorkerIndex = this.ind;
                }

                if (!should_execute)
                {
                    root.PendingJobUnduped(new_job);
                    return false;
                }

                root.ObservableAct(o => o.MarkJobStarted(new_job));
            }
            else
            {
                root.ObservableAct(o => o.MarkJobFinished(current_job));
                current_job.HasFinished = true;
            }
            current_job = new_job;

            thr.IsBackground = new_job is null;

            Interlocked.Add(ref root.active_job_count, new_job is null ? -1 : +1);
            root.InvokeActiveJobsCountChanged();
            return true;
        }

        private void JobConsumingLoop()
        {
            while (true)
                try
                {
                    suspended_wh.Wait();

                    var job = root.pending_jobs.Take();
                    if (!suspended_wh.IsSet)
                    {
                        root.pending_jobs.Add(job);
                        root.InvokePendingJobCountChanged();
                        continue;
                    }
                    root.InvokePendingJobCountChanged();

                    if (App.Current?.IsShuttingDown??false)
                        break;

                    if (!ChangeState(job))
                        continue;
                    try
                    {
                        job.Work(sj => {
                            job.CurrentSubJob = sj;
                            root.ObservableAct(o => o.MarkSubJobChanged(job));
                        });
                    }
                    finally
                    {
                        ChangeState(null);
                    }
                }
                catch (Exception e)
                {
                    Utils.HandleException(e);
                }
        }

    }

    private readonly ThreadPoolItem[] items;

    public int MaxJobCount { get => items.Length; }

    #endregion

    #region Current usage degree

    private volatile int want_job_count = 0;
    private readonly object job_count_lock = new();
    public void SetJobCount(int c)
    {
        if (want_job_count == c) return;
        using var job_count_locker = new ObjectLocker(job_count_lock);
        if (want_job_count == c) return;

        for (int i = want_job_count; i < c; i++)
            items[i].suspended_wh.Set();
        for (int i = c; i < want_job_count; i++)
            items[i].suspended_wh.Reset();

        Settings.Root.MaxJobCount = c;
        want_job_count = c;
    }

    private volatile int active_job_count = 0;
    public int ActiveJobsCount => active_job_count;
    public event Action? ActiveJobsCountChanged;
    private void InvokeActiveJobsCountChanged() =>
        ActiveJobsCountChanged?.Invoke();

    #endregion

    public CustomThreadPool(int max_size)
    {
        items = new ThreadPoolItem[max_size];
        for (int i = 0; i < items.Length; i++)
            items[i] = new ThreadPoolItem(this, i);
    }

}
