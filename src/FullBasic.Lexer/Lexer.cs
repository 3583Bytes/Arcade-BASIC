using FullBasic.Core;

namespace FullBasic.Lexer;

/// <summary>
/// Lexer for ISO/IEC 10279 Full BASIC. Produces a flat token stream including
/// explicit Newline tokens (so the parser can use line breaks as statement
/// terminators) and a final EndOfFile token.
/// </summary>
public sealed class BasicLexer
{
    // Diagnostic codes (FB01xx range = lexer)
    public const string ErrUnterminatedString = "FB0101";
    public const string ErrMalformedNumber = "FB0102";
    public const string ErrUnexpectedChar = "FB0103";

    private readonly SourceFile _file;
    private readonly string _text;
    private readonly DiagnosticBag _diags;

    private int _pos;
    private bool _atLineStart = true;

    public BasicLexer(SourceFile file, DiagnosticBag diagnostics)
    {
        _file = file;
        _text = file.Text;
        _diags = diagnostics;
    }

    /// <summary>Lex the full source file. Always emits EndOfFile as the last token.</summary>
    public List<Token> Lex()
    {
        var tokens = new List<Token>();
        while (true)
        {
            var tok = NextToken();
            tokens.Add(tok);
            if (tok.Kind == TokenKind.EndOfFile)
            {
                return tokens;
            }
        }
    }

    private Token NextToken()
    {
        SkipTrivia();

        if (_pos >= _text.Length)
        {
            return MakeToken(TokenKind.EndOfFile, _pos, 0);
        }

        var start = _pos;
        var c = _text[_pos];

        if (c == '\n' || c == '\r')
        {
            ConsumeNewline();
            _atLineStart = true;
            return MakeToken(TokenKind.Newline, start, _pos - start);
        }

        if (_atLineStart && IsDigit(c) && TryLexLineLabel(start, out var label))
        {
            _atLineStart = false;
            return label;
        }

        _atLineStart = false;

        if (IsDigit(c) || (c == '.' && _pos + 1 < _text.Length && IsDigit(_text[_pos + 1])))
        {
            return LexNumber(start);
        }

        if (c == '"')
        {
            return LexString(start);
        }

        if (IsIdentStart(c))
        {
            return LexIdentifier(start);
        }

        return LexSymbol(start);
    }

    private void SkipTrivia()
    {
        while (_pos < _text.Length)
        {
            var c = _text[_pos];

            // Spaces and tabs.
            if (c == ' ' || c == '\t')
            {
                _pos++;
                continue;
            }

            // BOM at the start of file.
            if (c == '﻿' && _pos == 0)
            {
                _pos++;
                continue;
            }

            // `!` introduces an end-of-line comment (Full BASIC shorthand).
            if (c == '!')
            {
                ConsumeToEol();
                continue;
            }

            break;
        }
    }

    private void ConsumeNewline()
    {
        if (_text[_pos] == '\r')
        {
            _pos++;
            if (_pos < _text.Length && _text[_pos] == '\n')
            {
                _pos++;
            }
        }
        else
        {
            _pos++;
        }
    }

    private void ConsumeToEol()
    {
        while (_pos < _text.Length && _text[_pos] != '\n' && _text[_pos] != '\r')
        {
            _pos++;
        }
    }

    private bool TryLexLineLabel(int start, out Token token)
    {
        var save = _pos;
        while (_pos < _text.Length && IsDigit(_text[_pos]))
        {
            _pos++;
        }

        // A line label is a digit run at the start of a logical line followed by
        // whitespace, end-of-line, end-of-file, or `:` (statement separator).
        if (_pos < _text.Length)
        {
            var nx = _text[_pos];
            if (!(nx == ' ' || nx == '\t' || nx == '\n' || nx == '\r' || nx == ':'))
            {
                _pos = save;
                token = default;
                return false;
            }
        }

        token = MakeToken(TokenKind.LineLabel, start, _pos - start);
        return true;
    }

    private Token LexNumber(int start)
    {
        // Mantissa: digits, optional '.', optional more digits.
        // Or starts with '.' followed by digits (handled by caller's check).
        var sawDot = false;
        var sawDigit = false;

        while (_pos < _text.Length)
        {
            var c = _text[_pos];
            if (IsDigit(c))
            {
                sawDigit = true;
                _pos++;
            }
            else if (c == '.' && !sawDot)
            {
                sawDot = true;
                _pos++;
            }
            else
            {
                break;
            }
        }

        // Optional exponent: E [+|-] digits
        if (_pos < _text.Length && (_text[_pos] == 'E' || _text[_pos] == 'e'))
        {
            var expStart = _pos;
            _pos++;
            if (_pos < _text.Length && (_text[_pos] == '+' || _text[_pos] == '-'))
            {
                _pos++;
            }

            var expDigitStart = _pos;
            while (_pos < _text.Length && IsDigit(_text[_pos]))
            {
                _pos++;
            }

            if (_pos == expDigitStart)
            {
                // 'E' without digits — malformed exponent.
                _diags.Error(
                    ErrMalformedNumber,
                    new SourceSpan(_file, expStart, _pos - expStart),
                    "exponent indicator 'E' must be followed by at least one digit");
                // Recover: we still emit the token covering everything we consumed.
            }
        }

        if (!sawDigit)
        {
            _diags.Error(
                ErrMalformedNumber,
                new SourceSpan(_file, start, _pos - start),
                "numeric constant has no digits");
        }

        return MakeToken(TokenKind.NumericLiteral, start, _pos - start);
    }

