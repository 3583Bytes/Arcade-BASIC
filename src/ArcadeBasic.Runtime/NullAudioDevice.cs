namespace ArcadeBasic.Runtime;

/// <summary>
/// A no-op <see cref="IAudioDevice"/> used when a program runs without a real
/// audio backend (e.g. <c>arcade-basic run foo.bas</c> with no <c>--wav</c>, or a
/// headless/piped run). `SOUND`/`BEEP`/`PLAY` execute and update musical state
/// but produce no sound.
/// </summary>
public sealed class NullAudioDevice : IAudioDevice
{
    public static readonly NullAudioDevice Instance = new();

    public void Emit(ToneEvent tone) { }
    public void Flush() { }
}
