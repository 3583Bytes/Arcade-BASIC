namespace ArcadeBasic.Runtime;

/// <summary>
/// The 256 colours a §13 colour index can name, and what <c>SET COLOR MIX</c> has
/// made of them. Indices 0–15 are the 16 colours every backend has always drawn
/// (0 the black background, 1 white, then the bright and dark primaries); 16–255
/// default to the xterm 256-colour palette (a 6 × 6 × 6 colour cube, then 24 greys),
/// so a terminal can show them exactly until they're remixed. Backends that can't
/// show any colour use <see cref="Nearest16"/> or <see cref="NearestXterm"/>.
/// </summary>
public sealed class GfxPalette
{
    public const int Count = 256;

    private static readonly int[] s_default = BuildDefault();

    private readonly int[] _argb = (int[])s_default.Clone();
    private readonly bool[] _mixed = new bool[Count];
    private readonly int[] _nearest16 = Filled(-1);
    private readonly int[] _nearestXterm = Filled(-1);

    /// <summary>Back to the starting colours (a new program).</summary>
    public void Reset()
    {
        Array.Copy(s_default, _argb, Count);
        Array.Clear(_mixed, 0, Count);
        Array.Fill(_nearest16, -1);
        Array.Fill(_nearestXterm, -1);
    }

    /// <summary>SET COLOR MIX: intensities 0–1 (the caller has clamped them).</summary>
    public void Set(int index, double red, double green, double blue)
    {
        if ((uint)index >= Count) return;
        _argb[index] = ToArgb(red, green, blue);
        _mixed[index] = true;
        _nearest16[index] = _nearestXterm[index] = -1;
    }

    /// <summary>The colour an index shows, as 0xAARRGGBB (an index out of range shows as 1).</summary>
    public int Argb(int index) => _argb[(uint)index < Count ? index : 1];

    /// <summary>Is this one of the 16 standard colours, still as it started?</summary>
    public bool IsStandard(int index) => (uint)index < 16 && !_mixed[index];

    /// <summary>The standard colour (0–15) closest to what <paramref name="index"/> shows — for a
    /// 16-colour terminal.</summary>
    public int Nearest16(int index)
    {
        if (IsStandard(index)) return index;
        if ((uint)index >= Count) return 1;
        if (_nearest16[index] < 0) _nearest16[index] = Nearest(_argb[index], 0, 15);   // (15 is 1 again)
        return _nearest16[index];
    }

    /// <summary>The xterm palette entry (16–255) closest to what <paramref name="index"/> shows —
    /// for a 256-colour terminal (<c>ESC[38;5;nm</c>). Entries 0–15 aren't used: terminals
    /// theme those.</summary>
    public int NearestXterm(int index)
    {
        if ((uint)index >= Count) index = 1;
        if (_nearestXterm[index] < 0) _nearestXterm[index] = Nearest(_argb[index], 16, Count);
        return _nearestXterm[index];
    }

    /// <summary>A colour's starting value, as 0xAARRGGBB.</summary>
    public static int DefaultArgb(int index) => s_default[(uint)index < Count ? index : 1];

    /// <summary>A colour's starting intensities, each 0–1 (what ASK COLOR MIX reports before
    /// any SET COLOR MIX).</summary>
    public static (double Red, double Green, double Blue) DefaultMix(int index)
    {
        var c = DefaultArgb(index);
        return (((c >> 16) & 0xFF) / 255.0, ((c >> 8) & 0xFF) / 255.0, (c & 0xFF) / 255.0);
    }

    public static int ToArgb(double red, double green, double blue) =>
        unchecked((int)0xFF000000) | (Byte(red) << 16) | (Byte(green) << 8) | Byte(blue);

    private static int Byte(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);

    /// <summary>The default palette entry in [from, to) nearest to <paramref name="argb"/>,
    /// weighting green over red over blue the way the eye does.</summary>
    private static int Nearest(int argb, int from, int to)
    {
        int r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
        var best = from;
        var bestDistance = long.MaxValue;
        for (var i = from; i < to; i++)
        {
            var c = s_default[i];
            long dr = r - ((c >> 16) & 0xFF), dg = g - ((c >> 8) & 0xFF), db = b - (c & 0xFF);
            var d = 3 * dr * dr + 4 * dg * dg + 2 * db * db;
            if (d < bestDistance) { bestDistance = d; best = i; }
        }
        return best;
    }

    private static int[] Filled(int value)
    {
        var a = new int[Count];
        Array.Fill(a, value);
        return a;
    }

    private static int[] BuildDefault()
    {
        var p = new int[Count];
        uint[] standard =
        [
            0xFF000000,   // 0  black: the background
            0xFFFFFFFF,   // 1  white (the default drawing colour)
            0xFFFF5555,   // 2  bright red
            0xFF55FF55,   // 3  bright green
            0xFF5555FF,   // 4  bright blue
            0xFFFF55FF,   // 5  bright magenta
            0xFF55FFFF,   // 6  bright cyan
            0xFFFFFF55,   // 7  bright yellow
            0xFFAAAAAA,   // 8  grey
            0xFFAA0000,   // 9  red
            0xFF00AA00,   // 10 green
            0xFF0000AA,   // 11 blue
            0xFFAA00AA,   // 12 magenta
            0xFF00AAAA,   // 13 cyan
            0xFFAA5500,   // 14 brown
            0xFFFFFFFF,   // 15 white
        ];
        for (var i = 0; i < 16; i++) p[i] = unchecked((int)standard[i]);
        int[] levels = [0, 95, 135, 175, 215, 255];
        for (var i = 0; i < 216; i++)
            p[16 + i] = unchecked((int)0xFF000000) | (levels[i / 36] << 16) | (levels[i / 6 % 6] << 8) | levels[i % 6];
        for (var i = 0; i < 24; i++)
        {
            var v = 8 + 10 * i;
            p[232 + i] = unchecked((int)0xFF000000) | (v << 16) | (v << 8) | v;
        }
        return p;
    }
}
