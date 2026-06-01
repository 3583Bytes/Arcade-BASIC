using ArcadeBasic.Runtime;

namespace ArcadeBasic.Runtime.Tests;

public class RasterizerTests
{
    [Fact]
    public void HorizontalLinePlotsEveryPixelInclusive()
    {
        var pts = new List<(int, int)>();
        Rasterizer.Line(0, 0, 5, 0, (x, y) => pts.Add((x, y)));
        Assert.Equal(6, pts.Count);
        Assert.Contains((0, 0), pts);
        Assert.Contains((5, 0), pts);
    }

    [Fact]
    public void PerfectDiagonalIsOnePixelPerStep()
    {
        var pts = new List<(int, int)>();
        Rasterizer.Line(0, 0, 3, 3, (x, y) => pts.Add((x, y)));
        Assert.Equal(new[] { (0, 0), (1, 1), (2, 2), (3, 3) }, pts);
    }

    [Fact]
    public void LineIsDrawableInReverseToo()
    {
        var pts = new List<(int, int)>();
        Rasterizer.Line(5, 2, 0, 2, (x, y) => pts.Add((x, y)));
        Assert.Contains((0, 2), pts);
        Assert.Contains((5, 2), pts);
    }

    [Fact]
    public void FillPolygonCoversInteriorAndEdges()
    {
        var fill = new HashSet<(int, int)>();
        // 5×5 square.
        Rasterizer.FillPolygon(new[] { (0, 0), (4, 0), (4, 4), (0, 4) }, (x, y) => fill.Add((x, y)));
        Assert.Contains((2, 2), fill);   // interior
        Assert.Contains((0, 0), fill);   // corner (edge)
        Assert.Contains((4, 4), fill);
        Assert.DoesNotContain((5, 5), fill);
    }

    [Fact]
    public void PolylineConnectsConsecutivePoints()
    {
        var pts = new List<(int, int)>();
        Rasterizer.Polyline(new[] { (0, 0), (2, 0), (2, 2) }, (x, y) => pts.Add((x, y)));
        Assert.Contains((1, 0), pts);   // along first segment
        Assert.Contains((2, 1), pts);   // along second segment
    }
}
