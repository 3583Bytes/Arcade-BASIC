using System.Text;
using Terminal.Gui;

namespace ArcadeBasic.Ide;

/// <summary>
/// Owns one run of a BASIC program. The interpreter runs on a Task; output
/// streams into a thread-safe sink that the main loop drains every ~80ms into
/// the <see cref="OutputPane"/>. Stop cancels mid-statement via
/// <see cref="CancellationTokenSource"/>; the interpreter checks the token
/// between statements and returns exit code 2 on cancellation.
/// </summary>
internal sealed class RunController
{
    private readonly OutputPane _output;
    private readonly GraphicsPane _graphics;
    private readonly Action<RunState> _onStateChanged;
    private readonly Action<IReadOnlyList<string>> _onDiagnostics;
    private readonly Action<bool> _onInputRequested;
    private readonly Action _onGraphicsDrawn;

    private CancellationTokenSource? _cts;
    private Task<BasicEngine.Result>? _task;
    private ThreadSafeWriter? _writer;
    private object? _pumpToken;
    private int _drainCursor;
    private bool _graphicsShown;

    private readonly TuiKeyboard _keyboard = new();
    private Func<KeyEvent, bool>? _prevRootKey;
    private volatile bool _readActive;   // true while a LINE INPUT / INPUT read is pending

    public RunController(
        OutputPane output,
        GraphicsPane graphics,
        Action<RunState> onStateChanged,
        Action<IReadOnlyList<string>> onDiagnostics,
        Action<bool> onInputRequested,
        Action onGraphicsDrawn)
    {
        _output = output;
        _graphics = graphics;
        _onStateChanged = onStateChanged;
        _onDiagnostics = onDiagnostics;
        _onInputRequested = onInputRequested;
        _onGraphicsDrawn = onGraphicsDrawn;
    }

    // Once a program has drawn anything, INPUT is served on the graphics
    // surface (so the board and its prompt sit together); otherwise on the text
    // output pane.
    private IInputSink ActiveSink() => _graphics.Canvas.IsEmpty ? _output : _graphics;

    public enum RunState { Idle, Running, Cancelled, Failed, Succeeded }

    public bool IsRunning => _task is not null;

    public void Run(string source)
    {
        if (_task is not null) return;
        if (string.IsNullOrWhiteSpace(source))
        {
            _output.Append("[nothing to run]\n");
            return;
        }

        _cts = new CancellationTokenSource();
        _writer = new ThreadSafeWriter();
        _drainCursor = 0;
        _graphicsShown = false;
        _graphics.Canvas.ClearBuffer();
        _keyboard.Clear();
        InstallKeyHook();
        _onStateChanged(RunState.Running);

        var token = _cts.Token;
        var writer = _writer;
        var device = new TuiGraphicsDevice(_graphics.Canvas);
        var keyboard = _keyboard;

        var stdin = new InteractiveTextReader(
            beginRead: done =>
            {
                var useGraphics = !_graphics.Canvas.IsEmpty;
                _onInputRequested(useGraphics);
                _readActive = true;                       // route keys to the input field, not INKEY$
                ActiveSink().BeginRead(result => { _readActive = false; done(result); });
            },
            cancelRead: () => { _readActive = false; ActiveSink().CancelRead(); },
            token);

        _task = Task.Run(() =>
        {
            try
            {
                return BasicEngine.Run(source, writer, stdin: stdin, filename: "<editor>", cancel: token, graphics: device, keyboard: keyboard);
            }
            catch (Exception ex)
            {
                return new BasicEngine.Result(1, new[] { "host exception: " + ex.Message });
            }
        }, token);

        _pumpToken = Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(80), _ =>
        {
            Drain();
            if (!_graphics.Canvas.IsEmpty)
            {
                _graphics.Canvas.SetNeedsDisplay();
                if (!_graphicsShown) { _graphicsShown = true; _onGraphicsDrawn(); }
            }
            if (_task is { IsCompleted: true })
            {
                Finish();
                return false;
            }
            return true;
        });
    }

    public void Stop()
    {
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { /* racing with Finish */ }
    }

    // While a program runs, capture game keys (printable + arrows) globally and
    // feed them to INKEY$ — independent of which view has focus. Esc, function
    // keys, and Ctrl-combos pass through untouched so Stop and IDE shortcuts keep
    // working; during a LINE INPUT read, keys go to the input field instead.
    private void InstallKeyHook()
    {
        _prevRootKey = Application.RootKeyEvent;
        Application.RootKeyEvent = OnRootKey;
    }

    private void RemoveKeyHook()
    {
        Application.RootKeyEvent = _prevRootKey;
        _prevRootKey = null;
    }

    private bool OnRootKey(KeyEvent ke)
    {
        if (_task is null || _readActive) return _prevRootKey?.Invoke(ke) ?? false;
        var key = MapGameKey(ke);
        if (key.Length == 0) return _prevRootKey?.Invoke(ke) ?? false;   // Esc/F-keys/Ctrl → passthrough
        _keyboard.Enqueue(key);
        return true;   // consume so the keypress doesn't leak into the editor
    }

    private static string MapGameKey(KeyEvent ke) => ke.Key switch
    {
        Key.CursorUp => "\0" + (char)72,
        Key.CursorDown => "\0" + (char)80,
        Key.CursorLeft => "\0" + (char)75,
        Key.CursorRight => "\0" + (char)77,
        _ => ke.KeyValue is >= 32 and < 127 ? ((char)ke.KeyValue).ToString() : string.Empty,
    };

    private void Drain()
    {
        if (_writer is null) return;
        var snapshot = _writer.Snapshot(out int totalLen);
        if (totalLen > _drainCursor)
        {
            _output.Append(snapshot.Substring(_drainCursor, totalLen - _drainCursor));
            _drainCursor = totalLen;
        }
    }

    private void Finish()
    {
        RemoveKeyHook();
        _readActive = false;
        Drain();
        if (!_graphics.Canvas.IsEmpty)
        {
            _graphics.Canvas.SetNeedsDisplay();
            _onGraphicsDrawn();
        }

        var task = _task!;
        var cts = _cts;
        _task = null;
        _writer = null;
        _cts = null;
        _pumpToken = null;

        var result = task.Result;
        _onDiagnostics(result.Diagnostics);

        switch (result.ExitCode)
        {
            case 0:
                _onStateChanged(RunState.Succeeded);
                break;
            case 2:
                _output.Append("[cancelled]\n");
                _onStateChanged(RunState.Cancelled);
                break;
            default:
                _output.Append($"[exit {result.ExitCode}]\n");
                _onStateChanged(RunState.Failed);
                break;
        }

        try { cts?.Dispose(); } catch { }
    }

    /// <summary>
    /// Thread-safe text sink the interpreter (running on a Task) writes to
    /// while the main thread polls. Mirrors the Unity sample's pattern.
    /// </summary>
    private sealed class ThreadSafeWriter : TextWriter
    {
        private readonly StringBuilder _sb = new();
        private readonly object _lock = new();
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) { lock (_lock) _sb.Append(value); }
        public override void Write(string? value) { lock (_lock) _sb.Append(value); }
        public override void Write(char[] buffer, int index, int count) { lock (_lock) _sb.Append(buffer, index, count); }
        public string Snapshot(out int totalLen) { lock (_lock) { totalLen = _sb.Length; return _sb.ToString(); } }
    }
}
