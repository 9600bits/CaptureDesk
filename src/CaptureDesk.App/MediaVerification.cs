using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureDesk.Core;
using CaptureDesk.Media;
using CaptureDesk.Native;

namespace CaptureDesk.App;

internal static class MediaVerification
{
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static void Run(string directory, List<string> passed)
    {
        var width = 100; var height = 600;
        var pixels = new byte[width * height * 4]; new Random(19).NextBytes(pixels);
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var page = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); page.Freeze();
        BitmapSource Slice(int y) { var b = new CroppedBitmap(page, new Int32Rect(0, y, width, 200)); b.Freeze(); return b; }
        var doc = new ScrollDocument(Slice(100));
        Require(doc.Append(Slice(180)).Matched && doc.Height == 280, "Scroll append failed");
        Require(doc.Append(Slice(140)).Matched && doc.Height == 280, "Reverse scroll duplicated rows");
        Require(doc.Append(Slice(40)).Matched && doc.Height == 340, "Prepending scroll failed");
        var actual = new byte[width * 340 * 4]; doc.Render().CopyPixels(actual, width * 4, 0);
        Require(actual.SequenceEqual(pixels.AsSpan(40 * width * 4, actual.Length).ToArray()), "Stitched pixels changed");
        passed.Add("PASS bidirectional scroll stitching, duplicate trimming, original pixels");

        var frames = Path.Combine(directory, "media-test-frames"); Directory.CreateDirectory(frames);
        var first = Path.Combine(frames, "first.png"); var second = Path.Combine(frames, "second.png");
        PngCodec.Save(Slice(0), first); PngCodec.Save(Slice(200), second);
        using var gif = new MemoryStream();
        GifStreamEncoder.Write(gif, new[] { (first, 10), (second, 25) }); gif.Position = 0;
        var decoded = new GifBitmapDecoder(gif, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Require(decoded.Frames.Count == 2 && decoded.Frames[0].PixelWidth == 100 && decoded.Frames[1].PixelHeight == 200, "GIF frame stream invalid");
        Require((ushort)((BitmapMetadata)decoded.Frames[1].Metadata).GetQuery("/grctlext/Delay") == 25, "GIF timing lost");
        File.WriteAllBytes(Path.Combine(directory, "recording-test.gif"), gif.ToArray());
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { GifStreamEncoder.Write(new MemoryStream(), new[] { (first, 10) }, cancellationToken: cancelled.Token); throw new InvalidOperationException("Export did not cancel"); }
        catch (OperationCanceledException) { }
        passed.Add("PASS streamed GIF decoding, frame dimensions, timing and cancellation");

        var bounds = NativeCaptureService.GetVirtualScreenRegion();
        Task.Run(async () =>
        {
            using var recorder = new LocalRecordingService { FrameRate = 5 };
            await recorder.StartAsync(new CaptureRegion(bounds.X, bounds.Y, 160, 100));
            await Task.Delay(500); await recorder.PauseAsync();
            var paused = recorder.Elapsed;
            await Task.Delay(200); Require((recorder.Elapsed - paused).TotalMilliseconds < 20, "Paused recording timer advanced");
            await recorder.PauseAsync(); await Task.Delay(300); await recorder.FinishAsync();
            Require(recorder.Failure is null && !recorder.IsRecording, "Screen recording failed: " + recorder.Failure);
            var path = Path.Combine(directory, "live-recording-test.gif");
            await recorder.ExportAsync(path, null);
            using var recovery = LocalRecordingService.Recover(Path.Combine(recorder.RecoveryDirectory!, "frames.tsv"));
            await recovery.ExportAsync(Path.Combine(directory, "recovered-recording-test.gif"), null, start: .1, end: .4);
            // Diagnostic owns only this generated session.
            Directory.Delete(recorder.RecoveryDirectory!, true);
        }).GetAwaiter().GetResult();
        passed.Add("PASS real desktop GIF recording, pause/resume, recovery and interval export");

        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 900, 150));
            drawing.DrawText(new FormattedText("CaptureDesk 12345", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 52, Brushes.Black, 1), new Point(24, 36));
        }
        var image = new RenderTargetBitmap(900, 150, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze();
        var png = PngCodec.Encode(image);
        var languages = LocalRecognitionProvider.Languages;
        if (languages.Count > 0)
        {
            var language = languages.FirstOrDefault(x => x.Tag.StartsWith("en"));
            if (language == default) language = languages[0];
            var result = Task.Run(() => new LocalRecognitionProvider().RecognizeAsync(new RecognitionRequest(png, language.Tag))).GetAwaiter().GetResult();
            Require(result.Status == FeatureAvailability.Available && result.Text.Contains("12345"), "OCR live recognition failed: " + result.Detail + " / " + result.Text);
            passed.Add("PASS Windows OCR recognizes test image: " + result.Text);
        }
        else passed.Add("SKIP OCR success: no Windows language pack installed");
        var available = languages.Select(x => x.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var absent = new[] { "ja-JP", "ko-KR", "ar-SA", "ru-RU" }.FirstOrDefault(x => !available.Contains(x));
        if (absent is not null)
        {
            var result = Task.Run(() => new LocalRecognitionProvider().RecognizeAsync(new RecognitionRequest(png, absent))).GetAwaiter().GetResult();
            Require(result.Status == FeatureAvailability.MissingModel, "OCR missing language not reported");
            passed.Add("PASS OCR missing-language state");
        }
        try { new LocalRecognitionProvider().RecognizeAsync(new RecognitionRequest(png), cancelled.Token).GetAwaiter().GetResult(); throw new InvalidOperationException("OCR did not cancel"); }
        catch (OperationCanceledException) { passed.Add("PASS OCR cancellation"); }
        var recordingWindow = new RecordingWindow(new CaptureRegion(0, 0, 400, 300)); recordingWindow.Show(); recordingWindow.UpdateLayout();
        UiSnapshot.Save(recordingWindow, Path.Combine(directory, "recording-panel.png")); recordingWindow.Close();
        Theme.Apply(true);
        var darkRecording = new RecordingWindow(new CaptureRegion(0, 0, 400, 300)); darkRecording.Show(); darkRecording.UpdateLayout();
        UiSnapshot.Save(darkRecording, Path.Combine(directory, "recording-panel-dark-final.png")); darkRecording.Close();
        Theme.Apply(false);
        var recognitionWindow = new RecognitionWindow(png); recognitionWindow.Show(); recognitionWindow.UpdateLayout();
        UiSnapshot.Save(recognitionWindow, Path.Combine(directory, "recognition-panel.png")); recognitionWindow.Close();
        App.History.Add(new CaptureResult(png, 900, 150, new CaptureRegion(0, 0, 900, 150)));
        var history = new HistoryWindow(); history.Show(); history.UpdateLayout();
        var windowCount = System.Windows.Application.Current.Windows.Count;
        var edit = Descendants(history).OfType<System.Windows.Controls.Button>().First(x => x.Content as string == "编辑");
        edit.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); history.UpdateLayout();
        Require(System.Windows.Application.Current.Windows.Count == windowCount && Descendants(history).OfType<System.Windows.Controls.Viewbox>().Any(), "History edit opened a new window");
        UiSnapshot.Save(history, Path.Combine(directory, "history-inline.png")); history.Close();
        passed.Add("PASS history editing stays in existing window");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
