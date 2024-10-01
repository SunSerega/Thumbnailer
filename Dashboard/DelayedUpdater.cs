using System;

using System.Threading;

using System.Linq;
using System.Collections.Concurrent;

namespace Dashboard;

public class DelayedUpdater
{
    private readonly ManualResetEventSlim ev = new(false);
    private DateTime saved_time;
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
                    var wait_left = controller.saved_time - DateTime.Now;
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
            catch when (App.Current?.IsShuttingDown??false)
            {
                break;
            }
            catch (Exception e)
            {
                Utils.HandleException(e);
            }
    };

    public DelayedUpdater(Action update, string description)
    {
        var thr = new Thread(MakeThreadStart(update, ev, new(this)))
        {
            IsBackground=true,
            Name = $"{nameof(DelayedUpdater)}: {description}",
        };
        thr.SetApartmentState(ApartmentState.STA);
        thr.Start();
    }

    public void Trigger(TimeSpan delay, bool can_delay_further)
    {
        var next_time = DateTime.Now + delay;
        if (
            !ev.IsSet ||
            (!can_delay_further && next_time<saved_time) ||
            (this.can_delay_further && next_time>saved_time)
        )
        {
            saved_time = next_time;
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

public class DelayedMultiUpdater<TKey>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, (DateTime time, bool cdf)> updatables = new();
    private readonly ManualResetEventSlim ev = new(false);
    private readonly Action<TKey> update;

    public DelayedMultiUpdater(Action<TKey> update, TimeSpan max_wait, string description)
    {
        this.update = update;
        new Thread(() =>
        {
            while (true)
                try
                {
                    if (updatables.IsEmpty)
                    {
                        ev.Wait();
                        ev.Reset();
                        continue;
                    }

                    var kvp = updatables.MinBy(kvp => kvp.Value.time);
                    {
                        var wait = kvp.Value.time-DateTime.Now;
                        if (wait > TimeSpan.Zero)
                        {
                            if (wait > max_wait)
                                wait = max_wait;
                            Thread.Sleep(wait);
                            continue;
                        }
                    }
                    if (!updatables.TryRemove(kvp))
                        continue;

                    try
                    {
                        Thread.CurrentThread.IsBackground = false;
                        update(kvp.Key);
                    }
                    finally
                    {
                        Thread.CurrentThread.IsBackground = true;
                    }

                }
                catch (Exception e)
                {
                    Utils.HandleException(e);
                }
        })
        {
            IsBackground = true,
            Name = $"{nameof(DelayedMultiUpdater<TKey>)}<{typeof(TKey)}>: {description}",
        }.Start();
    }

    public void Trigger(TKey key, TimeSpan delay, bool? can_delay_further)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        var next_val = (time: DateTime.Now + delay, cdf: can_delay_further??false);
        var need_set_ev = true;
        updatables.AddOrUpdate(key, next_val, (key, old_val) =>
        {
            if (can_delay_further is null)
                throw new InvalidOperationException();
            if (
                !ev.IsSet ||
                (!next_val.cdf && next_val.time<old_val.time) ||
                (old_val.cdf && next_val.time>old_val.time)
            )
            {
                return next_val;
            }
            need_set_ev = false;
            return old_val;
        });
        if (need_set_ev)
            ev.Set();
    }

}