using System.Text;
using Singulink.Numerics;

namespace FullBasic.Runtime;

/// <summary>
/// Concrete implementations of the supplied functions registered in
/// FullBasic.Sema/Builtins. Phase-3 keeps it simple: trig/exp/log evaluate
/// in double precision (per ISO 10279, accuracy of supplied functions is
/// implementation-defined; the standard recommends 6 significant decimal
/// digits, and double yields ~15). Strict big-decimal versions can be
/// added later if needed.
/// </summary>
public static class BuiltinImpls
{
    public delegate Value BuiltinFn(Value[] args);

    public static IReadOnlyDictionary<string, BuiltinFn> All { get; } = Build();

    public static Value EvalConstant(string name) => name.ToUpperInvariant() switch
    {
        "PI" => new NumericValue(BigDecimal.Parse("3.141592653589793238462643383279502884")),
        "EPS" => new NumericValue(BigDecimal.Parse("0.00000000000001")), // 1e-14
        "INF" => new NumericValue(BigDecimal.Parse("1E308")),
        "MAXNUM" => new NumericValue(BigDecimal.Parse("1E308")),
        _ => throw new BasicRuntimeException(0, $"unknown constant '{name}'"),
    };

    private static IReadOnlyDictionary<string, BuiltinFn> Build()
    {
        var t = new Dictionary<string, BuiltinFn>(StringComparer.OrdinalIgnoreCase);

        // Numeric -> Numeric
        t["ABS"] = args => Num(BigDecimal.Abs(N(args[0])));
        t["SGN"] = args =>
        {
            var x = N(args[0]);
            return x == BigDecimal.Zero ? NumericValue.Zero : (x < BigDecimal.Zero ? NumericValue.MinusOne : NumericValue.One);
        };
        t["INT"] = args => Num(BigDecimal.Floor(N(args[0])));
        t["TRUNCATE"] = args => Num(BigDecimal.Truncate(N(args[0])));
        t["CEIL"] = args => Num(BigDecimal.Ceiling(N(args[0])));
        t["ROUND"] = args => Num(BigDecimal.Round(N(args[0]), 0, RoundingMode.MidpointToEven));
        t["SQR"] = args => Num(FromDouble(Math.Sqrt(ToDouble(args[0]))));
        t["EXP"] = args => Num(FromDouble(Math.Exp(ToDouble(args[0]))));
        t["LOG"] = args =>
        {
            var x = ToDouble(args[0]);
            if (x <= 0) throw new BasicRuntimeException(2001, "LOG requires positive argument");
            return Num(FromDouble(Math.Log(x)));
        };
        t["LOG2"] = args =>
        {
            var x = ToDouble(args[0]);
            if (x <= 0) throw new BasicRuntimeException(2001, "LOG2 requires positive argument");
            return Num(FromDouble(Math.Log(x, 2)));
        };
        t["LOG10"] = args =>
        {
            var x = ToDouble(args[0]);
            if (x <= 0) throw new BasicRuntimeException(2001, "LOG10 requires positive argument");
            return Num(FromDouble(Math.Log10(x)));
        };
        t["SIN"] = args => Num(FromDouble(Math.Sin(ToDouble(args[0]))));
        t["COS"] = args => Num(FromDouble(Math.Cos(ToDouble(args[0]))));
        t["TAN"] = args => Num(FromDouble(Math.Tan(ToDouble(args[0]))));
        t["ATN"] = args => Num(FromDouble(Math.Atan(ToDouble(args[0]))));
        t["ASIN"] = args => Num(FromDouble(Math.Asin(ToDouble(args[0]))));
        t["ACOS"] = args => Num(FromDouble(Math.Acos(ToDouble(args[0]))));
        t["SEC"] = args => Num(FromDouble(1.0 / Math.Cos(ToDouble(args[0]))));
        t["CSC"] = args => Num(FromDouble(1.0 / Math.Sin(ToDouble(args[0]))));
        t["COT"] = args => Num(FromDouble(1.0 / Math.Tan(ToDouble(args[0]))));

        var rng = new Random();
        t["RND"] = args => Num(BigDecimal.Parse(
            rng.NextDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture));

        t["MAX"] = args =>
        {
            var best = N(args[0]);
            for (var i = 1; i < args.Length; i++)
            {
                var v = N(args[i]);
                if (v > best) best = v;
            }
            return Num(best);
        };
        t["MIN"] = args =>
        {
            var best = N(args[0]);
            for (var i = 1; i < args.Length; i++)
            {
                var v = N(args[i]);
                if (v < best) best = v;
            }
            return Num(best);
        };
        t["MOD"] = args =>
        {
            var a = N(args[0]);
            var b = N(args[1]);
            if (b == BigDecimal.Zero) throw new BasicRuntimeException(1001, "MOD by zero");
            // Per spec MOD: result has same sign as divisor (mathematical modulo).
            var q = BigDecimal.Floor(a / b);
            return Num(a - q * b);
        };
        t["REMAINDER"] = args =>
        {
            var a = N(args[0]);
            var b = N(args[1]);
            if (b == BigDecimal.Zero) throw new BasicRuntimeException(1001, "REMAINDER by zero");
            // Per spec REMAINDER: result has same sign as dividend.
            var q = BigDecimal.Truncate(a / b);
            return Num(a - q * b);
        };

        // String -> Numeric
        t["LEN"] = args => Num(BigDecimal.Parse(CountRunes(S(args[0])).ToString()));
        t["VAL"] = args =>
        {
            var s = S(args[0]).Trim();
            if (BigDecimal.TryParse(s, out var bd)) return Num(bd);
            throw new BasicRuntimeException(3001, $"VAL: '{s}' is not a numeric constant");
        };
        t["ORD"] = args =>
        {
            var s = S(args[0]);
            if (s.Length == 0) throw new BasicRuntimeException(3002, "ORD: string is empty");
            var cp = char.ConvertToUtf32(s, 0);
            return Num(BigDecimal.Parse(cp.ToString()));
        };
        t["POS"] = args =>
        {
            var hay = S(args[0]);
            var needle = S(args[1]);
            var startCp = args.Length > 2 ? (int)N(args[2]) : 1;
            // POS is 1-based, codepoint-positioned, returns 0 if not found.
            var startIdx = CodepointToCharIndex(hay, startCp - 1);
            var pos = hay.IndexOf(needle, startIdx, StringComparison.Ordinal);
            if (pos < 0) return NumericValue.Zero;
            return Num(BigDecimal.Parse((CharIndexToCodepoint(hay, pos) + 1).ToString()));
        };

        // Numeric -> String
        t["STR"] = args => Str(N(args[0]).ToString());
        t["CHR"] = args =>
        {
            var cp = (int)N(args[0]);
            if (cp < 0 || cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF))
                throw new BasicRuntimeException(3003, $"CHR: codepoint {cp} out of range");
            return Str(char.ConvertFromUtf32(cp));
        };
        t["REPEAT"] = args =>
        {
            var s = S(args[0]);
            var n = (int)N(args[1]);
            if (n < 0) throw new BasicRuntimeException(3004, "REPEAT count must be non-negative");
            return Str(string.Concat(Enumerable.Repeat(s, n)));
        };

