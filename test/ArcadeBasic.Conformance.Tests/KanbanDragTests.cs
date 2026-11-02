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
/// drives it: step the VM, look at the frame it drew, press, drag, right-click, type. The
/// board keeps itself in kanban.board (in the working directory), so each test starts
/// without one — or the other boards a test makes.
/// </summary>
[Collection(ScoreFiles.Collection)]
public sealed class KanbanDragTests : IDisposable
{
    private const string BoardFile = "kanban.board";
    private static readonly string[] Boards = [BoardFile, "home.board", "work.board"];

    public KanbanDragTests() => Forget();
    public void Dispose() => Forget();
    private static void Forget() { foreach (var board in Boards) File.Delete(board); }

    /// <summary>A screen (the browser's proportions, 5 x 3) that keeps each presented frame's
    /// text, with a pointer and keyboard the test drives. It doesn't measure text, so the
    /// board's laid out with GraphicsState's estimates (Helvetica's widths).</summary>
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

        /// <summary>The number on the badge beside a lane's name: how many cards it has.</summary>
        public string Count(string lane)
        {
            var name = Frame.Single(t => t.Text == lane).At;
            return Frame.First(t => Math.Abs(t.At.Y - name.Y) < 0.02 && t.At.X > name.X && t.Text.All(char.IsDigit)).Text;
        }

        public void Type(string keys)
        {
            foreach (var c in keys) Keys.Enqueue(c.ToString());
        }

        public void Click(GfxPoint at, int button = PointerState.Primary)
        {
            Pointer = at;
            Events.Enqueue(new PointerEvent(true, at, button));
            Events.Enqueue(new PointerEvent(false, at, button));
        }

        /// <summary>Click on the text that starts with <paramref name="prefix"/> (a card's, a
        /// button's label, a menu's choice).</summary>
        public void Click(string prefix, int button = PointerState.Primary)
        {
            var at = TextStarting(prefix);
            Click(new GfxPoint(at.X + 0.005, at.Y + 0.01), button);
        }

