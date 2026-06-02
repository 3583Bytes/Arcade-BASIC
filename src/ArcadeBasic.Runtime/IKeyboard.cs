namespace ArcadeBasic.Runtime;

/// <summary>
/// Non-blocking keyboard source behind <c>INKEY$</c> (a Microsoft-BASIC
/// extension — not part of ISO/ECMA Full BASIC). Threaded into the interpreter
/// and VM the same way <see cref="IGraphicsDevice"/> is, with backends for the
/// console (CLI/standalone) and the IDE.
/// </summary>
public interface IKeyboard
{
    /// <summary>The next buffered keypress, or <c>""</c> if none is waiting
    /// (never blocks). A normal key is a one-character string; a special key
    /// (arrows, function keys) is two characters: <c>CHR$(0)</c> followed by a
    /// key code (the GW-BASIC convention).</summary>
    string ReadKey();
}

/// <summary>A keyboard that never has a key — the default when no real input
/// source is wired in (headless runs, piped input), so <c>INKEY$</c> yields
/// <c>""</c>.</summary>
public sealed class NullKeyboard : IKeyboard
{
    public static readonly NullKeyboard Instance = new();
    private NullKeyboard() { }
    public string ReadKey() => string.Empty;
}
