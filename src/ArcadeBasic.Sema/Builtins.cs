namespace ArcadeBasic.Sema;

/// <summary>
/// Predefined supplied functions and constants per ISO/IEC 10279.
/// The list is intentionally a hand-maintained registry — see project plan.
/// Names are stored under their canonical lookup key (uppercase, with '$' for
/// string-typed names).
/// </summary>
internal static class Builtins
{
    public static IEnumerable<Symbol> All()
    {
        // Numeric -> Numeric (1 arg)
        foreach (var n in new[] { "ABS", "SGN", "INT", "SQR", "EXP", "LOG", "LOG2", "LOG10",
                                  "SIN", "COS", "TAN", "ATN", "ASIN", "ACOS", "SEC", "CSC", "COT",
                                  "TRUNCATE", "ROUND", "CEIL" })
        {
            yield return new BuiltinSymbol(n, IsString: false,
                new BuiltinSignature(1, 1, [BuiltinArgType.Numeric]));
        }

        // RND can be called with 0 or 1 numeric args (some dialects).
        yield return new BuiltinSymbol("RND", IsString: false,
            new BuiltinSignature(0, 1, [BuiltinArgType.Numeric]));

        // MAX, MIN: variadic (>= 1 numeric arg).
        yield return new BuiltinSymbol("MAX", IsString: false,
            new BuiltinSignature(1, int.MaxValue, [BuiltinArgType.Numeric]));
        yield return new BuiltinSymbol("MIN", IsString: false,
            new BuiltinSignature(1, int.MaxValue, [BuiltinArgType.Numeric]));

        // MOD, REMAINDER as functions (the operator forms are also keywords).
        yield return new BuiltinSymbol("MOD", IsString: false,
            new BuiltinSignature(2, 2, [BuiltinArgType.Numeric, BuiltinArgType.Numeric]));
        yield return new BuiltinSymbol("REMAINDER", IsString: false,
            new BuiltinSignature(2, 2, [BuiltinArgType.Numeric, BuiltinArgType.Numeric]));

        // String -> Numeric
        foreach (var n in new[] { "LEN", "VAL", "ORD" })
        {
            yield return new BuiltinSymbol(n, IsString: false,
                new BuiltinSignature(1, 1, [BuiltinArgType.String]));
        }

        // POS(haystack$, needle$, [start])
        yield return new BuiltinSymbol("POS", IsString: false,
            new BuiltinSignature(2, 3, [BuiltinArgType.String, BuiltinArgType.String, BuiltinArgType.Numeric]));

        // Numeric -> String
        yield return new BuiltinSymbol("STR", IsString: true,
            new BuiltinSignature(1, 1, [BuiltinArgType.Numeric]));
        yield return new BuiltinSymbol("CHR", IsString: true,
            new BuiltinSignature(1, 1, [BuiltinArgType.Numeric]));
        yield return new BuiltinSymbol("REPEAT", IsString: true,
            new BuiltinSignature(2, 2, [BuiltinArgType.String, BuiltinArgType.Numeric]));

        // String -> String
        foreach (var n in new[] { "LCASE", "UCASE", "UPRC", "LTRIM", "RTRIM" })
        {
            yield return new BuiltinSymbol(n, IsString: true,
                new BuiltinSignature(1, 1, [BuiltinArgType.String]));
        }

        // MID$(s$, start[, len])
        yield return new BuiltinSymbol("MID", IsString: true,
            new BuiltinSignature(2, 3, [BuiltinArgType.String, BuiltinArgType.Numeric, BuiltinArgType.Numeric]));
        // LEFT$(s$, n)
        yield return new BuiltinSymbol("LEFT", IsString: true,
            new BuiltinSignature(2, 2, [BuiltinArgType.String, BuiltinArgType.Numeric]));
        // RIGHT$(s$, n)
        yield return new BuiltinSymbol("RIGHT", IsString: true,
            new BuiltinSignature(2, 2, [BuiltinArgType.String, BuiltinArgType.Numeric]));

        // System
        yield return new BuiltinSymbol("DATE", IsString: true,
            new BuiltinSignature(0, 0, []));
        yield return new BuiltinSymbol("TIME", IsString: true,
            new BuiltinSignature(0, 0, []));

        // INKEY$ — non-blocking keyboard poll (Microsoft BASIC extension, not
        // ISO/ECMA Full BASIC). Niladic string function; the engines evaluate it
        // against their keyboard source rather than the static builtin registry.
        yield return new BuiltinSymbol("INKEY", IsString: true,
            new BuiltinSignature(0, 0, []));

        // Bound queries (used with array names)
        yield return new BuiltinSymbol("LBOUND", IsString: false,
            new BuiltinSignature(1, 2, [BuiltinArgType.Any, BuiltinArgType.Numeric]));
        yield return new BuiltinSymbol("UBOUND", IsString: false,
            new BuiltinSignature(1, 2, [BuiltinArgType.Any, BuiltinArgType.Numeric]));

        // Exception accessors
        yield return new BuiltinSymbol("EXTYPE", IsString: false,
            new BuiltinSignature(0, 0, []));
        yield return new BuiltinSymbol("EXLINE", IsString: false,
            new BuiltinSignature(0, 0, []));
        yield return new BuiltinSymbol("EXTEXT", IsString: true,
            new BuiltinSignature(0, 0, []));

        // -- Predefined constants ----------------------------------------
        // These act like nullary functions with a special evaluator; we model
        // them as ConstantSymbol so the interpreter can treat them specially.
        yield return new ConstantSymbol("PI", IsString: false);
        yield return new ConstantSymbol("EPS", IsString: false);
        yield return new ConstantSymbol("INF", IsString: false);
        yield return new ConstantSymbol("MAXNUM", IsString: false);
    }
}
