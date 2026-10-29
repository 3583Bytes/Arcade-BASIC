using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;
using ArcadeBasic.Compiler;
using ArcadeBasic.Vm;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Conformance.Tests;

/// <summary>
/// LOCATE POINT / GET POINT and GET MOUSE, given a scripted pointer: picks come back in window coordinates (picks outside the
/// window are ignored), and GET MOUSE reports each button edge once.
/// </summary>
public class PointerTests
{
    /// <summary>A surface whose scripted button events arrive one at a time, each only
    /// after the program has found nothing waiting — like a user clicking after the
    /// program asks.</summary>
    private sealed class ScriptedPointer(GfxPoint position, params PointerEvent[] script) : IGraphicsDevice, IPointer
    {
        private readonly Queue<PointerEvent> _script = new(script);
        private readonly Queue<PointerEvent> _arrived = new();

        public bool TryGetPosition(out GfxPoint p) { p = position; return true; }

        public bool TryReadEvent(out PointerEvent e)
        {
            if (_arrived.TryDequeue(out e)) return true;
            if (_script.TryDequeue(out var next)) _arrived.Enqueue(next);
            return false;
        }

        public void Clear() { }
        public void SetLineStyle(int style) { }
        public void SetPointStyle(int style) { }
        public void SetColor(GfxColorTarget target, int colorIndex) { }
        public void DrawPoints(IReadOnlyList<GfxPoint> points) { }
        public void DrawLines(IReadOnlyList<GfxPoint> polyline) { }
        public void FillArea(IReadOnlyList<GfxPoint> polygon) { }
        public void DrawText(GfxPoint at, string text) { }
        public void Flush() { }
        public int MaxColor => 15;
        public int MaxLineStyle => 3;
        public int MaxPointStyle => 3;
        public GfxDeviceSize DeviceSize => new(1, 1, "OTHER");
    }

    private static PointerEvent Press(double x, double y) => new(true, new GfxPoint(x, y));
    private static PointerEvent Release(double x, double y) => new(false, new GfxPoint(x, y));

    private static string Run(string source, Func<IGraphicsDevice> screen, string stdin = "")
    {
        var file = new SourceFile("pointer.bas", source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        Assert.False(diags.HasErrors, string.Join("\n", diags.All.Select(d => d.Render(false))));

        var output = new StringWriter { NewLine = "\n" };
        new BasicVm(BasicCompiler.Compile(program, info), output, new StringReader(stdin), screen()).Run();
        return output.ToString();
    }

    [Fact]
    public void LocatePointReturnsThePickInWindowCoordinates()
    {
        // The window fills the right half of the surface; a pick in the left half is outside it.
        var output = Run(
            "DIM A(3)\nSET WINDOW 0, 100, 0, 50\nSET VIEWPORT 0.5, 1, 0, 1\nLOCATE POINT : X, Y\nPRINT X; Y\nGET POINT, AT 1, 2: A(1), B\nPRINT A(1); B\n",
            () => new ScriptedPointer(default,
                Press(0.25, 0.2), Release(0.25, 0.2),      // outside the window: ignored
                Press(0.75, 0.2), Release(0.75, 0.2),      // (50, 10)
                Press(1.0, 1.0)));                         // (100, 50), for GET POINT
        Assert.Equal(" 50  10 \n 100  50 \n", output);
    }

    [Fact]
    public void GetMouseReportsEachButtonEdgeOnce()
    {
        var output = Run(
            "SET WINDOW 0, 100, 0, 50\nFOR I = 1 TO 6\nGET MOUSE X, Y, S\nPRINT S; X; Y\nNEXT I\n",
            () => new ScriptedPointer(new GfxPoint(0.1, 0.1), Press(0.5, 0.5), Release(0.6, 0.6)));
        Assert.Equal(
            " 0  10  5 \n" +      // nothing yet: up, where the pointer is
            " 2  50  25 \n" +     // pressed, where it was pressed
            " 1  10  5 \n" +      // held
            " 3  60  30 \n" +     // released, where it was released
            " 0  10  5 \n 0  10  5 \n", output);
    }

    [Fact]
    public void WithoutAPointerLocateReadsTypedCoordinatesAndGetMouseSaysSo()
    {
        var output = Run(
            "GET MOUSE X, Y, S\nPRINT S\nLOCATE POINT : X, Y\nPRINT X; Y\n",
            () => NullGraphicsDevice.Instance, stdin: "3, 4\n");
        Assert.Equal("-1 \n?  3  4 \n", output);
    }

    [Fact]
    public void AnyDeviceButTheFirstIsAnError()
    {
        var output = Run(
            "WHEN EXCEPTION IN\nLOCATE POINT(2): X, Y\nUSE\nPRINT EXTYPE\nEND WHEN\n",
            () => new ScriptedPointer(default));
        Assert.Equal(" 11140 \n", output);
    }
}
