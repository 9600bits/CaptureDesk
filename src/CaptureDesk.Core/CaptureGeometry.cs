namespace CaptureDesk.Core;

public static class CaptureGeometry
{
    // View coordinates are DIPs; the desktop frame and crop use physical pixels.
    public static CaptureRegion FromDrag(double x1, double y1, double x2, double y2,
        double viewWidth, double viewHeight, CaptureRegion desktop)
    {
        if (viewWidth <= 0 || viewHeight <= 0 || desktop.IsEmpty) return default;
        // Fractional WPF dimensions (e.g. 760 / 1.5) can land a few ulps above a pixel.
        static double Pixel(double value, double view, int pixels) => Math.Round(Math.Clamp(value / view, 0, 1) * pixels, 6);
        var left = (int)Math.Floor(Pixel(Math.Min(x1, x2), viewWidth, desktop.Width));
        var top = (int)Math.Floor(Pixel(Math.Min(y1, y2), viewHeight, desktop.Height));
        var right = (int)Math.Ceiling(Pixel(Math.Max(x1, x2), viewWidth, desktop.Width));
        var bottom = (int)Math.Ceiling(Pixel(Math.Max(y1, y2), viewHeight, desktop.Height));
        return new CaptureRegion(desktop.X + left, desktop.Y + top, right - left, bottom - top);
    }
}
