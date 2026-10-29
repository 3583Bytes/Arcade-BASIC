using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;
using ArcadeBasic.Compiler;
using ArcadeBasic.Vm;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Conformance.Tests;

/// <summary>
/// examples/kanban.bas with a mouse and keys, driven the way the browser playground
/// drives it: step the VM, look at the frame it drew, press, drag, type. The board keeps
/// itself in kanban.board (in the working directory), so each test starts without one.
/// </summary>
[Collection(ScoreFiles.Collection)]
public sealed class KanbanDragTests : IDisposable
{
    private const string BoardFile = "kanban.board";

    public KanbanDragTests() => File.Delete(BoardFile);
    public void Dispose() => File.Delete(BoardFile);

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
        public int MaxColor => 255;
        public int MaxLineStyle => 3;
        public int MaxPointStyle => 3;
        public GfxDeviceSize DeviceSize { get; set; } = new(160, 96, "OTHER");

        public GfxPoint TextStarting(string prefix) =>
            Frame.First(t => t.Text.StartsWith(prefix, StringComparison.Ordinal)).At;

        public bool Shows(string text) => Frame.Any(t => t.Text == text);

        /// <summary>The number drawn beside a lane's name: how many cards it has.</summary>
        public string Count(string lane)
        {
            var name = Frame.Single(t => t.Text == lane).At;
            return Frame.First(t => t.At.Y == name.Y && t.At.X > name.X && t.Text.All(char.IsDigit)).Text;
        }

        public void Type(string keys)
        {
            foreach (var c in keys) Keys.Enqueue(c.ToString());
        }

