using ArcadeBasic.Runtime;

namespace ArcadeBasic.Runtime.Tests;

public class RasterGraphicsDeviceTests
{
    private const int Black = unchecked((int)0xFF000000);
    private const int White = unchecked((int)0xFFFFFFFF);
    private const int BrightBlue = unchecked((int)0xFF5555FF);
    private const int BrightGreen = unchecked((int)0xFF55FF55);

    private static int At(RasterGraphicsDevice d, int x, int y) => d.Pixels[y * d.Width + x];

    [Fact]
    public void StartsClearedToOpaqueBlackAndReportsSize()
    {
        var d = new RasterGraphicsDevice(64, 48);
        Assert.Equal(64, d.Width);
        Assert.Equal(48, d.Height);
        Assert.Equal(64, (int)d.DeviceSize.Width);
        Assert.Equal(48, (int)d.DeviceSize.Height);
        Assert.All(d.Pixels, p => Assert.Equal(Black, p));
    }

    [Fact]
    public void FullWindowLineHitsBothCornersWithYFlip()
    {
        var d = new RasterGraphicsDevice(10, 10);
        d.SetColor(GfxColorTarget.Line, 4);                 // bright blue
        d.DrawLines(new GfxPoint[] { new(0, 0), new(1, 1) });   // NDC corner to corner
        // NDC (0,0) = bottom-left → buffer bottom-left (x=0, y=H-1).
        Assert.Equal(BrightBlue, At(d, 0, 9));
        // NDC (1,1) = top-right → buffer top-right (x=W-1, y=0).
        Assert.Equal(BrightBlue, At(d, 9, 0));
    }

    [Fact]
    public void PointPlotsAtTheMappedPixel()
    {
        var d = new RasterGraphicsDevice(11, 11);
        d.SetColor(GfxColorTarget.Point, 3);                // bright green
        d.DrawPoints(new GfxPoint[] { new(0.5, 0.5) });     // centre
        Assert.Equal(BrightGreen, At(d, 5, 5));
    }

    [Fact]
    public void FilledAreaPaintsManyPixels()
    {
        var d = new RasterGraphicsDevice(20, 20);
        d.SetColor(GfxColorTarget.Area, 2);
        d.FillArea(new GfxPoint[] { new(0.1, 0.1), new(0.9, 0.1), new(0.5, 0.9) });
        var painted = d.Pixels.Count(p => p != Black);
        Assert.True(painted > 30, $"expected a filled triangle to paint many pixels, got {painted}");
    }

    [Fact]
    public void ClearResetsAndResizeReallocates()
    {
        var d = new RasterGraphicsDevice(8, 8);
        d.DrawPoints(new GfxPoint[] { new(0, 1) });
        Assert.Contains(d.Pixels, p => p != Black);
        d.Clear();
        Assert.All(d.Pixels, p => Assert.Equal(Black, p));

        d.Resize(16, 4);
        Assert.Equal(16, d.Width);
        Assert.Equal(4, d.Height);
        Assert.Equal(16 * 4, d.Pixels.Length);
        Assert.All(d.Pixels, p => Assert.Equal(Black, p));
    }

    [Fact]
    public void TextDrawsGlyphPixelsInWhite()
    {
        var d = new RasterGraphicsDevice(40, 12);
        d.SetColor(GfxColorTarget.Text, 1);                 // white
        d.DrawText(new GfxPoint(0, 1), "HI");               // top-left-ish
        var white = d.Pixels.Count(p => p == White);
        Assert.True(white >= 10, $"expected glyph pixels for 'HI', got {white}");
    }
}
