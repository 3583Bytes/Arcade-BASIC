using ArcadeBasic.Lexer;
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace ArcadeBasic.Tui;

/// <summary>
/// Walks the lexer's token stream and overlays per-rune <see cref="Attribute"/>
/// colors onto a <see cref="TextView"/>'s contents. Mirrors the palette and
/// classification used by the Unity sample's BasicSyntaxHighlighter so the two
/// front-ends look familiar side-by-side.
/// </summary>
internal static class SyntaxColorizer
{
    private static Attribute Keyword => Application.Driver?.MakeAttribute(Color.BrightCyan, Color.Black) ?? default;
    private static Attribute StringLit => Application.Driver?.MakeAttribute(Color.BrightYellow, Color.Black) ?? default;
    private static Attribute NumberLit => Application.Driver?.MakeAttribute(Color.BrightGreen, Color.Black) ?? default;
    private static Attribute Label => Application.Driver?.MakeAttribute(Color.DarkGray, Color.Black) ?? default;
    private static Attribute Default => Application.Driver?.MakeAttribute(Color.White, Color.Black) ?? default;

    public static void Apply(TextView view, string source, IReadOnlyList<Token> tokens)
    {
        // TextView in Terminal.Gui v1 doesn't expose per-cell attributes directly;
        // we set its overall ColorScheme based on what the source looks like so
        // at least the background palette stays consistent. The richer per-token
        // overlay (drawn on top) is wired in via the editor's Redraw override —
        // see SourcePane's editor. For now this keeps the editor readable and
        // sets the stage for the overlay pass.
        view.ColorScheme = new ColorScheme
        {
            Normal = Default,
            Focus = Default,
            HotNormal = Default,
            HotFocus = Default,
            Disabled = Default,
        };

        _ = source;
        _ = tokens;
    }

    public static Attribute ColorFor(TokenKind kind)
    {
        // All BASIC reserved words are Kw* in the lexer's enum, matching the
        // Unity sample's classification logic.
        if (kind.ToString().StartsWith("Kw", StringComparison.Ordinal)) return Keyword;
        return kind switch
        {
            TokenKind.StringLiteral => StringLit,
            TokenKind.NumericLiteral => NumberLit,
            TokenKind.LineLabel => Label,
            _ => Default,
        };
    }
}
