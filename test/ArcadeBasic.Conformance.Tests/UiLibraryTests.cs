using ArcadeBasic.Core;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;
using ArcadeBasic.Compiler;
using ArcadeBasic.Vm;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Conformance.Tests;

/// <summary>
/// examples/ui.bas, the components library: programs that use it (LIBRARY "ui.bas"), run
/// on a recording device — whose text is GfxTextMetrics' estimate, so a monospace
/// character is 0.6 of its height: at UIBODY-like size 10, 6 points across.
/// </summary>
[Collection(ScoreFiles.Collection)]
public class UiLibraryTests
{
    private static string Run(string source)
    {
        var diags = new DiagnosticBag();
        var program = ProgramLoader.Load(new SourceFile("try.bas", "LIBRARY \"ui.bas\"\n" + source), (name, _) =>
        {
            var path = Path.Combine(ExamplesDir(), name);
            return File.Exists(path) ? new SourceFile(path, File.ReadAllText(path)) : null;
        }, diags);
        var info = Analyzer.Analyze(program, diags);
        Assert.False(diags.HasErrors, string.Join("\n", diags.All.Select(d => d.Render(false))));
        var printed = new StringWriter { NewLine = "\n" };   // the same on every OS
        new BasicVm(BasicCompiler.Compile(program, info), printed, TextReader.Null, new RecordingGraphicsDevice(maxColor: 255)).Run();
        return printed.ToString();
    }

    [Fact]
    public void TextWrapsAtSpacesAndBreaksAWordTooLongForALine()
    {
        var printed = Run("""
            DIM L$(8), P(8)
            CALL UIBEGIN
            LET N = UIWRAP("one two three four abcdefghijklmnop", 60, 10, "MONO", L$, P)
            FOR I = 1 TO N
              PRINT L$(I); "|"; P(I)
            NEXT I
            PRINT UIWRAP("", 60, 10, "MONO", L$, P); "["; L$(1); "]"
            """);
        Assert.Equal("one two| 1 \nthree four| 9 \nabcdefghij| 20 \nklmnop| 30 \n 1 []\n", printed);
    }

    [Fact]
    public void TextIsCutToFitWithAnEllipsis()
    {
        var printed = Run("""
            CALL UIBEGIN
            PRINT UIFIT$("abcdefghij", 60, 10, "MONO")
            PRINT UIFIT$("abcdefghijkl", 60, 10, "MONO")
            PRINT UIFIT$("abcdefgh ijk", 60, 10, "MONO")
            PRINT UIWIDTH("abc", 10, "MONO")
            """);
        Assert.Equal("abcdefghij\nabcdefghi…\nabcdefgh…\n 18 \n", printed);   // (no space before the "…")
    }

    [Fact]
    public void TypingEditsAtTheCaret()
    {
        var printed = Run("""
            DECLARE PUBLIC UITYPED$, UICARET
            CALL UIEDITSTART("abc")
            LET R = UIEDITKEY(CHR$(0) & CHR$(75))     ! left
            LET R = UIEDITKEY("X")
            LET R = UIEDITKEY(CHR$(0) & CHR$(71))     ! Home
            LET R = UIEDITKEY(CHR$(0) & CHR$(83))     ! Delete
            PRINT UITYPED$; UICARET
            LET R = UIEDITKEY(CHR$(0) & CHR$(79))     ! End
            LET R = UIEDITKEY(CHR$(8))                ! Backspace
            PRINT UITYPED$; UICARET; UIEDITKEY(CHR$(13)); UIEDITKEY(CHR$(27))
            """);
        Assert.Equal("bXc 0 \nbX 2  1 -1 \n", printed);
    }

    /// <summary>A screen that keeps each presented frame's text, with a pointer and keys the
    /// test drives (the browser's proportions; text widths are GraphicsState's estimates).</summary>
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
        public GfxDeviceSize DeviceSize => new(160, 96, "OTHER");

        public bool Shows(string text) => Frame.Any(t => t.Text == text);

        /// <summary>Press and let go on the text (a button's label, say).</summary>
        public void Click(string text, int button = PointerState.Primary)
        {
            var at = Frame.First(t => t.Text == text).At;
            Pointer = new GfxPoint(at.X + 0.005, at.Y + 0.01);
            Events.Enqueue(new PointerEvent(true, Pointer, button));
            Events.Enqueue(new PointerEvent(false, Pointer, button));
        }
    }

    [Fact]
    public void TheGalleryClicksTypesAndKeepsWhatsBehindItsDialogAndMenuStill()
    {
        var screen = new Screen();
        var path = Path.Combine(ExamplesDir(), "ui-gallery.bas");
        var diags = new DiagnosticBag();
        var program = ProgramLoader.Load(new SourceFile(path, File.ReadAllText(path)), (name, _) =>
        {
            var library = Path.Combine(ExamplesDir(), name);
            return File.Exists(library) ? new SourceFile(library, File.ReadAllText(library)) : null;
        }, diags);
        var info = Analyzer.Analyze(program, diags);
        Assert.False(diags.HasErrors, string.Join("\n", diags.All.Select(d => d.Render(false))));
        var vm = new BasicVm(BasicCompiler.Compile(program, info), new StringWriter(), new LineInputQueue(), screen, screen);
        StepStatus Frames(int n)   // (a click is a press, a letting go, then the drawing after it)
        {
            var status = StepStatus.Running;
            for (var f = 0; f < n; f++)
                for (var i = 0; i < 1000 && (status = vm.Step(100_000).Status) is not (StepStatus.Sleeping or StepStatus.Halted); i++) { }
            return status;
        }
        Frames(1);
        Assert.True(screen.Shows("Primary, raised, ghost and danger"));

        screen.Click("Save");
        Frames(3);
        Assert.True(screen.Shows("Clicked Save  ·  1 click(s) so far"));

        screen.Click("Open a dialog");
        Frames(3);
        Assert.True(screen.Shows("A dialog"));
        screen.Click("Save");                                          // behind the dialog: nothing
        Frames(3);
        Assert.True(screen.Shows("Clicked Save  ·  1 click(s) so far"));
        screen.Click("Close");
        Frames(3);
        Assert.False(screen.Shows("A dialog"));

        screen.Click("A title", PointerState.Secondary);               // a right-click: the menu, there
        Frames(3);
        Assert.True(screen.Shows("Reset the clicks"));
        screen.Click("Open");                                          // a press off it closes it, and clicks nothing
        Frames(3);
        Assert.False(screen.Shows("Reset the clicks"));
        Assert.True(screen.Shows("Clicked Save  ·  1 click(s) so far"));
        screen.Click("A menu ▾");                                      // the button opens it too
        Frames(3);
        screen.Click("Reset the clicks");
        Frames(3);
        Assert.False(screen.Shows("Reset the clicks"));
        Assert.True(screen.Shows("Primary, raised, ghost and danger"));

        screen.Click("Click here, and type");                          // the text field
        Frames(2);
        foreach (var c in "Ada") screen.Keys.Enqueue(c.ToString());
        screen.Keys.Enqueue("\r");
        Frames(1);
        Assert.True(screen.Shows("Ada"));
        Assert.True(screen.Shows("Enter: kept \"Ada\""));

        screen.Keys.Enqueue("q");
        Assert.Equal(StepStatus.Halted, Frames(1));
    }

    private static string ExamplesDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "examples"))) dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir ?? throw new DirectoryNotFoundException("examples"), "examples");
    }
}
