using ArcadeBasic.Runtime;
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;
using Rune = System.Rune;

namespace ArcadeBasic.Ide;

/// <summary>
/// The IDE's Graphics tab: a Terminal.Gui view that shows a <see cref="CellSurface"/>
/// — the same character-grid renderer the CLI uses, so straight lines are box-drawing
/// characters, fills are blocks and everything else is Braille (2 × 4 dots per cell).
/// Programs draw from the interpreter's background thread, so access is locked; the
/// main loop redraws.
///
/// The grid sizes itself to the view's bounds (so a drawing fills whatever space the
/// Graphics pane is given, and adapts when the terminal is resized);
/// <see cref="PixelWidth"/>/<see cref="PixelHeight"/> report it in dots so the graphics
/// device can map into it and <c>ASK DEVICE SIZE</c> reflects it. Until the view is
/// laid out (e.g. in headless tests) it stays at the default 80 × 24 cells.
/// </summary>
internal sealed class BrailleCanvas : View
{
    public const int DefaultDotsW = 160;
    public const int DefaultDotsH = 96;
    // Clamp the grid to a sane range so a stray (or huge) layout can't make a
    // degenerate or wildly large buffer. Limits are in cells.
    private const int MinCellsW = 8, MinCellsH = 3;
    private const int MaxCellsW = 400, MaxCellsH = 150;

    // Back buffer: the frame currently being drawn (the Draw* calls and CLEAR write
    // here; RenderToText/IsEmpty report it). Front buffer: the last *committed*
    // (complete) frame — Redraw paints only this, so the UI never samples a half-built
    // frame (mid-frame sampling right after CLEAR was the on-screen flicker).
    private readonly CellSurface _back = new(DefaultDotsW / 2, DefaultDotsH / 4);
    private readonly CellSurface _front = new(DefaultDotsW / 2, DefaultDotsH / 4);
    private readonly object _lock = new();

    public int PixelWidth { get { lock (_lock) return _back.DotsW; } }
    public int PixelHeight { get { lock (_lock) return _back.DotsH; } }
    public bool IsEmpty { get { lock (_lock) return _back.IsEmpty; } }

    public BrailleCanvas()
    {
        CanFocus = false;
        WantMousePositionReports = true;   // GET MOUSE follows the pointer, not just clicks
        // Re-fit the grid to the view's bounds once layout settles, so drawings fill
        // the pane and follow terminal resizes.
        LayoutComplete += _ => RefitToBounds();
    }

    private void RefitToBounds()
    {
        var cellsW = Bounds.Width;
        var cellsH = Bounds.Height;
        if (cellsW <= 0 || cellsH <= 0) return;   // not laid out yet
        lock (_lock)
        {
            // Resize both buffers where they stand (a no-op at the same size — layout
            // runs far more often than resizes). The front keeps showing the last
            // complete frame until the program presents the next; copying the back
            // buffer here would show whatever frame it's halfway through drawing.
            var cols = Math.Clamp(cellsW, MinCellsW, MaxCellsW);
            var rows = Math.Clamp(cellsH, MinCellsH, MaxCellsH);
            _back.Resize(cols, rows);
            _front.Resize(cols, rows);
        }
    }

    // -- Pointer: the UI thread records the mouse here; the program's thread reads it
    //    through TuiGraphicsDevice (IPointer). Positions are in the device unit square
    //    (origin bottom-left), at the centre of the cell under the mouse. Guarded by _lock.

    private const int MaxPointerEvents = 256;
    private readonly Queue<PointerEvent> _pointerEvents = new();
    private GfxPoint? _pointer;
    private bool _buttonDown;
    private bool _pressSeen;

