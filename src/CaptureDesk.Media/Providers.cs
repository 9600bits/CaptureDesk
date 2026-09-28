using CaptureDesk.Core;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace CaptureDesk.Media;

public sealed class LocalRecognitionProvider : IRecognitionProvider
{
    public sealed record Word(string Text, double X, double Y, double Width, double Height, int Line = 0);
    public sealed record Layout(FeatureAvailability Status, IReadOnlyList<Word> Words, string? Detail = null, string Text = "");
    public static IReadOnlyList<(string Tag, string Name)> Languages
    {
        get
        {
            var available = OcrEngine.AvailableRecognizerLanguages
                .Select(x => (Tag: x.LanguageTag, Name: x.DisplayName))
                .ToList();
            // Keep English visible even when its optional Windows OCR language
            // pack is not installed yet, so the user can select it and get a
            // precise installation message instead of assuming it is unsupported.
            if (!available.Any(x => x.Tag.StartsWith("en", StringComparison.OrdinalIgnoreCase)))
                available.Add(("en-US", "English (United States) · 需安装语言包"));
            return available
                .OrderBy(x => x.Tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? 0 :
                    x.Tag.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
    }

    public async Task<RecognitionResult> RecognizeAsync(RecognitionRequest request, CancellationToken cancellationToken = default)
    {
        var result = await RecognizeLayoutAsync(request, cancellationToken);
        return new(result.Status, result.Text, result.Detail);
    }
    public async Task<Layout> RecognizeLayoutAsync(RecognitionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var operationToken = timeout.Token;
        try
        {
            var engineLanguage = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(x =>
                string.Equals(x.LanguageTag, request.Language, StringComparison.OrdinalIgnoreCase)) ??
                OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(x =>
                    x.LanguageTag.StartsWith(request.Language.Split('-')[0], StringComparison.OrdinalIgnoreCase));
            var engine = engineLanguage is null ? null : OcrEngine.TryCreateFromLanguage(engineLanguage);
            if (engine is null) return new(FeatureAvailability.MissingModel, [], "未安装此语言的 Windows OCR 语言包。请在 Windows 设置 → 时间和语言 → 语言和区域中添加语言及文字识别组件。");
            var prepared = PrepareImage(request.PngBytes);
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(prepared.PngBytes);
                await writer.StoreAsync().AsTask(operationToken);
            }
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream).AsTask(operationToken);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(operationToken);
            var result = await engine.RecognizeAsync(bitmap).AsTask(operationToken);
            return new(FeatureAvailability.Available, result.Lines.SelectMany((line, index) => line.Words.Select(x => new Word(x.Text,
                x.BoundingRect.X / prepared.Scale, x.BoundingRect.Y / prepared.Scale, x.BoundingRect.Width / prepared.Scale, x.BoundingRect.Height / prepared.Scale, index))).ToArray(), Text: string.Join(Environment.NewLine, result.Lines.Select(x => x.Text)));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(FeatureAvailability.Failed, [], "识别超时，请缩小图片范围后重试。"); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new(FeatureAvailability.Failed, [], ex.Message); }
    }

    private static (byte[] PngBytes, double Scale) PrepareImage(byte[] source)
    {
        using var input = new MemoryStream(source, writable: false);
        var decoder = new System.Windows.Media.Imaging.PngBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var maxDimension = Math.Max(frame.PixelWidth, frame.PixelHeight);
        var scale = Math.Min(2d, OcrEngine.MaxImageDimension / (double)Math.Max(1, maxDimension));
        var width = Math.Max(1, (int)Math.Round(frame.PixelWidth * scale));
        var height = Math.Max(1, (int)Math.Round(frame.PixelHeight * scale));
        var resized = new TransformedBitmap(frame, new ScaleTransform(width / (double)frame.PixelWidth, height / (double)frame.PixelHeight));
        resized.Freeze();
        var bgra = new FormatConvertedBitmap(resized, PixelFormats.Bgra32, null, 0);
        bgra.Freeze();
        var pixels = new byte[width * height * 4];
        bgra.CopyPixels(pixels, width * 4, 0);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var luminance = (0.2126 * pixels[i + 2]) + (0.7152 * pixels[i + 1]) + (0.0722 * pixels[i]);
            // Normalize the common light-gray screenshot text/background range.
            var enhanced = Math.Clamp((luminance - 24) * 255 / 210, 0, 255);
            var value = (byte)Math.Round(enhanced);
            pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
            pixels[i + 3] = 255;
        }
        var enhancedBitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        enhancedBitmap.Freeze();
        using var output = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(enhancedBitmap));
        encoder.Save(output);
        return (output.ToArray(), scale);
    }
}

public sealed class ConfigurableTranslationProvider : ITranslationProvider
{
    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new TranslationResult(FeatureAvailability.NotConfigured, string.Empty, request.SourceLanguage, "未配置翻译服务。可在设置中添加本地或外部翻译 Provider。"));
}
