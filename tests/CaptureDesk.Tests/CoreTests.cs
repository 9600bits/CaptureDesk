using CaptureDesk.Core;

namespace CaptureDesk.Tests;

public sealed class CoreTests
{
    [Fact]
    public void CaptureRegion_RejectsNonPositiveArea()
    {
        Assert.True(new CaptureRegion(0, 0, 0, 10).IsEmpty);
        Assert.False(new CaptureRegion(-1920, 0, 800, 600).IsEmpty);
    }

    [Fact]
    public void HistoryStore_KeepsNewestWithinLimit()
    {
        var store = new MemoryHistoryStore(2);
        store.Add(Result(1)); store.Add(Result(2)); store.Add(Result(3));
        Assert.Equal(2, store.GetRecent().Count);
        Assert.Equal(3, store.GetRecent()[0].Width);
    }

    [Fact]
    public void JsonConfigStore_RoundTripsSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"capture-desk-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonConfigStore(path);
            store.Save(new AppSettings { DarkMode = true, ThemeMode = "深色", PinOpacity = 75, MouseZoomStep = 5, AnnotationColor = "钴蓝", HistoryLimit = 12, DefaultSaveFolder = "C:\\Shots" });
            var loaded = store.Load();
            Assert.True(loaded.DarkMode);
            Assert.Equal(12, loaded.HistoryLimit);
            Assert.Equal("深色", loaded.ThemeMode);
            Assert.Equal(75, loaded.PinOpacity);
            Assert.Equal(5, loaded.MouseZoomStep);
            Assert.Equal("钴蓝", loaded.AnnotationColor);
            Assert.Equal("C:\\Shots", loaded.DefaultSaveFolder);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void FeatureState_DoesNotContainMembershipGate()
    {
        var values = Enum.GetNames<FeatureAvailability>();
        Assert.DoesNotContain(values, value => value.Contains("Member", StringComparison.OrdinalIgnoreCase) || value.Contains("License", StringComparison.OrdinalIgnoreCase));
    }

    private static CaptureResult Result(int width) => new([1, 2, 3], width, 10, new CaptureRegion(0, 0, width, 10));

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void DragMapsToPhysicalPixelsAcrossDpi(double scale)
    {
        var region = CaptureGeometry.FromDrag(20, 30, 120, 90, 1920 / scale, 1080 / scale, new CaptureRegion(-1920, -100, 1920, 1080));
        Assert.Equal(-1920 + (int)(20 * scale), region.X);
        Assert.Equal(-100 + (int)(30 * scale), region.Y);
        Assert.Equal((int)(100 * scale), region.Width);
        Assert.Equal((int)(60 * scale), region.Height);
    }

    [Fact]
    public void ReverseDragClampsToDesktop()
    {
        var region = CaptureGeometry.FromDrag(1200, 800, -40, -20, 1000, 600, new CaptureRegion(-1000, 0, 2000, 1200));
        Assert.Equal(new CaptureRegion(-1000, 0, 2000, 1200), region);
    }

    [Fact]
    public void FractionalViewHeightDoesNotAddAnExtraPixel()
    {
        const double height = 506.66666666666663;
        var region = CaptureGeometry.FromDrag(160, height * .25, 560, height * .75, 800, height, new CaptureRegion(-1200, 0, 1200, 760));
        Assert.Equal(new CaptureRegion(-960, 190, 600, 380), region);
    }

    [Fact]
    public void LoweringHistoryLimitPrunesExistingItems()
    {
        var store = new MemoryHistoryStore(20);
        for (var i = 0; i < 8; i++) store.Add(Result(i + 1));
        store.SetLimit(2);
        Assert.Equal(new[] { 8, 7 }, store.GetRecent().Select(x => x.Width));
    }
}
