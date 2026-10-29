using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;
using ArcadeBasic.Compiler;
using ArcadeBasic.Vm;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Conformance.Tests;

/// <summary>
/// MAT GRAPH CELLS (ANSI X3.113 §13.3.4.4) and SET / ASK COLOR MIX (§13.2): what a
/// program draws and prints.
/// </summary>
public class CellArrayTests
{
    /// <summary>A device that can only fill areas — no <see cref="ICellArrayDevice"/>.</summary>
    private sealed class AreasOnly : IGraphicsDevice
    {
        public readonly List<string> Log = [];
        public void Clear() { }
        public void SetLineStyle(int style) { }
        public void SetPointStyle(int style) { }
        public void SetColor(GfxColorTarget target, int colorIndex) => Log.Add($"{target} {colorIndex}");
        public void DrawPoints(IReadOnlyList<GfxPoint> points) { }
        public void DrawLines(IReadOnlyList<GfxPoint> polyline) { }
        public void FillArea(IReadOnlyList<GfxPoint> polygon) =>
            Log.Add(FormattableString.Invariant($"AREA {polygon[0].X:0.##},{polygon[0].Y:0.##} {polygon[2].X:0.##},{polygon[2].Y:0.##}"));
        public void DrawText(GfxPoint at, string text) { }
        public void Flush() { }
        public int MaxColor => 255;
        public int MaxLineStyle => 3;
        public int MaxPointStyle => 3;
        public GfxDeviceSize DeviceSize => new(1, 1, "OTHER");
    }

