using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CaptureDesk.Native;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using PathShape = System.Windows.Shapes.Path;

namespace CaptureDesk.App;

public partial class EditorWindow : Window
{
    private sealed record Edit(Action Undo, Action Redo);
    private readonly Stack<Edit> _undo = new();
    private readonly Stack<Edit> _redo = new();
    private Point _start;
    private Shape? _draft;
    private FrameworkElement? _selected;
    private Point _moveOrigin;
    private bool _moving;
    private TextBox? _textEditor;
    private string _tool = "rect";
    private readonly double _strokeWidth;
    private readonly System.Windows.Media.Brush _pen;

    public EditorWindow(BitmapSource source, byte[] bytes)
    {
        InitializeComponent();
        SourceImage.Source = source;
        CanvasHost.Width = AnnotationCanvas.Width = SourceImage.Width = source.PixelWidth;
        CanvasHost.Height = AnnotationCanvas.Height = SourceImage.Height = source.PixelHeight;
        _strokeWidth = App.Settings.AnnotationWidth;
        _pen = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Settings.AnnotationColor switch
        {
            "朱红" => "#C4493D", "钴蓝" => "#2F6FED", "炭黑" => "#202620", _ => "#2F6B4B"
        }));
        DocumentSize.Text = $"{source.PixelWidth} × {source.PixelHeight} px";
        SetTool(App.Settings.RememberAnnotationTool ? App.Settings.LastAnnotationTool : "rect");
        Closed += (_, _) => AnnotationCanvas.ReleaseMouseCapture();
    }

    internal void SetTool(string tool)
    {
        CommitText();
        _tool = new[] { "select", "rect", "ellipse", "arrow", "pencil", "text", "redact" }.Contains(tool) ? tool : "rect";
        App.Settings.LastAnnotationTool = _tool;
        foreach (Button button in ToolButtons.Children)
            button.SetResourceReference(BackgroundProperty, (string)button.Tag == _tool ? "AccentSoftBrush" : "PanelBrush");
        AnnotationCanvas.Cursor = _tool == "select" ? Cursors.Arrow : _tool == "text" ? Cursors.IBeam : Cursors.Cross;
        ToolStatus.Text = _tool switch
        {
            "select" => "选择标注后拖动 · Delete 删除", "text" => "点击输入文字 · 点击空白处完成",
            "redact" => "拖动绘制实色遮挡", "arrow" => "拖动绘制箭头", "ellipse" => "拖动绘制椭圆",
            "pencil" => "按住鼠标绘制", _ => "拖动绘制矩形"
        };
    }
    private void Tool_Click(object sender, RoutedEventArgs e) => SetTool((string)((Button)sender).Tag);
    private Point Clamp(Point point) => new(Math.Clamp(point.X, 0, AnnotationCanvas.Width), Math.Clamp(point.Y, 0, AnnotationCanvas.Height));
    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        BeginStroke(e.GetPosition(AnnotationCanvas));
        if (_draft is not null || _moving) AnnotationCanvas.CaptureMouse();
        e.Handled = true;
    }

    internal void BeginStroke(Point point)
    {
        CommitText();
        _start = Clamp(point);
        if (_tool == "select")
        {
            var hit = VisualTreeHelper.HitTest(AnnotationCanvas, _start)?.VisualHit;
            while (hit is not null && VisualTreeHelper.GetParent(hit) != AnnotationCanvas) hit = VisualTreeHelper.GetParent(hit);
            _selected = hit as FrameworkElement;
            if (_selected is null) return;
            _moveOrigin = _selected.RenderTransform is TranslateTransform translate ? new Point(translate.X, translate.Y) : new Point();
            _moving = true;
            return;
        }
        if (_tool == "text")
        {
            _textEditor = new TextBox { Text = "", FontSize = 22, Foreground = _pen, Background = Brushes.White,
                BorderBrush = _pen, MinWidth = 80, MaxWidth = Math.Max(80, AnnotationCanvas.Width - _start.X),
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(2) };
            Canvas.SetLeft(_textEditor, Math.Min(_start.X, Math.Max(0, AnnotationCanvas.Width - 80)));
            Canvas.SetTop(_textEditor, Math.Min(_start.Y, Math.Max(0, AnnotationCanvas.Height - 32)));
            AnnotationCanvas.Children.Add(_textEditor);
            _textEditor.Focus();
            return;
        }
        _draft = _tool switch
        {
            "rect" => new Rectangle { Fill = Brushes.Transparent },
            "ellipse" => new Ellipse { Fill = Brushes.Transparent },
            "arrow" => new PathShape { Fill = _pen },
            "pencil" => new Polyline { Points = new PointCollection { _start }, StrokeLineJoin = PenLineJoin.Round },
            "redact" => new Rectangle { Fill = Brushes.Black },
            _ => null
        };
        if (_draft is null) return;
        _draft.Stroke = _tool == "redact" ? Brushes.Black : _pen;
        _draft.StrokeThickness = _strokeWidth;
        _draft.StrokeStartLineCap = _draft.StrokeEndLineCap = PenLineCap.Round;
        AnnotationCanvas.Children.Add(_draft);
        ContinueStroke(_start);
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draft is not null || _moving) ContinueStroke(e.GetPosition(AnnotationCanvas));
    }

    internal void ContinueStroke(Point point)
    {
        var end = Clamp(point);
        if (_moving && _selected is not null)
        {
            var bounds = VisualTreeHelper.GetDescendantBounds(_selected);
            if (!bounds.IsEmpty)
            {
                var left = Canvas.GetLeft(_selected); if (double.IsNaN(left)) left = 0;
                var top = Canvas.GetTop(_selected); if (double.IsNaN(top)) top = 0;
                var dx = Math.Clamp(_moveOrigin.X + end.X - _start.X, -left - bounds.Left, Math.Max(-left - bounds.Left, AnnotationCanvas.Width - left - bounds.Right));
                var dy = Math.Clamp(_moveOrigin.Y + end.Y - _start.Y, -top - bounds.Top, Math.Max(-top - bounds.Top, AnnotationCanvas.Height - top - bounds.Bottom));
                _selected.RenderTransform = new TranslateTransform(dx, dy);
            }
            return;
        }
        if (_draft is null) return;
        if (_draft is PathShape arrow)
        {
            var vector = end - _start;
            if (vector.Length < 1) return;
            vector.Normalize();
            var normal = new Vector(-vector.Y, vector.X);
            var headLength = Math.Min(18, (end - _start).Length / 2);
            var figure = new PathFigure { StartPoint = end, IsClosed = true, IsFilled = true };
            figure.Segments.Add(new LineSegment(end - vector * headLength + normal * headLength * .48, true));
            figure.Segments.Add(new LineSegment(end - vector * headLength - normal * headLength * .48, true));
            var geometry = new GeometryGroup();
            geometry.Children.Add(new LineGeometry(_start, end - vector * headLength * .5));
            geometry.Children.Add(new PathGeometry(new[] { figure }));
            arrow.Data = geometry;
        }
        else if (_draft is Polyline pencil) pencil.Points.Add(end);
        else
        {
            Canvas.SetLeft(_draft, Math.Min(_start.X, end.X));
            Canvas.SetTop(_draft, Math.Min(_start.Y, end.Y));
            _draft.Width = Math.Abs(end.X - _start.X);
            _draft.Height = Math.Abs(end.Y - _start.Y);
        }
    }
    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_draft is null && !_moving) return;
        ContinueStroke(e.GetPosition(AnnotationCanvas));
        EndStroke();
        AnnotationCanvas.ReleaseMouseCapture();
        e.Handled = true;
    }
    private void Canvas_LostCapture(object sender, MouseEventArgs e) => EndStroke();

    internal void EndStroke()
    {
        if (_draft is not null)
        {
            var element = _draft;
            _draft = null;
            Record(new Edit(() => AnnotationCanvas.Children.Remove(element), () => AnnotationCanvas.Children.Add(element)));
        }
        if (_moving && _selected is not null)
        {
            var element = _selected;
            var before = new TranslateTransform(_moveOrigin.X, _moveOrigin.Y);
            var after = element.RenderTransform.Clone();
            Record(new Edit(() => element.RenderTransform = before, () => element.RenderTransform = after));
        }
        _moving = false;
    }

    internal void CommitText()
    {
        if (_textEditor is null) return;
        var editor = _textEditor;
        _textEditor = null;
        AnnotationCanvas.Children.Remove(editor);
        if (string.IsNullOrWhiteSpace(editor.Text)) return;
        var text = new TextBlock { Text = editor.Text, FontSize = editor.FontSize, Foreground = _pen,
            TextWrapping = TextWrapping.Wrap, MaxWidth = editor.MaxWidth, Padding = new Thickness(2), Background = Brushes.Transparent };
        Canvas.SetLeft(text, Canvas.GetLeft(editor));
        Canvas.SetTop(text, Canvas.GetTop(editor));
        AnnotationCanvas.Children.Add(text);
        Record(new Edit(() => AnnotationCanvas.Children.Remove(text), () => AnnotationCanvas.Children.Add(text)));
    }

    private void Record(Edit edit) { _undo.Push(edit); _redo.Clear(); UpdateActions(); }
    private void UpdateActions() { UndoButton.IsEnabled = _undo.Count > 0; RedoButton.IsEnabled = _redo.Count > 0; }
    internal void Undo() { CommitText(); if (_undo.TryPop(out var edit)) { edit.Undo(); _redo.Push(edit); _selected = null; UpdateActions(); } }
    internal void Redo() { if (_redo.TryPop(out var edit)) { edit.Redo(); _undo.Push(edit); _selected = null; UpdateActions(); } }
    private void Undo_Click(object s, RoutedEventArgs e) => Undo();
    private void Redo_Click(object s, RoutedEventArgs e) => Redo();
    internal BitmapSource RenderDocument()
    {
        CommitText();
        EndStroke();
        CanvasHost.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)CanvasHost.Width, (int)CanvasHost.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(CanvasHost);
        bitmap.Freeze();
        return bitmap;
    }
    private void Copy_Click(object s, RoutedEventArgs e) => RunAction(() => { Clipboard.SetImage(RenderDocument()); ToolStatus.Text = "已复制图像"; });
    private void Pin_Click(object s, RoutedEventArgs e) => RunAction(() => new PinWindow(RenderDocument()).Show());
    private void Save_Click(object s, RoutedEventArgs e) => RunAction(() =>
    {
        var jpeg = App.Settings.SaveFormat == "JPEG";
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG 图像|*.png|JPEG 图像|*.jpg", FilterIndex = jpeg ? 2 : 1,
            DefaultExt = jpeg ? ".jpg" : ".png", AddExtension = true,
            InitialDirectory = Directory.Exists(App.Settings.DefaultSaveFolder) ? App.Settings.DefaultSaveFolder : "",
            FileName = $"CaptureDesk_{DateTime.Now:yyyyMMdd_HHmmss}"
        };
        if (dialog.ShowDialog(this) != true) return;
        PngCodec.Save(RenderDocument(), dialog.FileName, App.Settings.ImageQuality);
        ToolStatus.Text = $"已保存：{dialog.FileName}";
    });
    private void RunAction(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void Editor_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.Z) Undo();
            else if (e.Key == Key.Y) Redo();
            else if (e.Key == Key.C) Copy_Click(this, e);
            else if (e.Key == Key.S) Save_Click(this, e);
            else return;
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && _selected is not null)
        {
            var element = _selected;
            var index = AnnotationCanvas.Children.IndexOf(element);
            if (index < 0) return;
            AnnotationCanvas.Children.Remove(element);
            Record(new Edit(() => AnnotationCanvas.Children.Insert(index, element), () => AnnotationCanvas.Children.Remove(element)));
            _selected = null;
            e.Handled = true;
        }
    }
}
