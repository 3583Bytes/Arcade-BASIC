using System.Runtime.InteropServices.JavaScript;
using ArcadeBasic.Runtime;

// The editor's tools: the tune editor hears PLAY strings exactly as a program would
// (the engine's own AudioState), and the colour tools start from the engine's palette.
internal static partial class Playground
{
    private static string s_playError = "";

    /// <summary>The tones a <c>PLAY</c> string makes, from the starting state (octave 4,
    /// L4, T120, MN): (frequency Hz, sounded s, silent s) each, frequency 0 for a rest.
    /// Empty, with <see cref="PlayError"/> saying why, if the string is wrong.</summary>
    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    internal static double[] PlayTones(string mml)
    {
        var tones = new ToneList();
        try
        {
            new AudioState().EmitPlay(mml, tones);
            s_playError = "";
        }
        catch (BasicRuntimeException ex)
        {
            s_playError = ex.Message;
        }
        return [.. tones.Values];
    }

    [JSExport]
    internal static string PlayError() => s_playError;

    /// <summary>The 256 starting colours (<see cref="GfxPalette"/>) as 0xRRGGBB.</summary>
    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    internal static int[] DefaultPalette() =>
        [.. Enumerable.Range(0, GfxPalette.Count).Select(i => GfxPalette.DefaultArgb(i) & 0xFFFFFF)];

    private sealed class ToneList : IAudioDevice
    {
        public readonly List<double> Values = [];

        public void Emit(ToneEvent tone)
        {
            Values.Add(tone.FrequencyHz);
            Values.Add(tone.SoundedSeconds);
            Values.Add(tone.SilentSeconds);
        }

        public void Flush() { }
    }
}
