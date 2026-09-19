using CaptureDesk.Core;

namespace CaptureDesk.Tests;

public class ScrollMatcherTests
{
    private static byte[] Page(int height)
    {
        var data = new byte[80 * height * 4];
        new Random(42).NextBytes(data);
        return data;
    }
    [Theory]
    [InlineData(35)]
    [InlineData(-35)]
    [InlineData(0)]
    [InlineData(110)]
    public void DetectsSignedScroll(int delta)
    {
        var page = Page(600);
        var a = page.AsSpan(150 * 80 * 4, 160 * 80 * 4).ToArray();
        var b = page.AsSpan((150 + delta) * 80 * 4, a.Length).ToArray();
        var result = ScrollMatcher.Match(a, b, 80, 160);
        Assert.True(result.Matched);
        Assert.Equal(delta, result.Offset);
    }
    [Fact]
    public void RejectsUnrelatedContent()
    {
        var page = Page(600);
        Assert.False(ScrollMatcher.Match(page[..(160 * 80 * 4)], page[(300 * 80 * 4)..(460 * 80 * 4)], 80, 160).Matched);
    }
    [Fact]
    public void RejectsMismatchedDimensions() => Assert.Throws<ArgumentException>(() => ScrollMatcher.Match([], [], 80, 160));
}
