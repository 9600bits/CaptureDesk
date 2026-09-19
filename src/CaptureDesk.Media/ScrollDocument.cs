using CaptureDesk.Core;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CaptureDesk.Media;

public sealed class ScrollDocument
{
    private byte[] _last;
    private byte[] _pixels;
    private int _position;
    public int Width { get; }
    public int Height => _pixels.Length / (Width * 4);
    private readonly int _frameHeight;
    public ScrollDocument(BitmapSource first)
    {
        Width = first.PixelWidth;
        _frameHeight = first.PixelHeight;
        _last = Read(first);
        _pixels = (byte[])_last.Clone();
    }
    private static byte[] Read(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return pixels;
    }
    public ScrollMatch Append(BitmapSource next)
    {
        if (next.PixelWidth != Width || next.PixelHeight != _frameHeight) throw new ArgumentException("区域大小已改变。");
        var bytes = Read(next);
        var match = ScrollMatcher.Match(_last, bytes, Width, _frameHeight);
        if (!match.Matched || match.Offset == 0) return match;
        var position = _position + match.Offset;
        var prepend = Math.Max(0, -position);
        var height = Math.Max(Height + prepend, position + prepend + _frameHeight);
        if ((long)height * Width > 64_000_000) throw new InvalidOperationException("长图已达到 6400 万像素，请先保存再开始下一张。");
        var combined = new byte[height * Width * 4];
        Buffer.BlockCopy(_pixels, 0, combined, prepend * Width * 4, _pixels.Length);
        Buffer.BlockCopy(bytes, 0, combined, (position + prepend) * Width * 4, bytes.Length);
        _pixels = combined;
        _position = position + prepend;
        _last = bytes;
        return match;
    }
    public BitmapSource Render()
    {
        var source = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, _pixels, Width * 4);
        source.Freeze();
        return source;
    }
}
