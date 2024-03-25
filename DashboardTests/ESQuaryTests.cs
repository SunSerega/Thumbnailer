using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Linq;

namespace Dashboard.Tests
{

	[TestClass()]
	public class ESQuaryTests
	{

		[TestMethod()]
		public void ESQuaryTest()
		{
			var dir = Environment.CurrentDirectory+@"\";
			var fls = new ESQuary(dir, "ext:dll").Select(fname => {
				if (!fname.StartsWith(dir))
					throw new FormatException(fname);
				return fname.Remove(0, dir.Length);
			}).ToDictionary(f => f, f => 0).Keys.ToHashSet();
			Assert.IsTrue(fls.IsSupersetOf([
				"DashboardTests.dll",
				"Dashboard for Thumbnailer.dll",
				"Microsoft.VisualStudio.TestPlatform.Common.dll",
				"Hardcodet.NotifyIcon.Wpf.dll",
			]));
		}

		[TestMethod()]
		public void ESQuaryStressTest()
		{
			using var enmr = new ESQuary("ext:avi;flac;gif;m4a;mkv;mov;mp3;mp4;ogg;webm;webp;psd;png;jpg").GetEnumerator();
			while (enmr.MoveNext())
				_ = enmr.Current;
		}

	}

}