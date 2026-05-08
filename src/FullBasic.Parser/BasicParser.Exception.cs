using FullBasic.Core;
using FullBasic.Lexer;
using FullBasic.Parser.Ast;

namespace FullBasic.Parser;

/// <summary>
/// Phase-6 exception-handling parser. Forms:
///   WHEN EXCEPTION IN ... USE [&lt;name&gt;] ... END WHEN
///   HANDLER name ... END HANDLER
///   CAUSE EXCEPTION &lt;expr&gt;
///   RETRY
///   CONTINUE
/// </summary>
public sealed partial class BasicParser
{
    private Stmt? ParseWhen()
    {
        var start = Advance(); // WHEN
        if (!ExpectKind(TokenKind.KwException, "'EXCEPTION'")) return null;
        if (!ExpectKind(TokenKind.KwIn, "'IN'")) return null;
        if (!Match(TokenKind.Newline))
        {
            ErrorAt(Peek(), ErrExpectedToken, "expected end of line after WHEN EXCEPTION IN");
            return null;
        }
        var inBody = ParseStatementBlock(stoppers: [TokenKind.KwUse]);

        if (!ExpectKind(TokenKind.KwUse, "'USE'")) return null;

        // After USE, two forms:
        //   USE <handler-name> <newline>      — refers to a named HANDLER
        //   USE <newline> <stmts> END WHEN    — inline handler body
        if (Check(TokenKind.Identifier) && PeekKind(1) == TokenKind.Newline)
        {
            var nameTok = Advance();
            Advance(); // newline
            // Skip any blank lines, then expect END WHEN
            SkipNewlines();
            if (!ExpectKind(TokenKind.KwEnd, "'END WHEN'")) return null;
            if (!ExpectKind(TokenKind.KwWhen, "'WHEN' (END WHEN)")) return null;
            return new WhenStmt(SpanFrom(start, Previous().Span), inBody, null, nameTok.Text);
        }

        if (!Match(TokenKind.Newline))
        {
            ErrorAt(Peek(), ErrExpectedToken, "expected end of line or handler name after USE");
            return null;
        }
        var useBody = ParseStatementBlock(stoppers: [TokenKind.KwEnd]);
        if (!ExpectKind(TokenKind.KwEnd, "'END WHEN'")) return null;
        if (!ExpectKind(TokenKind.KwWhen, "'WHEN' (END WHEN)")) return null;
        return new WhenStmt(SpanFrom(start, Previous().Span), inBody, useBody, null);
    }

    private Stmt? ParseHandler()
    {
        var start = Advance(); // HANDLER
        if (Peek().Kind != TokenKind.Identifier)
        {
            ErrorAt(Peek(), ErrExpectedToken, "expected handler name after HANDLER");
            return null;
        }
        var nameTok = Advance();
        if (!Match(TokenKind.Newline))
        {
            ErrorAt(Peek(), ErrExpectedToken, "expected end of line after HANDLER name");
            return null;
        }
        var body = ParseStatementBlock(stoppers: [TokenKind.KwEnd]);
        if (!ExpectKind(TokenKind.KwEnd, "'END HANDLER'")) return null;
        if (!ExpectKind(TokenKind.KwHandler, "'HANDLER' (END HANDLER)")) return null;
        return new HandlerStmt(SpanFrom(start, Previous().Span), nameTok.Text, body);
    }

    private Stmt? ParseCause()
    {
        var start = Advance(); // CAUSE
        if (!ExpectKind(TokenKind.KwException, "'EXCEPTION'")) return null;
        var typeExpr = ParseExpression();
        if (typeExpr is null) return null;
        return new CauseStmt(SpanFrom(start, typeExpr.Span), typeExpr);
    }

    private Stmt ParseRetry()
    {
        var t = Advance(); // RETRY
        return new RetryStmt(t.Span);
    }

    private Stmt ParseContinueResume()
    {
        var t = Advance(); // CONTINUE
        return new ContinueResumeStmt(t.Span);
    }
}
