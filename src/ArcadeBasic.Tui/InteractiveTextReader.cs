using Terminal.Gui;

namespace ArcadeBasic.Tui;

/// <summary>
/// TextReader the interpreter uses for INPUT / LINE INPUT / MAT INPUT statements.
/// Each call to <see cref="ReadLine"/> blocks the BASIC task thread, marshals
/// a prompt dialog onto the UI thread, then returns the user's input. Honours
/// the supplied <see cref="CancellationToken"/> so Stop unblocks pending reads.
/// </summary>
internal sealed class InteractiveTextReader : TextReader
{
    private readonly CancellationToken _cancel;

    public InteractiveTextReader(CancellationToken cancel)
    {
        _cancel = cancel;
    }

    public override string? ReadLine()
    {
        if (_cancel.IsCancellationRequested) return null;

        string? result = null;
        var done = new ManualResetEventSlim(false);

        // Hop onto the UI thread to pop the dialog. The BASIC task thread
        // blocks below until the dialog closes (or the cancellation token
        // fires, which also signals the wait handle from the same thread).
        Application.MainLoop.Invoke(() =>
        {
            var dialog = new Dialog("INPUT", 60, 7);
            var field = new TextField(string.Empty)
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill() - 2,
            };
            var ok = new Button("OK", is_default: true);
            var cancel = new Button("Cancel");

            ok.Clicked += () =>
            {
                result = field.Text.ToString() ?? string.Empty;
                Application.RequestStop(dialog);
            };
            cancel.Clicked += () =>
            {
                result = null;
                Application.RequestStop(dialog);
            };

            dialog.Add(field);
            dialog.AddButton(ok);
            dialog.AddButton(cancel);

            field.SetFocus();
            Application.Run(dialog);
            done.Set();
        });

        // Wait for either the user to submit / cancel, OR Stop to fire.
        var handles = new[] { done.WaitHandle, _cancel.WaitHandle };
        WaitHandle.WaitAny(handles);
        done.Dispose();
        return _cancel.IsCancellationRequested ? null : result;
    }

    public override int Read()
    {
        // Single-char read isn't used by the interpreter's INPUT paths, but
        // implement it for completeness: pull a line and return its first
        // character (or -1 if cancelled / no input).
        var line = ReadLine();
        return string.IsNullOrEmpty(line) ? -1 : line[0];
    }
}
