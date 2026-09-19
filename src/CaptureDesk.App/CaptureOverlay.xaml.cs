using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureDesk.Core;
using CaptureDesk.Native;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace CaptureDesk.App;

public partial class CaptureOverlay : Window
{
    private readonly BitmapSource _desktop;
    private readonly CaptureRegion _desktopRegion;
    private Point _start;
    private Point _end;
    private bool _dragging;
    private bool _movingSelection;
    private Point _moveAnchor;
    private Rect _originalSelection;
    private readonly List<Thumb> _handles = new();
    private bool _showGuides = App.Settings.ShowGuides;
    private readonly bool _selectionOnly;
    public string OutputAction { get; private set; } = "copy";
    internal InlineImageEditor? InlineEditor { get; private set; }
    public BitmapSource? CapturedSource { get; private set; }
    public byte[]? CapturedBytes { get; private set; }
    public CaptureRegion CapturedRegion { get; private set; }

    public CaptureOverlay(BitmapSource desktop, CaptureRegion desktopRegion, bool selectionOnly = false)
    {
        InitializeComponent();
        MoreAnnotations.ContextMenu = AnnotationMenu.Create(() => InlineEditor, SelectInlineTool);
        _selectionOnly = selectionOnly;
        if (selectionOnly)
            foreach (var child in ((Panel)ActionBar.Child).Children.OfType<FrameworkElement>())
                if (child is not Button button || button.Tag is not null || button == InlineUndo || button == InlineRedo)
                    child.Visibility = Visibility.Collapsed;
        AddHandles();
        _desktop = desktop;
        _desktopRegion = desktopRegion;
        DesktopImage.Source = desktop;
        SelectionRect.StrokeThickness = App.Settings.CaptureBorderWidth;
        Loaded += (_, _) =>
        {
            WindowUtilities.PlaceInPhysicalPixels(this, desktopRegion);
            Activate();
            Focus();
            UpdateMask();
        };
        InputSurface.SizeChanged += (_, _) => UpdateMask();
    }

