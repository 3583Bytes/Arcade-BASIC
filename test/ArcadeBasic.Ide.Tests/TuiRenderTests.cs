using ArcadeBasic.Ide;
using ArcadeBasic.Interpreter;
using Terminal.Gui;

namespace ArcadeBasic.Ide.Tests;

/// <summary>
/// Reproduces the main-thread render path under Terminal.Gui's FakeDriver, which
/// (unlike the real console driver) runs headlessly. This exercises the parts
/// of the Graphics pane that need a driver — AddRune/SetAttribute in Redraw —
/// that the buffer-only tests can't reach.
/// </summary>
[Collection("tui")]
public class TuiRenderTests
{
    [Fact]
    public void RenderingKanbanUnderFakeDriverDoesNotThrow()
    {
        Application.Init(new FakeDriver());
        try
        {
            var pane = new GraphicsPane { Width = Dim.Fill(), Height = Dim.Fill() };
            Application.Top.Add(pane);
            Application.Begin(Application.Top);
            Application.Top.LayoutSubviews();

            var device = new TuiGraphicsDevice(pane.Canvas);
            var src = File.ReadAllText(KanbanPath());
            var result = BasicEngine.Run(src, new StringWriter(), new StringReader("Q\n"), "k", default, device);
            Assert.Equal(0, result.ExitCode);

            // The crashing operation in the real app: draw the populated canvas.
            pane.Redraw(pane.Bounds);
        }
        finally
        {
            Application.Shutdown();
        }
    }

    [Fact]
    public void GraphicsPaneInputAffordanceWorksUnderFakeDriver()
    {
        Application.Init(new FakeDriver());
        try
        {
            var pane = new GraphicsPane { Width = Dim.Fill(), Height = Dim.Fill() };
            Application.Top.Add(pane);
            Application.Begin(Application.Top);
            Application.Top.LayoutSubviews();
            pane.Canvas.Plot(5, 5, 1);

            string? got = "sentinel";
            pane.BeginRead(t => got = t);   // SetFocus + cursor visibility through the driver
            pane.CancelRead();              // completes the read with null
            Assert.Null(got);
        }
        finally
        {
            Application.Shutdown();
        }
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
}
