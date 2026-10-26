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
    /// colour), cell arrays and text, and a keyboard the test drives.</summary>
    private sealed class Screen : IGraphicsDevice, ICellArrayDevice, IKeyboard, IKeyState
    {
        private readonly List<(int Color, GfxPoint[] Polygon)> _areas = [];
        private readonly List<(GfxRect Rect, int[] Colors)> _strips = [];
        private readonly List<(GfxRect Rect, int Columns, int Rows, int[] Colors)> _planes = [];
        private readonly List<(GfxRect Rect, int[] Colors)> _sprites = [];
        private readonly List<string> _texts = [];
        private int _areaColor = 1;
        public List<(int Color, GfxPoint[] Polygon)> Areas { get; private set; } = [];
        public List<(GfxRect Rect, int[] Colors)> Strips { get; private set; } = [];
        /// <summary>The floor and the ceiling: one cell per dot, below and above eye level.</summary>
        public List<(GfxRect Rect, int Columns, int Rows, int[] Colors)> Planes { get; private set; } = [];
        /// <summary>Sprite columns: texture columns with see-through (-1) cells.</summary>
        public List<(GfxRect Rect, int[] Colors)> Sprites { get; private set; } = [];
        public List<string> Texts { get; private set; } = [];
        public readonly HashSet<string> Held = [];
        public readonly Queue<string> Typed = new();

        public void Clear()
        {
            _areas.Clear();
            _strips.Clear();
            _planes.Clear();
            _sprites.Clear();
            _texts.Clear();
        }

        public void FillArea(IReadOnlyList<GfxPoint> polygon) => _areas.Add((_areaColor, [.. polygon]));
        public void DrawText(GfxPoint at, string text) => _texts.Add(text);

        public void DrawCells(GfxRect rect, int columns, int rows, int[] colors, GfxRect visible)
        {
            if ((columns, rows) != (1, 16)) _planes.Add((rect, columns, rows, colors));
            else if (colors.Contains(-1)) _sprites.Add((rect, colors));   // a sprite's column
            else _strips.Add((rect, colors));                             // a wall's: one texture column
        }

        public void Flush()
        {
            Areas = [.. _areas];
            Strips = [.. _strips];
            Planes = [.. _planes];
            Sprites = [.. _sprites];
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
        public int MaxColor => 255;
        public int MaxLineStyle => 3;
        public int MaxPointStyle => 3;
        public GfxDeviceSize DeviceSize => new(160, 96, "OTHER");

        /// <summary>The wall slices, left to right: each one texture column (1 x 16 cells)
        /// one screen column (1/160) wide.</summary>
        public List<(GfxRect Rect, int[] Colors)> Columns()
        {
            Assert.All(Strips, s => Assert.Equal(1.0 / 160, s.Rect.Right - s.Rect.Left, 9));
            return [.. Strips.OrderBy(s => s.Rect.Left)];
        }

        public double CentreHeight()
        {
            var columns = Columns();
            var rect = columns[columns.Count / 2].Rect;
            return rect.Top - rect.Bottom;
        }

        /// <summary>Which texture the slice in the middle of the view shows (0 stone,
        /// 1 brick, 2 wood, 3 tiles, 4 the door, 5 the floor, 6 the ceiling), and whether
        /// it's the shaded copy: texture t's colours are 16 + 8t to 19 + 8t lit, the next
        /// four shaded.</summary>
        public (int Texture, bool Shaded) CentreTexture() => Texture(Columns()[80].Colors);

        public static (int Texture, bool Shaded) Texture(IEnumerable<int> colors)
        {
            var copies = colors.Select(k => (k - 16) / 4).Distinct().ToList();
            Assert.All(colors, k => Assert.InRange(k, 16, 71));
            Assert.Single(copies);                         // one texture's colours, one copy
            return (copies[0] / 2, copies[0] % 2 == 1);
        }
    }

    /// <summary>Compile maze3d.bas, with <paramref name="edits"/> (old → new text)
    /// applied to its source first.</summary>
    private static BasicVm Start(Screen screen, params (string Old, string New)[] edits)
    {
        // (A Windows checkout gives the example CRLF line endings; the edits are LF.)
        var source = File.ReadAllText(Path.Combine(ExamplesDir(), "maze3d.bas")).ReplaceLineEndings("\n");
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

        // You start facing east down a corridor of stone: a lit wall ahead, the
        // corridor's sides shaded — one textured slice per screen column.
        var columns = screen.Columns();
        Assert.Equal(160, columns.Count);
        Assert.Equal((0, false), screen.CentreTexture());
        Assert.Equal((0, true), Screen.Texture(columns[0].Colors));
        Assert.Equal((0, true), Screen.Texture(columns[^1].Colors));
        double Height(GfxRect r) => r.Top - r.Bottom;
        Assert.True(Height(columns[80].Rect) < Height(columns[0].Rect));   // ahead is farther
        Assert.Contains(screen.Texts, t => t == "TIME 90");
    }

    [Fact]
    public void TheFloorAndCeilingAreTexturedAndDarkenFarAway()
    {
        var screen = new Screen();
        var vm = Start(screen);
        NextFrame(vm, screen);

        // The floor below eye level and the ceiling above it, a cell per dot: 160
        // across, 44 rows (the 88-dot view's half) each.
        Assert.Equal(2, screen.Planes.Count);
        var (floor, ceiling) = (screen.Planes[0], screen.Planes[1]);
        Assert.Equal((160, 44), (floor.Columns, floor.Rows));
        Assert.Equal((160, 44), (ceiling.Columns, ceiling.Rows));
        Assert.True(floor.Rect.Top <= 0.5 + 1e-9 && ceiling.Rect.Bottom >= 0.5 - 1e-9);

        // Down the middle of the corridor (the wall ahead hides the rows nearest eye
        // level), the floor and ceiling show their own textures. Devices get the top
        // row first: for the floor that's eye level, for the ceiling the nearest row.
        static int[] Rows(int[] colors, int from, int to) =>
            [.. Enumerable.Range(from, to - from).Select(r => colors[r * 160 + 80])];
        Assert.Equal((5, false), Screen.Texture(Rows(floor.Colors, 14, 44)));
        Assert.Equal((6, false), Screen.Texture(Rows(ceiling.Colors, 0, 30)));

        // Beyond FAR (6 tiles; here 2) the floor is the shaded copy: the row 14 dots
        // below eye level is 4.2 tiles away, the bottom one 1.4.
        var near = new Screen();
        var vm2 = Start(near, ("LET FAR = 6 ", "LET FAR = 2 "));
        NextFrame(vm2, near);
        Assert.Equal((5, true), Screen.Texture(Rows(near.Planes[0].Colors, 14, 16)));
        Assert.Equal((5, false), Screen.Texture(Rows(near.Planes[0].Colors, 40, 44)));
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
        Assert.Equal((0, false), screen.CentreTexture());   // ahead, east: a lit east/west face

        // A quarter turn (20 frames of 0.08 rad): facing south down column 1 of the
        // map, the wall at its end is a north/south face, so it's shaded.
        screen.Held.Add("d");
        Frames(vm, screen, 20);
        Assert.Equal((0, true), screen.CentreTexture());
    }

    [Fact]
    public void TheDoorIsLockedWithoutTheKey()
    {
        // Start beside the door (row 14, column 15), facing it, without the key.
        var screen = new Screen();
        var vm = Start(screen, ("LET PX = 1.5\nLET PY = 1.5", "LET PX = 13.5\nLET PY = 14.5"));
        NextFrame(vm, screen);
        Assert.Contains("find the key", screen.Texts);

        screen.Held.Add("w");
        for (var i = 0; i < 25; i++) NextFrame(vm, screen);
        Assert.Contains("The door is locked: find the key", screen.Texts);
        Assert.DoesNotContain("YOU ESCAPED!", screen.Texts);
    }

    [Fact]
    public void WithTheKeyTheGreenDoorIsTheWayOut()
    {
        // Start beside the door (row 14, column 15), facing it, with the key.
        var screen = new Screen();
        var vm = Start(screen,
            ("LET PX = 1.5\nLET PY = 1.5", "LET PX = 13.5\nLET PY = 14.5"),
            ("LET HASKEY = 0", "LET HASKEY = 1"));
        NextFrame(vm, screen);
        Assert.Equal((4, false), screen.CentreTexture());   // the door, ahead

        screen.Held.Add("w");                          // a held key: there is a keyboard, so it asks R / Q
        for (var i = 0; i < 40 && !screen.Texts.Contains("YOU ESCAPED!"); i++) NextFrame(vm, screen);
        Assert.Contains("YOU ESCAPED!", screen.Texts);

        screen.Held.Clear();
        screen.Typed.Enqueue("q");
        Assert.Equal(StepStatus.Halted, NextFrame(vm, screen));
    }

    [Fact]
    public void ObjectsBehindWallsArentDrawn()
    {
        // From the start the key and every gem are behind walls.
        var screen = new Screen();
        var vm = Start(screen);
        NextFrame(vm, screen);
        Assert.Empty(screen.Sprites);
    }

    [Fact]
    public void AGemAheadIsDrawnAndWalkingOverItAddsTime()
    {
        // Put the first gem two tiles ahead, down the starting corridor.
        var screen = new Screen();
        var vm = Start(screen, ("DATA 2, 3, 3", "DATA 2, 3, 1"));
        NextFrame(vm, screen);

        // A sprite in the middle of the view: a few screen columns, each a texture
        // column of the gem's colours (80-83 lit) with see-through cells, standing on
        // the floor and half a tile tall — so its top is at eye level, half a tile up.
        var sprite = screen.Sprites;
        Assert.InRange(sprite.Count, 10, 40);
        Assert.All(sprite, s => Assert.InRange(s.Rect.Left, 0.3, 0.7));
        Assert.All(sprite.SelectMany(s => s.Colors), c => Assert.True(c == -1 || c is >= 80 and <= 83, $"colour {c}"));
        Assert.All(sprite, s => Assert.True(s.Rect.Bottom < 0.3 && Math.Abs(s.Rect.Top - 0.5) < 1e-9));
        Assert.Contains("GEMS 0   MAZE 3D", screen.Texts);

        screen.Held.Add("w");                          // 2 tiles at 0.12 a frame
        for (var i = 0; i < 20; i++) NextFrame(vm, screen);
        Assert.Contains("GEMS 1   MAZE 3D", screen.Texts);
        Assert.Contains("A gem: +10 seconds", screen.Texts);
        Assert.Contains("TIME 100", screen.Texts);     // 90 + 10, under a second in
        Assert.Empty(screen.Sprites);                  // picked up: gone
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
