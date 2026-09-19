using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureDesk.Core;
using CaptureDesk.Native;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Point = System.Windows.Point;

namespace CaptureDesk.App;

// Deterministic UI regression checks, executed in a separate STA app process.
// Diagnostic startup never loads or writes the user's settings or clipboard.
internal static class UiVerification
{
    private static BitmapSource TestFrame(int width = 1200, int height = 760)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                pixels[offset] = (byte)(170 + y % 75);
                pixels[offset + 1] = (byte)(170 + x % 75);
                pixels[offset + 2] = 225;
                pixels[offset + 3] = 255;
            }
        var frame = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        frame.Freeze();
        return frame;
    }
    public static CaptureOverlay CreateOverlayPreview()
    {
        var frame = TestFrame();
        var overlay = new CaptureOverlay(frame, new CaptureRegion(0, 0, frame.PixelWidth, frame.PixelHeight));
        overlay.ContentRendered += (_, _) =>
        {
            overlay.BeginSelection(new Point(180, 150));
            overlay.EndSelection(new Point(640, 440));
            overlay.UpdateLayout();
        };
        return overlay;
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        foreach (var child in Children(root))
            if (child is T element && AutomationProperties.GetName(element) == name) return element;
        throw new InvalidOperationException($"Control not found: {name}");
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Children(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static byte[] Pixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(bytes, converted.PixelWidth * 4, 0);
        return bytes;
    }
    public static void Run(string resultPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath))!);
        var passed = new List<string>();
        var uiFont = (FontFamily)System.Windows.Application.Current.FindResource("UiFont");
        foreach (var weight in new[] { FontWeight.FromOpenTypeWeight(330), FontWeight.FromOpenTypeWeight(520) })
        {
            var face = new Typeface(uiFont, FontStyles.Normal, weight, FontStretches.Normal);
            Require(face.TryGetGlyphTypeface(out var glyph), "Embedded MiSans did not resolve to a font file.");
            Require(glyph.FontUri.ToString().Contains("MiSans", StringComparison.OrdinalIgnoreCase), "UI silently fell back from MiSans.");
            Require(!face.IsBoldSimulated, "MiSans is being artificially emboldened.");
            foreach (var letter in "截图设置保存标注透明度快捷键ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")
                Require(glyph.CharacterToGlyphMap.ContainsKey(letter), $"MiSans is missing U+{(int)letter:X4}.");
            passed.Add($"PASS embedded font {weight}: {glyph.FontUri}");
        }
        var desktopBounds = NativeCaptureService.GetVirtualScreenRegion();
        var liveFrame = NativeCaptureService.CaptureRegion(desktopBounds, true);
        Require(liveFrame.PixelWidth == desktopBounds.Width && liveFrame.PixelHeight == desktopBounds.Height, "Native capture dimensions do not match the desktop.");
        passed.Add("PASS live Win32 desktop capture with cursor enabled");
        var frame = TestFrame();
        var overlay = new CaptureOverlay(frame, new CaptureRegion(-1200, 0, 1200, 760));
        overlay.Show();
        overlay.UpdateLayout();
        var input = (Grid)overlay.FindName("InputSurface");
        input.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        Require(ReferenceEquals(Mouse.Captured, input), "Selection must capture the event surface, not the Window.");
        input.ReleaseMouseCapture();
        var start = new Point(input.ActualWidth * .2, input.ActualHeight * .25);
        var end = new Point(input.ActualWidth * .7, input.ActualHeight * .75);
        overlay.BeginSelection(start);
        overlay.UpdateSelection(end);
        overlay.EndSelection(end);
        overlay.UpdateLayout();
        Require(overlay.CapturedRegion.Width >= 599 && overlay.CapturedRegion.Height == 380, $"Drag dimensions are not physical pixels: {overlay.CapturedRegion}; view {input.ActualWidth} × {input.ActualHeight}.");
        var bar = (Border)overlay.FindName("ActionBar");
        Require(bar.Parent is Canvas && bar.Visibility == Visibility.Visible, "Confirm controls must live in the positioning Canvas.");
        Require(Canvas.GetTop(bar) + bar.ActualHeight <= input.ActualHeight, "Toolbar extends beyond the overlay.");
        overlay.CreateCapture();
        var expected = NativeCaptureService.Crop(frame, overlay.CapturedRegion, new CaptureRegion(-1200, 0, 1200, 760));
        Require(Pixels(overlay.CapturedSource!).SequenceEqual(Pixels(expected)), "Export contains overlay graphics or an incorrect crop.");
        overlay.BeginSelection(end);
        overlay.EndSelection(start);
        Require(overlay.CapturedRegion.Width >= 599, "Reverse dragging failed.");
        var prior = overlay.CapturedRegion;
        overlay.ResizeSelection("SE", new Point(input.ActualWidth * .8, input.ActualHeight * .85));
        overlay.EndSelection(new Point(input.ActualWidth * .8, input.ActualHeight * .85));
        Require(overlay.CapturedRegion.Width > prior.Width && overlay.CapturedRegion.Height > prior.Height, "Corner resize did not expand the region.");
        Require(Children(overlay).OfType<System.Windows.Controls.Primitives.Thumb>().Count() == 8, "Expected eight resize handles.");
        overlay.ResizeSelection("NW", new Point(input.ActualWidth * .1, input.ActualHeight * .1));
        Require(overlay.CapturedRegion.X < prior.X, "Top-left resize failed.");
        overlay.UpdateLayout();
        UiSnapshot.Save(overlay, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(resultPath))!, "selection-toolbar.png"));
        Find<Button>(overlay, "重新选择区域").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(overlay.CapturedRegion.IsEmpty && bar.Visibility == Visibility.Collapsed, "Reselect did not clear selection.");
        passed.Add("PASS eight selection handles, corner resize, coordinates, reselect toolbar action");
        overlay.BeginSelection(start);
        overlay.EndSelection(end);
        var windowCount = System.Windows.Application.Current.Windows.Count;
        Find<Button>(overlay, "矩形标注").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(System.Windows.Application.Current.Windows.Count == windowCount, "Selecting annotation opened a new Window.");
        Require(overlay.IsVisible && overlay.InlineEditor is not null, "Inline editor not visible in the existing overlay.");
        overlay.UpdateLayout();
        overlay.CreateCapture();
        var baseline = Pixels(overlay.CapturedSource!);
        Require(baseline.SequenceEqual(Pixels(NativeCaptureService.Crop(frame, overlay.CapturedRegion, new CaptureRegion(-1200, 0, 1200, 760)))), "Inline view changed the original image before annotation.");
        var inline = overlay.InlineEditor!;
        inline.BeginStroke(new Point(40, 40));
        inline.ContinueStroke(new Point(250, 140));
        inline.EndStroke();
        overlay.CreateCapture();
        var annotated = Pixels(overlay.CapturedSource!);
        Require(!baseline.SequenceEqual(annotated), "Inline annotation missing in export.");
        Find<Button>(overlay, "撤销标注").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        overlay.CreateCapture();
        Require(Pixels(overlay.CapturedSource!).SequenceEqual(baseline), "Inline undo did not restore original pixels.");
        Find<Button>(overlay, "重做标注").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        overlay.CreateCapture();
        Require(Pixels(overlay.CapturedSource!).SequenceEqual(annotated), "Inline redo changed pixels.");
        Find<Button>(overlay, "文字标注").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        inline.BeginStroke(new Point(70, 200));
        inline.AnnotationCanvas.Children.OfType<TextBox>().Single().Text = "直接在选区标注";
        overlay.CreateCapture();
        Require(inline.AnnotationCanvas.Children.OfType<TextBlock>().Any(), "Inline text not committed for export.");
        Require(overlay.CapturedSource!.PixelWidth == overlay.CapturedRegion.Width, "Inline output dimensions changed.");
        UiSnapshot.Save(overlay, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(resultPath))!, "inline-editor.png"));
        passed.Add("PASS inline tools create no Window, pixel-exact original, annotation export, undo/redo, text and dimensions");
        overlay.Close();
        passed.Add("PASS capture input owner, non-zero drag, reverse drag, negative origin, toolbar bounds, original pixels");

        var editor = new EditorWindow(frame, PngCodec.Encode(frame));
        editor.Show();
        editor.UpdateLayout();
        var canvas = (Canvas)editor.FindName("AnnotationCanvas");
        canvas.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        Require(ReferenceEquals(Mouse.Captured, canvas), "Annotation input must be captured by the canvas.");
        editor.EndStroke();
        canvas.ReleaseMouseCapture();
        editor.Undo();
        foreach (var tool in new[] { ("rect", "矩形工具"), ("ellipse", "椭圆工具"), ("arrow", "箭头工具"), ("pencil", "画笔工具"), ("redact", "实色遮挡工具") })
        {
            Find<Button>(editor, tool.Item2).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            editor.BeginStroke(new Point(50, 60));
            editor.ContinueStroke(new Point(300, 190));
            editor.EndStroke();
        }
        Require(canvas.Children.Count == 5, "A drawing tool failed to add an annotation.");
        var arrow = canvas.Children[2] as System.Windows.Shapes.Path;
        Require(arrow?.Data is GeometryGroup group && group.Children.Count == 2, "Arrowhead is missing.");
        var beforeUndo = Pixels(editor.RenderDocument());
        Find<Button>(editor, "撤销").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(canvas.Children.Count == 4, "Undo button failed.");
        Find<Button>(editor, "重做").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(canvas.Children.Count == 5 && Pixels(editor.RenderDocument()).SequenceEqual(beforeUndo), "Redo changed exported pixels.");
        editor.SetTool("text");
        editor.BeginStroke(new Point(500, 300));
        var textbox = canvas.Children.OfType<TextBox>().Single();
        textbox.Text = "测试标注";
        editor.CommitText();
        Require(canvas.Children.OfType<TextBlock>().Single().Text == "测试标注", "Text was not committed to the document.");
        editor.SetTool("select");
        editor.UpdateLayout();
        editor.BeginStroke(new Point(150, 110));
        editor.ContinueStroke(new Point(190, 150));
        editor.EndStroke();
        Require(canvas.Children.OfType<FrameworkElement>().Any(x => x.RenderTransform is TranslateTransform t && t.X != 0), "Selection tool failed to move an annotation.");
        var rendered = editor.RenderDocument();
        Require(rendered.PixelWidth == frame.PixelWidth && rendered.PixelHeight == frame.PixelHeight, "Export dimensions changed.");
        var output = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(resultPath))!, "editor-verification.png");
        PngCodec.Save(rendered, output);
        Require(Pixels(PngCodec.Decode(File.ReadAllBytes(output))).SequenceEqual(Pixels(rendered)), "PNG round trip lost annotation pixels.");
        UiSnapshot.Save(editor, Path.Combine(Path.GetDirectoryName(output)!, "editor-window.png"));
        editor.Close();
        passed.Add("PASS rectangle, ellipse, arrowhead, pencil, redaction, text, select/move, undo/redo buttons, pixel-exact PNG export");

        var pin = new PinWindow(frame);
        pin.Show(); pin.UpdateLayout();
        var previousTopmost = pin.Topmost;
        Find<Button>(pin, "切换置顶").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(pin.Topmost != previousTopmost, "Pin topmost button did not toggle.");
        Find<Button>(pin, "切换透明度").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(pin.Opacity < 1, "Pin opacity button did not change opacity.");
        var pinWidth = pin.Width;
        pin.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = UIElement.MouseWheelEvent });
        Require(pin.Width > pinWidth, "Pin mouse wheel did not zoom.");
        Find<Button>(pin, "关闭贴图").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(!pin.IsVisible, "Pin close button failed.");
        passed.Add("PASS pin topmost, opacity, wheel zoom and close buttons");

        var settings = new SettingsWindow();
        settings.Show();
        settings.UpdateLayout();
        for (var i = 0; i < 10; i++) { settings.SelectPage(i); settings.UpdateLayout(); }
        settings.SelectPage(2); settings.UpdateLayout();
        Require(settings.FontFamily.Source.Contains("MiSans"), "Settings window does not use MiSans.");
        var label = Children(settings).OfType<TextBlock>().First(x => x.Text == "选区边框宽度（px）");
        var labelFace = new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch);
        Require(labelFace.TryGetGlyphTypeface(out var labelGlyph) && labelGlyph.FontUri.ToString().EndsWith("misans-regular.otf"), "Settings body text is not rendered with MiSans Regular.");
        Require(Children(settings).OfType<LucideIcon>().Count() >= 10, "Settings navigation icons are missing.");
        var guides = Find<CheckBox>(settings, "显示三分辅助线");
        guides.IsChecked = false;
        settings.SelectPage(0); settings.UpdateLayout();
        settings.SelectPage(2); settings.UpdateLayout();
        Require(Find<CheckBox>(settings, "显示三分辅助线").IsChecked == false, "Navigation lost the draft value.");
        Require(App.Settings.ShowGuides, "Editing the draft changed live settings before Save.");
        settings.Close();
        passed.Add("PASS all 10 settings pages, control binding, draft retention, cancel isolation");
        File.WriteAllLines(resultPath, passed);
        MediaVerification.Run(Path.GetDirectoryName(Path.GetFullPath(resultPath))!, passed);
        AnnotationVerification.Run(Path.GetDirectoryName(Path.GetFullPath(resultPath))!, passed);
        StructuredRecognitionVerification.Run(Path.GetDirectoryName(Path.GetFullPath(resultPath))!, passed);
        File.WriteAllLines(resultPath, passed);
    }
}
