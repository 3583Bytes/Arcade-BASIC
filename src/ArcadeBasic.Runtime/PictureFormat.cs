using System.Text;
using Singulink.Numerics;

namespace ArcadeBasic.Runtime;

/// <summary>
/// Picture-string formatter for the "editing" module — implements the portion
/// of ISO 10279's PRINT USING / FORMAT$ that's most-used in practice:
///
///   <c>#</c>   — digit position (space if no digit)
///   <c>0</c>   — digit position (zero-fill)
///   <c>*</c>   — digit position (asterisk-fill, for cheque protection)
///   <c>$</c>   — digit position with a floating currency sign
///   <c>,</c>   — group the integer part with thousands separators
///   <c>.</c>   — decimal point
///   <c>+</c>   — sign placeholder, '+' if non-negative
///   <c>-</c>   — sign placeholder, ' ' if non-negative
///   <c>&lt;####</c> — left-justified string, width = count of #
///   <c>&gt;####</c> — right-justified string
///   <c>=####</c>    — centered string
///
/// Everything else passes through as literal text. If a value's magnitude
/// can't fit in its numeric field, the field is filled with '*' characters
/// (overflow indicator). The exponent field ('^^^^', scaled notation) is not
/// implemented.
/// </summary>
public static class PictureFormat
{
    public abstract record class Part;
    public sealed record class LiteralPart(string Text) : Part;

    /// <summary>
    /// A numeric field. <paramref name="FillChar"/> is ' ' (from '#'), '0', or
    /// '*'. <paramref name="Grouping"/> inserts thousands separators in the
    /// integer part. <paramref name="FloatingDollar"/> prints a single '$'
    /// immediately left of the most-significant digit.
    /// </summary>
    public sealed record class NumberPart(
        int IntDigits, int FracDigits, char FillChar, bool Grouping, bool FloatingDollar,
        SignKind LeadingSign, SignKind TrailingSign) : Part;

    public sealed record class StringPart(int Width, char Justify) : Part;

    public enum SignKind
    {
        None,         // no sign placeholder
        SpaceForPositive,  // '-' in format -> ' ' for non-neg, '-' for neg
        PlusForPositive,   // '+' in format -> '+' for non-neg, '-' for neg
    }

    public static List<Part> Parse(string format)
    {
        var parts = new List<Part>();
        var i = 0;
        var literalBuf = new StringBuilder();

        void FlushLiteral()
        {
            if (literalBuf.Length > 0)
            {
                parts.Add(new LiteralPart(literalBuf.ToString()));
                literalBuf.Clear();
            }
        }

        while (i < format.Length)
        {
            var c = format[i];

            // String field: starts with < > = followed by 1+ '#'.
            if ((c == '<' || c == '>' || c == '=') && i + 1 < format.Length && format[i + 1] == '#')
            {
                FlushLiteral();
                var justify = c;
                i++;
                var width = 0;
                while (i < format.Length && format[i] == '#') { width++; i++; }
                parts.Add(new StringPart(width, justify));
                continue;
            }

            // Numeric field: starts with a digit position (# or 0), or a
            // sign / floating char ('$') / fill char ('*') that is *followed*
            // by another digit position — so a lone '$' or '*' stays literal.
            if (c == '#' || c == '0'
                || ((c == '+' || c == '-' || c == '$' || c == '*')
                    && i + 1 < format.Length && IsDigitPlace(format[i + 1])))
            {
                FlushLiteral();
                var (np, len) = ParseNumberField(format, i);
                parts.Add(np);
                i += len;
                continue;
            }

            literalBuf.Append(c);
            i++;
        }

        FlushLiteral();
        return parts;
    }

    private static bool IsDigitPlace(char c) => c is '#' or '0' or '*' or '$';

    private static (NumberPart Part, int Length) ParseNumberField(string format, int start)
    {
        var i = start;
        var leadingSign = SignKind.None;
        if (format[i] == '+') { leadingSign = SignKind.PlusForPositive; i++; }
        else if (format[i] == '-') { leadingSign = SignKind.SpaceForPositive; i++; }

        var intDigits = 0;
        var zeroFill = false;
        var starFill = false;
        var floatingDollar = false;
        var grouping = false;
        while (i < format.Length)
        {
            var c = format[i];
            if (c == '#') { intDigits++; }
            else if (c == '0') { intDigits++; zeroFill = true; }
            else if (c == '*') { intDigits++; starFill = true; }
            else if (c == '$') { intDigits++; floatingDollar = true; }
            else if (c == ',') { grouping = true; }
            else break;
            i++;
        }

        var fracDigits = 0;
        if (i < format.Length && format[i] == '.')
        {
            i++;
            while (i < format.Length && (format[i] == '#' || format[i] == '0'))
            {
                fracDigits++;
                i++;
            }
        }

        var trailingSign = SignKind.None;
        if (i < format.Length)
        {
            if (format[i] == '+') { trailingSign = SignKind.PlusForPositive; i++; }
            else if (format[i] == '-') { trailingSign = SignKind.SpaceForPositive; i++; }
        }

        // Fill precedence: '*' (cheque protection) wins over '0' over ' '.
        var fillChar = starFill ? '*' : (zeroFill ? '0' : ' ');
        return (new NumberPart(intDigits, fracDigits, fillChar, grouping, floatingDollar, leadingSign, trailingSign), i - start);
    }

