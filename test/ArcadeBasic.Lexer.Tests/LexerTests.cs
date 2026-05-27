using FluentAssertions;
using FullBasic.Core;
using FullBasic.Lexer;

namespace FullBasic.Lexer.Tests;

public class LexerTests
{
    private static (List<Token> Tokens, DiagnosticBag Diagnostics) Lex(string source)
    {
        var file = new SourceFile("test.bas", source);
        var diags = new DiagnosticBag();
        var lexer = new BasicLexer(file, diags);
        return (lexer.Lex(), diags);
    }

    private static List<TokenKind> Kinds(string source) =>
        Lex(source).Tokens.ConvertAll(t => t.Kind);

    [Fact]
    public void EmptyFileEmitsOnlyEof()
    {
        var (tokens, diags) = Lex("");

        tokens.Should().HaveCount(1);
        tokens[0].Kind.Should().Be(TokenKind.EndOfFile);
        diags.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void WhitespaceOnlyFileEmitsOnlyEof()
    {
        var (tokens, diags) = Lex("   \t  ");

        tokens.Should().HaveCount(1);
        tokens[0].Kind.Should().Be(TokenKind.EndOfFile);
        diags.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void NewlinesProduceNewlineTokens()
    {
        var kinds = Kinds("\n\r\n\r");
        kinds.Should().Equal(
            TokenKind.Newline,
            TokenKind.Newline,
            TokenKind.Newline,
            TokenKind.EndOfFile);
    }

    // --- Numeric literals -----------------------------------------------

    [Theory]
    [InlineData("42")]
    [InlineData("0")]
    [InlineData("3.14")]
    [InlineData(".5")]
    [InlineData("5.")]
    [InlineData("1E5")]
    [InlineData("1.5E-3")]
    [InlineData("2.0e+10")]
    [InlineData("3.14E5")]
    public void NumericLiteralsLexAsSingleToken(string literal)
    {
        // Wrap in an assignment so the literal is mid-line — at file start, a
        // bare integer would lex as a LineLabel per spec.
        var (tokens, diags) = Lex($"X = {literal}");
        tokens[2].Kind.Should().Be(TokenKind.NumericLiteral);
        tokens[2].Text.Should().Be(literal);
        diags.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void MalformedExponentEmitsDiagnostic()
    {
        var (tokens, diags) = Lex("X = 1E");
        tokens[2].Kind.Should().Be(TokenKind.NumericLiteral);
        diags.HasErrors.Should().BeTrue();
        diags.All[0].Code.Should().Be(BasicLexer.ErrMalformedNumber);
    }

    // --- String literals ------------------------------------------------

    [Fact]
    public void EmptyString()
    {
        var (tokens, _) = Lex("\"\"");
        tokens[0].Kind.Should().Be(TokenKind.StringLiteral);
        tokens[0].Text.Should().Be("\"\"");
    }

    [Fact]
    public void StringWithSpaces()
    {
        var (tokens, _) = Lex("\"hello world\"");
        tokens[0].Kind.Should().Be(TokenKind.StringLiteral);
        tokens[0].Text.Should().Be("\"hello world\"");
    }

    [Fact]
    public void DoubledQuoteIsLiteralQuote()
    {
        var (tokens, diags) = Lex("\"say \"\"hi\"\"\"");
        diags.HasErrors.Should().BeFalse();
        tokens[0].Kind.Should().Be(TokenKind.StringLiteral);
        tokens[0].Text.Should().Be("\"say \"\"hi\"\"\"");
    }

    [Fact]
    public void UnterminatedStringAtEol()
    {
        var (tokens, diags) = Lex("\"oops\nfoo");
        tokens[0].Kind.Should().Be(TokenKind.StringLiteral);
        diags.HasErrors.Should().BeTrue();
        diags.All[0].Code.Should().Be(BasicLexer.ErrUnterminatedString);
    }

    [Fact]
    public void UnterminatedStringAtEof()
    {
        var (tokens, diags) = Lex("\"oops");
        tokens[0].Kind.Should().Be(TokenKind.StringLiteral);
        diags.HasErrors.Should().BeTrue();
    }

    // --- Identifiers ----------------------------------------------------

    [Theory]
    [InlineData("X")]
    [InlineData("X1")]
    [InlineData("FOO_BAR")]
    [InlineData("A1B2C3")]
    public void SimpleIdentifiers(string source)
    {
        var (tokens, _) = Lex(source);
        tokens[0].Kind.Should().Be(TokenKind.Identifier);
        tokens[0].Text.Should().Be(source);
    }

    [Theory]
    [InlineData("X$")]
    [InlineData("NAME$")]
    [InlineData("A1$")]
    public void StringIdentifiers(string source)
    {
        var (tokens, _) = Lex(source);
        tokens[0].Kind.Should().Be(TokenKind.StringIdentifier);
        tokens[0].Text.Should().Be(source);
    }

    [Fact]
    public void StringIdentifierIsNeverAKeyword()
    {
        // STRING is a keyword; STRING$ is a string identifier.
        var (tokens, _) = Lex("STRING$");
        tokens[0].Kind.Should().Be(TokenKind.StringIdentifier);
    }

    // --- Keywords -------------------------------------------------------

    [Theory]
    [InlineData("LET", TokenKind.KwLet)]
    [InlineData("let", TokenKind.KwLet)]
    [InlineData("Let", TokenKind.KwLet)]
    [InlineData("LeT", TokenKind.KwLet)]
    [InlineData("PRINT", TokenKind.KwPrint)]
    [InlineData("IF", TokenKind.KwIf)]
    [InlineData("THEN", TokenKind.KwThen)]
    [InlineData("ELSE", TokenKind.KwElse)]
    [InlineData("ELSEIF", TokenKind.KwElseif)]
    [InlineData("END", TokenKind.KwEnd)]
    [InlineData("FOR", TokenKind.KwFor)]
    [InlineData("NEXT", TokenKind.KwNext)]
    [InlineData("MAT", TokenKind.KwMat)]
    [InlineData("MOD", TokenKind.KwMod)]
    [InlineData("AND", TokenKind.KwAnd)]
    public void KeywordsAreCaseInsensitive(string source, TokenKind expected)
    {
        var (tokens, _) = Lex(source);
        tokens[0].Kind.Should().Be(expected);
    }

    [Fact]
    public void RemConsumesToEndOfLine()
    {
        var (tokens, _) = Lex("REM this is a comment\nLET X = 1");
        tokens.Select(t => t.Kind).Should().StartWith([
            TokenKind.KwRem,
            TokenKind.Newline,
            TokenKind.KwLet,
        ]);
        tokens[0].Text.Should().Be("REM this is a comment");
    }

    [Fact]
    public void ExclamationCommentIsSkipped()
    {
        var (tokens, _) = Lex("LET X = 1 ! trailing comment\nPRINT X");
        var kinds = tokens.ConvertAll(t => t.Kind);
        kinds.Should().Equal(
            TokenKind.KwLet,
            TokenKind.Identifier,
            TokenKind.Equal,
            TokenKind.NumericLiteral,
            TokenKind.Newline,
            TokenKind.KwPrint,
            TokenKind.Identifier,
            TokenKind.EndOfFile);
    }

    // --- Operators & punctuation ----------------------------------------

    [Fact]
    public void RelationalOperators()
    {
        var (tokens, _) = Lex("= <> < <= > >=");
        tokens.Select(t => t.Kind).Should().StartWith([
            TokenKind.Equal,
            TokenKind.NotEqual,
            TokenKind.Less,
            TokenKind.LessEqual,
            TokenKind.Greater,
            TokenKind.GreaterEqual,
        ]);
    }

    [Fact]
    public void ArithmeticOperators()
    {
        var (tokens, _) = Lex("+ - * / ^ &");
        tokens.Select(t => t.Kind).Should().StartWith([
            TokenKind.Plus,
            TokenKind.Minus,
            TokenKind.Asterisk,
            TokenKind.Slash,
            TokenKind.Caret,
            TokenKind.Ampersand,
        ]);
    }

    [Fact]
    public void Punctuation()
    {
        var (tokens, _) = Lex("( ) [ ] , ; : ? #");
        tokens.Select(t => t.Kind).Should().StartWith([
            TokenKind.LParen,
            TokenKind.RParen,
            TokenKind.LBracket,
            TokenKind.RBracket,
            TokenKind.Comma,
            TokenKind.Semicolon,
            TokenKind.Colon,
            TokenKind.Question,
            TokenKind.Hash,
        ]);
    }

    // --- Line labels ----------------------------------------------------

    [Fact]
    public void IntegerAtStartOfLineIsLabel()
    {
        var (tokens, _) = Lex("100 LET X = 1");
        tokens.Select(t => t.Kind).Should().StartWith([
            TokenKind.LineLabel,
            TokenKind.KwLet,
            TokenKind.Identifier,
            TokenKind.Equal,
            TokenKind.NumericLiteral,
        ]);
        tokens[0].Text.Should().Be("100");
    }

    [Fact]
    public void IntegerNotAtStartOfLineIsNumericLiteral()
    {
        var (tokens, _) = Lex("LET X = 100");
        tokens[3].Kind.Should().Be(TokenKind.NumericLiteral);
        tokens[3].Text.Should().Be("100");
    }

    [Fact]
    public void FloatAtStartOfLineIsNumericLiteralNotLabel()
    {
        // 1.5 starts with a digit but isn't a valid label (the dot disqualifies it).
        var (tokens, _) = Lex("1.5");
        tokens[0].Kind.Should().Be(TokenKind.NumericLiteral);
    }

    [Fact]
    public void LineLabelFollowedByColonIsAcceptable()
    {
        var (tokens, _) = Lex("100: LET X = 1");
        tokens[0].Kind.Should().Be(TokenKind.LineLabel);
        tokens[1].Kind.Should().Be(TokenKind.Colon);
    }

    // --- Putting it together --------------------------------------------

    [Fact]
    public void SmallProgramLexesCleanly()
    {
        const string program = """
        100 REM hello world
        110 LET X = 42
        120 PRINT "answer is"; X
        130 END
        """;

        var (tokens, diags) = Lex(program);

        diags.HasErrors.Should().BeFalse();
        tokens.Select(t => t.Kind).Should().Equal(
            TokenKind.LineLabel, TokenKind.KwRem, TokenKind.Newline,
            TokenKind.LineLabel, TokenKind.KwLet, TokenKind.Identifier, TokenKind.Equal, TokenKind.NumericLiteral, TokenKind.Newline,
            TokenKind.LineLabel, TokenKind.KwPrint, TokenKind.StringLiteral, TokenKind.Semicolon, TokenKind.Identifier, TokenKind.Newline,
            TokenKind.LineLabel, TokenKind.KwEnd,
            TokenKind.EndOfFile);
    }

    [Fact]
    public void TokenSpansMatchSource()
    {
        var (tokens, _) = Lex("LET X");
        tokens[0].Span.Start.Should().Be(0);
        tokens[0].Span.Length.Should().Be(3);
        tokens[1].Span.Start.Should().Be(4);
        tokens[1].Span.Length.Should().Be(1);
    }

    [Fact]
    public void UnknownCharProducesUnknownTokenAndDiagnostic()
    {
        var (tokens, diags) = Lex("@");
        tokens[0].Kind.Should().Be(TokenKind.Unknown);
        diags.HasErrors.Should().BeTrue();
        diags.All[0].Code.Should().Be(BasicLexer.ErrUnexpectedChar);
    }
}
