using System;

using System.Linq;
using System.Collections;
using System.Collections.Generic;

using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Dashboard
{

	public partial class JobList : Window
	{

		public JobList(CustomThreadPool thr_pool)
		{
			InitializeComponent();

			var wjl = new WorkingJobList(thr_pool.MaxJobCount);

			var sp_pending = new StackPanel();

			Content = new StackPanel
			{
				Children = {
					new Border{
						Child = wjl,
						BorderBrush = Brushes.Black,
						BorderThickness = new Thickness(1),
					},
					sp_pending,
				}
			};

			var is_open = true;
			var change_wh = new System.Threading.ManualResetEventSlim(false);
			Closed += (o, e) =>
			{
				is_open = false;
				change_wh.Set();
			};

			new System.Threading.Thread(() => Utils.HandleExtension(() => thr_pool.ObserveLoop(change_wh.Set, observer =>
			{
				var pending_tb_map = new Dictionary<object, TextBlock>();

				while (is_open)
				{
					change_wh.Wait();
					change_wh.Reset();
					var next_update_time = DateTime.Now + TimeSpan.FromSeconds(1)/60;

					try
					{
						Dispatcher.Invoke(() => observer.GetChanges(
							(old_pending, ind) =>
							{
								if (!pending_tb_map.Remove(old_pending, out var tb))
									throw new InvalidOperationException();
								var b = wjl.HeaderBrush[ind];
								tb.Background = b;
								var c = ((SolidColorBrush)b).Color;
								tb.Foreground = new SolidColorBrush(Color.FromRgb(
									(byte)(255-c.R),
									(byte)(255-c.G),
									(byte)(255-c.B)
								));

								_=System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(1)).ContinueWith(_ =>
								{
									var anim = new DoubleAnimation
									{
										EasingFunction = new ExponentialEase()
										{
											EasingMode = EasingMode.EaseOut,
										},
										From = tb.ActualHeight,
										To = 0,
										Duration = TimeSpan.FromSeconds(0.5),
									};
									anim.Completed += (o, e) =>
									{
										var tb_ind = sp_pending.Children.IndexOf(tb);
										if (tb_ind == -1) throw new InvalidOperationException();
										sp_pending.Children.RemoveAt(tb_ind);
									};
									tb.BeginAnimation(TextBlock.HeightProperty, anim);
								}, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());

							},
							(new_pending, name, work) =>
							{
								var tb = new TextBlock
								{
									Text = name,
									Margin = new Thickness(2),
									Background = Brushes.LightGray,
								};
								pending_tb_map.Add(new_pending, tb);
								sp_pending.Children.Insert(0, tb);

								tb.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
								var anim = new DoubleAnimation
								{
									EasingFunction = new ExponentialEase()
									{
										EasingMode = EasingMode.EaseOut,
									},
									From = 0,
									To = tb.DesiredSize.Height,
									Duration = TimeSpan.FromSeconds(0.5),
								};
								tb.BeginAnimation(TextBlock.HeightProperty, anim);

							},
							(ind, name, thr) =>
							{
								wjl.ChangeJob(ind, name);
							},
							(ind, new_subjob) =>
							{
								wjl.ChangeSubJob(ind, new_subjob);
							},
							ind =>
							{
								wjl.ChangeJob(ind, null);
							}
						));
					}
					catch when (App.Current.IsShuttingDown)
					{
						return;
					}

					var wait_span = next_update_time - DateTime.Now;
					if (wait_span > TimeSpan.Zero)
						System.Threading.Thread.Sleep(wait_span);
				}

			})))
			{
				IsBackground = true,
				Name = $"Job list observation",
			}.Start();

		}

	}

	public sealed class WorkingJobList : FrameworkElement
	{
		private readonly Brush[] header_brushes;

		private readonly Line[] hor_lines;
		private readonly Line[] ver_lines;
		private readonly Rectangle[] header_rects;
		private readonly (TextBlock name, TextBlock subjob)[] content_tbs;
		private readonly Visual[] all_visuals;

		private static Line MakeLine() => new()
		{
			Stroke = Brushes.Black,
			StrokeThickness = 1,
		};
		private static TextBlock MakeTB() => new()
		{
			Margin = new Thickness(2),
			Background = Brushes.LightGray,
		};

		public WorkingJobList(int max_jobs)
		{
			
			header_brushes = Enumerable.Range(0, max_jobs).Select(i => new SolidColorBrush(
				ColorExtensions.FromAhsb(255, i/(double)max_jobs, 1, 1)
			)).ToArray();

			hor_lines = Enumerable.Range(0, max_jobs-1).Select(_ => MakeLine()).ToArray();
			ver_lines = Enumerable.Range(0, 2).Select(_ => MakeLine()).ToArray();
			header_rects = Array.ConvertAll(header_brushes, b => new Rectangle { Fill = b });
			content_tbs = Enumerable.Range(0, max_jobs).Select(_ => (MakeTB(), MakeTB())).ToArray();

			all_visuals = Enumerable.Empty<UIElement>()
				.Concat(hor_lines)
				.Concat(ver_lines)
				.Concat(header_rects)
				.Concat(content_tbs.Select(t => t.name))
				.Concat(content_tbs.Select(t => t.subjob))
				.ToArray();
			foreach (var v in all_visuals)
			{
				AddLogicalChild(v);
				AddVisualChild(v);
			}

			RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
		}

		public readonly struct HeaderBrushList : IReadOnlyList<Brush>
		{
			private readonly Brush[] a;
			public HeaderBrushList(WorkingJobList root) => a = root.header_brushes;

			public readonly Brush this[int i] => a[i];

			public readonly int Count => a.Length;

			public readonly IEnumerator<Brush> GetEnumerator() => a.AsEnumerable().GetEnumerator();

			readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		}
		public HeaderBrushList HeaderBrush => new(this);

		public void ChangeJob(int ind, string? name)
		{
			content_tbs[ind].name.Text = name??"";
			content_tbs[ind].name.Background =
				name is null ? Brushes.LightGray : Brushes.Transparent;
			if (name is null) ChangeSubJob(ind, null);
			InvalidateMeasure();
		}

		public void ChangeSubJob(int ind, string? subjob)
		{
			content_tbs[ind].subjob.Text = subjob??"";
			content_tbs[ind].subjob.Background =
				subjob is null ? Brushes.LightGray : Brushes.Transparent;
			InvalidateMeasure();
		}

		protected override int VisualChildrenCount => all_visuals.Length;
		protected override Visual GetVisualChild(int i) => all_visuals[i];

		private int max_header_w = 0;
		private double max_name_w = 0;
		private double max_subjob_w = 0;
		protected override Size MeasureOverride(Size sz)
		{
			var inf_sz = new Size(double.PositiveInfinity, double.PositiveInfinity);

			max_header_w = 0;
			max_name_w = 0;
			max_subjob_w = 0;

			foreach (var (name_tb, subjob_tb) in content_tbs)
			{

				name_tb.Measure(inf_sz);
				subjob_tb.Measure(inf_sz);

				if (name_tb.DesiredSize.Width>max_name_w)
					max_name_w = name_tb.DesiredSize.Width;
				if (subjob_tb.DesiredSize.Width>max_subjob_w)
					max_subjob_w = subjob_tb.DesiredSize.Width;

				var h = (int)Math.Ceiling(Math.Max(name_tb.DesiredSize.Height, subjob_tb.DesiredSize.Height));
				if (h > max_header_w)
					max_header_w = h;

			}
			foreach (var (name_tb, subjob_tb) in content_tbs)
			{
				name_tb.Measure(new(max_name_w, max_header_w));
				subjob_tb.Measure(new(max_name_w, max_header_w));
			}

			foreach (var header_r in header_rects)
			{
				header_r.Width = max_header_w;
				header_r.Height = max_header_w;
				//header_r.Measure(new(max_header_w,max_header_w));
			}

			sz = new(
				Math.Min(max_header_w+1+max_name_w+1+max_subjob_w, sz.Width),
				(max_header_w+1)*content_tbs.Length - 1
			);
			return sz;
		}

		protected override Size ArrangeOverride(Size sz)
		{
			var header_w = max_header_w;
			var name_w = max_name_w;
			var subjob_w = max_subjob_w;

			{
				var extra_w = sz.Width - (max_header_w+1+max_name_w+1+max_subjob_w);
				var w_diff = subjob_w - name_w;

				if (extra_w!=0 && w_diff!=0)
				{
					var s_extra = Math.Sign(extra_w);
					var d = s_extra * Math.Min(Math.Abs(extra_w), Math.Abs(w_diff));
					if (s_extra == Math.Sign(w_diff))
						name_w += d;
					else
						subjob_w += d;
					extra_w -= d;
				}

				{
					var d = extra_w/2;
					name_w += d;
					subjob_w += d;
				}
			}

			{
				var name_x = header_w;
				var subjob_x = name_x + name_w;

				ver_lines[0].X1 = name_x; ver_lines[0].Y1 = 0;
				ver_lines[0].X2 = name_x; ver_lines[0].Y2 = sz.Height;
				ver_lines[0].Arrange(new(sz));
				name_x += 1;

				ver_lines[1].X1 = subjob_x; ver_lines[1].Y1 = 0;
				ver_lines[1].X2 = subjob_x; ver_lines[1].Y2 = sz.Height;
				ver_lines[1].Arrange(new(sz));
				subjob_x += 1;

				var y = 0;
				foreach (var (header_r, (name_tb, subjob_tb)) in header_rects.Zip(content_tbs))
				{
					header_r.Arrange(new(0, y, header_w, header_w));
					name_tb.Arrange(new(name_x, y, name_w, header_w));
					subjob_tb.Arrange(new(subjob_x, y, subjob_w, header_w));
					y += header_w+1;
				}
			}

			{
				var y = 0;
				foreach (var l in hor_lines)
				{
					y += header_w+1;
					l.Y1 = y; l.X1 = 0;
					l.Y2 = y; l.X2 = sz.Width;
					l.Arrange(new(sz));
				}
			}

			sz = new(
				header_w +1+ name_w +1+ subjob_w,
				(max_header_w+1)*content_tbs.Length - 1
			);
			return sz;
		}

	}

}
