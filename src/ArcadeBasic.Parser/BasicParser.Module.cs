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

    /// <summary>PUBLIC: a prefix making the next SUB/FUNCTION/DEF callable outside its
    /// module, or (followed by names) the module's public variables.</summary>
    private Stmt? ParsePublicDecl()
    {
        if (Peek(1).Kind is not (TokenKind.KwSub or TokenKind.KwFunction or TokenKind.KwDef))
            return ParseModuleVars(isPublic: true);
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

    /// <summary>SHARE / PUBLIC name, A(10), B$ …: a module's variables.</summary>
    private Stmt? ParseModuleVars(bool isPublic)
    {
        var start = Advance(); // SHARE or PUBLIC
        var items = new List<DimSpec>();
        do
        {
            var spec = ParseDimSpec(boundsRequired: false, isPublic ? "PUBLIC" : "SHARE");
            if (spec is null) return null;
            items.Add(spec);
        }
        while (Match(TokenKind.Comma));
        return new ModuleVarsStmt(SpanFrom(start, Previous().Span), isPublic, items);
    }

    /// <summary>DECLARE PUBLIC name, A(), B$ … (the only DECLARE form so far).</summary>
    private Stmt? ParseDeclare()
    {
        var start = Advance(); // DECLARE
        if (!Match(TokenKind.KwPublic))
        {
            ErrorAt(Peek(), ErrExpectedToken, "expected PUBLIC after DECLARE (DECLARE PUBLIC is the form supported)");
            return null;
        }
        var names = new List<PublicName>();
        do
        {
            var nameTok = Peek();
            if (nameTok.Kind != TokenKind.Identifier && nameTok.Kind != TokenKind.StringIdentifier)
            {
                ErrorAt(nameTok, ErrExpectedToken, "expected a variable name in DECLARE PUBLIC");
                return null;
            }
            Advance();
            var isArray = false;
            if (Match(TokenKind.LParen))
            {
                while (Match(TokenKind.Comma)) { }             // A(), B(,) — the commas say the rank; not checked
                if (!ExpectKind(TokenKind.RParen, "')'")) return null;
                isArray = true;
            }
            names.Add(new PublicName(SpanBetween(nameTok.Span, Previous().Span), NameWithoutDollar(nameTok.Text),
                nameTok.Kind == TokenKind.StringIdentifier, isArray));
        }
        while (Match(TokenKind.Comma));
        return new DeclarePublicStmt(SpanFrom(start, Previous().Span), names);
    }

    /// <summary>LIBRARY "file" [, "file" …].</summary>
    private Stmt? ParseLibrary()
    {
        var start = Advance(); // LIBRARY (a name, not a reserved word)
        var files = new List<string>();
        do
        {
            if (!Check(TokenKind.StringLiteral))
            {
                ErrorAt(Peek(), ErrExpectedToken, "expected a file name in quotes after LIBRARY");
                return null;
            }
            files.Add(UnescapeString(Advance().Text));
        }
        while (Match(TokenKind.Comma));
        return new LibraryStmt(SpanFrom(start, Previous().Span), files);
    }

    /// <summary>PRIVATE prefix — explicit form of the default visibility. Accepted, no-op.</summary>
    private Stmt? ParsePrivateDecl()
    {
        Advance(); // PRIVATE
        return ParseStatement();
    }
}
