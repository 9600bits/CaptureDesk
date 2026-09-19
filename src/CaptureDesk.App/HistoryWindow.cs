using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CaptureDesk.Native;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;

namespace CaptureDesk.App;

public sealed class HistoryWindow : Window
{
    public HistoryWindow()
    {
        Style = (Style)FindResource("AppWindowStyle");
        Title = "截图历史"; Width = 860; Height = 620; MinWidth = 620; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "CanvasBrush");
        var layout = new DockPanel { Margin = new Thickness(24) };
        Content = layout;
        var header = new TextBlock { Text = "截图历史", FontSize = 20, FontWeight = FontWeight.FromOpenTypeWeight(520), Margin = new Thickness(0, 0, 0, 20) };
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        var items = App.History.GetRecent();
        if (items.Count == 0)
        {
            layout.Children.Add(new TextBlock { Text = "还没有截图", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            return;
        }
        var list = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        System.Windows.Automation.AutomationProperties.SetName(list, "本次会话截图");
        foreach (var item in items)
        {
            var source = PngCodec.Decode(item.PngBytes);
            var row = new DockPanel { Margin = new Thickness(8) };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            void Add(string label, RoutedEventHandler action)
            {
                var button = new Button { Content = label, Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(8, 0, 0, 0) };
                button.Click += action; actions.Children.Add(button);
            }
            Add("编辑", (_, _) => EditImage(source, layout));
            Add("贴图", (_, _) => new PinWindow(source).Show());
            DockPanel.SetDock(actions, Dock.Right); row.Children.Add(actions);
            row.Children.Add(new Image { Source = source, Width = 140, Height = 88, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 20, 0) });
            row.Children.Add(new TextBlock { Text = $"{item.Width} × {item.Height} px", VerticalAlignment = VerticalAlignment.Center });
            list.Items.Add(row);
        }
        layout.Children.Add(list);
    }
    private void EditImage(System.Windows.Media.Imaging.BitmapSource source, DockPanel history)
    {
        var editor = new InlineImageEditor(source);
        var layout = new DockPanel { Margin = new Thickness(16) };
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        toolbar.Children.Add(WorkflowUi.Button("返回历史", () =>
        {
            if (editor.CanUndo && MessageBox.Show(this, "返回将结束本次编辑，请先复制或保存需要的结果。", "截图历史", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
            Content = history;
        }));
        foreach (var (tool, icon, title) in new[] { ("select", "SelectIcon", "选择标注"), ("rect", "RectangleIcon", "矩形"), ("ellipse", "EllipseIcon", "椭圆"), ("arrow", "ArrowIcon", "箭头"), ("pencil", "PencilIcon", "画笔"), ("text", "TextIcon", "文字"), ("redact", "RedactIcon", "遮挡") })
        {
            var button = new Button { Style = (Style)FindResource("VectorToolButton"), Content = FindResource(icon), ToolTip = title };
            System.Windows.Automation.AutomationProperties.SetName(button, title);
            button.Click += (_, _) => editor.SetTool(tool); toolbar.Children.Add(button);
        }
        var undo = WorkflowUi.Button("撤销", editor.Undo); var redo = WorkflowUi.Button("重做", editor.Redo);
        var more = new Button { Style = (Style)FindResource("VectorToolButton"), Content = FindResource("ChevronDownIcon"), ToolTip = "更多标注 / 颜色 / 粗细" };
        System.Windows.Automation.AutomationProperties.SetName(more, "更多标注");
        more.ContextMenu = AnnotationMenu.Create(() => editor, editor.SetTool);
        more.Click += (_, _) => { more.ContextMenu.PlacementTarget = more; more.ContextMenu.IsOpen = true; };
        toolbar.Children.Add(more);
        undo.IsEnabled = redo.IsEnabled = false;
        editor.Changed += () => { undo.IsEnabled = editor.CanUndo; redo.IsEnabled = editor.CanRedo; };
        toolbar.Children.Add(undo); toolbar.Children.Add(redo);
        toolbar.Children.Add(WorkflowUi.Button("删除标注", editor.DeleteSelected));
        toolbar.Children.Add(WorkflowUi.Button("复制", () => WorkflowUi.Try(this, () => Clipboard.SetImage(editor.RenderDocument()))));
        toolbar.Children.Add(WorkflowUi.Button("保存", () => WorkflowUi.Try(this, () =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG 图像|*.png|JPEG 图像|*.jpg", FileName = "截图.png" };
            if (dialog.ShowDialog(this) == true) PngCodec.Save(editor.RenderDocument(), dialog.FileName);
        })));
        DockPanel.SetDock(toolbar, Dock.Top); layout.Children.Add(toolbar);
        layout.Children.Add(new Viewbox { Child = editor.CanvasHost, Stretch = Stretch.Uniform });
        Content = layout;
    }
}

