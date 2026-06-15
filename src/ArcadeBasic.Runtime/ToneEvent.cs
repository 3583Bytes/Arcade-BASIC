namespace ArcadeBasic.Runtime;

/// <summary>
/// A single audio event on the timeline, the device-independent primitive that
/// <see cref="AudioState"/> hands an <see cref="IAudioDevice"/> — the audio
/// analogue of a clipped graphics primitive. A note sounds at
/// <see cref="FrequencyHz"/> for <see cref="SoundedSeconds"/>, then is silent for
/// <see cref="SilentSeconds"/> (the articulation gap). A rest is
/// <c>FrequencyHz == 0</c> with its duration in <see cref="SoundedSeconds"/>.
/// </summary>
public readonly record struct ToneEvent(double FrequencyHz, double SoundedSeconds, double SilentSeconds)
{
    /// <summary>Total wall-clock span of this event (sounded + silent).</summary>
    public double TotalSeconds => SoundedSeconds + SilentSeconds;
}
