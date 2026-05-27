using Singulink.Numerics;

namespace ArcadeBasic.Runtime;

/// <summary>
/// Base for all runtime values. Sealed record class hierarchy per Q7. Pattern
/// matching with switch expressions provides exhaustiveness checking.
/// </summary>
public abstract record class Value;

/// <summary>A numeric value backed by an arbitrary-precision decimal.</summary>
public sealed record class NumericValue(BigDecimal V) : Value
{
    public static readonly NumericValue Zero = new(BigDecimal.Zero);
    public static readonly NumericValue One = new(BigDecimal.One);
    public static readonly NumericValue MinusOne = new(-BigDecimal.One);
}

/// <summary>A string value. Storage is C# string; codepoint-aware via Rune helpers.</summary>
public sealed record class StringValue(string V) : Value
{
    public static readonly StringValue Empty = new("");
}

/// <summary>A numeric array — flat data plus per-dim bounds.</summary>
public sealed record class NumericArrayValue(BigDecimal[] Data, Bounds Bounds) : Value;

/// <summary>A string array — same shape, string storage.</summary>
public sealed record class StringArrayValue(string[] Data, Bounds Bounds) : Value;
