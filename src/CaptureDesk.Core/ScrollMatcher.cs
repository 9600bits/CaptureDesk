namespace CaptureDesk.Core;

public readonly record struct ScrollMatch(bool Matched, int Offset, double Error);

/// <summary>Finds vertical motion between equal-sized BGRA frames. Positive offsets scroll down.</summary>
public static class ScrollMatcher
{
    public static ScrollMatch Match(byte[] previous, byte[] next, int width, int height)
    {
        if (width < 8 || height < 32 || previous.Length != checked(width * height * 4) || next.Length != previous.Length)
            throw new ArgumentException("滚动截图尺寸不匹配，或区域过小。");
        var limit = height * 3 / 4;
        double Score(int offset)
        {
            var first = Math.Max(0, offset);
            var last = Math.Min(height, height + offset);
            double sum = 0;
            var count = 0;
            for (var y = first + 2; y < last - 2; y += Math.Max(1, (last - first) / 40))
                for (var x = width / 10; x < width * 9 / 10; x += Math.Max(1, width / 64))
                {
                    var a = (y * width + x) * 4;
                    var b = ((y - offset) * width + x) * 4;
                    for (var c = 0; c < 3; c++) { sum += Math.Abs(previous[a + c] - next[b + c]); count++; }
                }
            return count == 0 ? 255 : sum / count;
        }
        var stationary = Score(0);
        if (stationary < .35) return new(true, 0, stationary);
        var scores = new List<(int Offset, double Error)>();
        for (var offset = -limit; offset <= limit; offset++) scores.Add((offset, Score(offset)));
        var best = scores.MinBy(x => x.Error);
        var alternative = scores.Where(x => Math.Abs(x.Offset - best.Offset) > 3).Min(x => x.Error);
        // Reject uncertain/repeating content instead of silently corrupting a long image.
        return new(best.Error < 5 && alternative > best.Error + 1.2, best.Offset, best.Error);
    }
}
