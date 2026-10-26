using ArcadeBasic.Runtime;

namespace ArcadeBasic.Runtime.Tests;

/// <summary>
/// <see cref="CellSurface"/>: straight lines become box-drawing characters, fills
/// become blocks, slanted lines Braille, text sits in the cell over its baseline, and
/// later drawing of another kind replaces earlier drawing.
/// </summary>
public class CellSurfaceTests
{
    /// <summary>A 10 × 5-cell surface addressed in cell units: x 0–10 across, y 0–5 up,
    /// so (c + 0.5, r) is the middle of column c at height r.</summary>
    private static CellSurface Surface() => new(10, 5);

    private static GfxPoint P(double x, double y) => new(x / 10, y / 5);

    private static string Rows(CellSurface s) => s.ToText().TrimEnd('\n');

    [Fact]
    public void ARectangleDrawsAsABoxWithEvenLines()
    {
        var s = Surface();
        s.Lines([P(1.5, 0.5), P(8.5, 0.5), P(8.5, 4.5), P(1.5, 4.5), P(1.5, 0.5)], 3, 1);
        Assert.Equal(
            " ┌──────┐ \n" +
            " │      │ \n" +
            " │      │ \n" +
            " │      │ \n" +
            " └──────┘ ", Rows(s));
        Assert.Equal(3, s[1, 0].Color);
    }

    [Fact]
    public void CrossingLinesJoin()
    {
        var s = Surface();
        s.Lines([P(0.5, 2.5), P(9.5, 2.5)], 1, 1);
        s.Lines([P(4.5, 0.5), P(4.5, 4.5)], 1, 1);
        Assert.Equal("────┼─────", Rows(s).Split('\n')[2]);
        Assert.Equal('┼', s.Glyph(4, 2));                          // a 4-way join where they cross
    }

    [Fact]
    public void LineStylesUseDashedBoxCharacters()
    {
        var s = Surface();
        s.Lines([P(0.5, 4.5), P(4.5, 4.5)], 1, 2);
        s.Lines([P(0.5, 2.5), P(4.5, 2.5)], 1, 3);
        var rows = Rows(s).Split('\n');
        Assert.Equal("┄┄┄┄┄", rows[0][..5]);
        Assert.Equal("┈┈┈┈┈", rows[2][..5]);
    }

    [Fact]
    public void FillsAreSolidBlocksAndColourZeroErases()
    {
        var s = Surface();
        s.Area([P(0, 0), P(10, 0), P(10, 5), P(0, 5)], 2);
        Assert.All(Rows(s).Replace("\n", ""), c => Assert.Equal('█', c));

        s.Area([P(2, 1), P(8, 1), P(8, 4), P(2, 4)], 0);          // a black card over it
        Assert.Equal(' ', s.Glyph(5, 2));
        Assert.Equal('█', s.Glyph(0, 2));
    }

    [Fact]
    public void ANarrowFillUsesHalfBlocks()
    {
        var s = Surface();
        s.Area([P(3, 0), P(3.4, 0), P(3.4, 5), P(3, 5)], 4);       // the left dot column of cell 3
        Assert.All(Enumerable.Range(0, 5), r => Assert.Equal('▌', s.Glyph(3, r)));
    }

    [Fact]
    public void SlantedLinesAreBraille()
    {
        var s = Surface();
        s.Lines([P(0, 0), P(10, 5)], 1, 1);
        Assert.Contains(s.ToText(), c => c is > '⠀' and <= '⣿');
        Assert.DoesNotContain(s.ToText(), c => c is '─' or '│');
    }

    [Fact]
    public void TextSitsInTheCellAboveItsBaseline()
    {
        var s = Surface();
        s.Text(P(1, 3), "hi", 7);                                  // baseline at the bottom of row 1 (y = 3)
        Assert.Equal(" hi       ", Rows(s).Split('\n')[1]);
        Assert.Equal(7, s[1, 1].Color);
    }

