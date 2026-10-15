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
    private sealed class TypedKeys(params string[] keys) : ArcadeBasic.Runtime.IKeyboard
    {
        private readonly Queue<string> _keys = new(keys);
        public string ReadKey() => _keys.Count > 0 ? _keys.Dequeue() : "";
    }

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

            // The canvas has a mouse, so the board runs its interactive loop and reads
            // the keyboard: type Q and Enter to quit, as a user would.
            var device = new TuiGraphicsDevice(pane.Canvas);
            var keys = new TypedKeys("Q", "\r");
            var src = File.ReadAllText(KanbanPath());
            var result = BasicEngine.Run(src, new StringWriter(), new StringReader(""), "k", default, device, keys);
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
            pane.Canvas.DrawPoints([new ArcadeBasic.Runtime.GfxPoint(0.1, 0.9)], 1);   // something on the canvas

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

    [Fact]
    public void ALayoutMidFrameKeepsShowingTheLastCompleteFrame()
    {
        // Terminal.Gui lays views out again whenever it likes, not just on a resize.
        // A game clears and redraws every frame, so a layout often lands mid-frame —
        // the screen must keep showing the last committed frame, not the half-drawn one.
        Application.Init(new FakeDriver());
        try
        {
            var pane = new GraphicsPane { Width = Dim.Fill(), Height = Dim.Fill() };
            Application.Top.Add(pane);
            Application.Begin(Application.Top);
            Application.Top.LayoutSubviews();
            var canvas = pane.Canvas;

            ArcadeBasic.Runtime.GfxPoint[] square = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
            canvas.FillArea(square, 4);                    // a whole frame ...
            canvas.Commit();                               // ... presented
            var shown = canvas.RenderFrontToText();

            canvas.ClearBuffer();                          // the next frame, half drawn
            canvas.DrawText(new ArcadeBasic.Runtime.GfxPoint(0, 0.5), "half", 1);
            canvas.Width = Dim.Fill();                     // a layout, at the same size
            Application.Top.LayoutSubviews();
            Assert.Equal(shown, canvas.RenderFrontToText());

            canvas.Commit();                               // until it's presented
            Assert.Contains("half", canvas.RenderFrontToText());
        }
        finally
        {
            Application.Shutdown();
        }
    }

    [Fact]
    public void ACanvasFittedWhileItsTabIsHiddenIsUnchangedWhenTheTabShows()
    {
        // The IDE's case: a run starts, measures the screen and draws while the
        // Graphics tab is hidden; the tab is shown after the first frame. Fitted to
        // the tab area first, the canvas — and the frame on it — stay as drawn.
        Application.Init(new FakeDriver());
        try
        {
            var pane = new GraphicsPane { Width = Dim.Fill(), Height = Dim.Fill() };
            var graphicsTab = new TabView.Tab("Graphics", pane);
            var tabs = new TabView { Width = Dim.Fill(), Height = Dim.Fill() - 1 };
            tabs.AddTab(new TabView.Tab("Source", new TextView { Width = Dim.Fill(), Height = Dim.Fill() }), true);
            tabs.AddTab(graphicsTab, false);
            Application.Top.Add(tabs);
            Application.Begin(Application.Top);
            Application.Top.LayoutSubviews();

            pane.FitTo(tabs.SelectedTab.View.Frame);
            var canvas = pane.Canvas;
            var size = (canvas.PixelWidth, canvas.PixelHeight);
            Assert.NotEqual((BrailleCanvas.DefaultDotsW, BrailleCanvas.DefaultDotsH), size);

            // A box one cell in from the edges of whatever size the screen is.
            var src = "ASK DEVICE SIZE W, H, U$\nLET C = W / 2\nLET R = H / 4\nSET WINDOW 0, C, 0, R\n" +
                      "GRAPH LINES: 1.5, 1.5; C - 1.5, 1.5; C - 1.5, R - 1.5; 1.5, R - 1.5; 1.5, 1.5\n";
            BasicEngine.Run(src, new StringWriter(), stdin: null, "box", default, new TuiGraphicsDevice(canvas));
            canvas.Commit();
            var drawn = canvas.RenderFrontToText().Split('\n');
            Assert.Equal('┌', drawn[1][1]);
            Assert.Equal('┘', drawn[^3][^2]);

            tabs.SelectedTab = graphicsTab;
            Application.Top.LayoutSubviews();
            Assert.Equal(size, (canvas.PixelWidth, canvas.PixelHeight));
            Assert.Equal(drawn, canvas.RenderFrontToText().Split('\n'));
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