    private void Surface_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ActionBar.IsMouseOver || _handles.Any(x => x.IsMouseOver)) return;
        if (InlineEditor is not null)
        {
            InlineEditor.CommitText();
            e.Handled = true;
            return;
        }
        var point = e.GetPosition(InputSurface);
        if (!CapturedRegion.IsEmpty && new Rect(_start, _end).Contains(point))
        {
            if (e.ClickCount == 2) { ConfirmSelection(); e.Handled = true; return; }
            _movingSelection = _dragging = true;
            _moveAnchor = point;
            _originalSelection = new Rect(_start, _end);
            ActionBar.Visibility = Visibility.Collapsed;
        }
        else BeginSelection(point);
        InputSurface.CaptureMouse();
        e.Handled = true;
    }

    internal void BeginSelection(Point point)
    {
        _movingSelection = false;
        _start = point;
        _dragging = true;
        ActionBar.Visibility = Visibility.Collapsed;
        HintBar.Visibility = Visibility.Collapsed;
        SelectionRect.Visibility = SizeBadge.Visibility = Visibility.Visible;
        UpdateSelection(point);
    }

    private void Surface_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging) UpdateDrag(e.GetPosition(InputSurface));
    }

    private void UpdateDrag(Point point)
    {
        if (!_movingSelection) { UpdateSelection(point); return; }
        var dx = Math.Clamp(point.X - _moveAnchor.X, -_originalSelection.Left, InputSurface.ActualWidth - _originalSelection.Right);
        var dy = Math.Clamp(point.Y - _moveAnchor.Y, -_originalSelection.Top, InputSurface.ActualHeight - _originalSelection.Bottom);
        _start = new Point(_originalSelection.Left + dx, _originalSelection.Top + dy);
        UpdateSelection(new Point(_originalSelection.Right + dx, _originalSelection.Bottom + dy));
    }

    private void Surface_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        UpdateDrag(e.GetPosition(InputSurface));
        EndSelection(_end);
        InputSurface.ReleaseMouseCapture();
        e.Handled = true;
    }

    internal void EndSelection(Point point)
    {
        UpdateSelection(point);
        _dragging = false;
        _movingSelection = false;
        if (CapturedRegion.Width >= 2 && CapturedRegion.Height >= 2)
        {
            ActionBar.Visibility = Visibility.Visible;
            PositionActionBar();
        }
        else
        {
            SelectionRect.Visibility = SizeBadge.Visibility = Visibility.Collapsed;
            HintBar.Visibility = Visibility.Visible;
            UpdateMask();
        }
    }

    private void Surface_LostCapture(object sender, MouseEventArgs e)
    {
        if (_dragging) EndSelection(_end);
    }

    internal void UpdateSelection(Point end)
    {
        _end = new Point(Math.Clamp(end.X, 0, InputSurface.ActualWidth), Math.Clamp(end.Y, 0, InputSurface.ActualHeight));
        var rect = new Rect(_start, _end);
        Canvas.SetLeft(SelectionRect, rect.Left);
        Canvas.SetTop(SelectionRect, rect.Top);
        SelectionRect.Width = rect.Width;
        SelectionRect.Height = rect.Height;
        CapturedRegion = CaptureGeometry.FromDrag(_start.X, _start.Y, _end.X, _end.Y,
            InputSurface.ActualWidth, InputSurface.ActualHeight, _desktopRegion);
        SizeText.Text = $"{CapturedRegion.X}, {CapturedRegion.Y}   {CapturedRegion.Width} × {CapturedRegion.Height} px";
        SizeBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(SizeBadge, Math.Clamp(rect.Left, 0, Math.Max(0, InputSurface.ActualWidth - SizeBadge.DesiredSize.Width)));
        Canvas.SetTop(SizeBadge, rect.Top >= 32 ? rect.Top - 32 : rect.Top + 5);
        UpdateMask();
        UpdateHandles();
    }

    private void UpdateMask()
    {
        if (DimMask is null) return;
        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(new Rect(0, 0, InputSurface.ActualWidth, InputSurface.ActualHeight)));
        var guides = new GeometryGroup();
        if (SelectionRect.Visibility == Visibility.Visible)
        {
            var rect = new Rect(_start, _end);
            geometry.Children.Add(new RectangleGeometry(rect));
            if (_showGuides && InlineEditor is null)
                for (var i = 1; i <= 2; i++)
                {
                    guides.Children.Add(new LineGeometry(new Point(rect.Left + rect.Width * i / 3, rect.Top), new Point(rect.Left + rect.Width * i / 3, rect.Bottom)));
                    guides.Children.Add(new LineGeometry(new Point(rect.Left, rect.Top + rect.Height * i / 3), new Point(rect.Right, rect.Top + rect.Height * i / 3)));
                }
        }
        DimMask.Data = geometry;
        Guides.Data = guides;
    }

    private void PositionActionBar()
    {
        ActionBar.LayoutTransform = Transform.Identity;
        var rect = new Rect(_start, _end);
        var work = WindowUtilities.GetNearestWorkArea(CapturedRegion);
        var scaleX = InputSurface.ActualWidth / _desktopRegion.Width;
        var scaleY = InputSurface.ActualHeight / _desktopRegion.Height;
        var available = new Rect((work.X - _desktopRegion.X) * scaleX, (work.Y - _desktopRegion.Y) * scaleY, work.Width * scaleX, work.Height * scaleY);
        available.Intersect(new Rect(0, 0, InputSurface.ActualWidth, InputSurface.ActualHeight));
        if (available.IsEmpty)
            available = new Rect(0, 0, InputSurface.ActualWidth, InputSurface.ActualHeight);
        ActionBar.MaxWidth = Math.Max(100, Math.Min(760, available.Width - 16));
        ActionBar.Measure(new Size(ActionBar.MaxWidth, double.PositiveInfinity));
        var size = ActionBar.DesiredSize;
        var y = rect.Bottom + 8;
        if (y + size.Height > available.Bottom) y = rect.Top - size.Height - 8;
        Canvas.SetLeft(ActionBar, Math.Clamp(rect.Right - size.Width, available.Left + 8, Math.Max(available.Left + 8, available.Right - size.Width - 8)));
        Canvas.SetTop(ActionBar, Math.Clamp(y, available.Top + 8, Math.Max(available.Top + 8, available.Bottom - size.Height - 8)));
    }

    internal void CreateCapture()
    {
        if (CapturedRegion.Width < 2 || CapturedRegion.Height < 2) return;
        // Use the frame captured before the overlay, excluding all selection UI.
        CapturedSource = InlineEditor is null
            ? NativeCaptureService.Crop(_desktop, CapturedRegion, _desktopRegion)
            : InlineEditor.RenderDocument();
        CapturedBytes = PngCodec.Encode(CapturedSource);
    }

    private void ConfirmSelection()
    {
        CreateCapture();
        if (CapturedSource is null) return;
        if (OutputAction == "save" && !_selectionOnly)
        {
            var jpeg = App.Settings.SaveFormat == "JPEG";
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG 图像|*.png|JPEG 图像|*.jpg", FilterIndex = jpeg ? 2 : 1,
                DefaultExt = jpeg ? ".jpg" : ".png", AddExtension = true,
                InitialDirectory = System.IO.Directory.Exists(App.Settings.DefaultSaveFolder) ? App.Settings.DefaultSaveFolder : "",
                FileName = $"CaptureDesk_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            var wasTopmost = Topmost;
            try
            {
                Topmost = false;
                if (dialog.ShowDialog(this) != true) { OutputAction = "copy"; return; }
                PngCodec.Save(CapturedSource, dialog.FileName, App.Settings.ImageQuality);
                OutputAction = "saved";
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存失败"); OutputAction = "copy"; return; }
            finally { Topmost = wasTopmost; }
        }
        DialogResult = true;
    }
    private void AddHandles()
    {
        foreach (var direction in new[] { "NW", "N", "NE", "E", "SE", "S", "SW", "W" })
        {
            var handle = new Thumb { Width = 18, Height = 18, Tag = direction, Visibility = Visibility.Collapsed,
                Cursor = direction is "N" or "S" ? Cursors.SizeNS : direction is "E" or "W" ? Cursors.SizeWE : direction is "NW" or "SE" ? Cursors.SizeNWSE : Cursors.SizeNESW };
            var template = new ControlTemplate(typeof(Thumb));
            var circle = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));
            circle.SetValue(WidthProperty, 10d);
            circle.SetValue(HeightProperty, 10d);
            circle.SetValue(System.Windows.Shapes.Shape.FillProperty, FindResource("AccentBrush"));
            circle.SetValue(System.Windows.Shapes.Shape.StrokeProperty, Brushes.White);
            circle.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.5d);
            template.VisualTree = circle;
            handle.Template = template;
            System.Windows.Automation.AutomationProperties.SetName(handle, $"调整选区 {direction}");
            handle.DragStarted += (_, _) => { _originalSelection = new Rect(_start, _end); ActionBar.Visibility = Visibility.Collapsed; };
            handle.DragDelta += (_, _) => ResizeSelection(direction, Mouse.GetPosition(InputSurface));
            handle.DragCompleted += (_, _) => EndSelection(_end);
            SelectionCanvas.Children.Add(handle);
            _handles.Add(handle);
        }
    }
    internal void ResizeSelection(string direction, Point position)
    {
        var rect = new Rect(_start, _end);
        var x = Math.Clamp(position.X, 0, InputSurface.ActualWidth);
        var y = Math.Clamp(position.Y, 0, InputSurface.ActualHeight);
        var left = rect.Left; var right = rect.Right; var top = rect.Top; var bottom = rect.Bottom;
        if (direction.Contains('W')) left = Math.Min(x, right - 2);
        if (direction.Contains('E')) right = Math.Max(x, left + 2);
        if (direction.Contains('N')) top = Math.Min(y, bottom - 2);
        if (direction.Contains('S')) bottom = Math.Max(y, top + 2);
        _start = new Point(Math.Max(0, left), Math.Max(0, top));
        UpdateSelection(new Point(right, bottom));
    }
    private void UpdateHandles()
    {
        var rect = new Rect(_start, _end);
        foreach (var handle in _handles)
        {
            var direction = (string)handle.Tag;
            var x = direction.Contains('W') ? rect.Left : direction.Contains('E') ? rect.Right : rect.Left + rect.Width / 2;
            var y = direction.Contains('N') ? rect.Top : direction.Contains('S') ? rect.Bottom : rect.Top + rect.Height / 2;
            Canvas.SetLeft(handle, x - 9);
            Canvas.SetTop(handle, y - 9);
            handle.Visibility = InlineEditor is null && SelectionRect.Visibility == Visibility.Visible && !CapturedRegion.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        }
    }
    private void EditTool_Click(object sender, RoutedEventArgs e)
    {
        SelectInlineTool((string)((Button)sender).Tag);
    }
    private void MoreAnnotations_Click(object sender, RoutedEventArgs e)
    {
        MoreAnnotations.ContextMenu.PlacementTarget = MoreAnnotations;
        MoreAnnotations.ContextMenu.IsOpen = true;
    }
    internal void SelectInlineTool(string tool)
    {
        if (CapturedRegion.IsEmpty) return;
        if (InlineEditor is null)
        {
            InlineEditor = new InlineImageEditor(NativeCaptureService.Crop(_desktop, CapturedRegion, _desktopRegion));
            InlineEditor.Changed += UpdateEditActions;
            InlineEditorHost.Child = InlineEditor.CanvasHost;
            var rect = new Rect(_start, _end);
            InlineEditorHost.Width = rect.Width;
            InlineEditorHost.Height = rect.Height;
            Canvas.SetLeft(InlineEditorHost, rect.Left);
            Canvas.SetTop(InlineEditorHost, rect.Top);
            InlineEditorHost.Visibility = Visibility.Visible;
            UpdateHandles();
            UpdateMask();
        }
        InlineEditor.SetTool(tool);
        MoreAnnotations.SetResourceReference(BackgroundProperty, new[] { "line", "highlight", "number", "mosaic" }.Contains(tool) ? "AccentSoftBrush" : "PanelBrush");
        MoreAnnotations.ToolTip = tool switch { "line" => "直线（Shift 固定角度）", "highlight" => "荧光笔", "number" => "序号", "mosaic" => "马赛克", _ => "更多标注 / 颜色 / 粗细" };
        foreach (var button in ((Panel)ActionBar.Child).Children.OfType<Button>())
            if (button.Tag is string tag && new[] { "select", "rect", "ellipse", "pencil", "arrow", "text", "redact" }.Contains(tag))
                button.SetResourceReference(BackgroundProperty, tag == tool ? "AccentSoftBrush" : "PanelBrush");
        Focus();
    }
    private void UpdateEditActions()
    {
        InlineUndo.IsEnabled = InlineEditor?.CanUndo == true;
        InlineRedo.IsEnabled = InlineEditor?.CanRedo == true;
    }
    private void Undo_Click(object sender, RoutedEventArgs e) => InlineEditor?.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => InlineEditor?.Redo();
    private void Output_Click(object sender, RoutedEventArgs e)
    {
        OutputAction = (string)((Button)sender).Tag;
        ConfirmSelection();
    }
    private void Guides_Click(object sender, RoutedEventArgs e) { _showGuides = !_showGuides; UpdateMask(); }
    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        InlineEditorHost.Child = null;
        InlineEditorHost.Visibility = Visibility.Collapsed;
        InlineEditor = null;
        UpdateEditActions();
        foreach (var button in ((Panel)ActionBar.Child).Children.OfType<Button>())
            button.SetResourceReference(BackgroundProperty, "PanelBrush");
        OutputAction = "copy";
        CapturedRegion = default;
        SelectionRect.Visibility = SizeBadge.Visibility = ActionBar.Visibility = Visibility.Collapsed;
        HintBar.Visibility = Visibility.Visible;
        UpdateHandles(); UpdateMask();
    }
    private void Confirm_Click(object sender, RoutedEventArgs e) { OutputAction = "copy"; ConfirmSelection(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Surface_RightClick(object sender, MouseButtonEventArgs e) => DialogResult = false;
    private void Overlay_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
        {
            if (e.Key == Key.Escape) { InlineEditor?.CommitText(); Focus(); e.Handled = true; }
            return;
        }
        if (InlineEditor is not null && Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Z or Key.Y)
        {
            if (e.Key == Key.Z) InlineEditor.Undo(); else InlineEditor.Redo();
            e.Handled = true; return;
        }
        if (InlineEditor is not null && e.Key == Key.Delete) { InlineEditor.DeleteSelected(); e.Handled = true; return; }
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        else if (e.Key == Key.Enter && !_dragging) { ConfirmSelection(); e.Handled = true; }
        else if (!_dragging && !CapturedRegion.IsEmpty && Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.C or Key.S)
        {
            OutputAction = e.Key == Key.C ? "copy" : "save"; ConfirmSelection(); e.Handled = true;
        }
        else if (InlineEditor is null && !_dragging && !CapturedRegion.IsEmpty && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            var dx = (e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0) * InputSurface.ActualWidth / _desktopRegion.Width;
            var dy = (e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0) * InputSurface.ActualHeight / _desktopRegion.Height;
            var rect = new Rect(_start, _end);
            dx = Math.Clamp(dx, -rect.Left, InputSurface.ActualWidth - rect.Right);
            dy = Math.Clamp(dy, -rect.Top, InputSurface.ActualHeight - rect.Bottom);
            _start = new Point(rect.Left + dx, rect.Top + dy);
            EndSelection(new Point(rect.Right + dx, rect.Bottom + dy));
            e.Handled = true;
        }
    }
}
