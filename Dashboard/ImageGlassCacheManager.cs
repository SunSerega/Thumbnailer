using System;

using System.IO;
using System.Text;

using SunSharpUtils.Threading;

namespace Dashboard;

public static class ImageGlassCacheManager
{

    private static readonly DelayedMultiUpdater<String> ig_resetter = new(fname =>
    {
        var cache_dir = @"C:\Users\SunSerega\AppData\Local\ImageGlass\ThumbnailsCache";
        var fi = new FileInfo(fname);
        if (!fi.Exists) return;

        var sb = new StringBuilder();
        sb.Append(fname);
        sb.Append(':');
        sb.Append(fi.LastWriteTimeUtc.ToBinary());
        sb.Append(':');
        sb.Append(50); // Thumbnail size
        sb.Append(',');
        sb.Append(50);
        sb.Append(':');
        sb.Append("Auto");
        sb.Append(':');
        sb.Append(true);

        var cache_key = sb.ToString();
        var hash = System.Security.Cryptography.MD5.HashData(Encoding.ASCII.GetBytes(cache_key));
        cache_key = Convert.ToHexString(hash).ToLowerInvariant();

        var cache_file_path = Path.Combine(cache_dir, cache_key);
        if (File.Exists(cache_file_path))
            File.Delete(cache_file_path);

    }, "", is_background: false);
    public static void ResetFor(String fname) => ig_resetter.TriggerNow(fname);

}
