using System.Text;
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace ArcadeBasic.Tui;

/// <summary>
/// Read-only scrollback area that displays PRINT output and diagnostics.
/// The buffer is capped (<see cref="CharCap"/>) so a runaway program can't
/// blow up memory — the oldest content is trimmed when the cap is exceeded.
/// </summary>
internal sealed class OutputPane : FrameView
{
    private const int CharCap = 32_000;

    private readonly TextView _view;
    private readonly StringBuilder _buffer = new();

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
            Height = Dim.Fill(),
            ReadOnly = true,
            WordWrap = false,
            ColorScheme = scheme,
        };
        Add(_view);
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
}
