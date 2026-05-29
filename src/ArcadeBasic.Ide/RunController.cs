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
    private readonly Action<RunState> _onStateChanged;
    private readonly Action<IReadOnlyList<string>> _onDiagnostics;
    private readonly Action _onInputRequested;

    private CancellationTokenSource? _cts;
    private Task<BasicEngine.Result>? _task;
    private ThreadSafeWriter? _writer;
    private object? _pumpToken;
    private int _drainCursor;

    public RunController(
        OutputPane output,
        Action<RunState> onStateChanged,
        Action<IReadOnlyList<string>> onDiagnostics,
        Action onInputRequested)
    {
        _output = output;
        _onStateChanged = onStateChanged;
        _onDiagnostics = onDiagnostics;
        _onInputRequested = onInputRequested;
    }

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
        _onStateChanged(RunState.Running);

        var token = _cts.Token;
        var writer = _writer;

        var stdin = new InteractiveTextReader(_output, _onInputRequested, token);

        _task = Task.Run(() =>
        {
            try
            {
                return BasicEngine.Run(source, writer, stdin: stdin, filename: "<editor>", cancel: token);
            }
            catch (Exception ex)
            {
                return new BasicEngine.Result(1, new[] { "host exception: " + ex.Message });
            }
        }, token);

        _pumpToken = Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(80), _ =>
        {
            Drain();
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
        Drain();

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
