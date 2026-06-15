using System;

namespace ArcadeBasic.Runtime;

/// <summary>
/// A platform audio output: accepts 16-bit mono PCM at
/// <see cref="PcmRenderer.SampleRate"/> and plays it. A real backend blocks in
/// <see cref="Write"/> roughly in real time (the audio hardware consumes samples
/// at the sample rate), which is what paces foreground playback. The silent
/// fallback returns immediately so a machine with no audio device never stalls.
/// Concrete P/Invoke implementations live in the CLI; this seam keeps the
/// queueing/async device (<see cref="RealtimeAudioDevice"/>) platform-agnostic
/// and testable.
/// </summary>
public interface IPcmSink : IDisposable
{
    void Write(short[] samples);
}

/// <summary>No-op sink: used on machines with no audio device, or when a native
/// backend fails to initialize. Returns immediately (no real-time pacing) so a
/// silent run is never slowed down.</summary>
public sealed class SilentPcmSink : IPcmSink
{
    public void Write(short[] samples) { }
    public void Dispose() { }
}
