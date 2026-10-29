using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;
using ArcadeBasic.Compiler;
using ArcadeBasic.Vm;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Conformance.Tests;

/// <summary>
/// examples/readyedit.bas (Ready Edit) with a mouse and keys, driven the way the browser playground
/// drives it: step the VM a frame at a time, read the text it drew, click, drag, type.
/// It keeps the files it has open in readyedit.files (in the working directory), so each
/// test starts without one.
/// </summary>
[Collection(ScoreFiles.Collection)]
public sealed class ReadyEditTests : IDisposable
{
    private static readonly string[] Files = ["readyedit.files", "notes.txt", "other.txt"];

    public ReadyEditTests() => DeleteFiles();
    public void Dispose() => DeleteFiles();

    private static void DeleteFiles()
    {
        foreach (var f in Files) File.Delete(f);
    }

    /// <summary>An 80 x 24-cell screen (the browser's) that keeps each presented frame's
    /// text, with a pointer and a keyboard (that sees Shift held) the test drives.</summary>
    private sealed class Screen : IGraphicsDevice, IPointer, IKeyboard, IKeyState
    {
        private readonly List<(GfxPoint At, string Text)> _drawing = [];
        public List<(GfxPoint At, string Text)> Frame { get; private set; } = [];
        public readonly Queue<PointerEvent> Events = new();
        public readonly Queue<string> Keys = new();
        public readonly HashSet<string> Held = [];
        public GfxPoint Pointer;

        public void Clear() => _drawing.Clear();
        public void DrawText(GfxPoint at, string text) => _drawing.Add((at, text));
        public void Flush() => Frame = [.. _drawing];

        public bool TryGetPosition(out GfxPoint p) { p = Pointer; return true; }
        public bool TryReadEvent(out PointerEvent e) => Events.TryDequeue(out e);
        public string ReadKey() => Keys.Count > 0 ? Keys.Dequeue() : "";
        public bool IsKeyDown(string key) => Held.Contains(key);

        public void SetLineStyle(int style) { }
        public void SetPointStyle(int style) { }
        public void SetColor(GfxColorTarget target, int colorIndex) { }
        public void DrawPoints(IReadOnlyList<GfxPoint> points) { }
        public void DrawLines(IReadOnlyList<GfxPoint> polyline) { }
        public void FillArea(IReadOnlyList<GfxPoint> polygon) { }
        public int MaxColor => 255;
        public int MaxLineStyle => 3;
        public int MaxPointStyle => 3;
        public GfxDeviceSize DeviceSize => new(160, 96, "OTHER");

        public bool Shows(string text) => Frame.Any(t => t.Text == text);

        /// <summary>The text on screen row <paramref name="row"/> (0 = the top) from column
        /// <paramref name="from"/>, rebuilt from the pieces drawn on it.</summary>
        public string Row(int row, int from = 0, int to = 80)
        {
            var baseline = (24 - 1 - row + 0.25) / 24;
            var cells = Enumerable.Repeat(' ', 80).ToArray();
            foreach (var (at, text) in Frame.Where(t => Math.Abs(t.At.Y - baseline) < 1e-6))
            {
                var col = (int)Math.Round(at.X * 80);
                for (var i = 0; i < text.Length && col + i < 80; i++)
                    if (col + i >= from && col + i < to) cells[col + i] = text[i];
            }
            return new string(cells[from..to]).TrimEnd();
        }

        /// <summary>The text area's line <paramref name="line"/> (0 = the first showing),
        /// after its line number.</summary>
        public string Line(int line) => Row(line + 2, from: 5, to: 79);

        public string Status => Row(23).Trim();

        /// <summary>The middle of the cell at column <paramref name="col"/>, row <paramref name="row"/>.</summary>
        public static GfxPoint Cell(double col, int row) => new(col / 80, (24 - 1 - row + 0.5) / 24);

        /// <summary>Text column <paramref name="col"/> (where the cursor goes before
        /// character <paramref name="col"/>), on the text area's line <paramref name="line"/>.</summary>
        public static GfxPoint Text(int col, int line) => Cell(5 + col + 0.1, line + 2);

        public void Type(params string[] keys)
        {
            foreach (var k in keys)
                if (k.Length > 0 && k[0] == '\0') Keys.Enqueue(k);
                else foreach (var c in k) Keys.Enqueue(c.ToString());
        }

        public void Click(GfxPoint at)
        {
            Pointer = at;
            Events.Enqueue(new PointerEvent(true, at));
            Events.Enqueue(new PointerEvent(false, at));
        }
    }

