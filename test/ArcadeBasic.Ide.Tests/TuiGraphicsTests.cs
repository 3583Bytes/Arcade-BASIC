using ArcadeBasic.Ide;
using ArcadeBasic.Interpreter;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Ide.Tests;

/// <summary>
/// Headless verification of the terminal graphics backend: a BASIC program runs
/// through <see cref="TuiGraphicsDevice"/> into a <see cref="BrailleCanvas"/>,
/// and we inspect the braille buffer directly (no console driver). The actual
/// on-screen rendering is verified visually in a real terminal.
/// </summary>
public class TuiGraphicsTests
{
    private sealed class TypedKeys(params string[] keys) : IKeyboard
    {
        private readonly Queue<string> _keys = new(keys);
        public string ReadKey() => _keys.Count > 0 ? _keys.Dequeue() : "";
    }

    private static BrailleCanvas RunOntoCanvas(string source)
    {
        var canvas = new BrailleCanvas();
        var device = new TuiGraphicsDevice(canvas);
        var result = BasicEngine.Run(source, new StringWriter(), stdin: null, filename: "t", default, graphics: device);
        Assert.Equal(0, result.ExitCode);
        return canvas;
    }

    [Fact]
    public void DrawingPopulatesTheBrailleBuffer()
    {
        var canvas = RunOntoCanvas("""
            SET WINDOW 0, 10, 0, 10
            GRAPH LINES: 0, 0; 10, 10
            """);
        Assert.False(canvas.IsEmpty);
        // At least one braille cell has dots set (char above the blank U+2800).
        Assert.Contains(canvas.RenderToText(), c => c > '⠀');
    }

    [Fact]
    public void FilledAreaSetsManyDots()
    {
        var canvas = RunOntoCanvas("""
            SET WINDOW 0, 10, 0, 10
            SET AREA COLOR 2
            GRAPH AREA: 1, 1; 9, 1; 5, 9
            """);
        var dotCells = canvas.RenderToText().Count(c => c > '⠀');
        Assert.True(dotCells > 20, $"expected a filled triangle to set many cells, got {dotCells}");
    }

    [Fact]
    public void TextLabelMarksCanvasNonEmpty()
    {
        var canvas = RunOntoCanvas("""
            SET WINDOW 0, 10, 0, 10
            GRAPH TEXT, AT 1, 5: "hello"
            """);
        Assert.False(canvas.IsEmpty);
    }

    [Fact]
    public void KanbanBoardPopulatesAndPacksWithoutError()
    {
        // kanban draws full-height filled GRAPH AREA columns + many GRAPH TEXT
        // labels — the heaviest canvas use. Run one frame and pack the whole grid to
        // braille (same CellBits path as Redraw, minus the driver). The canvas has a
        // mouse, so the board reads typed keys: Q, Enter quits.
        var src = File.ReadAllText(KanbanPath());
        var canvas = new BrailleCanvas();
        var device = new TuiGraphicsDevice(canvas);
        var keys = new TypedKeys("Q", "\r");
        var result = BasicEngine.Run(src, new StringWriter(), new StringReader(""), "kanban", default, device, keys);
        Assert.Equal(0, result.ExitCode);
        Assert.False(canvas.IsEmpty);
        var text = canvas.RenderToText();
        Assert.Contains(text, c => c > '⠀');
    }

    private static string KanbanPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var p = Path.Combine(dir, "examples", "kanban.bas");
            if (File.Exists(p)) return p;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("examples/kanban.bas not found");
    }

    [Fact]
    public void FrontBufferStaysBlankUntilCommitted()
    {
        // Double buffering (the flicker fix): drawing fills the back buffer, but the
        // front buffer Redraw paints stays blank until Commit() promotes the frame.
        var canvas = new BrailleCanvas();
        var device = new TuiGraphicsDevice(canvas);
        BasicEngine.Run("""
            SET WINDOW 0, 10, 0, 10
            SET AREA COLOR 2
            GRAPH AREA: 1, 1; 9, 1; 9, 9; 1, 9
            """, new StringWriter(), stdin: null, filename: "t", default, graphics: device);

        // Back buffer has the drawing; front buffer is still empty (no flush/commit).
        Assert.Contains(canvas.RenderToText(), c => c > '⠀');
        Assert.DoesNotContain(canvas.RenderFrontToText(), c => c > '⠀');

        canvas.Commit();
        Assert.Contains(canvas.RenderFrontToText(), c => c > '⠀');   // now presented
    }

    [Fact]
    public void ClearEmptiesTheCanvas()
    {
        var canvas = RunOntoCanvas("""
            SET WINDOW 0, 10, 0, 10
            GRAPH LINES: 0, 0; 10, 10
            CLEAR
            """);
        Assert.True(canvas.IsEmpty);
    }

    [Fact]
    public void TheCanvasMouseIsTheProgramsPointer()
    {
        var canvas = new BrailleCanvas();                          // 80 x 24 cells until laid out
        IPointer pointer = new TuiGraphicsDevice(canvas);
        static Terminal.Gui.MouseEvent Mouse(int x, int y, Terminal.Gui.MouseFlags flags) => new() { X = x, Y = y, Flags = flags };

        canvas.MouseEvent(Mouse(0, 23, Terminal.Gui.MouseFlags.ReportMousePosition));
        Assert.True(pointer.TryGetPosition(out var corner));
        Assert.True(corner.X < 0.01 && corner.Y < 0.03);           // bottom-left cell ≈ (0, 0)
        Assert.False(pointer.TryReadEvent(out _));                 // moving isn't a button event

        // A drag: press, moves reported as pressed+position, release.
        canvas.MouseEvent(Mouse(10, 5, Terminal.Gui.MouseFlags.Button1Pressed));
        canvas.MouseEvent(Mouse(20, 5, Terminal.Gui.MouseFlags.Button1Pressed | Terminal.Gui.MouseFlags.ReportMousePosition));
        canvas.MouseEvent(Mouse(30, 5, Terminal.Gui.MouseFlags.Button1Released));
        canvas.MouseEvent(Mouse(30, 5, Terminal.Gui.MouseFlags.Button1Clicked));   // after a seen press: no extra click
        Assert.True(pointer.TryReadEvent(out var press) && press.Pressed);
        Assert.True(pointer.TryReadEvent(out var release) && !release.Pressed);
        Assert.True(release.At.X > press.At.X);
        Assert.False(pointer.TryReadEvent(out _));

        // A terminal that reports a quick click only as "clicked".
        canvas.MouseEvent(Mouse(40, 12, Terminal.Gui.MouseFlags.Button1Clicked));
        Assert.True(pointer.TryReadEvent(out var p) && p.Pressed);
        Assert.True(pointer.TryReadEvent(out var r) && !r.Pressed);
        Assert.InRange(p.At.X, 0.49, 0.52);                        // the middle cell
    }
}
