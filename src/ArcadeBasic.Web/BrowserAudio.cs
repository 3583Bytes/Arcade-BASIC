using ArcadeBasic.Runtime;

namespace ArcadeBasic.Web;

/// <summary>
/// The page's audio: <c>SOUND</c>, <c>BEEP</c> and <c>PLAY</c> tones, collected for the
/// page (wwwroot/audio.js) to play with the Web Audio API. Nothing waits here — the
/// browser has one thread. A foreground statement's wait is the VM's
/// <see cref="Vm.StepStatus.AwaitingAudio"/>, which the page sits out while the tones
/// play; background (<c>MB</c>) music keeps the program going until it has more than
/// GW-BASIC's 32 tones queued (<see cref="IAudioQueue"/>), then the VM waits for room.
/// </summary>
internal sealed class BrowserAudio : IAudioDevice, IAudioQueue
{
    private readonly List<double> _tones = [];
    private int _playing;   // tones the page has and hasn't finished (SetPlaying)

    /// <summary>Tones given and not played yet: the page's, and those it hasn't taken.</summary>
    public int Queued => _playing + _tones.Count / 3;

    /// <summary>How many tones the page is still playing (it says before each step).</summary>
    public void SetPlaying(int count) => _playing = count;

    public void Emit(ToneEvent tone)
    {
        _tones.Add(tone.FrequencyHz);
        _tones.Add(tone.SoundedSeconds);
        _tones.Add(tone.SilentSeconds);
    }

    /// <summary>Only <see cref="Vm.BasicVm.Run()"/> calls this, to drain; the page never runs
    /// the VM that way.</summary>
    public void Flush() { }

    /// <summary>The tones since the last call: (frequency Hz, sounded s, silent s) each,
    /// frequency 0 for a rest.</summary>
    public double[] Take()
    {
        var tones = _tones.ToArray();
        _playing += _tones.Count / 3;
        _tones.Clear();
        return tones;
    }

    public void Clear()
    {
        _tones.Clear();
        _playing = 0;
    }
}