    public override bool MouseEvent(MouseEvent me)
    {
        lock (_lock)
        {
            var at = CellToUnit(me.X, me.Y);
            _pointer = at;
            if (me.Flags.HasFlag(MouseFlags.Button1Pressed)) Press(at);
            if (me.Flags.HasFlag(MouseFlags.Button1Released)) Release(at);
            if (me.Flags.HasFlag(MouseFlags.Button1Clicked))
            {
                // Some terminals report a quick click only as "clicked".
                if (!_pressSeen)
                {
                    Press(at);
                    Release(at);
                }
                _pressSeen = false;
            }
        }
        return true;
    }

    internal bool TryGetPointer(out GfxPoint position)
    {
        lock (_lock)
        {
            position = _pointer ?? default;
            return _pointer is not null;
        }
    }

    internal bool TryReadPointerEvent(out PointerEvent e)
    {
        lock (_lock) return _pointerEvents.TryDequeue(out e);
    }

    /// <summary>A program is starting: forget clicks from before it.</summary>
    internal void ResetPointer()
    {
        lock (_lock)
        {
            _pointerEvents.Clear();
            _buttonDown = _pressSeen = false;
        }
    }

    internal GfxPoint CellToUnit(int cx, int cy) => new(
        (cx + 0.5) / _back.Cols,
        1 - (cy + 0.5) / _back.Rows);   // the middle of the cell (CellSurface's mapping, inverted)

    private void Press(GfxPoint at)
    {
        if (_buttonDown) return;   // drags repeat Button1Pressed with each move
        _buttonDown = _pressSeen = true;
        AddPointerEvent(new PointerEvent(true, at));
    }

    private void Release(GfxPoint at)
    {
        if (!_buttonDown) return;
        _buttonDown = false;
        AddPointerEvent(new PointerEvent(false, at));
    }

    private void AddPointerEvent(PointerEvent e)
    {
        if (_pointerEvents.Count == MaxPointerEvents) _pointerEvents.Dequeue();
        _pointerEvents.Enqueue(e);
    }

    public void ClearBuffer()
    {
        lock (_lock) _back.Clear();
    }

    /// <summary>Promote the working (back) frame to the front buffer that Redraw
    /// paints. Called at each frame's present point (SLEEP/Flush, before an INPUT
    /// read, and at program end), so the UI only ever shows complete frames.</summary>
    public void Commit()
    {
        lock (_lock) _back.CopyTo(_front);
    }

    // Drawing, in the device unit square — see CellSurface for how each kind renders.

    public void DrawPoints(IReadOnlyList<GfxPoint> points, int color)
    {
        lock (_lock) _back.Points(points, color);
    }

    public void DrawLines(IReadOnlyList<GfxPoint> polyline, int color, int style)
    {
        lock (_lock) _back.Lines(polyline, color, style);
    }

    public void FillArea(IReadOnlyList<GfxPoint> polygon, int color)
    {
        lock (_lock) _back.Area(polygon, color);
    }

    public void DrawText(GfxPoint at, string text, int color)
    {
        lock (_lock) _back.Text(at, text, color);
    }

    /// <summary>The frame being drawn, as text (one line per cell row) — for headless
    /// tests and debugging.</summary>
    internal string RenderToText()
    {
        lock (_lock) return _back.ToText();
    }

    /// <summary>The committed <em>front</em> buffer (what Redraw paints) as text — the
    /// counterpart to <see cref="RenderToText"/>, for double-buffering tests.</summary>
    internal string RenderFrontToText()
    {
        lock (_lock) return _front.ToText();
    }

    public override void Redraw(Rect bounds)
    {
        var driver = Application.Driver;
        if (driver is null) return;

        lock (_lock)
        {
            var rows = Math.Min(_front.Rows, Bounds.Height);
            var cols = Math.Min(_front.Cols, Bounds.Width);
            for (var cy = 0; cy < rows; cy++)
            {
                for (var cx = 0; cx < cols; cx++)
                {
                    var cell = _front[cx, cy];
                    driver.SetAttribute(Attr(driver, cell.Kind == CellSurface.CellKind.Empty ? 1 : cell.Color));
                    AddRune(cx, cy, new Rune(_front.Glyph(cx, cy)));
                }
            }
        }
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
