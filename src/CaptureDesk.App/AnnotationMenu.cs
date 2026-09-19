using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CaptureDesk.App;

internal static class AnnotationMenu
{
    internal static ContextMenu Create(Func<InlineImageEditor?> getEditor, Action<string> selectTool)
    {
        var menu = new ContextMenu { FontFamily = (FontFamily)System.Windows.Application.Current.FindResource("UiFont"), FontSize = 14 };
        foreach (var (id, title) in new[] { ("line", "直线"), ("highlight", "荧光笔"), ("number", "序号"), ("mosaic", "马赛克") })
        {
            var item = new MenuItem { Header = title, IsCheckable = true };
            item.Click += (_, _) => selectTool(id);
            menu.Opened += (_, _) => item.IsChecked = getEditor()?.CurrentTool == id;
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var colors = new MenuItem { Header = "标注颜色" };
        foreach (var (name, hex) in new[] { ("森林绿", "#2F6B4B"), ("朱红", "#C4493D"), ("钴蓝", "#2F6FED"), ("炭黑", "#202620"), ("金黄", "#E5B52D"), ("白色", "#FFFFFF") })
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var item = new MenuItem { Header = name, IsCheckable = true, Icon = new Border { Width = 14, Height = 14, Background = new SolidColorBrush(color), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) } };
            item.Click += (_, _) => { var editor = getEditor(); if (editor is null) { selectTool("rect"); editor = getEditor(); } editor?.SetPen(color, editor.StrokeWidth); };
            menu.Opened += (_, _) => item.IsChecked = getEditor()?.PenColor == color;
            colors.Items.Add(item);
        }
        menu.Items.Add(colors);
        var widths = new MenuItem { Header = "线条粗细 / 马赛克强度" };
        foreach (var width in new[] { 1, 3, 5, 8, 12 })
        {
            var item = new MenuItem { Header = $"{width} px", IsCheckable = true };
            item.Click += (_, _) => { var editor = getEditor(); if (editor is null) { selectTool("rect"); editor = getEditor(); } editor?.SetPen(editor.PenColor, width); };
            menu.Opened += (_, _) => item.IsChecked = getEditor()?.StrokeWidth == width;
            widths.Items.Add(item);
        }
        menu.Items.Add(widths);
        return menu;
    }
}
