using Singulink.Numerics;

namespace ArcadeBasic.Runtime;

/// <summary>An axis-aligned rectangle given by its edge coordinates.</summary>
public struct GfxRect(double left, double right, double bottom, double top)
{
    public double Left = left, Right = right, Bottom = bottom, Top = top;

    public readonly double XMin => Math.Min(Left, Right);
    public readonly double XMax => Math.Max(Left, Right);
    public readonly double YMin => Math.Min(Bottom, Top);
    public readonly double YMax => Math.Max(Bottom, Top);
}

/// <summary>
/// Device-independent state and geometry for the ECMA-116 §13 graphics module,
/// shared by the tree-walking interpreter and the bytecode VM. It owns the
/// current window / viewport / device window / device viewport / clipping flag
/// and the modal style/colour indices, and turns problem-coordinate drawing
/// requests into clipped <see cref="GfxPoint"/> primitives in the normalized
/// device unit square, which it hands to an <see cref="IGraphicsDevice"/>.
///
/// Keeping every transform and clip here (à la <c>MatOps</c>/<c>PictureFormat</c>)
/// is what makes the two engines render byte-identically.
///
/// Coordinate pipeline: problem coords --(WINDOW→VIEWPORT)--> NDC, clip to the
/// effective rectangle, --(DEVICE WINDOW→DEVICE VIEWPORT)--> normalized surface.
/// The device transform is a plain linear remap; physical aspect-ratio
/// preservation is left to the backend, which knows its real pixel geometry.
/// </summary>
public sealed class GraphicsState
{
    // Defaults per §13.1.4 / §13.2.4.
    public GfxRect Window = new(0, 1, 0, 1);
    public GfxRect Viewport = new(0, 1, 0, 1);
    public GfxRect DeviceWindow = new(0, 1, 0, 1);
    public GfxRect DeviceViewport = new(0, 1, 0, 1);
    public bool ClipEnabled = true;

    public int LineStyle = 1;   // 1 solid, 2 dashed, 3 dotted
    public int PointStyle = 3;  // 1 dot, 2 plus, 3 asterisk
    public int PointColor = 1, LineColor = 1, TextColor = 1, AreaColor = 1;

    /// <summary>Convert an evaluated numeric value to a drawing coordinate.
    /// Both engines route through this so they hand identical points to the
    /// device (preserving byte-for-byte parity).</summary>
    public static double ToCoord(NumericValue v) => v.D;

    /// <summary>Round an evaluated numeric value to a style/colour index.</summary>
    public static int ToIndex(NumericValue v) => v.IsNative
        ? (int)Math.Round(v.D, MidpointRounding.ToEven)
        : (int)BigDecimal.Round(v.V, 0, RoundingMode.MidpointToEven);

    /// <summary>
    /// Compute the value an ASK statement reads for the given object and target
    /// index. Both engines call this, so they assign identical values. Indices:
    /// rectangles 0..3 = left/right/bottom/top; DEVICE SIZE 0/1/2 = width/height/unit$.
    /// </summary>
    public Value Query(GfxQuery q, int index, IGraphicsDevice dev, bool native) => q switch
    {
        GfxQuery.Window => RectComponent(Window, index, native),
        GfxQuery.Viewport => RectComponent(Viewport, index, native),
        GfxQuery.DeviceWindow => RectComponent(DeviceWindow, index, native),
        GfxQuery.DeviceViewport => RectComponent(DeviceViewport, index, native),
        GfxQuery.DeviceSize => index switch
        {
            0 => Num(dev.DeviceSize.Width, native),
            1 => Num(dev.DeviceSize.Height, native),
            _ => new StringValue(dev.DeviceSize.Unit),
        },
        GfxQuery.Clip => new StringValue(ClipEnabled ? "ON" : "OFF"),
        GfxQuery.PointStyle => Num(PointStyle, native),
        GfxQuery.LineStyle => Num(LineStyle, native),
        GfxQuery.PointColor => Num(PointColor, native),
        GfxQuery.LineColor => Num(LineColor, native),
        GfxQuery.TextColor => Num(TextColor, native),
        GfxQuery.AreaColor => Num(AreaColor, native),
        GfxQuery.MaxColor => Num(dev.MaxColor, native),
        GfxQuery.MaxPointStyle => Num(dev.MaxPointStyle, native),
        GfxQuery.MaxLineStyle => Num(dev.MaxLineStyle, native),
        _ => Num(0, native),
    };

