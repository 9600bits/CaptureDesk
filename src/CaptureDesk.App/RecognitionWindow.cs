using System.IO;
using System.Windows;
using System.Windows.Controls;
using CaptureDesk.Core;
using CaptureDesk.Media;
using CaptureDesk.Native;

namespace CaptureDesk.App;

public sealed class RecognitionWindow : Window
{
    private readonly byte[] _png;
    private readonly ComboBox _languages = new() { MinWidth = 140, DisplayMemberPath = "Name", Foreground = System.Windows.Media.Brushes.Black, Background = System.Windows.Media.Brushes.White };
    private readonly TextBox _result = new() { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12) };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly Button _recognize;
    private readonly Button _copy;
    private readonly Button _save;
    private CancellationTokenSource? _request;
    private bool _closed;
    private sealed record LanguageChoice(string Tag, string Name);
    public RecognitionWindow(byte[] png)
    {
        _png = png;
        var nativeText = new Style(typeof(TextBlock), (Style)FindResource(typeof(TextBlock)));
        nativeText.Setters.Add(new Setter(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Black));
        _languages.Resources[typeof(TextBlock)] = nativeText;
        var languageText = new FrameworkElementFactory(typeof(TextBlock));
        languageText.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Black);
        languageText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name"));
        _languages.DisplayMemberPath = "";
        _languages.ItemTemplate = new DataTemplate { VisualTree = languageText };
        Title = "文字识别"; Width = 850; Height = 580; MinWidth = 640; MinHeight = 400;
        Style = (Style)FindResource("AppWindowStyle");
        SetResourceReference(BackgroundProperty, "CanvasBrush");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Margin = new Thickness(20) };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        var languageLabel = new Label { Content = "识别语言", Target = _languages, VerticalAlignment = VerticalAlignment.Center };
        languageLabel.SetResourceReference(ForegroundProperty, "TextBrush"); top.Children.Add(languageLabel);
        top.Children.Add(_languages);
        _recognize = WorkflowUi.Button("重新识别", async () => await RecognizeAsync());
        top.Children.Add(_recognize);
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var bottom = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        _copy = WorkflowUi.Button("复制", () => WorkflowUi.Try(this, () => Clipboard.SetText(_result.Text)));
        _save = WorkflowUi.Button("保存文本", Save);
        actions.Children.Add(_copy); actions.Children.Add(_save);
        DockPanel.SetDock(actions, Dock.Right); bottom.Children.Add(actions); bottom.Children.Add(_status);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition()); split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); split.ColumnDefinitions.Add(new ColumnDefinition());
        var preview = new ScrollViewer { Content = new Image { Source = PngCodec.Decode(png), Stretch = System.Windows.Media.Stretch.Uniform }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        split.Children.Add(preview);
        _result.SetResourceReference(BackgroundProperty, "PanelBrush"); _result.SetResourceReference(ForegroundProperty, "TextBrush");
        System.Windows.Automation.AutomationProperties.SetName(_result, "识别结果，可编辑");
        Grid.SetColumn(_result, 2); split.Children.Add(_result); root.Children.Add(split);
        Content = root;
        _copy.IsEnabled = _save.IsEnabled = false;
        Loaded += async (_, _) =>
        {
            try
            {
                var choices = LocalRecognitionProvider.Languages.Select(x => new LanguageChoice(x.Tag, x.Name)).ToArray();
                _languages.ItemsSource = choices;
                _languages.SelectedItem = choices.FirstOrDefault(x => x.Tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) ?? choices.FirstOrDefault();
                await RecognizeAsync();
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        };
        Closed += (_, _) => { _closed = true; _request?.Cancel(); };
    }
    private async Task RecognizeAsync()
    {
        if (_languages.SelectedItem is not LanguageChoice language) { _status.Text = "请在 Windows 语言设置中安装 OCR 语言包。"; return; }
        _request?.Cancel(); _request?.Dispose(); _request = new();
        _recognize.IsEnabled = _languages.IsEnabled = _copy.IsEnabled = _save.IsEnabled = false;
        _status.Text = "正在识别…";
        try
        {
            var result = await new LocalRecognitionProvider().RecognizeAsync(new RecognitionRequest(_png, language.Tag), _request.Token);
            if (_closed) return;
            _result.Text = result.Text;
            _status.Text = result.Status == FeatureAvailability.Available ? (result.Text.Length > 0 ? $"{result.Text.Length} 个字符" : "未检测到文字") : result.Detail;
            _copy.IsEnabled = _save.IsEnabled = result.Text.Length > 0;
        }
        catch (OperationCanceledException) { }
        finally { if (!_closed) _recognize.IsEnabled = _languages.IsEnabled = true; }
    }
    private void Save() => WorkflowUi.Try(this, () =>
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "文本文件|*.txt", FileName = "识别结果.txt" };
        if (dialog.ShowDialog(this) == true) { File.WriteAllText(dialog.FileName, _result.Text); _status.Text = "已保存"; }
    });
}
