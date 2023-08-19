using System;

using System.Drawing;
using System.Drawing.Imaging;

using System.IO;

using System.Runtime.InteropServices;

using System.Windows.Forms;



namespace Thumbnailer
{
	public static class Common
	{

		public static readonly System.Text.UTF8Encoding Encoding = new(true);

	}

	public enum WTS_ALPHATYPE
	{
		WTSAT_UNKNOWN = 0x0,
		WTSAT_RGB = 0x1,
		WTSAT_ARGB = 0x2,
	}

	[ComVisible(true)]
	[Guid("e357fccd-a995-4576-b01f-234630154e96")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface IThumbnailProvider
	{
		void GetThumbnail(int cx, out IntPtr hBitmap, out WTS_ALPHATYPE bitmapType);
	}

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

	[ComVisible(true)]
	[Guid("7f73be3f-fb79-493c-a6c7-7ee14e245841")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface IInitializeWithItem
	{
		void Initialize(IShellItem psi, int grfMode);
	}

	[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
	[ProgId("Thumbnailer.ThumbnailProvider")]
	[Guid("E7CBDB01-06C9-4C8F-A061-2EDCE8598F99")]
	public class ThumbnailProvider : IThumbnailProvider, IInitializeWithItem
	{
		private void HandleError(Exception e)
		{
			try
			{
				var lns = new System.Collections.Generic.List<string>
				{
					$"Error making thumb for: {curr_file_name??"<null>"}"
				};
				lns.AddRange(e.ToString().Replace("\r", "").Split('\n'));
				lns.Add("");
				File.AppendAllLines(@"C:\Users\SunMachine\Desktop\Thumbnailer.log", lns, Common.Encoding);
			}
			catch (Exception e2)
			{
				MessageBox.Show(e2.ToString(), $"Error reporting error for: {curr_file_name??"<null>"}");
			}
		}

		private string? curr_file_name = null;
		public void SetFile(string fname)
		{
			curr_file_name = fname;
		}

		public void Initialize(IShellItem psi, int grfMode)
		{
			try
			{
				psi.GetDisplayName(SIGDN.FILESYSPATH, out var displayNamePtr);
				var displayName = Marshal.PtrToStringUni(displayNamePtr);
				Marshal.FreeCoTaskMem(displayNamePtr);
				if (displayName == null)
					throw new InvalidOperationException(nameof(displayName));
				SetFile(displayName);
			}
			catch (Exception e)
			{
				HandleError(e);
			}
		}

		private static Bitmap LoadBitmap(string filename)
		{
			using var str = File.OpenRead(filename);
			return new Bitmap(str);
		}

		public void GetThumbnail(int cx, out IntPtr hBitmap, out WTS_ALPHATYPE bitmapType)
		{
			//var sw = System.Diagnostics.Stopwatch.StartNew();
			//File.AppendAllLines(@"C:\Users\SunMachine\Desktop\Thumbnailer.info.log", new[] { $"{DateTime.Now} | Starter: {curr_file_name}" }, Common.Encoding);

			hBitmap = IntPtr.Zero;
			bitmapType = WTS_ALPHATYPE.WTSAT_UNKNOWN;
			try
			{

				Bitmap loaded_bmp;
				{
					var load_exc_lst = new System.Collections.Generic.List<Exception>();
					while (true)
						try
						{
							using var client = new System.IO.Pipes.NamedPipeClientStream("Dashboard for Thumbnailer");
							client.Connect();
							var bw = new BinaryWriter(client);
							var br = new BinaryReader(client);
							bw.Write(2); // GimmiThumb
							bw.Write(curr_file_name!);
							bw.Flush();
							var res_fname = br.ReadString();
							loaded_bmp = LoadBitmap(res_fname);
							break;
						}
						catch (Exception e)
						{
							if (!File.Exists(curr_file_name))
								return;
							load_exc_lst.Add(e);
							if (load_exc_lst.Count<100) continue;
							throw new AggregateException(load_exc_lst.ToArray());
						}
				}

				var format = PixelFormat.Format32bppArgb;
				var res_bmp = loaded_bmp;
				if (loaded_bmp.PixelFormat != format)
				{
					res_bmp = new Bitmap(res_bmp.Width, res_bmp.Height, format);
					using var gr = Graphics.FromImage(res_bmp);
					gr.DrawImageUnscaled(loaded_bmp, Point.Empty);
				}

				bitmapType = WTS_ALPHATYPE.WTSAT_ARGB;
				hBitmap = res_bmp.GetHbitmap();

				//outBitmap.Save(Path.ChangeExtension(curr_file_name, ".bmp"));

				//MessageBox.Show($"cx={cx}");
				//MessageBox.Show($"regenerated in {sw.Elapsed}: curr_file_name={curr_file_name}\nFrom dur={dur} chose {frame_at}"+sb.ToString());
			}
			catch (Exception e)
			{
				HandleError(e);
			}
			finally
			{
				//File.AppendAllLines(@"C:\Users\SunMachine\Desktop\Thumbnailer.info.log", new[] { $"{DateTime.Now} | Finished: {curr_file_name} ({sw.Elapsed})" }, Common.Encoding);
			}
		}

	}
}


