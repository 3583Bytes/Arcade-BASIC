using Singulink.Numerics;

namespace ArcadeBasic.Runtime;

/// <summary>
/// Shared BASIC-style numeric formatting for PRINT output. Lives in Runtime
/// so the tree-walking interpreter and the bytecode VM call the same code —
/// keeping their output byte-identical without each side reinventing the
/// rounding / zero-trim rules.
/// </summary>
public static class DisplayFormat
{
    /// <summary>Significant-digit cap for PRINT output. ISO 10279 mandates
    /// at least 6 significant digits; 9 is a reasonable default that keeps
    /// long BigDecimal arithmetic tails from leaking into user output.</summary>
    public const int DisplaySignificantDigits = 9;

    /// <summary>BASIC-style numeric formatting: leading space for non-negative,
    /// trailing space always, value rounded/trimmed to <see cref="DisplaySignificantDigits"/>.</summary>
    public static string FormatNumeric(BigDecimal x)
    {
        var rounded = RoundForDisplay(x);
        var s = rounded.ToString();
        if (s.Contains('.'))
        {
            s = s.TrimEnd('0').TrimEnd('.');
            if (s.Length == 0 || s == "-") s = "0";
        }
        return x >= BigDecimal.Zero ? " " + s + " " : s + " ";
    }

    /// <summary>Round to <see cref="DisplaySignificantDigits"/> significant digits
    /// (treating leading zeros after the decimal as negative integer digits, so
    /// 0.0000123 still keeps 9 significant figures of the fractional part).</summary>
    public static BigDecimal RoundForDisplay(BigDecimal x)
    {
        if (x == BigDecimal.Zero) return x;
        var s = BigDecimal.Abs(x).ToString();
        var dot = s.IndexOf('.');
        int intDigits;
        if (dot < 0)
        {
            intDigits = s.Length;
        }
        else if (dot == 1 && s[0] == '0')
        {
            intDigits = 1;
            for (var i = dot + 1; i < s.Length && s[i] == '0'; i++) intDigits--;
        }
        else
        {
            intDigits = dot;
        }
        if (intDigits >= DisplaySignificantDigits)
        {
            return BigDecimal.Round(x, 0, RoundingMode.MidpointToEven);
        }
        return BigDecimal.Round(x, DisplaySignificantDigits - intDigits, RoundingMode.MidpointToEven);
    }
}
