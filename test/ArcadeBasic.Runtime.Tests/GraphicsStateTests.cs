using ArcadeBasic.Runtime;

namespace ArcadeBasic.Runtime.Tests;

/// <summary>The §13 coordinate/clip core, exercised directly through a
/// <see cref="RecordingGraphicsDevice"/> (no interpreter/VM needed).</summary>
public class GraphicsStateTests
{
    private static string Emit(Action<GraphicsState, IGraphicsDevice> draw)
    {
        var state = new GraphicsState();
        var rec = new RecordingGraphicsDevice();
        draw(state, rec);
        return rec.Transcript;
    }

    [Fact]
    public void WindowMapsToNormalizedDeviceCoordinates()
    {
        var t = Emit((s, d) =>
        {
            s.SetWindow(0, 10, 0, 10);
            s.EmitPoints(new[] { new GfxPoint(5, 5) }, d);
        });
        Assert.Contains("POINTS 0.5000,0.5000", t);
    }

    [Fact]
    public void LineIsClippedToTheViewport()
    {
        var t = Emit((s, d) =>
        {
            s.SetWindow(0, 10, 0, 10);
            s.SetViewport(0, 0.5, 0, 0.5);
            s.EmitLines(new[] { new GfxPoint(-5, 5), new GfxPoint(15, 5) }, d);
        });
        Assert.Contains("LINES 0.0000,0.2500 0.5000,0.2500", t);
    }

    [Fact]
    public void ClipOnDropsGeometryOutsideTheViewport()
    {
        var t = Emit((s, d) =>
        {
            s.SetWindow(0, 10, 0, 10);
            s.SetViewport(0, 0.5, 0, 0.5);     // window 20,20 → NDC (1,1), outside
            s.EmitPoints(new[] { new GfxPoint(20, 20) }, d);
        });
        Assert.DoesNotContain("POINTS", t);
    }

    [Fact]
    public void ClipOffKeepsGeometryWithinTheDeviceWindow()
    {
        var t = Emit((s, d) =>
        {
            s.SetWindow(0, 10, 0, 10);
            s.SetViewport(0, 0.5, 0, 0.5);
            s.ClipEnabled = false;
            s.EmitPoints(new[] { new GfxPoint(20, 20) }, d);   // NDC (1,1) — on device-window edge
        });
        Assert.Contains("POINTS 1.0000,1.0000", t);
    }

    [Fact]
    public void InvalidViewportLeavesCurrentValueUnchanged()
    {
        var t = Emit((s, d) =>
        {
            s.SetWindow(0, 10, 0, 10);
            s.SetViewport(0.5, 0.5, 0, 1);     // zero width — ignored (nonfatal)
            s.EmitPoints(new[] { new GfxPoint(10, 10) }, d);  // still maps with default viewport
        });
        Assert.Contains("POINTS 1.0000,1.0000", t);
    }
}
