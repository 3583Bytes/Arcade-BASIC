using System;
using System.Collections.Generic;
using System.Threading;

namespace ArcadeBasic.Runtime;

/// <summary>
/// A <b>pull-based</b> <see cref="IAudioDevice"/> for hosts that consume audio
/// through a periodic callback rather than a blocking write — Unity's streaming
/// <c>AudioClip</c> (PCM reader callback) today, and a browser/WebAudio playground
/// later. The BASIC program (on its own thread) <see cref="Emit"/>s tones, which
/// are rendered to PCM and buffered; the host pulls them on its audio thread via
/// <see cref="Read"/>. <see cref="Flush"/> (a foreground/MF statement) blocks until
/// the buffer has drained, so the program waits for the music to finish; a
/// background/MB statement skips the flush and keeps running.
///
/// No UnityEngine (or any host) dependency, so it lives in Runtime and is unit
/// tested directly. Thread-safe: <see cref="Emit"/>/<see cref="Flush"/> run on the
/// BASIC thread, <see cref="Read"/> on the host's audio thread.
/// </summary>
public sealed class BufferedAudioDevice : IAudioDevice
{
    private readonly object _lock = new();
    private readonly Queue<short> _buffer = new();
    private readonly AudioDiagnostic? _onDiagnostic;
    private int _noConsumerReported;
    private bool _closed;

    /// <param name="onDiagnostic">Optional sink for the non-fatal "no consumer is
    /// draining the buffer" condition that makes <see cref="Flush"/> give up waiting.</param>
    public BufferedAudioDevice(AudioDiagnostic? onDiagnostic = null) => _onDiagnostic = onDiagnostic;

    public void Emit(ToneEvent tone)
    {
        var pcm = PcmRenderer.Render(tone);
        lock (_lock)
        {
            foreach (var s in pcm) _buffer.Enqueue(s);
            Monitor.PulseAll(_lock);
        }
    }

    /// <summary>Foreground completion: wait until the host has played everything
    /// buffered. Bounded by the buffered audio's own duration (plus a margin) so it
    /// can't hang if no consumer is pulling.</summary>
    public void Flush()
    {
        var timedOut = false;
        lock (_lock)
        {
            while (_buffer.Count > 0 && !_closed)
            {
                // Buffered samples drain at the sample rate; wait at most that long.
                var ms = (int)(_buffer.Count * 1000L / PcmRenderer.SampleRate) + 250;
                if (!Monitor.Wait(_lock, ms)) { timedOut = true; break; }   // no consumer; give up waiting
            }
        }
        // Report outside the lock (the handler may be slow / re-enter) and only
        // once per device, so a music-heavy program with no consumer isn't spammed.
        if (timedOut && _onDiagnostic is not null
            && Interlocked.Exchange(ref _noConsumerReported, 1) == 0)
        {
            try { _onDiagnostic("audio flush timed out — no consumer is draining the buffer; continuing", null); }
            catch { /* a diagnostic sink must never break the program */ }
        }
    }

    /// <summary>Host audio-thread callback: fill <paramref name="dest"/> with the
    /// next mono float samples (range −1..1), zero-padding on underrun.</summary>
    public void Read(float[] dest)
    {
        if (dest is null) return;
        lock (_lock)
        {
            var i = 0;
            for (; i < dest.Length && _buffer.Count > 0; i++)
                dest[i] = _buffer.Dequeue() / 32768f;
            for (; i < dest.Length; i++) dest[i] = 0f;   // underrun → silence
            Monitor.PulseAll(_lock);
        }
    }

    /// <summary>Number of PCM samples still buffered (test/diagnostic aid).</summary>
    public int BufferedSamples { get { lock (_lock) return _buffer.Count; } }

    /// <summary>Unblock any pending <see cref="Flush"/> (e.g. on stop/teardown).</summary>
    public void Close()
    {
        lock (_lock) { _closed = true; Monitor.PulseAll(_lock); }
    }
}
