namespace ArcadeBasic.Runtime;

/// <summary>
/// A compact 5×7 bitmap font for rasterizing <c>GRAPH TEXT</c> on pixel backends
/// (the Unity texture device) where there is no terminal cell font to lean on.
/// Glyphs are stored as 7 rows of 5 bits (bit 4 = leftmost pixel). Letters are
/// upper-case only — lowercase folds to uppercase (a deliberate retro choice;
/// 5×7 lowercase with descenders is cramped). Undefined characters draw nothing.
///
/// Pure int/byte data, so it is netstandard2.1- and IL2CPP-safe.
/// </summary>
public sealed class BitmapFont
{
    public const int Width = 5;
    public const int Height = 7;
    /// <summary>Pixels to advance per character (glyph width + 1 spacing column).</summary>
    public const int Advance = 6;

    public static readonly BitmapFont Default = new();

    private readonly Dictionary<char, byte[]> _glyphs = Build();

    /// <summary>Plot the set pixels of <paramref name="c"/> with its top-left at
    /// (<paramref name="x"/>, <paramref name="y"/>) via the callback.</summary>
    public void Draw(char c, int x, int y, Action<int, int> plot)
    {
        var g = Lookup(c);
        if (g is null) return;
        for (var row = 0; row < Height; row++)
        {
            int bits = g[row];
            for (var col = 0; col < Width; col++)
                if ((bits & (1 << (Width - 1 - col))) != 0)
                    plot(x + col, y + row);
        }
    }

    private byte[]? Lookup(char c)
    {
        c = Fold(c);
        return _glyphs.TryGetValue(c, out var g) ? g : null;
    }

    private static char Fold(char c)
    {
        if (c >= 'a' && c <= 'z') return (char)(c - 32);   // lowercase → uppercase
        return c switch
        {
            '—' or '–' => '-',   // em/en dash → hyphen
            '·' => '.',               // middle dot → period
            _ => c,
        };
    }

