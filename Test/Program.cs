


using Dashboard;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml.Linq;

partial class Test
{
	record struct RawData(string Source, string? Info) { }

	static void Main()
	{
		var thr_pool = new CustomThreadPool(10);
		thr_pool.SetJobCount(6);

		var raw_data = new ConcurrentDictionary<string, ConcurrentBag<RawData>>();
		var errors = new ConcurrentDictionary<string, Exception>();
		void add_xml_node(string key, XElement n, string source)
		{

			raw_data.GetOrAdd(key, _ => []).Add(new(source, null));

			foreach (var attrib in n.Attributes())
				raw_data.GetOrAdd($"{key}::{attrib.Name}", _ => []).Add(new(source, attrib.Value));

			foreach (var sub_n in n.Descendants())
				add_xml_node($"{key}.{sub_n.Name}", sub_n, source);

		}

		var found_c = 0;
		var done_c = 0;
		var pre_done_c = 0;
		var sw1 = Stopwatch.StartNew();
		var sw2 = new Stopwatch();

		var otp_wh = new ManualResetEventSlim(false);
		var finished_enmr = false;
		var otp_thr = new Thread(() =>
		{
			while (true)
			{
				otp_wh.Wait();
				otp_wh.Reset();
				var l_done_c = done_c;
				var l_found_c = found_c;

				Console.SetCursorPosition(0, 0);
				
				static void Print(string s)
				{
					foreach (var l in s.Replace("\r", "").Split('\n'))
					{
						var buff_w = Console.BufferWidth;
						var need_w = (l.Length+buff_w-1)/buff_w*buff_w;
						Console.Write(l.PadRight(need_w));
					}
				}

				Print($"{l_done_c}/{l_found_c} ({l_done_c/(double)l_found_c:P2})");

				{
					static string done_time(Stopwatch sw, int done, int total)
					{
						string res = "?";
						try
						{
							var done_in_sec = sw.Elapsed.TotalSeconds * (total/(double)done - 1);
							res = done_in_sec.ToString();
							res = TimeSpan.FromSeconds((long)done_in_sec).ToString();
						}
						catch (OverflowException) { }
						return res;
					}
					Print($"Done in {done_time(sw2, l_done_c, l_found_c)} ~ {done_time(sw2, l_done_c-pre_done_c, l_found_c-pre_done_c)} ~ {done_time(sw1, l_done_c, l_found_c)}");
				}

				if (!errors.IsEmpty)
				{
					Print($"Errors:");
					foreach (var g in errors.GroupBy(kvp => kvp.Value.ToString()))
					{
						Print($"In {g.Count()} files, like: {g.First().Key}");
						Print(g.Key);
					}
				}

				if (finished_enmr && l_done_c == l_found_c) break;
				Thread.Sleep(100);
			}
		})
		{
			Name = "Output"
		};
		otp_thr.Start();

		foreach (var fname in new ESQuary("")/**.Take(1000)/**/)
		{
			if (fname.Contains("$RECYCLE.BIN")) continue;
			found_c += 1;
			thr_pool.AddJob(fname, change_subjob =>
			{
				try
				{

					//change_subjob("getting metadata");
					var metadata_s = FFmpeg.Invoke($"-i \"{fname}\" -hide_banner -show_format -show_streams -print_format xml", () => true, exe: "probe").Output!;
					//change_subjob(null);

					//change_subjob("parsing metadata XML");
					var metadata_xml = XDocument.Parse(metadata_s).Root ?? throw new InvalidOperationException("No xml root");
					//change_subjob(null);

					add_xml_node(metadata_xml.Name.LocalName, metadata_xml, fname);

				}
				catch (Exception e)
				{
					if (!errors.TryAdd(fname, e))
						throw null!;
				}
				finally
				{
					Interlocked.Increment(ref done_c);
					otp_wh.Set();
				}
			});
			pre_done_c = done_c;
		}
		finished_enmr = true;
		otp_wh.Set();
		thr_pool.SetJobCount(thr_pool.MaxJobCount);
		sw2.Start();

		otp_thr.Join();
		Console.WriteLine("Done running ffmpeg");

		//var tw = Console.Out;
		TextWriter tw = new StreamWriter(File.Create("otp.txt"), new System.Text.UTF8Encoding(true));

		foreach (var (key, bag) in raw_data.OrderBy(kvp=>kvp.Key))
		{
			tw.WriteLine(new string('=', 30));
			var lu = bag.ToLookup(d => d.Info, d => d.Source);
			tw.WriteLine($"{key}: {lu.Count}");
			foreach (var g in lu/**.Take(10)/**/)
				tw.WriteLine($"[{g.Key??"<null>"}] in {g.Count()} files like [{g.First()}]");
			//if (lu.Count>10)
			//	tw.WriteLine("...");
		}

		tw.Close();
		//Console.ReadLine();
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Test")]
	public static unsafe void ReplaceThumbnail(string filePath, string newThumbnailPath)
	{
		//var newThumbnail = new Bitmap(newThumbnailPath);

		SHCreateItemFromParsingName(filePath, IntPtr.Zero, typeof(IShellItem).GUID, out var item);

		var CLSID_LocalThumbnailCache = new Guid("50EF4544-AC9F-4A8E-B21B-8A26180DB13F");
		var thumbnailCache = (IThumbnailCache)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_LocalThumbnailCache, true)!)!;

