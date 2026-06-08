namespace ArcadeBasic.Runtime;

/// <summary>
/// Maps a 2×4 Braille dot-coverage mask to the glyph used when painting a cell.
/// Braille (U+2800+bits) is ideal for thin line art but renders <em>fills</em> as
/// a speckled dot field. So the fully- and cleanly-covered masks that axis-aligned
/// filled shapes (game sprites, bars, bricks) produce are mapped to solid block
/// glyphs instead — a solid block reads as solid colour, where the equivalent
/// Braille glyph reads as stipple. Everything else (diagonals, sparse line cells)
/// stays Braille. Shared by the CLI <see cref="AnsiGraphicsDevice"/> and the IDE's
/// BrailleCanvas so both backends render identically.
///
/// Braille bit layout (matches the CellBits packing in both backends):
/// <code>
///   col0 col1        bit0  bit3   (row0)
///    .    .          bit1  bit4   (row1)
///    .    .          bit2  bit5   (row2)
///    .    .          bit6  bit7   (row3)
/// </code>
/// </summary>
public static class CellGlyph
{
    private const int Left = 0x47;    // col0, all four rows  → ▌
    private const int Right = 0xB8;   // col1, all four rows  → ▐
    private const int Top = 0x1B;     // both cols, rows 0–1  → ▀
    private const int Bottom = 0xE4;  // both cols, rows 2–3  → ▄

    /// <summary>The glyph for a cell with the given dot mask (0 → space).</summary>
    public static char ForBits(int bits) => bits switch
    {
        0x00 => ' ',
        0xFF => '█',   // █ full block
        Top => '▀',    // ▀ upper half
        Bottom => '▄', // ▄ lower half
        Left => '▌',   // ▌ left half
        Right => '▐',  // ▐ right half
        _ => (char)(0x2800 + bits),
    };
}
