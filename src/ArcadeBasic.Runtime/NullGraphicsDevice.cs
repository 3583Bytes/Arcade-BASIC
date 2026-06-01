namespace ArcadeBasic.Runtime;

/// <summary>
/// A no-op <see cref="IGraphicsDevice"/> used when a program runs without a real
/// rendering backend (e.g. <c>arcade-basic run foo.bas</c> with no <c>--svg</c>).
/// Graphic-output statements execute and update state but draw nothing; ASK
/// queries return benign capability defaults.
/// </summary>
public sealed class NullGraphicsDevice : IGraphicsDevice
{
    public static readonly NullGraphicsDevice Instance = new();

    public int MaxColor => 15;
    public int MaxLineStyle => 3;
    public int MaxPointStyle => 3;
    public GfxDeviceSize DeviceSize => new(0, 0, "OTHER");

    public void Clear() { }
    public void SetLineStyle(int style) { }
    public void SetPointStyle(int style) { }
    public void SetColor(GfxColorTarget target, int colorIndex) { }
    public void DrawPoints(IReadOnlyList<GfxPoint> points) { }
    public void DrawLines(IReadOnlyList<GfxPoint> polyline) { }
    public void FillArea(IReadOnlyList<GfxPoint> polygon) { }
    public void DrawText(GfxPoint at, string text) { }
    public void Flush() { }
}