		{

			var thumbnailCachePrivate = (IThumbnailCachePrivate)thumbnailCache;

			thumbnailCachePrivate.DeleteThumbnail(new WTS_THUMBNAILID());
		}

		Console.WriteLine(filePath);

		try
		{
			thumbnailCache.GetThumbnail(item, int.MaxValue, WTS_FLAGS.WTS_INCACHEONLY, out var sharedBitmap, out var cacheFlags, out var id);

			var res1 = sharedBitmap.GetFormat(out var format);

			var res2 = sharedBitmap.GetSize(out var size);

			var res3 = sharedBitmap.GetSharedBitmap(out var hbmp);

			Image.FromHbitmap(hbmp).Save(@"G:\0Prog\Thumbnailer\Test\1.bmp");

			//var CLSID_LocalThumbnailCache = new Guid("50EF4544-AC9F-4A8E-B21B-8A26180DB13F");
			//var thumbnailCachePrivate = (IThumbnailCachePrivate)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_LocalThumbnailCache, true)!)!;
			var thumbnailCachePrivate = (IThumbnailCachePrivate)thumbnailCache;

			var res7 = thumbnailCachePrivate.DeleteThumbnail(id);
			Console.WriteLine("deleted");

			SHChangeNotify(HChangeNotifyEventID.SHCNE_UPDATEITEM, HChangeNotifyFlags.SHCNF_PATHW, filePath, IntPtr.Zero);

		}
		catch (COMException e) when (e.HResult == unchecked((int)0x80030002))
		{
			Console.WriteLine("missing");
		}
		//var bd = newThumbnail.LockBits(new Rectangle(default, newThumbnail.Size), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
		//sharedBitmap.InitializeBitmap(bd.Scan0, WTS_ALPHATYPE.WTSAT_RGB);
		//var res4 = sharedBitmap.InitializeBitmap(newThumbnail.GetHbitmap(), WTS_ALPHATYPE.WTSAT_ARGB);

		//var res5 = sharedBitmap.GetSharedBitmap(out var hbmp2);

		//Bitmap.FromHbitmap(hbmp2).Save(@"G:\0Prog\Thumbnailer\Test\2.bmp");

		//thumbnailCache.GetThumbnail(item, int.MaxValue, WTS_FLAGS.WTS_INCACHEONLY, out var sharedBitmap2, out var cacheFlags2, out _);

		//var res6 = sharedBitmap2.GetSharedBitmap(out var hbmp3);

