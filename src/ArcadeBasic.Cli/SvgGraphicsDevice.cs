using System.Globalization;
using System.Text;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Cli;

/// <summary>
/// A headless <see cref="IGraphicsDevice"/> that renders the §13 primitive
/// stream to an SVG document. Coordinates arrive in the normalized device unit
/// square [0,1] (origin bottom-left); they are scaled to a fixed viewBox and
/// Y-flipped to SVG's top-left origin. This is the Phase-1 backend: vector
/// output, deterministic, no UI.
/// </summary>
public sealed class SvgGraphicsDevice : IGraphicsDevice, ICellArrayDevice, IColorMixDevice
{
    private const int Size = 1000;
    private readonly StringBuilder _body = new();
    private readonly GfxPalette _palette = new();

    private int _lineStyle = 1, _pointStyle = 3;
    private int _pointColor = 1, _lineColor = 1, _textColor = 1, _areaColor = 1;

    public int MaxColor => GfxPalette.Count - 1;
    public int MaxLineStyle => 3;
    public int MaxPointStyle => 3;
    public GfxDeviceSize DeviceSize => new(Size, Size, "OTHER");

    // Paper is white here, so the 16 standard colours are re-mapped for print (0 white,
    // 1 black); colours 16–255, and any SET COLOR MIX has changed, are their own.
    private static readonly string[] Palette =
    [
        "white", "black", "red", "green", "blue", "magenta", "cyan", "yellow",
        "gray", "maroon", "lime", "navy", "purple", "teal", "olive", "silver",
    ];

    public void Clear() => _body.Clear();
    public void SetLineStyle(int style) => _lineStyle = style;
    public void SetPointStyle(int style) => _pointStyle = style;

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
        var fill = Color(_pointColor);
        foreach (var p in points)
        {
            var (x, y) = Map(p);
            _body.Append(FormattableString.Invariant(
                $"  <circle cx=\"{x:F2}\" cy=\"{y:F2}\" r=\"4\" fill=\"{fill}\" />\n"));
        }
    }

    public void DrawLines(IReadOnlyList<GfxPoint> polyline)
    {
        _body.Append("  <polyline fill=\"none\" stroke=\"").Append(Color(_lineColor))
             .Append("\" stroke-width=\"2\"").Append(Dash(_lineStyle))
             .Append(" points=\"").Append(Points(polyline)).Append("\" />\n");
    }

    public void FillArea(IReadOnlyList<GfxPoint> polygon)
    {
        _body.Append("  <polygon fill=\"").Append(Color(_areaColor))
             .Append("\" points=\"").Append(Points(polygon)).Append("\" />\n");
    }

    public void DrawText(GfxPoint at, string text)
    {
        var (x, y) = Map(at);
        _body.Append(FormattableString.Invariant(
            $"  <text x=\"{x:F2}\" y=\"{y:F2}\" fill=\"{Color(_textColor)}\" font-size=\"24\">{Escape(text)}</text>\n"));
    }

    public void Flush() { }

    public void DrawCells(GfxRect rect, int columns, int rows, int[] colors, GfxRect visible)
    {
        // One rectangle per run of same-coloured cells in a row, cut to the visible part.
        var cw = (rect.Right - rect.Left) / columns;
        var ch = (rect.Top - rect.Bottom) / rows;
        for (var r = 0; r < rows; r++)
        {
            var top = Math.Min(rect.Top - r * ch, visible.Top);
            var bottom = Math.Max(rect.Top - (r + 1) * ch, visible.Bottom);
            if (bottom >= top) continue;
            for (var c = 0; c < columns;)
            {
                var color = colors[r * columns + c];
                var end = c + 1;
                while (end < columns && colors[r * columns + end] == color) end++;
                var left = Math.Max(rect.Left + c * cw, visible.Left);
                var right = Math.Min(rect.Left + end * cw, visible.Right);
                if (left < right && color >= 0)   // -1: see-through
                {
                    var (x, y) = Map(new GfxPoint(left, top));
                    _body.Append(FormattableString.Invariant(
                        $"  <rect x=\"{x:F2}\" y=\"{y:F2}\" width=\"{(right - left) * Size:F2}\" height=\"{(top - bottom) * Size:F2}\" fill=\"{Color(color)}\" />\n"));
                }
                c = end;
            }
        }
    }

    public void SetColorMix(int index, double red, double green, double blue) => _palette.Set(index, red, green, blue);

    public string ToSvg()
    {
        var sb = new StringBuilder();
        sb.Append(FormattableString.Invariant(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {Size} {Size}\" width=\"{Size}\" height=\"{Size}\">\n"));
        sb.Append("  <rect width=\"100%\" height=\"100%\" fill=\"white\" />\n");
        sb.Append(_body);
        sb.Append("</svg>\n");
        return sb.ToString();
    }

    private static (double X, double Y) Map(GfxPoint p) => (p.X * Size, (1.0 - p.Y) * Size);

    private static string Points(IReadOnlyList<GfxPoint> pts)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < pts.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            var (x, y) = Map(pts[i]);
            sb.Append(x.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(y.ToString("F2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private string Color(int index) =>
        (uint)index >= GfxPalette.Count ? Palette[1]   // out of range: the default colour
        : _palette.IsStandard(index) ? Palette[index]
        : "#" + (_palette.Argb(index) & 0xFFFFFF).ToString("x6", CultureInfo.InvariantCulture);

    private static string Dash(int style) => style switch
    {
        2 => " stroke-dasharray=\"12 6\"",
        3 => " stroke-dasharray=\"3 5\"",
        _ => "",
    };

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