    /// <summary>Apply parsed parts to a sequence of values, cycling format parts when items remain.</summary>
    public static string Apply(IReadOnlyList<Part> parts, IReadOnlyList<Value> items)
    {
        var sb = new StringBuilder();
        var idx = 0;
        var partIdx = 0;
        var anyFieldInFormat = parts.Any(p => p is NumberPart or StringPart);

        // First pass: emit until we run out of format parts AND items.
        while (partIdx < parts.Count)
        {
            var p = parts[partIdx++];
            switch (p)
            {
                case LiteralPart lp:
                    sb.Append(lp.Text);
                    break;
                case NumberPart np:
                    if (idx >= items.Count) return sb.ToString();
                    sb.Append(FormatNumber(((NumericValue)items[idx]).V, np));
                    idx++;
                    break;
                case StringPart sp:
                    if (idx >= items.Count) return sb.ToString();
                    sb.Append(FormatString(((StringValue)items[idx]).V, sp));
                    idx++;
                    break;
            }
        }

        // Cycle the format if there are more items than fields.
        while (idx < items.Count && anyFieldInFormat)
        {
            partIdx = 0;
            while (partIdx < parts.Count && idx < items.Count)
            {
                var p = parts[partIdx++];
                switch (p)
                {
                    case LiteralPart lp: sb.Append(lp.Text); break;
                    case NumberPart np:
                        sb.Append(FormatNumber(((NumericValue)items[idx]).V, np));
                        idx++;
                        break;
                    case StringPart sp:
                        sb.Append(FormatString(((StringValue)items[idx]).V, sp));
                        idx++;
                        break;
                }
            }
        }

        return sb.ToString();
    }

    private static string FormatString(string value, StringPart sp)
    {
        if (value.Length >= sp.Width) return value[..sp.Width];
        var pad = sp.Width - value.Length;
        return sp.Justify switch
        {
            '<' => value + new string(' ', pad),
            '>' => new string(' ', pad) + value,
            '=' => new string(' ', pad / 2) + value + new string(' ', pad - pad / 2),
            _ => value,
        };
    }

    private static string FormatNumber(BigDecimal value, NumberPart np)
    {
        var negative = value < BigDecimal.Zero;
        var magnitude = negative ? -value : value;

        // Round to required fractional digits.
        var rounded = BigDecimal.Round(magnitude, np.FracDigits, RoundingMode.MidpointAwayFromZero);
        var str = rounded.ToString();

        // Split into integer and fractional parts.
        string intPart, fracPart;
        var dot = str.IndexOf('.');
        if (dot < 0) { intPart = str; fracPart = ""; }
        else { intPart = str[..dot]; fracPart = str[(dot + 1)..]; }

        if (fracPart.Length < np.FracDigits) fracPart += new string('0', np.FracDigits - fracPart.Length);
        if (fracPart.Length > np.FracDigits) fracPart = fracPart[..np.FracDigits];

        var hasExplicitSign = np.LeadingSign != SignKind.None || np.TrailingSign != SignKind.None;

        // A negative number with no explicit sign slot floats a '-' that
        // consumes one integer digit slot (like the floating '$').
        var floatingMinus = negative && !hasExplicitSign;
        var capacity = np.IntDigits - (floatingMinus ? 1 : 0);

        if (intPart.Length > capacity)
        {
            return new string('*', OverflowWidth(np, hasExplicitSign));
        }

        string intRegion;
        if (np.FillChar == '0')
        {
            // Zero-fill: pad to the full digit width first, then group.
            var padded = intPart.PadLeft(np.IntDigits, '0');
            var prefix = (floatingMinus ? "-" : "") + (np.FloatingDollar ? "$" : "");
            intRegion = prefix + (np.Grouping ? InsertGrouping(padded) : padded);
        }
        else
        {
            // Space / asterisk fill: group the actual digits, float the sign and
            // '$' immediately left of the most-significant digit, and pad the
            // unused leading digit slots with the fill character.
            var grouped = np.Grouping ? InsertGrouping(intPart) : intPart;
            var floatPrefix = (floatingMinus ? "-" : "") + (np.FloatingDollar ? "$" : "");
            var pad = capacity - intPart.Length;
            intRegion = new string(np.FillChar, pad) + floatPrefix + grouped;
        }

        var sb = new StringBuilder();
        if (np.LeadingSign != SignKind.None) sb.Append(ResolveSign(negative, np.LeadingSign));
        sb.Append(intRegion);
        if (np.FracDigits > 0) { sb.Append('.'); sb.Append(fracPart); }
        if (np.TrailingSign != SignKind.None) sb.Append(ResolveSign(negative, np.TrailingSign));
        return sb.ToString();
    }

    /// <summary>Insert thousands separators into a run of decimal digits.</summary>
    private static string InsertGrouping(string digits)
    {
        if (digits.Length <= 3) return digits;
        var sb = new StringBuilder();
        var first = digits.Length % 3;
        if (first == 0) first = 3;
        sb.Append(digits, 0, first);
        for (var k = first; k < digits.Length; k += 3)
        {
            sb.Append(',');
            sb.Append(digits, k, 3);
        }
        return sb.ToString();
    }

    /// <summary>Character width of an overflowed numeric field (filled with '*').</summary>
    private static int OverflowWidth(NumberPart np, bool hasExplicitSign)
    {
        var commas = np.Grouping && np.IntDigits > 1 ? (np.IntDigits - 1) / 3 : 0;
        return np.IntDigits + commas
            + (np.FloatingDollar ? 1 : 0)
            + (hasExplicitSign ? 1 : 0)
            + (np.FracDigits > 0 ? 1 + np.FracDigits : 0);
    }

    private static char ResolveSign(bool negative, SignKind kind) => (kind, negative) switch
    {
        (SignKind.None, _) => ' ',
        (SignKind.SpaceForPositive, false) => ' ',
        (SignKind.SpaceForPositive, true) => '-',
        (SignKind.PlusForPositive, false) => '+',
        (SignKind.PlusForPositive, true) => '-',
        _ => ' ',
    };
}
