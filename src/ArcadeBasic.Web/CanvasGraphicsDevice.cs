using ArcadeBasic.Runtime;

namespace ArcadeBasic.Web;

/// <summary>
/// The §13 graphics backend for the page's &lt;canvas&gt;. GraphicsState hands it
/// already-clipped primitives in the unit square (origin bottom-left); it records
/// them as a flat list of numbers that the page replays onto the canvas, one
/// JS-interop call per frame rather than one per primitive.
///
/// Frames follow the terminal backends' double buffering: CLEAR starts a new
/// frame, SLEEP's <see cref="Flush"/> presents it, and the page only ever sees
/// complete frames. A program that never flushes (a static drawing) is shown live.
///
/// It's also the pointer for LOCATE POINT and GET MOUSE: the page reports pointer
/// positions and primary-button presses/releases in the same unit square.
/// </summary>
internal sealed class CanvasGraphicsDevice : IGraphicsDevice, IPointer, ICellArrayDevice, IColorMixDevice
{
    // Command encoding (main.js `drawFrame` must match):
    //   Color  target index
    //   Lines  n  x1 y1 … xn yn
    //   Area   n  x1 y1 … xn yn
    //   Points n  x1 y1 … xn yn
    //   Text   x y textIndex        (textIndex into FrameText)
    //   LineStyle style              (1 solid, 2 dashed, 3 dotted)
    //   PointStyle style             (1 dot, 2 plus, 3 asterisk)
    //   Cells  left bottom right top columns rows  visibleLeft visibleBottom visibleRight visibleTop
    //          atlasX atlasY         (the cells' colours, row-major from the top-left, are a
    //                                 columns x rows block of FrameCells' atlas at that spot)
    //   Palette argb0 … argb255     (first in a frame, when the colours have changed)
    internal const double Color = 2, Lines = 3, Area = 4, Points = 5, Text = 6, LineStyle = 7, PointStyle = 8,
        Cells = 9, Palette = 10;

    // Each frame's cell arrays are packed into one atlas — the page uploads it as a
    // single image and draws every array from it — in shelves up to this wide.
    private const int AtlasWidth = 2048;

    private readonly List<double> _commands = [];
    private readonly List<string> _texts = [];
    private readonly int[] _colors = [1, 1, 1, 1];   // per GfxColorTarget: point, line, text, area
    private int _lineStyle = 1, _pointStyle = 3;    // GraphicsState's defaults
    private double[]? _presented;
    private string[] _presentedTexts = [];
    private bool _everFlushed;
    private bool _changed;

    private readonly GfxPalette _palette = new();
    private bool _paletteChanged = true;            // the page hasn't been sent these colours yet
    private readonly List<(int X, int Y, int Columns, int Rows, int[] Colors)> _cellBlocks = [];
    private int _shelfX, _shelfY, _shelfHeight, _atlasW;
    private double[] _presentedCells = [];

    private const int MaxPointerEvents = 256;
    private readonly Queue<PointerEvent> _pointerEvents = new();
    private GfxPoint? _pointer;
    private bool _buttonDown;

    public int MaxColor => GfxPalette.Count - 1;
    public int MaxLineStyle => 3;
    public int MaxPointStyle => 3;
    /// <summary>The same 80 x 24-cell screen the terminal backends give (2 x 4 Braille dots
    /// per cell), so programs that size their layout from ASK DEVICE SIZE — kanban.bas
    /// fits one text line per 4 units — come out the same in the browser. The page
    /// draws it at 960 x 576 (12 x 24 px cells) with text one cell high.</summary>
    public GfxDeviceSize DeviceSize => new(160, 96, "OTHER");

    /// <summary>The strings the last frame from <see cref="TakeFrame"/> refers to.</summary>
    public string[] FrameText { get; private set; } = [];

    /// <summary>The last frame's cell-array atlas: its width and height, then the colour
    /// index of each of its cells, row by row (empty when the frame has no cells).</summary>
    public double[] FrameCells { get; private set; } = [];

    /// <summary>Forget everything (a new program is starting).</summary>
    public void Reset()
    {
        _commands.Clear();
        _texts.Clear();
        Array.Fill(_colors, 1);
        _lineStyle = 1;
        _pointStyle = 3;
        _presented = null;
        _presentedTexts = [];
        _presentedCells = [];
        ClearCells();
        _palette.Reset();
        _paletteChanged = true;
        _everFlushed = false;
        _changed = true;
        _pointerEvents.Clear();
        _buttonDown = false;
    }

    // -- Pointer (fed by the page) -------------------------------------------

    public void PointerMove(double x, double y) => _pointer = new GfxPoint(x, y);

    public void PointerDown(double x, double y)
    {
        _pointer = new GfxPoint(x, y);
        if (_buttonDown) return;
        _buttonDown = true;
        AddPointerEvent(new PointerEvent(true, _pointer.Value));
    }

    public void PointerUp(double x, double y)
    {
        _pointer = new GfxPoint(x, y);
        if (!_buttonDown) return;
        _buttonDown = false;
        AddPointerEvent(new PointerEvent(false, _pointer.Value));
    }

    /// <summary>The page lost the pointer mid-press (focus, capture): release where it was last seen.</summary>
    public void PointerCancel()
    {
        if (!_buttonDown) return;
        _buttonDown = false;
        AddPointerEvent(new PointerEvent(false, _pointer ?? default));
    }

