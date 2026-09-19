using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace CaptureDesk.App;

/// <summary>Renders Lucide geometry in its original 24-unit view box, preserving optical size.</summary>
public sealed class LucideIcon : FrameworkElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(nameof(Data), typeof(Geometry), typeof(LucideIcon), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(LucideIcon), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    public Geometry? Data { get => (Geometry?)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public LucideIcon() { Width = Height = 20; IsHitTestVisible = false; }
    protected override void OnRender(DrawingContext drawing)
    {
        if (Data is null) return;
        var scale = Math.Min(ActualWidth, ActualHeight) / 24;
        drawing.PushTransform(new TranslateTransform((ActualWidth - 24 * scale) / 2, (ActualHeight - 24 * scale) / 2));
        drawing.PushTransform(new ScaleTransform(scale, scale));
        var pen = new Pen(Foreground, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        drawing.DrawGeometry(null, pen, Data);
        drawing.Pop();
        drawing.Pop();
    }
}
