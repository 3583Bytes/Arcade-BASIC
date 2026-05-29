using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace ArcadeBasic.Ide;

/// <summary>
/// Bottom-of-source-tab pane that shows compile/runtime diagnostics emitted
/// by <see cref="ArcadeBasic.BasicEngine.Run"/>. Auto-opens when a run produces
/// diagnostics; toggleable via the View menu.
/// </summary>
internal sealed class ProblemsPane : FrameView
{
    private readonly TextView _view;
    private readonly Button _closeButton;
    private readonly Button _copyButton;

    /// <summary>Raised when the user clicks the close [X] button.</summary>
    public event Action? CloseRequested;

    /// <summary>Raised after a copy attempt with a short message the shell can surface in its status bar.</summary>
    public event Action<string>? StatusMessage;

    public ProblemsPane()
    {
        Title = "Problems";
        var scheme = MakeScheme();
        ColorScheme = scheme;

        // Top-right controls on row 0. The text view starts at row 1 so the
        // buttons don't overwrite diagnostics. v1 Button renders as
        // "[ Title ]" — visible width is title.Length + 4. Layout, right-aligned:
        //   ... [ Copy ] [ X ]
        //         ↑       ↑
        //         |       AnchorEnd(5)   (5 columns wide)
        //         AnchorEnd(14)          (8 columns wide + 1-column gap)
        _copyButton = new Button("Copy")
        {
            X = Pos.AnchorEnd(14),
            Y = 0,
            CanFocus = true,
            ColorScheme = scheme,
        };
        _copyButton.Clicked += CopyAll;

        _closeButton = new Button("X")
        {
            X = Pos.AnchorEnd(5),
            Y = 0,
            CanFocus = true,
            ColorScheme = scheme,
        };
        _closeButton.Clicked += () => CloseRequested?.Invoke();

        _view = new TextView
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            WordWrap = false,
            CanFocus = false,
            ColorScheme = scheme,
        };
        Add(_copyButton, _closeButton, _view);
    }

    private void CopyAll()
    {
        if (Count == 0)
        {
            StatusMessage?.Invoke("No problems to copy");
            return;
        }

        var text = _view.Text.ToString() ?? string.Empty;
        try
        {
            Clipboard.Contents = text;
            StatusMessage?.Invoke($"Copied {Count} problem{(Count == 1 ? "" : "s")} to clipboard");
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke("Copy failed: " + ex.Message);
        }
    }

    public int Count { get; private set; }

    public void SetDiagnostics(IReadOnlyList<string> diagnostics)
    {
        Count = diagnostics.Count;
        if (Count == 0)
        {
            _view.Text = string.Empty;
            Title = "Problems";
            return;
        }

        Title = $"Problems ({Count})";
        _view.Text = string.Join('\n', diagnostics.Select(d => d.TrimEnd('\n', '\r')));
        _view.MoveHome();
        SetNeedsDisplay();
    }

    public void ClearProblems()
    {
        Count = 0;
        Title = "Problems";
        _view.Text = string.Empty;
        SetNeedsDisplay();
    }

    private static ColorScheme MakeScheme()
    {
        Attribute Make(Color fg, Color bg) =>
            Application.Driver?.MakeAttribute(fg, bg) ?? default;

        return new ColorScheme
        {
            Normal = Make(Color.BrightRed, Color.Black),
            Focus = Make(Color.BrightRed, Color.Black),
            HotNormal = Make(Color.BrightRed, Color.Black),
            HotFocus = Make(Color.BrightRed, Color.Black),
            Disabled = Make(Color.DarkGray, Color.Black),
        };
    }
}