        public void Click(GfxPoint at)
        {
            Pointer = at;
            Events.Enqueue(new PointerEvent(true, at));
            Events.Enqueue(new PointerEvent(false, at));
        }
    }

    private const string Up = "\0H", Down = "\0P", Left = "\0K", Right = "\0M", Enter = "\r";

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
        var vm = new BasicVm(BasicCompiler.Compile(program, info), new StringWriter(), new LineInputQueue(), screen, screen);
        Assert.Equal(StepStatus.Sleeping, NextFrame(vm));
        return vm;
    }

    private static string[] SavedCards() => File.ReadAllLines(BoardFile).Skip(3).ToArray();

    [Fact]
    public void DraggingACardOntoAnotherLaneMovesItAndSavesTheBoard()
    {
        var screen = new Screen();
        var vm = Start(screen);
        Assert.True(screen.Shows("+ add a card"));                     // it found the mouse
        Assert.Contains(screen.Frame, t => t.Text.StartsWith("A new board", StringComparison.Ordinal));

        // Card #4 starts in To Do; press on it, drag it over Done: it follows the pointer
        // (drawn twice — greyed in place, and floating).
        var card = screen.TextStarting("Select a card");
        var grab = new GfxPoint(card.X + 0.02, card.Y + 0.01);
        screen.Pointer = grab;
        screen.Events.Enqueue(new PointerEvent(true, grab));
        NextFrame(vm);
        var done = new GfxPoint(screen.TextStarting("DONE").X + 0.02, 0.5);
        screen.Pointer = done;
        NextFrame(vm);
        Assert.Equal(2, screen.Frame.Count(t => t.Text.StartsWith("Select a card", StringComparison.Ordinal)));
        Assert.Contains(screen.Frame, t => t.Text.StartsWith("Moving #4", StringComparison.Ordinal));

        // Drop it: it moves, and the board's saved.
        screen.Events.Enqueue(new PointerEvent(false, done));
        NextFrame(vm);
        Assert.Contains(screen.Frame, t => t.Text == "Moved #4 to Done");
        Assert.Equal("3", screen.Count("TO DO"));                       // 4 → 3
        Assert.Equal("3", screen.Count("DONE"));                        // 2 → 3
        Assert.Contains("3 4 Select a card and press Delete (twice) to remove it", SavedCards());

        // Typing goes onto the board; Q + Enter quits.
        screen.Type("q");
        NextFrame(vm);
        Assert.True(screen.Shows("> "));
        Assert.True(screen.Shows("q_"));
        screen.Keys.Enqueue(Enter);
        Assert.Equal(StepStatus.Halted, NextFrame(vm));
    }

    [Fact]
    public void TheBoardOpensWhereItWasLeftAndKeepsItsCardNumbers()
    {
        File.WriteAllLines(BoardFile, ["ARCADE-KANBAN 2", "12", "2", "2 7 Fix the tap", "3 11 Buy milk"]);
        var screen = new Screen();
        var vm = Start(screen);
        Assert.Contains(screen.Frame, t => t.Text == "Opened kanban.board · 2 cards");
        Assert.Equal("0", screen.Count("TO DO"));
        Assert.Equal("1", screen.Count("DOING"));

        screen.Type("A Paint the fence" + Enter + "D 7" + Enter);        // a new card gets #12; #11 stays #11
        NextFrame(vm);
        Assert.Equal(["3 11 Buy milk", "1 12 Paint the fence"], SavedCards());
        Assert.Equal("13", File.ReadAllLines(BoardFile)[1]);
    }

    [Fact]
    public void AVersionOneBoardOpensWithItsCardsNumbered()
    {
        File.WriteAllLines(BoardFile, ["ARCADE-KANBAN 1", "2", "1 Write the PAD$ helper", "3 Ship it, today"]);
        var screen = new Screen();
        var vm = Start(screen);
        Assert.Contains(screen.Frame, t => t.Text == "Opened kanban.board · 2 cards");
        screen.Type("M 1 2" + Enter);
        NextFrame(vm);
        Assert.Equal(["3 2 Ship it, today", "2 1 Write the PAD$ helper"], SavedCards());   // (to the end of Doing)
    }

    [Fact]
    public void TheKeysSelectPickUpMoveAndEditACard()
    {
        var screen = new Screen();
        var vm = Start(screen);

        screen.Keys.Enqueue(Down);                                     // selects To Do's first card, #1
        screen.Keys.Enqueue(Down);                                     // then #2
        screen.Type(" ");                                              // picks it up
        screen.Keys.Enqueue(Right);                                    // into Doing ...
        screen.Keys.Enqueue(Up);                                       // ... above #5
        screen.Type(" ");                                              // puts it down
        NextFrame(vm);
        Assert.Contains(screen.Frame, t => t.Text == "Put it down");
        Assert.Equal("2", screen.Count("DOING"));
        Assert.Equal(["2 2", "2 5"], SavedCards().Where(l => l.StartsWith('2')).Select(l => l[..3]));

        screen.Keys.Enqueue(Enter);                                    // edit it: its text, the cursor at the end
        screen.Type(" fast");
        NextFrame(vm);
        Assert.True(screen.Shows("Edit #2: "));
        screen.Keys.Enqueue(Enter);
        NextFrame(vm);
        Assert.Contains(screen.Frame, t => t.Text == "Changed #2");
        Assert.Contains("2 2 Drag cards to another lane, or up and down fast", SavedCards());

        screen.Keys.Enqueue("\0S");                                    // Delete asks first ...
        NextFrame(vm);
        Assert.Contains(screen.Frame, t => t.Text.StartsWith("Delete #2 ", StringComparison.Ordinal));
        screen.Keys.Enqueue("\0S");                                    // ... and again deletes
        NextFrame(vm);
        Assert.Contains(screen.Frame, t => t.Text.StartsWith("Deleted #2 ", StringComparison.Ordinal));
        Assert.DoesNotContain(SavedCards(), l => l.StartsWith("2 2 ", StringComparison.Ordinal));
    }

    [Fact]
    public void ALongLaneScrolls()
    {
        var screen = new Screen();
        var vm = Start(screen);
        for (var i = 1; i <= 25; i++) screen.Type($"A Job {i}" + Enter);
        screen.Keys.Enqueue("\u001b");                                 // (let go of the last one added)
        NextFrame(vm);
        Assert.Equal("29", screen.Count("TO DO"));
        var more = screen.TextStarting("▼ ");
        Assert.Matches(@"^▼ \d+ more$", screen.Frame.First(t => t.At == more).Text);

        screen.Click(new GfxPoint(more.X + 0.02, more.Y + 0.01));        // show the next ones
        NextFrame(vm);
        Assert.Contains(screen.Frame, t => t.Text.StartsWith("▲ ", StringComparison.Ordinal));
        Assert.True(screen.Shows("Job 25"));
    }

    [Fact]
    public void ResizingTheScreenLaysTheBoardOutAgain()
    {
        var screen = new Screen();
        var vm = Start(screen);
        Assert.Equal(22.25 / 24, screen.TextStarting("TO DO").Y, 6);    // 2 rows from the top of 24

        screen.DeviceSize = new GfxDeviceSize(160, 120, "OTHER");      // the window grows to 30 rows
        NextFrame(vm);
        Assert.Equal(28.25 / 30, screen.TextStarting("TO DO").Y, 6);    // 2 rows from the top of 30
    }

    private static string ExamplesDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "examples"))) dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir ?? throw new DirectoryNotFoundException("examples"), "examples");
    }
}
