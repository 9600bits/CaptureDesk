using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using CaptureDesk.Core;
using CaptureDesk.Native;

namespace CaptureDesk.App;

internal static class WorkflowUi
{
    public static Button Button(string label, Action action)
    {
        var button = new Button { Content = label, Style = (Style)System.Windows.Application.Current.FindResource("SecondaryButton"), Margin = new Thickness(4, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        return button;
    }
    public static void Try(Window owner, Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(owner, ex.Message, owner.Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    public static void PlaceControl(Window window, CaptureRegion region)
    {
        var work = WindowUtilities.GetNearestWorkArea(region);
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        var width = (int)(window.ActualWidth * dpi.DpiScaleX);
        var height = (int)(window.ActualHeight * dpi.DpiScaleY);
        var y = region.Y + region.Height + 10;
        if (y + height > work.Y + work.Height) y = region.Y - height - 10;
        WindowUtilities.PlaceInPhysicalPixels(window, new CaptureRegion(
            Math.Clamp(region.X, work.X, Math.Max(work.X, work.X + work.Width - width)),
            Math.Clamp(y, work.Y, Math.Max(work.Y, work.Y + work.Height - height)), width, height));
    }
    public static bool ExcludeFromCapture(Window window) => SetWindowDisplayAffinity(new WindowInteropHelper(window).Handle, 0x11);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
}
