using System.Collections.Concurrent;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Ide;

/// <summary>
/// <see cref="IKeyboard"/> for the IDE: the UI thread enqueues keypresses (via a
/// global key hook while a program runs) and the program's background thread
/// drains them through <c>INKEY$</c>. The queue is the only shared state, so it
/// is concurrency-safe by construction.
/// </summary>
internal sealed class TuiKeyboard : IKeyboard
{
    private readonly ConcurrentQueue<string> _keys = new();

    public void Enqueue(string key)
    {
        if (!string.IsNullOrEmpty(key)) _keys.Enqueue(key);
    }

    public void Clear()
    {
        while (_keys.TryDequeue(out _)) { }
    }

    public string ReadKey() => _keys.TryDequeue(out var k) ? k : string.Empty;
}
