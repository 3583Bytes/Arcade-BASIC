namespace ArcadeBasic.Runtime;

/// <summary>
/// Per-dimension lower/upper bounds for an array. Supports up to 7 dimensions
/// per spec. Stores parallel arrays so we don't allocate one struct per dim.
/// </summary>
public sealed record class Bounds(int[] Lower, int[] Upper)
{
    public int Rank => Lower.Length;

    public int Length
    {
        get
        {
            var n = 1;
            for (var i = 0; i < Lower.Length; i++)
            {
                n *= (Upper[i] - Lower[i] + 1);
            }
            return n;
        }
    }

    /// <summary>Compute a flat-array index for the given multi-dim subscripts.
    /// Throws ArgumentOutOfRangeException if any subscript is out of bounds —
    /// callers translate that into the spec-defined exception.</summary>
    public int IndexOf(ReadOnlySpan<int> subscripts)
    {
        if (subscripts.Length != Rank)
        {
            throw new ArgumentException($"expected {Rank} subscript(s), got {subscripts.Length}");
        }

        var idx = 0;
        for (var i = 0; i < Rank; i++)
        {
            var s = subscripts[i];
            if (s < Lower[i] || s > Upper[i])
            {
                throw new ArgumentOutOfRangeException(nameof(subscripts),
                    $"subscript {s} out of range [{Lower[i]}..{Upper[i]}] for dimension {i + 1}");
            }
            idx = idx * (Upper[i] - Lower[i] + 1) + (s - Lower[i]);
        }
        return idx;
    }
}
