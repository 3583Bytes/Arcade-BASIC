using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;
using Rune = System.Rune;

namespace ArcadeBasic.Ide;

/// <summary>
/// A Terminal.Gui view that renders a sub-cell bitmap using Unicode Braille
/// patterns: each character cell packs a 2×4 dot matrix (U+2800..U+28FF), so a
/// region of C×R cells addresses a (2C)×(4R) "pixel" grid. Graphic-output
/// statements plot into the dot grid (from the interpreter's background thread,
/// so access is locked); the main loop redraws. GRAPH TEXT labels are drawn
/// natively on top of the braille cells.
///
/// The dot grid sizes itself to the view's bounds (so a drawing fills whatever
/// space the Graphics pane is given, and adapts when the terminal is resized);
/// <see cref="PixelWidth"/>/<see cref="PixelHeight"/> report the current grid so
/// the graphics device can map into it and <c>ASK DEVICE SIZE</c> reflects it.
/// Until the view is laid out (e.g. in headless tests) it stays at the default
/// 160×96 / 80×24 size.
/// </summary>
internal sealed class BrailleCanvas : View
{
    public const int DefaultDotsW = 160;
    public const int DefaultDotsH = 96;
    // Clamp the dot grid to a sane range so a stray (or huge) layout can't make a
    // degenerate or wildly large buffer. Limits are in cells; dots are ×2 / ×4.
    private const int MinCellsW = 8, MinCellsH = 3;
    private const int MaxCellsW = 400, MaxCellsH = 150;

    private int _dotsW = DefaultDotsW;
    private int _dotsH = DefaultDotsH;
    // Back buffer: the frame currently being drawn (Plot/CLEAR/AddLabel write here;
    // RenderToText/IsEmpty report it).
    private bool[,] _dots = new bool[DefaultDotsW, DefaultDotsH];
    private int[,] _cellColor = new int[DefaultDotsW / 2, DefaultDotsH / 4];
    private readonly List<(int Cx, int Cy, string Text, int Color)> _labels = new();
    // Front buffer: the last *committed* (complete) frame. Redraw paints only this,
    // so the UI never samples a half-built frame — that mid-frame sampling (the pump
    // catching the buffer right after CLEAR) was the on-screen flicker. CLEAR touches
    // only the back buffer, so the displayed frame holds steady until the next Commit.
    private bool[,] _frontDots = new bool[DefaultDotsW, DefaultDotsH];
    private int[,] _frontCellColor = new int[DefaultDotsW / 2, DefaultDotsH / 4];
    private List<(int Cx, int Cy, string Text, int Color)> _frontLabels = new();
    private readonly object _lock = new();
    private bool _empty = true;

    public int PixelWidth => _dotsW;
    public int PixelHeight => _dotsH;
    private int CellsW => _dotsW / 2;
    private int CellsH => _dotsH / 4;
    public bool IsEmpty { get { lock (_lock) return _empty; } }

    public BrailleCanvas()
    {
        CanFocus = false;
        ClearBuffer();
        // Re-fit the dot grid to the view's bounds once layout settles, so
        // drawings fill the pane and follow terminal resizes.
        LayoutComplete += _ => RefitToBounds();
    }

    private void RefitToBounds()
    {
        var cellsW = Bounds.Width;
        var cellsH = Bounds.Height;
        if (cellsW <= 0 || cellsH <= 0) return;   // not laid out yet
        Resize(Math.Clamp(cellsW, MinCellsW, MaxCellsW),
               Math.Clamp(cellsH, MinCellsH, MaxCellsH));
    }

    public void ClearBuffer()
    {
        lock (_lock)
        {
            Array.Clear(_dots, 0, _dots.Length);
            ResetColors();
            _labels.Clear();
            _empty = true;
        }
    }

    /// <summary>Promote the working (back) frame to the front buffer that Redraw
    /// paints. Called at each frame's present point (SLEEP/Flush, before an INPUT
    /// read, and at program end), so the UI only ever shows complete frames.</summary>
    public void Commit()
    {
        lock (_lock) CopyBackToFront();
    }

    // Clone the back buffer into the front buffer in one shot. Caller holds _lock.
    private void CopyBackToFront()
    {
        if (_frontDots.GetLength(0) != _dotsW || _frontDots.GetLength(1) != _dotsH)
        {
            _frontDots = new bool[_dotsW, _dotsH];
            _frontCellColor = new int[CellsW, CellsH];
        }
        Array.Copy(_dots, _frontDots, _dots.Length);
        Array.Copy(_cellColor, _frontCellColor, _cellColor.Length);
        _frontLabels = new List<(int Cx, int Cy, string Text, int Color)>(_labels);
    }

    public void Plot(int x, int y, int colorIndex)
    {
        lock (_lock)
        {
            if (x < 0 || x >= _dotsW || y < 0 || y >= _dotsH) return;
            _dots[x, y] = true;
            _cellColor[x / 2, y / 4] = colorIndex;
            _empty = false;
        }
    }

    public void AddLabel(int dotX, int dotY, string text, int colorIndex)
    {
        if (string.IsNullOrEmpty(text)) return;
        lock (_lock)
        {
            _labels.Add((dotX / 2, dotY / 4, text, colorIndex));
            _empty = false;
        }
    }

