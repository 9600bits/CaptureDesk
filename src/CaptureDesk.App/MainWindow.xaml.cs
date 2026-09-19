using System.ComponentModel;
using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using CaptureDesk.Core;
using CaptureDesk.Native;

namespace CaptureDesk.App;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0xCDA;
    private bool _capturing;
    private readonly bool _interactive;
    private SettingsWindow? _settingsWindow;
    public MainWindow(bool interactive = true)
    {
        _interactive = interactive;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId);
    }
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (!_interactive) return;
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
        if (!RegisterHotKey(handle, HotkeyId, 0x0002 | 0x0004 | 0x4000, 0x41))
            ShortcutStatus.Text = "Ctrl + Shift + A 已被其他程序占用";
    }
    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam.ToInt32() == HotkeyId) { handled = true; _ = StartCaptureAsync(); }
        return IntPtr.Zero;
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_interactive && !App.IsExiting && App.Settings.CloseBehavior == "最小化到托盘") { e.Cancel = true; Hide(); }
        else if (_interactive && !App.IsExiting) { e.Cancel = true; App.ExitApplication(); }
        base.OnClosing(e);
    }

    internal async Task StartCaptureAsync(string workflow = "capture")
    {
        if (_capturing) return;
        _capturing = true;
        var wasVisible = IsVisible;
        try
        {
            Hide();
            await Task.Delay(160);
            var region = NativeCaptureService.GetVirtualScreenRegion();
            var frame = NativeCaptureService.CaptureRegion(region, App.Settings.ShowCursor);
            var overlay = new CaptureOverlay(frame, region, workflow != "capture");
            if (overlay.ShowDialog() != true || overlay.CapturedSource is null || overlay.CapturedBytes is null) return;
            if (workflow == "capture") workflow = overlay.OutputAction;
            if (workflow == "ocr") { new RecognitionWindow(overlay.CapturedBytes).ShowDialog(); return; }
            if (workflow == "long") { new LongCaptureWindow(overlay.CapturedRegion).ShowDialog(); return; }
            if (workflow == "record") { new RecordingWindow(overlay.CapturedRegion).ShowDialog(); return; }
            if (workflow == "table") { new StructuredRecognitionWindow(overlay.CapturedBytes).ShowDialog(); return; }
            App.History.Add(new CaptureResult(overlay.CapturedBytes, overlay.CapturedSource.PixelWidth, overlay.CapturedSource.PixelHeight, overlay.CapturedRegion));
            if (overlay.OutputAction == "copy") { Clipboard.SetImage(overlay.CapturedSource); return; }
            if (overlay.OutputAction == "pin") { new PinWindow(overlay.CapturedSource).Show(); return; }
            // Save completes inside the overlay; no separate editor is opened.
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "截图失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally
        {
            _capturing = false;
            if (wasVisible) Show();
        }
    }
    public void StartCaptureFromTray() => _ = StartCaptureAsync();
    private void Capture_Click(object sender, RoutedEventArgs e) => _ = StartCaptureAsync();
    private void RecoverRecording_Click(object sender, RoutedEventArgs e) => WorkflowUi.Try(this, () =>
    {
        new RecordingRecoveryWindow { Owner = this }.ShowDialog();
    });
    public void OpenSettings()
    {
        if (_settingsWindow is not null) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow();
        try { _settingsWindow.ShowDialog(); }
        finally { _settingsWindow = null; }
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();
    public void OpenHistory() => new HistoryWindow().Show();
    private void History_Click(object sender, RoutedEventArgs e) => OpenHistory();
    private void Clipboard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var source = Clipboard.GetImage();
            if (source is null) { MessageBox.Show(this, "剪贴板中没有图像。", "贴图", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            source.Freeze();
            new PinWindow(source).Show();
        }
        catch (ExternalException) { MessageBox.Show(this, "剪贴板正被其他程序使用，请稍后重试。", "贴图", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