    private const string Enter = "\r", Tab = "\t", Backspace = "\b", Escape = "\x1b";
    private const string F2 = "\0<", F3 = "\0=", F4 = "\0>", F7 = "\0A", F8 = "\0B";
    private const string Up = "\0H", Insert = "\0R", Delete = "\0S", CtrlX = "\x18", CtrlV = "\x16";
    private const string Right = "\0M", Down = "\0P", End = "\0O", CtrlB = "\x02", CtrlK = "\x0b";

    /// <summary>Esc, then <paramref name="letter"/>: a command.</summary>
    private static string[] Esc(char letter) => [Escape, letter.ToString()];

    /// <summary>Click the menu bar's button <paramref name="label"/>.</summary>
    private static void ClickButton(Screen screen, BasicVm vm, string label)
    {
        var at = screen.Frame.First(t => t.Text == label).At;
        screen.Click(at with { X = at.X + 0.005, Y = at.Y + 0.01 });
        NextFrame(vm, 2);
    }

    /// <summary>Press <paramref name="key"/> with Shift held.</summary>
    private static void Shifted(Screen screen, BasicVm vm, string key)
    {
        screen.Held.Add("ShiftLeft");
        screen.Type(key);
        NextFrame(vm);
        screen.Held.Clear();
    }

    /// <summary>Step until the program presents its next frame (SLEEP) or ends.</summary>
    private static StepStatus NextFrame(BasicVm vm, int frames = 1)
    {
        var status = StepStatus.Running;
        for (var f = 0; f < frames; f++)
        {
            for (var i = 0; ; i++)
            {
                if (i == 1000) throw new InvalidOperationException("no frame");
                status = vm.Step(100_000).Status;
                if (status is StepStatus.Sleeping or StepStatus.Halted) break;
            }
            if (status == StepStatus.Halted) break;
        }
        return status;
    }

