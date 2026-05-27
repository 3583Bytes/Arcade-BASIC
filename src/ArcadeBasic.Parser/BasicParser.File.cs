using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser.Ast;

namespace ArcadeBasic.Parser;

/// <summary>
/// File-I/O statement parsing — Phase-5 scope:
///   OPEN #ch: NAME f$, ACCESS access, ORGANIZATION org, CREATE create
///   CLOSE #ch
///   PRINT #ch: items
///   INPUT #ch: targets
///   LINE INPUT #ch: var$
///
/// Deferred to follow-ups: ERASE, RESET, WRITE/READ #ch (INTERNAL mode),
/// RECTYPE, RECSIZE, KEY, MARGIN, ZONEWIDTH, RANDOM organization, INTERNAL
/// and BYTE modes.
/// </summary>
public sealed partial class BasicParser
{
    private OpenStmt? ParseOpenStmt()
    {
        var start = Advance(); // OPEN
        if (!ExpectKind(TokenKind.Hash, "'#' (channel marker)")) return null;
        var channel = ParseExpression();
        if (channel is null) return null;
        if (!ExpectKind(TokenKind.Colon, "':' after channel")) return null;

        Expr? name = null;
        var access = OpenAccess.Default;
        var organization = OpenOrganization.Default;
        var create = OpenCreate.Default;

        // Comma-separated clauses, NAME first by convention but order is flexible.
        do
        {
            var tok = Peek();
            if (Match(TokenKind.KwName))
            {
                name = ParseExpression();
                if (name is null) return null;
            }
            else if (Match(TokenKind.KwAccess))
            {
                access = ParseOpenAccess();
            }
            else if (Match(TokenKind.KwOrganization))
            {
                organization = ParseOpenOrganization();
            }
            else if (Match(TokenKind.KwCreate))
            {
                create = ParseOpenCreate();
            }
            else
            {
                ErrorAt(tok, ErrUnsupportedSyntax,
                    "expected NAME, ACCESS, ORGANIZATION, or CREATE clause in OPEN");
                return null;
            }
        }
        while (Match(TokenKind.Comma));

        if (name is null)
        {
            ErrorAt(start, ErrExpectedToken, "OPEN requires a NAME clause");
            return null;
        }

        return new OpenStmt(SpanFrom(start, Previous().Span), channel, name, access, organization, create);
    }

    private OpenAccess ParseOpenAccess()
    {
        var tok = Peek();
        if (Match(TokenKind.KwInput)) return OpenAccess.Input;
        if (Match(TokenKind.KwOutput)) return OpenAccess.Output;
        if (Match(TokenKind.KwOutin)) return OpenAccess.Outin;
        ErrorAt(tok, ErrExpectedToken, "expected INPUT, OUTPUT, or OUTIN");
        return OpenAccess.Default;
    }

    private OpenOrganization ParseOpenOrganization()
    {
        var tok = Peek();
        if (Match(TokenKind.KwSequential)) return OpenOrganization.Sequential;
        if (Match(TokenKind.KwStream)) return OpenOrganization.Stream;
        if (tok.Kind == TokenKind.Identifier
            && string.Equals(tok.Text, "RANDOM", StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            return OpenOrganization.Random;
        }
        ErrorAt(tok, ErrExpectedToken, "expected SEQUENTIAL, STREAM, or RANDOM");
        return OpenOrganization.Default;
    }

    private OpenCreate ParseOpenCreate()
    {
        var tok = Peek();
        if (Match(TokenKind.KwNew)) return OpenCreate.New;
        if (tok.Kind == TokenKind.Identifier)
        {
            if (string.Equals(tok.Text, "OLD", StringComparison.OrdinalIgnoreCase))
            {
                Advance();
                return OpenCreate.Old;
            }
            if (string.Equals(tok.Text, "NEWOLD", StringComparison.OrdinalIgnoreCase))
            {
                Advance();
                return OpenCreate.NewOld;
            }
        }
        ErrorAt(tok, ErrExpectedToken, "expected NEW, OLD, or NEWOLD");
        return OpenCreate.Default;
    }

    private CloseStmt? ParseCloseStmt()
    {
        var start = Advance(); // CLOSE
        if (!ExpectKind(TokenKind.Hash, "'#' (channel marker)")) return null;
        var channel = ParseExpression();
        if (channel is null) return null;
        return new CloseStmt(SpanFrom(start, channel.Span), channel);
    }

    /// <summary>Called from ParsePrint when it sees `#` after PRINT — turns into PRINT #ch: items.</summary>
    private PrintFileStmt? ParsePrintFile(Token start)
    {
        // We've already consumed PRINT; now see '#'.
        Advance(); // #
        var channel = ParseExpression();
        if (channel is null) return null;
        if (!ExpectKind(TokenKind.Colon, "':' after channel in PRINT #")) return null;

        var items = new List<PrintItem>();
        while (!AtStatementEnd())
        {
            if (Match(TokenKind.Comma))
            {
                items.Add(new PrintComma(Previous().Span));
                continue;
            }
            if (Match(TokenKind.Semicolon))
            {
                items.Add(new PrintSemicolon(Previous().Span));
                continue;
            }
            var expr = ParseExpression();
            if (expr is null) break;
            items.Add(new PrintExprItem(expr.Span, expr));
        }
        return new PrintFileStmt(SpanFrom(start, Previous().Span), channel, items);
    }

    /// <summary>Called from ParseInput when it sees `#` after INPUT — turns into INPUT #ch: targets.</summary>
    private InputFileStmt? ParseInputFile(Token start)
    {
        Advance(); // #
        var channel = ParseExpression();
        if (channel is null) return null;
        if (!ExpectKind(TokenKind.Colon, "':' after channel in INPUT #")) return null;

        var targets = new List<Expr>();
        do
        {
            var t = ParseAssignmentTarget();
            if (t is null) return null;
            targets.Add(t);
        }
        while (Match(TokenKind.Comma));
        return new InputFileStmt(SpanFrom(start, Previous().Span), channel, targets);
    }

    /// <summary>Called from ParseLineInputOrError when LINE INPUT is followed by '#'.</summary>
    private LineInputFileStmt? ParseLineInputFile(Token start)
    {
        Advance(); // #
        var channel = ParseExpression();
        if (channel is null) return null;
        if (!ExpectKind(TokenKind.Colon, "':' after channel in LINE INPUT #")) return null;
        var target = ParseAssignmentTarget();
        if (target is null) return null;
        return new LineInputFileStmt(SpanFrom(start, target.Span), channel, target);
    }
}
