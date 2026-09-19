using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureDesk.Core;

namespace CaptureDesk.Native;

public sealed class NativeCaptureService : ICaptureService
{
    public async Task<CaptureResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Mode != CaptureMode.Screen && request.Mode != CaptureMode.Window)
            throw new NotSupportedException("此截图模式尚未实现。");
        if (request.DelayMilliseconds > 0) await Task.Delay(request.DelayMilliseconds, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var virtualRegion = GetVirtualScreenRegion();
        var full = CaptureRegion(virtualRegion, request.IncludeCursor);
        var selected = request.Region is { } region && !region.IsEmpty ? region : virtualRegion;
        var cropped = selected == virtualRegion ? full : Crop(full, selected, virtualRegion);
        return new CaptureResult(PngCodec.Encode(cropped), cropped.PixelWidth, cropped.PixelHeight, selected);
    }

    public static CaptureRegion GetVirtualScreenRegion() => new(
        GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));

    public static BitmapSource CaptureRegion(CaptureRegion region, bool includeCursor = false)
    {
        if (region.IsEmpty) throw new ArgumentException("截图区域不能为空。", nameof(region));
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, region.Width, region.Height);
        IntPtr previous = IntPtr.Zero;
        try
        {
            if (screen == IntPtr.Zero || memory == IntPtr.Zero || bitmap == IntPtr.Zero)
                throw new InvalidOperationException("无法读取屏幕图像。");
            previous = SelectObject(memory, bitmap);
            if (!BitBlt(memory, 0, 0, region.Width, region.Height, screen, region.X, region.Y, 0x40CC0020))
                throw new InvalidOperationException("屏幕捕获失败。");
            if (includeCursor)
            {
                var cursor = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
                if (GetCursorInfo(ref cursor) && (cursor.Flags & 1) != 0 && GetIconInfo(cursor.Handle, out var icon))
                {
                    try { DrawIconEx(memory, cursor.X - region.X - (int)icon.XHotspot, cursor.Y - region.Y - (int)icon.YHotspot, cursor.Handle, 0, 0, 0, IntPtr.Zero, 3); }
                    finally
                    {
                        if (icon.Mask != IntPtr.Zero) DeleteObject(icon.Mask);
                        if (icon.Color != IntPtr.Zero) DeleteObject(icon.Color);
                    }
                }
            }
            SelectObject(memory, previous);
            previous = IntPtr.Zero;
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
        }
    }

    public static BitmapSource Crop(BitmapSource source, CaptureRegion selected, CaptureRegion sourceRegion)
    {
        var x = Math.Clamp(selected.X - sourceRegion.X, 0, source.PixelWidth - 1);
        var y = Math.Clamp(selected.Y - sourceRegion.Y, 0, source.PixelHeight - 1);
        var width = Math.Clamp(selected.Width, 1, source.PixelWidth - x);
        var height = Math.Clamp(selected.Height, 1, source.PixelHeight - y);
        var result = new CroppedBitmap(source, new Int32Rect(x, y, width, height));
        result.Freeze();
        return result;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, uint rop);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { public int Size; public int Flags; public IntPtr Handle; public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { public int IsIcon; public uint XHotspot; public uint YHotspot; public IntPtr Mask; public IntPtr Color; }
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height, uint step, IntPtr brush, uint flags);
}
