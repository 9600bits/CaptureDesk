using System.Data;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureDesk.Core;
using CaptureDesk.Native;

namespace CaptureDesk.Media;

public sealed record TableResult(FeatureAvailability Status, DataTable Table, string? Detail = null, double[]? Columns = null, double[]? Rows = null, bool Ruled = false);

public static class TableRecognition
{
    public static async Task<TableResult> RecognizeAsync(byte[] png, string language, CancellationToken token = default)
    {
        var source = PngCodec.Decode(png);
        var layout = await new LocalRecognitionProvider().RecognizeLayoutAsync(new RecognitionRequest(png, language), token);
        if (layout.Status != FeatureAvailability.Available) return new(layout.Status, new DataTable(), layout.Detail);
        var english = LocalRecognitionProvider.Languages.FirstOrDefault(x => x.Tag.StartsWith("en", StringComparison.OrdinalIgnoreCase)).Tag;
        if (!language.StartsWith("en", StringComparison.OrdinalIgnoreCase) && english is not null)
        {
            var latin = await new LocalRecognitionProvider().RecognizeLayoutAsync(new RecognitionRequest(png, english), token);
            if (latin.Status == FeatureAvailability.Available)
            {
                var combined = layout.Words.ToList();
                foreach (var word in latin.Words)
                    if (!combined.Any(w => new Rect(w.X, w.Y, w.Width, w.Height).Contains(new Point(word.X + word.Width / 2, word.Y + word.Height / 2)))) combined.Add(word);
                layout = layout with { Words = combined };
            }
        }
        var result = await Task.Run(() => Build(source, layout.Words, token), token);
        if (result.Status == FeatureAvailability.Available && !result.Ruled)
        {
            // Sparse tables may be ignored by Windows OCR; add inferred rules only to the recognition input.
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
                foreach (var x in result.Columns!) dc.DrawLine(new Pen(Brushes.Black, 2), new Point(Math.Clamp(x, 1, source.PixelWidth - 1), 1), new Point(Math.Clamp(x, 1, source.PixelWidth - 1), source.PixelHeight - 1));
                foreach (var y in result.Rows!) dc.DrawLine(new Pen(Brushes.Black, 2), new Point(1, Math.Clamp(y, 1, source.PixelHeight - 1)), new Point(source.PixelWidth - 1, Math.Clamp(y, 1, source.PixelHeight - 1)));
            }
            var guided = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32); guided.Render(visual); guided.Freeze();
            var refined = await new LocalRecognitionProvider().RecognizeLayoutAsync(new RecognitionRequest(PngCodec.Encode(guided), language), token);
            if (refined.Status == FeatureAvailability.Available && refined.Words.Count > layout.Words.Count)
                result = await Task.Run(() => Build(source, refined.Words, token), token);
        }
        if (result.Status == FeatureAvailability.Available)
        {
            var repaired = 0;
            for (var row = 0; row < result.Table.Rows.Count; row++)
                for (var col = 0; col < result.Table.Columns.Count; col++)
                {
                    if (!string.IsNullOrWhiteSpace(result.Table.Rows[row][col].ToString()) || repaired >= 100) continue;
                    token.ThrowIfCancellationRequested(); repaired++;
                    var x = (int)result.Columns![col] + 3; var y = (int)result.Rows![row] + 3;
                    var w = Math.Min(source.PixelWidth - x, (int)(result.Columns[col + 1] - x) - 3);
                    var h = Math.Min(source.PixelHeight - y, (int)(result.Rows[row + 1] - y) - 3);
                    if (w < 2 || h < 2) continue;
                    var crop = new CroppedBitmap(source, new Int32Rect(x, y, w, h));
                    var scale = Math.Min(2d, 1800d / Math.Max(w, h));
                    var visual = new DrawingVisual();
                    using (var dc = visual.RenderOpen())
                    {
                        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w * scale + 40, h * scale + 40));
                        dc.DrawImage(crop, new Rect(20, 20, w * scale, h * scale));
                    }
                    var cellImage = new RenderTargetBitmap((int)(w * scale + 40), (int)(h * scale + 40), 96, 96, PixelFormats.Pbgra32); cellImage.Render(visual); cellImage.Freeze();
                    var cell = await new LocalRecognitionProvider().RecognizeAsync(new RecognitionRequest(PngCodec.Encode(cellImage), language), token);
                    if (cell.Status == FeatureAvailability.Available && string.IsNullOrWhiteSpace(cell.Text))
                    {
                        var originalCell = await new LocalRecognitionProvider().RecognizeAsync(new RecognitionRequest(PngCodec.Encode(crop), language), token);
                        if (!string.IsNullOrWhiteSpace(originalCell.Text)) cell = originalCell;
                    }
                    if (cell.Status == FeatureAvailability.Available && string.IsNullOrWhiteSpace(cell.Text))
                    {
                        var repeated = new DrawingVisual();
                        using (var dc = repeated.RenderOpen())
                            for (var i = 0; i < 3; i++) dc.DrawImage(cellImage, new Rect(0, i * cellImage.PixelHeight, cellImage.PixelWidth, cellImage.PixelHeight));
                        var sheet = new RenderTargetBitmap(cellImage.PixelWidth, cellImage.PixelHeight * 3, 96, 96, PixelFormats.Pbgra32); sheet.Render(repeated); sheet.Freeze();
                        var retry = await new LocalRecognitionProvider().RecognizeAsync(new RecognitionRequest(PngCodec.Encode(sheet), language), token);
                        if (retry.Status == FeatureAvailability.Available) cell = retry with { Text = retry.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "" };
                    }
                    if (cell.Status == FeatureAvailability.Available) result.Table.Rows[row][col] = cell.Text;
                }
        }
        return result;
    }
    public static TableResult Build(BitmapSource source, IReadOnlyList<LocalRecognitionProvider.Word> words, CancellationToken token = default)
    {
        var image = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = image.PixelWidth; var height = image.PixelHeight;
        var pixels = new byte[checked(width * height * 4)]; image.CopyPixels(pixels, width * 4, 0);
        bool Dark(int x, int y) { var i = (y * width + x) * 4; return pixels[i] + pixels[i + 1] + pixels[i + 2] < 420 && pixels[i + 3] > 128; }
        List<double> Lines(bool vertical)
        {
            var extent = vertical ? width : height; var cross = vertical ? height : width;
            var positions = new List<double>(); var run = -1;
            for (var i = 0; i < extent; i++)
            {
                token.ThrowIfCancellationRequested(); var dark = 0; var total = 0;
                for (var j = 0; j < cross; j += Math.Max(1, cross / 800)) { if (vertical ? Dark(i, j) : Dark(j, i)) dark++; total++; }
                if (dark > total * .65) { if (run < 0) run = i; }
                else if (run >= 0) { positions.Add((run + i - 1) / 2d); run = -1; }
            }
            if (run >= 0) positions.Add((run + extent - 1) / 2d);
            return positions;
        }
        var xs = Lines(true); var ys = Lines(false);
        var ruled = xs.Count >= 3 && ys.Count >= 3;
        if (!ruled)
        {
            if (words.Count == 0) return new(FeatureAvailability.Failed, new DataTable(), "未检测到表格文字或网格，请框选清晰的表格区域。");
            var typical = words.Select(w => w.Height).Order().ElementAt(words.Count / 2);
            var rows = new List<List<LocalRecognitionProvider.Word>>();
            foreach (var word in words.OrderBy(w => w.Y + w.Height / 2))
            {
                var row = rows.LastOrDefault();
                if (row is null || Math.Abs(row.Average(w => w.Y + w.Height / 2) - word.Y - word.Height / 2) > typical * .65) rows.Add([word]);
                else row.Add(word);
            }
            var occupied = new bool[width];
            for (var x = 0; x < width; x++)
                for (var y = 0; y < height; y++) if (Dark(x, y)) { occupied[x] = true; break; }
            xs = [0]; var start = -1;
            for (var x = 0; x < width; x++)
            {
                if (!occupied[x]) { if (start < 0) start = x; }
                else if (start >= 0) { if (start > 0 && x - start >= typical * 1.3) xs.Add((start + x) / 2d); start = -1; }
            }
            xs.Add(width);
            ys = [0];
            for (var i = 1; i < rows.Count; i++) ys.Add((rows[i - 1].Max(w => w.Y + w.Height) + rows[i].Min(w => w.Y)) / 2);
            ys.Add(height);
        }
        if (xs.Count < 3 || ys.Count < 3) return new(FeatureAvailability.Failed, new DataTable(), "未检测到至少两行两列的表格，请排除周围文字后重试。");
        if (xs.Count > 101 || ys.Count > 501) return new(FeatureAvailability.Failed, new DataTable(), "表格过大，请分段识别（最多 500 行、100 列）。");
        var table = new DataTable();
        for (var x = 1; x < xs.Count; x++) table.Columns.Add($"列{x}", typeof(string));
        for (var y = 1; y < ys.Count; y++) table.Rows.Add(table.NewRow());
        for (var y = 0; y < table.Rows.Count; y++)
            for (var x = 0; x < table.Columns.Count; x++)
            {
                token.ThrowIfCancellationRequested();
                var cell = words.Where(w => w.X + w.Width / 2 >= xs[x] && w.X + w.Width / 2 < xs[x + 1] && w.Y + w.Height / 2 >= ys[y] && w.Y + w.Height / 2 < ys[y + 1]).OrderBy(w => w.Y).ThenBy(w => w.X).ToList();
                var lines = new List<List<LocalRecognitionProvider.Word>>();
                foreach (var word in cell)
                {
                    var line = lines.LastOrDefault();
                    if (line is null || Math.Abs(line[0].Y - word.Y) > Math.Max(line[0].Height, word.Height) * .6) lines.Add([word]); else line.Add(word);
                }
                table.Rows[y][x] = string.Join(Environment.NewLine, lines.Select(line =>
                {
                    var ordered = line.OrderBy(w => w.X).ToArray(); var text = "";
                    for (var i = 0; i < ordered.Length; i++)
                    {
                        var w = ordered[i];
                        if (i > 0 && w.X - ordered[i - 1].X - ordered[i - 1].Width > w.Height * .4) text += " ";
                        text += w.Text;
                    }
                    return text;
                }));
            }
        return new(FeatureAvailability.Available, table, ruled ? "已按网格分列，请核对识别结果" : "已按文字间距分列，请核对列边界", xs.ToArray(), ys.ToArray(), ruled);
    }
}
