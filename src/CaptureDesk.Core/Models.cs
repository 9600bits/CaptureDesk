namespace CaptureDesk.Core;

public enum CaptureMode { Screen, Freeform, Polygon, MultiRegion, Window, LongCapture }
public enum AnnotationTool { Select, Rectangle, Ellipse, Arrow, Line, Pencil, Highlighter, Text, Mosaic, Blur, Eraser, Spotlight, Magnifier, Watermark, Number }
public enum FeatureAvailability { Available, MissingModel, NotConfigured, Busy, Failed }
public enum PinKind { Image, AnimatedImage, Text, Html, File, Color, Latex }

public readonly record struct CaptureRegion(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public sealed record CaptureRequest(CaptureMode Mode, CaptureRegion? Region = null, bool IncludeCursor = false, int DelayMilliseconds = 0);
public sealed record CaptureResult(byte[] PngBytes, int Width, int Height, CaptureRegion Region);
public sealed record RecognitionRequest(byte[] PngBytes, string Language = "zh-Hans", bool FastModel = false);
public sealed record RecognitionResult(FeatureAvailability Status, string Text, string? Detail = null);
public sealed record TranslationRequest(string Text, string TargetLanguage = "简体中文", string? SourceLanguage = null);
public sealed record TranslationResult(FeatureAvailability Status, string Text, string? SourceLanguage = null, string? Detail = null);
public sealed record PinItem(Guid Id, PinKind Kind, string Title, string? SourcePath = null, bool IsLocked = false, bool IsTopmost = true);
public sealed record PinGroup(Guid Id, string Name, string Color, IReadOnlyList<PinItem> Items);

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; }
    public bool DarkMode { get; set; }
    public string AccentColor { get; set; } = "#2F6B4B";
    public string CloseBehavior { get; set; } = "最小化到托盘";
    public string ThemeMode { get; set; } = "浅色";
    public int HistoryLimit { get; set; } = 50;
    public int RecordingFrameRate { get; set; } = 30;
    public string DefaultSaveFolder { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    public string SaveFormat { get; set; } = "PNG";
    public int ImageQuality { get; set; } = 95;
    public bool ShowCursor { get; set; }
    public bool ShowGuides { get; set; } = true;
    public int CaptureBorderWidth { get; set; } = 2;
    public int PinOpacity { get; set; } = 100;
    public bool PinAlwaysOnTop { get; set; } = true;
    public bool PinShadow { get; set; } = true;
    public string AnnotationColor { get; set; } = "森林绿";
    public int AnnotationWidth { get; set; } = 3;
    public bool RememberAnnotationTool { get; set; } = true;
    public string LastAnnotationTool { get; set; } = "rect";
    public string MouseWheelAction { get; set; } = "缩放贴图";
    public int MouseZoomStep { get; set; } = 10;
    public int MouseOpacityStep { get; set; } = 5;
}

public interface ICaptureService { Task<CaptureResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken = default); }
public interface IAnnotationRenderer { byte[] Render(byte[] sourcePng, IReadOnlyList<object> annotations); }
public interface IPinWindowManager { void ShowImage(byte[] pngBytes, string title = "贴图"); void CloseAll(); }
public interface IRecordingService
{
    Task StartAsync(CaptureRegion region, CancellationToken cancellationToken = default);
    Task PauseAsync();
    Task StopAsync(string outputPath, CancellationToken cancellationToken = default);
}
public interface IRecognitionProvider { Task<RecognitionResult> RecognizeAsync(RecognitionRequest request, CancellationToken cancellationToken = default); }
public interface ITranslationProvider { Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default); }
public interface IExportService { Task ExportAsync(byte[] pngBytes, string outputPath, CancellationToken cancellationToken = default); }
public interface IHotkeyService { void Register(string gesture, Action callback); void UnregisterAll(); }
public interface IConfigStore { AppSettings Load(); void Save(AppSettings settings); }
public interface IHistoryStore { void Add(CaptureResult result); IReadOnlyList<CaptureResult> GetRecent(); }
