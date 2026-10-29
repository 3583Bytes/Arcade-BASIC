using System.Text;

namespace ArcadeBasic.Runtime;

/// <summary>
/// An <see cref="IGraphicsDevice"/> that renders the §13 primitive stream to a
/// terminal using ANSI escape sequences and Unicode Braille glyphs (a 2×4 dot
/// matrix per character cell, U+2800…U+28FF), written straight to a
/// <see cref="TextWriter"/>, so graphics work in the CLI and in standalone binaries
/// (netstandard2.1- and AOT-safe).
///
/// Layout: the drawing fills the terminal except the <b>bottom
/// row, which is reserved for the program's input line</b>. So <c>ASK DEVICE
/// SIZE</c> reports <c>(cols×2) × ((rows−1)×4)</c> dots and a program reads input
/// on the line directly beneath the picture.
///
/// The device is inert until the first draw call, so a non-graphics program
/// leaves the terminal completely alone. The first <see cref="Present"/> switches
/// to the alternate screen; <see cref="EndSession"/> restores it.
///
/// The mouse: with <see cref="MouseReporting"/> on, the first time a program looks for
/// the pointer the device asks the terminal to report the mouse (xterm's reporting, with
/// SGR coordinates). The reports arrive among the keys; the keyboard picks them out and
/// hands them to <see cref="MouseReport"/>. Until the first one there's no pointer
/// (<see cref="IPointerPresence"/>), so a program on a terminal that can't report the
/// mouse carries on without one.
/// </summary>
public sealed class AnsiGraphicsDevice : IGraphicsDevice, ICellArrayDevice, IColorMixDevice, IPointer, IPointerPresence
{
    private const string Esc = "\x1b[";

    private readonly TextWriter _out;
    private readonly Func<(int Cols, int Rows)> _size;

    private int _cols, _rows;          // terminal size snapshot (rows counts the input row)
    private readonly CellSurface _surface = new(1, 1);   // the drawing, cell by cell (shared with the IDE)
    private readonly GfxPalette _palette = new();
    private bool _sized;
    private bool _dirty;               // something has been drawn
    private bool _begun;               // alternate screen entered

    private int _pointColor = 1, _lineColor = 1, _textColor = 1, _areaColor = 1;
    private int _lineStyle = 1;

    private readonly Queue<PointerEvent> _clicks = new();
    private GfxPoint _mouseAt;
    private bool _mouseOn;             // the terminal has been asked to report the mouse
    private bool _mouseSeen;           // and it has

    /// <param name="output">Where ANSI output goes (the console, or a writer in tests).</param>
    /// <param name="size">Current terminal size in character cells; re-read each frame.</param>
    public AnsiGraphicsDevice(TextWriter output, Func<(int Cols, int Rows)> size)
    {
        _out = output;
        _size = size;
    }

    /// <summary>True once the program has drawn at least one primitive.</summary>
    public bool Active => _dirty;

