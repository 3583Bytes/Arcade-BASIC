using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;
using ArcadeBasic.Compiler;
using ArcadeBasic.Vm;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Conformance.Tests;

/// <summary>
/// Audio extension (SOUND/BEEP/PLAY — Microsoft BASIC / GW-BASIC dialect): what a
/// program plays, observed through a <see cref="RecordingAudioDevice"/>, plus direct
/// <see cref="AudioState"/> checks of the MML semantics documented in docs/audio-extension.md.
/// </summary>
public class AudioTests
{
    private static string Record(string source)
    {
        var file = new SourceFile("audio.bas", source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        Assert.False(diags.HasErrors, string.Join("\n", diags.All.Select(d => d.Render(false))));

        var recorder = new RecordingAudioDevice();
        new BasicVm(BasicCompiler.Compile(program, info), new StringWriter(), TextReader.Null, null, null, recorder).Run();
        return recorder.Transcript;
    }

    // -- programs ---------------------------------------------------------

    [Fact]
    public void BeepIsAFixedTone()
    {
        var tones = Record("BEEP");
        Assert.Equal("TONE 800.0000 0.2500 0.0000\n", tones);
    }

    [Fact]
    public void SoundTakesFrequencyAndDurationInTicks()
    {
        // 18.2 ticks = 1.0 s (18.2 ticks/sec); foreground tone, no gap.
        var tones = Record("SOUND 440, 18.2");
        Assert.Equal("TONE 440.0000 1.0000 0.0000\n", tones);
    }

    [Fact]
    public void PlayFollowsNoteTempoLengthAndArticulation()
    {
        // T120 → quarter note = 0.5 s; MN → sounds 7/8 (0.4375), gap 0.0625; A in O4 = 440 Hz.
        var tones = Record("""PLAY "T120 L4 MN O4 A" """);
        Assert.Equal("TONE 440.0000 0.4375 0.0625\n", tones);
    }

    [Fact]
    public void PlayPlaysAScaleAndARest()
    {
        var tones = Record("""
            PLAY "T120 O4 L8 CDEFGAB>C"
            PLAY "P4"
            """);
        Assert.Contains("TONE 0.0000 0.5000 0.0000", tones);   // the P4 rest
    }

    [Fact]
    public void SoundBeepAndPlayMix()
    {
        var tones = Record("""
            SOUND 262, 4
            BEEP
            PLAY "MS T140 O5 L16 CDE"
            """);
        Assert.Equal(
            "TONE 262.0000 0.2198 0.0000\n" +   // 4 ticks of 18.2 a second
            "TONE 800.0000 0.2500 0.0000\n" +
            "TONE 523.2511 0.0804 0.0268\n" +   // staccato: 3/4 of a 1/16 note at T140
            "TONE 587.3295 0.0804 0.0268\n" +
            "TONE 659.2551 0.0804 0.0268\n", tones);
    }

    // -- AudioState semantics (MML) --------------------------------------

    [Fact]
    public void OctaveUpDoublesFrequency()
    {
        var lo = new RecordingAudioDevice();
        new AudioState().EmitPlay("O4 A", lo);
        var hi = new RecordingAudioDevice();
        new AudioState().EmitPlay("O4 > A", hi);
        Assert.Contains("TONE 440.0000", lo.Transcript);
        Assert.Contains("TONE 880.0000", hi.Transcript);
    }

    [Fact]
    public void LegatoSoundsFullSlot()
    {
        var d = new RecordingAudioDevice();
        new AudioState().EmitPlay("T120 ML L4 O4 A", d);
        Assert.Equal("TONE 440.0000 0.5000 0.0000\n", d.Transcript);
    }

    [Fact]
    public void DottedNoteIsOneAndAHalf()
    {
        var d = new RecordingAudioDevice();
        new AudioState().EmitPlay("T120 ML L4 O4 A.", d);   // 0.5 * 1.5 = 0.75
        Assert.Equal("TONE 440.0000 0.7500 0.0000\n", d.Transcript);
    }

    [Fact]
    public void SharpRaisesAndFlatLowersOneSemitone()
    {
        var d = new RecordingAudioDevice();
        new AudioState().EmitPlay("ML O4 A A# A-", d);
        // A=440; A#≈466.16; A-≈415.30
        Assert.Contains("TONE 440.0000", d.Transcript);
        Assert.Contains("TONE 466.1638", d.Transcript);
        Assert.Contains("TONE 415.3047", d.Transcript);
    }

    [Fact]
    public void NoteNumberZeroIsRest()
    {
        var d = new RecordingAudioDevice();
        new AudioState().EmitPlay("T120 L4 N0", d);
        Assert.Equal("TONE 0.0000 0.5000 0.0000\n", d.Transcript);
    }

    // -- range / error handling ------------------------------------------

    [Fact]
    public void SoundRejectsFrequencyBelowRange()
    {
        Assert.Throws<BasicRuntimeException>(
            () => new AudioState().EmitSound(20, 5, new RecordingAudioDevice()));
    }

    [Fact]
    public void SoundDurationZeroEmitsNothing()
    {
        var d = new RecordingAudioDevice();
        new AudioState().EmitSound(440, 0, d);
        Assert.Equal("", d.Transcript);
    }

    [Fact]
    public void PlayRejectsBadTempo()
    {
        Assert.Throws<BasicRuntimeException>(
            () => new AudioState().EmitPlay("T999 A", new RecordingAudioDevice()));
    }

    [Fact]
    public void PlayRejectsVarptrOnlyCommands()
    {
        // X (execute substring) and = (variable substitution) need GW-BASIC's
        // VARPTR$ memory pointers, which have no analog here — deliberately
        // unsupported rather than given an invented syntax (see conformance.md).
        Assert.Throws<BasicRuntimeException>(() => new AudioState().EmitPlay("X", new RecordingAudioDevice()));
        Assert.Throws<BasicRuntimeException>(() => new AudioState().EmitPlay("T=", new RecordingAudioDevice()));
    }
}
