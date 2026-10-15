using ArcadeBasic.Runtime;

namespace ArcadeBasic.Web;

/// <summary>
/// The page's keyboard, fed from DOM <c>keydown</c>/<c>keyup</c>. Presses — auto-repeats
/// included, as in a terminal — queue for <c>INKEY$</c> in its spelling; and because the
/// browser reports releases, <c>KEYDOWN</c> is exact: held keys are tracked both by
/// <c>KeyboardEvent.code</c> (position) and by the lower-cased character they type.
/// </summary>
internal sealed class BrowserKeyboard : IKeyboard, IKeyState
{
    private const int MaxQueued = 256;

    private readonly Queue<string> _presses = new();
    private readonly HashSet<string> _codes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _typedBy = new(StringComparer.Ordinal);   // code → the character it typed

    public void Down(string code, string key, bool repeat)
    {
        var press = InkeyText(key);
        if (press.Length > 0)
        {
            if (_presses.Count == MaxQueued) _presses.Dequeue();
            _presses.Enqueue(press);
        }
        if (repeat) return;
        _codes.Add(code);
        if (key.Length == 1) _typedBy[code] = key.ToLowerInvariant();
    }

    public void Up(string code)
    {
        _codes.Remove(code);
        _typedBy.Remove(code);
    }

    /// <summary>The page lost focus: its keyups won't arrive, so nothing is held any more.</summary>
    public void ReleaseAll()
    {
        _codes.Clear();
        _typedBy.Clear();
    }

    public void Clear()
    {
        ReleaseAll();
        _presses.Clear();
    }

    public string ReadKey() => _presses.Count > 0 ? _presses.Dequeue() : "";

    public bool IsKeyDown(string key) =>
        key.Length == 1 ? _typedBy.ContainsValue(key) : _codes.Contains(key);

    /// <summary>What INKEY$ returns for a DOM <c>KeyboardEvent.key</c>; "" for keys it doesn't report.</summary>
    private static string InkeyText(string key) => key switch
    {
        { Length: 1 } => key,
        "Enter" => "\r",
        "Tab" => "\t",
        "Backspace" => "\b",
        "Escape" => "\x1b",
        "ArrowUp" => Extended(72),
        "ArrowDown" => Extended(80),
        "ArrowLeft" => Extended(75),
        "ArrowRight" => Extended(77),
        "Home" => Extended(71),
        "End" => Extended(79),
        "PageUp" => Extended(73),
        "PageDown" => Extended(81),
        "Insert" => Extended(82),
        "Delete" => Extended(83),
        "F1" => Extended(59), "F2" => Extended(60), "F3" => Extended(61), "F4" => Extended(62),
        "F5" => Extended(63), "F6" => Extended(64), "F7" => Extended(65), "F8" => Extended(66),
        "F9" => Extended(67), "F10" => Extended(68), "F11" => Extended(133), "F12" => Extended(134),
        _ => "",
    };

    private static string Extended(int code) => "\0" + (char)code;
}