    /// <summary>Run <paramref name="source"/> on a fresh device from <paramref name="device"/>;
    /// what it printed, and drew as <paramref name="drawing"/> reports it.</summary>
    private static (string Output, string Drawing) Run<TDevice>(string source, Func<TDevice> device, Func<TDevice, string> drawing)
        where TDevice : IGraphicsDevice
    {
        var file = new SourceFile("cells.bas", source);
        var diags = new DiagnosticBag();
        var program = new BasicParser(new BasicLexer(file, diags).Lex(), file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        Assert.False(diags.HasErrors, string.Join("\n", diags.All.Select(d => d.Render(false))));

        var output = new StringWriter { NewLine = "\n" };   // the same on every OS
        var screen = device();
        new BasicVm(BasicCompiler.Compile(program, info), output, TextReader.Null, screen).Run();
        return (output.ToString(), drawing(screen));
    }

    private static (string Output, string Transcript) Run(string source, int maxColor = 255) =>
        Run(source, () => new RecordingGraphicsDevice(maxColor), d => d.Transcript);

    /// <summary>The standard's own example: A(1,1)=5, A(2,1)=6, A(3,1)=7 along the
    /// bottom, A(1,2)=7, A(2,2)=8, A(3,2)=9 along the top.</summary>
    private const string StandardsExample = """
        OPTION BASE 1
        DIM A(3, 2)
        LET A(1, 1) = 5
        LET A(2, 1) = 6
        LET A(3, 1) = 7
        LET A(1, 2) = 7
        LET A(2, 2) = 8
        LET A(3, 2) = 9
        SET WINDOW 0, 10, 0, 5

        """;

    [Fact]
    public void TheFirstPointHoldsTheLowestSubscripts()
    {
        var (_, drawing) = Run(StandardsExample + "MAT GRAPH CELLS, IN 1, 1; 6, 2: A\n");
        // Devices get the cells row by row from the top-left: 7 8 9, then 5 6 7.
        Assert.Contains("CELLS 0.1000,0.2000 0.6000,0.4000 3x2 VISIBLE 0.1000,0.2000 0.6000,0.4000: 7 8 9 5 6 7", drawing);
    }

    [Fact]
    public void TheFirstPointCanBeAnyCorner()
    {
        // From the top-right corner: A(1,1) is there, so the top row reads backwards.
        var (_, drawing) = Run(StandardsExample + "MAT GRAPH CELLS, IN 6, 2; 1, 1: A\n");
        Assert.Contains("3x2 VISIBLE 0.1000,0.2000 0.6000,0.4000: 7 6 5 9 8 7", drawing);
    }

    [Fact]
    public void CellsAreClippedToTheViewport()
    {
        var (_, drawing) = Run(StandardsExample + "SET VIEWPORT 0, 0.3, 0, 1\nMAT GRAPH CELLS, IN 1, 1; 6, 2: A\n");
        // The window now maps onto the left 0.3, so the cells span 0.03–0.18; all visible.
        Assert.Contains("CELLS 0.0300,0.2000 0.1800,0.4000 3x2 VISIBLE 0.0300,0.2000 0.1800,0.4000:", drawing);
        var (_, cut) = Run(StandardsExample + "SET VIEWPORT 0, 0.3, 0, 1\nMAT GRAPH CELLS, IN 1, 1; 16, 2: A\n");
        Assert.Contains("CELLS 0.0300,0.2000 0.4800,0.4000 3x2 VISIBLE 0.0300,0.2000 0.3000,0.4000:", cut);
    }

    [Fact]
    public void ElementsAreRoundedAndColoursOutOfRangeAreSeeThrough()
    {
        var (_, drawing) = Run("""
            DIM A(0 TO 3, 0 TO 0)
            LET A(0, 0) = 2.6
            LET A(1, 0) = 3.4
            LET A(2, 0) = -1
            LET A(3, 0) = 300
            MAT GRAPH CELLS, IN 0, 0; 1, 1: A
            """);
        Assert.Contains("4x1 VISIBLE 0.0000,0.0000 1.0000,1.0000: 3 3 -1 -1", drawing);
    }

    [Fact]
    public void ADeviceThatOnlyFillsAreasGetsARectanglePerRunOfColour()
    {
        var (_, drawing) = Run("""
            DIM A(0 TO 3, 0 TO 1)
            LET A(0, 1) = 2
            LET A(1, 1) = 2
            LET A(2, 1) = 4
            LET A(3, 1) = 4
            SET AREA COLOR 7
            MAT GRAPH CELLS, IN 0, 0; 1, 1: A
            """, () => new AreasOnly(), d => string.Join("\n", d.Log));
        // Rows from the top (subscript 1, then 0): 2 2 4 4, then 0 0 0 0; AREA COLOR put back.
        Assert.Equal("""
            Area 7
            Area 2
            AREA 0,0.5 0.5,1
            Area 4
            AREA 0.5,0.5 1,1
            Area 0
            AREA 0,0 1,0.5
            Area 7
            """.ReplaceLineEndings("\n"), drawing);   // (a Windows checkout gives this file CRLF line endings)
    }

    [Fact]
    public void SeeThroughCellsGetNoRectangle()
    {
        var (_, drawing) = Run("""
            DIM A(0 TO 2, 0 TO 0)
            LET A(0, 0) = 2
            LET A(1, 0) = -1
            LET A(2, 0) = 2
            SET WINDOW 0, 3, 0, 1
            MAT GRAPH CELLS, IN 0, 0; 3, 1: A
            """, () => new AreasOnly(), d => string.Join("\n", d.Log));
        // Three cells across the screen: the middle one, -1, gets nothing.
        Assert.Equal("Area 2\nAREA 0,0 0.33,1\nAREA 0.67,0 1,1\nArea 1", drawing);
    }

    [Fact]
    public void ColorMixReadsBackWhatWasSetAndKeepsIntensitiesInRange()
    {
        var (output, drawing) = Run("""
            SET COLOR MIX (40) 0.2, 1.5, -1
            ASK COLOR MIX (40) R, G, B STATUS S
            PRINT R; G; B; S
            """);
        Assert.Equal(" 0.2  1  0  0 \n", output);
        Assert.Contains("COLOR MIX 40 0.2000 1.0000 0.0000", drawing);
    }

    [Fact]
    public void AskColorMixReportsTheStartingColoursAndABadIndex()
    {
        var (output, _) = Run("""
            ASK COLOR MIX (2) R, G, B
            PRINT R; G; B
            ASK COLOR MIX (21) R, G, B
            PRINT R; G; B
            ASK COLOR MIX (256) R, G, B STATUS S
            PRINT R; G; B; S
            ASK MAX COLOR M
            PRINT M
            """);
        Assert.Equal(" 1  0.33333333  0.33333333 \n 0  0  1 \n 0  0  0  11086 \n 255 \n", output);
    }

    [Fact]
    public void SetColorMixPastTheDevicesColoursIsIgnored()
    {
        var (output, drawing) = Run("""
            SET COLOR MIX (20) 1, 1, 1
            ASK COLOR MIX (20) R, G, B STATUS S
            PRINT S
            """, maxColor: 15);
        Assert.Equal(" 11086 \n", output);
        Assert.DoesNotContain("COLOR MIX", drawing);
    }

    [Fact]
    public void ARedimensionedArrayDrawsAtItsNewSize()
    {
        var (_, drawing) = Run("""
            DIM A(1, 1)
            MAT REDIM A(2, 3)
            MAT GRAPH CELLS, IN 0, 0; 1, 1: A
            """);
        Assert.Contains(" 2x3 ", drawing);
    }
}
