using System.Globalization;
using System.Text;

namespace ArcadeBasic.Runtime;

/// <summary>
/// An <see cref="IGraphicsDevice"/> that records every primitive call as a
/// deterministic text transcript instead of drawing anything. It is the basis
/// of the engine-parity tests: running a program on the interpreter and on the
/// VM must yield identical transcripts. Coordinates are formatted at fixed
/// precision so the text is stable across platforms.
/// </summary>
public sealed class RecordingGraphicsDevice : IGraphicsDevice
{
    private readonly StringBuilder _log = new();

    public int MaxColor { get; }
    public int MaxLineStyle { get; }
    public int MaxPointStyle { get; }
    public GfxDeviceSize DeviceSize { get; }

    public RecordingGraphicsDevice(
        int maxColor = 15, int maxLineStyle = 3, int maxPointStyle = 3,
        double width = 1.0, double height = 1.0, string unit = "OTHER")
    {
        MaxColor = maxColor;
        MaxLineStyle = maxLineStyle;
        MaxPointStyle = maxPointStyle;
        DeviceSize = new GfxDeviceSize(width, height, unit);
    }

    public string Transcript => _log.ToString();

    public void Clear() => _log.Append("CLEAR\n");
    public void SetLineStyle(int style) => _log.Append("LINE STYLE ").Append(style).Append('\n');
    public void SetPointStyle(int style) => _log.Append("POINT STYLE ").Append(style).Append('\n');
    public void SetColor(GfxColorTarget target, int colorIndex) =>
        _log.Append(target.ToString().ToUpperInvariant()).Append(" COLOR ").Append(colorIndex).Append('\n');

    public void DrawPoints(IReadOnlyList<GfxPoint> points) => Emit("POINTS", points);
    public void DrawLines(IReadOnlyList<GfxPoint> polyline) => Emit("LINES", polyline);
    public void FillArea(IReadOnlyList<GfxPoint> polygon) => Emit("AREA", polygon);

    public void DrawText(GfxPoint at, string text) =>
        _log.Append("TEXT ").Append(Fmt(at)).Append(" \"").Append(text).Append("\"\n");

    public void Flush() => _log.Append("FLUSH\n");

    private void Emit(string verb, IReadOnlyList<GfxPoint> pts)
    {
        _log.Append(verb);
        foreach (var p in pts) _log.Append(' ').Append(Fmt(p));
        _log.Append('\n');
    }

    private static string Fmt(GfxPoint p) =>
        p.X.ToString("F4", CultureInfo.InvariantCulture) + "," +
        p.Y.ToString("F4", CultureInfo.InvariantCulture);
}
