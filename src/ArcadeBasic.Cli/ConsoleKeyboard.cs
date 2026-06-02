using ArcadeBasic.Runtime;

namespace ArcadeBasic.Cli;

/// <summary>
/// <see cref="IKeyboard"/> backed by the real console: a non-blocking poll via
/// <see cref="Console.KeyAvailable"/> + <see cref="Console.ReadKey(bool)"/>
/// (intercepted, so keys aren't echoed). Used by <c>INKEY$</c> in
/// <c>arcade-basic run</c>/<c>vm</c> and standalone binaries.
/// </summary>
internal sealed class ConsoleKeyboard : IKeyboard
{
    public string ReadKey()
    {
        bool available;
        try { available = Console.KeyAvailable; }
        catch { return string.Empty; }   // input redirected — no live keyboard
        if (!available) return string.Empty;

        var k = Console.ReadKey(intercept: true);

        // Printable key → its character.
        if (k.KeyChar != '\0' && !char.IsControl(k.KeyChar))
            return k.KeyChar.ToString();

        // Special / control keys → CHR$(0) + a key code (GW-BASIC convention for
        // the arrows; common editing keys keep their familiar control codes).
        return k.Key switch
        {
            ConsoleKey.UpArrow => "\0" + (char)72,
            ConsoleKey.DownArrow => "\0" + (char)80,
            ConsoleKey.LeftArrow => "\0" + (char)75,
            ConsoleKey.RightArrow => "\0" + (char)77,
            ConsoleKey.Enter => "\r",
            ConsoleKey.Backspace => "\b",
            ConsoleKey.Tab => "\t",
            ConsoleKey.Escape => "\x1b",
            _ => k.KeyChar != '\0' ? k.KeyChar.ToString() : string.Empty,
        };
    }
}
