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
/// they map to the buffer with Y flipped (row 0 = top). Colours come from the
/// shared 256-entry <see cref="GfxPalette"/>, which SET COLOR MIX changes. Text is
/// drawn with the shared <see cref="BitmapFont"/>.
/// </summary>
public sealed class RasterGraphicsDevice : IGraphicsDevice, ICellArrayDevice, IColorMixDevice
{
    private int _w;
    private int _h;
    private int[] _pixels = [];
    private readonly BitmapFont _font = BitmapFont.Default;
    private readonly GfxPalette _palette = new();

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

    public int MaxColor => GfxPalette.Count - 1;
    public int MaxLineStyle => 3;
    public int MaxPointStyle => 3;
    public GfxDeviceSize DeviceSize => new(_w, _h, "OTHER");

    public void Clear() => Array.Fill(_pixels, Color(0));   // colour 0 is the background (opaque black unless remixed)

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

    public void DrawCells(GfxRect rect, int columns, int rows, int[] colors, GfxRect visible)
    {
        // Every pixel whose centre is inside the visible part takes the cell under it.
        visible = new GfxRect(Math.Max(visible.Left, rect.Left), Math.Min(visible.Right, rect.Right),
            Math.Max(visible.Bottom, rect.Bottom), Math.Min(visible.Top, rect.Top));
        int px0 = (int)Math.Ceiling(visible.Left * (_w - 1)), px1 = (int)Math.Floor(visible.Right * (_w - 1));
        int py0 = (int)Math.Ceiling((1 - visible.Top) * (_h - 1)), py1 = (int)Math.Floor((1 - visible.Bottom) * (_h - 1));
        double width = rect.Right - rect.Left, height = rect.Top - rect.Bottom;
        for (var py = Math.Max(0, py0); py <= Math.Min(_h - 1, py1); py++)
        {
            var y = 1 - (double)py / (_h - 1);
            var cy = Math.Clamp((int)((rect.Top - y) / height * rows), 0, rows - 1);
            for (var px = Math.Max(0, px0); px <= Math.Min(_w - 1, px1); px++)
            {
                var cx = Math.Clamp((int)(((double)px / (_w - 1) - rect.Left) / width * columns), 0, columns - 1);
                _pixels[py * _w + px] = Color(colors[cy * columns + cx]);
            }
        }
    }

    public void SetColorMix(int index, double red, double green, double blue) => _palette.Set(index, red, green, blue);

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

    private int Color(int index) => _palette.Argb(index);

}