    private static BasicVm Start(Screen screen)
    {
        var source = File.ReadAllText(Path.Combine(ExamplesDir(), "readyedit.bas"));
        var file = new SourceFile("readyedit.bas", source);
        var diags = new DiagnosticBag();
        var program = new BasicParser(new BasicLexer(file, diags).Lex(), file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        Assert.False(diags.HasErrors);
        var vm = new BasicVm(BasicCompiler.Compile(program, info), new StringWriter(), new LineInputQueue(), screen, screen);
        Assert.Equal(StepStatus.Sleeping, NextFrame(vm));
        return vm;
    }

    /// <summary>Start with <paramref name="names"/> open, as if they were open last time.</summary>
    private static void OpenLastTime(params string[] names) =>
        File.WriteAllLines("readyedit.files", ["READY-EDIT 1", names[0], names.Length.ToString(), .. names, "0"]);

    [Fact]
    public void TheFirstRunShowsANewFileThatSaysHowItWorks()
    {
        var screen = new Screen();
        Start(screen);

        Assert.Equal("New   Open   Save   Save as   Close     Cut   Copy   Paste       Help   Quit", screen.Row(0).Trim());
        Assert.True(screen.Shows("untitled-1"));
        Assert.Equal("Welcome to Ready Edit, a text editor written in Arcade BASIC.", screen.Line(0));
        // a tab character runs on to the next stop of 4, a faint → where it starts
        Assert.Equal("→   This line starts with a tab,", screen.Line(6));
        Assert.StartsWith("→   →   and this one with two.", screen.Line(7));
        Assert.EndsWith("Ln 1, Col 1   Indent: tabs", screen.Row(1));   // top right
        // the keys bar, the bottom two rows: Esc, then a letter
        Assert.Equal("Esc, then  S Save  E Save as  O Open  N New  W Close  T Next tab  P Prev tab", screen.Row(22).Trim());
        Assert.Equal("B Select  A Select all  X Cut  C Copy  V Paste  G Help  Q Quit", screen.Row(23).Trim());
    }

    [Fact]
    public void TypingAndSaveAsWriteTheFileAndTheEditorRemembersIt()
    {
        var screen = new Screen();
        var vm = Start(screen);

        screen.Type(F7, "hello", Tab, "world", Enter, "  indented", Enter, "x");
        NextFrame(vm);
        Assert.True(screen.Shows("untitled-2"));
        Assert.True(screen.Shows("●"));                                // unsaved
        Assert.Equal("hello→  world", screen.Line(0));
        Assert.Equal("  indented", screen.Line(1));
        Assert.Equal("  x", screen.Line(2));                           // Enter kept the indent
        Assert.EndsWith("Ln 3, Col 4   Indent: tabs", screen.Row(1));

        // F2 on a new file asks for a name
        screen.Type(F2);
        NextFrame(vm);
        Assert.StartsWith("Save as: untitled-2.txt_", screen.Status);
        screen.Type(string.Concat(Enumerable.Repeat(Backspace, 14)), "notes.txt", Enter);
        NextFrame(vm);
        Assert.Equal(["hello\tworld", "  indented", "  x"], File.ReadAllLines("notes.txt"));
        Assert.StartsWith("Saved notes.txt · 3 lines", screen.Status);
        Assert.True(screen.Shows("notes.txt"));
        Assert.False(screen.Shows("●"));
        Assert.Equal(["READY-EDIT 1", "notes.txt", "1", "notes.txt", "1", "notes.txt"], File.ReadAllLines("readyedit.files"));

        // next time it opens again, in its own tab
        var again = new Screen();
        Start(again);
        Assert.True(again.Shows("notes.txt"));
        Assert.False(again.Shows("untitled-1"));
        Assert.Equal("hello→  world", again.Line(0));
        Assert.StartsWith("Opened 1 file(s) from last time", again.Status);
    }

    [Fact]
    public void ClickingDraggingAndDoubleClickingPlaceTheCursorAndSelect()
    {
        File.WriteAllLines("notes.txt", ["alpha beta", "gamma", "delta"]);
        OpenLastTime("notes.txt");
        var screen = new Screen();
        var vm = Start(screen);
        Assert.Equal("alpha beta", screen.Line(0));

        // a click puts the cursor there
        screen.Click(Screen.Text(6, 0));
        NextFrame(vm, 2);
        screen.Type("X");
        NextFrame(vm);
        Assert.Equal("alpha Xbeta", screen.Line(0));

        // drag from the start of line 1 into line 2, and Tab indents both lines
        screen.Pointer = Screen.Text(0, 0);
        screen.Events.Enqueue(new PointerEvent(true, screen.Pointer));
        NextFrame(vm);
        screen.Pointer = Screen.Text(3, 1);
        NextFrame(vm);
        screen.Events.Enqueue(new PointerEvent(false, screen.Pointer));
        NextFrame(vm);
        screen.Type(Tab, F2);
        NextFrame(vm);
        Assert.Equal(["\talpha Xbeta", "\tgamma", "delta"], File.ReadAllLines("notes.txt"));

        // Shift+Tab undents them again (the selection stays)
        screen.Held.Add("ShiftLeft");
        screen.Type(Tab);
        NextFrame(vm);
        screen.Held.Clear();
        screen.Type(F2);
        NextFrame(vm);
        Assert.Equal(["alpha Xbeta", "gamma", "delta"], File.ReadAllLines("notes.txt"));

        // a double-click selects a word, and typing replaces it
        screen.Click(Screen.Text(2, 2));
        screen.Click(Screen.Text(2, 2));
        NextFrame(vm, 4);
        screen.Type("D", F2);
        NextFrame(vm);
        Assert.Equal(["alpha Xbeta", "gamma", "D"], File.ReadAllLines("notes.txt"));
    }

    [Fact]
    public void TabsOpenSwitchAndCloseAndUnsavedChangesNeedASecondClose()
    {
        File.WriteAllLines("notes.txt", ["the notes"]);
        File.WriteAllLines("other.txt", ["the other one"]);
        OpenLastTime("notes.txt");
        var screen = new Screen();
        var vm = Start(screen);

        // F3: the Open box offers the recent files; type another name
        screen.Type(F3);
        NextFrame(vm);
        Assert.True(screen.Shows("Open a file"));
        screen.Type("other.txt", Enter);
        NextFrame(vm);
        Assert.Equal("the other one", screen.Line(0));
        Assert.True(screen.Shows("notes.txt"));
        Assert.True(screen.Shows("other.txt"));

        // a file that isn't there
        screen.Type(F3, "nope.txt", Enter);
        NextFrame(vm);
        Assert.StartsWith("There's no file called nope.txt", screen.Status);

        // click the first tab to show it; change it; its × asks first, then closes it
        var notesTab = screen.Frame.First(t => t.Text == "notes.txt").At;
        screen.Click(notesTab with { X = notesTab.X + 0.01, Y = notesTab.Y + 0.01 });
        NextFrame(vm, 2);
        Assert.Equal("the notes", screen.Line(0));
        screen.Type("!");
        NextFrame(vm);
        var close = screen.Frame.First(t => t.Text == "●").At;
        screen.Click(close with { X = close.X + 0.005, Y = close.Y + 0.01 });
        NextFrame(vm, 2);
        Assert.StartsWith("notes.txt has changes that aren't saved", screen.Status);
        Assert.True(screen.Shows("notes.txt"));
        screen.Click(close with { X = close.X + 0.005, Y = close.Y + 0.01 });
        NextFrame(vm, 2);
        Assert.False(screen.Shows("notes.txt"));
        Assert.Equal("the other one", screen.Line(0));
        Assert.Equal(["the notes"], File.ReadAllLines("notes.txt"));    // not saved

        // F8 closes the last one too, and a new file takes its place
        screen.Type(F8);
        NextFrame(vm);
        Assert.False(screen.Shows("other.txt"));
        Assert.True(screen.Shows("untitled-1"));
    }

    [Fact]
    public void CutCopyAndPasteWorkOnSelectionsAndWholeLinesAndBetweenTabs()
    {
        File.WriteAllLines("notes.txt", ["alpha beta", "gamma", "delta"]);
        OpenLastTime("notes.txt");
        var screen = new Screen();
        var vm = Start(screen);

        // double-click "beta", Copy, click past the end of "delta", Shift+Ins pastes
        screen.Click(Screen.Text(7, 0));
        screen.Click(Screen.Text(7, 0));
        NextFrame(vm, 4);
        ClickButton(screen, vm, "Copy");
        Assert.StartsWith("Copied 4 characters", screen.Status);
        screen.Click(Screen.Text(20, 2));
        NextFrame(vm, 2);
        Shifted(screen, vm, Insert);
        Assert.Equal("deltabeta", screen.Line(2));

        // nothing selected: Shift+Del cuts the whole line, and Paste puts it back above
        // the cursor's line
        screen.Click(Screen.Text(1, 1));
        NextFrame(vm, 2);
        Shifted(screen, vm, Delete);
        Assert.StartsWith("Cut the line", screen.Status);
        Assert.Equal("deltabeta", screen.Line(1));
        screen.Type(Up);
        NextFrame(vm);
        ClickButton(screen, vm, "Paste");
        Assert.Equal(["gamma", "alpha beta", "deltabeta"], [screen.Line(0), screen.Line(1), screen.Line(2)]);

        // a selection over two lines: Ctrl+X (as a terminal sends it) cuts it ...
        screen.Pointer = Screen.Text(2, 1);
        screen.Events.Enqueue(new PointerEvent(true, screen.Pointer));
        NextFrame(vm);
        screen.Pointer = Screen.Text(5, 2);
        NextFrame(vm);
        screen.Events.Enqueue(new PointerEvent(false, screen.Pointer));
        NextFrame(vm);
        screen.Type(CtrlX, F2);
        NextFrame(vm);
        Assert.Equal(["gamma", "albeta"], File.ReadAllLines("notes.txt"));

        // ... and Ctrl+V pastes it into a new file, in another tab
        screen.Type(F7, CtrlV);
        NextFrame(vm);
        Assert.True(screen.Shows("untitled-1"));
        Assert.Equal(["pha beta", "delta", ""], [screen.Line(0), screen.Line(1), screen.Line(2)]);
        Assert.StartsWith("Pasted 2 lines", screen.Status);
    }

    [Fact]
    public void TheKeyboardAloneSelectsCopiesPastesAndRunsTheCommands()
    {
        File.WriteAllLines("notes.txt", ["alpha beta", "gamma"]);
        OpenLastTime("notes.txt");
        var screen = new Screen();
        var vm = Start(screen);

        // Esc: the last message makes way for the keys, to pick a letter from
        Assert.StartsWith("Opened 1 file(s) from last time", screen.Status);
        screen.Type(Escape);
        NextFrame(vm);
        Assert.StartsWith("B Select", screen.Status);

        // B starts selecting, and moving selects; C copies, V pastes
        screen.Type("b", Right, Right, Right, Right, Right);
        NextFrame(vm);
        screen.Type([.. Esc('c'), End, .. Esc('v')]);
        NextFrame(vm);
        Assert.Equal("alpha betaalpha", screen.Line(0));

        // a letter that isn't a command isn't typed either
        screen.Type([.. Esc('z')]);
        NextFrame(vm);
        Assert.StartsWith("Esc Z isn't a command", screen.Status);
        Assert.Equal("alpha betaalpha", screen.Line(0));

        // A selects everything, X cuts it, S saves
        screen.Type([.. Esc('a')]);
        NextFrame(vm);
        Assert.StartsWith("Selected all 2 lines", screen.Status);
        screen.Type([.. Esc('x'), .. Esc('s')]);
        NextFrame(vm);
        Assert.Equal([""], File.ReadAllLines("notes.txt"));

        // in a terminal Ctrl and the letter: Ctrl+V pastes it all back; Ctrl+B and
        // Down select a line, Ctrl+K copies it
        screen.Type(CtrlV, Up, Up, CtrlB, Down, CtrlK);
        NextFrame(vm);
        Assert.StartsWith("Copied 1 line", screen.Status);
        Assert.Equal(["alpha betaalpha", "gamma"], [screen.Line(0), screen.Line(1)]);

        // the keys bar can be clicked: Select all (back once the next key clears the message)
        Assert.DoesNotContain(screen.Frame, t => t.Text == "Select all");
        screen.Type(End);
        NextFrame(vm);
        var all = screen.Frame.First(t => t.Text == "Select all").At;
        screen.Click(all with { X = all.X + 0.005, Y = all.Y + 0.01 });
        NextFrame(vm, 2);
        Assert.StartsWith("Selected all 2 lines", screen.Status);

        // Esc W closes the tab (twice, with unsaved changes)
        screen.Type([.. Esc('w')]);
        NextFrame(vm);
        Assert.StartsWith("notes.txt has changes that aren't saved", screen.Status);
        screen.Type([.. Esc('w')]);
        NextFrame(vm);
        Assert.False(screen.Shows("notes.txt"));
    }

    [Fact]
    public void ShiftUpAndDownSelectWholeLines()
    {
        File.WriteAllLines("notes.txt", ["one", "two", "three", "four"]);
        OpenLastTime("notes.txt");
        var screen = new Screen();
        var vm = Start(screen);

        // Shift+↓ in the middle of "two": all of "two"; again: "three" too; Shift+↑ gives it back
        screen.Click(Screen.Text(1, 1));
        NextFrame(vm, 2);
        Shifted(screen, vm, Down);
        screen.Type([.. Esc('c')]);
        NextFrame(vm);
        Assert.StartsWith("Copied 1 line", screen.Status);
        Shifted(screen, vm, Down);
        screen.Type([.. Esc('c')]);
        NextFrame(vm);
        Assert.StartsWith("Copied 2 lines", screen.Status);
        Shifted(screen, vm, Up);
        screen.Type([.. Esc('c')]);
        NextFrame(vm);
        Assert.StartsWith("Copied 1 line", screen.Status);

        // a few characters picked with Shift+→, then Shift+↑: the whole line, then the one above
        screen.Click(Screen.Text(1, 2));
        NextFrame(vm, 2);
        Shifted(screen, vm, Right);
        Shifted(screen, vm, Up);
        Shifted(screen, vm, Up);
        screen.Type([.. Esc('x'), .. Esc('s')]);
        NextFrame(vm);
        Assert.Equal(["one", "four"], File.ReadAllLines("notes.txt"));

        // and from the last line, Shift+↑ takes all of it
        screen.Click(Screen.Text(2, 1));
        NextFrame(vm, 2);
        Shifted(screen, vm, Up);
        screen.Type([.. Esc('x'), .. Esc('s')]);
        NextFrame(vm);
        Assert.Equal(["one", ""], File.ReadAllLines("notes.txt"));
    }

    [Fact]
    public void SaveAsAsksBeforeReplacingAnotherFileAndQuitAsksAboutUnsavedChanges()
    {
        File.WriteAllLines("other.txt", ["keep me"]);
        var screen = new Screen();
        var vm = Start(screen);

        screen.Type(F7, "new text", F4, string.Concat(Enumerable.Repeat(Backspace, 14)), "other.txt", Enter);
        NextFrame(vm);
        Assert.StartsWith("There's a file called other.txt already", screen.Status);
        Assert.Equal(["keep me"], File.ReadAllLines("other.txt"));
        screen.Type(Escape);
        NextFrame(vm);
        Assert.StartsWith("Not saved", screen.Status);

        // Quit (the menu's last button) with unsaved changes: only the second time
        var quit = screen.Frame.First(t => t.Text == "Quit").At;
        screen.Click(quit with { Y = quit.Y + 0.01 });
        NextFrame(vm, 2);
        Assert.StartsWith("1 file(s) with changes that aren't saved", screen.Status);
        screen.Click(quit with { Y = quit.Y + 0.01 });
        Assert.Equal(StepStatus.Halted, NextFrame(vm, 3));
    }

    private static string ExamplesDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "examples"))) dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir ?? throw new DirectoryNotFoundException("examples"), "examples");
    }
}
