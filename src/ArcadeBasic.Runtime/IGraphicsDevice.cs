namespace ArcadeBasic.Runtime;

/// <summary>A point in the normalized device unit square — see <see cref="IGraphicsDevice"/>.</summary>
public readonly record struct GfxPoint(double X, double Y);

/// <summary>Reported by <see cref="IGraphicsDevice.DeviceSize"/> for <c>ASK DEVICE SIZE</c>.</summary>
public readonly record struct GfxDeviceSize(double Width, double Height, string Unit);

/// <summary>Which primitive a <c>SET … COLOR</c> applies to.</summary>
public enum GfxColorTarget { Point, Line, Text, Area }

/// <summary>What an <c>ASK</c> statement queries. The ordinal values must stay
/// in lock-step with the parser's <c>GfxAskObject</c> enum — both engines cast
/// between them by ordinal when lowering/executing ASK.</summary>
public enum GfxQuery
{
    Window, Viewport, DeviceWindow, DeviceViewport, DeviceSize, Clip,
    PointStyle, LineStyle, MaxPointStyle, MaxLineStyle,
    PointColor, LineColor, TextColor, AreaColor, MaxColor,
}

/// <summary>
/// The backend seam for the ECMA-116 §13 graphics module. The device-independent
/// core (<see cref="GraphicsState"/>) performs all coordinate mapping and
/// clipping, then hands the backend <b>already-clipped vector primitives in the
/// normalized device unit square</b> <c>[0,1] × [0,1]</c>, origin bottom-left
/// (BASIC convention, Y increases upward). Backends scale that square to their
/// own surface (texture pixels, terminal cells, SVG viewBox) and flip Y as
/// needed.
///
/// Implementations must stay netstandard2.1- and IL2CPP-safe: no reflection, no
/// dynamic codegen, only simple value types across the boundary.
/// </summary>
public interface IGraphicsDevice
{
    /// <summary>Clear the surface to its background colour.</summary>
    void Clear();

    void SetLineStyle(int style);
    void SetPointStyle(int style);
    void SetColor(GfxColorTarget target, int colorIndex);

    /// <summary>Draw a point marker (current POINT STYLE/COLOR) at each point.</summary>
    void DrawPoints(IReadOnlyList<GfxPoint> points);

    /// <summary>Draw connected line segments through the points (current LINE STYLE/COLOR).
    /// The core may call this more than once per source statement when clipping
    /// fragments a polyline into disjoint runs.</summary>
    void DrawLines(IReadOnlyList<GfxPoint> polyline);

    /// <summary>Fill a polygon (current AREA COLOR), edges closed automatically.</summary>
    void FillArea(IReadOnlyList<GfxPoint> polygon);

    /// <summary>Draw a text label with its initial point at <paramref name="at"/>.</summary>
    void DrawText(GfxPoint at, string text);

    /// <summary>Present any buffered drawing (end of program, or between frames).</summary>
    void Flush();

    // -- Capabilities, surfaced by ASK … --------------------------------
    int MaxColor { get; }
    int MaxLineStyle { get; }
    int MaxPointStyle { get; }
    GfxDeviceSize DeviceSize { get; }
}

/// <summary>
/// Optional: a device that draws <c>MAT GRAPH CELLS</c> itself (as an image). Without it,
/// <see cref="GraphicsState"/> fills each run of same-coloured cells with
/// <see cref="IGraphicsDevice.FillArea"/>. New capabilities are separate interfaces so
/// existing <see cref="IGraphicsDevice"/> implementations keep compiling.
/// </summary>
public interface ICellArrayDevice
{
    /// <summary>Fill <paramref name="rect"/> (unit square, origin bottom-left) with
    /// <paramref name="columns"/> × <paramref name="rows"/> equal cells. Colours are
    /// row-major starting from the top-left cell, and -1 means see-through: leave what's
    /// there. Draw only the part inside <paramref name="visible"/>, which lies within
    /// <paramref name="rect"/>.</summary>
    void DrawCells(GfxRect rect, int columns, int rows, int[] colors, GfxRect visible);
}

/// <summary>Optional: a device whose colours <c>SET COLOR MIX</c> can change.</summary>
public interface IColorMixDevice
{
    /// <summary>Colour <paramref name="index"/> (0 to MaxColor) is now red, green and
    /// blue at these intensities, each 0–1.</summary>
    void SetColorMix(int index, double red, double green, double blue);
}
