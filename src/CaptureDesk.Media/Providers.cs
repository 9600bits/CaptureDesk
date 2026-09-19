using CaptureDesk.Core;
using System.IO;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace CaptureDesk.Media;

public sealed class LocalRecognitionProvider : IRecognitionProvider
{
    public sealed record Word(string Text, double X, double Y, double Width, double Height, int Line = 0);
    public sealed record Layout(FeatureAvailability Status, IReadOnlyList<Word> Words, string? Detail = null, string Text = "");
    public static IReadOnlyList<(string Tag, string Name)> Languages =>
        OcrEngine.AvailableRecognizerLanguages.Select(x => (x.LanguageTag, x.DisplayName)).ToArray();

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
            var engine = OcrEngine.TryCreateFromLanguage(new Language(request.Language));
            if (engine is null) return new(FeatureAvailability.MissingModel, [], "未安装此语言的 Windows OCR 语言包。请在 Windows 设置 → 时间和语言 → 语言和区域中添加语言及文字识别组件。");
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(request.PngBytes);
                await writer.StoreAsync().AsTask(operationToken);
            }
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(operationToken);
            var scale = Math.Min(1d, OcrEngine.MaxImageDimension / (double)Math.Max(decoder.PixelWidth, decoder.PixelHeight));
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                new BitmapTransform { ScaledWidth = Math.Max(1, (uint)(decoder.PixelWidth * scale)), ScaledHeight = Math.Max(1, (uint)(decoder.PixelHeight * scale)) },
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(operationToken);
            var result = await engine.RecognizeAsync(bitmap).AsTask(operationToken);
            return new(FeatureAvailability.Available, result.Lines.SelectMany((line, index) => line.Words.Select(x => new Word(x.Text,
                x.BoundingRect.X / scale, x.BoundingRect.Y / scale, x.BoundingRect.Width / scale, x.BoundingRect.Height / scale, index))).ToArray(), Text: string.Join(Environment.NewLine, result.Lines.Select(x => x.Text)));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(FeatureAvailability.Failed, [], "识别超时，请缩小图片范围后重试。"); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new(FeatureAvailability.Failed, [], ex.Message); }
    }
}

public sealed class ConfigurableTranslationProvider : ITranslationProvider
{
    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new TranslationResult(FeatureAvailability.NotConfigured, string.Empty, request.SourceLanguage, "未配置翻译服务。可在设置中添加本地或外部翻译 Provider。"));
}