    private static Value RectComponent(GfxRect r, int i, bool native) =>
        Num(i switch { 0 => r.Left, 1 => r.Right, 2 => r.Bottom, _ => r.Top }, native);

    private static Value Num(double v, bool native) => Coordinate(v, native);

    // -- SET handling ----------------------------------------------------
    // Invalid bounds (zero/negative size, out of range) leave the current
    // value unchanged — the spec's nonfatal "continue with current values".

    public void SetWindow(double l, double r, double b, double t)
    {
        if (l == r || b == t) return;
        Window = new GfxRect(l, r, b, t);
    }

    public void SetViewport(double l, double r, double b, double t)
    {
        if (l >= r || b >= t) return;
        if (!InUnit(l) || !InUnit(r) || !InUnit(b) || !InUnit(t)) return;
        Viewport = new GfxRect(l, r, b, t);
    }

    public bool SetDeviceWindow(double l, double r, double b, double t)
    {
        if (l >= r || b >= t) return false;
        if (!InUnit(l) || !InUnit(r) || !InUnit(b) || !InUnit(t)) return false;
        DeviceWindow = new GfxRect(l, r, b, t);
        return true; // caller clears the surface (§13.1.4)
    }

    public bool SetDeviceViewport(double l, double r, double b, double t)
    {
        if (l >= r || b >= t) return false;
        if (!InUnit(l) || !InUnit(r) || !InUnit(b) || !InUnit(t)) return false;
        DeviceViewport = new GfxRect(l, r, b, t);
        return true; // caller clears the surface (§13.1.4)
    }

    private static bool InUnit(double v) => v is >= 0 and <= 1;

    // -- Emit: problem coords → clipped normalized-surface primitives ----

    public void EmitPoints(IReadOnlyList<GfxPoint> problem, IGraphicsDevice dev)
    {
        var clip = EffectiveClipRect();
        var outPts = new List<GfxPoint>();
        foreach (var p in problem)
        {
            var ndc = ToNdc(p);
            if (PointInRect(ndc, clip)) outPts.Add(ToSurface(ndc));
        }
        if (outPts.Count > 0) dev.DrawPoints(outPts);
    }

    public void EmitLines(IReadOnlyList<GfxPoint> problem, IGraphicsDevice dev)
    {
        var clip = EffectiveClipRect();
        var ndc = new GfxPoint[problem.Count];
        for (var i = 0; i < problem.Count; i++) ndc[i] = ToNdc(problem[i]);

        // Clip each segment; emit maximal connected runs that survive.
        var run = new List<GfxPoint>();
        for (var i = 0; i + 1 < ndc.Length; i++)
        {
            if (ClipSegment(ndc[i], ndc[i + 1], clip, out var a, out var b))
            {
                if (run.Count == 0 || !Near(run[^1], a))
                {
                    if (run.Count >= 2) dev.DrawLines(run);
                    if (run.Count > 0) run = [];
                    run.Add(ToSurface(a));
                }
                run.Add(ToSurface(b));
            }
            else if (run.Count >= 2) { dev.DrawLines(run); run = []; }
            else if (run.Count > 0) run = [];
        }
        if (run.Count >= 2) dev.DrawLines(run);
    }

    public void EmitArea(IReadOnlyList<GfxPoint> problem, IGraphicsDevice dev)
    {
        var clip = EffectiveClipRect();
        var poly = new List<GfxPoint>(problem.Count);
        foreach (var p in problem) poly.Add(ToNdc(p));
        var clipped = ClipPolygon(poly, clip);
        if (clipped.Count >= 3)
        {
            for (var i = 0; i < clipped.Count; i++) clipped[i] = ToSurface(clipped[i]);
            dev.FillArea(clipped);
        }
    }

    public void EmitText(GfxPoint problemAt, string text, IGraphicsDevice dev) =>
        dev.DrawText(ToSurface(ToNdc(problemAt)), text);

