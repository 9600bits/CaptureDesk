using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CaptureDesk.App;

internal static class AnnotationVerification
{
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static byte[] Pixels(BitmapSource source)
    {
        var image = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0); return pixels;
    }
    internal static void Run(string directory, List<string> passed)
    {
        var data = new byte[400 * 260 * 4]; new Random(92).NextBytes(data);
        for (var i = 3; i < data.Length; i += 4) data[i] = 255;
        var source = BitmapSource.Create(400, 260, 96, 96, PixelFormats.Bgra32, null, data, 1600); source.Freeze();
        var editor = new InlineImageEditor(source);
        var host = new Window { Content = new Viewbox { Child = editor.CanvasHost }, Width = 620, Height = 440, Style = (Style)System.Windows.Application.Current.FindResource("AppWindowStyle") };
        host.Show(); host.UpdateLayout();
        var original = Pixels(editor.RenderDocument());
        editor.SetTool("mosaic"); editor.BeginStroke(new Point(20, 20)); editor.ContinueStroke(new Point(180, 120)); editor.EndStroke();
        var masked = Pixels(editor.RenderDocument());
        Require(!masked.SequenceEqual(original), "Mosaic did not change exported pixels");
        Require(masked.AsSpan((200 * 400 + 200) * 4, 4).SequenceEqual(original.AsSpan((200 * 400 + 200) * 4, 4)), "Mosaic altered pixels outside region");
        editor.Undo(); Require(Pixels(editor.RenderDocument()).SequenceEqual(original), "Mosaic undo not pixel exact");
        editor.Redo(); Require(Pixels(editor.RenderDocument()).SequenceEqual(masked), "Mosaic redo changed result");
        editor.SetTool("mosaic"); editor.BeginStroke(new Point(2, 2)); editor.ContinueStroke(new Point(3, 3)); editor.EndStroke(); editor.Undo();
        passed.Add("PASS real mosaic pixels, unchanged exterior, tiny regions, exact undo/redo");
        editor.SetPen(Colors.Red, 5);
        foreach (var tool in new[] { "line", "highlight", "number" })
        {
            var count = editor.AnnotationCanvas.Children.Count;
            editor.SetTool(tool); editor.BeginStroke(new Point(200, 30)); editor.ContinueStroke(new Point(340, 100)); editor.EndStroke();
            Require(editor.AnnotationCanvas.Children.Count == count + 1, tool + " did not create annotation");
            editor.Undo(); Require(editor.AnnotationCanvas.Children.Count == count, tool + " undo failed"); editor.Redo();
        }
        editor.SetTool("text"); editor.BeginStroke(new Point(100, 180));
        editor.AnnotationCanvas.Children.OfType<TextBox>().Single().Text = "原文字"; editor.CommitText();
        var text = editor.AnnotationCanvas.Children.OfType<TextBlock>().Single();
        editor.EditText(text); editor.AnnotationCanvas.Children.OfType<TextBox>().Single().Text = "修改后的文字"; editor.CommitText();
        Require(text.Text == "修改后的文字", "Text edit failed"); editor.Undo(); Require(text.Text == "原文字", "Text edit undo failed"); editor.Redo();
        Require(text.Text == "修改后的文字" && ((SolidColorBrush)text.Foreground).Color == Colors.Red, "Text redo failed or pen color lost");
        passed.Add("PASS line, translucent highlighter, sequence markers and text re-edit undo/redo");
        var menu = AnnotationMenu.Create(() => editor, editor.SetTool);
        ((MenuItem)menu.Items[3]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Require(editor.CurrentTool == "mosaic", "Menu did not select mosaic");
        var colors = (MenuItem)menu.Items[5]; ((MenuItem)colors.Items[2]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var widths = (MenuItem)menu.Items[6]; ((MenuItem)widths.Items[3]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Require(editor.PenColor == (Color)ColorConverter.ConvertFromString("#2F6FED") && editor.StrokeWidth == 8, "Menu pen settings not applied");
        passed.Add("PASS advanced tool menu and pen color/width actions");
        editor.RenderDocument(); host.UpdateLayout();
        UiSnapshot.Save(host, System.IO.Path.Combine(directory, "advanced-annotations-final.png")); host.Close();
        var pin = new PinWindow(source); pin.Show(); pin.UpdateLayout(); pin.SetLocked(true);
        var width = pin.Width;
        pin.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = UIElement.MouseWheelEvent });
        Require(pin.Width == width && pin.ResizeMode == ResizeMode.NoResize, "Locked pin resized");
        pin.SetLocked(false);
        pin.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = UIElement.MouseWheelEvent });
        Require(pin.Width > width, "Unlocked pin did not resize"); pin.Close();
        passed.Add("PASS pin lock prevents resizing and unlock restores zoom");
    }
}
