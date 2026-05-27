using ArcadeBasic.Lexer;
using ArcadeBasic.Parser.Ast;

namespace ArcadeBasic.Parser;

/// <summary>
/// Phase-7 module parsing. MODULE name ... END MODULE; PUBLIC prefix on
/// SUB/FUNCTION/DEF; PRIVATE prefix accepted but no-op (the default).
/// </summary>
public sealed partial class BasicParser
{
    private Stmt? ParseModule()
    {
        var start = Advance(); // MODULE
        if (Peek().Kind != TokenKind.Identifier)
        {
            ErrorAt(Peek(), ErrExpectedToken, "expected module name");
            return null;
        }
        var nameTok = Advance();
        if (!Match(TokenKind.Newline))
        {
            ErrorAt(Peek(), ErrExpectedToken, "expected end of line after MODULE name");
            return null;
        }
        var body = ParseStatementBlock(stoppers: [TokenKind.KwEnd]);
        if (!ExpectKind(TokenKind.KwEnd, "'END MODULE'")) return null;
        if (!ExpectKind(TokenKind.KwModule, "'MODULE' (END MODULE)")) return null;
        return new ModuleStmt(SpanFrom(start, Previous().Span), nameTok.Text, body);
    }

    /// <summary>Consume PUBLIC and apply the visibility flag to the next decl.</summary>
    private Stmt? ParsePublicDecl()
    {
        Advance(); // PUBLIC
        var stmt = ParseStatement();
        return stmt switch
        {
            SubStmt s => s with { IsPublic = true },
            FunctionStmt f => f with { IsPublic = true },
            DefStmt d => d with { IsPublic = true },
            _ => stmt,
        };
    }

    /// <summary>PRIVATE prefix — explicit form of the default visibility. Accepted, no-op.</summary>
    private Stmt? ParsePrivateDecl()
    {
        Advance(); // PRIVATE
        return ParseStatement();
    }
}
