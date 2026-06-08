using Singulink.Numerics;

namespace ArcadeBasic.Runtime;

/// <summary>
/// Centralised arithmetic for the numeric operators <c>+</c>, <c>-</c>, <c>*</c>,
/// shared by the tree-walking interpreter and the bytecode VM so the two engines
/// stay byte-for-byte identical.
///
/// <para><see cref="BigDecimal"/> multiply/add are <em>exact</em>: multiplying two
/// n-digit values yields a 2n-digit value, so an iterative loop such as Mandelbrot's
/// <c>z = z*z + c</c> doubles its significant-digit count every step and the work per
/// op grows exponentially. We clamp every result to <see cref="WorkingPrecision"/>
/// significant digits (banker's rounding, matching what <c>/</c> already does), which
/// keeps each op bounded. A result that already fits is returned untouched, so the
/// cap is invisible to any program whose values stay under it.</para>
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

    public static BigDecimal Add(BigDecimal a, BigDecimal b) => Cap(a + b);
    public static BigDecimal Subtract(BigDecimal a, BigDecimal b) => Cap(a - b);
    public static BigDecimal Multiply(BigDecimal a, BigDecimal b) => Cap(a * b);

    /// <summary>Rounds to <see cref="WorkingPrecision"/> significant digits only when the
    /// value exceeds it; otherwise returns it unchanged.</summary>
    public static BigDecimal Cap(BigDecimal value) =>
        value.Precision > WorkingPrecision
            ? BigDecimal.RoundToPrecision(value, WorkingPrecision, RoundingMode.MidpointToEven)
            : value;
}
