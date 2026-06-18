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

    private sealed class ThrowingPcmSink : IPcmSink
    {
        public void Write(short[] s) => throw new System.IO.IOException("device went away");
        public void Dispose() { }
    }

    /// <summary>Collects diagnostics from the worker thread for assertion on the
    /// test thread. A <see cref="RealtimeAudioDevice.Flush"/> establishes the
    /// happens-before, so messages reported before it returns are visible here.</summary>
    private sealed class DiagnosticLog
    {
        private readonly object _gate = new();
        private readonly System.Collections.Generic.List<string> _messages = new();
        public void Record(string message, System.Exception? error)
        {
            lock (_gate) _messages.Add(error is null ? message : $"{message} :: {error.Message}");
        }
        public string[] Messages { get { lock (_gate) return _messages.ToArray(); } }
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
    public void SinkFactoryFailureIsReportedToDiagnostic()
    {
        var log = new DiagnosticLog();
        using (var dev = new RealtimeAudioDevice(
            () => throw new System.InvalidOperationException("no audio"), log.Record))
        {
            dev.Emit(new ToneEvent(440, 0.01, 0));
            dev.Flush();   // worker has tried (and failed) the factory by the time this returns
        }
        Assert.Single(log.Messages);
        Assert.Contains("could not be initialized", log.Messages[0]);
        Assert.Contains("no audio", log.Messages[0]);   // the underlying exception message is included
    }

    [Fact]
    public void MidRunWriteFailureIsReportedAndDegradesToSilence()
    {
        var log = new DiagnosticLog();
        using (var dev = new RealtimeAudioDevice(() => new ThrowingPcmSink(), log.Record))
        {
            dev.Emit(new ToneEvent(440, 0.01, 0));
            dev.Flush();
            // Further tones are silently dropped (sink torn down) and must not re-report.
            dev.Emit(new ToneEvent(880, 0.01, 0));
            dev.Flush();
        }
        Assert.Single(log.Messages);
        Assert.Contains("playback failed", log.Messages[0]);
    }

    [Fact]
    public void NoDiagnosticHandlerStillDegradesCleanly()
    {
        // The handler is optional: a failing sink with no handler must not throw.
        using var dev = new RealtimeAudioDevice(() => new ThrowingPcmSink());
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
