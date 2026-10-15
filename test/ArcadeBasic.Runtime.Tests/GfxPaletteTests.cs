using ArcadeBasic.Runtime;

namespace ArcadeBasic.Runtime.Tests;

/// <summary>
/// <see cref="GfxPalette"/>: the 16 standard colours, then the xterm palette, and how
/// SET COLOR MIX colours are approximated on terminals.
/// </summary>
public class GfxPaletteTests
{
    [Theory]
    [InlineData(0, 0xFF000000u)]   // black background
    [InlineData(1, 0xFFFFFFFFu)]   // white
    [InlineData(14, 0xFFAA5500u)]  // brown
    [InlineData(16, 0xFF000000u)]  // the colour cube starts at black ...
    [InlineData(21, 0xFF0000FFu)]  // ... blue at full
    [InlineData(196, 0xFFFF0000u)] // ... red at full
    [InlineData(231, 0xFFFFFFFFu)] // ... and ends at white
    [InlineData(232, 0xFF080808u)] // then the greys
    [InlineData(255, 0xFFEEEEEEu)]
    public void StartsWithTheStandardColoursThenXterms(int index, uint argb)
    {
        Assert.Equal(unchecked((int)argb), new GfxPalette().Argb(index));
        Assert.Equal(unchecked((int)argb), GfxPalette.DefaultArgb(index));
    }

    [Fact]
    public void AMixReplacesAColourUntilReset()
    {
        var p = new GfxPalette();
        Assert.True(p.IsStandard(2));
        p.Set(2, 0, 0.5, 1);
        Assert.False(p.IsStandard(2));
        Assert.Equal(unchecked((int)0xFF0080FF), p.Argb(2));
        p.Reset();
        Assert.True(p.IsStandard(2));
        Assert.Equal(unchecked((int)0xFFFF5555), p.Argb(2));
    }

    [Fact]
    public void TerminalsShowTheNearestColourTheyHave()
    {
        var p = new GfxPalette();
        Assert.Equal(9, p.Nearest16(9));                 // a standard colour is itself
        Assert.Equal(196, p.NearestXterm(196));          // an xterm colour is itself
        Assert.Equal(203, p.NearestXterm(2));            // bright red #FF5555 → #FF5F5F
        p.Set(40, 0.9, 0.35, 0.35);                      // a light red
        Assert.Equal(2, p.Nearest16(40));
        p.Set(41, 0.6, 0.3, 0.05);                       // a brown
        Assert.Equal(14, p.Nearest16(41));
        Assert.Equal(1, p.Nearest16(-3));                // out of range: the default colour
    }
}
