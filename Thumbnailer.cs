


using FFmpeg.NET;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Thumbnailer
{
    // COM interfaces
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

    [ComVisible(true)]
    [Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithStream
    {
        void Initialize(IStream stream, int grfMode);
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

    // my thumbnail provider class
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    [ProgId("Thumbnailer.ThumbnailProvider")]
    [Guid("E7CBDB01-06C9-4C8F-A061-2EDCE8598F99")]
    public class ThumbnailProvider : IThumbnailProvider
        //, IInitializeWithStream
        , IInitializeWithItem
    {
        private void HandleError(Exception e)
        {
            try
            {
                var lns = new List<string>();
                lns.Add($"Error making thumb for: {curr_file_name??"<null>"}");
                lns.AddRange(e.ToString().Replace("\r", "").Split('\n'));
                File.AppendAllLines(@"C:\Users\SunMachine\Desktop\Thumbnailer.log", lns, new UTF8Encoding(true));
                if (curr_file_name!=null)
                {
                    Directory.CreateDirectory(@"C:\Users\SunMachine\Desktop\Thumbnailer broken files");
                    File.Copy(curr_file_name, @"C:\Users\SunMachine\Desktop\Thumbnailer broken files\"+Path.GetFileName(curr_file_name), true);
                }
                //MessageBox.Show(e.ToString(), $"Error making thumb for: {curr_file_name??"<null>"}");
            }
            catch (Exception e2)
            {
                MessageBox.Show(e2.ToString(), $"Error reporting errorfor: {curr_file_name??"<null>"}");
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
                    throw new ArgumentNullException(nameof(displayName));
                SetFile(displayName);
            } catch (Exception e)
            {
                HandleError(e);
            }
        }

        private static void DrawString(Graphics gr, string str, Color c, Color glow_c, Func<SizeF, float> make_scale, Func<SizeF, PointF> make_pos, string ff_name = "Arial")
        {
            var ff = new FontFamily(ff_name);
            var str_sz = gr.MeasureString(str, new Font(ff, 1f));
            var font_scale = make_scale(str_sz);
            var sz = str_sz * font_scale;
            var pos = make_pos(sz);

            var gpath = new GraphicsPath();
            gpath.AddString(str, ff, (int)FontStyle.Regular, gr.DpiY/72 * font_scale, pos, new StringFormat());

            gr.DrawPath(new Pen(glow_c, font_scale*0.2f), gpath);
            gr.FillPath(new SolidBrush(c), gpath);

            //var font_extra_scale = 1.1f;
            //gr.DrawString(str, new Font(family, font_scale * font_extra_scale), new SolidBrush(glow_c), pos - sz * (font_extra_scale-1) / 2);
            //gr.DrawString(str, new Font(family, font_scale), new SolidBrush(c), pos);
        }

        public void GetThumbnail(int cx, out IntPtr hBitmap, out WTS_ALPHATYPE bitmapType)
        {
            hBitmap = IntPtr.Zero;
            bitmapType = WTS_ALPHATYPE.WTSAT_UNKNOWN;
            try
            {
                if (curr_file_name == null)
                    throw new ArgumentNullException(nameof(curr_file_name));
                if (curr_file_name.StartsWith(@"C:\Users\SunMachine\Desktop\Thumbnailer broken files\"))
                    return;

                var sw = Stopwatch.StartNew();

                var ffmpeg = new FFmpeg.NET.Engine(@"C:\Program Files\ffmpeg\bin\ffmpeg.exe");
                ffmpeg.Error += (s,e)=>
                {
                    HandleError(e.Exception);
                };

                var sb = new StringBuilder("\n===");
                ffmpeg.Data += (s, e) =>
                {
                    sb.Append('\n');
                    sb.Append(e.Data);
                };

                TimeSpan dur = default, frame_at = default;

                var temp_dir = Path.GetTempFileName();
                File.Delete(temp_dir);
                Directory.CreateDirectory(temp_dir);
                var frame_fname = temp_dir + @"\frame.png";

                ffmpeg.GetMetaDataAsync(new InputFile(curr_file_name), default).ContinueWith(t =>
                {
                    dur = t.Result.Duration;
                    frame_at = dur * 0.3;
                    ffmpeg.ExecuteAsync($"-i \"{curr_file_name}\" -ss {Math.Truncate(frame_at.TotalSeconds)} -vframes 1 \"{frame_fname}\"", default).Wait();
                }).Wait();

                for (int try_i = 0; try_i < 100; try_i++)
                {
                    if (File.Exists(frame_fname))
                        break;
                    Thread.Sleep(10);
                }
                if (!File.Exists(frame_fname))
                    throw new Exception($"Frame file was not created at {frame_at.TotalSeconds} / {dur}");

                bitmapType = WTS_ALPHATYPE.WTSAT_RGB;
                var frame_stream = File.OpenRead(frame_fname);
                var frame_im = Image.FromStream(frame_stream);
                frame_stream.Close();
                Directory.Delete(temp_dir, true);
                var outBitmap = new Bitmap(frame_im.Width, frame_im.Height, PixelFormat.Format24bppRgb);

                Graphics gr = Graphics.FromImage(outBitmap);
                gr.FillRectangle(new SolidBrush(Color.White), new Rectangle(default, outBitmap.Size));
                gr.DrawImageUnscaled(frame_im, point: default);

                var dur_sb = new StringBuilder();
                dur_sb.Append(dur.Seconds.ToString("00"));
                if (dur.TotalSeconds>=60)
                {
                    dur_sb.Insert(0, ':');
                    dur_sb.Insert(0, dur.Minutes.ToString("00"));
                    if (dur.TotalMinutes>=60)
                    {
                        dur_sb.Insert(0, ':');
                        dur_sb.Insert(0, Math.Truncate(dur.TotalHours));
                    }
                }
                
                var text_bmp = new Bitmap(outBitmap.Width, outBitmap.Height, PixelFormat.Format32bppArgb);
                //DrawString(Graphics.FromImage(text_bmp), dur_sb.ToString(), Color.FromArgb(255, 0, 0, 0), Color.White,
                //    sz => MathF.Min(outBitmap.Width/sz.Width, outBitmap.Height*0.3f/sz.Height),
                //    sz => new PointF((outBitmap.Width-sz.Width)/2f, outBitmap.Height-sz.Height)
                //);
                DrawString(Graphics.FromImage(text_bmp), dur_sb.ToString(), Color.FromArgb(255, 0, 0, 0), Color.White,
                    sz => MathF.Min(outBitmap.Width/sz.Width, outBitmap.Height*0.3f/sz.Height),
                    sz => new PointF(outBitmap.Width-sz.Width, (outBitmap.Height-sz.Height)/2f)
                );
                var text_attr = new ImageAttributes();
                var text_c_mtr = new ColorMatrix();
                text_c_mtr.Matrix33 = 0.5f;
                text_attr.SetColorMatrix(text_c_mtr, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                gr.DrawImage(text_bmp, new Rectangle(default, text_bmp.Size), 0,0, text_bmp.Width,text_bmp.Height, GraphicsUnit.Pixel, text_attr);

                //DrawString(gr, sw.Elapsed.ToString(), Color.FromArgb(255, 0, 0, 0), Color.White,
                //    sz => MathF.Min(outBitmap.Width/sz.Width, outBitmap.Height*0.5f/sz.Height),
                //    sz => new PointF((outBitmap.Width-sz.Width)/2f, 0)
                //);

                hBitmap = outBitmap.GetHbitmap();

                //outBitmap.Save(Path.ChangeExtension(curr_file_name, ".bmp"));

                //MessageBox.Show($"cx={cx}");
                //MessageBox.Show($"regenerated in {sw.Elapsed}: curr_file_name={curr_file_name}\nFrom dur={dur} chose {frame_at}"+sb.ToString());
            }
            catch (Exception e)
            {
                HandleError(e);
            }
        }

    }
}


