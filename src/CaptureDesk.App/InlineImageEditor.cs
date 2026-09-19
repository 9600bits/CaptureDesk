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

internal sealed class InlineImageEditor
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
    private double _strokeWidth;
    private System.Windows.Media.Brush _pen;
    private TextBlock? _editingText;
    private int _textIndex;
    private BitmapSource? _mosaicSource;
    internal string CurrentTool => _tool;
    internal double StrokeWidth => _strokeWidth;
    internal Color PenColor => ((SolidColorBrush)_pen).Color;
    internal void SetPen(Color color, double width) { CommitText(); EndStroke(); _pen = new SolidColorBrush(color); _strokeWidth = Math.Clamp(width, 1, 24); }


    public Canvas AnnotationCanvas { get; }
    public Grid CanvasHost { get; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public event Action? Changed;
    public InlineImageEditor(BitmapSource source)
    {
        AnnotationCanvas = new Canvas { Width = source.PixelWidth, Height = source.PixelHeight, Background = Brushes.Transparent, ClipToBounds = true };
        CanvasHost = new Grid { Width = source.PixelWidth, Height = source.PixelHeight };
        CanvasHost.Children.Add(new Image { Source = source, Stretch = Stretch.Fill, IsHitTestVisible = false });
        CanvasHost.Children.Add(AnnotationCanvas);
        AnnotationCanvas.MouseLeftButtonDown += Canvas_MouseDown;
        AnnotationCanvas.MouseMove += Canvas_MouseMove;
        AnnotationCanvas.MouseLeftButtonUp += Canvas_MouseUp;
        AnnotationCanvas.LostMouseCapture += Canvas_LostCapture;
        AnnotationCanvas.PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.FocusedElement is TextBox)
            {
                if (e.Key == Key.Escape) { CommitText(); AnnotationCanvas.Focus(); e.Handled = true; }
                return;
            }
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Z or Key.Y)
            { if (e.Key == Key.Z) Undo(); else Redo(); e.Handled = true; }
            else if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; }
        };
        AnnotationCanvas.Focusable = true;
        _strokeWidth = App.Settings.AnnotationWidth;
        _pen = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Settings.AnnotationColor switch
        {
            "朱红" => "#C4493D", "钴蓝" => "#2F6FED", "炭黑" => "#202620", _ => "#2F6B4B"
        }));
    }
    internal void SetTool(string tool)
    {
        CommitText();
        EndStroke();
        _tool = tool;
        AnnotationCanvas.Cursor = tool == "select" ? Cursors.Arrow : tool == "text" ? Cursors.IBeam : Cursors.Cross;
    }
    private Point Clamp(Point point) => new(Math.Clamp(point.X, 0, AnnotationCanvas.Width), Math.Clamp(point.Y, 0, AnnotationCanvas.Height));
    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject target)
        {
            while (target is not null && target != AnnotationCanvas)
            {
                if (target is TextBox) return;
                target = VisualTreeHelper.GetParent(target);
            }
        }
        AnnotationCanvas.Focus();
        if (e.ClickCount == 2 && _tool == "select")
        {
            var hit = HitElement(e.GetPosition(AnnotationCanvas));
            if (hit is TextBlock text) { EndStroke(); EditText(text); e.Handled = true; return; }
        }
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
            _selected = HitElement(_start);
            if (_selected is null) return;
            _moveOrigin = _selected.RenderTransform is TranslateTransform translate ? new Point(translate.X, translate.Y) : new Point();
            _moving = true;
            return;
        }
        if (_tool == "number")
        {
            var number = AnnotationCanvas.Children.OfType<Border>().Where(x => x.Tag as string == "number")
                .Select(x => int.Parse(((TextBlock)x.Child).Text)).DefaultIfEmpty(0).Max() + 1;
            var marker = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = _pen, Tag = "number",
                Child = new TextBlock { Text = number.ToString(), Foreground = Brushes.White, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            Canvas.SetLeft(marker, Math.Clamp(_start.X - 16, 0, Math.Max(0, AnnotationCanvas.Width - 32)));
            Canvas.SetTop(marker, Math.Clamp(_start.Y - 16, 0, Math.Max(0, AnnotationCanvas.Height - 32)));
            AnnotationCanvas.Children.Add(marker);
            Record(new Edit(() => AnnotationCanvas.Children.Remove(marker), () => AnnotationCanvas.Children.Add(marker)));
            return;
        }
        if (_tool == "mosaic") _mosaicSource = RenderDocument();
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
            "mosaic" => new Rectangle { Fill = Brushes.Transparent },
            "line" => new Line(),
            "highlight" => new Polyline { Points = new PointCollection { _start }, StrokeLineJoin = PenLineJoin.Round, Opacity = .35 },
            _ => null
        };
        if (_draft is null) return;
        _draft.Stroke = _tool == "redact" ? Brushes.Black : _pen;
        _draft.StrokeThickness = _strokeWidth;
        if (_tool == "highlight") _draft.StrokeThickness = Math.Max(12, _strokeWidth * 5);
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
        if (!_moving && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            var delta = end - _start;
            if (_tool is "line" or "arrow")
            {
                var angle = Math.Round(Math.Atan2(delta.Y, delta.X) / (Math.PI / 4)) * Math.PI / 4;
                end = Clamp(_start + new Vector(Math.Cos(angle), Math.Sin(angle)) * delta.Length);
            }
            else if (_tool is "rect" or "ellipse")
            {
                var side = Math.Min(Math.Abs(delta.X), Math.Abs(delta.Y));
                end = _start + new Vector(Math.Sign(delta.X) * side, Math.Sign(delta.Y) * side);
            }
        }
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
        if (_draft is Line line) { line.X1 = _start.X; line.Y1 = _start.Y; line.X2 = end.X; line.Y2 = end.Y; }
        else if (_draft is PathShape arrow)
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
            FrameworkElement element = _draft;
            _draft = null;
            if (_tool == "mosaic" && _mosaicSource is not null)
            {
                AnnotationCanvas.Children.Remove(element);
                var x = (int)Canvas.GetLeft(element); var y = (int)Canvas.GetTop(element);
                var width = Math.Min((int)element.Width, _mosaicSource.PixelWidth - x);
                var height = Math.Min((int)element.Height, _mosaicSource.PixelHeight - y);
                if (width < 1 || height < 1) { _mosaicSource = null; return; }
                var cropped = new CroppedBitmap(_mosaicSource, new Int32Rect(x, y, width, height));
                var block = Math.Max(8, (int)_strokeWidth * 4);
                var small = new TransformedBitmap(cropped, new ScaleTransform(Math.Max(1, width / block) / (double)width, Math.Max(1, height / block) / (double)height));
                small.Freeze();
                element = new Image { Source = small, Width = width, Height = height, Stretch = Stretch.Fill, Tag = "mosaic" };
                RenderOptions.SetBitmapScalingMode(element, BitmapScalingMode.NearestNeighbor);
                Canvas.SetLeft(element, x); Canvas.SetTop(element, y);
                AnnotationCanvas.Children.Add(element); _mosaicSource = null;
            }
            Record(new Edit(() => AnnotationCanvas.Children.Remove(element), () => AnnotationCanvas.Children.Add(element)));
        }
        if (_moving && _selected is not null)
        {
            var element = _selected;
            var before = new TranslateTransform(_moveOrigin.X, _moveOrigin.Y);
            var after = element.RenderTransform.Clone();
            if (before.Value != after.Value) Record(new Edit(() => element.RenderTransform = before, () => element.RenderTransform = after));
        }
        _moving = false;
    }

    internal void CommitText()
    {
        if (_textEditor is null) return;
        var editor = _textEditor;
        _textEditor = null;
        AnnotationCanvas.Children.Remove(editor);
        if (_editingText is not null)
        {
            var existing = _editingText; _editingText = null;
            var before = existing.Text; var after = editor.Text;
            existing.Text = after;
            AnnotationCanvas.Children.Insert(Math.Min(_textIndex, AnnotationCanvas.Children.Count), existing);
            if (before != after) Record(new Edit(() => existing.Text = before, () => existing.Text = after));
            return;
        }
        if (string.IsNullOrWhiteSpace(editor.Text)) return;
        var text = new TextBlock { Text = editor.Text, FontSize = editor.FontSize, Foreground = editor.Foreground,
            TextWrapping = TextWrapping.Wrap, MaxWidth = editor.MaxWidth, Padding = new Thickness(2), Background = Brushes.Transparent };
        Canvas.SetLeft(text, Canvas.GetLeft(editor));
        Canvas.SetTop(text, Canvas.GetTop(editor));
        AnnotationCanvas.Children.Add(text);
        Record(new Edit(() => AnnotationCanvas.Children.Remove(text), () => AnnotationCanvas.Children.Add(text)));
    }
    private FrameworkElement? HitElement(Point point)
    {
        var hit = VisualTreeHelper.HitTest(AnnotationCanvas, point)?.VisualHit;
        if (hit == AnnotationCanvas) return null;
        while (hit is not null && VisualTreeHelper.GetParent(hit) != AnnotationCanvas) hit = VisualTreeHelper.GetParent(hit);
        return hit as FrameworkElement;
    }
    internal void EditText(TextBlock text)
    {
        CommitText();
        if (!AnnotationCanvas.Children.Contains(text)) return;
        _editingText = text; _textIndex = AnnotationCanvas.Children.IndexOf(text);
        _textEditor = new TextBox { Text = text.Text, FontSize = text.FontSize, Foreground = text.Foreground, Background = Brushes.White,
            MinWidth = 80, MaxWidth = text.MaxWidth, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(2), RenderTransform = text.RenderTransform.Clone() };
        Canvas.SetLeft(_textEditor, Canvas.GetLeft(text)); Canvas.SetTop(_textEditor, Canvas.GetTop(text));
        AnnotationCanvas.Children.Remove(text); AnnotationCanvas.Children.Insert(_textIndex, _textEditor);
        _textEditor.Focus(); _textEditor.SelectAll();
    }

    private void Record(Edit edit) { _undo.Push(edit); _redo.Clear(); UpdateActions(); }
    private void UpdateActions() => Changed?.Invoke();
    internal void Undo() { CommitText(); EndStroke(); if (_undo.TryPop(out var edit)) { edit.Undo(); _redo.Push(edit); _selected = null; UpdateActions(); } }
    internal void Redo() { CommitText(); EndStroke(); if (_redo.TryPop(out var edit)) { edit.Redo(); _undo.Push(edit); _selected = null; UpdateActions(); } }
    private void Undo_Click(object s, RoutedEventArgs e) => Undo();
    private void Redo_Click(object s, RoutedEventArgs e) => Redo();
    internal BitmapSource RenderDocument()
    {
        CommitText();
        EndStroke();
        CanvasHost.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)CanvasHost.Width, (int)CanvasHost.Height, 96, 96, PixelFormats.Pbgra32);
        // Draw through a visual brush in pixel coordinates, independent of the on-screen viewbox.
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(CanvasHost) { Stretch = Stretch.Fill }, null, new Rect(0, 0, CanvasHost.Width, CanvasHost.Height));
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    internal void DeleteSelected()
    {
        if (_selected is null) return;
        var element = _selected;
        var index = AnnotationCanvas.Children.IndexOf(element);
        if (index < 0) return;
        AnnotationCanvas.Children.Remove(element);
        Record(new Edit(() => AnnotationCanvas.Children.Insert(index, element), () => AnnotationCanvas.Children.Remove(element)));
        _selected = null;
    }
}