    /// <summary>
    /// MAT GRAPH CELLS (ANSI X3.113 §13.3.4.4): fill the rectangle with corners
    /// <paramref name="p1"/> and <paramref name="p2"/> (problem coordinates) with one
    /// equal cell per element of the two-dimensional <paramref name="cells"/>, coloured
    /// by the element's rounded value. The first subscript runs horizontally and the
    /// second vertically; the element with the lowest subscripts sits at
    /// <paramref name="p1"/> and the highest at <paramref name="p2"/>.
    /// </summary>
    public void EmitCells(GfxPoint p1, GfxPoint p2, NumericArrayValue cells, IGraphicsDevice dev)
    {
        if (cells.Bounds.Rank != 2)
            throw new BasicRuntimeException(6401, "MAT GRAPH CELLS needs a two-dimensional array");
        var nx = cells.Bounds.Upper[0] - cells.Bounds.Lower[0] + 1;
        var ny = cells.Bounds.Upper[1] - cells.Bounds.Lower[1] + 1;
        if (nx <= 0 || ny <= 0) return;

        var a = ToSurface(ToNdc(p1));
        var b = ToSurface(ToNdc(p2));
        var rect = new GfxRect(Math.Min(a.X, b.X), Math.Max(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y));
        // A zero-width or zero-height rectangle (the standard recommends a line of
        // cells) draws nothing.
        if (rect.Left == rect.Right || rect.Bottom == rect.Top) return;
        var clipNdc = EffectiveClipRect();
        var c0 = ToSurface(new GfxPoint(clipNdc.XMin, clipNdc.YMin));
        var c1 = ToSurface(new GfxPoint(clipNdc.XMax, clipNdc.YMax));
        var visible = new GfxRect(
            Math.Max(rect.Left, c0.X), Math.Min(rect.Right, c1.X),
            Math.Max(rect.Bottom, c0.Y), Math.Min(rect.Top, c1.Y));
        if (visible.Left >= visible.Right || visible.Bottom >= visible.Top) return;

        // Devices take the cells row by row from the top-left; the window may flip
        // either axis, so walk the array from whichever corner ends up there.
        var flipX = a.X > b.X;
        var firstAtTop = a.Y > b.Y;
        var colors = new int[nx * ny];
        var max = dev.MaxColor;
        for (var r = 0; r < ny; r++)
        {
            var j = firstAtTop ? r : ny - 1 - r;
            for (var c = 0; c < nx; c++)
            {
                var i = flipX ? nx - 1 - c : c;
                var k = ToIndex(cells[i * ny + j]);
                colors[r * nx + c] = k < 0 || k > max ? 1 : k;   // 11085 (nonfatal): the default colour
            }
        }

        if (dev is ICellArrayDevice cellDevice) cellDevice.DrawCells(rect, nx, ny, colors, visible);
        else FillCells(rect, nx, ny, colors, visible, dev);
    }

    /// <summary>Cells for a device that can only fill areas: one rectangle per run of
    /// same-coloured cells in a row, then the AREA COLOR put back.</summary>
    private void FillCells(GfxRect rect, int nx, int ny, int[] colors, GfxRect visible, IGraphicsDevice dev)
    {
        var cw = (rect.Right - rect.Left) / nx;
        var ch = (rect.Top - rect.Bottom) / ny;
        var current = -1;
        for (var r = 0; r < ny; r++)
        {
            var y1 = Math.Min(rect.Top - r * ch, visible.Top);
            var y0 = Math.Max(rect.Top - (r + 1) * ch, visible.Bottom);
            if (y0 >= y1) continue;
            for (var c = 0; c < nx;)
            {
                var color = colors[r * nx + c];
                var end = c + 1;
                while (end < nx && colors[r * nx + end] == color) end++;
                var x0 = Math.Max(rect.Left + c * cw, visible.Left);
                var x1 = Math.Min(rect.Left + end * cw, visible.Right);
                if (x0 < x1)
                {
                    if (color != current) dev.SetColor(GfxColorTarget.Area, current = color);
                    dev.FillArea([new GfxPoint(x0, y0), new GfxPoint(x1, y0), new GfxPoint(x1, y1), new GfxPoint(x0, y1)]);
                }
                c = end;
            }
        }
        if (current >= 0 && current != AreaColor) dev.SetColor(GfxColorTarget.Area, AreaColor);
    }

    // -- COLOR MIX -------------------------------------------------------

    private double[]? _mix;   // red, green, blue per colour index, once SET COLOR MIX is used

