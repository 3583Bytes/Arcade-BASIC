using ArcadeBasic.Runtime;

namespace ArcadeBasic.Cli;

/// <summary>
/// <see cref="IKeyboard"/> backed by the real console: a non-blocking poll via
/// <see cref="Console.KeyAvailable"/> + <see cref="Console.ReadKey(bool)"/>
/// (intercepted, so keys aren't echoed). Used by <c>INKEY$</c> in
/// <c>arcade-basic run</c>/<c>vm</c> and standalone binaries. It says whether Shift
/// came with each key (<see cref="IKeyModifiers"/>), so <c>KEYDOWN</c> can see Shift+→.
/// </summary>
internal sealed class ConsoleKeyboard : IKeyboard, IKeyModifiers
{
    /// <summary>True once the program has polled the keyboard at least once — i.e.
    /// it's an interactive INKEY$ program that manages its own end screen, so the
    /// end-of-run "press Enter" hold should be skipped.</summary>
    public bool Polled { get; private set; }

    public bool LastKeyShifted { get; private set; }

    public string ReadKey()
    {
        Polled = true;
        bool available;
        try { available = Console.KeyAvailable; }
        catch { return string.Empty; }   // input redirected — no live keyboard
        if (!available) return string.Empty;

        var k = Console.ReadKey(intercept: true);
        LastKeyShifted = (k.Modifiers & ConsoleModifiers.Shift) != 0;

        // Printable key → its character.
        if (k.KeyChar != '\0' && !char.IsControl(k.KeyChar))
            return k.KeyChar.ToString();

        // Special / control keys → CHR$(0) + a GW-BASIC key code, as the browser's
        // keyboard gives them; common editing keys keep their familiar control codes.
        return k.Key switch
        {
            ConsoleKey.UpArrow => "\0" + (char)72,
            ConsoleKey.DownArrow => "\0" + (char)80,
            ConsoleKey.LeftArrow => "\0" + (char)75,
            ConsoleKey.RightArrow => "\0" + (char)77,
            ConsoleKey.Home => "\0" + (char)71,
            ConsoleKey.End => "\0" + (char)79,
            ConsoleKey.PageUp => "\0" + (char)73,
            ConsoleKey.PageDown => "\0" + (char)81,
            ConsoleKey.Insert => "\0" + (char)82,
            ConsoleKey.Delete => "\0" + (char)83,
            >= ConsoleKey.F1 and <= ConsoleKey.F10 => "\0" + (char)(59 + (k.Key - ConsoleKey.F1)),
            ConsoleKey.F11 => "\0" + (char)133,
            ConsoleKey.F12 => "\0" + (char)134,
            ConsoleKey.Enter => "\r",
            ConsoleKey.Backspace => "\b",
            ConsoleKey.Tab => "\t",
            ConsoleKey.Escape => "\x1b",
            _ => k.KeyChar != '\0' ? k.KeyChar.ToString() : string.Empty,
        };
    }
}
