using System.Globalization;
using System.Text;

namespace ArcadeBasic.Runtime;

/// <summary>
/// An <see cref="IAudioDevice"/> that records every <see cref="ToneEvent"/> as a
/// deterministic text transcript instead of producing sound. It is the basis of
/// the engine-parity tests: running a program on the interpreter and on the VM
/// must yield identical transcripts. Values are formatted at fixed precision so
/// the text is stable across platforms — the audio analogue of
/// <see cref="RecordingGraphicsDevice"/>.
/// </summary>
public sealed class RecordingAudioDevice : IAudioDevice
{
    private readonly StringBuilder _log = new();

    public string Transcript => _log.ToString();

    public void Emit(ToneEvent tone) =>
        _log.Append("TONE ")
            .Append(Fmt(tone.FrequencyHz)).Append(' ')
            .Append(Fmt(tone.SoundedSeconds)).Append(' ')
            .Append(Fmt(tone.SilentSeconds)).Append('\n');

    public void Flush() => _log.Append("FLUSH\n");

    private static string Fmt(double v) => v.ToString("F4", CultureInfo.InvariantCulture);
}
