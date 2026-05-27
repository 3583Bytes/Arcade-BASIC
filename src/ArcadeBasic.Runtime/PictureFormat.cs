using System.Text;
using Singulink.Numerics;

namespace ArcadeBasic.Runtime;

/// <summary>
/// Picture-string formatter for Phase-8a "editing" module — implements the
/// portion of ISO 10279's PRINT USING / FORMAT$ that's most-used in practice:
///
///   <c>#</c>   — digit (space if no digit)
///   <c>0</c>   — digit (zero-fill)
///   <c>.</c>   — decimal point
///   <c>+</c>   — sign placeholder, '+' if non-negative
///   <c>-</c>   — sign placeholder, ' ' if non-negative
///   <c>&lt;####</c> — left-justified string, width = count of #
///   <c>&gt;####</c> — right-justified string
///   <c>=####</c>    — centered string
///
/// Everything else passes through as literal text. If a value's magnitude
/// can't fit in its numeric field, the field is filled with '*' characters
/// (overflow indicator). Deferred to a later refinement: thousands separator
/// (','), floating currency ('$$'), asterisk-fill ('**'), exponent ('^^^^').
/// </summary>
public static class PictureFormat
{
    public abstract record class Part;
    public sealed record class LiteralPart(string Text) : Part;
    public sealed record class NumberPart(int IntDigits, int FracDigits, bool ZeroFill, SignKind LeadingSign, SignKind TrailingSign) : Part;
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

            // Numeric field: starts with #, 0, or +/- followed by # or 0.
            if (c == '#' || c == '0'
                || ((c == '+' || c == '-') && i + 1 < format.Length
                    && (format[i + 1] == '#' || format[i + 1] == '0')))
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

    private static (NumberPart Part, int Length) ParseNumberField(string format, int start)
    {
        var i = start;
        var leadingSign = SignKind.None;
        if (format[i] == '+') { leadingSign = SignKind.PlusForPositive; i++; }
        else if (format[i] == '-') { leadingSign = SignKind.SpaceForPositive; i++; }

        var intDigits = 0;
        var zeroFill = false;
        while (i < format.Length && (format[i] == '#' || format[i] == '0'))
        {
            if (format[i] == '0') zeroFill = true;
            intDigits++;
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

        return (new NumberPart(intDigits, fracDigits, zeroFill, leadingSign, trailingSign), i - start);
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

        // Pad fractional part with trailing zeros if shorter than required.
        if (fracPart.Length < np.FracDigits)
        {
            fracPart += new string('0', np.FracDigits - fracPart.Length);
        }
        // Truncate fractional if longer (rounding above should cover this).
        if (fracPart.Length > np.FracDigits)
        {
            fracPart = fracPart[..np.FracDigits];
        }

        // Has explicit sign slot? If yes, integer width is digits-only.
        // Otherwise, a negative number consumes one of the integer slots for the '-'.
        var hasExplicitSign = np.LeadingSign != SignKind.None || np.TrailingSign != SignKind.None;

        // Field overflow check.
        var maxIntDigitsAvailable = hasExplicitSign ? np.IntDigits : (negative ? np.IntDigits - 1 : np.IntDigits);
        if (intPart.Length > maxIntDigitsAvailable)
        {
            var totalWidth = np.IntDigits + (np.FracDigits > 0 ? 1 + np.FracDigits : 0)
                + (hasExplicitSign ? 1 : 0);
            return new string('*', totalWidth);
        }

        var sb = new StringBuilder();

        if (hasExplicitSign)
        {
            // Pad digits with the chosen fill char.
            var intPad = np.IntDigits - intPart.Length;
            var intFill = np.ZeroFill ? '0' : ' ';
            var paddedInt = new string(intFill, intPad) + intPart;
            var sign = ResolveSign(negative, np.LeadingSign != SignKind.None ? np.LeadingSign : np.TrailingSign);
            if (np.LeadingSign != SignKind.None) sb.Append(sign);
            sb.Append(paddedInt);
            if (np.FracDigits > 0) { sb.Append('.'); sb.Append(fracPart); }
            if (np.TrailingSign != SignKind.None) sb.Append(sign);
        }
        else
        {
            // Negative: emit "<spaces>-<digits>", consuming one of the integer slots.
            // Positive: emit "<fill><digits>" (zero-fill or space-fill).
            if (negative)
            {
                var pad = np.IntDigits - 1 - intPart.Length;
                sb.Append(' ', pad);
                sb.Append('-');
                sb.Append(intPart);
            }
            else
            {
                var pad = np.IntDigits - intPart.Length;
                sb.Append(np.ZeroFill ? '0' : ' ', pad);
                sb.Append(intPart);
            }
            if (np.FracDigits > 0) { sb.Append('.'); sb.Append(fracPart); }
        }

        return sb.ToString();
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
