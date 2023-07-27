using System;
using System.Threading;

namespace Dashboard
{

	public class DelayedUpdater
	{
		private readonly ManualResetEventSlim ev = new(false);
		private DateTime save_time;
		private bool can_delay_further = true;
		private bool is_shut_down = false;

		private static ThreadStart MakeThreadStart(
			Action update,
			ManualResetEventSlim ev,
			WeakReference<DelayedUpdater> controller_ref
		) => () =>
		{
			while (true)
				try
				{
					ev.Wait();

					while (true)
					{
						if (!controller_ref.TryGetTarget(out var controller))
							return;
						var wait_left = controller.save_time - DateTime.Now;
						controller = null;
						if (wait_left <= TimeSpan.Zero) break;
						Thread.Sleep(wait_left);
					}
					{
						if (!controller_ref.TryGetTarget(out var controller))
							return;
						if (controller.is_shut_down)
							return;
						controller.can_delay_further = true;
						controller = null;
					}

					ev.Reset();
					Thread.CurrentThread.IsBackground = false;
					try
					{
						update();
					}
					finally
					{
						Thread.CurrentThread.IsBackground = true;
					}
				}
				catch (Exception e)
				{
					Utils.HandleExtension(e);
				}
		};

		public DelayedUpdater(Action update)
		{

			new Thread(MakeThreadStart(update, ev, new(this)))
			{
				IsBackground=true
			}.Start();

		}

		public void Trigger(TimeSpan delay, bool can_delay_further)
		{
			var next_time = DateTime.Now + delay;
			if (
				!ev.IsSet ||
				(!can_delay_further && next_time<save_time) ||
				(this.can_delay_further && next_time>save_time)
			)
			{
				save_time = next_time;
				this.can_delay_further = can_delay_further;
				ev.Set();
			}
		}

		public void Shutdown()
		{
			is_shut_down = true;
			ev.Set();
		}

		~DelayedUpdater() => Shutdown();

	}

}