using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;
using ArcadeBasic.Compiler;
using ArcadeBasic.Vm;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Conformance.Tests;

/// <summary>
/// examples/kanban.bas with a mouse, driven the way the browser playground drives it:
/// step the VM, look at the frame it drew, press on a card, drag it over another lane,
/// release — then type Q to quit.
/// </summary>
public class KanbanDragTests
{
    /// <summary>An 80 x 24-cell screen (like the terminal's) that keeps each presented
    /// frame's text, with a pointer and keyboard the test drives.</summary>
    private sealed class Screen : IGraphicsDevice, IPointer, IKeyboard
    {
        private readonly List<(GfxPoint At, string Text)> _drawing = [];
        public List<(GfxPoint At, string Text)> Frame { get; private set; } = [];
        public readonly Queue<PointerEvent> Events = new();
        public readonly Queue<string> Keys = new();
        public GfxPoint Pointer;

        public void Clear() => _drawing.Clear();
        public void DrawText(GfxPoint at, string text) => _drawing.Add((at, text));
        public void Flush() => Frame = [.. _drawing];

        public bool TryGetPosition(out GfxPoint p) { p = Pointer; return true; }
        public bool TryReadEvent(out PointerEvent e) => Events.TryDequeue(out e);
        public string ReadKey() => Keys.Count > 0 ? Keys.Dequeue() : "";

        public void SetLineStyle(int style) { }
        public void SetPointStyle(int style) { }
        public void SetColor(GfxColorTarget target, int colorIndex) { }
        public void DrawPoints(IReadOnlyList<GfxPoint> points) { }
        public void DrawLines(IReadOnlyList<GfxPoint> polyline) { }
        public void FillArea(IReadOnlyList<GfxPoint> polygon) { }
        public int MaxColor => 15;
        public int MaxLineStyle => 3;
        public int MaxPointStyle => 3;
        public GfxDeviceSize DeviceSize { get; set; } = new(160, 96, "OTHER");

        public GfxPoint TextStarting(string prefix) =>
            Frame.Single(t => t.Text.StartsWith(prefix, StringComparison.Ordinal)).At;
    }

    /// <summary>Step until the program presents its next frame (SLEEP) or ends.</summary>
    private static StepStatus NextFrame(BasicVm vm)
    {
        for (var i = 0; i < 1000; i++)
        {
            var status = vm.Step(100_000).Status;
            if (status is StepStatus.Sleeping or StepStatus.Halted) return status;
        }
        throw new InvalidOperationException("no frame");
    }

    private static BasicVm Start(Screen screen)
    {
        var source = File.ReadAllText(Path.Combine(ExamplesDir(), "kanban.bas"));
        var file = new SourceFile("kanban.bas", source);
        var diags = new DiagnosticBag();
        var program = new BasicParser(new BasicLexer(file, diags).Lex(), file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        Assert.False(diags.HasErrors);
        return new BasicVm(BasicCompiler.Compile(program, info), new StringWriter(), new LineInputQueue(), screen, screen);
    }

    [Fact]
    public void DraggingACardOntoAnotherLaneMovesIt()
    {
        var screen = new Screen();
        var vm = Start(screen);

        Assert.Equal(StepStatus.Sleeping, NextFrame(vm));
        Assert.Contains(screen.Frame, t => t.Text.Contains("type a command"));   // it found the mouse

        // Card #5 starts in To-Do; press on it.
        var card = screen.TextStarting("#5 ");
        var grab = new GfxPoint(card.X + 0.02, card.Y + 0.01);
        screen.Pointer = grab;
        screen.Events.Enqueue(new PointerEvent(true, grab));
        NextFrame(vm);
        Assert.Contains(screen.Frame, t => t.Text.StartsWith("Status: Moving #5", StringComparison.Ordinal));

        // Drag it over Done: the card follows the pointer (drawn twice — greyed in place, and floating).
        var done = new GfxPoint(screen.TextStarting("DONE (").X + 0.02, 0.5);
        screen.Pointer = done;
        NextFrame(vm);
        Assert.Equal(2, screen.Frame.Count(t => t.Text.StartsWith("#5 ", StringComparison.Ordinal)));

        // Drop it.
        screen.Events.Enqueue(new PointerEvent(false, done));
        NextFrame(vm);
        Assert.Contains(screen.Frame, t => t.Text.StartsWith("Status: Moved #5 to Done", StringComparison.Ordinal));
        Assert.Contains(screen.Frame, t => t.Text == "TO-DO (2)");   // it left To-Do (3 → 2)
        Assert.Contains(screen.Frame, t => t.Text == "DONE (3)");    // and joined Done (2 → 3)

        // Typing goes onto the board; Q + Enter quits.
        screen.Keys.Enqueue("q");
        NextFrame(vm);
        Assert.Contains(screen.Frame, t => t.Text == "> q_");
        screen.Keys.Enqueue("\r");
        Assert.Equal(StepStatus.Halted, NextFrame(vm));
    }

    [Fact]
    public void ResizingTheScreenLaysTheBoardOutAgain()
    {
        var screen = new Screen();
        var vm = Start(screen);
        NextFrame(vm);
        Assert.Equal(22.0 / 24, screen.TextStarting("TO-DO (").Y, 6);  // 2 rows from the top of 24

        screen.DeviceSize = new GfxDeviceSize(160, 120, "OTHER");      // the window grows to 30 rows
        NextFrame(vm);
        Assert.Equal(28.0 / 30, screen.TextStarting("TO-DO (").Y, 6);  // 2 rows from the top of 30
    }

    private static string ExamplesDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "examples"))) dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir ?? throw new DirectoryNotFoundException("examples"), "examples");
    }
}
