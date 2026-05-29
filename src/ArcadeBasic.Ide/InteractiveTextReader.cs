using Terminal.Gui;

namespace ArcadeBasic.Ide;

/// <summary>
/// TextReader the interpreter uses for INPUT / LINE INPUT / MAT INPUT statements.
/// Each <see cref="ReadLine"/> call blocks the BASIC task thread, marshals an
/// activation of the output pane's input field onto the UI thread, then returns
/// the user's submitted text. Honours the supplied <see cref="CancellationToken"/>
/// so Stop unblocks pending reads.
/// </summary>
internal sealed class InteractiveTextReader : TextReader
{
    private readonly OutputPane _output;
    private readonly Action _onInputRequested;
    private readonly CancellationToken _cancel;

    public InteractiveTextReader(OutputPane output, Action onInputRequested, CancellationToken cancel)
    {
        _output = output;
        _onInputRequested = onInputRequested;
        _cancel = cancel;
    }

    public override string? ReadLine()
    {
        _cancel.ThrowIfCancellationRequested();

        string? result = null;
        using var done = new ManualResetEventSlim(false);

        Application.MainLoop.Invoke(() =>
        {
            _onInputRequested();
            _output.BeginRead(text =>
            {
                result = text;
                done.Set();
            });
        });

        // Stop should unblock the read by cancelling the input field.
        using var reg = _cancel.Register(() =>
        {
            Application.MainLoop.Invoke(() => _output.CancelRead());
        });

        done.Wait();
        // Throwing OCE (rather than returning null) is what tells the engine
        // this was a user-initiated stop, not stdin EOF. Engine catches OCE
        // and exits cleanly with code 2; null would raise runtime error 4003
        // ("INPUT: end of input stream") inside the INPUT statement.
        _cancel.ThrowIfCancellationRequested();
        return result;
    }

    public override int Read()
    {
        var line = ReadLine();
        return string.IsNullOrEmpty(line) ? -1 : line[0];
    }
}
