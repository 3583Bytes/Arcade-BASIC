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

    [Fact]
    public void CanvasFitsItsPaneAndDeviceReportsThatSize()
    {
        Application.Init(new FakeDriver());
        try
        {
            // A deterministic, smaller-than-default pane. After layout the dot
            // grid should track the pane bounds (not stay at the 160×96 default),
            // so the board fills the available space rather than a fixed corner.
            var pane = new GraphicsPane { X = 0, Y = 0, Width = 40, Height = 20 };
            Application.Top.Add(pane);
            Application.Begin(Application.Top);
            Application.Top.LayoutSubviews();

            var canvas = pane.Canvas;
            Assert.True(canvas.PixelWidth < BrailleCanvas.DefaultDotsW,
                $"expected the grid to shrink to the pane, got {canvas.PixelWidth}");
            Assert.Equal(0, canvas.PixelWidth % 2);    // 2 dots per cell
            Assert.Equal(0, canvas.PixelHeight % 4);   // 4 dots per cell

            // ASK DEVICE SIZE (via the device) must report the live grid so a
            // program can lay itself out to fit.
            var device = new TuiGraphicsDevice(canvas);
            Assert.Equal(canvas.PixelWidth, (int)device.DeviceSize.Width);
            Assert.Equal(canvas.PixelHeight, (int)device.DeviceSize.Height);

            // A full-window line reaches the far corner of the resized grid.
            var src = "SET WINDOW 0, 1, 0, 1\nGRAPH LINES: 0, 0; 1, 1\n";
            BasicEngine.Run(src, new StringWriter(), stdin: null, "fit", default, device);
            Assert.False(canvas.IsEmpty);
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
