namespace ArcadeBasic.Runtime;

/// <summary>
/// How wide text is, for a device that can't measure it (SVG, tests, no device): in ems —
/// multiples of the text's height. A monospace character is 0.6 of an em; a sans-serif one
/// is as wide as Helvetica's (its widths, in thousandths of an em, for printable ASCII),
/// which is close to every common sans-serif. Bold is a little wider.
/// </summary>
public static class GfxTextMetrics
{
    public const double MonospaceEm = 0.6;

    // Helvetica's advance widths for ' ' (32) to '~' (126).
    private static readonly short[] s_sans =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,   // space ! " # $ % & ' ( ) * + , - . /
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,   // 0-9 : ; < = > ?
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,  // @ A-O
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,   // P-Z [ \ ] ^ _
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,   // ` a-o
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,        // p-z { | } ~
    ];

    /// <summary>The width of <paramref name="text"/> in ems.</summary>
    public static double Ems(string text, bool sans, bool bold)
    {
        if (!sans) return text.Length * MonospaceEm;
        var thousandths = 0;
        foreach (var c in text) thousandths += c is >= ' ' and <= '~' ? s_sans[c - ' '] : 556;
        return thousandths / 1000.0 * (bold ? 1.06 : 1);
    }
}
