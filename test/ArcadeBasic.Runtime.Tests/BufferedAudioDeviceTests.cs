using System.Threading;
using System.Threading.Tasks;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Runtime.Tests;

/// <summary>The pull-based <see cref="BufferedAudioDevice"/> used by host backends
/// (Unity streaming AudioClip, WebAudio). Verified without any host: Emit buffers
/// PCM, Read drains it (with underrun → silence), and Flush waits for drain.</summary>
public class BufferedAudioDeviceTests
{
    [Fact]
    public void EmitBuffersPcmThatReadDrainsInOrder()
    {
        var dev = new BufferedAudioDevice();
        var expected = PcmRenderer.Render(new ToneEvent(440, 0.01, 0));
        dev.Emit(new ToneEvent(440, 0.01, 0));
        Assert.Equal(expected.Length, dev.BufferedSamples);

        var dest = new float[expected.Length];
        dev.Read(dest);
        Assert.Equal(0, dev.BufferedSamples);
        for (var i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i] / 32768f, dest[i], 5);
    }

    [Fact]
    public void ReadUnderrunZeroPads()
    {
        var dev = new BufferedAudioDevice();
        dev.Emit(new ToneEvent(440, 0.001, 0));     // a few samples
        var n = dev.BufferedSamples;
        var dest = new float[n + 16];
        dev.Read(dest);
        for (var i = n; i < dest.Length; i++) Assert.Equal(0f, dest[i]);
    }

    [Fact]
    public void FlushReturnsImmediatelyWhenEmpty()
    {
        var dev = new BufferedAudioDevice();
        dev.Flush();   // nothing buffered → must not block
    }

    [Fact]
    public async Task FlushReturnsOnceAConsumerDrains()
    {
        var dev = new BufferedAudioDevice();
        dev.Emit(new ToneEvent(440, 0.05, 0));

        // A background "audio thread" that pulls in small blocks.
        using var stop = new CancellationTokenSource();
        var reader = Task.Run(() =>
        {
            var block = new float[256];
            while (!stop.IsCancellationRequested && dev.BufferedSamples > 0)
                dev.Read(block);
        });

        await Task.Run(() => dev.Flush());
        Assert.Equal(0, dev.BufferedSamples);
        stop.Cancel();
        await reader;
    }

    [Fact]
    public async Task CloseUnblocksFlush()
    {
        var dev = new BufferedAudioDevice();
        dev.Emit(new ToneEvent(440, 5.0, 0));        // a lot, with no consumer
        var t = Task.Run(() => dev.Flush());
        dev.Close();                                  // must release the waiting Flush
        var finished = await Task.WhenAny(t, Task.Delay(2000));
        Assert.Same(t, finished);
        await t;
    }
}
