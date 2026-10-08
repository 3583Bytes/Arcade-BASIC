using Singulink.Numerics;

namespace ArcadeBasic.Runtime;

/// <summary>
/// Base for all runtime values. Sealed record class hierarchy per Q7. Pattern
/// matching with switch expressions provides exhaustiveness checking.
/// </summary>
public abstract record class Value;

/// <summary>
/// A numeric value: an arbitrary-precision decimal under the default
/// <c>OPTION ARITHMETIC DECIMAL</c>, or an IEEE double under
/// <c>OPTION ARITHMETIC NATIVE</c>. Both views are always available —
/// <see cref="V"/> converts a native value to decimal and <see cref="D"/> a
/// decimal one to double — so code that just needs "a number" (file I/O,
/// PRINT USING, graphics) works under either mode, while the arithmetic hot
/// paths in <see cref="Numbers"/> check <see cref="IsNative"/> and stay in double.
/// A native value is always finite: <see cref="Numbers.Native"/> turns an
/// overflow into a BASIC exception before one is built.
/// </summary>
public sealed record class NumericValue : Value
{
    private readonly BigDecimal _decimal;
    private readonly double _native;

    public NumericValue(BigDecimal v) => _decimal = v;

    private NumericValue(double d)
    {
        _native = d;
        IsNative = true;
    }

    public static readonly NumericValue Zero = new(BigDecimal.Zero);
    public static readonly NumericValue One = new(BigDecimal.One);
    public static readonly NumericValue MinusOne = new(-BigDecimal.One);
    public static readonly NumericValue NativeZero = new(0.0);
    public static readonly NumericValue NativeOne = new(1.0);
    public static readonly NumericValue NativeMinusOne = new(-1.0);

    /// <summary>Wrap a double the caller knows is finite. Arithmetic results go
    /// through <see cref="Numbers.Native"/> instead, which checks.</summary>
    public static NumericValue FromDouble(double d) => new(d);

    /// <summary><paramref name="v"/> in the representation the program's
    /// arithmetic mode uses — for values an engine creates from scratch
    /// (literals, INPUT/READ data, builtin results).</summary>
    public static NumericValue From(BigDecimal v, bool native) =>
        native ? new NumericValue(Numbers.ToDouble(v)) : new NumericValue(v);

    public static NumericValue From(long n, bool native) =>
        native ? new NumericValue((double)n) : new NumericValue((BigDecimal)n);

    public static NumericValue Zeroed(bool native) => native ? NativeZero : Zero;

    /// <summary>BASIC truth value: -1 for true, 0 for false.</summary>
    public static NumericValue Bool(bool b, bool native) =>
        b ? (native ? NativeMinusOne : MinusOne) : (native ? NativeZero : Zero);

    public bool IsNative { get; }

    /// <summary>The value as a decimal (exact for a decimal value; the shortest
    /// round-tripping decimal for a native one).</summary>
    public BigDecimal V => IsNative ? Numbers.ToDecimal(_native) : _decimal;

    /// <summary>The value as a double (the nearest double for a decimal value).</summary>
    public double D => IsNative ? _native : Numbers.ToDouble(_decimal);

    public bool IsZero => IsNative ? _native == 0 : _decimal == BigDecimal.Zero;

    public bool IsNegative => IsNative ? _native < 0 : _decimal < BigDecimal.Zero;

    /// <summary>Truncated toward zero — the conversion subscripts, TAB, channel
    /// numbers and string positions use.</summary>
    public int ToInt32() => IsNative ? (int)_native : (int)_decimal;

    public long ToInt64() => IsNative ? (long)_native : (long)_decimal;

    /// <summary>This value in the requested representation.</summary>
    public NumericValue As(bool native) =>
        native == IsNative ? this : native ? new NumericValue(D) : new NumericValue(V);
}

/// <summary>A string value. Storage is C# string; codepoint-aware via Rune helpers.</summary>
public sealed record class StringValue(string V) : Value
{
    public static readonly StringValue Empty = new("");
}

/// <summary>
/// A numeric array — flat element storage plus per-dim bounds. Elements are
/// decimals, or doubles for an array created under <c>OPTION ARITHMETIC
/// NATIVE</c>; the indexer reads and writes either as a <see cref="NumericValue"/>.
/// </summary>
public sealed record class NumericArrayValue : Value
{
    private readonly BigDecimal[]? _decimal;
    private readonly double[]? _native;

    public NumericArrayValue(BigDecimal[] data, Bounds bounds)
    {
        _decimal = data;
        Bounds = bounds;
    }

    public NumericArrayValue(double[] data, Bounds bounds)
    {
        _native = data;
        Bounds = bounds;
    }

    /// <summary>A zero-filled array in the program's representation (DIM, MAT REDIM).</summary>
    public static NumericArrayValue Create(Bounds bounds, bool native) =>
        native ? new NumericArrayValue(new double[bounds.Length], bounds)
               : new NumericArrayValue(new BigDecimal[bounds.Length], bounds);

    /// <summary>Decimal element data (a MAT result, which <see cref="MatOps"/>
    /// computes in decimal) stored in the program's representation.</summary>
    public static NumericArrayValue FromDecimals(BigDecimal[] data, Bounds bounds, bool native) =>
        native ? new NumericArrayValue(Array.ConvertAll(data, Numbers.ToDouble), bounds)
               : new NumericArrayValue(data, bounds);

    public Bounds Bounds { get; }

    public bool IsNative => _native is not null;

    public NumericValue this[int index]
    {
        get => _native is not null ? NumericValue.FromDouble(_native[index]) : new NumericValue(_decimal![index]);
        set
        {
            if (_native is not null) _native[index] = value.D;
            else _decimal![index] = value.V;
        }
    }

    /// <summary>The elements as decimals: the backing store itself for a decimal
    /// array, a converted copy for a native one — so treat it as read-only.</summary>
    public BigDecimal[] ToDecimals() => _decimal ?? Array.ConvertAll(_native!, Numbers.ToDecimal);
}

/// <summary>A string array — same shape, string storage.</summary>
public sealed record class StringArrayValue(string[] Data, Bounds Bounds) : Value;
