namespace ArcadeBasic.Runtime;

/// <summary>
/// A line source that can say "nothing yet" instead of blocking. When the reader
/// handed to the bytecode VM implements this, <c>INPUT</c> / <c>LINE INPUT</c> /
/// <c>MAT INPUT</c> with no line available pause the program (the VM's step
/// returns <c>NeedsInput</c>) rather than block the host thread — which a browser
/// or a game's main loop can't afford.
/// </summary>
public interface INonBlockingLineReader
{
    /// <summary>
    /// Take the next line if one is ready. Returns false when nothing is available
    /// yet. Returns true with <paramref name="line"/> = <c>null</c> at end of input.
    /// </summary>
    bool TryReadLine(out string? line);
}

/// <summary>
/// An input queue for hosts that step the VM: the host <see cref="Provide"/>s lines
/// as the user enters them (from a text box, say) and calls <see cref="Complete"/>
/// to signal end of input. Reading never blocks. Safe to feed from another thread.
/// </summary>
public sealed class LineInputQueue : TextReader, INonBlockingLineReader
{
    private readonly Queue<string> _lines = new();
    private bool _complete;

    /// <summary>Queue one line of input (without its line terminator).</summary>
    public void Provide(string line)
    {
        if (line is null) throw new ArgumentNullException(nameof(line));
        lock (_lines) _lines.Enqueue(line);
    }

    /// <summary>End of input: once the queued lines are read, reads report end of
    /// stream (an <c>INPUT</c> then raises its end-of-input exception).</summary>
    public void Complete()
    {
        lock (_lines) _complete = true;
    }

    /// <summary>Lines queued and not yet read.</summary>
    public int Count
    {
        get { lock (_lines) return _lines.Count; }
    }

    public bool TryReadLine(out string? line)
    {
        lock (_lines)
        {
            if (_lines.Count > 0)
            {
                line = _lines.Dequeue();
                return true;
            }
            line = null;
            return _complete;
        }
    }

    /// <summary>The next queued line, or <c>null</c> if none is queued — this reader
    /// never waits, so to a blocking caller an empty queue looks like end of input.</summary>
    public override string? ReadLine()
    {
        lock (_lines) return _lines.Count > 0 ? _lines.Dequeue() : null;
    }
}