    /// <summary>SET COLOR MIX (index) red, green, blue. An index outside 0 to MAX COLOR is
    /// exception 11085 and an intensity outside 0–1 is 11088 — both nonfatal: the mix is
    /// left as it was, and an intensity is taken to the nearest end of the range.</summary>
    public void SetColorMix(int index, double red, double green, double blue, IGraphicsDevice dev)
    {
        if (index < 0 || index > dev.MaxColor || index >= GfxPalette.Count) return;
        red = Intensity(red);
        green = Intensity(green);
        blue = Intensity(blue);
        var mix = Mixes();
        mix[3 * index] = red;
        mix[3 * index + 1] = green;
        mix[3 * index + 2] = blue;
        if (dev is IColorMixDevice mixDevice) mixDevice.SetColorMix(index, red, green, blue);
    }

    /// <summary>What ASK COLOR MIX (index) reads: component 0–2 is red, green or blue (the
    /// last SET COLOR MIX for it, else its starting colour); 3 is the STATUS — 11086 for
    /// an index outside 0 to MAX COLOR, whose intensities read as zero.</summary>
    public Value QueryColorMix(int index, int component, IGraphicsDevice dev, bool native)
    {
        var valid = index >= 0 && index <= dev.MaxColor && index < GfxPalette.Count;
        if (component == 3) return Num(valid ? 0 : 11086, native);
        return Num(valid ? Mixes()[3 * index + component] : 0, native);
    }

    private double[] Mixes()
    {
        if (_mix is not null) return _mix;
        _mix = new double[3 * GfxPalette.Count];
        for (var i = 0; i < GfxPalette.Count; i++)
            (_mix[3 * i], _mix[3 * i + 1], _mix[3 * i + 2]) = GfxPalette.DefaultMix(i);
        return _mix;
    }

    private static double Intensity(double v) => double.IsNaN(v) ? 0 : Math.Clamp(v, 0, 1);

    // -- Transforms ------------------------------------------------------

    private GfxPoint ToNdc(GfxPoint p) => new(
        Remap(p.X, Window.Left, Window.Right, Viewport.Left, Viewport.Right),
        Remap(p.Y, Window.Bottom, Window.Top, Viewport.Bottom, Viewport.Top));

    private GfxPoint ToSurface(GfxPoint ndc) => new(
        Remap(ndc.X, DeviceWindow.Left, DeviceWindow.Right, DeviceViewport.Left, DeviceViewport.Right),
        Remap(ndc.Y, DeviceWindow.Bottom, DeviceWindow.Top, DeviceViewport.Bottom, DeviceViewport.Top));

    /// <summary>The output pipeline run backwards: a point on the device surface (unit
    /// square, origin bottom-left) in problem coordinates — what graphic input reports.</summary>
    public GfxPoint ToProblem(GfxPoint surface)
    {
        var ndc = new GfxPoint(
            Remap(surface.X, DeviceViewport.Left, DeviceViewport.Right, DeviceWindow.Left, DeviceWindow.Right),
            Remap(surface.Y, DeviceViewport.Bottom, DeviceViewport.Top, DeviceWindow.Bottom, DeviceWindow.Top));
        return new GfxPoint(
            Remap(ndc.X, Viewport.Left, Viewport.Right, Window.Left, Window.Right),
            Remap(ndc.Y, Viewport.Bottom, Viewport.Top, Window.Bottom, Window.Top));
    }

    /// <summary>Is a problem-coordinate point inside the current window?</summary>
    public bool InWindow(GfxPoint problem) => PointInRect(problem, Window);

    /// <summary>A coordinate as a BASIC number in the program's arithmetic mode.</summary>
    public static NumericValue Coordinate(double v, bool native) =>
        native ? NumericValue.FromDouble(v) : new NumericValue(Numbers.ToDecimal(v));

    private static double Remap(double v, double a0, double a1, double b0, double b1) =>
        a1 == a0 ? b0 : b0 + (v - a0) * (b1 - b0) / (a1 - a0);

    /// <summary>The NDC rectangle geometry is clipped to: device window always,
    /// intersected with the viewport when clipping is enabled.</summary>
    private GfxRect EffectiveClipRect()
    {
        if (!ClipEnabled) return DeviceWindow;
        return new GfxRect(
            Math.Max(Viewport.XMin, DeviceWindow.XMin),
            Math.Min(Viewport.XMax, DeviceWindow.XMax),
            Math.Max(Viewport.YMin, DeviceWindow.YMin),
            Math.Min(Viewport.YMax, DeviceWindow.YMax));
    }

    // -- Clipping primitives ---------------------------------------------

    private static bool PointInRect(GfxPoint p, GfxRect r) =>
        p.X >= r.XMin && p.X <= r.XMax && p.Y >= r.YMin && p.Y <= r.YMax;

