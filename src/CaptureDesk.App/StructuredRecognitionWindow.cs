using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CaptureDesk.Core;
using CaptureDesk.Media;
using CaptureDesk.Native;

namespace CaptureDesk.App;

public sealed class StructuredRecognitionWindow : Window
{
    private readonly byte[] _png;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly DataGrid _grid = new() { AutoGenerateColumns = true, CanUserAddRows = true, CanUserDeleteRows = true, MinColumnWidth = 80, HeadersVisibility = DataGridHeadersVisibility.All, ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader };
    private readonly ComboBox _language = new() { Width = 165, Foreground = Brushes.Black, Background = Brushes.White };
    private readonly Button _retry, _copy, _save, _cancel;
    private DataTable? _table;
    private CancellationTokenSource? _request;
    private bool _closed;
    internal bool IsBusy => _request is not null;
    internal DataTable? ResultTable => _table;
    public StructuredRecognitionWindow(byte[] png)
    {
        _png = png;
        Title = "表格识别";
        Width = 1020; Height = 640; MinWidth = 720; MinHeight = 420;
        Style = (Style)FindResource("AppWindowStyle"); SetResourceReference(BackgroundProperty, "CanvasBrush");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        var label = new Label { Content = "识别语言", Target = _language }; label.SetResourceReference(ForegroundProperty, "TextBrush"); top.Children.Add(label); top.Children.Add(_language);
        _retry = WorkflowUi.Button("重新识别", async () => await RecognizeAsync());
        _cancel = WorkflowUi.Button("取消识别", () => _request?.Cancel());
        top.Children.Add(_retry); top.Children.Add(_cancel);
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var bottom = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        _copy = WorkflowUi.Button("复制表格", () => WorkflowUi.Try(this, () =>
        {
            CommitGrid(); Clipboard.SetText(TableExport.ToTsv(_table!)); _status.Text = "已复制";
        }));
        _save = WorkflowUi.Button("导出表格", Save);
        actions.Children.Add(_copy); actions.Children.Add(_save); DockPanel.SetDock(actions, Dock.Right); bottom.Children.Add(actions); bottom.Children.Add(_status);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var split = new Grid(); split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        split.Children.Add(new ScrollViewer { Content = new Image { Source = PngCodec.Decode(png), Stretch = Stretch.Uniform }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        FrameworkElement result = _grid;
        result.SetResourceReference(Control.BackgroundProperty, "PanelBrush"); result.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        System.Windows.Automation.AutomationProperties.SetName(result, "识别表格，可编辑单元格");
        var rowStyle = new Style(typeof(DataGridRow)); rowStyle.Setters.Add(new Setter(Control.BackgroundProperty, FindResource("PanelBrush"))); rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, FindResource("TextBrush"))); _grid.RowStyle = rowStyle;
        var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, FindResource("SurfaceMutedBrush"))); headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, FindResource("TextBrush"))); headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5))); _grid.ColumnHeaderStyle = headerStyle;
        _grid.AutoGeneratingColumn += (_, e) =>
        {
            if (e.Column is DataGridTextColumn column)
            {
                var style = new Style(typeof(TextBlock)); style.Setters.Add(new Setter(TextBlock.ForegroundProperty, FindResource("TextBrush"))); style.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(6, 4, 6, 4))); column.ElementStyle = style;
                var editStyle = new Style(typeof(TextBox)); editStyle.Setters.Add(new Setter(Control.BackgroundProperty, FindResource("PanelBrush"))); editStyle.Setters.Add(new Setter(Control.ForegroundProperty, FindResource("TextBrush"))); column.EditingElementStyle = editStyle;
            }
        };
        Grid.SetColumn(result, 2); split.Children.Add(result); root.Children.Add(split);
        _copy.IsEnabled = _save.IsEnabled = _cancel.IsEnabled = false;
        Loaded += async (_, _) =>
        {
            try
            {
                foreach (var language in LocalRecognitionProvider.Languages)
                    _language.Items.Add(new ComboBoxItem { Tag = language.Tag, Content = new TextBlock { Text = language.Name, Foreground = Brushes.Black } });
                _language.SelectedItem = _language.Items.Cast<ComboBoxItem>().FirstOrDefault(x => ((string)x.Tag).StartsWith("zh")) ?? _language.Items.Cast<ComboBoxItem>().FirstOrDefault();
                await RecognizeAsync();
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        };
        Closed += (_, _) => { _closed = true; _request?.Cancel(); };
    }
    internal async Task RecognizeAsync()
    {
        if (_request is not null) return;
        if (_language.SelectedItem is not ComboBoxItem) { _status.Text = "请先在 Windows 设置中安装 OCR 语言包"; return; }
        _request = new(); _retry.IsEnabled = _copy.IsEnabled = _save.IsEnabled = _language.IsEnabled = false; _cancel.IsEnabled = true;
        _status.Text = "正在识别表格…";
        try
        {
            var result = await TableRecognition.RecognizeAsync(_png, (string)((ComboBoxItem)_language.SelectedItem).Tag, _request.Token);
            if (_closed) return;
            if (result.Status == FeatureAvailability.Available) { _table = result.Table; _grid.ItemsSource = _table.DefaultView; }
            _status.Text = result.Status == FeatureAvailability.Available ? $"{result.Table.Rows.Count} 行 × {result.Table.Columns.Count} 列 · 请核对数字和空白单元格" : result.Detail;
        }
        catch (OperationCanceledException) { if (!_closed) _status.Text = "已取消识别"; }
        catch (Exception ex) { if (!_closed) _status.Text = ex.Message; }
        finally
        {
            _request.Dispose(); _request = null;
            if (!_closed) { _retry.IsEnabled = _language.IsEnabled = true; _cancel.IsEnabled = false; _copy.IsEnabled = _save.IsEnabled = _table is not null; }
        }
    }
    private void CommitGrid() { _grid.CommitEdit(DataGridEditingUnit.Cell, true); _grid.CommitEdit(DataGridEditingUnit.Row, true); }
    private void Save() => WorkflowUi.Try(this, () =>
    {
        CommitGrid();
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Excel 工作簿|*.xlsx|CSV 表格|*.csv", FileName = "表格.xlsx" };
        if (dialog.ShowDialog(this) != true) return;
        TableExport.Save(_table!, dialog.FileName);
        _status.Text = "已保存";
    });
}
