using ArcadeBasic.Runtime;

namespace ArcadeBasic.Runtime.Tests;

public class BitmapFontTests
{
    private static List<(int X, int Y)> Pixels(char c)
    {
        var pts = new List<(int X, int Y)>();
        BitmapFont.Default.Draw(c, 0, 0, (x, y) => pts.Add((x, y)));
        return pts;
    }

    [Fact]
    public void SpaceAndUnknownDrawNothing()
    {
        Assert.Empty(Pixels(' '));
        Assert.Empty(Pixels('☃'));   // snowman — undefined → nothing
    }

    [Fact]
    public void GlyphsFitWithinTheFiveBySevenCell()
    {
        foreach (var (x, y) in Pixels('A'))
        {
            Assert.InRange(x, 0, BitmapFont.Width - 1);
            Assert.InRange(y, 0, BitmapFont.Height - 1);
        }
    }

    [Fact]
    public void LowercaseFoldsToUppercase()
    {
        Assert.Equal(Pixels('A'), Pixels('a'));
        Assert.Equal(Pixels('Z'), Pixels('z'));
    }

    [Fact]
    public void TopRowOfCapitalAMatchesItsBitPattern()
    {
        // 'A' row 0 is 0b01110 → columns 1,2,3 set on the top row.
        var top = Pixels('A').Where(p => p.Y == 0).Select(p => p.X).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, top);
    }

    [Fact]
    public void DrawOffsetsByPosition()
    {
        var pts = new List<(int X, int Y)>();
        BitmapFont.Default.Draw('A', 10, 20, (x, y) => pts.Add((x, y)));
        Assert.All(pts, p => Assert.InRange(p.X, 10, 10 + BitmapFont.Width - 1));
        Assert.All(pts, p => Assert.InRange(p.Y, 20, 20 + BitmapFont.Height - 1));
    }
}
