namespace ArcadeBasic.Runtime;

/// <summary>
/// Wraps an input <see cref="TextReader"/> and presents the latest frame of an
/// <see cref="AnsiGraphicsDevice"/> just before each line read — so an
/// interactive graphics program shows its current picture right before it blocks
/// for input. Counts line reads so the caller can tell an interactive program
/// (read at least once) from a static drawing (never read).
/// </summary>
public sealed class PresentingTextReader : TextReader
{
    private readonly TextReader _inner;
    private readonly AnsiGraphicsDevice _device;

    public PresentingTextReader(TextReader inner, AnsiGraphicsDevice device)
    {
        _inner = inner;
        _device = device;
    }

    /// <summary>How many times a line read has been requested.</summary>
    public int LineReadCount { get; private set; }

    public override string? ReadLine()
    {
        _device.Present();
        LineReadCount++;
        return _inner.ReadLine();
    }

    public override int Peek() => _inner.Peek();
    public override int Read() => _inner.Read();
}
