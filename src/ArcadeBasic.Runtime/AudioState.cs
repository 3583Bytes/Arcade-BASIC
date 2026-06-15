using System;

namespace ArcadeBasic.Runtime;

/// <summary>
/// The device-independent core of the audio extension (Microsoft BASIC /
/// GW-BASIC dialect), shared by the interpreter and the VM so both engines
/// produce identical audio. It owns the musical state that persists across
/// statements (octave, default length, tempo, articulation, foreground/background
/// mode), lowers `SOUND`/`BEEP`/`PLAY` to <see cref="ToneEvent"/>s, and hands them
/// to an <see cref="IAudioDevice"/>. Mirrors the role of <see cref="GraphicsState"/>.
///
/// Semantics follow the GW-BASIC User's Guide; see docs/audio-extension.md for the
/// sourced spec and the implementation-defined choices (A4 = 440 Hz equal
/// temperament; `MB` background music is parsed but rendered as foreground in this
/// phase).
/// </summary>
public sealed class AudioState
{
    // Persistent PLAY state (GW-BASIC defaults).
    public int Octave = 4;        // O0..O6
    public int Length = 4;        // default note length denominator (L1..L64)
    public int Tempo = 120;       // quarter notes (L4) per minute (T32..T255)
    public bool Background;       // MB set; MF (default) clears. Phase 1: no async.

    private double _articulation = 7.0 / 8.0;   // MN default; ML=1, MS=3/4

    private const double TicksPerSecond = 18.2;  // GW-BASIC SOUND duration unit

    // -- SOUND / BEEP ----------------------------------------------------

    /// <summary>SOUND frequency, duration. Frequency in Hz (37..32767); duration
    /// in clock ticks (18.2/sec). duration = 0 stops/clears (emits nothing).</summary>
    public void EmitSound(double frequencyHz, double durationTicks, IAudioDevice device)
    {
        if (durationTicks < 0 || durationTicks > 65535)
            throw new BasicRuntimeException(5, "SOUND duration must be in 0..65535");
        if (durationTicks == 0) return;   // GW-BASIC: duration 0 turns off the current sound
        if (frequencyHz < 37 || frequencyHz > 32767)
            throw new BasicRuntimeException(5, "SOUND frequency must be in 37..32767 Hz");
        device.Emit(new ToneEvent(frequencyHz, durationTicks / TicksPerSecond, 0));
        EndStatement(device);
    }

    /// <summary>BEEP — the standard ~800 Hz / 0.25 s alert tone (≡ PRINT CHR$(7)).</summary>
    public void EmitBeep(IAudioDevice device)
    {
        device.Emit(new ToneEvent(800, 0.25, 0));
        EndStatement(device);
    }

    // A foreground (MF) statement waits for its audio to finish before the
    // program continues; a background (MB) statement returns immediately and the
    // audio plays asynchronously. On the device seam that is exactly Flush() =
    // "drain": real-time backends block until the queue empties; the offline WAV
    // and Null devices treat Flush() as a no-op (everything is already on the
    // timeline), so this is invisible to them and to the parity transcript.
    private void EndStatement(IAudioDevice device)
    {
        if (!Background) device.Flush();
    }

    // -- PLAY (Music Macro Language) -------------------------------------

    /// <summary>Parse and play a GW-BASIC MML string, updating persistent state.</summary>
    public void EmitPlay(string mml, IAudioDevice device)
    {
        int i = 0;
        while (i < mml.Length)
        {
            char c = char.ToUpperInvariant(mml[i]);
            if (c == ' ' || c == '\t') { i++; continue; }
            switch (c)
            {
                case 'O': i++; Octave = ReadInt(mml, ref i, 0, 6, "PLAY octave (O)"); break;
                case '>': i++; if (Octave < 6) Octave++; break;
                case '<': i++; if (Octave > 0) Octave--; break;
                case 'T': i++; Tempo = ReadInt(mml, ref i, 32, 255, "PLAY tempo (T)"); break;
                case 'L': i++; Length = ReadInt(mml, ref i, 1, 64, "PLAY length (L)"); break;
                case 'N': i++; EmitNoteNumber(ReadInt(mml, ref i, 0, 84, "PLAY note (N)"), device); break;
                case 'P':
                {
                    i++;
                    int len = ReadInt(mml, ref i, 1, 64, "PLAY pause (P)");
                    int dots = ReadDots(mml, ref i);
                    device.Emit(new ToneEvent(0, SlotSeconds(len, dots), 0));   // rest
                    break;
                }
                case 'M': i++; ParseModeCommand(mml, ref i); break;
                case 'A': case 'B': case 'C': case 'D': case 'E': case 'F': case 'G':
                    i++; EmitLetterNote(c, mml, ref i, device); break;
                case 'X':
                    throw new BasicRuntimeException(5, "PLAY: X (substring) is not yet implemented");
                case '=':
                    throw new BasicRuntimeException(5, "PLAY: = (variable substitution) is not yet implemented");
                default:
                    throw new BasicRuntimeException(5, $"PLAY: unexpected character '{mml[i]}'");
            }
        }
        EndStatement(device);   // MF waits for completion; MB returns immediately
    }

