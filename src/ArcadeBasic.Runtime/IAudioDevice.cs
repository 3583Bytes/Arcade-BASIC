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
