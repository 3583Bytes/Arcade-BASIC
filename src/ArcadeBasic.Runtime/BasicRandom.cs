namespace ArcadeBasic.Runtime;

/// <summary>
/// The pseudo-random sequence behind <c>RND</c> and <c>RANDOMIZE</c> — one per
/// program run, shared by all its program units (ECMA-116 §5.4.4).
///
/// Without RANDOMIZE every run gets the same sequence, as the standard requires;
/// <c>RANDOMIZE</c> jumps to an unpredictable one; <c>RANDOMIZE n</c> (an extension)
/// to a repeatable one per <c>n</c>. A host can supply a <i>randomize seed</i> that
/// RANDOMIZE uses instead of the clock, which makes a whole run repeatable — for
/// tests, or replaying a game.
///
/// The generator is SplitMix64, written out here rather than <see cref="Random"/>
/// so .NET, Unity (Mono/IL2CPP) and WebAssembly all produce the same numbers.
/// </summary>
public sealed class BasicRandom
{
    /// <summary>Where every run without RANDOMIZE starts.</summary>
    private const ulong DefaultSeed = 0x4152_4341_4445_4241;   // "ARCADEBA"

    private const ulong Gamma = 0x9E37_79B9_7F4A_7C15;

    /// <summary>Separates clock seeds taken in the same tick (two runs started together).</summary>
    private static long s_clockRandomizations;

    private readonly int? _randomizeSeed;
    private ulong _randomizations;
    private ulong _state = DefaultSeed;

    /// <param name="randomizeSeed">What a plain <c>RANDOMIZE</c> seeds from instead of the
    /// clock; null for the clock. Doesn't affect a run that never executes RANDOMIZE.</param>
    public BasicRandom(int? randomizeSeed = null)
    {
        _randomizeSeed = randomizeSeed;
    }

    /// <summary>The next number in the sequence, uniform in [0, 1).</summary>
    public double NextDouble() => (Next() >> 11) * (1.0 / (1UL << 53));

    /// <summary><c>RND</c>: the next number, in the program's arithmetic representation.</summary>
    public NumericValue Next(bool native)
    {
        var d = NextDouble();
        return native ? NumericValue.FromDouble(d) : NumericValue.From(Numbers.ToDecimal(d), native: false);
    }

    /// <summary><c>RANDOMIZE</c>: start an unpredictable sequence — or, with a randomize
    /// seed, the next sequence derived from it.</summary>
    public void Randomize()
    {
        _randomizations++;
        var seed = _randomizeSeed is int s
            ? ((ulong)(uint)s << 32) ^ _randomizations
            : (ulong)DateTime.UtcNow.Ticks ^ ((ulong)Interlocked.Increment(ref s_clockRandomizations) * Gamma);
        _state = Mix(seed);
    }

    /// <summary><c>RANDOMIZE n</c>: start the sequence that belongs to <paramref name="seed"/>.</summary>
    public void Randomize(double seed)
    {
        if (seed == 0) seed = 0;   // -0 and 0 are the same seed
        _state = Mix((ulong)BitConverter.DoubleToInt64Bits(seed));
    }

    private ulong Next()
    {
        _state += Gamma;
        return Mix(_state);
    }

    /// <summary>SplitMix64's output function: a bijective scramble of 64 bits.</summary>
    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58_476D_1CE4_E5B9;
        z = (z ^ (z >> 27)) * 0x94D0_49BB_1331_11EB;
        return z ^ (z >> 31);
    }
}
