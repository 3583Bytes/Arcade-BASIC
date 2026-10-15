using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;
using ArcadeBasic.Compiler;
using ArcadeBasic.Vm;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Conformance.Tests;

/// <summary>
/// examples/maze3d.bas, stepped the way the browser playground steps it, on a
/// 160 x 96-dot screen with keys the test holds down (KEYDOWN) or types (INKEY$).
/// </summary>
public class Maze3dTests
{
    /// <summary>A screen that keeps each presented frame's filled areas (with their
    /// colour) and text, and a keyboard the test drives.</summary>
    private sealed class Screen : IGraphicsDevice, IKeyboard, IKeyState
    {
        private readonly List<(int Color, GfxPoint[] Polygon)> _areas = [];
        private readonly List<string> _texts = [];
        private int _areaColor = 1;
        public List<(int Color, GfxPoint[] Polygon)> Areas { get; private set; } = [];
        public List<string> Texts { get; private set; } = [];
        public readonly HashSet<string> Held = [];
        public readonly Queue<string> Typed = new();

        public void Clear()
        {
            _areas.Clear();
            _texts.Clear();
        }

        public void FillArea(IReadOnlyList<GfxPoint> polygon) => _areas.Add((_areaColor, [.. polygon]));
        public void DrawText(GfxPoint at, string text) => _texts.Add(text);

        public void Flush()
        {
            Areas = [.. _areas];
            Texts = [.. _texts];
        }

        public void SetColor(GfxColorTarget target, int colorIndex)
        {
            if (target == GfxColorTarget.Area) _areaColor = colorIndex;
        }

        public string ReadKey() => Typed.Count > 0 ? Typed.Dequeue() : "";
        public bool IsKeyDown(string key) => Held.Contains(key);

        public void SetLineStyle(int style) { }
        public void SetPointStyle(int style) { }
        public void DrawPoints(IReadOnlyList<GfxPoint> points) { }
        public void DrawLines(IReadOnlyList<GfxPoint> polyline) { }
        public int MaxColor => 15;
        public int MaxLineStyle => 3;
        public int MaxPointStyle => 3;
        public GfxDeviceSize DeviceSize => new(160, 96, "OTHER");

        /// <summary>The wall slices: the areas one screen column (1/160) wide that
        /// reach eye level, left to right.</summary>
        public List<(int Color, double Bottom, double Top)> Columns() =>
        [
            .. Areas
                .Where(a => Math.Abs(a.Polygon.Max(p => p.X) - a.Polygon.Min(p => p.X) - 1.0 / 160) < 1e-9)
                .Select(a => (a.Color, Bottom: a.Polygon.Min(p => p.Y), Top: a.Polygon.Max(p => p.Y), Left: a.Polygon.Min(p => p.X)))
                .Where(c => c.Bottom <= 0.5 && c.Top >= 0.5)
                .OrderBy(c => c.Left)
                .Select(c => (c.Color, c.Bottom, c.Top)),
        ];

        public double CentreHeight()
        {
            var columns = Columns();
            var (_, bottom, top) = columns[columns.Count / 2];
            return top - bottom;
        }
    }

