using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using CaptureDesk.Native;

namespace CaptureDesk.App;

public partial class PinWindow : Window
{
    private readonly BitmapSource _source;
    internal bool IsLocked { get; private set; }
    private readonly MenuItem _lockMenu;
    public PinWindow(BitmapSource source)
    {
        InitializeComponent();
        _source = source;
        var menu = new ContextMenu { FontFamily = FontFamily };
        _lockMenu = new MenuItem { Header = "锁定位置和大小", IsCheckable = true };
        _lockMenu.Click += (_, _) => SetLocked(_lockMenu.IsChecked);
        menu.Items.Add(_lockMenu);
        void Add(string title, Action action)
        {
            var item = new MenuItem { Header = title }; item.Click += (_, _) => action(); menu.Items.Add(item);
        }
        Add("原始尺寸", () => { if (!IsLocked) { Width = Math.Max(MinWidth, _source.PixelWidth + 14); Height = Math.Max(MinHeight, _source.PixelHeight + 14); } });
        Add("保存图像…", () => WorkflowUi.Try(this, () =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG 图像|*.png|JPEG 图像|*.jpg", FileName = "贴图.png" };
            if (dialog.ShowDialog(this) == true) PngCodec.Save(_source, dialog.FileName, App.Settings.ImageQuality);
        }));
        Add("复制图像", () => Copy_Click(this, new RoutedEventArgs()));
        menu.Items.Add(new Separator());
        Add("关闭贴图", Close);
        ContextMenu = menu;
        MinWidth = 210;
        PinnedImage.Source = source;
        Topmost = App.Settings.PinAlwaysOnTop;
        Opacity = Math.Clamp(App.Settings.PinOpacity / 100d, .1, 1);
        var scale = Math.Min(1, Math.Min(748d / source.PixelWidth, 588d / source.PixelHeight));
        Width = Math.Max(MinWidth, source.PixelWidth * scale + 14);
        Height = Math.Max(100, source.PixelHeight * scale + 14);
        if (App.Settings.PinShadow) Frame.Effect = new DropShadowEffect { Color = Color.FromRgb(20, 35, 24), BlurRadius = 10, ShadowDepth = 2, Opacity = .22 };
        MouseEnter += (_, _) => Toolbar.Opacity = 1;
        MouseLeave += (_, _) => { if (!Toolbar.IsKeyboardFocusWithin) Toolbar.Opacity = 0; };
        GotKeyboardFocus += (_, _) => Toolbar.Opacity = 1;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        MouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || App.Settings.MouseWheelAction == "调整透明度")
                Opacity = Math.Clamp(Opacity + Math.Sign(e.Delta) * App.Settings.MouseOpacityStep / 100d, .1, 1);
            else
            {
                if (IsLocked) { e.Handled = true; return; }
                var scaleStep = Math.Pow(1 + App.Settings.MouseZoomStep / 100d, Math.Sign(e.Delta));
                var width = Math.Clamp(Width * scaleStep, MinWidth, SystemParameters.VirtualScreenWidth);
                var height = Math.Clamp(Height * (width / Width), MinHeight, SystemParameters.VirtualScreenHeight);
                Width = width; Height = height;
            }
            e.Handled = true;
        };
        UpdateTopmostButton();
    }
    internal void SetLocked(bool locked)
    {
        IsLocked = locked; _lockMenu.IsChecked = locked;
        ResizeMode = locked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        Frame.ToolTip = locked ? "已锁定 · 右键解锁" : null;
        Frame.SetResourceReference(Border.BorderBrushProperty, locked ? "AccentBrush" : "LineBrush");
    }
    private void Image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (IsLocked) return; if (e.ClickCount == 2) Close(); else DragMove(); }
    private void Topmost_Click(object sender, RoutedEventArgs e) { Topmost = !Topmost; UpdateTopmostButton(); }
    private void UpdateTopmostButton()
    {
        TopmostButton.SetResourceReference(BackgroundProperty, Topmost ? "AccentSoftBrush" : "PanelBrush");
        TopmostButton.ToolTip = Topmost ? "取消置顶" : "置顶";
    }
    private void Opacity_Click(object sender, RoutedEventArgs e) => Opacity = Opacity > .55 ? .45 : 1;
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetImage(_source); }
        catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show("剪贴板正被使用，请稍后重试。", "复制贴图"); }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
