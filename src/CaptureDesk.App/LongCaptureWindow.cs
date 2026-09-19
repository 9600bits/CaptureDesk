using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CaptureDesk.Core;
using CaptureDesk.Media;
using CaptureDesk.Native;

namespace CaptureDesk.App;

public sealed class LongCaptureWindow : Window
{
    private readonly CaptureRegion _region;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly TextBlock _status = new() { Text = "缓慢滚动内容，每次保留至少一半重叠", Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap };
    private readonly Button _pause;
    private readonly Button _finish;
    private readonly Button _copy;
    private readonly Button _save;
    private readonly Image _preview = new() { Stretch = System.Windows.Media.Stretch.Uniform };
    private ScrollDocument? _document;
    private bool _busy, _closed, _finished, _excluded;
    public LongCaptureWindow(CaptureRegion region)
    {
        _region = region;
        Title = "长截图"; Width = 520; Height = 160; MinWidth = 460; MinHeight = 160; Topmost = true;
        Style = (Style)FindResource("AppWindowStyle"); SetResourceReference(BackgroundProperty, "CanvasBrush");
        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(_status, Dock.Top); root.Children.Add(_status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _pause = WorkflowUi.Button("暂停", TogglePause);
        _finish = WorkflowUi.Button("完成", Finish);
        _copy = WorkflowUi.Button("复制", () => WorkflowUi.Try(this, () => { Clipboard.SetImage(_document!.Render()); _status.Text = "已复制"; }));
        _save = WorkflowUi.Button("保存", Save);
        _copy.IsEnabled = _save.IsEnabled = false;
        foreach (var button in new[] { _pause, _finish, _copy, _save }) actions.Children.Add(button);
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        root.Children.Add(new ScrollViewer { Content = _preview, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        Loaded += async (_, _) => { WorkflowUi.PlaceControl(this, region); _excluded = WorkflowUi.ExcludeFromCapture(this); await SampleAsync(); if (!_closed && !_finished && _document is not null) _timer.Start(); };
        _timer.Tick += async (_, _) => await SampleAsync();
        Closed += (_, _) => { _closed = true; _timer.Stop(); };
        Closing += (_, e) =>
        {
            if (!_finished && _document is not null && MessageBox.Show(this, "关闭将放弃当前长截图。继续关闭？", "长截图", MessageBoxButton.YesNo) != MessageBoxResult.Yes) e.Cancel = true;
        };
    }
    private void TogglePause()
    {
        if (_timer.IsEnabled) { _timer.Stop(); _pause.Content = "继续"; }
        else { _timer.Start(); _pause.Content = "暂停"; }
    }
    private async Task SampleAsync()
    {
        if (_busy || _closed || _finished) return;
        _busy = true;
        _finish.IsEnabled = _pause.IsEnabled = false;
        try
        {
            // Hide just the control surface; capture remains the original physical region.
            if (!_excluded) { Opacity = 0; await Task.Delay(90); }
            if (_closed) return;
            var frame = NativeCaptureService.CaptureRegion(_region);
            Opacity = 1;
            if (_document is null) _document = new ScrollDocument(frame);
            else
            {
                var match = await Task.Run(() => _document.Append(frame));
                if (_closed) return;
                if (!match.Matched) { _status.Text = "未找到重叠，请回滚一些再缓慢滚动。已有内容保留。"; return; }
            }
            _status.Text = $"{_document.Width} × {_document.Height} px · 缓慢滚动，完成后点击完成";
        }
        catch (Exception ex) { _timer.Stop(); _status.Text = ex.Message; _pause.Content = "继续"; }
        finally { _busy = false; if (!_closed) { Opacity = 1; _finish.IsEnabled = _pause.IsEnabled = true; } }
    }
    private void Finish()
    {
        if (_busy || _document is null) return;
        _finished = true; _timer.Stop(); _pause.IsEnabled = _finish.IsEnabled = false;
        _copy.IsEnabled = _save.IsEnabled = true; Topmost = false; Height = 600;
        var source = _document.Render(); _preview.Source = source;
        App.History.Add(new CaptureResult(PngCodec.Encode(source), source.PixelWidth, source.PixelHeight, _region));
        _status.Text = $"{source.PixelWidth} × {source.PixelHeight} px";
        WorkflowUi.PlaceControl(this, _region); Topmost = false;
    }
    private void Save() => WorkflowUi.Try(this, () =>
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG 图像|*.png", FileName = $"长截图-{DateTime.Now:yyyyMMdd-HHmmss}.png" };
        if (dialog.ShowDialog(this) == true) { PngCodec.Save(_document!.Render(), dialog.FileName); _status.Text = "已保存"; }
    });
}