		//Bitmap.FromHbitmap(hbmp3).Save(@"G:\0Prog\Thumbnailer\Test\3.bmp");

		thumbnailCache.GetThumbnail(item, int.MaxValue, WTS_FLAGS.WTS_EXTRACT, out _, out _, out _);

	}

	[LibraryImport("shell32.dll")]
	static partial void SHChangeNotify(
		HChangeNotifyEventID wEventId,
		HChangeNotifyFlags uFlags,
		[MarshalAs(UnmanagedType.LPWStr)] string dwItem1,
		IntPtr dwItem2
	);

	[ComImport]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("3413b9cd-0db3-4e97-8bf4-68fb100d1815")]
	public interface IThumbnailCachePrivate
	{
		void MethodDummy0();
		void MethodDummy1();
		void MethodDummy2();
		void MethodDummy3();
		void MethodDummy4();
		void MethodDummy5();

		uint DeleteThumbnail(WTS_THUMBNAILID id);

	}

	#region enum HChangeNotifyEventID
	/// <summary>
	/// Describes the event that has occurred.
	/// Typically, only one event is specified at a time.
	/// If more than one event is specified, the values contained
	/// in the <i>dwItem1</i> and <i>dwItem2</i>
	/// parameters must be the same, respectively, for all specified events.
	/// This parameter can be one or more of the following values.
	/// </summary>
	/// <remarks>
	/// <para><b>Windows NT/2000/XP:</b> <i>dwItem2</i> contains the index
	/// in the system image list that has changed.
	/// <i>dwItem1</i> is not used and should be <see langword="null"/>.</para>
	/// <para><b>Windows 95/98:</b> <i>dwItem1</i> contains the index
	/// in the system image list that has changed.
	/// <i>dwItem2</i> is not used and should be <see langword="null"/>.</para>
	/// </remarks>
	[Flags]
	enum HChangeNotifyEventID
	{
		/// <summary>
		/// All events have occurred.
		/// </summary>
		SHCNE_ALLEVENTS = 0x7FFFFFFF,

		/// <summary>
		/// A file type association has changed. <see cref="HChangeNotifyFlags.SHCNF_IDLIST"/>
		/// must be specified in the <i>uFlags</i> parameter.
		/// <i>dwItem1</i> and <i>dwItem2</i> are not used and must be <see langword="null"/>.
		/// </summary>
		SHCNE_ASSOCCHANGED = 0x08000000,

		/// <summary>
		/// The attributes of an item or folder have changed.
		/// <see cref="HChangeNotifyFlags.SHCNF_IDLIST"/> or
		/// <see cref="HChangeNotifyFlags.SHCNF_PATH"/> must be specified in <i>uFlags</i>.
		/// <i>dwItem1</i> contains the item or folder that has changed.
		/// <i>dwItem2</i> is not used and should be <see langword="null"/>.
		/// </summary>
		SHCNE_ATTRIBUTES = 0x00000800,

		/// <summary>
		/// A nonfolder item has been created.
		/// <see cref="HChangeNotifyFlags.SHCNF_IDLIST"/> or
		/// <see cref="HChangeNotifyFlags.SHCNF_PATH"/> must be specified in <i>uFlags</i>.
		/// <i>dwItem1</i> contains the item that was created.
		/// <i>dwItem2</i> is not used and should be <see langword="null"/>.
		/// </summary>
		SHCNE_CREATE = 0x00000002,

		/// <summary>
		/// A nonfolder item has been deleted.
		/// <see cref="HChangeNotifyFlags.SHCNF_IDLIST"/> or
		/// <see cref="HChangeNotifyFlags.SHCNF_PATH"/> must be specified in <i>uFlags</i>.
		/// <i>dwItem1</i> contains the item that was deleted.
		/// <i>dwItem2</i> is not used and should be <see langword="null"/>.
		/// </summary>
		SHCNE_DELETE = 0x00000004,

		/// <summary>
		/// A drive has been added.
		/// <see cref="HChangeNotifyFlags.SHCNF_IDLIST"/> or
		/// <see cref="HChangeNotifyFlags.SHCNF_PATH"/> must be specified in <i>uFlags</i>.
		/// <i>dwItem1</i> contains the root of the drive that was added.
		/// <i>dwItem2</i> is not used and should be <see langword="null"/>.
		/// </summary>
		SHCNE_DRIVEADD = 0x00000100,

		/// <summary>
		/// A drive has been added and the Shell should create a new window for the drive.
		/// <see cref="HChangeNotifyFlags.SHCNF_IDLIST"/> or
		/// <see cref="HChangeNotifyFlags.SHCNF_PATH"/> must be specified in <i>uFlags</i>.
		/// <i>dwItem1</i> contains the root of the drive that was added.
		/// <i>dwItem2</i> is not used and should be <see langword="null"/>.
		/// </summary>
		SHCNE_DRIVEADDGUI = 0x00010000,

		/// <summary>
		/// A drive has been removed. <see cref="HChangeNotifyFlags.SHCNF_IDLIST"/> or
		/// <see cref="HChangeNotifyFlags.SHCNF_PATH"/> must be specified in <i>uFlags</i>.
		/// <i>dwItem1</i> contains the root of the drive that was removed.
		/// <i>dwItem2</i> is not used and should be <see langword="null"/>.
		/// </summary>
		SHCNE_DRIVEREMOVED = 0x00000080,

		/// <summary>
		/// Not currently used.
		/// </summary>
		SHCNE_EXTENDED_EVENT = 0x04000000,

		/// <summary>
		/// The amount of free space on a drive has changed.
		/// <see cref="HChangeNotifyFlags.SHCNF_IDLIST"/> or
		/// <see cref="HChangeNotifyFlags.SHCNF_PATH"/> must be specified in <i>uFlags</i>.
		/// <i>dwItem1</i> contains the root of the drive on which the free space changed.
		/// <i>dwItem2</i> is not used and should be <see langword="null"/>.
		/// </summary>
		SHCNE_FREESPACE = 0x00040000,

		/// <summary>
		/// Storage media has been inserted into a drive.
		/// <see cref="HChangeNotifyFlags.SHCNF_IDLIST"/> or
		/// <see cref="HChangeNotifyFlags.SHCNF_PATH"/> must be specified in <i>uFlags</i>.
		/// <i>dwItem1</i> contains the root of the drive that contains the new media.
		/// <i>dwItem2</i> is not used and should be <see langword="null"/>.
		/// </summary>
		SHCNE_MEDIAINSERTED = 0x00000020,

		/// <summary>
		/// Storage media has been removed from a drive.
		/// <see cref="HChangeNotifyFlags.SHCNF_IDLIST"/> or
		/// <see cref="HChangeNotifyFlags.SHCNF_PATH"/> must be specified in <i>uFlags</i>.
		/// <i>dwItem1</i> contains the root of the drive from which the media was removed.
		/// <i>dwItem2</i> is not used and should be <see langword="null"/>.
		/// </summary>
		SHCNE_MEDIAREMOVED = 0x00000040,

		/// <summary>
		/// A folder has been created. <see cref="HChangeNotifyFlags.SHCNF_IDLIST"/>
		/// or <see cref="HChangeNotifyFlags.SHCNF_PATH"/> must be specified in <i>uFlags</i>.
		/// <i>dwItem1</i> contains the folder that was created.
		/// <i>dwItem2</i> is not used and should be <see langword="null"/>.
		/// </summary>
		SHCNE_MKDIR = 0x00000008,

		/// <summary>
		/// A folder on the local computer is being shared via the network.
		/// <see cref="HChangeNotifyFlags.SHCNF_IDLIST"/> or
		/// <see cref="HChangeNotifyFlags.SHCNF_PATH"/> must be specified in <i>uFlags</i>.
		/// <i>dwItem1</i> contains the folder that is being shared.
		/// <i>dwItem2</i> is not used and should be <see langword="null"/>.
		/// </summary>
		SHCNE_NETSHARE = 0x00000200,

		/// <summary>
		/// A folder on the local computer is no longer being shared via the network.
		/// <see cref="HChangeNotifyFlags.SHCNF_IDLIST"/> or
		/// <see cref="HChangeNotifyFlags.SHCNF_PATH"/> must be specified in <i>uFlags</i>.
		/// <i>dwItem1</i> contains the folder that is no longer being shared.
		/// <i>dwItem2</i> is not used and should be <see langword="null"/>.
		/// </summary>
		SHCNE_NETUNSHARE = 0x00000400,

		/// <summary>
		/// The name of a folder has changed.
		/// <see cref="HChangeNotifyFlags.SHCNF_IDLIST"/> or
		/// <see cref="HChangeNotifyFlags.SHCNF_PATH"/> must be specified in <i>uFlags</i>.
		/// <i>dwItem1</i> contains the previous pointer to an item identifier list (PIDL) or name of the folder.
		/// <i>dwItem2</i> contains the new PIDL or name of the folder.
		/// </summary>
		SHCNE_RENAMEFOLDER = 0x00020000,

		/// <summary>
		/// The name of a nonfolder item has changed.
		/// <see cref="HChangeNotifyFlags.SHCNF_IDLIST"/> or
		/// <see cref="HChangeNotifyFlags.SHCNF_PATH"/> must be specified in <i>uFlags</i>.
		/// <i>dwItem1</i> contains the previous PIDL or name of the item.
		/// <i>dwItem2</i> contains the new PIDL or name of the item.
		/// </summary>
		SHCNE_RENAMEITEM = 0x00000001,

		/// <summary>
		/// A folder has been removed.
		/// <see cref="HChangeNotifyFlags.SHCNF_IDLIST"/> or
		/// <see cref="HChangeNotifyFlags.SHCNF_PATH"/> must be specified in <i>uFlags</i>.
		/// <i>dwItem1</i> contains the folder that was removed.
		/// <i>dwItem2</i> is not used and should be <see langword="null"/>.
		/// </summary>
		SHCNE_RMDIR = 0x00000010,

		/// <summary>
		/// The computer has disconnected from a server.
		/// <see cref="HChangeNotifyFlags.SHCNF_IDLIST"/> or
		/// <see cref="HChangeNotifyFlags.SHCNF_PATH"/> must be specified in <i>uFlags</i>.
		/// <i>dwItem1</i> contains the server from which the computer was disconnected.
		/// <i>dwItem2</i> is not used and should be <see langword="null"/>.
		/// </summary>
		SHCNE_SERVERDISCONNECT = 0x00004000,

		/// <summary>
		/// The contents of an existing folder have changed,
		/// but the folder still exists and has not been renamed.
		/// <see cref="HChangeNotifyFlags.SHCNF_IDLIST"/> or
		/// <see cref="HChangeNotifyFlags.SHCNF_PATH"/> must be specified in <i>uFlags</i>.
		/// <i>dwItem1</i> contains the folder that has changed.
		/// <i>dwItem2</i> is not used and should be <see langword="null"/>.
		/// If a folder has been created, deleted, or renamed, use SHCNE_MKDIR, SHCNE_RMDIR, or
		/// SHCNE_RENAMEFOLDER, respectively, instead.
		/// </summary>
		SHCNE_UPDATEDIR = 0x00001000,

		/// <summary>
		/// An image in the system image list has changed.
		/// <see cref="HChangeNotifyFlags.SHCNF_DWORD"/> must be specified in <i>uFlags</i>.
		/// </summary>
		SHCNE_UPDATEIMAGE = 0x00008000,

		/// <summary>
		/// An existing item (a folder or a nonfolder) has changed, but the item still exists and has not been renamed. SHCNF_IDLIST or SHCNF_PATH must be specified in uFlags. dwItem1 contains the item that has changed. dwItem2 is not used and should be NULL. If a nonfolder item has been created, deleted, or renamed, use SHCNE_CREATE, SHCNE_DELETE, or SHCNE_RENAMEITEM, respectively, instead.
		/// <see cref="HChangeNotifyFlags.SHCNF_DWORD"/> must be specified in <i>uFlags</i>.
		/// </summary>
		SHCNE_UPDATEITEM = 0x00002000,

	}
	#endregion // enum HChangeNotifyEventID

	#region public enum HChangeNotifyFlags
	/// <summary>
	/// Flags that indicate the meaning of the <i>dwItem1</i> and <i>dwItem2</i> parameters.
	/// The uFlags parameter must be one of the following values.
	/// </summary>
	[Flags]
	public enum HChangeNotifyFlags
	{
		/// <summary>
		/// The <i>dwItem1</i> and <i>dwItem2</i> parameters are DWORD values.
		/// </summary>
		SHCNF_DWORD = 0x0003,
		/// <summary>
		/// <i>dwItem1</i> and <i>dwItem2</i> are the addresses of ITEMIDLIST structures that
		/// represent the item(s) affected by the change.
		/// Each ITEMIDLIST must be relative to the desktop folder.
		/// </summary>
		SHCNF_IDLIST = 0x0000,
		/// <summary>
		/// <i>dwItem1</i> and <i>dwItem2</i> are the addresses of null-terminated strings of
		/// maximum length MAX_PATH that contain the full path names
		/// of the items affected by the change.
		/// </summary>
		SHCNF_PATHA = 0x0001,
		/// <summary>
		/// <i>dwItem1</i> and <i>dwItem2</i> are the addresses of null-terminated strings of
		/// maximum length MAX_PATH that contain the full path names
		/// of the items affected by the change.
		/// </summary>
		SHCNF_PATHW = 0x0005,
		/// <summary>
		/// <i>dwItem1</i> and <i>dwItem2</i> are the addresses of null-terminated strings that
		/// represent the friendly names of the printer(s) affected by the change.
		/// </summary>
		SHCNF_PRINTERA = 0x0002,
		/// <summary>
		/// <i>dwItem1</i> and <i>dwItem2</i> are the addresses of null-terminated strings that
		/// represent the friendly names of the printer(s) affected by the change.
		/// </summary>
		SHCNF_PRINTERW = 0x0006,
		/// <summary>
		/// The function should not return until the notification
		/// has been delivered to all affected components.
		/// As this flag modifies other data-type flags, it cannot by used by itself.
		/// </summary>
		SHCNF_FLUSH = 0x1000,
		/// <summary>
		/// The function should begin delivering notifications to all affected components
		/// but should return as soon as the notification process has begun.
		/// As this flag modifies other data-type flags, it cannot by used by itself.
		/// </summary>
		SHCNF_FLUSHNOWAIT = 0x2000
	}
	#endregion // enum HChangeNotifyFlags

	[DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
	public static extern void SHCreateItemFromParsingName(
		[In][MarshalAs(UnmanagedType.LPWStr)] string pszPath,
		[In] IntPtr pbc,
		[In][MarshalAs(UnmanagedType.LPStruct)] Guid riid,
		[Out][MarshalAs(UnmanagedType.Interface, IidParameterIndex = 2)] out IShellItem ppv
	);

	[ComImport]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[Guid("F676C15D-596A-4ce2-8234-33996F445DB1")]
	public interface IThumbnailCache
	{
		uint GetThumbnail(
			[In] IShellItem pShellItem,
			[In] uint cxyRequestedThumbSize,
			[In] WTS_FLAGS flags /*default:  WTS_FLAGS.WTS_EXTRACT*/,
			[Out][MarshalAs(UnmanagedType.Interface)] out ISharedBitmap ppvThumb,
			[Out] out WTS_CACHEFLAGS pOutFlags,
			[Out] out WTS_THUMBNAILID pThumbnailID
		);

		void GetThumbnailByID(
			[In, MarshalAs(UnmanagedType.Struct)] WTS_THUMBNAILID thumbnailID,
			[In] uint cxyRequestedThumbSize,
			[Out][MarshalAs(UnmanagedType.Interface)] out ISharedBitmap ppvThumb,
			[Out] out WTS_CACHEFLAGS pOutFlags
		);
	}

	[ComImport]
	[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface IShellItem
	{
		void BindToHandler(IntPtr pbc,
			[MarshalAs(UnmanagedType.LPStruct)] Guid bhid,
			[MarshalAs(UnmanagedType.LPStruct)] Guid riid,
			out IntPtr ppv);

		void GetParent(out IShellItem ppsi);

		void GetDisplayName(SIGDN sigdnName, out IntPtr ppszName);

		void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

		void Compare(IShellItem psi, uint hint, out int piOrder);
	};

	public enum SIGDN : uint
	{
		NORMALDISPLAY = 0,
		PARENTRELATIVEPARSING = 0x80018001,
		PARENTRELATIVEFORADDRESSBAR = 0x8001c001,
		DESKTOPABSOLUTEPARSING = 0x80028000,
		PARENTRELATIVEEDITING = 0x80031001,
		DESKTOPABSOLUTEEDITING = 0x8004c000,
		FILESYSPATH = 0x80058000,
		URL = 0x80068000,
		/// <summary>
		/// Returns the path relative to the parent folder.
		/// </summary>
		PARENTRELATIVE = 0x80080001,
		/// <summary>
		/// Introduced in Windows 8.
		/// </summary>
		PARENTRELATIVEFORUI = 0x80094001
	}

	[Flags]
	public enum WTS_FLAGS : uint
	{
		WTS_EXTRACT = 0x00000000,
		WTS_INCACHEONLY = 0x00000001,
		WTS_FASTEXTRACT = 0x00000002,
		WTS_SLOWRECLAIM = 0x00000004,
		WTS_FORCEEXTRACTION = 0x00000008,
		WTS_EXTRACTDONOTCACHE = 0x00000020,
		WTS_SCALETOREQUESTEDSIZE = 0x00000040,
		WTS_SKIPFASTEXTRACT = 0x00000080,
		WTS_EXTRACTINPROC = 0x00000100
	}

	[Flags]
	public enum WTS_CACHEFLAGS : uint
	{
		WTS_DEFAULT = 0x00000000,
		WTS_LOWQUALITY = 0x00000001,
		WTS_CACHED = 0x00000002
	}

	[StructLayout(LayoutKind.Sequential, Size = 16), Serializable]
	public readonly struct WTS_THUMBNAILID
	{
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
		readonly byte[] rgbKey;
	}

	[ComImport()]
	[Guid("091162a4-bc96-411f-aae8-c5122cd03363")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface ISharedBitmap
	{

		uint GetSharedBitmap(
			[Out] out IntPtr phbm
		);

		uint GetSize(
			[Out, MarshalAs(UnmanagedType.Struct)] out SIZE pSize
		);

		uint GetFormat(
			[Out] out WTS_ALPHATYPE pat
		);

		uint InitializeBitmap(
			[In] IntPtr hbm,
			[In] WTS_ALPHATYPE wtsAT
		);

		uint Detach(
			[Out] out IntPtr phbm
		);

	}

	[StructLayout(LayoutKind.Sequential)]
	public struct SIZE(int cx, int cy)
	{
		public int cx = cx;
		public int cy = cy;
	}

	public enum WTS_ALPHATYPE : uint
	{
		WTSAT_UNKNOWN = 0,
		WTSAT_RGB = 1,
		WTSAT_ARGB = 2
	}

}


