using ArcadeBasic.Runtime;

namespace ArcadeBasic.Ide;

/// <summary>
/// Terminal graphics backend: hands the core's primitives (normalized device
/// coordinates, origin bottom-left) to a <see cref="BrailleCanvas"/>, whose
/// <see cref="CellSurface"/> draws them as box-drawing lines, block fills, Braille
/// dots and text. Colours map to the 16-colour terminal palette. Drawing happens on
/// the interpreter's background thread — the canvas serialises access. The canvas's
/// mouse is the pointer for LOCATE POINT and GET MOUSE.
/// </summary>
internal sealed class TuiGraphicsDevice : IGraphicsDevice, IPointer
{
    private readonly BrailleCanvas _canvas;
    private int _pointColor = 1, _lineColor = 1, _textColor = 1, _areaColor = 1;
    private int _lineStyle = 1;

    public TuiGraphicsDevice(BrailleCanvas canvas) => _canvas = canvas;

    public int MaxColor => 15;
    public int MaxLineStyle => 3;
    public int MaxPointStyle => 3;
    public GfxDeviceSize DeviceSize => new(_canvas.PixelWidth, _canvas.PixelHeight, "OTHER");

    public void Clear() => _canvas.ClearBuffer();

    public void SetLineStyle(int style) => _lineStyle = style;   // dashed/dotted box-drawing lines
    public void SetPointStyle(int style) { }                       // a point is one Braille dot

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

    public void DrawPoints(IReadOnlyList<GfxPoint> points) => _canvas.DrawPoints(points, _pointColor);

    public void DrawLines(IReadOnlyList<GfxPoint> polyline) => _canvas.DrawLines(polyline, _lineColor, _lineStyle);

    public void FillArea(IReadOnlyList<GfxPoint> polygon) => _canvas.FillArea(polygon, _areaColor);

    public void DrawText(GfxPoint at, string text) => _canvas.DrawText(at, text, _textColor);

    // A frame is complete: promote it to the canvas's front buffer so the UI shows
    // it atomically. SLEEP calls this each frame; without it the UI would sample
    // half-built frames and flicker.
    public void Flush() => _canvas.Commit();

    public bool TryGetPosition(out GfxPoint position) => _canvas.TryGetPointer(out position);

    public bool TryReadEvent(out PointerEvent e) => _canvas.TryReadPointerEvent(out e);
}
