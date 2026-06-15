using System.Threading;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Runtime.Tests;

/// <summary>
/// The platform-agnostic async machinery of <see cref="RealtimeAudioDevice"/>
/// (queue → worker → sink → drain), exercised through a recording sink so it's
/// deterministic and needs no audio hardware. The per-OS native sinks themselves
/// are not unit-tested here (they require real hardware — see
/// docs/audio-extension.md).
/// </summary>
public class RealtimeAudioDeviceTests
{
    private sealed class RecordingPcmSink : IPcmSink
    {
        private long _samples;
        public long Samples => Interlocked.Read(ref _samples);
        public void Write(short[] s) => Interlocked.Add(ref _samples, s.Length);
        public void Dispose() { }
    }

    [Fact]
    public void FlushDrainsEveryEmittedTone()
    {
        var sink = new RecordingPcmSink();
        long expected;
        using (var dev = new RealtimeAudioDevice(() => sink))
        {
            var tones = new[]
            {
                new ToneEvent(440, 0.05, 0.01),
                new ToneEvent(0, 0.02, 0),        // a rest
                new ToneEvent(880, 0.03, 0.0),
            };
            expected = 0;
            foreach (var t in tones) { dev.Emit(t); expected += PcmRenderer.Render(t).Length; }
            dev.Flush();                          // blocks until the worker has played everything
            Assert.Equal(expected, sink.Samples);
        }
        Assert.True(expected > 0);
    }

    [Fact]
    public void SinkFactoryFailureDegradesToSilence()
    {
        using var dev = new RealtimeAudioDevice(() => throw new System.InvalidOperationException("no audio"));
        // Must not throw despite the sink never initializing.
        dev.Emit(new ToneEvent(440, 0.01, 0));
        dev.Flush();
    }

    [Fact]
    public void DisposeIsCleanWithPendingWork()
    {
        var sink = new RecordingPcmSink();
        var dev = new RealtimeAudioDevice(() => sink);
        for (var i = 0; i < 10; i++) dev.Emit(new ToneEvent(330, 0.005, 0));
        dev.Dispose();                            // joins the worker without hanging
        // Emitting after dispose is a no-op, not a crash.
        dev.Emit(new ToneEvent(440, 0.01, 0));
        dev.Flush();
    }
}
