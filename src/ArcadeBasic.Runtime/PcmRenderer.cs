namespace ArcadeBasic.Runtime;

/// <summary>
/// Turns a <see cref="ToneEvent"/> into 16-bit mono PCM samples — the single
/// place the square-wave synthesis lives, shared by the offline WAV backend and
/// the real-time audio backends so they produce identical audio. 44.1 kHz, a
/// gentle amplitude, square wave (most faithful to the PC speaker).
/// </summary>
public static class PcmRenderer
{
    public const int SampleRate = 44100;
    public const short Amplitude = 9000;   // of 32767 — avoids clipping/harshness

    /// <summary>Render one tone (or rest) to PCM. A rest (FrequencyHz == 0) is
    /// silence for its sounded span; the trailing articulation gap is silence.</summary>
    public static short[] Render(ToneEvent tone)
    {
        var soundedN = (int)(tone.SoundedSeconds * SampleRate);
        var silentN = (int)(tone.SilentSeconds * SampleRate);
        var buf = new short[soundedN + silentN];
        if (tone.FrequencyHz > 0)
        {
            for (var i = 0; i < soundedN; i++)
            {
                var phase = (i * tone.FrequencyHz / SampleRate) % 1.0;
                buf[i] = phase < 0.5 ? Amplitude : (short)-Amplitude;
            }
        }
        // soundedN..end stays zero (silence/rest gap).
        return buf;
    }
}
