using System.Diagnostics;

namespace ArcadeBasic.Runtime;

/// <summary>
/// Optional companion to <see cref="IKeyboard"/> for a backend that sees key
/// <i>releases</i> — a browser, a game engine — and so knows exactly which keys are
/// held. <c>KEYDOWN</c> asks it directly; keyboards without it (terminals only
/// report presses) get an estimate from presses and auto-repeat instead.
/// </summary>
public interface IKeyState
{
    /// <summary>Is <paramref name="key"/> held right now?</summary>
    /// <param name="key">A canonical key from <see cref="KeyNames.Canonical"/>: either a
    /// W3C <c>KeyboardEvent.code</c> value in its standard spelling (<c>"ArrowLeft"</c>,
    /// <c>"Space"</c>, <c>"KeyA"</c> — a physical position), or a single character,
    /// lower-case for letters, meaning the key that types it (compare with a
    /// browser's lower-cased <c>KeyboardEvent.key</c>).</param>
    bool IsKeyDown(string key);
}

/// <summary>
/// The key names <c>KEYDOWN</c> understands: what <c>INKEY$</c> returns for a key
/// (a character, or <c>CHR$(0)</c> + a GW-BASIC extended key code), and the W3C UI
/// Events <c>KeyboardEvent.code</c> names, ignoring case.
/// </summary>
public static class KeyNames
{
    /// <summary>W3C UI Events KeyboardEvent.code values (writing system, functional,
    /// control pad, arrow, numpad and function sections), in canonical spelling.</summary>
    private static readonly string[] W3cCodes =
    [
        "Backquote", "Backslash", "BracketLeft", "BracketRight", "Comma",
        "Digit0", "Digit1", "Digit2", "Digit3", "Digit4", "Digit5", "Digit6", "Digit7", "Digit8", "Digit9",
        "Equal", "IntlBackslash", "IntlRo", "IntlYen",
        "KeyA", "KeyB", "KeyC", "KeyD", "KeyE", "KeyF", "KeyG", "KeyH", "KeyI", "KeyJ", "KeyK", "KeyL", "KeyM",
        "KeyN", "KeyO", "KeyP", "KeyQ", "KeyR", "KeyS", "KeyT", "KeyU", "KeyV", "KeyW", "KeyX", "KeyY", "KeyZ",
        "Minus", "Period", "Quote", "Semicolon", "Slash",
        "AltLeft", "AltRight", "Backspace", "CapsLock", "ContextMenu", "ControlLeft", "ControlRight",
        "Enter", "MetaLeft", "MetaRight", "ShiftLeft", "ShiftRight", "Space", "Tab",
        "Delete", "End", "Help", "Home", "Insert", "PageDown", "PageUp",
        "ArrowDown", "ArrowLeft", "ArrowRight", "ArrowUp",
        "NumLock", "Numpad0", "Numpad1", "Numpad2", "Numpad3", "Numpad4", "Numpad5", "Numpad6", "Numpad7",
        "Numpad8", "Numpad9", "NumpadAdd", "NumpadDecimal", "NumpadDivide", "NumpadEnter", "NumpadEqual",
        "NumpadMultiply", "NumpadSubtract",
        "Escape", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "PrintScreen", "ScrollLock", "Pause",
    ];

    private static readonly Dictionary<string, string> s_codes =
        W3cCodes.ToDictionary(c => c, c => c, StringComparer.OrdinalIgnoreCase);

    /// <summary>The character each writing-system key types on a US layout — all a
    /// terminal can tell us about which key was pressed.</summary>
    private static readonly Dictionary<string, string> s_usCharacters = BuildUsCharacters();

    /// <summary>
    /// The canonical identity of a key, or null if <paramref name="key"/> names none:
    /// a W3C code name (<c>"ArrowLeft"</c>), or one character — lower-case if a letter —
    /// for "the key that types it". INKEY$'s spellings map onto these: <c>" "</c> →
    /// Space, <c>CHR$(13)</c> → Enter, <c>CHR$(0) + CHR$(75)</c> → ArrowLeft, <c>"A"</c> → <c>"a"</c>.
    /// </summary>
    public static string? Canonical(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (key.Length == 2 && key[0] == '\0') return ExtendedKey(key[1]);
        if (key.Length == 1)
        {
            var c = key[0];
            switch (c)
            {
                case ' ': return "Space";
                case '\r': return "Enter";
                case '\t': return "Tab";
                case '\b': return "Backspace";
                case '\x1b': return "Escape";
            }
            return char.IsControl(c) ? null : char.ToLowerInvariant(c).ToString();
        }
        return s_codes.TryGetValue(key, out var code) ? code : null;
    }

    /// <summary>For a keyboard that only reports characters: the character a canonical
    /// writing-system key types on a US layout (<c>"KeyA"</c> → <c>"a"</c>); any other key unchanged.</summary>
    public static string AsTyped(string canonical) =>
        s_usCharacters.TryGetValue(canonical, out var c) ? c : canonical;

