


using System;
using Thumbnailer;

class Test
{
    static void Main(string[] args)
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


        var tp = new ThumbnailProvider();

        tp.SetFile(@"G:\0Music\2Special\0 Legendary\GHOST DATA\[20220207] 🍁 Blanke - The Fall [GHOST DATA Remix] 🍁.mp4");

        tp.GetThumbnail(1, out var hBitmap, out var bitmapType);
    }
}