    /// <summary>A rectangle in the surface's cell units (x 0–10, y 0–5).</summary>
    private static GfxRect R(double left, double right, double bottom, double top) => new(left / 10, right / 10, bottom / 5, top / 5);

    [Fact]
    public void CellsLightEachQuadrantWithTheColourUnderIt()
    {
        var s = Surface();
        // A 2 x 2 array over character (0, 0): top-left 3, top-right 0, bottom-left 3,
        // bottom-right 5. Colour 0 leaves its quadrant dark; the character shows the
        // colour most of its lit quadrants have.
        s.Cells(R(0, 1, 4, 5), 2, 2, [3, 0, 3, 5], R(0, 10, 0, 5));
        Assert.Equal('▙', s.Glyph(0, 0));
        Assert.Equal(3, s[0, 0].Color);
        Assert.Equal(' ', s.Glyph(1, 0));
    }

    [Fact]
    public void CellsCoverWhatWasThere()
    {
        var s = Surface();
        s.Text(P(0, 3), "xxxxxxxxxx", 1);
        s.Area([P(0, 0), P(10, 0), P(10, 2), P(0, 2)], 2);
        // One array across the middle of the screen, white on the left, black (0) on the right.
        s.Cells(R(0, 10, 1, 4), 2, 1, [1, 0], R(0, 10, 0, 5));
        Assert.Equal("█████     ", Rows(s).Split('\n')[1]);   // the text row, covered
        Assert.Equal("█████     ", Rows(s).Split('\n')[3]);   // the filled area, covered
        Assert.Equal("██████████", Rows(s).Split('\n')[4]);   // below the array: untouched
    }

    [Fact]
    public void SeeThroughCellsLeaveWhatsThere()
    {
        var s = Surface();
        s.Area([P(0, 0), P(10, 0), P(10, 5), P(0, 5)], 2);
        s.Cells(R(0, 1, 4, 5), 2, 2, [-1, 5, -1, 5], R(0, 10, 0, 5));   // a sprite: left half see-through
        Assert.Equal('█', s.Glyph(0, 0));                              // the left half still filled
        Assert.Equal(5, s[0, 0].Color);                                // (one colour: the sprite's)
        s.Cells(R(1, 2, 4, 5), 1, 1, [-1], R(0, 10, 0, 5));             // all see-through: nothing changes
        Assert.Equal(2, s[1, 0].Color);
    }

    [Fact]
    public void CellsAreCutToWhatsVisible()
    {
        var s = Surface();
        s.Cells(R(0, 10, 0, 5), 1, 1, [4], R(0, 3, 0, 5));
        Assert.All(Rows(s).Split('\n'), row => Assert.Equal("███       ", row));
        Assert.Equal(4, s[2, 2].Color);
    }

    [Fact]
    public void ResizingKeepsTheCharactersWhereTheyAre()
    {
        var s = Surface();
        s.Lines([P(1.5, 0.5), P(8.5, 0.5), P(8.5, 4.5), P(1.5, 4.5), P(1.5, 0.5)], 3, 1);
        var box = Rows(s).Split('\n');

        s.Resize(13, 7);                                           // bigger: padded right and below
        var rows = Rows(s).Split('\n');
        Assert.All(Enumerable.Range(0, 5), r => Assert.Equal(box[r] + "   ", rows[r]));
        Assert.Equal(new string(' ', 13), rows[6]);

        s.Resize(6, 3);                                            // smaller: cropped
        Assert.Equal(" ┌────\n │    \n │    ", Rows(s));
    }

    [Fact]
    public void LaterDrawingOfAnotherKindReplacesEarlier()
    {
        var s = Surface();
        s.Text(P(0, 3), "xxxxxxxxxx", 1);
        s.Lines([P(0.5, 3.5), P(9.5, 3.5)], 1, 1);                 // a line through the text's row
        Assert.Equal("──────────", Rows(s).Split('\n')[1]);
        s.Text(P(2, 3), "ok", 1);                                  // and text back over the line
        Assert.Equal("──ok──────", Rows(s).Split('\n')[1]);
    }
}
