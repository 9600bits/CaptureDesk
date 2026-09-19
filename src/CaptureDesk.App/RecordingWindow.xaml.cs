using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CaptureDesk.Core;
using CaptureDesk.Media;

namespace CaptureDesk.App;

public partial class RecordingWindow : Window
{
    private readonly CaptureRegion _region;
    private LocalRecordingService? _service;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private CancellationTokenSource? _export;
    private bool _allowClose, _stopping, _exporting;
    public RecordingWindow() : this(default(CaptureRegion)) { }
    public RecordingWindow(string journal) : this(default(CaptureRegion))
    {
        _service = LocalRecordingService.Recover(journal);
        StartButton.IsEnabled = Options.IsEnabled = false;
        SaveButton.IsEnabled = true; Topmost = false;
        ClockText.Text = TimeSpan.FromSeconds(_service.Duration).ToString(@"mm\:ss\.f");
        StatusText.Text = "录制已恢复，可以重新导出";
        ShowTrim();
    }
    public RecordingWindow(CaptureRegion region)
    {
        _region = region;
        InitializeComponent();
        CursorToggle.IsChecked = App.Settings.ShowCursor;
        Loaded += (_, _) =>
        {
            if (!region.IsEmpty) WorkflowUi.PlaceControl(this, region);
            if (!WorkflowUi.ExcludeFromCapture(this)) { StartButton.IsEnabled = false; StatusText.Text = "无法将控制条排除出录制画面，请重启应用后重试。"; }
        };
        _timer.Tick += async (_, _) =>
        {
            if (_service is null) return;
            ClockText.Text = _service.Elapsed.ToString(@"mm\:ss\.f");
            if (!_service.IsRecording && !_stopping) await FinishAsync();
        };
        Closing += OnClosing;
        Closed += (_, _) => { _timer.Stop(); _service?.Dispose(); _export?.Dispose(); };
    }
    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = Options.IsEnabled = false;
        try
        {
            _service = new LocalRecordingService { FrameRate = int.Parse(((ComboBoxItem)FrameRate.SelectedItem).Tag.ToString()!), IncludeCursor = CursorToggle.IsChecked == true };
            await _service.StartAsync(_region);
            PauseButton.IsEnabled = StopButton.IsEnabled = true;
            StatusText.Text = "正在录制 · GIF · 无音频"; _timer.Start();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; StartButton.IsEnabled = Options.IsEnabled = true; }
    }
    private async void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null) return;
        await _service.PauseAsync();
        PauseButton.Content = _service.IsPaused ? "继续" : "暂停";
        StatusText.Text = _service.IsPaused ? "已暂停" : "正在录制 · GIF · 无音频";
    }
    private async void Stop_Click(object sender, RoutedEventArgs e) => await FinishAsync();
    private async Task FinishAsync()
    {
        if (_service is null || _stopping) return;
        _stopping = true; _timer.Stop(); PauseButton.IsEnabled = StopButton.IsEnabled = false;
        await _service.FinishAsync();
        SaveButton.IsEnabled = true; Topmost = false;
        StatusText.Text = _service.Failure ?? "录制完成，可以保存 GIF";
        ShowTrim();
        _stopping = false;
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_exporting) { _export?.Cancel(); return; }
        if (!double.TryParse(TrimStart.Text, out var start) || !double.TryParse(TrimEnd.Text, out var end) || start < 0 || end <= start || end > (_service?.Duration ?? 0) + .01)
        { StatusText.Text = "请输入有效的开始和结束时间（秒）"; return; }
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "GIF 动图|*.gif", FileName = $"录屏-{DateTime.Now:yyyyMMdd-HHmmss}.gif" };
        if (dialog.ShowDialog(this) != true || _service is null) return;
        _export = new(); _exporting = true; SaveButton.Content = "取消导出";
        ExportProgress.Value = 0; ExportProgress.Visibility = Visibility.Visible;
        StatusText.Text = "正在导出…";
        try
        {
            await _service.ExportAsync(dialog.FileName, new Progress<double>(p => { if (_exporting) { ExportProgress.Value = p * 100; StatusText.Text = $"正在导出 {p:P0}"; } }), _export.Token, start, end);
            StatusText.Text = "已保存 GIF";
        }
        catch (OperationCanceledException) { StatusText.Text = "已取消导出，录制帧保留，可重新保存"; }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { _exporting = false; SaveButton.Content = "保存 GIF"; ExportProgress.Visibility = Visibility.Collapsed; _export.Dispose(); _export = null; }
    }
    private void ShowTrim()
    {
        if (TrimOptions.Visibility == Visibility.Visible) return;
        Height = 265; TrimOptions.Visibility = Visibility.Visible;
        TrimEnd.Text = (_service?.Duration ?? 0).ToString("F2");
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        if (_exporting) { e.Cancel = true; _export?.Cancel(); StatusText.Text = "正在取消导出，请稍后关闭"; return; }
        if (_service is null) return;
        e.Cancel = true;
        if (_stopping) return;
        if (MessageBox.Show(this, "关闭录制？帧文件会保留，可从“恢复录制”继续导出。", "GIF 录制", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        await FinishAsync(); _allowClose = true; Close();
    }
}