    public bool TryGetPosition(out GfxPoint position)
    {
        position = _pointer ?? default;
        return _pointer is not null;
    }

    public bool TryReadEvent(out PointerEvent e) => _pointerEvents.TryDequeue(out e);

    private void AddPointerEvent(PointerEvent e)
    {
        if (_pointerEvents.Count == MaxPointerEvents) _pointerEvents.Dequeue();
        _pointerEvents.Enqueue(e);
    }

    /// <summary>The newest frame to draw, or an empty array if nothing changed since the last call.</summary>
    public double[] TakeFrame()
    {
        if (_presented is not null)
        {
            var frame = _presented;
            FrameText = _presentedTexts;
            FrameCells = _presentedCells;
            _presented = null;
            return WithPalette(frame);
        }
        if (!_everFlushed && _changed)
        {
            _changed = false;
            FrameText = [.. _texts];
            FrameCells = Atlas();
            return WithPalette([.. _commands]);
        }
        return [];
    }

    /// <summary>The frame, led by the palette if the page hasn't got these colours yet.</summary>
    private double[] WithPalette(double[] frame)
    {
        if (!_paletteChanged) return frame;
        _paletteChanged = false;
        var withPalette = new double[1 + GfxPalette.Count + frame.Length];
        withPalette[0] = Palette;
        for (var i = 0; i < GfxPalette.Count; i++) withPalette[1 + i] = _palette.Argb(i);
        frame.CopyTo(withPalette, 1 + GfxPalette.Count);
        return withPalette;
    }

    public void Clear()
    {
        _commands.Clear();
        _texts.Clear();
        ClearCells();
        // Colours and styles outlive CLEAR, so the new frame starts by restating them.
        for (var t = 0; t < _colors.Length; t++) _commands.AddRange([Color, t, _colors[t]]);
        _commands.AddRange([LineStyle, _lineStyle, PointStyle, _pointStyle]);
        _changed = true;
    }

    public void Flush()
    {
        _presented = [.. _commands];
        _presentedTexts = [.. _texts];
        _presentedCells = Atlas();
        _everFlushed = true;
    }

    public void DrawCells(GfxRect rect, int columns, int rows, int[] colors, GfxRect visible)
    {
        if (_shelfX + columns > AtlasWidth && _shelfX > 0)
        {
            _shelfY += _shelfHeight;
            _shelfX = _shelfHeight = 0;
        }
        var (x, y) = (_shelfX, _shelfY);
        _cellBlocks.Add((x, y, columns, rows, colors));
        _shelfX += columns;
        _shelfHeight = Math.Max(_shelfHeight, rows);
        _atlasW = Math.Max(_atlasW, _shelfX);
        _commands.AddRange([Cells, rect.Left, rect.Bottom, rect.Right, rect.Top, columns, rows,
            visible.Left, visible.Bottom, visible.Right, visible.Top, x, y]);
        _changed = true;
    }

    public void SetColorMix(int index, double red, double green, double blue)
    {
        _palette.Set(index, red, green, blue);
        _paletteChanged = true;
        _changed = true;
    }

    private void ClearCells()
    {
        _cellBlocks.Clear();
        _shelfX = _shelfY = _shelfHeight = _atlasW = 0;
    }

    /// <summary>The frame's cell arrays as one atlas (see <see cref="FrameCells"/>).</summary>
    private double[] Atlas()
    {
        if (_cellBlocks.Count == 0) return [];
        int w = _atlasW, h = _shelfY + _shelfHeight;
        var atlas = new double[2 + w * h];
        atlas[0] = w;
        atlas[1] = h;
        foreach (var (x, y, columns, rows, colors) in _cellBlocks)
        {
            for (var r = 0; r < rows; r++)
                for (var c = 0; c < columns; c++)
                    atlas[2 + (y + r) * w + x + c] = colors[r * columns + c];
        }
        return atlas;
    }

    public void SetColor(GfxColorTarget target, int colorIndex)
    {
        _colors[(int)target] = colorIndex;
        _commands.AddRange([Color, (int)target, colorIndex]);
        _changed = true;
    }

    public void SetLineStyle(int style)
    {
        _lineStyle = style;
        _commands.AddRange([LineStyle, style]);
        _changed = true;
    }

    public void SetPointStyle(int style)
    {
        _pointStyle = style;
        _commands.AddRange([PointStyle, style]);
        _changed = true;
    }

    public void DrawPoints(IReadOnlyList<GfxPoint> points) => Shape(Points, points);

    public void DrawLines(IReadOnlyList<GfxPoint> polyline) => Shape(Lines, polyline);

    public void FillArea(IReadOnlyList<GfxPoint> polygon) => Shape(Area, polygon);

    public void DrawText(GfxPoint at, string text)
    {
        _commands.AddRange([Text, at.X, at.Y, _texts.Count]);
        _texts.Add(text);
        _changed = true;
    }

    private void Shape(double op, IReadOnlyList<GfxPoint> points)
    {
        _commands.Add(op);
        _commands.Add(points.Count);
        foreach (var p in points)
        {
            _commands.Add(p.X);
            _commands.Add(p.Y);
        }
        _changed = true;
    }
}
