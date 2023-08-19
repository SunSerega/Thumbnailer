using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

using System.Threading;

using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;

namespace Dashboard.Tests
{
	[TestClass()]
	public class OneToManyLockTests
	{

		[TestMethod()]
		public void OneToManyLockTest()
		{
			var l = new OneToManyLock();

			var one_priority = true;

			var one_thr_c = 3;
			var many_thr_c = 100;

			var one_rep_c = 10;
			var many_rep_c = 1000;

			var counter_lock = new object();
			var one_counter = 0;
			var many_counter = 0;

			var threads = new List<Thread>();

			var done_one = 0;
			var done_many = 0;

			for (var i = 0; i<one_thr_c; ++i)
				threads.Add(new Thread(() => Utils.HandleException(() =>
				{
					Thread.Sleep(1000);
					for (int i = 0; i<one_rep_c; ++i)
					{
						l.OneLocked(() =>
						{
							lock (counter_lock)
								one_counter++;
							Thread.Sleep(100);
							lock (counter_lock)
								one_counter--;
						}, one_priority);
						Thread.Sleep(1000);
						Interlocked.Increment(ref done_one);
					}
				}))
				{
					Name=$"One thr [{i}]"
				});

			for (var i = 0; i<many_thr_c; ++i)
				threads.Add(new Thread(() => Utils.HandleException(() =>
				{
					Thread.Sleep(1000);
					for (int i = 0; i<many_rep_c; ++i)
					{
						l.ManyLocked(() =>
						{
							lock (counter_lock)
								many_counter++;
							Thread.Sleep(10);
							lock (counter_lock)
								many_counter--;
						});
						//Thread.Sleep(1000);
						Interlocked.Increment(ref done_many);
					}
				}))
				{
					Name=$"Many thr [{i}]"
				});

			foreach (var thr in threads)
				thr.Start();

			var win_thr = new Thread(() => Utils.HandleException(() =>
			{
				static UIElement make_otp_line(string name, out TextBlock tb) => new Viewbox
				{
					Child = new StackPanel
					{
						Orientation = Orientation.Horizontal,
						Children =
						{
							new TextBlock{ Text = $"{name}: " },
							(tb = new TextBlock()),
						}
					}
				};

				var g = new Grid
				{
					Children =
						{
							make_otp_line("tested (one)", out var tb_tested_one),
							make_otp_line("tested (many)", out var tb_tested_many),
							make_otp_line("one", out var tb_one),
							make_otp_line("many", out var tb_many),
						}
				};
				foreach (UIElement child in g.Children)
				{
					Grid.SetRow(child, g.RowDefinitions.Count);
					g.RowDefinitions.Add(new() {
						Height=new(1, GridUnitType.Star)
					});
				}
				var w = new Window() {
					Content = g,
					WindowState = WindowState.Maximized,
				};

				new DispatcherTimer(TimeSpan.FromSeconds(1/60d), DispatcherPriority.Normal, (o, e) =>
				{
					using var counter_locker = new ObjectLocker(counter_lock);

					if (one_counter!=0 && many_counter!=0)
						throw new InvalidOperationException();
					if (one_counter>1)
						throw new InvalidOperationException();
					if (many_counter>many_thr_c)
						throw new InvalidOperationException();

					tb_tested_one.Text = $"{done_one} / {one_thr_c*one_rep_c}";
					tb_tested_many.Text = $"{done_many} / {many_thr_c*many_rep_c}";
					tb_one.Text = one_counter.ToString();
					tb_many.Text = many_counter.ToString();

				}, w.Dispatcher).Start();

				w.Show();
				Dispatcher.Run();
			}));
			win_thr.SetApartmentState(ApartmentState.STA);
			win_thr.Start();

			foreach (var thr in threads)
				thr.Join();

			Dispatcher.FromThread(win_thr).InvokeShutdown();

			win_thr.Join();
		}

	}
}