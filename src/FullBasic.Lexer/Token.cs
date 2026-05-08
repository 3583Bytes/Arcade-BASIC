using FullBasic.Core;

namespace FullBasic.Lexer;

/// <summary>
/// A single lexed token. Carries its kind, the source span it occupies, and the
/// raw source text. Trivia (leading/trailing whitespace and comments) is not
/// attached here yet — the lexer skips it. We can revisit if a formatter is needed.
/// </summary>
public readonly record struct Token(TokenKind Kind, SourceSpan Span, string Text)
{
    public override string ToString() =>
        Kind switch
        {
            TokenKind.EndOfFile => "<eof>",
            TokenKind.Newline => "<eol>",
            _ => $"{Kind}({Text})",
        };
}
