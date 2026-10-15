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
internal sealed class CanvasGraphicsDevice : IGraphicsDevice, IPointer
{
    // Command encoding (main.js `drawFrame` must match):
    //   Color  target index
    //   Lines  n  x1 y1 … xn yn
    //   Area   n  x1 y1 … xn yn
    //   Points n  x1 y1 … xn yn
    //   Text   x y textIndex        (textIndex into FrameText)
    //   LineStyle style              (1 solid, 2 dashed, 3 dotted)
    //   PointStyle style             (1 dot, 2 plus, 3 asterisk)
    internal const double Color = 2, Lines = 3, Area = 4, Points = 5, Text = 6, LineStyle = 7, PointStyle = 8;

    private readonly List<double> _commands = [];
    private readonly List<string> _texts = [];
    private readonly int[] _colors = [1, 1, 1, 1];   // per GfxColorTarget: point, line, text, area
    private int _lineStyle = 1, _pointStyle = 3;    // GraphicsState's defaults
    private double[]? _presented;
    private string[] _presentedTexts = [];
    private bool _everFlushed;
    private bool _changed;

    private const int MaxPointerEvents = 256;
    private readonly Queue<PointerEvent> _pointerEvents = new();
    private GfxPoint? _pointer;
    private bool _buttonDown;

    public int MaxColor => 15;
    public int MaxLineStyle => 3;
    public int MaxPointStyle => 3;
    /// <summary>The same 80 x 24-cell screen the terminal backends give (2 x 4 Braille dots
    /// per cell), so programs that size their layout from ASK DEVICE SIZE — kanban.bas
    /// fits one text line per 4 units — come out the same in the browser. The page
    /// draws it at 960 x 576 (12 x 24 px cells) with text one cell high.</summary>
    public GfxDeviceSize DeviceSize => new(160, 96, "OTHER");

    /// <summary>The strings the last frame from <see cref="TakeFrame"/> refers to.</summary>
    public string[] FrameText { get; private set; } = [];

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
            _presented = null;
            return frame;
        }
        if (!_everFlushed && _changed)
        {
            _changed = false;
            FrameText = [.. _texts];
            return [.. _commands];
        }
        return [];
    }

    public void Clear()
    {
        _commands.Clear();
        _texts.Clear();
        // Colours and styles outlive CLEAR, so the new frame starts by restating them.
        for (var t = 0; t < _colors.Length; t++) _commands.AddRange([Color, t, _colors[t]]);
        _commands.AddRange([LineStyle, _lineStyle, PointStyle, _pointStyle]);
        _changed = true;
    }

    public void Flush()
    {
        _presented = [.. _commands];
        _presentedTexts = [.. _texts];
        _everFlushed = true;
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
