using System.Globalization;
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
    public static double ToCoord(BigDecimal v) =>
        double.Parse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture);

    /// <summary>Round an evaluated numeric value to a style/colour index.</summary>
    public static int ToIndex(BigDecimal v) =>
        (int)BigDecimal.Round(v, 0, RoundingMode.MidpointToEven);

    /// <summary>Convert a drawing coordinate back to a BASIC numeric value (for ASK).</summary>
    public static BigDecimal FromCoord(double v) =>
        BigDecimal.Parse(v.ToString("R", CultureInfo.InvariantCulture));

    /// <summary>
    /// Compute the value an ASK statement reads for the given object and target
    /// index. Both engines call this, so they assign identical values. Indices:
    /// rectangles 0..3 = left/right/bottom/top; DEVICE SIZE 0/1/2 = width/height/unit$.
    /// </summary>
    public Value Query(GfxQuery q, int index, IGraphicsDevice dev) => q switch
    {
        GfxQuery.Window => RectComponent(Window, index),
        GfxQuery.Viewport => RectComponent(Viewport, index),
        GfxQuery.DeviceWindow => RectComponent(DeviceWindow, index),
        GfxQuery.DeviceViewport => RectComponent(DeviceViewport, index),
        GfxQuery.DeviceSize => index switch
        {
            0 => Num(dev.DeviceSize.Width),
            1 => Num(dev.DeviceSize.Height),
            _ => new StringValue(dev.DeviceSize.Unit),
        },
        GfxQuery.Clip => new StringValue(ClipEnabled ? "ON" : "OFF"),
        GfxQuery.PointStyle => Num(PointStyle),
        GfxQuery.LineStyle => Num(LineStyle),
        GfxQuery.PointColor => Num(PointColor),
        GfxQuery.LineColor => Num(LineColor),
        GfxQuery.TextColor => Num(TextColor),
        GfxQuery.AreaColor => Num(AreaColor),
        GfxQuery.MaxColor => Num(dev.MaxColor),
        GfxQuery.MaxPointStyle => Num(dev.MaxPointStyle),
        GfxQuery.MaxLineStyle => Num(dev.MaxLineStyle),
        _ => Num(0),
    };

    private static Value RectComponent(GfxRect r, int i) =>
        Num(i switch { 0 => r.Left, 1 => r.Right, 2 => r.Bottom, _ => r.Top });

    private static Value Num(double v) => new NumericValue(FromCoord(v));

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

    // -- Transforms ------------------------------------------------------

    private GfxPoint ToNdc(GfxPoint p) => new(
        Remap(p.X, Window.Left, Window.Right, Viewport.Left, Viewport.Right),
        Remap(p.Y, Window.Bottom, Window.Top, Viewport.Bottom, Viewport.Top));

    private GfxPoint ToSurface(GfxPoint ndc) => new(
        Remap(ndc.X, DeviceWindow.Left, DeviceWindow.Right, DeviceViewport.Left, DeviceViewport.Right),
        Remap(ndc.Y, DeviceWindow.Bottom, DeviceWindow.Top, DeviceViewport.Bottom, DeviceViewport.Top));

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
