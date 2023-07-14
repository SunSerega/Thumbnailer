﻿


using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Windows.Forms;
using Thumbnailer;

class Test
{
	static void Main()
	{

		/**
		var psi = new ProcessStartInfo(@"ffmpeg", @"-y -i ""C:\Users\SunMachine\Desktop\0.mkv""  -f ffmetadata -");
		psi.UseShellExecute = false;
		psi.RedirectStandardOutput = true;
		psi.RedirectStandardError = true;
		psi.RedirectStandardInput = true;
		var p = Process.Start(psi)!;

		p.OutputDataReceived += (o, e) =>
		{
			Console.WriteLine($"Otp: {e.Data}");
		};
		//p.ErrorDataReceived += (o, e) =>
		//{
		//    Console.WriteLine($"Err: {e.Data}");
		//};

		p.EnableRaisingEvents = true;
		p.BeginOutputReadLine();
		p.BeginErrorReadLine();
		p.WaitForExit();

		return;
		/**/


		/**
		var tp = new ThumbnailProvider();

		tp.SetFile(@"G:\0Music\2Special\0 Legendary\GHOST DATA\[20220207] 🍁 Blanke - The Fall [GHOST DATA Remix] 🍁.mp4");
		tp.GetThumbnail(1, out _, out _);
		/**/


		ReplaceThumbnail(@"G:\0Music\3Sort\!temp\Geoxor\[20230714] Geoxor - Apathy.mkv", @"G:\0Prog\Thumbnailer\Test\0.bmp");
		//ReplaceThumbnail(@"C:\Users\SunMachine\Desktop\Thumbnailer broken files\kevin macleod merry go.mp3", new Bitmap(@"G:\0Prog\Thumbnailer\Test\0.bmp"));

	}

	public static void ReplaceThumbnail(string filePath, string newThumbnailPath)
	{
		var newThumbnail = new Bitmap(newThumbnailPath);

		SHCreateItemFromParsingName(filePath, IntPtr.Zero, typeof(IShellItem).GUID, out var item);

		var CLSID_LocalThumbnailCache = new Guid("50EF4544-AC9F-4A8E-B21B-8A26180DB13F");
		var thumbnailCache = (IThumbnailCache)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_LocalThumbnailCache, true)!)!;

		thumbnailCache.GetThumbnail(item, int.MaxValue, WTS_FLAGS.WTS_INCACHEONLY, out var sharedBitmap, out var cacheFlags, out _);

		var res1 = sharedBitmap.GetFormat(out var format);

		var res2 = sharedBitmap.GetSize(out var size);

		var res3 = sharedBitmap.GetSharedBitmap(out var hbmp);

		Bitmap.FromHbitmap(hbmp).Save(@"G:\0Prog\Thumbnailer\Test\1.bmp");

		//var bd = newThumbnail.LockBits(new Rectangle(default, newThumbnail.Size), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
		//sharedBitmap.InitializeBitmap(bd.Scan0, WTS_ALPHATYPE.WTSAT_RGB);
		var res4 = sharedBitmap.InitializeBitmap(newThumbnail.GetHbitmap(), WTS_ALPHATYPE.WTSAT_ARGB);

		var res5 = sharedBitmap.GetSharedBitmap(out var hbmp2);

		Bitmap.FromHbitmap(hbmp2).Save(@"G:\0Prog\Thumbnailer\Test\2.bmp");

		thumbnailCache.GetThumbnail(item, int.MaxValue, WTS_FLAGS.WTS_INCACHEONLY, out var sharedBitmap2, out var cacheFlags2, out _);

		var res6 = sharedBitmap2.GetSharedBitmap(out var hbmp3);

		Bitmap.FromHbitmap(hbmp3).Save(@"G:\0Prog\Thumbnailer\Test\3.bmp");

		SHChangeNotify(HChangeNotifyEventID.SHCNE_ALLEVENTS, HChangeNotifyFlags.SHCNF_PATHW, filePath, IntPtr.Zero);

	}

	[DllImport("shell32.dll")]
	static extern void SHChangeNotify(
		HChangeNotifyEventID wEventId,
		HChangeNotifyFlags uFlags,
		[MarshalAs(UnmanagedType.LPWStr)] string dwItem1,
		IntPtr dwItem2
	);

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
	public struct WTS_THUMBNAILID
	{
		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
		byte[] rgbKey;
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
	public struct SIZE
	{
		public int cx;
		public int cy;

		public SIZE(int cx, int cy)
		{
			this.cx = cx;
			this.cy = cy;
		}
	}

	public enum WTS_ALPHATYPE : uint
	{
		WTSAT_UNKNOWN = 0,
		WTSAT_RGB = 1,
		WTSAT_ARGB = 2
	}

}


