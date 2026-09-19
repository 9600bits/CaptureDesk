using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace CaptureDesk.Native;

public static class PngCodec
{
    public static byte[] Encode(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    public static BitmapSource Decode(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var source = decoder.Frames[0];
        source.Freeze();
        return source;
    }

    public static void Save(BitmapSource source, string path, int quality = 95)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        BitmapEncoder encoder = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) },
            _ => new PngBitmapEncoder()
        };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public static void CopyToClipboard(BitmapSource source) => Clipboard.SetImage(source);
}
