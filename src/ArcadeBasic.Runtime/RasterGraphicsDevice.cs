namespace ArcadeBasic.Runtime;

/// <summary>
/// A software-rasterizing <see cref="IGraphicsDevice"/> that renders the §13
/// primitive stream into an in-memory ARGB pixel buffer — the engine-agnostic
/// core of the Unity (Texture2D) backend, and usable by any raster target. It
/// has no UnityEngine dependency, so it is netstandard2.1- and IL2CPP-safe and
/// is unit-tested headlessly; the Unity wrapper just copies <see cref="Pixels"/>
/// into a texture.
///
/// Coordinates arrive in the normalized device square [0,1] (origin bottom-left);
/// they map to the buffer with Y flipped (row 0 = top). Colours are a fixed
/// 16-entry palette shared in spirit with the other backends. Text is drawn with
/// the shared <see cref="BitmapFont"/>.
/// </summary>
public sealed class RasterGraphicsDevice : IGraphicsDevice
{
    private int _w;
    private int _h;
    private int[] _pixels = [];
    private readonly BitmapFont _font = BitmapFont.Default;

    private int _pointColor = 1, _lineColor = 1, _textColor = 1, _areaColor = 1;

    public RasterGraphicsDevice(int width, int height) => Resize(width, height);

    /// <summary>Width/height of the pixel buffer.</summary>
    public int Width => _w;
    public int Height => _h;

    /// <summary>The ARGB (0xAARRGGBB) pixel buffer, row-major, row 0 at the top.
    /// The Unity wrapper converts these to <c>Color32</c> (and flips rows, since
    /// Unity textures are bottom-up).</summary>
    public int[] Pixels => _pixels;

    /// <summary>Re-allocate the buffer (e.g. when the display area changes) and clear it.</summary>
    public void Resize(int width, int height)
    {
        _w = Math.Max(1, width);
        _h = Math.Max(1, height);
        _pixels = new int[_w * _h];
        Clear();
    }

    public int MaxColor => Palette.Length - 1;
    public int MaxLineStyle => 3;
    public int MaxPointStyle => 3;
    public GfxDeviceSize DeviceSize => new(_w, _h, "OTHER");

    public void Clear() => Array.Fill(_pixels, OpaqueBlack);

    // Styles aren't expressed on this solid raster backend (same as the braille canvas).
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
        var argb = Color(_pointColor);
        foreach (var p in points) { var (x, y) = ToPixel(p); Plot(x, y, argb); }
    }

    public void DrawLines(IReadOnlyList<GfxPoint> polyline)
    {
        var argb = Color(_lineColor);
        Rasterizer.Polyline(ToPixels(polyline), (x, y) => Plot(x, y, argb));
    }

    public void FillArea(IReadOnlyList<GfxPoint> polygon)
    {
        var argb = Color(_areaColor);
        Rasterizer.FillPolygon(ToPixels(polygon), (x, y) => Plot(x, y, argb));
    }

    public void DrawText(GfxPoint at, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var argb = Color(_textColor);
        var (x, y) = ToPixel(at);
        // BASIC's world Y is up, so the anchor point is the text's bottom-left and
        // the glyph rises from it. BitmapFont.Draw places y at the glyph's top and
        // draws downward, so shift up by the glyph height; otherwise text drawn at
        // the bottom edge (e.g. a status line at y=0) would render off the buffer.
        var top = y - (BitmapFont.Height - 1);
        for (var i = 0; i < text.Length; i++)
            _font.Draw(text[i], x + i * BitmapFont.Advance, top, (px, py) => Plot(px, py, argb));
    }

    public void Flush() { }   // the Unity wrapper reads Pixels and uploads to its texture

    // -- internals --------------------------------------------------------

    private void Plot(int x, int y, int argb)
    {
        if (x < 0 || x >= _w || y < 0 || y >= _h) return;
        _pixels[y * _w + x] = argb;
    }

    private (int X, int Y) ToPixel(GfxPoint p) => (
        (int)Math.Round(p.X * (_w - 1)),
        (int)Math.Round((1.0 - p.Y) * (_h - 1)));   // flip Y: BASIC up → buffer row down

    private List<(int X, int Y)> ToPixels(IReadOnlyList<GfxPoint> pts)
    {
        var r = new List<(int X, int Y)>(pts.Count);
        foreach (var p in pts) r.Add(ToPixel(p));
        return r;
    }

    private static int Color(int index) => Palette[index < 0 || index >= Palette.Length ? 1 : index];

    // Explicit opaque black so the cleared screen reads as an arcade display.
    private static readonly int OpaqueBlack = unchecked((int)0xFF000000);

    // 16-colour ARGB palette (0xAARRGGBB), matching the other backends' intent.
    private static readonly int[] Palette =
    [
        OpaqueBlack,                       // 0  used as the clear/background colour
        unchecked((int)0xFFFFFFFF),        // 1  white (default)
        unchecked((int)0xFFFF5555),        // 2  bright red
        unchecked((int)0xFF55FF55),        // 3  bright green
        unchecked((int)0xFF5555FF),        // 4  bright blue
        unchecked((int)0xFFFF55FF),        // 5  bright magenta
        unchecked((int)0xFF55FFFF),        // 6  bright cyan
        unchecked((int)0xFFFFFF55),        // 7  bright yellow
        unchecked((int)0xFFAAAAAA),        // 8  gray
        unchecked((int)0xFFAA0000),        // 9  red
        unchecked((int)0xFF00AA00),        // 10 green
        unchecked((int)0xFF0000AA),        // 11 blue
        unchecked((int)0xFFAA00AA),        // 12 magenta
        unchecked((int)0xFF00AAAA),        // 13 cyan
        unchecked((int)0xFFAA5500),        // 14 brown
        unchecked((int)0xFFFFFFFF),        // 15 white
    ];
}