    private static bool Near(GfxPoint a, GfxPoint b) =>
        Math.Abs(a.X - b.X) < 1e-12 && Math.Abs(a.Y - b.Y) < 1e-12;

    [Flags]
    private enum OutCode { Inside = 0, Left = 1, Right = 2, Bottom = 4, Top = 8 }

    private static OutCode Code(GfxPoint p, GfxRect r)
    {
        var c = OutCode.Inside;
        if (p.X < r.XMin) c |= OutCode.Left; else if (p.X > r.XMax) c |= OutCode.Right;
        if (p.Y < r.YMin) c |= OutCode.Bottom; else if (p.Y > r.YMax) c |= OutCode.Top;
        return c;
    }

    /// <summary>Cohen–Sutherland clip of a single segment to <paramref name="r"/>.</summary>
    private static bool ClipSegment(GfxPoint p0, GfxPoint p1, GfxRect r, out GfxPoint a, out GfxPoint b)
    {
        double x0 = p0.X, y0 = p0.Y, x1 = p1.X, y1 = p1.Y;
        var c0 = Code(new GfxPoint(x0, y0), r);
        var c1 = Code(new GfxPoint(x1, y1), r);
        while (true)
        {
            if ((c0 | c1) == OutCode.Inside) { a = new GfxPoint(x0, y0); b = new GfxPoint(x1, y1); return true; }
            if ((c0 & c1) != OutCode.Inside) { a = default; b = default; return false; }

            var outside = c0 != OutCode.Inside ? c0 : c1;
            double x = 0, y = 0;
            if ((outside & OutCode.Top) != 0) { x = x0 + (x1 - x0) * (r.YMax - y0) / (y1 - y0); y = r.YMax; }
            else if ((outside & OutCode.Bottom) != 0) { x = x0 + (x1 - x0) * (r.YMin - y0) / (y1 - y0); y = r.YMin; }
            else if ((outside & OutCode.Right) != 0) { y = y0 + (y1 - y0) * (r.XMax - x0) / (x1 - x0); x = r.XMax; }
            else if ((outside & OutCode.Left) != 0) { y = y0 + (y1 - y0) * (r.XMin - x0) / (x1 - x0); x = r.XMin; }

            if (outside == c0) { x0 = x; y0 = y; c0 = Code(new GfxPoint(x0, y0), r); }
            else { x1 = x; y1 = y; c1 = Code(new GfxPoint(x1, y1), r); }
        }
    }

    /// <summary>Sutherland–Hodgman clip of a polygon to the rectangle.</summary>
    private static List<GfxPoint> ClipPolygon(List<GfxPoint> poly, GfxRect r)
    {
        List<GfxPoint> output = poly;
        output = ClipEdge(output, p => p.X >= r.XMin, (s, e) => IntersectX(s, e, r.XMin));
        output = ClipEdge(output, p => p.X <= r.XMax, (s, e) => IntersectX(s, e, r.XMax));
        output = ClipEdge(output, p => p.Y >= r.YMin, (s, e) => IntersectY(s, e, r.YMin));
        output = ClipEdge(output, p => p.Y <= r.YMax, (s, e) => IntersectY(s, e, r.YMax));
        return output;
    }

    private static List<GfxPoint> ClipEdge(List<GfxPoint> input, Func<GfxPoint, bool> inside, Func<GfxPoint, GfxPoint, GfxPoint> intersect)
    {
        var output = new List<GfxPoint>();
        if (input.Count == 0) return output;
        var prev = input[^1];
        foreach (var cur in input)
        {
            bool curIn = inside(cur), prevIn = inside(prev);
            if (curIn)
            {
                if (!prevIn) output.Add(intersect(prev, cur));
                output.Add(cur);
            }
            else if (prevIn)
            {
                output.Add(intersect(prev, cur));
            }
            prev = cur;
        }
        return output;
    }

    private static GfxPoint IntersectX(GfxPoint s, GfxPoint e, double x)
    {
        var t = (x - s.X) / (e.X - s.X);
        return new GfxPoint(x, s.Y + t * (e.Y - s.Y));
    }

    private static GfxPoint IntersectY(GfxPoint s, GfxPoint e, double y)
    {
        var t = (y - s.Y) / (e.Y - s.Y);
        return new GfxPoint(s.X + t * (e.X - s.X), y);
    }
}
