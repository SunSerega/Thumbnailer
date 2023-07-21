using System;

using System.Collections.Concurrent;

using System.Threading;

namespace Dashboard
{
	public class CustomThreadPool
	{
		private readonly ThreadPoolItem[] items;

		public int MaxJobCount { get => items.Length; }

		private readonly BlockingCollection<Action> pending_jobs = new();
		public event Action<Func<int>>? PendingJobCountChanged;

		public void AddJob(Action job)
		{
			pending_jobs.Add(job);
			PendingJobCountChanged?.Invoke(() => pending_jobs.Count);
		}

		private volatile int want_job_count = 0;
		private readonly object job_count_lock = new();
		public void SetJobCount(int c)
		{
			lock (job_count_lock)
			{
				Settings.Root.MaxJobCount = c;
				for (int i = want_job_count; i < c; i++)
					items[i].ev_resume.Set();
				want_job_count = c;
			}
		}

		private volatile int active_job_count = 0;
		public event Action<Func<int>>? ActiveJobsCountChanged;

		public CustomThreadPool(int max_size)
		{
			items = new ThreadPoolItem[max_size];
			for (int i = 0; i < items.Length; i++)
				items[i] = new ThreadPoolItem(this, i);
		}

		public sealed class ThreadPoolItem
		{
			private readonly CustomThreadPool root;
			private readonly int ind;
			private readonly Thread thr;

			public readonly ManualResetEventSlim ev_resume = new();
			public ThreadPoolItem(CustomThreadPool root, int ind)
			{
				this.root = root;
				this.ind = ind;
				thr = new(JobConsumingLoop);
				thr.SetApartmentState(ApartmentState.STA);
				thr.Start();
			}

			private bool last_job_state = false;
			private void ChangeState(bool state)
			{
				if (last_job_state == state)
					throw new InvalidOperationException();
				last_job_state = state;

				thr.IsBackground = !state;

				Interlocked.Add(ref root.active_job_count, state ? +1 : -1);
				root.ActiveJobsCountChanged?.Invoke(() => root.active_job_count);
			}

			private void JobConsumingLoop()
			{
				ChangeState(true);
				while (true)
					try
					{
						if (ind > root.want_job_count)
						{
							ChangeState(false);
							ev_resume.Wait();
							ChangeState(true);
							ev_resume.Reset();
							continue;
						}

						if (!root.pending_jobs.TryTake(out var job, 0))
						{
							ChangeState(false);
							job = root.pending_jobs.Take();
							ChangeState(true);
						}
						root.PendingJobCountChanged?.Invoke(() => root.pending_jobs.Count);

						job();
					}
					catch (Exception e)
					{
						Utils.HandleExtension(e);
					}
			}

		}

	}
}
