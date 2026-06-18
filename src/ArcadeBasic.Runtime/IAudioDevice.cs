namespace ArcadeBasic.Runtime;

/// <summary>
/// The backend seam for the audio extension (`SOUND`/`BEEP`/`PLAY` — Microsoft
/// BASIC / GW-BASIC dialect). Mirrors <see cref="IGraphicsDevice"/>: the
/// device-independent core (<see cref="AudioState"/>) does all the musical work —
/// MML parsing, tempo/length/articulation timing, pitch computation — and hands
/// the backend a stream of already-resolved <see cref="ToneEvent"/>s. Backends
/// turn those into sound (PCM samples for a WAV file, a real-time audio sink, a
/// Unity AudioClip) or, for tests, a transcript.
///
/// Implementations must stay netstandard2.1- and IL2CPP-safe: no reflection, no
/// dynamic codegen, only simple value types across the boundary.
/// </summary>
public interface IAudioDevice
{
    /// <summary>Append one tone (or rest) to the audio timeline.</summary>
    void Emit(ToneEvent tone);

    /// <summary>Present/finalize buffered audio (end of program, or between frames).</summary>
    void Flush();
}

/// <summary>
/// Optional sink for <b>non-fatal</b> audio diagnostics: an initialization or
/// playback failure that a device handles by degrading to silence rather than
/// throwing (a <c>SOUND</c> statement must never crash a program just because the
/// machine has no speakers). Routing this to stderr or a log turns an otherwise
/// mysterious silent run into an explainable one. Handlers must not throw —
/// devices invoke it from a background audio thread and ignore any exception it
/// raises. netstandard2.1- and IL2CPP-safe (a plain delegate, no reflection).
/// </summary>
/// <param name="message">Human-readable description of what degraded.</param>
/// <param name="error">The underlying exception, if one was thrown.</param>
public delegate void AudioDiagnostic(string message, Exception? error);