    private static Dictionary<char, byte[]> Build()
    {
        var t = new Dictionary<char, byte[]>();
        void G(char c, params byte[] rows) => t[c] = rows;

        G(' ', 0, 0, 0, 0, 0, 0, 0);

        // Digits
        G('0', 0b01110, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0b01110);
        G('1', 0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110);
        G('2', 0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b01000, 0b11111);
        G('3', 0b11111, 0b00010, 0b00100, 0b00010, 0b00001, 0b10001, 0b01110);
        G('4', 0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010);
        G('5', 0b11111, 0b10000, 0b11110, 0b00001, 0b00001, 0b10001, 0b01110);
        G('6', 0b00110, 0b01000, 0b10000, 0b11110, 0b10001, 0b10001, 0b01110);
        G('7', 0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b01000, 0b01000);
        G('8', 0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110);
        G('9', 0b01110, 0b10001, 0b10001, 0b01111, 0b00001, 0b00010, 0b01100);

        // Letters A–Z
        G('A', 0b01110, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001);
        G('B', 0b11110, 0b10001, 0b10001, 0b11110, 0b10001, 0b10001, 0b11110);
        G('C', 0b01110, 0b10001, 0b10000, 0b10000, 0b10000, 0b10001, 0b01110);
        G('D', 0b11100, 0b10010, 0b10001, 0b10001, 0b10001, 0b10010, 0b11100);
        G('E', 0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b11111);
        G('F', 0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b10000);
        G('G', 0b01110, 0b10001, 0b10000, 0b10111, 0b10001, 0b10001, 0b01111);
        G('H', 0b10001, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001);
        G('I', 0b01110, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110);
        G('J', 0b00111, 0b00010, 0b00010, 0b00010, 0b00010, 0b10010, 0b01100);
        G('K', 0b10001, 0b10010, 0b10100, 0b11000, 0b10100, 0b10010, 0b10001);
        G('L', 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b11111);
        G('M', 0b10001, 0b11011, 0b10101, 0b10101, 0b10001, 0b10001, 0b10001);
        G('N', 0b10001, 0b10001, 0b11001, 0b10101, 0b10011, 0b10001, 0b10001);
        G('O', 0b01110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110);
        G('P', 0b11110, 0b10001, 0b10001, 0b11110, 0b10000, 0b10000, 0b10000);
        G('Q', 0b01110, 0b10001, 0b10001, 0b10001, 0b10101, 0b10010, 0b01101);
        G('R', 0b11110, 0b10001, 0b10001, 0b11110, 0b10100, 0b10010, 0b10001);
        G('S', 0b01111, 0b10000, 0b10000, 0b01110, 0b00001, 0b00001, 0b11110);
        G('T', 0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100);
        G('U', 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110);
        G('V', 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01010, 0b00100);
        G('W', 0b10001, 0b10001, 0b10001, 0b10101, 0b10101, 0b11011, 0b10001);
        G('X', 0b10001, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001, 0b10001);
        G('Y', 0b10001, 0b10001, 0b01010, 0b00100, 0b00100, 0b00100, 0b00100);
        G('Z', 0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b10000, 0b11111);

        // Punctuation
        G('.', 0, 0, 0, 0, 0, 0b00110, 0b00110);
        G(',', 0, 0, 0, 0, 0b00110, 0b00100, 0b01000);
        G(':', 0, 0b00110, 0b00110, 0, 0b00110, 0b00110, 0);
        G(';', 0, 0b00110, 0b00110, 0, 0b00110, 0b00100, 0b01000);
        G('!', 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0, 0b00100);
        G('?', 0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0, 0b00100);
        G('-', 0, 0, 0, 0b11111, 0, 0, 0);
        G('+', 0, 0b00100, 0b00100, 0b11111, 0b00100, 0b00100, 0);
        G('=', 0, 0, 0b11111, 0, 0b11111, 0, 0);
        G('*', 0, 0b00100, 0b10101, 0b01110, 0b10101, 0b00100, 0);
        G('/', 0b00001, 0b00010, 0b00100, 0b00100, 0b00100, 0b01000, 0b10000);
        G('\\', 0b10000, 0b01000, 0b00100, 0b00100, 0b00100, 0b00010, 0b00001);
        G('(', 0b00010, 0b00100, 0b01000, 0b01000, 0b01000, 0b00100, 0b00010);
        G(')', 0b01000, 0b00100, 0b00010, 0b00010, 0b00010, 0b00100, 0b01000);
        G('<', 0b00010, 0b00100, 0b01000, 0b10000, 0b01000, 0b00100, 0b00010);
        G('>', 0b01000, 0b00100, 0b00010, 0b00001, 0b00010, 0b00100, 0b01000);
        G('[', 0b01110, 0b01000, 0b01000, 0b01000, 0b01000, 0b01000, 0b01110);
        G(']', 0b01110, 0b00010, 0b00010, 0b00010, 0b00010, 0b00010, 0b01110);
        G('#', 0b01010, 0b01010, 0b11111, 0b01010, 0b11111, 0b01010, 0b01010);
        G('&', 0b01100, 0b10010, 0b10100, 0b01000, 0b10101, 0b10010, 0b01101);
        G('\'', 0b00100, 0b00100, 0b01000, 0, 0, 0, 0);
        G('"', 0b01010, 0b01010, 0b01010, 0, 0, 0, 0);
        G('$', 0b00100, 0b01111, 0b10100, 0b01110, 0b00101, 0b11110, 0b00100);
        G('%', 0b11000, 0b11001, 0b00010, 0b00100, 0b01000, 0b10011, 0b00011);
        G('@', 0b01110, 0b10001, 0b10111, 0b10101, 0b10111, 0b10000, 0b01110);
        G('_', 0, 0, 0, 0, 0, 0, 0b11111);

        return t;
    }
}