        // String -> String (1 arg)
        t["LCASE"] = args => Str(S(args[0]).ToLowerInvariant());
        t["UCASE"] = args => Str(S(args[0]).ToUpperInvariant());
        t["UPRC"] = args => Str(S(args[0]).ToUpperInvariant());
        t["LTRIM"] = args => Str(S(args[0]).TrimStart());
        t["RTRIM"] = args => Str(S(args[0]).TrimEnd());

        // MID$(s, start[, len]) — start and len in codepoints, 1-based.
        t["MID"] = args =>
        {
            var s = S(args[0]);
            var start = (int)N(args[1]);
            var lenCp = args.Length > 2 ? (int)N(args[2]) : int.MaxValue;
            if (start < 1) throw new BasicRuntimeException(3005, "MID start must be >= 1");
            return Str(SubstringByCodepoints(s, start - 1, lenCp));
        };
        t["LEFT"] = args =>
        {
            var s = S(args[0]);
            var n = (int)N(args[1]);
            if (n < 0) throw new BasicRuntimeException(3005, "LEFT length must be >= 0");
            return Str(SubstringByCodepoints(s, 0, n));
        };
        t["RIGHT"] = args =>
        {
            var s = S(args[0]);
            var n = (int)N(args[1]);
            if (n < 0) throw new BasicRuntimeException(3005, "RIGHT length must be >= 0");
            var total = CountRunes(s);
            return Str(SubstringByCodepoints(s, Math.Max(0, total - n), n));
        };