    /// <summary>Render the braille layer to a plain string (one line per cell
    /// row), no console driver required. For headless tests and debugging.</summary>
    internal string RenderToText()
    {
        lock (_lock)
        {
            var sb = new System.Text.StringBuilder();
            for (var cy = 0; cy < CellsH; cy++)
            {
                for (var cx = 0; cx < CellsW; cx++)
                    sb.Append((char)(0x2800 + CellBits(_dots, cx, cy)));
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }

    /// <summary>Render the committed <em>front</em> buffer (what Redraw paints) to
    /// text — the counterpart to <see cref="RenderToText"/>, which renders the
    /// still-being-drawn back buffer. For headless double-buffering tests.</summary>
    internal string RenderFrontToText()
    {
        lock (_lock)
        {
            var sb = new System.Text.StringBuilder();
            for (var cy = 0; cy < CellsH; cy++)
            {
                for (var cx = 0; cx < CellsW; cx++)
                    sb.Append((char)(0x2800 + CellBits(_frontDots, cx, cy)));
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }

    public override void Redraw(Rect bounds)
    {
        var driver = Application.Driver;
        if (driver is null) return;

        lock (_lock)
        {
            var rows = Math.Min(CellsH, Bounds.Height);
            var cols = Math.Min(CellsW, Bounds.Width);
            for (var cy = 0; cy < rows; cy++)
            {
                for (var cx = 0; cx < cols; cx++)
                {
                    var bits = CellBits(_frontDots, cx, cy);
                    driver.SetAttribute(Attr(driver, bits == 0 ? 1 : _frontCellColor[cx, cy]));
                    AddRune(cx, cy, new Rune(ArcadeBasic.Runtime.CellGlyph.ForBits(bits)));
                }
            }

            // Native text labels on top of the braille layer.
            foreach (var (lcx, lcy, text, color) in _frontLabels)
            {
                if (lcy < 0 || lcy >= rows) continue;
                driver.SetAttribute(Attr(driver, color));
                for (var i = 0; i < text.Length; i++)
                {
                    var col = lcx + i;
                    if (col < 0 || col >= cols) continue;
                    AddRune(col, lcy, new Rune(text[i]));
                }
            }
        }
    }

    /// <summary>Reallocate the dot grid to <paramref name="cellsW"/>×<paramref name="cellsH"/>
    /// cells, rescaling any existing drawing (nearest-neighbour) so a resize keeps
    /// the picture visible until the program redraws. Leaves <see cref="IsEmpty"/>
    /// unchanged so input routing stays stable across a resize.</summary>
    private void Resize(int cellsW, int cellsH)
    {
        lock (_lock)
        {
            if (cellsW == CellsW && cellsH == CellsH) return;

            int newDotsW = cellsW * 2, newDotsH = cellsH * 4;
            var newDots = new bool[newDotsW, newDotsH];
            var newColor = new int[cellsW, cellsH];
            for (var i = 0; i < cellsW; i++)
                for (var j = 0; j < cellsH; j++)
                    newColor[i, j] = 1;

            if (!_empty)
            {
                int oldDotsW = _dotsW, oldDotsH = _dotsH, oldCellsW = CellsW, oldCellsH = CellsH;
                for (var nx = 0; nx < newDotsW; nx++)
                {
                    int ox = Math.Min((int)((long)nx * oldDotsW / newDotsW), oldDotsW - 1);
                    for (var ny = 0; ny < newDotsH; ny++)
                    {
                        int oy = Math.Min((int)((long)ny * oldDotsH / newDotsH), oldDotsH - 1);
                        newDots[nx, ny] = _dots[ox, oy];
                    }
                }
                for (var i = 0; i < cellsW; i++)
                {
                    int oi = Math.Min(i * oldCellsW / cellsW, oldCellsW - 1);
                    for (var j = 0; j < cellsH; j++)
                        newColor[i, j] = _cellColor[oi, Math.Min(j * oldCellsH / cellsH, oldCellsH - 1)];
                }
                for (var k = 0; k < _labels.Count; k++)
                {
                    var (cx, cy, txt, col) = _labels[k];
                    _labels[k] = (cx * cellsW / oldCellsW, cy * cellsH / oldCellsH, txt, col);
                }
            }

            _dots = newDots;
            _cellColor = newColor;
            _dotsW = newDotsW;
            _dotsH = newDotsH;
            CopyBackToFront();   // keep the displayed frame sized to the new grid
        }
    }

    private void ResetColors()
    {
        for (var i = 0; i < CellsW; i++)
            for (var j = 0; j < CellsH; j++)
                _cellColor[i, j] = 1;
    }

    private static int CellBits(bool[,] dots, int cx, int cy)
    {
        int bx = cx * 2, by = cy * 4;
        var bits = 0;
        if (dots[bx, by]) bits |= 0x01;
        if (dots[bx, by + 1]) bits |= 0x02;
        if (dots[bx, by + 2]) bits |= 0x04;
        if (dots[bx + 1, by]) bits |= 0x08;
        if (dots[bx + 1, by + 1]) bits |= 0x10;
        if (dots[bx + 1, by + 2]) bits |= 0x20;
        if (dots[bx, by + 3]) bits |= 0x40;
        if (dots[bx + 1, by + 3]) bits |= 0x80;
        return bits;
    }

    private static readonly Color[] Palette =
    [
        Color.DarkGray, Color.White, Color.BrightRed, Color.BrightGreen,
        Color.BrightBlue, Color.BrightMagenta, Color.BrightCyan, Color.BrightYellow,
        Color.Gray, Color.Red, Color.Green, Color.Blue,
        Color.Magenta, Color.Cyan, Color.Brown, Color.White,
    ];

    private static Attribute Attr(ConsoleDriver driver, int index) =>
        driver.MakeAttribute(Palette[index < 0 || index >= Palette.Length ? 1 : index], Color.Black);
}
