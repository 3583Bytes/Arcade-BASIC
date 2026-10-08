using System.Text;
using Singulink.Numerics;

namespace ArcadeBasic.Runtime;

/// <summary>
/// Concrete implementations of the supplied functions registered in
/// ArcadeBasic.Sema/Builtins. Trig/exp/log evaluate in double precision (per
/// ISO 10279, accuracy of supplied functions is implementation-defined; the
/// standard recommends 6 significant decimal digits, and double yields ~15).
///
/// Every function takes the program's arithmetic mode: under OPTION ARITHMETIC
/// NATIVE numeric results come back as doubles, otherwise as decimals (the
/// exact-decimal functions — ABS, INT, ROUND, … — then stay exact).
/// </summary>
public static class BuiltinImpls
{
    public delegate Value BuiltinFn(Value[] args, bool native);

    public static IReadOnlyDictionary<string, BuiltinFn> All { get; } = Build();

    private static readonly NumericValue Pi = new(BigDecimal.Parse("3.141592653589793238462643383279502884"));
    private static readonly NumericValue Eps = new(BigDecimal.Parse("0.00000000000001")); // 1e-14
    private static readonly NumericValue Maxnum = new(Numbers.ToDecimal(1e308));

    public static Value EvalConstant(string name, bool native) => name.ToUpperInvariant() switch
    {
        "PI" => Pi.As(native),
        "EPS" => Eps.As(native),
        "INF" or "MAXNUM" => Maxnum.As(native),
        _ => throw new BasicRuntimeException(0, $"unknown constant '{name}'"),
    };

