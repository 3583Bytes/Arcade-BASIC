using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace ArcadeBasic.Ide;

/// <summary>
/// The Graphics tab: a <see cref="BrailleCanvas"/> with its own bottom input
/// line. A program that draws can also read INPUT here, so the view doesn't
/// jump to the text Output tab mid-loop. The interpreter writes prompts to the
/// canvas with GRAPH TEXT; this field just collects the typed value.
/// </summary>
internal sealed class GraphicsPane : FrameView, IInputSink
{
    private readonly BrailleCanvas _canvas = new();
    private readonly TextField _input;
    private Action<string?>? _inputCompletion;

    public GraphicsPane()
    {
        Title = "Graphics";
        // The canvas isn't focusable and the input only becomes focusable during
        // a read, so the pane has no focusable child at construction. Mark the
        // pane itself focusable, otherwise toggling the input field's CanFocus
        // later throws "SuperView CanFocus is false".
        CanFocus = true;
        var scheme = MakeBlackScheme();
        ColorScheme = scheme;

        _canvas.X = 0;
        _canvas.Y = 0;
        _canvas.Width = Dim.Fill();
        _canvas.Height = Dim.Fill() - 1;

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
        Add(_canvas, _input);
    }

    public BrailleCanvas Canvas => _canvas;

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
}
