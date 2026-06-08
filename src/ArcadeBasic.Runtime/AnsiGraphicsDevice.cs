using System.Text;

namespace ArcadeBasic.Runtime;

/// <summary>
/// An <see cref="IGraphicsDevice"/> that renders the §13 primitive stream to a
/// terminal using ANSI escape sequences and Unicode Braille glyphs (a 2×4 dot
/// matrix per character cell, U+2800…U+28FF) — the same braille model as the IDE
/// canvas, but written straight to a <see cref="TextWriter"/>, so graphics work
/// in the CLI and in standalone binaries (no Terminal.Gui; netstandard2.1- and
/// AOT-safe).
///
/// Layout mirrors the IDE: the drawing fills the terminal except the <b>bottom
/// row, which is reserved for the program's input line</b>. So <c>ASK DEVICE
/// SIZE</c> reports <c>(cols×2) × ((rows−1)×4)</c> dots and a program reads input
/// on the line directly beneath the picture.
///
/// The device is inert until the first draw call, so a non-graphics program
/// leaves the terminal completely alone. The first <see cref="Present"/> switches
/// to the alternate screen; <see cref="EndSession"/> restores it.
/// </summary>
public sealed class AnsiGraphicsDevice : IGraphicsDevice
{
    private const string Esc = "\x1b[";

    private readonly TextWriter _out;
    private readonly Func<(int Cols, int Rows)> _size;

    private int _cols, _rows;          // terminal size snapshot (rows counts the input row)
    private bool[,] _dots = new bool[0, 0];
    private int[,] _cellColor = new int[0, 0];
    private readonly List<(int Cx, int Cy, string Text, int Color)> _labels = new();
    private bool _sized;
    private bool _dirty;               // something has been drawn
    private bool _begun;               // alternate screen entered

    private int _pointColor = 1, _lineColor = 1, _textColor = 1, _areaColor = 1;

    /// <param name="output">Where ANSI output goes (the console, or a writer in tests).</param>
    /// <param name="size">Current terminal size in character cells; re-read each frame.</param>
    public AnsiGraphicsDevice(TextWriter output, Func<(int Cols, int Rows)> size)
    {
        _out = output;
        _size = size;
    }

    /// <summary>True once the program has drawn at least one primitive.</summary>
    public bool Active => _dirty;

    public int MaxColor => 15;
    public int MaxLineStyle => 3;
    public int MaxPointStyle => 3;

    public GfxDeviceSize DeviceSize { get { EnsureSized(); return new GfxDeviceSize(DotsW, DotsH, "OTHER"); } }

    private int CanvasRows => Math.Max(1, _rows - 1);   // reserve the bottom row for input
    private int DotsW => _cols * 2;
    private int DotsH => CanvasRows * 4;

    // -- IGraphicsDevice --------------------------------------------------

    public void Clear()
    {
        Snapshot();                    // new frame: pick up any terminal resize
        Array.Clear(_dots, 0, _dots.Length);
        ResetCellColors();
        _labels.Clear();
        // CLEAR by itself isn't "drawing": leave _dirty as-is so a program that
        // only clears never switches the terminal to the alternate screen.
    }

