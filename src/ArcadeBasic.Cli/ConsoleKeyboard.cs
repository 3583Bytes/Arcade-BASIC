using ArcadeBasic.Runtime;

namespace ArcadeBasic.Cli;

/// <summary>
/// <see cref="IKeyboard"/> backed by the real console: a non-blocking poll via
/// <see cref="Console.KeyAvailable"/> + <see cref="Console.ReadKey(bool)"/>
/// (intercepted, so keys aren't echoed). Used by <c>INKEY$</c> in
/// <c>arcade-basic run</c>/<c>vm</c> and standalone binaries. It says whether Shift
/// came with each key (<see cref="IKeyModifiers"/>), so <c>KEYDOWN</c> can see Shift+→.
/// The terminal's mouse reports arrive among the keys (once the screen has asked for
/// them): those it hands to the screen, <paramref name="screen"/>, instead.
/// </summary>
internal sealed class ConsoleKeyboard(AnsiGraphicsDevice? screen = null) : IKeyboard, IKeyModifiers
{
    /// <summary>Keys read while looking for a mouse report that turned out not to be one,
    /// to give out in order before any others.</summary>
    private readonly List<ConsoleKeyInfo> _readAhead = [];

    /// <summary>True once the program has polled the keyboard at least once — i.e.
    /// it's an interactive INKEY$ program that manages its own end screen, so the
    /// end-of-run "press Enter" hold should be skipped.</summary>
    public bool Polled { get; private set; }

    public bool LastKeyShifted { get; private set; }

    public string ReadKey()
    {
        Polled = true;
        ConsoleKeyInfo k;
        do
        {
            if (!TryNext(out k)) return string.Empty;
        } while ((k.Key == ConsoleKey.Escape || k.KeyChar == (char)27) && screen is not null && TryMouseReport());
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

    /// <summary>The next key: one read ahead, else one waiting at the console.</summary>
    private bool TryNext(out ConsoleKeyInfo k)
    {
        if (_readAhead.Count > 0)
        {
            k = _readAhead[0];
            _readAhead.RemoveAt(0);
            return true;
        }
        k = default;
        try
        {
            if (!Console.KeyAvailable) return false;
        }
        catch
        {
            return false;                // input redirected — no live keyboard
        }
        k = Console.ReadKey(intercept: true);
        return true;
    }

    /// <summary>After an Esc: is the rest of a mouse report waiting — "[&lt;" button ";"
    /// column ";" row, then "M" (pressed or moved) or "m" (released)? If so it goes to the
    /// screen; if not, the keys read go back, to come out after the Esc.</summary>
    private bool TryMouseReport()
    {
        var read = new List<ConsoleKeyInfo>();
        bool Take(out ConsoleKeyInfo k)
        {
            if (!TryNext(out k)) return false;
            read.Add(k);
            return true;
        }

        if (Take(out var bracket) && bracket.KeyChar == '[' && Take(out var less) && less.KeyChar == '<')
        {
            var fields = "";
            while (Take(out var c) && fields.Length < 20)
            {
                if (c.KeyChar is 'M' or 'm')
                {
                    var parts = fields.Split(';');
                    if (parts.Length == 3 && int.TryParse(parts[0], out var button) &&
                        int.TryParse(parts[1], out var column) && int.TryParse(parts[2], out var row))
                    {
                        screen!.MouseReport(button, column, row, released: c.KeyChar == 'm');
                        return true;
                    }
                    break;
                }
                if (!char.IsDigit(c.KeyChar) && c.KeyChar != ';') break;
                fields += c.KeyChar;
            }
        }
        _readAhead.InsertRange(0, read);
        return false;
    }
}
