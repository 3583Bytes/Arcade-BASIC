using ArcadeBasic.Runtime;

namespace ArcadeBasic.Runtime.Tests;

/// <summary>The shared square-wave PCM synthesis (<see cref="PcmRenderer"/>) that
/// both the WAV file backend and the real-time backends use.</summary>
public class PcmRendererTests
{
    [Fact]
    public void SampleCountMatchesDurationsAtSampleRate()
    {
        var n = PcmRenderer.Render(new ToneEvent(440, 1.0, 0.0)).Length;
        Assert.Equal(PcmRenderer.SampleRate, n);

        var gapped = PcmRenderer.Render(new ToneEvent(440, 0.01, 0.01)).Length;
        Assert.Equal((int)(0.01 * PcmRenderer.SampleRate) * 2, gapped);
    }

    [Fact]
    public void SquareWaveStartsHighThenSwingsNegative()
    {
        var buf = PcmRenderer.Render(new ToneEvent(100, 1.0, 0.0));   // 100 Hz: 441-sample period
        Assert.Equal(PcmRenderer.Amplitude, buf[0]);                  // phase 0 → high
        Assert.Equal((short)-PcmRenderer.Amplitude, buf[300]);        // past the half-period → low
    }

    [Fact]
    public void RestIsAllSilence()
    {
        var buf = PcmRenderer.Render(new ToneEvent(0, 0.5, 0.0));     // frequency 0 == rest
        Assert.Equal((int)(0.5 * PcmRenderer.SampleRate), buf.Length);
        Assert.All(buf, s => Assert.Equal((short)0, s));
    }

    [Fact]
    public void ArticulationGapIsSilentTail()
    {
        var buf = PcmRenderer.Render(new ToneEvent(440, 0.0, 0.02));  // all-gap (no sounded part)
        Assert.All(buf, s => Assert.Equal((short)0, s));
    }
}