    public int MaxColor => GfxPalette.Count - 1;
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
        _surface.Clear();
        // CLEAR by itself isn't "drawing": leave _dirty as-is so a program that
        // only clears never switches the terminal to the alternate screen.
    }

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

    public void DrawPoints(IReadOnlyList<GfxPoint> points) => Draw(() => _surface.Points(points, _pointColor));

    public void DrawLines(IReadOnlyList<GfxPoint> polyline) => Draw(() => _surface.Lines(polyline, _lineColor, _lineStyle));

    public void FillArea(IReadOnlyList<GfxPoint> polygon) => Draw(() => _surface.Area(polygon, _areaColor));

    public void DrawText(GfxPoint at, string text) => Draw(() => _surface.Text(at, text, _textColor));

    public void DrawCells(GfxRect rect, int columns, int rows, int[] colors, GfxRect visible) =>
        Draw(() => _surface.Cells(rect, columns, rows, colors, visible));

    public void SetColorMix(int index, double red, double green, double blue) => _palette.Set(index, red, green, blue);

    private void Draw(Action draw)
    {
        EnsureSized();
        draw();
        _dirty |= !_surface.IsEmpty;
    }

    public void Flush() => Present();

    // -- the mouse ----------------------------------------------------------

    /// <summary>May the device ask the terminal to report the mouse? (An xterm-compatible
    /// terminal reports it among the keys; the Windows console doesn't.)</summary>
    public bool MouseReporting { get; set; }

    public bool PointerSeen
    {
        get
        {
            if (MouseReporting && !_mouseOn)
            {
                // presses and releases, moves with a button held, any move; SGR coordinates
                _out.Write(Esc + "?1000h" + Esc + "?1002h" + Esc + "?1003h" + Esc + "?1006h");
                _out.Flush();
                _mouseOn = true;
            }
            return _mouseSeen;
        }
    }

    public bool TryGetPosition(out GfxPoint position)
    {
        position = _mouseAt;
        return _mouseSeen;
    }

    public bool TryReadEvent(out PointerEvent e)
    {
        if (_clicks.Count > 0)
        {
            e = _clicks.Dequeue();
            return true;
        }
        e = default;
        return false;
    }

    /// <summary>A mouse report from the terminal: <c>ESC [ &lt; button ; column ; row M</c>
    /// (<c>m</c> for a release), column and row counted from 1. The pointer is in the middle
    /// of that cell; only the primary button's presses and releases are events — a move
    /// (button + 32), the wheel (64 on) and the other buttons just move it.</summary>
    public void MouseReport(int button, int column, int row, bool released)
    {
        EnsureSized();
        var x = Math.Min(Math.Max((column - 0.5) / _cols, 0), 1);
        var y = Math.Min(Math.Max(1 - (row - 0.5) / CanvasRows, 0), 1);
        _mouseAt = new GfxPoint(x, y);
        _mouseSeen = true;
        if ((button & (32 | 64)) != 0 || (button & 3) != 0) return;
        _clicks.Enqueue(new PointerEvent(!released, _mouseAt));
    }

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
            int cur = -1, curBack = 0;
            for (var cx = 0; cx < _cols; cx++)
            {
                var cell = _surface[cx, cy];
                var color = cell.Kind == CellSurface.CellKind.Empty ? 1 : cell.Color;
                var back = cell.Kind == CellSurface.CellKind.Text ? cell.Back : 0;
                if (color != cur) { sb.Append(Sgr(color)); cur = color; }
                if (back != curBack) { sb.Append(SgrBack(back)); curBack = back; }
                sb.Append(_surface.Glyph(cx, cy));
            }
            if (curBack != 0) sb.Append(SgrBack(0));
            sb.Append(Esc).Append('K');                                    // clear to end of line
        }

        sb.Append(Esc).Append(_rows).Append(";1H").Append(Esc).Append('K'); // clear the input row
        sb.Append(Esc).Append("0m").Append(Esc).Append("?25h");             // reset colours, show cursor
        _out.Write(sb.ToString());
        _out.Flush();
    }

    /// <summary>Restore the terminal (stop its mouse reports, leave the alternate screen, show the
    /// cursor, reset colours). Safe to call when never begun.</summary>
    public void EndSession()
    {
        if (_mouseOn)
        {
            _out.Write(Esc + "?1006l" + Esc + "?1003l" + Esc + "?1002l" + Esc + "?1000l");
            _out.Flush();
            _mouseOn = false;
        }
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
        if (cols == _cols && rows == _rows) return;
        _cols = cols;
        _rows = rows;
        _surface.Resize(_cols, CanvasRows);
    }

    /// <summary>The 16 standard colours are the terminal's own; any other colour (16–255,
    /// or one SET COLOR MIX has changed) is the nearest of the 256-colour palette.</summary>
    private string Sgr(int color) => _palette.IsStandard(color)
        ? Esc + AnsiColor(color) + "m"
        : Esc + "38;5;" + _palette.NearestXterm(color) + "m";

    /// <summary>The background behind text written over a filled area (0: the terminal's own).</summary>
    private string SgrBack(int color) => color == 0
        ? Esc + "49m"
        : _palette.IsStandard(color)
            ? Esc + (AnsiColor(color) + 10) + "m"
            : Esc + "48;5;" + _palette.NearestXterm(color) + "m";

    // Palette index → ANSI SGR foreground code: the 16 standard colours.
    private static int AnsiColor(int index) => index switch
    {
        0 => 90, 1 => 37, 2 => 91, 3 => 92, 4 => 94, 5 => 95, 6 => 96, 7 => 93,
        8 => 90, 9 => 31, 10 => 32, 11 => 34, 12 => 35, 13 => 36, 14 => 33, 15 => 97,
        _ => 37,
    };
}
