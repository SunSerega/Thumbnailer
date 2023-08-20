using System;

using System.Threading;

using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Dashboard
{

	public sealed class CustomThreadPool
	{

		#region Observing

		public delegate void JobWork(Action<string?> change_subjob);
		private sealed class ThreadPoolJobDescription
		{
			public DateTime EnqTime { get; } = DateTime.Now;

			public required string Name { get; init; }
			public required JobWork Work { get; init; }

			public Thread? JobThread { get; set; } = null;
			public int WorkerIndex { get; set; } = -1;

			public string? CurrentSubJob { get; set; } = null;

		}

		public abstract class ThreadPoolObserver
		{

			public abstract void GetChanges(
				Action<object, int> on_rem_pending,
				Action<object, string, JobWork> on_add_pending,
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

			public ThreadPoolObserverImpl(CustomThreadPool root, Action update) {
				this.update = update;
				threads = Array.ConvertAll(root.items, item => item.Thread);

				newly_pending_jobs = new(root.items.Length, root.pending_jobs.Count);
				foreach (var job in root.pending_jobs)
					if (!newly_pending_jobs.TryAdd(job, default))
						throw new InvalidOperationException();
				if (!newly_pending_jobs.IsEmpty)
					update();

				newly_started_jobs = Array.ConvertAll(root.items, item => item.CurrentJob);
				old_pending_jobs = Array.ConvertAll(root.items, item => new List<ThreadPoolJobDescription>());

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

			private struct NoDictValue { }
			private readonly ConcurrentDictionary<ThreadPoolJobDescription, NoDictValue> newly_pending_jobs = new();
			public void MarkJobAdded(ThreadPoolJobDescription job) => Update(() =>
			{
				if (disposed)
					throw new InvalidOperationException();
				if (!newly_pending_jobs.TryAdd(job, default))
					throw new InvalidOperationException();
			});

			private readonly ThreadPoolJobDescription?[] newly_started_jobs;
			private readonly List<ThreadPoolJobDescription>[] old_pending_jobs;
			public void MarkJobStarted(ThreadPoolJobDescription job) => Update(() =>
			{
				var worker_ind = job.WorkerIndex;
				ValidateAccess(worker_ind);

				if (newly_started_jobs[worker_ind] != null)
					throw new InvalidOperationException();
				newly_started_jobs[worker_ind] = job;
				newly_finished_jobs[worker_ind] = false;

				if (!newly_pending_jobs.TryRemove(job, out _))
					old_pending_jobs[worker_ind].Add(job);

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
				Action<object, int> on_rem_pending,
				Action<object, string, JobWork> on_add_pending,
				Action<int, string, Thread> on_worker_accepted,
				Action<int, string?> on_subjob_changed,
				Action<int> on_finished
			) => change_lock.OneLocked(() =>
			{

				for (var i = 0; i < old_pending_jobs.Length; ++i)
				{
					var l = old_pending_jobs[i];
					foreach (var job in l)
						on_rem_pending(job, i);
					l.Clear();
				}

				foreach (var job in newly_pending_jobs.Keys.OrderBy(job=>job.EnqTime))
					on_add_pending(job, job.Name, job.Work);
				newly_pending_jobs.Clear();

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
		private readonly Dictionary<Thread, WeakReference<ThreadPoolObserverImpl>> observers = new();

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
		public event Action? PendingJobCountChanged;
		private void InvokePendingJobCountChanged() =>
			PendingJobCountChanged?.Invoke();

		public void AddJob(string name, JobWork work)
		{
			var job = new ThreadPoolJobDescription { Name=name, Work=work };
			ObservableAct(o => o.MarkJobAdded(job));
			pending_jobs.Add(job);
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
				this.ind=ind;
				thr = new(JobConsumingLoop) {
					IsBackground = true,
					Name = $"ThreadPoolItem[{ind}]",
				};
				thr.SetApartmentState(ApartmentState.STA);
				thr.Start();
			}

			private void ChangeState(ThreadPoolJobDescription? new_job)
			{
				if ((current_job is null) == (new_job is null))
					throw new InvalidOperationException();

				if (current_job is null)
				{
					new_job!.WorkerIndex = ind;
					root.ObservableAct(o => o.MarkJobStarted(new_job));
				}
				else
				{
					root.ObservableAct(o => o.MarkJobFinished(current_job));
					current_job.WorkerIndex = -2;
				}
				current_job = new_job;

				thr.IsBackground = new_job is null;

				Interlocked.Add(ref root.active_job_count, new_job is null ? -1 : +1);
				root.InvokeActiveJobsCountChanged();
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

						ChangeState(job);
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
}
