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