    /// <summary>INKEY$'s CHR$(0) + code keys: the GW-BASIC / QBasic extended key codes.</summary>
    private static string? ExtendedKey(char code) => code switch
    {
        (char)59 => "F1", (char)60 => "F2", (char)61 => "F3", (char)62 => "F4", (char)63 => "F5",
        (char)64 => "F6", (char)65 => "F7", (char)66 => "F8", (char)67 => "F9", (char)68 => "F10",
        (char)133 => "F11", (char)134 => "F12",
        (char)71 => "Home", (char)72 => "ArrowUp", (char)73 => "PageUp",
        (char)75 => "ArrowLeft", (char)77 => "ArrowRight",
        (char)79 => "End", (char)80 => "ArrowDown", (char)81 => "PageDown",
        (char)82 => "Insert", (char)83 => "Delete",
        _ => null,
    };

    private static Dictionary<string, string> BuildUsCharacters()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Backquote"] = "`", ["Minus"] = "-", ["Equal"] = "=", ["BracketLeft"] = "[", ["BracketRight"] = "]",
            ["Backslash"] = "\\", ["Semicolon"] = ";", ["Quote"] = "'", ["Comma"] = ",", ["Period"] = ".", ["Slash"] = "/",
        };
        for (var c = 'a'; c <= 'z'; c++) map["Key" + char.ToUpperInvariant(c)] = c.ToString();
        for (var d = '0'; d <= '9'; d++) map["Digit" + d] = d.ToString();
        return map;
    }
}

/// <summary>
/// One run's keyboard as the program sees it: <c>INKEY$</c>'s presses and
/// <c>KEYDOWN</c>'s held keys.
///
/// A keyboard implementing <see cref="IKeyState"/> answers KEYDOWN exactly. For
/// one that only reports presses — a terminal, where holding a key arrives as a
/// press and then, after the OS's repeat delay, a stream of repeats — the held
/// state is estimated: a lone press reads as held for <see cref="TapSeconds"/>, and
/// once repeats arrive the key stays held until <see cref="RepeatSeconds"/> after
/// the last one. So a tap nudges, a hold moves smoothly once the OS starts
/// repeating, and a release takes effect within a fifth of a second.
/// </summary>
public sealed class KeyboardState
{
    /// <summary>How long a single press counts as held.</summary>
    public const double TapSeconds = 0.1;

    /// <summary>How long a key counts as held after each auto-repeat.</summary>
    public const double RepeatSeconds = 0.2;

    /// <summary>A second press of a key within this long is an auto-repeat (covers
    /// the OS's initial repeat delay, typically 0.25–0.66 s).</summary>
    public const double RepeatDetectSeconds = 0.75;

    /// <summary>Presses collected for INKEY$ that it hasn't read yet; older ones drop.</summary>
    private const int MaxPending = 256;

    private readonly IKeyboard _keyboard;
    private readonly IKeyState? _keyState;
    private readonly Func<double> _clock;
    private readonly Queue<string> _pending = new();
    private readonly Dictionary<string, (double At, bool Repeating)> _lastPress = new(StringComparer.Ordinal);

    /// <param name="keyboard">The run's keyboard.</param>
    /// <param name="clock">Seconds on a monotonic clock (tests pass a fake); defaults to <see cref="Stopwatch"/>.</param>
    public KeyboardState(IKeyboard keyboard, Func<double>? clock = null)
    {
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _keyState = keyboard as IKeyState;
        _clock = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
    }

    /// <summary><c>INKEY$</c>: the next press, or <c>""</c>. One read of the keyboard per
    /// call, as before KEYDOWN existed — after any presses KEYDOWN already collected.</summary>
    public string ReadKey()
    {
        if (_pending.Count > 0) return _pending.Dequeue();
        var key = _keyboard.ReadKey();
        Record(key);
        return key;
    }

    /// <summary><c>KEYDOWN</c>: is the key held? Doesn't consume presses — INKEY$ still
    /// sees each one. Raises exception 4000 if <paramref name="key"/> names no key.</summary>
    public bool IsDown(string key)
    {
        var canonical = KeyNames.Canonical(key)
            ?? throw new BasicRuntimeException(4000, $"KEYDOWN: {Describe(key)} is not a key name");
        if (_keyState is not null) return _keyState.IsKeyDown(canonical);

        Collect();
        if (!_lastPress.TryGetValue(KeyNames.AsTyped(canonical), out var press)) return false;
        return _clock() - press.At < (press.Repeating ? RepeatSeconds : TapSeconds);
    }

    /// <summary>Read every waiting press now, so the estimate is current; keep them for INKEY$.</summary>
    private void Collect()
    {
        for (var i = 0; i < MaxPending; i++)
        {
            var key = _keyboard.ReadKey();
            if (key.Length == 0) return;
            Record(key);
            if (_pending.Count == MaxPending) _pending.Dequeue();
            _pending.Enqueue(key);
        }
    }

    private void Record(string key)
    {
        if (_keyState is not null || key.Length == 0) return;
        if (KeyNames.Canonical(key) is not { } canonical) return;
        var now = _clock();
        var repeating = _lastPress.TryGetValue(canonical, out var last) && now - last.At <= RepeatDetectSeconds;
        _lastPress[canonical] = (now, repeating);
    }

    /// <summary>The argument as BASIC source would spell it, for the error message.</summary>
    private static string Describe(string key) =>
        key.Any(char.IsControl)
            ? string.Join(" & ", key.Select(c => char.IsControl(c) ? $"CHR$({(int)c})" : $"\"{c}\""))
            : $"\"{key}\"";
}
