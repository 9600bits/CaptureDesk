using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using CaptureDesk.Core;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;

namespace CaptureDesk.App;

public partial class SettingsWindow : Window
{
    private sealed record Page(string Title, System.Windows.Media.Geometry Icon);
    private AppSettings _draft = Clone(App.Settings);
    private readonly Dictionary<int, List<string>> _pageFields = new();
    private int _page;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public SettingsWindow()
    {
        InitializeComponent();
        Navigation.ItemsSource = new[]
        {
            Menu("系统", "Settings"), Menu("外观", "Appearance"), Menu("截图", "Capture"),
            Menu("贴图", "Pin"), Menu("保存", "Save"), Menu("标注", "Annotation"),
            Menu("快捷键", "Keyboard"), Menu("鼠标", "Mouse"), Menu("导出 / 导入", "Transfer"), Menu("关于", "Info")
        };
        Navigation.SelectedIndex = 0;
    }
    private Page Menu(string title, string icon) => new(title, (System.Windows.Media.Geometry)FindResource(icon + "Icon"));
    internal static AppSettings Clone(AppSettings value) => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(value))!;
    internal void SelectPage(int index) => Navigation.SelectedIndex = index;
    private void Navigation_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Navigation.SelectedItem is not Page page) return;
        _page = Navigation.SelectedIndex;
        PageTitle.Text = page.Title;
        BuildPage();
        PageScroll.ScrollToTop();
    }
    private void Bind(FrameworkElement control, DependencyProperty property, string field, string label)
    {
        _pageFields[_page].Add(field);
        control.SetBinding(property, new Binding(field) { Source = _draft, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        AutomationProperties.SetName(control, label);
    }
    private void Section(string title)
    {
        PageContent.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.FromOpenTypeWeight(520),
            Margin = new Thickness(0, PageContent.Children.Count == 0 ? 0 : 26, 0, 9) });
    }
    private void Row(string label, FrameworkElement control)
    {
        var grid = new Grid { MinHeight = 56, Margin = new Thickness(16, 0, 16, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 9, 18, 9) };
        grid.Children.Add(text);
        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        var border = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
        border.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        PageContent.Children.Add(border);
    }
    private void Choice(string label, string field, params object[] choices)
    {
        var current = typeof(AppSettings).GetProperty(field)!.GetValue(_draft);
        var values = choices.ToList();
        if (current is not null && !values.Contains(current)) values.Add(current);
        var box = new ComboBox { ItemsSource = values, Width = 210 };
        Bind(box, ComboBox.SelectedItemProperty, field, label);
        Row(label, box);
    }
    private void Toggle(string label, string field)
    {
        var check = new CheckBox { Content = label, MinHeight = 42, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 0, 0, 0) };
        check.SetResourceReference(ForegroundProperty, "TextBrush");
        Bind(check, CheckBox.IsCheckedProperty, field, label);
        var border = new Border { Padding = new Thickness(16, 4, 16, 4), BorderThickness = new Thickness(0, 0, 0, 1), Child = check };
        border.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        PageContent.Children.Add(border);
    }
    private Button ActionButton(string label, RoutedEventHandler action)
    {
        var button = new Button { Content = label, Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(button, label);
        button.Click += action;
        return button;
    }
    private void ReadOnlyRow(string label, string value) => Row(label, new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, MaxWidth = 276, Margin = new Thickness(0, 8, 0, 8) });
    private void BuildPage()
    {
        PageContent.Children.Clear();
        _pageFields[_page] = new List<string>();
        ResetPageButton.Visibility = _page is 6 or 8 or 9 ? Visibility.Collapsed : Visibility.Visible;
        switch (_page)
        {
            case 0:
                Section("启动与关闭");
                Toggle("开机后自动启动", nameof(AppSettings.StartWithWindows));
                Choice("关闭主窗口时", nameof(AppSettings.CloseBehavior), "最小化到托盘", "退出程序");
                Section("任务栏托盘");
                ReadOnlyRow("双击托盘图标", "显示主窗口");
                ReadOnlyRow("右键托盘图标", "截图、历史、设置、退出");
                break;
            case 1:
                Section("主题");
                Choice("主题模式", nameof(AppSettings.ThemeMode), "浅色", "深色");
                ReadOnlyRow("强调色", "森林绿");
                ReadOnlyRow("界面字体", "MiSans");
                break;
            case 2:
                Section("选区");
                Toggle("截图中包含鼠标指针", nameof(AppSettings.ShowCursor));
                Toggle("显示三分辅助线", nameof(AppSettings.ShowGuides));
                Choice("选区边框宽度（px）", nameof(AppSettings.CaptureBorderWidth), 1, 2, 3, 4);
                Section("历史");
                Choice("本次会话保留的截图数量", nameof(AppSettings.HistoryLimit), 10, 25, 50, 100, 200, 500);
                break;
            case 3:
                Section("新建贴图");
                Toggle("默认置顶", nameof(AppSettings.PinAlwaysOnTop));
                Toggle("显示窗口阴影", nameof(AppSettings.PinShadow));
                Choice("默认不透明度（%）", nameof(AppSettings.PinOpacity), 25, 50, 75, 90, 100);
                Section("操作");
                ReadOnlyRow("移动", "按住图像拖动");
                ReadOnlyRow("关闭", "双击图像 / Esc");
                break;
            case 4:
                Section("文件");
                Choice("默认格式", nameof(AppSettings.SaveFormat), "PNG", "JPEG");
                Choice("JPEG 质量", nameof(AppSettings.ImageQuality), 60, 75, 85, 90, 95, 100);
                Section("保存位置");
                var folder = new TextBox { Text = _draft.DefaultSaveFolder, Margin = new Thickness(0, 0, 0, 12) };
                Bind(folder, TextBox.TextProperty, nameof(AppSettings.DefaultSaveFolder), "默认保存文件夹");
                PageContent.Children.Add(folder);
                PageContent.Children.Add(ActionButton("选择文件夹…", (_, _) =>
                {
                    var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择保存文件夹", InitialDirectory = Directory.Exists(folder.Text) ? folder.Text : "" };
                    if (dialog.ShowDialog(this) == true) folder.Text = dialog.FolderName;
                }));
                break;
            case 5:
                Section("默认画笔");
                Choice("标注颜色", nameof(AppSettings.AnnotationColor), "森林绿", "朱红", "钴蓝", "炭黑");
                Choice("线条粗细（px）", nameof(AppSettings.AnnotationWidth), 1, 2, 3, 4, 6, 8);
                Toggle("记住上次使用的工具", nameof(AppSettings.RememberAnnotationTool));
                break;
            case 6:
                Section("全局");
                ReadOnlyRow("区域截图", "Ctrl + Shift + A");
                Section("截图时");
                ReadOnlyRow("完成截图", "Enter / 双击选区");
                ReadOnlyRow("取消截图", "Esc / 鼠标右键");
                Section("编辑器");
                ReadOnlyRow("撤销 / 重做", "Ctrl + Z / Ctrl + Y");
                ReadOnlyRow("复制 / 保存", "Ctrl + C / Ctrl + S");
                ReadOnlyRow("删除选中的标注", "Delete");
                break;
            case 7:
                Section("贴图滚轮");
                Choice("滚轮操作", nameof(AppSettings.MouseWheelAction), "缩放贴图", "调整透明度");
                Choice("缩放步长（%）", nameof(AppSettings.MouseZoomStep), 1, 5, 10, 15, 20);
                Choice("透明度步长（%）", nameof(AppSettings.MouseOpacityStep), 1, 5, 10, 20);
                ReadOnlyRow("临时调整透明度", "按住 Ctrl 滚动");
                break;
            case 8:
                Section("配置文件");
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 20) };
                actions.Children.Add(ActionButton("导出配置…", Export_Click));
                actions.Children.Add(ActionButton("导入配置…", Import_Click));
                PageContent.Children.Add(actions);
                ReadOnlyRow("文件格式", "JSON");
                ReadOnlyRow("内容", "主题、截图、贴图及编辑偏好");
                break;
            case 9:
                Section("CaptureDesk");
                ReadOnlyRow("版本", "0.4");
                ReadOnlyRow("平台", "Windows x64");
                ReadOnlyRow("配置位置", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CaptureDesk"));
                break;
        }
    }
    private bool Validate()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_draft.DefaultSaveFolder) || !Path.IsPathFullyQualified(_draft.DefaultSaveFolder))
                throw new InvalidDataException("请选择完整的保存文件夹路径。");
            _draft.DefaultSaveFolder = Path.GetFullPath(_draft.DefaultSaveFolder);
            if (_draft.HistoryLimit is < 1 or > 500 || _draft.CaptureBorderWidth is < 1 or > 4 ||
                _draft.PinOpacity is < 10 or > 100 || _draft.AnnotationWidth is < 1 or > 20 ||
                _draft.ImageQuality is < 1 or > 100 || _draft.MouseZoomStep is < 1 or > 50 ||
                _draft.MouseOpacityStep is < 1 or > 50) throw new InvalidDataException("配置中的数值超出允许范围。");
            if (!new[] { "浅色", "深色" }.Contains(_draft.ThemeMode) ||
                !new[] { "PNG", "JPEG" }.Contains(_draft.SaveFormat) ||
                !new[] { "最小化到托盘", "退出程序" }.Contains(_draft.CloseBehavior) ||
                !new[] { "缩放贴图", "调整透明度" }.Contains(_draft.MouseWheelAction) ||
                !new[] { "森林绿", "朱红", "钴蓝", "炭黑" }.Contains(_draft.AnnotationColor))
                throw new InvalidDataException("配置包含不支持的选项。");
            _draft.AccentColor = "#2F6B4B";
            _draft.DarkMode = _draft.ThemeMode == "深色";
            return true;
        }
        catch (Exception ex) { StatusText.Text = ex.Message; return false; }
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!Validate()) return;
        try { App.ApplySettings(_draft); DialogResult = true; }
        catch (Exception ex) { StatusText.Text = $"保存失败：{ex.Message}"; }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void ResetPage_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings();
        foreach (var name in _pageFields[_page])
        {
            var property = typeof(AppSettings).GetProperty(name)!;
            property.SetValue(_draft, property.GetValue(defaults));
        }
        BuildPage();
        StatusText.Text = "本页已恢复默认，保存后生效";
    }
    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        _draft = new AppSettings();
        BuildPage();
        StatusText.Text = "已恢复全部默认设置，保存后生效";
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!Validate()) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "CaptureDesk 配置|*.json", FileName = "CaptureDesk.settings.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(_draft, JsonOptions)); StatusText.Text = "配置已导出"; }
        catch (Exception ex) { StatusText.Text = $"导出失败：{ex.Message}"; }
    }
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "CaptureDesk 配置|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        var previous = _draft;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 1024 * 1024) throw new InvalidDataException("配置文件过大。");
            _draft = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(dialog.FileName)) ?? throw new InvalidDataException("配置文件为空。");
            if (!Validate()) { _draft = previous; return; }
            BuildPage();
            StatusText.Text = "配置已载入，保存后生效";
        }
        catch (Exception ex) { _draft = previous; StatusText.Text = $"导入失败：{ex.Message}"; }
    }
}

