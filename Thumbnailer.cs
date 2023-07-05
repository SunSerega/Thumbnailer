


using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

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

    // my thumbnail provider class
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    [ProgId("ThumbUrlHandler.ThumbnailProvider")]
    [Guid("E7CBDB01-06C9-4C8F-A061-2EDCE8598F99")]
    public class ThumbnailProvider : IThumbnailProvider, IInitializeWithStream
    {
        public void Initialize(IStream stream, int grfMode)
        {
            // instantly dispose the COM stream, as I don't need it for the test
            //Marshal.ReleaseComObject(stream);
        }

        private static void HandleError(Exception e)
        {
            MessageBox.Show(e.ToString(), "Error making ");
        }

        private static void DrawString(Graphics gr, string str, Color c, Func<SizeF, float> make_scale, Func<SizeF, PointF> make_pos, string family = "Arial")
        {
            var font = new Font(family, 1f);
            var str_sz = gr.MeasureString(str, font);
            font = new Font(family, make_scale(str_sz));
            gr.DrawString(str, font, new SolidBrush(c), make_pos(str_sz * font.Size));
        }

        public void GetThumbnail(int cx, out IntPtr hBitmap, out WTS_ALPHATYPE bitmapType)
        {
            try
            {
                var sw = Stopwatch.StartNew();

                var ffmpeg = new FFmpeg.NET.Engine(@"C:\Program Files\ffmpeg\bin\ffmpeg.exe");

                bitmapType = WTS_ALPHATYPE.WTSAT_RGB;
                var outBitmap = new Bitmap(cx, cx, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

                Graphics gr = Graphics.FromImage(outBitmap);
                gr.FillRectangle(new SolidBrush(Color.Black), new Rectangle(Point.Empty, outBitmap.Size));

                DrawString(gr, $"cx={cx}", Color.Red, sz => cx/MathF.Max(sz.Width, sz.Height), sz => new PointF(cx, cx) - sz);

                hBitmap = outBitmap.GetHbitmap();

                outBitmap.Save(@"C:\Users\SunMachine\Desktop\temp.bmp");

                MessageBox.Show($"regenerated in {sw.Elapsed}");
            }
            catch (Exception e)
            {
                hBitmap = IntPtr.Zero;
                bitmapType = WTS_ALPHATYPE.WTSAT_UNKNOWN;
                HandleError(e);
            }
        }
    }
}