    private static IReadOnlyDictionary<string, BuiltinFn> Build()
    {
        var t = new Dictionary<string, BuiltinFn>(StringComparer.OrdinalIgnoreCase);

        // Numeric -> Numeric
        t["ABS"] = (args, native) => native
            ? NumericValue.FromDouble(Math.Abs(D(args[0])))
            : Num(BigDecimal.Abs(N(args[0])));
        t["SGN"] = (args, native) => NumericValue.From(NV(args[0]) switch
        {
            { IsZero: true } => 0,
            { IsNegative: true } => -1,
            _ => 1,
        }, native);
        t["INT"] = (args, native) => native
            ? NumericValue.FromDouble(Math.Floor(D(args[0])))
            : Num(BigDecimal.Floor(N(args[0])));
        t["TRUNCATE"] = (args, native) => native
            ? NumericValue.FromDouble(Math.Truncate(D(args[0])))
            : Num(BigDecimal.Truncate(N(args[0])));
        t["CEIL"] = (args, native) => native
            ? NumericValue.FromDouble(Math.Ceiling(D(args[0])))
            : Num(BigDecimal.Ceiling(N(args[0])));
        t["ROUND"] = (args, native) => native
            ? NumericValue.FromDouble(Math.Round(D(args[0]), MidpointRounding.ToEven))
            : Num(BigDecimal.Round(N(args[0]), 0, RoundingMode.MidpointToEven));
        t["SQR"] = (args, native) =>
        {
            var x = D(args[0]);
            if (x < 0) throw new BasicRuntimeException(3005, "SQR requires a non-negative argument");
            return Real("SQR", Math.Sqrt(x), native);
        };
        t["EXP"] = (args, native) => Real("EXP", Math.Exp(D(args[0])), native);
        t["LOG"] = (args, native) => Real("LOG", Math.Log(PositiveArg("LOG", args[0])), native);
        t["LOG2"] = (args, native) => Real("LOG2", Math.Log(PositiveArg("LOG2", args[0]), 2), native);
        t["LOG10"] = (args, native) => Real("LOG10", Math.Log10(PositiveArg("LOG10", args[0])), native);
        t["SIN"] = (args, native) => Real("SIN", Math.Sin(D(args[0])), native);
        t["COS"] = (args, native) => Real("COS", Math.Cos(D(args[0])), native);
        t["TAN"] = (args, native) => Real("TAN", Math.Tan(D(args[0])), native);
        t["ATN"] = (args, native) => Real("ATN", Math.Atan(D(args[0])), native);
        t["ASIN"] = (args, native) => Real("ASIN", Math.Asin(UnitArg("ASIN", args[0])), native);
        t["ACOS"] = (args, native) => Real("ACOS", Math.Acos(UnitArg("ACOS", args[0])), native);
        t["SEC"] = (args, native) => Real("SEC", 1.0 / Math.Cos(D(args[0])), native);
        t["CSC"] = (args, native) => Real("CSC", 1.0 / Math.Sin(D(args[0])), native);
        t["COT"] = (args, native) => Real("COT", 1.0 / Math.Tan(D(args[0])), native);

        var rng = new Random();
        t["RND"] = (_, native) => native
            ? NumericValue.FromDouble(rng.NextDouble())
            : Num(Numbers.ToDecimal(rng.NextDouble()));

        t["MAX"] = (args, native) =>
        {
            var best = NV(args[0]);
            for (var i = 1; i < args.Length; i++)
            {
                var v = NV(args[i]);
                if (Numbers.Compare(v, best) > 0) best = v;
            }
            return best.As(native);
        };
        t["MIN"] = (args, native) =>
        {
            var best = NV(args[0]);
            for (var i = 1; i < args.Length; i++)
            {
                var v = NV(args[i]);
                if (Numbers.Compare(v, best) < 0) best = v;
            }
            return best.As(native);
        };
        t["MOD"] = (args, native) => Numbers.Mod(NV(args[0]).As(native), NV(args[1]).As(native));
        t["REMAINDER"] = (args, native) => Numbers.Remainder(NV(args[0]).As(native), NV(args[1]).As(native));

        // String -> Numeric
        t["LEN"] = (args, native) => NumericValue.From(CountRunes(S(args[0])), native);
        t["VAL"] = (args, native) =>
        {
            var s = S(args[0]).Trim();
            if (BigDecimal.TryParse(s, out var bd)) return NumericValue.From(bd, native);
            throw new BasicRuntimeException(3001, $"VAL: '{s}' is not a numeric constant");
        };
        t["ORD"] = (args, native) =>
        {
            var s = S(args[0]);
            if (s.Length == 0) throw new BasicRuntimeException(3002, "ORD: string is empty");
            return NumericValue.From(char.ConvertToUtf32(s, 0), native);
        };
        t["POS"] = (args, native) =>
        {
            var hay = S(args[0]);
            var needle = S(args[1]);
            var startCp = args.Length > 2 ? I(args[2]) : 1;
            // POS is 1-based, codepoint-positioned, returns 0 if not found.
            var startIdx = CodepointToCharIndex(hay, startCp - 1);
            var pos = hay.IndexOf(needle, startIdx, StringComparison.Ordinal);
            return NumericValue.From(pos < 0 ? 0 : CharIndexToCodepoint(hay, pos) + 1, native);
        };

        // Numeric -> String
        // ISO 10279: STR$ is the string PRINT shows for the value, without the
        // leading and trailing spaces — so it rounds to the display precision.
        t["STR"] = (args, _) => Str(DisplayFormat.FormatNumeric(N(args[0])).Trim());
        t["CHR"] = (args, _) =>
        {
            var cp = I(args[0]);
            if (cp < 0 || cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF))
                throw new BasicRuntimeException(3003, $"CHR: codepoint {cp} out of range");
            return Str(char.ConvertFromUtf32(cp));
        };
        t["REPEAT"] = (args, _) =>
        {
            var s = S(args[0]);
            var n = I(args[1]);
            if (n < 0) throw new BasicRuntimeException(3004, "REPEAT count must be non-negative");
            return Str(string.Concat(Enumerable.Repeat(s, n)));
        };

        // String -> String (1 arg)
        t["LCASE"] = (args, _) => Str(S(args[0]).ToLowerInvariant());
        t["UCASE"] = (args, _) => Str(S(args[0]).ToUpperInvariant());
        t["UPRC"] = (args, _) => Str(S(args[0]).ToUpperInvariant());
        t["LTRIM"] = (args, _) => Str(S(args[0]).TrimStart());
        t["RTRIM"] = (args, _) => Str(S(args[0]).TrimEnd());

