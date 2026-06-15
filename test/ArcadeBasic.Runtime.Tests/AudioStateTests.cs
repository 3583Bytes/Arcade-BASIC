using ArcadeBasic.Runtime;

namespace ArcadeBasic.Runtime.Tests;

/// <summary>
/// Music Macro Language semantics of <see cref="AudioState"/> (the GW-BASIC PLAY
/// dialect — see docs/audio-extension.md), observed through a
/// <see cref="RecordingAudioDevice"/>. Tone transcript lines are
/// "TONE freq sounded silent" at F4 precision.
/// </summary>
public class AudioStateTests
{
    private static int ToneCount(string transcript) =>
        transcript.Split('\n', System.StringSplitOptions.RemoveEmptyEntries).Length;

    [Fact]
    public void OctaveShiftClampsAtBounds()
    {
        var up = new AudioState();
        up.EmitPlay("O6 >", new RecordingAudioDevice());
        Assert.Equal(6, up.Octave);

        var down = new AudioState();
        down.EmitPlay("O0 <", new RecordingAudioDevice());
        Assert.Equal(0, down.Octave);
    }

    [Fact]
    public void DefaultLengthAndPerNoteOverride()
    {
        var d = new RecordingAudioDevice();
        new AudioState().EmitPlay("T120 ML L4 A A8", d);   // quarter (0.5s) then eighth (0.25s)
        Assert.Contains("TONE 440.0000 0.5000 0.0000", d.Transcript);
        Assert.Contains("TONE 440.0000 0.2500 0.0000", d.Transcript);
    }

    [Fact]
    public void TempoControlsDuration()
    {
        var d = new RecordingAudioDevice();
        new AudioState().EmitPlay("T240 ML L4 A", d);      // quarter at 240 bpm = 0.25 s
        Assert.Equal("TONE 440.0000 0.2500 0.0000\n", d.Transcript);
    }

    [Fact]
    public void StaccatoSplitsSlotIntoSoundedAndGap()
    {
        var d = new RecordingAudioDevice();
        new AudioState().EmitPlay("MS T120 L4 A", d);      // 0.5 slot → 3/4 sounded, 1/4 gap
        Assert.Equal("TONE 440.0000 0.3750 0.1250\n", d.Transcript);
    }

    [Fact]
    public void DoubleDotIsOneAndThreeQuarters()
    {
        var d = new RecordingAudioDevice();
        new AudioState().EmitPlay("T120 ML L4 A..", d);    // 0.5 * 1.75 = 0.875
        Assert.Equal("TONE 440.0000 0.8750 0.0000\n", d.Transcript);
    }

    [Fact]
    public void StatePersistsAcrossPlayCalls()
    {
        var s = new AudioState();
        s.EmitPlay("T200 O3 L16 ML", new RecordingAudioDevice());
        Assert.Equal(200, s.Tempo);
        Assert.Equal(3, s.Octave);
        Assert.Equal(16, s.Length);

        var d = new RecordingAudioDevice();
        s.EmitPlay("A", d);                                // O3 A = 220 Hz; L16 at T200 = 0.075 s
        Assert.Equal("TONE 220.0000 0.0750 0.0000\n", d.Transcript);
    }

    [Fact]
    public void BackgroundFlagSetByMbClearedByMf()
    {
        var s = new AudioState();
        s.EmitPlay("MB CDE", new RecordingAudioDevice());
        Assert.True(s.Background);
        s.EmitPlay("MF CDE", new RecordingAudioDevice());
        Assert.False(s.Background);
    }

    [Fact]
    public void SpacesAreIgnored()
    {
        var d = new RecordingAudioDevice();
        new AudioState().EmitPlay("C D E", d);
        Assert.Equal(3, ToneCount(d.Transcript));
    }

    [Fact]
    public void OctaveDoublesFrequencyPerStep()
    {
        var d = new RecordingAudioDevice();
        new AudioState().EmitPlay("ML O3 A O4 A O5 A", d);
        Assert.Contains("TONE 220.0000", d.Transcript);
        Assert.Contains("TONE 440.0000", d.Transcript);
        Assert.Contains("TONE 880.0000", d.Transcript);
    }

    [Fact]
    public void NoteNumberAgreesWithLetterPitch()
    {
        // N58 and O4 A must be the same pitch (440 Hz).
        var d = new RecordingAudioDevice();
        new AudioState().EmitPlay("ML N58", d);
        Assert.Contains("TONE 440.0000", d.Transcript);
    }

    [Fact]
    public void SoundAcceptsFrequencyRangeEndpoints()
    {
        var lo = new RecordingAudioDevice();
        new AudioState().EmitSound(37, 1, lo);
        Assert.Contains("TONE 37.0000", lo.Transcript);

        var hi = new RecordingAudioDevice();
        new AudioState().EmitSound(32767, 1, hi);
        Assert.Contains("TONE 32767.0000", hi.Transcript);
    }

    [Fact]
    public void SoundRejectsFrequencyOutsideRange()
    {
        Assert.Throws<BasicRuntimeException>(() => new AudioState().EmitSound(36, 1, new RecordingAudioDevice()));
        Assert.Throws<BasicRuntimeException>(() => new AudioState().EmitSound(32768, 1, new RecordingAudioDevice()));
    }

    [Fact]
    public void UnknownMmlCharacterThrows()
    {
        Assert.Throws<BasicRuntimeException>(() => new AudioState().EmitPlay("CDQ", new RecordingAudioDevice()));
    }
}
