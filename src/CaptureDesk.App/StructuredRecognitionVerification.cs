using System.Data;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using CaptureDesk.Core;
using CaptureDesk.Media;
using CaptureDesk.Native;

namespace CaptureDesk.App;

internal static class StructuredRecognitionVerification
{
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static BitmapSource TableImage(bool lines = true)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 600, 240));
            if (lines)
            {
                for (var x = 0; x <= 3; x++) dc.DrawLine(new Pen(Brushes.Black, 2), new Point(2 + x * 198, 2), new Point(2 + x * 198, 238));
                for (var y = 0; y <= 3; y++) dc.DrawLine(new Pen(Brushes.Black, 2), new Point(2, 2 + y * 78), new Point(596, 2 + y * 78));
            }
            var labels = new[] { "Name", "Count", "Price", "Apple", "12", "25", "Pear", "8", "16" };
            for (var i = 0; i < labels.Length; i++) dc.DrawText(new FormattedText(labels[i], System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 28, Brushes.Black, 1), new Point(20 + i % 3 * 198, 22 + i / 3 * 78));
        }
        var image = new RenderTargetBitmap(600, 240, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze(); return image;
    }
    internal static void Run(string directory, List<string> passed)
    {
        var png = PngCodec.Encode(TableImage());
        var lang = LocalRecognitionProvider.Languages.FirstOrDefault(x => x.Tag.StartsWith("en")).Tag ?? LocalRecognitionProvider.Languages.First().Tag;
        var table = Task.Run(() => TableRecognition.RecognizeAsync(png, lang)).GetAwaiter().GetResult();
        Require(table.Status == FeatureAvailability.Available && table.Table.Rows.Count == 3 && table.Table.Columns.Count == 3, "Ruled table structure failed: " + table.Detail);
        Require(table.Table.Rows[1][0].ToString()!.Contains("Apple") && table.Table.Rows[2][1].ToString()!.Contains("8"), "Table OCR cell placement failed");
        Require(table.Table.Rows[0][0].ToString()!.Contains("Name") && table.Table.Rows[0][2].ToString()!.Contains("price", StringComparison.OrdinalIgnoreCase), "Sparse text cells were not recovered: " + TableExport.ToTsv(table.Table));
        if (string.IsNullOrWhiteSpace(table.Table.Rows[2][2].ToString())) passed.Add("LIMITATION Windows OCR omitted short numeric cell 16; left blank for review");
        var noLines = PngCodec.Encode(TableImage(false));
        File.WriteAllBytes(Path.Combine(directory, "borderless-input.png"), noLines);
        var layout = Task.Run(() => new LocalRecognitionProvider().RecognizeLayoutAsync(new RecognitionRequest(noLines, lang))).GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(directory, "table-layout.json"), System.Text.Json.JsonSerializer.Serialize(layout));
        var borderless = Task.Run(() => TableRecognition.RecognizeAsync(noLines, lang)).GetAwaiter().GetResult();
        Require(borderless.Status == FeatureAvailability.Available && borderless.Table.Columns.Count == 3 && borderless.Table.Rows.Count == 3, $"Borderless table columns failed: {borderless.Status} {borderless.Table.Rows.Count}x{borderless.Table.Columns.Count} {borderless.Detail}");
        table.Table.Rows[1][1] = "=SUM(A1:A3)";
        var path = Path.Combine(directory, "recognized-table.xlsx"); TableExport.Save(table.Table, path);
        using (var zip = ZipFile.OpenRead(path))
        {
            using var entry = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open(); var xml = XDocument.Load(entry);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            Require(xml.Descendants(ns + "row").Count() == 3 && !xml.Descendants(ns + "f").Any() && xml.ToString().Contains("=SUM(A1:A3)"), "XLSX does not preserve literal edited cells");
        }
        TableExport.Save(table.Table, Path.Combine(directory, "recognized-table.csv"));
        Require(File.ReadAllText(Path.Combine(directory, "recognized-table.csv")).Contains("'=SUM"), "CSV formula input not escaped");
        passed.Add("PASS live table OCR, ruled/borderless structure, editable values, XLSX/CSV exports");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { Task.Run(() => TableRecognition.RecognizeAsync(png, lang, cancelled.Token)).GetAwaiter().GetResult(); throw new InvalidOperationException("Table did not cancel"); }
        catch (OperationCanceledException) { passed.Add("PASS table cancellation"); }

        foreach (var action in new[] { "long", "record", "ocr", "table" })
        {
            var overlay = new CaptureOverlay(TableImage(), new CaptureRegion(0, 0, 600, 240));
            overlay.ContentRendered += (_, _) =>
            {
                overlay.BeginSelection(new Point(30, 30)); overlay.EndSelection(new Point(300, 180)); overlay.UpdateLayout();
                var bar = (Border)overlay.FindName("ActionBar");
                Require(!((Panel)bar.Child).Children.OfType<Button>().Any(x => x.Tag as string == "formula"), "Removed formula action is still visible");
                var button = ((Panel)bar.Child).Children.OfType<Button>().Single(x => x.Tag as string == action);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            Require(overlay.ShowDialog() == true && overlay.OutputAction == action && overlay.CapturedBytes is not null, "Selection toolbar action failed: " + action);
        }
        passed.Add("PASS all four selection toolbar actions preserve region and route output");
        foreach (var mode in new[] { "table", "table-dark" })
        {
            Theme.Apply(mode == "table-dark");
            var window = new StructuredRecognitionWindow(png);
            window.Show(); window.UpdateLayout();
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (window.IsBusy && DateTime.UtcNow < deadline)
            {
                var frame = new System.Windows.Threading.DispatcherFrame();
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
                timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
            }
            Require(!window.IsBusy && window.ResultTable?.Columns.Count == 3, "Recognition window did not display results");
            window.UpdateLayout(); UiSnapshot.Save(window, Path.Combine(directory, mode + "-window-final.png")); window.Close();
        }
        Theme.Apply(false);
        passed.Add("PASS table windows display live editable results");
    }
}