        // System
        t["DATE"] = _ => Str(DateTime.Today.ToString("yyyyMMdd"));
        t["TIME"] = _ => Str(DateTime.Now.ToString("HH:mm:ss"));

        // Bound queries
        t["LBOUND"] = args =>
        {
            var arr = (Bounds)BoundsOf(args[0]);
            var dim = args.Length > 1 ? (int)N(args[1]) : 1;
            return Num(BigDecimal.Parse(arr.Lower[dim - 1].ToString()));
        };
        t["UBOUND"] = args =>
        {
            var arr = (Bounds)BoundsOf(args[0]);
            var dim = args.Length > 1 ? (int)N(args[1]) : 1;
            return Num(BigDecimal.Parse(arr.Upper[dim - 1].ToString()));
        };

        // Exception accessors — Phase 6 will hook these into the active
        // handler frame. For Phase 3 they return safe defaults.
        t["EXTYPE"] = _ => NumericValue.Zero;
        t["EXLINE"] = _ => NumericValue.Zero;
        t["EXTEXT"] = _ => StringValue.Empty;

        return t;
    }

    // -- Conversion helpers ---------------------------------------------

    private static BigDecimal N(Value v) => v switch
    {
        NumericValue n => n.V,
        _ => throw new BasicRuntimeException(0, $"expected numeric, got {v.GetType().Name}"),
    };

    private static string S(Value v) => v switch
    {
        StringValue s => s.V,
        _ => throw new BasicRuntimeException(0, $"expected string, got {v.GetType().Name}"),
    };

    private static NumericValue Num(BigDecimal x) => new(x);
    private static StringValue Str(string s) => new(s);

    private static double ToDouble(Value v)
    {
        var bd = N(v);
        if (!double.TryParse(bd.ToString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var d))
        {
            throw new BasicRuntimeException(0, "cannot convert to double");
        }
        return d;
    }

    private static BigDecimal FromDouble(double d) =>
        BigDecimal.Parse(d.ToString("R", System.Globalization.CultureInfo.InvariantCulture));

    private static int CountRunes(string s)
    {
        var n = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
            n++;
        }
        return n;
    }

    private static int CodepointToCharIndex(string s, int cpIndex)
    {
        if (cpIndex <= 0) return 0;
        var i = 0;
        var cp = 0;
        while (i < s.Length && cp < cpIndex)
        {
            i += char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 1;
            cp++;
        }
        return i;
    }

    private static int CharIndexToCodepoint(string s, int charIndex)
    {
        var cp = 0;
        var i = 0;
        while (i < charIndex)
        {
            i += char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 1;
            cp++;
        }
        return cp;
    }

    private static string SubstringByCodepoints(string s, int startCp, int lenCp)
    {
        if (lenCp <= 0) return string.Empty;
        var sb = new StringBuilder();
        var cp = 0;
        var i = 0;
        while (i < s.Length)
        {
            var width = char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 1;
            if (cp >= startCp && cp < startCp + lenCp) sb.Append(s, i, width);
            cp++;
            i += width;
            if (cp >= startCp + lenCp) break;
        }
        return sb.ToString();
    }

    private static Bounds BoundsOf(Value v) => v switch
    {
        NumericArrayValue a => a.Bounds,
        StringArrayValue a => a.Bounds,
        _ => throw new BasicRuntimeException(0, "LBOUND/UBOUND requires an array argument"),
    };
}
