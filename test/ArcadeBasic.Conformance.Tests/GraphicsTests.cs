using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;
using ArcadeBasic.Interpreter;
using ArcadeBasic.Compiler;
using ArcadeBasic.Vm;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Conformance.Tests;

/// <summary>
/// ECMA-116 §13 graphics: correctness of the shared coordinate/clip engine and
/// byte-for-byte parity between the tree-walker and the VM, observed through a
/// <see cref="RecordingGraphicsDevice"/> (the device-coordinate primitive stream
/// both engines must produce identically).
/// </summary>
public class GraphicsTests
{
    private static (string Interp, string Vm) Both(string source)
    {
        var file = new SourceFile("gfx.bas", source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        Assert.False(diags.HasErrors, string.Join("\n", diags.All.Select(d => d.Render(false))));

        var ir = new RecordingGraphicsDevice();
        new BasicInterpreter(program, info, new StringWriter(), TextReader.Null, default, ir).Run();

        var vr = new RecordingGraphicsDevice();
        new BasicVm(BasicCompiler.Compile(program, info), new StringWriter(), TextReader.Null, vr).Run();

        return (ir.Transcript, vr.Transcript);
    }

    [Fact]
    public void WindowToViewportMapping()
    {
        var (i, v) = Both("""
            SET WINDOW 0, 10, 0, 10
            GRAPH LINES: 0, 0; 10, 10
            """);
        Assert.Equal(i, v);
        // (0,0) and (10,10) map to the NDC corners (Y up).
        Assert.Contains("LINES 0.0000,0.0000 1.0000,1.0000", i);
    }

    [Fact]
    public void LineClippedToViewport()
    {
        var (i, v) = Both("""
            SET WINDOW 0, 10, 0, 10
            SET VIEWPORT 0, 0.5, 0, 0.5
            GRAPH LINES: -5, 5; 15, 5
            """);
        Assert.Equal(i, v);
        // The horizontal line is clipped to the viewport's x-range [0, 0.5].
        Assert.Contains("LINES 0.0000,0.2500 0.5000,0.2500", i);
    }

    [Fact]
    public void StyleColorClearAndPrimitivesAgree()
    {
        var (i, v) = Both("""
            SET WINDOW 0, 1, 0, 1
            SET LINE COLOR 4
            SET LINE STYLE 2
            CLEAR
            GRAPH LINES: 0, 0; 1, 1
            SET AREA COLOR 2
            GRAPH AREA: 0, 0; 1, 0; 0.5, 1
            GRAPH POINTS: 0.5, 0.5
            """);
        Assert.Equal(i, v);
        Assert.Contains("LINE COLOR 4", i);
        Assert.Contains("LINE STYLE 2", i);
        Assert.Contains("CLEAR", i);
        Assert.Contains("AREA COLOR 2", i);
        Assert.Contains("POINTS 0.5000,0.5000", i);
    }

    [Fact]
    public void AskRoundTripsThroughBothEngines()
    {
        // ASK reads back into variables; the program prints them so we can also
        // compare stdout parity in passing. Here we just assert the draw that
        // uses the queried values is identical on both engines.
        var (i, v) = Both("""
            SET WINDOW 0, 200, 0, 100
            ASK WINDOW A, B, C, D
            GRAPH POINTS: A, C; B, D
            """);
        Assert.Equal(i, v);
        // (0,0) → (0,0); (200,100) → (1,1) in NDC.
        Assert.Contains("POINTS 0.0000,0.0000 1.0000,1.0000", i);
    }

    [Fact]
    public void ExampleProgramRendersIdenticallyOnBothEngines()
    {
        var path = Path.Combine(ExamplesDir(), "graphics.bas");
        var (i, v) = Both(File.ReadAllText(path));
        Assert.Equal(i, v);
        Assert.NotEmpty(i);
        Assert.Contains("LINES", i);   // the sine curve and axes
        Assert.Contains("AREA", i);    // the filled triangle
        Assert.Contains("TEXT", i);    // the label
    }

    [Fact]
    public void InvadersRendersIdenticallyOnBothEngines()
    {
        // The game is driven by INKEY$; feeding both engines the same scripted
        // key sequence must yield byte-identical primitive streams (no RND, so it
        // is deterministic). Also exercises SLEEP on both engines.
        var path = Path.Combine(ExamplesDir(), "invaders.bas");
        var source = File.ReadAllText(path);
        var file = new SourceFile("invaders.bas", source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        Assert.False(diags.HasErrors, string.Join("\n", diags.All.Select(d => d.Render(false))));

        // idle, fire, idle, left, idle, right, idle, quit
        string[] script = { "", " ", "", "a", "", "d", "", "q" };

        // The game persists a high score to invaders.score on quit; start both
        // engines from the same (absent) state so the run is deterministic.
        try { File.Delete("invaders.score"); } catch { /* ignore */ }

        var ir = new RecordingGraphicsDevice();
        var iExit = new BasicInterpreter(program, info, new StringWriter(), TextReader.Null, default, ir, new ScriptKeyboard(script)).Run();

        try { File.Delete("invaders.score"); } catch { /* ignore */ }

        var vr = new RecordingGraphicsDevice();
        var vExit = new BasicVm(BasicCompiler.Compile(program, info), new StringWriter(), TextReader.Null, vr, new ScriptKeyboard(script)).Run();

        try { File.Delete("invaders.score"); } catch { /* ignore */ }

        Assert.Equal(0, iExit);
        Assert.Equal(0, vExit);
        Assert.Equal(ir.Transcript, vr.Transcript);
        Assert.NotEmpty(ir.Transcript);
        Assert.Contains("AREA", ir.Transcript);   // aliens + ship are filled areas
        Assert.Contains("TEXT", ir.Transcript);    // the HUD
    }

    private sealed class ScriptKeyboard : IKeyboard
    {
        private readonly Queue<string> _keys;
        public ScriptKeyboard(IEnumerable<string> keys) => _keys = new Queue<string>(keys);
        public string ReadKey() => _keys.Count > 0 ? _keys.Dequeue() : "";
    }

    private static string ExamplesDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "examples");
            if (File.Exists(Path.Combine(candidate, "graphics.bas"))) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("could not locate examples/ directory");
    }
}
