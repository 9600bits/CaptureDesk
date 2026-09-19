using System.IO;
using System.Windows;
using System.Windows.Controls;
using CaptureDesk.Media;

namespace CaptureDesk.App;

public sealed class RecordingRecoveryWindow : Window
{
    private sealed record Session(string Journal, string Label);
    private readonly ListBox _list = new() { DisplayMemberPath = "Label", BorderThickness = new Thickness(0), Padding = new Thickness(8) };
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 0, 0, 12) };
    public RecordingRecoveryWindow()
    {
        Title = "恢复录制"; Width = 560; Height = 410; MinWidth = 460; MinHeight = 300;
        Style = (Style)FindResource("AppWindowStyle"); SetResourceReference(BackgroundProperty, "CanvasBrush");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(_status, Dock.Top); root.Children.Add(_status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(WorkflowUi.Button("移到回收站", Trash));
        actions.Children.Add(WorkflowUi.Button("恢复", () => WorkflowUi.Try(this, () =>
        {
            if (_list.SelectedItem is Session session) new RecordingWindow(session.Journal) { Owner = this }.ShowDialog();
        })));
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        _list.SetResourceReference(BackgroundProperty, "PanelBrush"); _list.SetResourceReference(ForegroundProperty, "TextBrush");
        System.Windows.Automation.AutomationProperties.SetName(_list, "可恢复的录制");
        root.Children.Add(_list); Content = root; Refresh();
    }
    private void Refresh()
    {
        _list.Items.Clear();
        if (Directory.Exists(LocalRecordingService.RecoveryRoot))
            foreach (var folder in new DirectoryInfo(LocalRecordingService.RecoveryRoot).EnumerateDirectories().OrderByDescending(x => x.CreationTime))
            {
                var journal = Path.Combine(folder.FullName, "frames.tsv");
                if (!File.Exists(journal)) continue;
                var size = folder.EnumerateFiles().Sum(x => x.Length) / 1048576d;
                _list.Items.Add(new Session(journal, $"{folder.CreationTime:yyyy-MM-dd  HH:mm:ss}     {size:F1} MB"));
            }
        _status.Text = _list.Items.Count == 0 ? "没有可恢复的录制" : $"{_list.Items.Count} 段录制";
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
    }
    private void Trash() => WorkflowUi.Try(this, () =>
    {
        if (_list.SelectedItem is not Session session) return;
        if (MessageBox.Show(this, "将这段录制的缓存移到回收站？已导出的 GIF 不受影响。", Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var folder = Path.GetDirectoryName(Path.GetFullPath(session.Journal))!;
        if (!string.Equals(Path.GetDirectoryName(folder), Path.GetFullPath(LocalRecordingService.RecoveryRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("录制缓存路径无效。");
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(folder, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        Refresh(); _status.Text = "已移到回收站，可以还原";
    });
}