    public void SetLineStyle(int style) { }   // not expressible on a 1-bit braille grid
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
        foreach (var p in points) { var (x, y) = ToPixel(p); Plot(x, y, _pointColor); }
    }

    public void DrawLines(IReadOnlyList<GfxPoint> polyline) =>
        Rasterizer.Polyline(ToPixels(polyline), (x, y) => Plot(x, y, _lineColor));

    public void FillArea(IReadOnlyList<GfxPoint> polygon) =>
        Rasterizer.FillPolygon(ToPixels(polygon), (x, y) => Plot(x, y, _areaColor));

    public void DrawText(GfxPoint at, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var (x, y) = ToPixel(at);
        _labels.Add((x / 2, y / 4, text, _textColor));
        _dirty = true;
    }

    public void Flush() => Present();

    // -- presentation -----------------------------------------------------

    /// <summary>Paint the current frame to the terminal and leave the cursor on
    /// the reserved input row. No-op until something has been drawn.</summary>
    public void Present()
    {
        if (!_dirty) return;
        EnsureSized();

        var sb = new StringBuilder();
        if (!_begun) { sb.Append(Esc).Append("?1049h"); _begun = true; }   // alternate screen
        sb.Append(Esc).Append("?25l");                                     // hide cursor while painting

        for (var cy = 0; cy < CanvasRows; cy++)
        {
            sb.Append(Esc).Append(cy + 1).Append(";1H");                   // row cy+1, col 1 (1-based)
            var cur = -1;
            for (var cx = 0; cx < _cols; cx++)
            {
                var bits = CellBits(cx, cy);
                var color = bits == 0 ? 1 : _cellColor[cx, cy];
                if (color != cur) { sb.Append(Sgr(color)); cur = color; }
                sb.Append(CellGlyph.ForBits(bits));
            }
            sb.Append(Esc).Append('K');                                    // clear to end of line
        }

        foreach (var (lcx, lcy, text, color) in _labels)                   // text labels on top
        {
            if (lcy < 0 || lcy >= CanvasRows) continue;
            sb.Append(Esc).Append(lcy + 1).Append(';').Append(lcx + 1).Append('H').Append(Sgr(color));
            for (var i = 0; i < text.Length; i++)
            {
                var col = lcx + i;
                if (col >= 0 && col < _cols) sb.Append(text[i]);
            }
        }

        sb.Append(Esc).Append(_rows).Append(";1H").Append(Esc).Append('K'); // clear the input row
        sb.Append(Esc).Append("0m").Append(Esc).Append("?25h");             // reset colours, show cursor
        _out.Write(sb.ToString());
        _out.Flush();
    }

    /// <summary>Restore the terminal (leave the alternate screen, show the
    /// cursor, reset colours). Safe to call when never begun.</summary>
    public void EndSession()
    {
        if (!_begun) return;
        _out.Write(Esc + "0m" + Esc + "?25h" + Esc + "?1049l");
        _out.Flush();
        _begun = false;
    }

    // -- internals --------------------------------------------------------

    private void EnsureSized() { if (!_sized) Snapshot(); }

    private void Snapshot()
    {
        var (cols, rows) = _size();
        cols = Math.Max(1, cols);
        rows = Math.Max(2, rows);
        _sized = true;
        if (cols == _cols && rows == _rows && _dots.Length != 0) return;
        _cols = cols;
        _rows = rows;
        _dots = new bool[DotsW, DotsH];
        _cellColor = new int[_cols, CanvasRows];
        ResetCellColors();
    }

    private void ResetCellColors()
    {
        for (var i = 0; i < _cols; i++)
            for (var j = 0; j < CanvasRows; j++)
                _cellColor[i, j] = 1;
    }

    private void Plot(int x, int y, int color)
    {
        if (x < 0 || x >= DotsW || y < 0 || y >= DotsH) return;
        _dots[x, y] = true;
        _cellColor[x / 2, y / 4] = color;
        _dirty = true;
    }

    private (int X, int Y) ToPixel(GfxPoint p)
    {
        EnsureSized();
        return ((int)Math.Round(p.X * (DotsW - 1)),
                (int)Math.Round((1.0 - p.Y) * (DotsH - 1)));   // flip Y: BASIC up → screen down
    }

    private List<(int X, int Y)> ToPixels(IReadOnlyList<GfxPoint> pts)
    {
        var r = new List<(int X, int Y)>(pts.Count);
        foreach (var p in pts) r.Add(ToPixel(p));
        return r;
    }

    private int CellBits(int cx, int cy)
    {
        // 2×4 dot cell → Unicode braille bit pattern (matches the IDE canvas).
        int bx = cx * 2, by = cy * 4, bits = 0;
        if (_dots[bx, by]) bits |= 0x01;
        if (_dots[bx, by + 1]) bits |= 0x02;
        if (_dots[bx, by + 2]) bits |= 0x04;
        if (_dots[bx + 1, by]) bits |= 0x08;
        if (_dots[bx + 1, by + 1]) bits |= 0x10;
        if (_dots[bx + 1, by + 2]) bits |= 0x20;
        if (_dots[bx, by + 3]) bits |= 0x40;
        if (_dots[bx + 1, by + 3]) bits |= 0x80;
        return bits;
    }

    private static string Sgr(int color) => Esc + AnsiColor(color) + "m";

    // Palette index → ANSI SGR foreground code, approximating the IDE's 16-colour
    // terminal palette.
    private static int AnsiColor(int index) => index switch
    {
        0 => 90, 1 => 37, 2 => 91, 3 => 92, 4 => 94, 5 => 95, 6 => 96, 7 => 93,
        8 => 90, 9 => 31, 10 => 32, 11 => 34, 12 => 35, 13 => 36, 14 => 33, 15 => 97,
        _ => 37,
    };
}
