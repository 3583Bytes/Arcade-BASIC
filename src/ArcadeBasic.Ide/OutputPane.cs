using System.Text;
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace ArcadeBasic.Ide;

/// <summary>
/// Read-only scrollback area that displays PRINT output and diagnostics,
/// plus a single-line input field at the bottom that the interpreter uses
/// for INPUT / LINE INPUT. The buffer is capped (<see cref="CharCap"/>) so
/// a runaway program can't blow up memory — the oldest content is trimmed
/// when the cap is exceeded.
/// </summary>
internal sealed class OutputPane : FrameView
{
    private const int CharCap = 32_000;

    private readonly TextView _view;
    private readonly TextField _input;
    private readonly StringBuilder _buffer = new();
    private Action<string?>? _inputCompletion;

    public OutputPane()
    {
        Title = "Output";
        var scheme = MakeBlackScheme();
        ColorScheme = scheme;
        _view = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill() - 1,
            ReadOnly = true,
            WordWrap = false,
            ColorScheme = scheme,
        };
        _input = new TextField(string.Empty)
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
            ColorScheme = scheme,
            ReadOnly = true,
            CanFocus = false,
        };
        _input.KeyPress += OnInputKeyPress;
        Add(_view, _input);
    }

    private static ColorScheme MakeBlackScheme()
    {
        Attribute Make(Color fg, Color bg) =>
            Application.Driver?.MakeAttribute(fg, bg) ?? default;

        return new ColorScheme
        {
            Normal = Make(Color.White, Color.Black),
            Focus = Make(Color.White, Color.Black),
            HotNormal = Make(Color.BrightCyan, Color.Black),
            HotFocus = Make(Color.BrightCyan, Color.Black),
            Disabled = Make(Color.DarkGray, Color.Black),
        };
    }

    public void Append(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _buffer.Append(text);
        if (_buffer.Length > CharCap)
        {
            _buffer.Remove(0, _buffer.Length - CharCap);
        }
        _view.Text = _buffer.ToString();
        ScrollToEnd();
    }

    public void ClearOutput()
    {
        _buffer.Clear();
        _view.Text = string.Empty;
    }

    private void ScrollToEnd()
    {
        _view.MoveEnd();
        SetNeedsDisplay();
    }

    /// <summary>
    /// Activate the input field and call <paramref name="onComplete"/> with the
    /// text the user submits (Enter) or <c>null</c> if reading is cancelled.
    /// Marshalled callers must invoke this on the UI thread.
    /// </summary>
    public void BeginRead(Action<string?> onComplete)
    {
        _inputCompletion = onComplete;
        _input.Text = string.Empty;
        _input.ReadOnly = false;
        _input.CanFocus = true;
        _input.SetFocus();
        Application.Driver?.SetCursorVisibility(CursorVisibility.Default);
        SetNeedsDisplay();
    }

    /// <summary>
    /// Cancel a pending <see cref="BeginRead"/> and invoke its callback with
    /// <c>null</c>. Safe to call when no read is in flight.
    /// </summary>
    public void CancelRead()
    {
        var cb = _inputCompletion;
        _inputCompletion = null;
        EndInputUi();
        cb?.Invoke(null);
    }

    private void OnInputKeyPress(View.KeyEventEventArgs e)
    {
        if (_inputCompletion is null) return;
        if (e.KeyEvent.Key != Key.Enter) return;

        var text = _input.Text.ToString() ?? string.Empty;
        var cb = _inputCompletion;
        _inputCompletion = null;
        Append(text + Environment.NewLine);
        EndInputUi();
        e.Handled = true;
        cb.Invoke(text);
    }

    private void EndInputUi()
    {
        _input.Text = string.Empty;
        _input.ReadOnly = true;
        _input.CanFocus = false;
    }
}