    /// <summary>Compile maze3d.bas, with <paramref name="edits"/> (old → new text)
    /// applied to its source first.</summary>
    private static BasicVm Start(Screen screen, params (string Old, string New)[] edits)
    {
        var source = File.ReadAllText(Path.Combine(ExamplesDir(), "maze3d.bas"));
        foreach (var (old, @new) in edits)
        {
            Assert.Contains(old, source);
            source = source.Replace(old, @new);
        }
        var file = new SourceFile("maze3d.bas", source);
        var diags = new DiagnosticBag();
        var program = new BasicParser(new BasicLexer(file, diags).Lex(), file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        Assert.False(diags.HasErrors);
        Assert.Empty(diags.All);   // no warnings either
        return new BasicVm(BasicCompiler.Compile(program, info), new StringWriter(), new LineInputQueue(), screen, screen);
    }

    /// <summary>Step until the program presents its next frame (SLEEP) or ends —
    /// when, like the playground, show what it drew last.</summary>
    private static StepStatus NextFrame(BasicVm vm, Screen screen)
    {
        for (var i = 0; i < 1000; i++)
        {
            var status = vm.Step(100_000).Status;
            if (status == StepStatus.Halted) screen.Flush();
            if (status is StepStatus.Sleeping or StepStatus.Halted) return status;
        }
        throw new InvalidOperationException("no frame");
    }

    private static void Frames(BasicVm vm, Screen screen, int count)
    {
        for (var i = 0; i < count; i++) Assert.Equal(StepStatus.Sleeping, NextFrame(vm, screen));
    }

    [Fact]
    public void EachScreenColumnIsASliceOfWall()
    {
        var screen = new Screen();
        var vm = Start(screen);
        NextFrame(vm, screen);

        // You start facing east down a corridor: a lit wall ahead (bright blue), the
        // corridor's sides shaded (dark blue) — one slice per screen column.
        var columns = screen.Columns();
        Assert.Equal(160, columns.Count);
        Assert.Equal(4, columns[80].Color);
        Assert.Equal(11, columns[0].Color);
        Assert.Equal(11, columns[^1].Color);
        Assert.True(columns[80].Top - columns[80].Bottom < columns[0].Top - columns[0].Bottom);   // ahead is farther
        Assert.Contains(screen.Texts, t => t == "TIME 90");
    }

    [Fact]
    public void WalkingTowardAWallMakesItTaller()
    {
        var screen = new Screen();
        var vm = Start(screen);
        NextFrame(vm, screen);
        var before = screen.CentreHeight();

        screen.Held.Add("w");                          // 20 frames x 0.12: 4.5 tiles away -> 2.1
        Frames(vm, screen, 20);
        Assert.True(screen.CentreHeight() > before * 2, $"{before} -> {screen.CentreHeight()}");

        // Back up as far, and it's as it was.
        screen.Held.Clear();
        screen.Held.Add("ArrowDown");
        Frames(vm, screen, 20);
        Assert.Equal(before, screen.CentreHeight(), 6);
    }

    [Fact]
    public void TurningRightFacesSouthDownTheOtherCorridor()
    {
        var screen = new Screen();
        var vm = Start(screen);
        NextFrame(vm, screen);
        Assert.Equal(4, screen.Columns()[80].Color);   // ahead, east: a lit east/west face

        // A quarter turn (20 frames of 0.08 rad): facing south down column 1 of the
        // map, the wall at its end is a north/south face, so it's shaded.
        screen.Held.Add("d");
        Frames(vm, screen, 20);
        Assert.Equal(11, screen.Columns()[80].Color);
    }

    [Fact]
    public void WalkingIntoTheGreenDoorEscapes()
    {
        // Start beside the door (row 14, column 15), facing it.
        var screen = new Screen();
        var vm = Start(screen, ("LET PX = 1.5\nLET PY = 1.5", "LET PX = 13.5\nLET PY = 14.5"));
        NextFrame(vm, screen);
        Assert.Equal(3, screen.Columns()[80].Color);   // green, ahead

        screen.Held.Add("w");                          // a held key: there is a keyboard, so it asks R / Q
        for (var i = 0; i < 40 && !screen.Texts.Contains("YOU ESCAPED!"); i++) NextFrame(vm, screen);
        Assert.Contains("YOU ESCAPED!", screen.Texts);

        screen.Held.Clear();
        screen.Typed.Enqueue("q");
        Assert.Equal(StepStatus.Halted, NextFrame(vm, screen));
    }

    [Fact]
    public void WithNoKeyboardTheClockRunsOutAndItEnds()
    {
        var screen = new Screen();
        var vm = Start(screen, ("LET LIMIT = 90 ", "LET LIMIT = 2 "));
        var status = StepStatus.Sleeping;
        for (var i = 0; i < 100 && status != StepStatus.Halted; i++) status = NextFrame(vm, screen);
        Assert.Equal(StepStatus.Halted, status);       // no prompt: nobody pressed a key
        Assert.Contains("OUT OF TIME", screen.Texts);
    }

    [Fact]
    public void MTogglesTheMinimapAndQQuits()
    {
        var screen = new Screen();
        var vm = Start(screen);
        NextFrame(vm, screen);
        var withMap = screen.Areas.Count;

        screen.Typed.Enqueue("m");
        NextFrame(vm, screen);
        Assert.True(screen.Areas.Count < withMap);
        Assert.Equal(160, screen.Columns().Count);

        screen.Typed.Enqueue("q");
        Assert.Equal(StepStatus.Halted, NextFrame(vm, screen));
    }

    private static string ExamplesDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "examples"))) dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir ?? throw new DirectoryNotFoundException("examples"), "examples");
    }
}
