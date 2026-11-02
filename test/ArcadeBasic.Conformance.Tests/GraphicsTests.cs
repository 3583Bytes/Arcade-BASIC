using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;
using ArcadeBasic.Compiler;
using ArcadeBasic.Vm;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Conformance.Tests;

/// <summary>
/// ECMA-116 §13 graphics: correctness of the coordinate/clip engine, observed through a
/// <see cref="RecordingGraphicsDevice"/> (the device-coordinate primitive stream).
/// </summary>
[Collection(ScoreFiles.Collection)]
public class GraphicsTests
{
    private static string Draw(string source) => DrawAndPrint(source).Drawing;

    /// <summary>What the program draws (on a 1 x 1 surface whose text is 1/24 of it high, a
    /// monospace character 0.6 of that wide) and what it prints.</summary>
    private static (string Drawing, string Printed) DrawAndPrint(string source)
    {
        var file = new SourceFile("gfx.bas", source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        Assert.False(diags.HasErrors, string.Join("\n", diags.All.Select(d => d.Render(false))));

        var recorder = new RecordingGraphicsDevice();
        var printed = new StringWriter { NewLine = "\n" };   // the same on every OS
        new BasicVm(BasicCompiler.Compile(program, info), printed, TextReader.Null, recorder).Run();
        return (recorder.Transcript, printed.ToString());
    }

    // -- Text attributes, TEXTWIDTH, rounded corners, opacity ---------------------

    [Fact]
    public void TextHeightAndFontAreTheDevices()
    {
        var (drawing, printed) = DrawAndPrint("""
            SET WINDOW 0, 10, 0, 20
            ASK TEXT HEIGHT H
            PRINT H
            SET TEXT HEIGHT 2
            SET TEXT FONT "sans bold"
            GRAPH TEXT, AT 1, 1: "big"
            ASK TEXT HEIGHT H
            ASK TEXT FONT F$
            PRINT H; F$
            SET TEXT HEIGHT 0
            SET TEXT FONT "MONO"
            GRAPH TEXT, AT 1, 1: "usual"
            """);
        Assert.Equal(" 0.83333333 \n 2 SANS BOLD\n", printed);   // the usual 1/24 of the surface: 20/24 units
        Assert.Contains("TEXT STYLE 0.1000 SANS BOLD\nTEXT 0.1000,0.0500 \"big\"", drawing);   // 2 of 20 units: 0.1 of the surface
        Assert.Contains("TEXT STYLE 0.0000 MONO\nTEXT 0.1000,0.0500 \"usual\"", drawing);
    }

    [Fact]
    public void JustifyPutsThePointWhereItSaysOnTheText()
    {
        // "abcd" at height 1 of 10 is 4 x 0.6 x 0.1 = 0.24 of the surface across.
        var (drawing, printed) = DrawAndPrint("""
            SET WINDOW 0, 10, 0, 10
            SET TEXT HEIGHT 1
            PRINT TEXTWIDTH("abcd")
            SET TEXT JUSTIFY "center", "half"
            GRAPH TEXT, AT 5, 5: "abcd"
            SET TEXT JUSTIFY "RIGHT", "TOP"
            GRAPH TEXT, AT 5, 5: "abcd"
            SET TEXT JUSTIFY "MIDDLE", "BOTTOM"
            GRAPH TEXT, AT 5, 5: "abcd"
            ASK TEXT JUSTIFY H$, V$
            PRINT H$; " "; V$
            """);
        Assert.Equal(" 2.4 \nRIGHT BOTTOM\n", printed);   // (MIDDLE isn't one: RIGHT stays)
        Assert.Contains("TEXT 0.3800,0.4650 \"abcd\"", drawing);   // half the width left, the middle of a capital down
        Assert.Contains("TEXT 0.2600,0.4200 \"abcd\"", drawing);   // all of it left, 0.8 of the height down
        Assert.Contains("TEXT 0.2600,0.5200 \"abcd\"", drawing);   // and 0.2 up
    }

    [Fact]
    public void RoundCornersAreArcs()
    {
        var drawing = Draw("""
            SET WINDOW 0, 10, 0, 10
            GRAPH AREA, ROUND 1: 0, 0; 10, 0; 10, 10; 0, 10
            GRAPH LINES, ROUND 100: 0, 0; 10, 0; 10, 10
            """);
        var lines = drawing.Split('\n');
        var area = lines.Single(l => l.StartsWith("AREA ")).Split(' ').Skip(1).ToList();
        Assert.Equal(4 * 11, area.Count);                        // a quarter circle of 10 steps at each corner
        Assert.Equal("0.0000,0.1000", area[0]);                  // the first corner's arc starts up its left edge
        Assert.Equal("0.1000,0.0000", area[10]);                 // and ends along its bottom one
        Assert.DoesNotContain("0.0000,0.0000", area);            // no corner is left square
        var line = lines.Single(l => l.StartsWith("LINES ")).Split(' ').Skip(1).ToList();
        Assert.Equal(["0.0000,0.0000", "0.5000,0.0000"], line.Take(2));   // a radius too big for the edges is half of them
        Assert.Equal("1.0000,1.0000", line[^1]);                 // the ends stay where they are
    }

    [Fact]
    public void AColourCanBeSeeThrough()
    {
        var (drawing, printed) = DrawAndPrint("""
            SET COLOR MIX (9) 0, 0, 0, 0.25
            SET COLOR MIX (10) 1, 0, 0
            ASK COLOR MIX (9) R, G, B, A
            ASK COLOR MIX (10) R, G, B, A2
            PRINT A; A2
            """);
        Assert.Equal(" 0.25  1 \n", printed);
        Assert.Contains("COLOR MIX 9 0.0000 0.0000 0.0000 OPACITY 0.2500\n", drawing);
        Assert.Contains("COLOR MIX 10 1.0000 0.0000 0.0000\n", drawing);
    }

    [Fact]
    public void WindowToViewportMapping()
    {
        var drawing = Draw("""
            SET WINDOW 0, 10, 0, 10
            GRAPH LINES: 0, 0; 10, 10
            """);
        // (0,0) and (10,10) map to the NDC corners (Y up).
        Assert.Contains("LINES 0.0000,0.0000 1.0000,1.0000", drawing);
    }

    [Fact]
    public void LineClippedToViewport()
    {
        var drawing = Draw("""
            SET WINDOW 0, 10, 0, 10
            SET VIEWPORT 0, 0.5, 0, 0.5
            GRAPH LINES: -5, 5; 15, 5
            """);
        // The horizontal line is clipped to the viewport's x-range [0, 0.5].
        Assert.Contains("LINES 0.0000,0.2500 0.5000,0.2500", drawing);
    }

    [Fact]
    public void StyleColorClearAndPrimitivesAgree()
    {
        var drawing = Draw("""
            SET WINDOW 0, 1, 0, 1
            SET LINE COLOR 4
            SET LINE STYLE 2
            CLEAR
            GRAPH LINES: 0, 0; 1, 1
            SET AREA COLOR 2
            GRAPH AREA: 0, 0; 1, 0; 0.5, 1
            GRAPH POINTS: 0.5, 0.5
            """);
        Assert.Contains("LINE COLOR 4", drawing);
        Assert.Contains("LINE STYLE 2", drawing);
        Assert.Contains("CLEAR", drawing);
        Assert.Contains("AREA COLOR 2", drawing);
        Assert.Contains("POINTS 0.5000,0.5000", drawing);
    }

    [Fact]
    public void AskReadsTheWindowBack()
    {
        // ASK reads back into variables, which the drawing then uses.
        var drawing = Draw("""
            SET WINDOW 0, 200, 0, 100
            ASK WINDOW A, B, C, D
            GRAPH POINTS: A, C; B, D
            """);
        // (0,0) → (0,0); (200,100) → (1,1) in NDC.
        Assert.Contains("POINTS 0.0000,0.0000 1.0000,1.0000", drawing);
    }

    [Fact]
    public void TheGraphicsExampleDraws()
    {
        var path = Path.Combine(ExamplesDir(), "graphics.bas");
        var drawing = Draw(File.ReadAllText(path));
        Assert.NotEmpty(drawing);
        Assert.Contains("LINES", drawing);   // the sine curve and axes
        Assert.Contains("AREA", drawing);    // the filled triangle
        Assert.Contains("TEXT", drawing);    // the label
    }

    [Fact]
    public void InvadersPlaysWithScriptedKeys()
    {
        // The game is driven by INKEY$ and KEYDOWN: a scripted key sequence plays it
        // through to the quit (no RND, so it is deterministic), through SLEEP.
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

        // The game persists a high score to invaders.score on quit; start from
        // none, so the run is deterministic.
        try { File.Delete("invaders.score"); } catch { /* ignore */ }

        var recorder = new RecordingGraphicsDevice();
        var exit = new BasicVm(BasicCompiler.Compile(program, info), new StringWriter(), TextReader.Null, recorder, new ScriptKeyboard(script)).Run();

        try { File.Delete("invaders.score"); } catch { /* ignore */ }

        Assert.Equal(0, exit);
        Assert.Contains("AREA", recorder.Transcript);   // aliens + ship are filled areas
        Assert.Contains("TEXT", recorder.Transcript);    // the HUD
    }

    /// <summary>Scripted input, one entry per INKEY$ read ("" ends a frame's drain).
    /// A scripted key also counts as <i>held</i> for the frame it arrives in — the game
    /// drains INKEY$ (the key, then the "" that ends the frame) before it asks KEYDOWN —
    /// so held-key movement is as deterministic as the presses.</summary>
    private sealed class ScriptKeyboard : IKeyboard, IKeyState
    {
        private readonly Queue<string> _keys;
        private string _last = "", _beforeLast = "";
        public ScriptKeyboard(IEnumerable<string> keys) => _keys = new Queue<string>(keys);

        public string ReadKey()
        {
            var key = _keys.Count > 0 ? _keys.Dequeue() : "";
            (_beforeLast, _last) = (_last, key);
            return key;
        }

        public bool IsKeyDown(string key) => Is(_last, key) || Is(_beforeLast, key);

        private static bool Is(string pressed, string key) =>
            KeyNames.Canonical(pressed) is { } p && (p == key || p == KeyNames.AsTyped(key));
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
