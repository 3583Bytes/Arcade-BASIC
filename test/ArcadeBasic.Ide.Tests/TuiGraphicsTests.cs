using ArcadeBasic.Ide;
using ArcadeBasic.Interpreter;

namespace ArcadeBasic.Ide.Tests;

/// <summary>
/// Headless verification of the terminal graphics backend: a BASIC program runs
/// through <see cref="TuiGraphicsDevice"/> into a <see cref="BrailleCanvas"/>,
/// and we inspect the braille buffer directly (no console driver). The actual
/// on-screen rendering is verified visually in a real terminal.
/// </summary>
public class TuiGraphicsTests
{
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
        // labels — the heaviest canvas use. Run one frame (Q quits) and pack the
        // whole grid to braille (same CellBits path as Redraw, minus the driver).
        var src = File.ReadAllText(KanbanPath());
        var canvas = new BrailleCanvas();
        var device = new TuiGraphicsDevice(canvas);
        var result = BasicEngine.Run(src, new StringWriter(), new StringReader("Q\n"), "kanban", default, device);
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
    public void ClearEmptiesTheCanvas()
    {
        var canvas = RunOntoCanvas("""
            SET WINDOW 0, 10, 0, 10
            GRAPH LINES: 0, 0; 10, 10
            CLEAR
            """);
        Assert.True(canvas.IsEmpty);
    }
}