        public void RightClick(string prefix) => Click(prefix, PointerState.Secondary);
    }

    private const string Up = "\0H", Down = "\0P", Left = "\0K", Right = "\0M", Enter = "\r", Esc = "\u001b", Delete = "\0S";

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
        var path = Path.Combine(ExamplesDir(), "kanban.bas");
        var diags = new DiagnosticBag();
        var program = ProgramLoader.Load(new SourceFile(path, File.ReadAllText(path)), (name, from) =>   // (and ui.bas, its LIBRARY)
        {
            var library = Path.Combine(Path.GetDirectoryName(from.Path)!, name);
            return File.Exists(library) ? new SourceFile(library, File.ReadAllText(library)) : null;
        }, diags);
        var info = Analyzer.Analyze(program, diags);
        Assert.False(diags.HasErrors, string.Join("\n", diags.All.Select(d => d.Render(false))));
        var vm = new BasicVm(BasicCompiler.Compile(program, info), new StringWriter(), new LineInputQueue(), screen, screen);
        Assert.Equal(StepStatus.Sleeping, NextFrame(vm));
        return vm;
    }

    /// <summary>A click is three frames: the press, the letting go (when a button or a menu's
    /// choice clicks), and the drawing after it.</summary>
    private static StepStatus Frames(BasicVm vm, int n = 3)
    {
        var status = StepStatus.Running;
        for (var i = 0; i < n && status != StepStatus.Halted; i++) status = NextFrame(vm);
        return status;
    }

    private static string[] SavedCards(string file = BoardFile) => File.ReadAllLines(file).Skip(3).ToArray();

    [Fact]
    public void DraggingACardOntoAnotherLaneMovesItAndSavesTheBoard()
    {
        var screen = new Screen();
        var vm = Start(screen);
        Assert.True(screen.Shows("+ Add a card"));                     // it found the mouse
        Assert.Contains(screen.Frame, t => t.Text.StartsWith("A new board", StringComparison.Ordinal));

        // Card #4 starts in To Do; press on it, drag it over Done: it follows the pointer
        // (drawn twice — greyed in place, and floating).
        var card = screen.TextStarting("Right-click a card");
        var grab = new GfxPoint(card.X + 0.02, card.Y + 0.01);
        screen.Pointer = grab;
        screen.Events.Enqueue(new PointerEvent(true, grab));
        NextFrame(vm);
        var done = new GfxPoint(screen.TextStarting("Done").X + 0.02, 0.5);
        screen.Pointer = done;
        NextFrame(vm);
        Assert.Equal(2, screen.Frame.Count(t => t.Text.StartsWith("Right-click a card", StringComparison.Ordinal)));
        Assert.Contains(screen.Frame, t => t.Text.StartsWith("Moving #4", StringComparison.Ordinal));

        // Drop it: it moves, and the board's saved.
        screen.Events.Enqueue(new PointerEvent(false, done));
        NextFrame(vm);
        Assert.Contains(screen.Frame, t => t.Text == "Moved #4 to Done");
        Assert.Equal("3", screen.Count("To Do"));                       // 4 → 3
        Assert.Equal("3", screen.Count("Done"));                        // 2 → 3
        Assert.Contains("3 4 Right-click a card for its menu: move it, delete it", SavedCards());

        // Typing doesn't start a command; the board's menu quits.
        screen.Type("q");
        NextFrame(vm);
        Assert.DoesNotContain(screen.Frame, t => t.Text.StartsWith('q'));
        screen.Click("kanban.board ▾");
        Frames(vm);
        Assert.True(screen.Shows("Open a board…"));
        screen.Click("Quit");
        Assert.Equal(StepStatus.Halted, Frames(vm));
    }

    [Fact]
    public void TheBoardOpensWhereItWasLeftAndKeepsItsCardNumbers()
    {
        File.WriteAllLines(BoardFile, ["ARCADE-KANBAN 2", "12", "2", "2 7 Fix the tap", "3 11 Buy milk"]);
        var screen = new Screen();
        var vm = Start(screen);
        Assert.Contains(screen.Frame, t => t.Text == "Opened kanban.board · 2 cards");
        Assert.Equal("0", screen.Count("To Do"));
        Assert.Equal("1", screen.Count("Doing"));

        screen.Click("+ Add a card");                                  // (To Do's: a new card gets #12)
        Frames(vm);
        screen.Type("Paint the fence" + Enter + Esc);
        NextFrame(vm);
        Assert.Contains(screen.Frame, t => t.Text == "Added #12 to To Do");
        screen.RightClick("Fix the tap");                              // its menu ...
        Frames(vm);
        Assert.True(screen.Shows("Move to To Do"));
        Assert.True(screen.Shows("Move to Done"));
        Assert.False(screen.Shows("Move to Doing"));                   // (it's there already)
        screen.Click("Delete");                                        // ... deletes it; #11 stays #11
        Frames(vm);
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
        screen.RightClick("Write the PAD$");
        Frames(vm);
        screen.Click("Move to Doing");
        Frames(vm);
        Assert.Contains(screen.Frame, t => t.Text == "Moved #1 to Doing");
        Assert.Equal(["3 2 Ship it, today", "2 1 Write the PAD$ helper"], SavedCards());   // (to the end of Doing)
    }

    [Fact]
    public void TheKeysSelectEditAndDeleteACardAndUndoPutsItBack()
    {
        var screen = new Screen();
        var vm = Start(screen);

        screen.Keys.Enqueue(Down);                                     // selects To Do's first card, #1
        screen.Keys.Enqueue(Down);                                     // then #2
        screen.Keys.Enqueue(Right);                                    // Doing's first, #5
        screen.Keys.Enqueue(Left);                                     // and back to To Do's first, #1
        screen.Keys.Enqueue(Down);                                     // #2
        screen.Keys.Enqueue(Enter);                                    // edit it, where it is: its text, the caret at the end
        screen.Type(" fast");
        NextFrame(vm);
        Assert.Contains(screen.Frame, t => t.Text.StartsWith("Editing #2", StringComparison.Ordinal));
        Assert.Contains(screen.Frame, t => t.Text.EndsWith("fast", StringComparison.Ordinal));
        screen.Keys.Enqueue(Enter);
        NextFrame(vm);
        Assert.Contains(screen.Frame, t => t.Text == "Changed #2");
        Assert.Contains("1 2 Drag cards to another lane, or up and down fast", SavedCards());

        screen.Keys.Enqueue(Delete);                                   // Delete deletes it, at once ...
        NextFrame(vm);
        Assert.Contains(screen.Frame, t => t.Text.StartsWith("Deleted #2 ", StringComparison.Ordinal));
        Assert.DoesNotContain(SavedCards(), l => l.StartsWith("1 2 ", StringComparison.Ordinal));
        screen.Click("Undo");                                          // ... and Undo puts it back, where it was
        Frames(vm);
        Assert.Contains(screen.Frame, t => t.Text == "Put #2 back");
        Assert.Equal("1 2 Drag cards to another lane, or up and down fast", SavedCards()[1]);
        Assert.False(screen.Shows("Undo"));
    }

    [Fact]
    public void ALongLaneScrolls()
    {
        var screen = new Screen();
        var vm = Start(screen);
        screen.Click("+ Add a card");                                  // (To Do's)
        Frames(vm);
        for (var i = 1; i <= 25; i++) screen.Type($"Job {i}" + Enter); // Enter adds it, and the next goes after it
        screen.Keys.Enqueue(Esc);
        NextFrame(vm);
        Assert.Equal("29", screen.Count("To Do"));
        Assert.True(screen.Shows("Job 25"));                           // the lane follows the cards added, to the last
        var more = screen.TextStarting("▲ ");
        Assert.Matches(@"^▲ \d+ more$", screen.Frame.First(t => t.At == more).Text);

        // Show the ones above (pressed, let go, and drawn again), until the first shows.
        bool First() => screen.Frame.Any(t => t.Text.StartsWith("Click a card to select", StringComparison.Ordinal));
        for (var page = 0; page < 12 && !First(); page++)
        {
            screen.Click("▲ ");
            Frames(vm);
            Assert.Contains(screen.Frame, t => t.Text.StartsWith("▼ ", StringComparison.Ordinal));
        }
        Assert.True(First());
        Assert.DoesNotContain(screen.Frame, t => t.Text.StartsWith("▲ ", StringComparison.Ordinal));
    }

    [Fact]
    public void ResizingTheScreenLaysTheBoardOutAgain()
    {
        var screen = new Screen();
        var vm = Start(screen);
        // (in points from the top: the screen is 960 across, so 576 down, then 720)
        var name = (1 - screen.TextStarting("To Do").Y) * 576;
        var add = (1 - screen.TextStarting("+ Add a card").Y) * 576;   // (a lane's: the title bar's is "+ New card")

        screen.DeviceSize = new GfxDeviceSize(160, 120, "OTHER");      // the window grows taller
        NextFrame(vm);
        Assert.Equal(name, (1 - screen.TextStarting("To Do").Y) * 720, 6);        // the lanes' names stay where they were
        Assert.Equal(add + 144, (1 - screen.TextStarting("+ Add a card").Y) * 720, 6);   // and the lanes reach the new bottom
    }

    [Fact]
    public void TheBoardsMenuSavesAsStartsAndOpensBoards()
    {
        var screen = new Screen();
        var vm = Start(screen);
        void Choose(string choice)
        {
            screen.Click(screen.Frame.First(t => t.Text.EndsWith(".board ▾", StringComparison.Ordinal)).Text);
            Frames(vm);
            screen.Click(choice);
            Frames(vm);
        }

        Choose("Save as…");                                            // (the name it has, to change)
        Assert.True(screen.Shows("Save as"));
        Assert.True(screen.Shows("kanban"));
        screen.Type(new string('\b', 6) + "home" + Enter);
        NextFrame(vm);
        Assert.Contains(screen.Frame, t => t.Text == "Saved home.board · 7 cards");
        Assert.True(screen.Shows("home.board ▾"));
        Assert.Equal(7, SavedCards("home.board").Length);

        Choose("New board…");
        screen.Type("home" + Enter);                                   // there's one: the dialog stays, and says so
        NextFrame(vm);
        Assert.True(screen.Shows("There's already a home.board"));
        screen.Type(new string('\b', 4) + "work");
        NextFrame(vm);
        screen.Click("Create");
        Frames(vm);
        Assert.Contains(screen.Frame, t => t.Text == "A new board, work.board");
        Assert.Equal("0", screen.Count("To Do"));
        Assert.Empty(SavedCards("work.board"));

        Choose("Open a board…");
        screen.Type("nope" + Enter);
        NextFrame(vm);
        Assert.True(screen.Shows("There's no nope.board"));
        screen.Keys.Enqueue(Esc);                                      // Esc cancels it
        NextFrame(vm);
        Assert.False(screen.Shows("Open a board"));

        Choose("Open a board…");
        screen.Type("home" + Enter);
        NextFrame(vm);
        Assert.Contains(screen.Frame, t => t.Text == "Opened home.board · 7 cards");
        Assert.Equal("4", screen.Count("To Do"));
    }

    private static string ExamplesDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "examples"))) dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir ?? throw new DirectoryNotFoundException("examples"), "examples");
    }
}
