using System.Text;
using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using Terminal.Gui;

namespace ArcadeBasic.Ide;

/// <summary>
/// Source editor pane: a <see cref="TextView"/> for the program text plus a
/// thin read-only gutter on the left that mirrors the line count, and a
/// position label on the bottom-left of the frame title row.
///
/// Syntax highlighting is applied by recoloring the editor's per-rune attributes
/// after each content change (debounced via the main loop). The mapping is the
/// same one the Unity sample's highlighter uses: keywords get one color, string
/// literals another, etc.
/// </summary>
internal sealed class SourcePane : FrameView
{
    private const int ProblemsPaneHeight = 8;

    private readonly TextView _editor;
    private readonly TextView _gutter;
    private readonly ProblemsPane _problems;
    private bool _syncingGutter;
    private bool _problemsVisible;
    private string _baseline = string.Empty;
    private Func<bool>? _pendingHighlightToken;

    public TextView Editor => _editor;
    public ProblemsPane Problems => _problems;
    public bool ProblemsVisible => _problemsVisible;
    public bool IsModified => GetText() != _baseline;

    /// <summary>Fires whenever the cursor position changes, so the shell can update its status bar.</summary>
    public event Action<int, int>? CursorMoved;

    public SourcePane()
    {
        Title = "Source — untitled";

        _gutter = new TextView
        {
            X = 0,
            Y = 0,
            Width = 4,
            Height = Dim.Fill(),
            ReadOnly = true,
            WordWrap = false,
            CanFocus = false,
            ColorScheme = MakeGutterScheme(),
        };

        _editor = new TextView
        {
            X = Pos.Right(_gutter),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            WordWrap = false,
            AllowsTab = true,
            TabWidth = 2,
            DesiredCursorVisibility = CursorVisibility.Default,
        };

        _editor.UnwrappedCursorPosition += OnCursorMoved;
        _editor.TextChanged += OnTextChanged;
        // Cursor-move and text-change events don't fire for scrolls that move the
        // viewport without moving the caret (mouse wheel, PgUp/PgDn landing on the
        // same logical line, etc.). DrawContent runs on every editor redraw, so
        // re-syncing the gutter here keeps the line numbers glued to the source.
        _editor.DrawContent += _ => SyncGutterScroll();

        _problems = new ProblemsPane
        {
            X = 0,
            Y = Pos.AnchorEnd(ProblemsPaneHeight),
            Width = Dim.Fill(),
            Height = ProblemsPaneHeight,
            Visible = false,
        };
        _problems.CloseRequested += () =>
        {
            SetProblemsVisible(false);
            _editor.SetFocus();
        };

        Add(_gutter, _editor, _problems);
    }

    public void SetProblemsVisible(bool visible)
    {
        if (_problemsVisible == visible) return;
        _problemsVisible = visible;
        _problems.Visible = visible;

        // Reclaim or yield the bottom strip for the editor + gutter.
        var editorHeight = visible ? Dim.Fill(ProblemsPaneHeight) : Dim.Fill();
        _gutter.Height = editorHeight;
        _editor.Height = editorHeight;

        LayoutSubviews();
        SetNeedsDisplay();
    }

    public string GetText() => _editor.Text.ToString() ?? string.Empty;

    public void SetText(string text)
    {
        text ??= string.Empty;
        _editor.Text = text;
        _baseline = text;
        RecomputeGutter();
        ScheduleHighlight();
    }

    /// <summary>Snapshot the current buffer as the new clean baseline (call after a successful save).</summary>
    public void MarkClean() => _baseline = GetText();

    public void SetTitle(string title)
    {
        Title = "Source — " + title;
        SetNeedsDisplay();
    }

    private void OnTextChanged()
    {
        RecomputeGutter();
        ScheduleHighlight();
    }

    private void OnCursorMoved(Point pt)
    {
        // Terminal.Gui v1 gives 0-based row/col; surface as 1-based to the user.
        CursorMoved?.Invoke(pt.Y + 1, pt.X + 1);
        SyncGutterScroll();
    }

    private void RecomputeGutter()
    {
        var text = _editor.Text.ToString() ?? string.Empty;
        var lineCount = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') lineCount++;
        }

        var sb = new StringBuilder(lineCount * 5);
        for (int i = 1; i <= lineCount; i++)
        {
            sb.Append(i);
            sb.Append('\n');
        }

        var newWidth = Math.Max(3, lineCount.ToString().Length + 1);
        if (_gutter.Frame.Width != newWidth)
        {
            _gutter.Width = newWidth;
            LayoutSubviews();
        }

        _syncingGutter = true;
        try { _gutter.Text = sb.ToString(); }
        finally { _syncingGutter = false; }

        SyncGutterScroll();
    }

    private void SyncGutterScroll()
    {
        if (_syncingGutter) return;
        if (_gutter.TopRow == _editor.TopRow) return;
        _gutter.TopRow = _editor.TopRow;
        _gutter.SetNeedsDisplay();
    }

    // ---- Syntax highlighting -----------------------------------------------

    private void ScheduleHighlight()
    {
        // Debounce: cancel any pending pass and queue a fresh one. The lexer
        // is fast enough that we can recolor on every change, but doing it on
        // the next main-loop tick avoids re-running mid-keystroke.
        if (_pendingHighlightToken is not null)
        {
            try { Application.MainLoop?.RemoveIdle(_pendingHighlightToken); } catch { }
            _pendingHighlightToken = null;
        }

        _pendingHighlightToken = Application.MainLoop?.AddIdle(() =>
        {
            _pendingHighlightToken = null;
            ApplyHighlight();
            return false;
        });
    }

    private void ApplyHighlight()
    {
        var source = _editor.Text.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(source)) return;

        List<Token> tokens;
        try
        {
            var file = new SourceFile("<editor>", source);
            var diags = new DiagnosticBag();
            tokens = new BasicLexer(file, diags).Lex();
        }
        catch
        {
            return;
        }

        SyntaxColorizer.Apply(_editor, source, tokens);
    }

    private static ColorScheme MakeGutterScheme() => new()
    {
        Normal = Application.Driver?.MakeAttribute(Color.DarkGray, Color.Black) ?? default,
        Focus = Application.Driver?.MakeAttribute(Color.DarkGray, Color.Black) ?? default,
        HotNormal = Application.Driver?.MakeAttribute(Color.DarkGray, Color.Black) ?? default,
        HotFocus = Application.Driver?.MakeAttribute(Color.DarkGray, Color.Black) ?? default,
        Disabled = Application.Driver?.MakeAttribute(Color.DarkGray, Color.Black) ?? default,
    };
}
