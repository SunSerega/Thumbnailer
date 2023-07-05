


using Thumbnailer;

class Test
{
    static void Main(string[] args)
    {
        var tp = new ThumbnailProvider();

        tp.SetFile(@"C:\Users\SunMachine\Desktop\0.thumb_test");

        tp.GetThumbnail(1, out var hBitmap, out var bitmapType);
    }
}


