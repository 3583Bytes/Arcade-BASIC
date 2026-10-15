using ArcadeBasic.Runtime;

namespace ArcadeBasic.Runtime.Tests;

/// <summary>
/// <see cref="BasicRandom"/>: the RND sequence repeats run to run until RANDOMIZE,
/// a seed always gives the same sequence, and the numbers fill [0, 1).
/// </summary>
public class BasicRandomTests
{
    private static double[] Take(BasicRandom r, int n) => Enumerable.Range(0, n).Select(_ => r.NextDouble()).ToArray();

    [Fact]
    public void EveryRunStartsWithTheSameSequence()
    {
        Assert.Equal(Take(new BasicRandom(), 100), Take(new BasicRandom(), 100));
        Assert.Equal(Take(new BasicRandom(), 100), Take(new BasicRandom(randomizeSeed: 5), 100));   // until RANDOMIZE
    }

    [Fact]
    public void NumbersAreInTheUnitIntervalAndRoughlyUniform()
    {
        var xs = Take(new BasicRandom(), 100_000);
        Assert.All(xs, x => Assert.InRange(x, 0.0, Math.BitDecrement(1.0)));
        Assert.InRange(xs.Average(), 0.49, 0.51);
        // each tenth of the interval gets close to its share
        Assert.All(xs.GroupBy(x => (int)(x * 10)), g => Assert.InRange(g.Count(), 9_500, 10_500));
    }

    [Fact]
    public void ASeedAlwaysGivesItsOwnSequence()
    {
        static double[] Seeded(double seed)
        {
            var r = new BasicRandom();
            r.Randomize(seed);
            return Take(r, 20);
        }

        Assert.Equal(Seeded(42), Seeded(42));
        Assert.Equal(Seeded(0), Seeded(-0.0));
        Assert.NotEqual(Seeded(42), Seeded(43));
        Assert.NotEqual(Seeded(42), Seeded(42.5));
        Assert.NotEqual(Take(new BasicRandom(), 20), Seeded(42));
    }

    [Fact]
    public void RandomizeWithAHostSeedIsRepeatableAndMovesOnEachTime()
    {
        static double[] Run(int hostSeed)
        {
            var r = new BasicRandom(hostSeed);
            r.Randomize();
            var first = Take(r, 10);
            r.Randomize();
            return [.. first, .. Take(r, 10)];
        }

        var run = Run(7);
        Assert.Equal(run, Run(7));
        Assert.NotEqual(run, Run(8));
        Assert.NotEqual(run[..10], run[10..]);   // the second RANDOMIZE starts somewhere new
    }

    [Fact]
    public void RandomizeFromTheClockDiffersBetweenRuns()
    {
        // Two runs started in the same instant still differ.
        var a = new BasicRandom();
        var b = new BasicRandom();
        a.Randomize();
        b.Randomize();
        Assert.NotEqual(Take(a, 10), Take(b, 10));
    }
}