        // MID$(s, start[, len]) — start and len in codepoints, 1-based.
        t["MID"] = (args, _) =>
        {
            var s = S(args[0]);
            var start = I(args[1]);
            var lenCp = args.Length > 2 ? I(args[2]) : int.MaxValue;
            if (start < 1) throw new BasicRuntimeException(3005, "MID start must be >= 1");
            return Str(SubstringByCodepoints(s, start - 1, lenCp));
        };
        t["LEFT"] = (args, _) =>
        {
            var s = S(args[0]);
            var n = I(args[1]);
            if (n < 0) throw new BasicRuntimeException(3005, "LEFT length must be >= 0");
            return Str(SubstringByCodepoints(s, 0, n));
        };
        t["RIGHT"] = (args, _) =>
        {
            var s = S(args[0]);
            var n = I(args[1]);
            if (n < 0) throw new BasicRuntimeException(3005, "RIGHT length must be >= 0");
            var total = CountRunes(s);
            return Str(SubstringByCodepoints(s, Math.Max(0, total - n), n));
        };

        // System
        t["DATE"] = (_, _) => Str(DateTime.Today.ToString("yyyyMMdd"));
        t["TIME"] = (_, _) => Str(DateTime.Now.ToString("HH:mm:ss"));

        // Bound queries
        t["LBOUND"] = (args, native) =>
        {
            var arr = BoundsOf(args[0]);
            var dim = args.Length > 1 ? I(args[1]) : 1;
            return NumericValue.From(arr.Lower[dim - 1], native);
        };
        t["UBOUND"] = (args, native) =>
        {
            var arr = BoundsOf(args[0]);
            var dim = args.Length > 1 ? I(args[1]) : 1;
            return NumericValue.From(arr.Upper[dim - 1], native);
        };

        // Exception accessors — the engines intercept these to read their
        // active handler frame; the stubs return safe defaults.
        t["EXTYPE"] = (_, native) => NumericValue.Zeroed(native);
        t["EXLINE"] = (_, native) => NumericValue.Zeroed(native);
        t["EXTEXT"] = (_, _) => StringValue.Empty;

        return t;
    }

    // -- Conversion helpers ---------------------------------------------

    private static NumericValue NV(Value v) => v switch
    {
        NumericValue n => n,
        _ => throw new BasicRuntimeException(0, $"expected numeric, got {v.GetType().Name}"),
    };

    private static BigDecimal N(Value v) => NV(v).V;

    private static double D(Value v) => NV(v).D;

    private static int I(Value v) => NV(v).ToInt32();

    private static string S(Value v) => v switch
    {
        StringValue s => s.V,
        _ => throw new BasicRuntimeException(0, $"expected string, got {v.GetType().Name}"),
    };

    private static NumericValue Num(BigDecimal x) => new(x);
    private static StringValue Str(string s) => new(s);

    /// <summary>The result of a supplied function computed in double, in the
    /// program's representation. A non-finite result is the spec's
    /// supplied-function overflow (1003).</summary>
    private static NumericValue Real(string fn, double r, bool native)
    {
        if (!double.IsFinite(r)) throw new BasicRuntimeException(1003, $"{fn}: result overflows");
        return native ? NumericValue.FromDouble(r) : Num(Numbers.ToDecimal(r));
    }

    private static double PositiveArg(string fn, Value v)
    {
        var x = D(v);
        if (x <= 0) throw new BasicRuntimeException(2001, $"{fn} requires positive argument");
        return x;
    }

    private static double UnitArg(string fn, Value v)
    {
        var x = D(v);
        if (x < -1 || x > 1) throw new BasicRuntimeException(3007, $"{fn} requires an argument between -1 and 1");
        return x;
    }

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
        // Use a long end bound: the 2-arg MID$(s, start) passes lenCp = int.MaxValue,
        // and startCp + lenCp would overflow Int32 (going negative) — which made the
        // whole substring come back empty.
        long end = (long)startCp + lenCp;
        var sb = new StringBuilder();
        var cp = 0;
        var i = 0;
        while (i < s.Length)
        {
            var width = char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 1;
            if (cp >= startCp && cp < end) sb.Append(s, i, width);
            cp++;
            i += width;
            if (cp >= end) break;
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
