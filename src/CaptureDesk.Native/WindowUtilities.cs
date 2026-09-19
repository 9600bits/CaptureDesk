using System.Runtime.InteropServices;
using System.Windows;

namespace CaptureDesk.Native;

public static class WindowUtilities
{
    public static CaptureDesk.Core.CaptureRegion GetNearestWorkArea(CaptureDesk.Core.CaptureRegion selection)
    {
        var rect = new NativeRect { Left = selection.X, Top = selection.Y, Right = selection.X + selection.Width, Bottom = selection.Y + selection.Height };
        var monitor = MonitorFromRect(ref rect, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return NativeCaptureService.GetVirtualScreenRegion();
        return new(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
    }

    public static void PlaceInPhysicalPixels(Window window, CaptureDesk.Core.CaptureRegion region)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        SetWindowPos(handle, new IntPtr(-1), region.X, region.Y, region.Width, region.Height, 0x0040);
    }

    public static void SetNoActivate(Window window)
    {
        var helper = new System.Windows.Interop.WindowInteropHelper(window);
        var style = GetWindowLong(helper.Handle, -20);
        SetWindowLong(helper.Handle, -20, style | 0x08000000);
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor; public NativeRect Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
