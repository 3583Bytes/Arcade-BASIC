using System.Globalization;
using Singulink.Numerics;

namespace ArcadeBasic.Runtime;

/// <summary>
/// Centralised arithmetic for the numeric operators, shared by the tree-walking
/// interpreter and the bytecode VM so the two engines stay byte-for-byte identical.
///
/// <para><see cref="BigDecimal"/> multiply/add are <em>exact</em>: multiplying two
/// n-digit values yields a 2n-digit value, so an iterative loop such as Mandelbrot's
/// <c>z = z*z + c</c> doubles its significant-digit count every step and the work per
/// op grows exponentially. We clamp every result to <see cref="WorkingPrecision"/>
/// significant digits (banker's rounding, matching what <c>/</c> already does), which
/// keeps each op bounded. A result that already fits is returned untouched, so the
/// cap is invisible to any program whose values stay under it.</para>
///
/// <para>The <see cref="NumericValue"/> overloads implement both arithmetic modes:
/// if either operand is native (<c>OPTION ARITHMETIC NATIVE</c>) the operation is
/// done in double, otherwise in exact decimal. A native result that overflows to
/// infinity raises exception 1002 rather than escaping into the program.</para>
/// </summary>
public static class Numbers
{
    /// <summary>
    /// Maximum significant digits an arithmetic result may carry. Chosen to sit above
    /// everything the language otherwise promises — <c>PI</c> (37 digits), the 30-digit
    /// <c>/</c> rounding, exact INTERNAL file round-trips — while still far exceeding the
    /// ~15–16 digits of IEEE <c>double</c>. See docs/conformance.md.
    /// </summary>
    public const int WorkingPrecision = 40;

    /// <summary>Significant digits a decimal <c>/</c> keeps.</summary>
    public const int DivisionPrecision = 30;

    public static BigDecimal Add(BigDecimal a, BigDecimal b) => Cap(a + b);
    public static BigDecimal Subtract(BigDecimal a, BigDecimal b) => Cap(a - b);
    public static BigDecimal Multiply(BigDecimal a, BigDecimal b) => Cap(a * b);

    /// <summary>Rounds to <see cref="WorkingPrecision"/> significant digits only when the
    /// value exceeds it; otherwise returns it unchanged.</summary>
    public static BigDecimal Cap(BigDecimal value) =>
        value.Precision > WorkingPrecision
            ? BigDecimal.RoundToPrecision(value, WorkingPrecision, RoundingMode.MidpointToEven)
            : value;

    // -- Decimal <-> double --------------------------------------------------

    /// <summary>The nearest double.</summary>
    public static double ToDouble(BigDecimal v) => (double)v;

    /// <summary>The shortest decimal that round-trips to <paramref name="d"/>, so a
    /// double such as 0.1 converts to 0.1 rather than its exact binary expansion.</summary>
    public static BigDecimal ToDecimal(double d) =>
        BigDecimal.Parse(d.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>Wrap a native result, raising the overflow exception (1002) if it
    /// left the finite range.</summary>
    public static NumericValue Native(double d) =>
        double.IsFinite(d) ? NumericValue.FromDouble(d) : throw new BasicRuntimeException(1002, "numeric overflow");

    // -- Operators ------------------------------------------------------------

    public static NumericValue Add(NumericValue a, NumericValue b) =>
        a.IsNative || b.IsNative ? Native(a.D + b.D) : new NumericValue(Add(a.V, b.V));

    public static NumericValue Subtract(NumericValue a, NumericValue b) =>
        a.IsNative || b.IsNative ? Native(a.D - b.D) : new NumericValue(Subtract(a.V, b.V));

    public static NumericValue Multiply(NumericValue a, NumericValue b) =>
        a.IsNative || b.IsNative ? Native(a.D * b.D) : new NumericValue(Multiply(a.V, b.V));

    public static NumericValue Divide(NumericValue a, NumericValue b)
    {
        if (b.IsZero) throw new BasicRuntimeException(1001, "division by zero");
        return a.IsNative || b.IsNative
            ? Native(a.D / b.D)
            : new NumericValue(BigDecimal.Divide(a.V, b.V, DivisionPrecision, RoundingMode.MidpointToEven));
    }

    /// <summary>MOD: the result takes the sign of the divisor (mathematical modulo).</summary>
    public static NumericValue Mod(NumericValue a, NumericValue b)
    {
        if (b.IsZero) throw new BasicRuntimeException(1001, "MOD by zero");
        if (a.IsNative || b.IsNative)
        {
            // % is exact and takes the dividend's sign; shift it into the divisor's.
            var y = b.D;
            var r = a.D % y;
            return Native(r != 0 && (r < 0) != (y < 0) ? r + y : r);
        }
        var av = a.V;
        var bv = b.V;
        return new NumericValue(av - BigDecimal.Floor(av / bv) * bv);
    }

    /// <summary>REMAINDER: the result takes the sign of the dividend.</summary>
    public static NumericValue Remainder(NumericValue a, NumericValue b)
    {
        if (b.IsZero) throw new BasicRuntimeException(1001, "REMAINDER by zero");
        if (a.IsNative || b.IsNative) return Native(a.D % b.D);
        var av = a.V;
        var bv = b.V;
        return new NumericValue(av - BigDecimal.Truncate(av / bv) * bv);
    }

    public static NumericValue Power(NumericValue a, NumericValue b)
    {
        if (!a.IsNative && !b.IsNative) return new NumericValue(Power(a.V, b.V));
        var x = a.D;
        var y = b.D;
        if (x == 0 && y < 0) throw ZeroToNegativePower();
        if (x < 0 && y != Math.Floor(y)) throw NegativeToFractionalPower();
        return Native(Math.Pow(x, y));
    }

    public static BigDecimal Power(BigDecimal a, BigDecimal b)
    {
        if (a == BigDecimal.Zero && b < BigDecimal.Zero) throw ZeroToNegativePower();
        // Integer exponent: exact (a negative one is a reciprocal, rounded like '/').
        if (b == BigDecimal.Truncate(b) && b >= int.MinValue && b <= int.MaxValue)
        {
            var n = (int)b;
            return n >= 0
                ? BigDecimal.Pow(a, n)
                : BigDecimal.Divide(BigDecimal.One, BigDecimal.Pow(a, -n), DivisionPrecision, RoundingMode.MidpointToEven);
        }
        if (a < BigDecimal.Zero) throw NegativeToFractionalPower();
        // Otherwise approximate via doubles, like the supplied functions.
        var r = Math.Pow(ToDouble(a), ToDouble(b));
        if (!double.IsFinite(r)) throw new BasicRuntimeException(1002, "numeric overflow");
        return ToDecimal(r);
    }

    public static NumericValue Negate(NumericValue a) =>
        a.IsNative ? NumericValue.FromDouble(-a.D) : new NumericValue(-a.V);

    /// <summary>Three-way comparison, in double if either side is native.</summary>
    public static int Compare(NumericValue a, NumericValue b) =>
        a.IsNative || b.IsNative ? a.D.CompareTo(b.D) : a.V.CompareTo(b.V);

    private static BasicRuntimeException ZeroToNegativePower() =>
        new(3003, "zero raised to a negative power");

    private static BasicRuntimeException NegativeToFractionalPower() =>
        new(3002, "negative number raised to a non-integral power");
}