    private Token LexString(int start)
    {
        _pos++; // consume opening "
        var contentStart = _pos;

        while (_pos < _text.Length)
        {
            var c = _text[_pos];

            if (c == '\n' || c == '\r')
            {
                _diags.Error(
                    ErrUnterminatedString,
                    new SourceSpan(_file, start, _pos - start),
                    "string literal not terminated before end of line",
                    "string literals must be closed with a matching \" on the same line");
                return MakeToken(TokenKind.StringLiteral, start, _pos - start);
            }

            if (c == '"')
            {
                // Could be doubled-quote escape or end of string.
                if (_pos + 1 < _text.Length && _text[_pos + 1] == '"')
                {
                    _pos += 2;
                    continue;
                }

                _pos++; // consume closing "
                return MakeToken(TokenKind.StringLiteral, start, _pos - start);
            }

            _pos++;
        }

        // Reached EOF without close.
        _ = contentStart;
        _diags.Error(
            ErrUnterminatedString,
            new SourceSpan(_file, start, _pos - start),
            "string literal not terminated before end of file");
        return MakeToken(TokenKind.StringLiteral, start, _pos - start);
    }

    private Token LexIdentifier(int start)
    {
        while (_pos < _text.Length && IsIdentContinue(_text[_pos]))
        {
            _pos++;
        }

        var isStringIdent = false;
        if (_pos < _text.Length && _text[_pos] == '$')
        {
            isStringIdent = true;
            _pos++;
        }

        var span = _file.Text.AsSpan(start, _pos - start);

        if (isStringIdent)
        {
            // String identifiers (FOO$) are never keywords.
            return MakeToken(TokenKind.StringIdentifier, start, _pos - start);
        }

        if (Keywords.TryLookup(span, out var kw))
        {
            // REM swallows the rest of the line into the token's span; the parser
            // then sees `KwRem Newline` with the comment text inside the token.
            if (kw == TokenKind.KwRem)
            {
                ConsumeToEol();
            }
            return MakeToken(kw, start, _pos - start);
        }

        return MakeToken(TokenKind.Identifier, start, _pos - start);
    }

    private Token LexSymbol(int start)
    {
        var c = _text[_pos];
        switch (c)
        {
            case '+': _pos++; return MakeToken(TokenKind.Plus, start, 1);
            case '-': _pos++; return MakeToken(TokenKind.Minus, start, 1);
            case '*': _pos++; return MakeToken(TokenKind.Asterisk, start, 1);
            case '/': _pos++; return MakeToken(TokenKind.Slash, start, 1);
            case '^': _pos++; return MakeToken(TokenKind.Caret, start, 1);
            case '&': _pos++; return MakeToken(TokenKind.Ampersand, start, 1);
            case '=': _pos++; return MakeToken(TokenKind.Equal, start, 1);
            case '(': _pos++; return MakeToken(TokenKind.LParen, start, 1);
            case ')': _pos++; return MakeToken(TokenKind.RParen, start, 1);
            case '[': _pos++; return MakeToken(TokenKind.LBracket, start, 1);
            case ']': _pos++; return MakeToken(TokenKind.RBracket, start, 1);
            case ',': _pos++; return MakeToken(TokenKind.Comma, start, 1);
            case ';': _pos++; return MakeToken(TokenKind.Semicolon, start, 1);
            case ':': _pos++; return MakeToken(TokenKind.Colon, start, 1);
            case '?': _pos++; return MakeToken(TokenKind.Question, start, 1);
            case '#': _pos++; return MakeToken(TokenKind.Hash, start, 1);

            case '<':
                _pos++;
                if (_pos < _text.Length && _text[_pos] == '=') { _pos++; return MakeToken(TokenKind.LessEqual, start, 2); }
                if (_pos < _text.Length && _text[_pos] == '>') { _pos++; return MakeToken(TokenKind.NotEqual, start, 2); }
                return MakeToken(TokenKind.Less, start, 1);

            case '>':
                _pos++;
                if (_pos < _text.Length && _text[_pos] == '=') { _pos++; return MakeToken(TokenKind.GreaterEqual, start, 2); }
                return MakeToken(TokenKind.Greater, start, 1);

            default:
                _pos++;
                _diags.Error(
                    ErrUnexpectedChar,
                    new SourceSpan(_file, start, 1),
                    $"unexpected character '{c}' (U+{(int)c:X4})");
                return MakeToken(TokenKind.Unknown, start, 1);
        }
    }

    private Token MakeToken(TokenKind kind, int start, int length)
    {
        var span = new SourceSpan(_file, start, length);
        var text = length == 0 ? string.Empty : _text.Substring(start, length);
        return new Token(kind, span, text);
    }

    private static bool IsDigit(char c) => c >= '0' && c <= '9';

    private static bool IsLetter(char c) =>
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

    private static bool IsIdentStart(char c) => IsLetter(c);

    private static bool IsIdentContinue(char c) =>
        IsLetter(c) || IsDigit(c) || c == '_';

    private static bool IsSpace(char c) => c == ' ' || c == '\t';
}
