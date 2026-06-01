using ArcadeBasic.Runtime;

namespace ArcadeBasic.Ide;

/// <summary>
/// Terminal graphics backend: maps the core's normalized device coordinates
/// ([0,1], origin bottom-left) onto a <see cref="BrailleCanvas"/> dot grid
/// (Y-flipped to top-left), rasterizing lines and filled areas via the shared
/// <see cref="Rasterizer"/>. Colours map to the 16-colour terminal palette;
/// GRAPH TEXT is drawn as native characters. Plotting happens on the
/// interpreter's background thread — the canvas serialises access.
/// </summary>
internal sealed class TuiGraphicsDevice : IGraphicsDevice
{
    private readonly BrailleCanvas _canvas;
    private int _pointColor = 1, _lineColor = 1, _textColor = 1, _areaColor = 1;

    public TuiGraphicsDevice(BrailleCanvas canvas) => _canvas = canvas;

    public int MaxColor => 15;
    public int MaxLineStyle => 3;
    public int MaxPointStyle => 3;
    public GfxDeviceSize DeviceSize => new(_canvas.PixelWidth, _canvas.PixelHeight, "OTHER");

    public void Clear() => _canvas.ClearBuffer();

    // Line/point styles aren't expressible on a 1-bit braille grid; accepted and ignored.
    public void SetLineStyle(int style) { }
    public void SetPointStyle(int style) { }

    public void SetColor(GfxColorTarget target, int colorIndex)
    {
        switch (target)
        {
            case GfxColorTarget.Point: _pointColor = colorIndex; break;
            case GfxColorTarget.Line: _lineColor = colorIndex; break;
            case GfxColorTarget.Text: _textColor = colorIndex; break;
            case GfxColorTarget.Area: _areaColor = colorIndex; break;
        }
    }

    public void DrawPoints(IReadOnlyList<GfxPoint> points)
    {
        foreach (var p in points)
        {
            var (x, y) = ToPixel(p);
            _canvas.Plot(x, y, _pointColor);
        }
    }

    public void DrawLines(IReadOnlyList<GfxPoint> polyline) =>
        Rasterizer.Polyline(ToPixels(polyline), (x, y) => _canvas.Plot(x, y, _lineColor));

    public void FillArea(IReadOnlyList<GfxPoint> polygon) =>
        Rasterizer.FillPolygon(ToPixels(polygon), (x, y) => _canvas.Plot(x, y, _areaColor));

    public void DrawText(GfxPoint at, string text)
    {
        var (x, y) = ToPixel(at);
        _canvas.AddLabel(x, y, text, _textColor);
    }

    public void Flush() { }

    private static (int X, int Y) ToPixel(GfxPoint p) => (
        (int)Math.Round(p.X * (BrailleCanvas.DotsW - 1)),
        (int)Math.Round((1.0 - p.Y) * (BrailleCanvas.DotsH - 1)));   // flip Y: BASIC up → screen down

    private static List<(int X, int Y)> ToPixels(IReadOnlyList<GfxPoint> pts)
    {
        var result = new List<(int X, int Y)>(pts.Count);
        foreach (var p in pts) result.Add(ToPixel(p));
        return result;
    }
}