    private void ParseModeCommand(string s, ref int i)
    {
        if (i >= s.Length) throw new BasicRuntimeException(5, "PLAY: M must be followed by N/L/S/F/B");
        char m = char.ToUpperInvariant(s[i]);
        i++;
        switch (m)
        {
            case 'N': _articulation = 7.0 / 8.0; break;   // normal
            case 'L': _articulation = 1.0; break;         // legato
            case 'S': _articulation = 3.0 / 4.0; break;   // staccato
            case 'F': Background = false; break;          // foreground
            case 'B': Background = true; break;           // background (Phase 1: rendered as foreground)
            default: throw new BasicRuntimeException(5, $"PLAY: unknown M command 'M{s[i - 1]}'");
        }
    }

    private void EmitLetterNote(char letter, string s, ref int i, IAudioDevice device)
    {
        int semitone = letter switch
        {
            'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, 'B' => 11,
            _ => throw new BasicRuntimeException(5, $"PLAY: bad note '{letter}'"),
        };
        // Optional accidental (one of # + -).
        if (i < s.Length)
        {
            char a = s[i];
            if (a == '#' || a == '+') { semitone++; i++; }
            else if (a == '-') { semitone--; i++; }
        }
        // Optional per-note length override, else the current default L.
        int len = ReadOptInt(s, ref i, 1, 64, "PLAY note length");
        if (len < 0) len = Length;
        int dots = ReadDots(s, ref i);

        int midi = 12 * (Octave + 1) + semitone;
        EmitPitched(MidiToFreq(midi), SlotSeconds(len, dots), device);
    }

    private void EmitNoteNumber(int note, IAudioDevice device)
    {
        double slot = SlotSeconds(Length, 0);   // N uses the current L; no dots
        if (note == 0) { device.Emit(new ToneEvent(0, slot, 0)); return; }   // rest
        EmitPitched(MidiToFreq(11 + note), slot, device);
    }

    private void EmitPitched(double freq, double slotSeconds, IAudioDevice device)
    {
        double sounded = slotSeconds * _articulation;
        device.Emit(new ToneEvent(freq, sounded, slotSeconds - sounded));
    }

    // Seconds for a note/rest of length denominator `len` with `dots` dots.
    // A quarter note (L4) lasts 60/Tempo s; length n lasts 240/(n*Tempo) s.
    // Each dot adds half of the previous increment: factor = 2 - (1/2)^dots.
    private double SlotSeconds(int len, int dots)
    {
        double basic = 240.0 / (len * Tempo);
        double factor = 2.0 - Math.Pow(0.5, dots);
        return basic * factor;
    }

    // A4 (MIDI 69) = 440 Hz, equal temperament. Octave numbers follow scientific
    // pitch notation (O4 is the octave of A440). Implementation-defined; GW-BASIC's
    // 8253-divisor frequencies differ by a few cents.
    private static double MidiToFreq(int midi) => 440.0 * Math.Pow(2.0, (midi - 69) / 12.0);

    // -- little MML scanners ---------------------------------------------

    private static int ReadInt(string s, ref int i, int min, int max, string what)
    {
        int v = ReadOptInt(s, ref i, min, max, what);
        if (v < 0) throw new BasicRuntimeException(5, $"{what}: expected a number");
        return v;
    }

    // Reads an optional run of digits. Returns -1 if no digit is present.
    private static int ReadOptInt(string s, ref int i, int min, int max, string what)
    {
        int start = i;
        long v = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9')
        {
            v = v * 10 + (s[i] - '0');
            if (v > 1_000_000) v = 1_000_000;   // guard against silly input
            i++;
        }
        if (i == start) return -1;
        if (v < min || v > max)
            throw new BasicRuntimeException(5, $"{what}: value {v} out of range {min}..{max}");
        return (int)v;
    }

    private static int ReadDots(string s, ref int i)
    {
        int dots = 0;
        while (i < s.Length && s[i] == '.') { dots++; i++; }
        return dots;
    }
}
