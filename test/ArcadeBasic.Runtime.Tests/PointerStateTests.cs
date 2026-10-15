using ArcadeBasic.Runtime;

namespace ArcadeBasic.Runtime.Tests;

/// <summary>
/// <see cref="PointerState"/> (GET MOUSE's states, LOCATE POINT's picks) and
/// <see cref="GraphicsState.ToProblem"/> (surface point → problem coordinates).
/// </summary>
public class PointerStateTests
{
    /// <summary>A drawing surface with a scripted pointer.</summary>
    private sealed class Surface : IGraphicsDevice, IPointer
    {
        public readonly Queue<PointerEvent> Events = new();
        public GfxPoint? Position;

        public void Press(double x, double y) => Events.Enqueue(new PointerEvent(true, new GfxPoint(x, y)));
        public void Release(double x, double y) => Events.Enqueue(new PointerEvent(false, new GfxPoint(x, y)));

        public bool TryGetPosition(out GfxPoint position)
        {
            position = Position ?? default;
            return Position is not null;
        }

        public bool TryReadEvent(out PointerEvent e) => Events.TryDequeue(out e);

        public void Clear() { }
        public void SetLineStyle(int style) { }
        public void SetPointStyle(int style) { }
        public void SetColor(GfxColorTarget target, int colorIndex) { }
        public void DrawPoints(IReadOnlyList<GfxPoint> points) { }
        public void DrawLines(IReadOnlyList<GfxPoint> polyline) { }
        public void FillArea(IReadOnlyList<GfxPoint> polygon) { }
        public void DrawText(GfxPoint at, string text) { }
        public void Flush() { }
        public int MaxColor => 15;
        public int MaxLineStyle => 3;
        public int MaxPointStyle => 3;
        public GfxDeviceSize DeviceSize => new(1, 1, "OTHER");
    }

    [Fact]
    public void WithoutAPointingDeviceTheStateIsMinusOne()
    {
        var pointer = new PointerState(NullGraphicsDevice.Instance);
        Assert.False(pointer.Present);
        Assert.Equal(PointerState.NoPointer, pointer.Sample(out _));
    }

    [Fact]
    public void SamplesReportEachEdgeOnceThenTheHeldState()
    {
        var surface = new Surface { Position = new GfxPoint(0.1, 0.1) };
        var pointer = new PointerState(surface);
        Assert.Equal(PointerState.Up, pointer.Sample(out var at));
        Assert.Equal(new GfxPoint(0.1, 0.1), at);

        surface.Press(0.2, 0.3);
        Assert.Equal(PointerState.Pressed, pointer.Sample(out at));
        Assert.Equal(new GfxPoint(0.2, 0.3), at);                 // where it was pressed

        surface.Position = new GfxPoint(0.5, 0.5);                 // dragging
        Assert.Equal(PointerState.Held, pointer.Sample(out at));
        Assert.Equal(new GfxPoint(0.5, 0.5), at);

        surface.Release(0.6, 0.6);
        Assert.Equal(PointerState.Released, pointer.Sample(out at));
        Assert.Equal(new GfxPoint(0.6, 0.6), at);
        Assert.Equal(PointerState.Up, pointer.Sample(out _));
    }

    [Fact]
    public void AClickBetweenTwoSamplesIsStillSeen()
    {
        var surface = new Surface();
        var pointer = new PointerState(surface);
        surface.Press(0.4, 0.4);
        surface.Release(0.4, 0.4);
        Assert.Equal(PointerState.Pressed, pointer.Sample(out _));
        Assert.Equal(PointerState.Released, pointer.Sample(out _));
        Assert.Equal(PointerState.Up, pointer.Sample(out _));
    }

    [Fact]
    public void LocateIgnoresEarlierClicksAndPointsItDoesNotAccept()
    {
        var surface = new Surface();
        var pointer = new PointerState(surface);
        surface.Press(0.9, 0.9);                                   // before the request: not an answer
        surface.Release(0.9, 0.9);
        Assert.False(pointer.TryLocate(_ => true, out _));        // the request begins, nothing new yet

        surface.Press(0.9, 0.1);                                   // outside what's accepted
        surface.Release(0.9, 0.1);
        surface.Press(0.2, 0.2);
        Assert.True(pointer.TryLocate(p => p.X < 0.5, out var at));
        Assert.Equal(new GfxPoint(0.2, 0.2), at);
    }

    [Fact]
    public void SurfacePointsMapBackToProblemCoordinates()
    {
        var gfx = new GraphicsState();
        gfx.SetWindow(0, 100, 0, 50);
        Assert.Equal(new GfxPoint(25, 25), gfx.ToProblem(new GfxPoint(0.25, 0.5)));

        gfx.SetViewport(0.5, 1, 0, 1);                             // the window fills the right half
        Assert.Equal(new GfxPoint(50, 10), gfx.ToProblem(new GfxPoint(0.75, 0.2)));
        Assert.False(gfx.InWindow(gfx.ToProblem(new GfxPoint(0.25, 0.2))));   // the left half is outside

        gfx.SetWindow(100, 0, 50, 0);                              // flipped axes run backwards too
        gfx.SetViewport(0, 1, 0, 1);
        Assert.Equal(new GfxPoint(75, 25), gfx.ToProblem(new GfxPoint(0.25, 0.5)));
    }
}
