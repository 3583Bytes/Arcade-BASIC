using System.Text;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Runtime.Tests;

public class AnsiGraphicsDeviceTests
{
    private static AnsiGraphicsDevice Device(StringWriter w, int cols = 80, int rows = 24) =>
        new(w, () => (cols, rows));

    [Fact]
    public void DeviceSizeReservesTheBottomRowForInput()
    {
        var d = Device(new StringWriter(), cols: 80, rows: 24);
        var size = d.DeviceSize;
        Assert.Equal(160, size.Width);     // cols * 2 dots
        Assert.Equal(92, size.Height);     // (rows - 1) * 4 dots — bottom row is the input line
    }

    [Fact]
    public void InertUntilSomethingIsDrawn()
    {
        var w = new StringWriter();
        var d = Device(w);
        d.Clear();          // clearing alone must not touch the terminal
        d.Present();
        d.EndSession();
        Assert.Equal(string.Empty, w.ToString());
        Assert.False(d.Active);
    }

    [Fact]
    public void PresentEntersAlternateScreenAndPaintsBraille()
    {
        var w = new StringWriter();
        var d = Device(w);
        d.Clear();
        d.SetColor(GfxColorTarget.Line, 4);
        d.DrawLines(new GfxPoint[] { new(0, 0), new(1, 1) });   // full-window diagonal
        Assert.True(d.Active);

        d.Present();
        var outp = w.ToString();

        Assert.Contains("\x1b[?1049h", outp);   // entered the alternate screen
        Assert.Contains("\x1b[?25l", outp);      // hid the cursor while painting
        Assert.Contains("\x1b[?25h", outp);      // showed it again for input
        Assert.Contains("\x1b[24;1H", outp);     // positioned at the reserved input row (row 24)
        Assert.Contains("\x1b[94m", outp);       // line colour 4 → bright-blue SGR
        Assert.Contains(outp, c => c >= '⠀' && c <= '⣿');         // at least one braille glyph
    }

    [Fact]
    public void FilledAreaRendersAsSolidBlocksNotSpeckledBraille()
    {
        // A filled shape saturates interior cells (all 8 dots). Those should paint
        // as the solid full block █, which reads as solid colour — not the dense
        // braille glyph ⣿, which reads as stipple.
        var w = new StringWriter();
        var d = Device(w);
        d.Clear();
        d.SetColor(GfxColorTarget.Area, 3);
        d.FillArea(new GfxPoint[] { new(0, 0), new(1, 0), new(1, 1), new(0, 1) });   // whole window
        d.Present();
        var outp = w.ToString();

        Assert.Contains('█', outp);              // solid interior cells
        Assert.DoesNotContain('⣿', outp);        // no fully-set braille glyph survives a fill
    }

    [Fact]
    public void ColoursBeyondTheStandard16UseThe256ColourPalette()
    {
        var w = new StringWriter();
        var d = Device(w, cols: 10, rows: 4);
        d.SetColorMix(40, 1, 0, 0);                        // pure red: xterm 196
        d.DrawCells(new GfxRect(0, 0.5, 0, 1), 1, 1, [40], new GfxRect(0, 0.5, 0, 1));
        d.SetColor(GfxColorTarget.Area, 9);                // a standard colour: the terminal's own
        d.FillArea([new GfxPoint(0.5, 0), new GfxPoint(1, 0), new GfxPoint(1, 1), new GfxPoint(0.5, 1)]);
        d.Present();
        var output = w.ToString();
        Assert.Contains("\x1b[38;5;196m█████", output);
        Assert.Contains("\x1b[31m█████", output);
    }

    [Fact]
    public void EndSessionLeavesTheAlternateScreen()
    {
        var w = new StringWriter();
        var d = Device(w);
        d.DrawText(new GfxPoint(0, 1), "hi");
        d.Present();
        d.EndSession();
        var outp = w.ToString();
        Assert.Contains("hi", outp);             // the label was painted
        Assert.Contains("\x1b[?1049l", outp);    // restored the main screen
    }

    [Fact]
    public void TheMouseIsAskedForWhenAProgramLooksForItAndCountsOnceItReports()
    {
        var w = new StringWriter();
        var d = Device(w, cols: 80, rows: 25);           // 24 rows to draw on
        var pointer = new PointerState(d);
        Assert.Equal(PointerState.NoPointer, pointer.Sample(out _));   // (not allowed to ask)
        Assert.Equal(string.Empty, w.ToString());

        d.MouseReporting = true;
        Assert.Equal(PointerState.NoPointer, pointer.Sample(out _));   // asked; nothing reported yet
        Assert.Equal(PointerState.NoPointer, pointer.Sample(out _));
        Assert.Equal(1, w.ToString().Split("\x1b[?1000h").Length - 1);  // asked once
        Assert.Contains("\x1b[?1006h", w.ToString());                   // with SGR coordinates

        d.MouseReport(35, 41, 13, released: false);      // moved (no button) to column 41, row 13
        Assert.Equal(PointerState.Up, pointer.Sample(out var at));
        Assert.Equal(40.5 / 80, at.X, 9);                 // the middle of that cell
        Assert.Equal(1 - 12.5 / 24, at.Y, 9);

        d.MouseReport(0, 1, 1, released: false);         // pressed in the top-left cell,
        d.MouseReport(32, 2, 1, released: false);        // dragged a cell right,
        d.MouseReport(0, 2, 1, released: true);          // let go
        Assert.Equal(PointerState.Pressed, pointer.Sample(out at));
        Assert.Equal(0.5 / 80, at.X, 9);
        Assert.Equal(PointerState.Released, pointer.Sample(out at));
        Assert.Equal(1.5 / 80, at.X, 9);
        d.MouseReport(64, 2, 1, released: false);        // the wheel, and the right button: no clicks
        d.MouseReport(2, 2, 1, released: false);
        Assert.Equal(PointerState.Up, pointer.Sample(out _));

        d.EndSession();
        Assert.Contains("\x1b[?1000l", w.ToString());   // stopped
    }

    [Fact]
    public void NotDirtyAfterClearAloneStaysInert()
    {
        var w = new StringWriter();
        var d = Device(w);
        d.Clear();
        d.Clear();
        Assert.False(d.Active);
        d.Present();
        Assert.Equal(string.Empty, w.ToString());
    }
}
