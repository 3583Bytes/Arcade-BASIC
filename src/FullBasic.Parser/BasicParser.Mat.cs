using FullBasic.Core;
using FullBasic.Lexer;
using FullBasic.Parser.Ast;

namespace FullBasic.Parser;

/// <summary>
/// MAT-statement parsing. Five forms after the MAT keyword:
///   MAT REDIM name(bounds, ...)
///   MAT INPUT name
///   MAT PRINT name
///   MAT READ name
///   MAT name = rhs       (where rhs is a small dedicated grammar)
///
/// The rhs grammar accepts a name, IDN/ZER/CON/NUL$ constants, INV(name)
/// or TRN(name), a scalar*matrix product written as (expr)*name, and
/// element-wise + / - / * between matrix expressions.
/// </summary>
public sealed partial class BasicParser
{
    private Stmt? ParseMat()
    {
        var start = Advance(); // MAT

        if (Match(TokenKind.KwRedim)) return ParseMatRedim(start);
        if (Match(TokenKind.KwInput)) return ParseMatInput(start);
        if (Match(TokenKind.KwPrint)) return ParseMatPrint(start);
        if (Match(TokenKind.KwRead)) return ParseMatRead(start);

        // MAT name = rhs
        var nameTok = Peek();
        if (nameTok.Kind != TokenKind.Identifier && nameTok.Kind != TokenKind.StringIdentifier)
        {
            ErrorAt(nameTok, ErrExpectedToken, "expected array name after MAT");
            return null;
        }
        Advance();
        var isString = nameTok.Kind == TokenKind.StringIdentifier;
        var name = NameWithoutDollar(nameTok.Text);

        if (!ExpectKind(TokenKind.Equal, "'=' after MAT target")) return null;

        var rhs = ParseMatRhs();
        if (rhs is null) return null;

        return new MatAssignStmt(SpanFrom(start, Previous().Span), name, isString, rhs);
    }

    private Stmt? ParseMatRedim(Token start)
    {
        var nameTok = Peek();
        if (nameTok.Kind != TokenKind.Identifier && nameTok.Kind != TokenKind.StringIdentifier)
        {
            ErrorAt(nameTok, ErrExpectedToken, "expected array name in MAT REDIM");
            return null;
        }
        Advance();
        var isString = nameTok.Kind == TokenKind.StringIdentifier;
        var name = NameWithoutDollar(nameTok.Text);

        if (!ExpectKind(TokenKind.LParen, "'(' (MAT REDIM requires bounds)")) return null;
        var bounds = new List<DimBound>();
        do
        {
            var lower = ParseExpression();
            if (lower is null) return null;
            Expr? upper;
            if (Match(TokenKind.KwTo))
            {
                upper = ParseExpression();
                if (upper is null) return null;
                bounds.Add(new DimBound(SpanBetween(lower.Span, upper.Span), lower, upper));
            }
            else
            {
                upper = lower;
                bounds.Add(new DimBound(lower.Span, null, upper));
            }
        }
        while (Match(TokenKind.Comma));
        if (!ExpectKind(TokenKind.RParen, "')'")) return null;
        return new MatRedimStmt(SpanFrom(start, Previous().Span), name, isString, bounds);
    }

    private Stmt? ParseMatInput(Token start)
    {
        var (name, isString) = ParseMatTargetName();
        if (name is null) return null;
        return new MatInputStmt(SpanFrom(start, Previous().Span), name, isString);
    }

    private Stmt? ParseMatPrint(Token start)
    {
        var (name, isString) = ParseMatTargetName();
        if (name is null) return null;
        return new MatPrintStmt(SpanFrom(start, Previous().Span), name, isString);
    }

    private Stmt? ParseMatRead(Token start)
    {
        var (name, isString) = ParseMatTargetName();
        if (name is null) return null;
        return new MatReadStmt(SpanFrom(start, Previous().Span), name, isString);
    }

    private (string? Name, bool IsString) ParseMatTargetName()
    {
        var tok = Peek();
        if (tok.Kind != TokenKind.Identifier && tok.Kind != TokenKind.StringIdentifier)
        {
            ErrorAt(tok, ErrExpectedToken, "expected array name");
            return (null, false);
        }
        Advance();
        return (NameWithoutDollar(tok.Text), tok.Kind == TokenKind.StringIdentifier);
    }

    // -- MAT RHS grammar -------------------------------------------------

    /// <summary>Top of MAT-rhs precedence: + and - are left-associative.</summary>
    private MatRhs? ParseMatRhs() => ParseMatAddSub();

    private MatRhs? ParseMatAddSub()
    {
        var left = ParseMatMul();
        if (left is null) return null;
        while (true)
        {
            MatBinaryKind? op = Peek().Kind switch
            {
                TokenKind.Plus => MatBinaryKind.Add,
                TokenKind.Minus => MatBinaryKind.Subtract,
                _ => null,
            };
            if (op is null) break;
            Advance();
            var right = ParseMatMul();
            if (right is null) return null;
            left = new MatRhsBinary(SpanBetween(left.Span, right.Span), op.Value, left, right);
        }
        return left;
    }

    private MatRhs? ParseMatMul()
    {
        var left = ParseMatPrimary();
        if (left is null) return null;
        while (Match(TokenKind.Asterisk))
        {
            var right = ParseMatPrimary();
            if (right is null) return null;
            left = new MatRhsBinary(SpanBetween(left.Span, right.Span), MatBinaryKind.Multiply, left, right);
        }
        return left;
    }

    private MatRhs? ParseMatPrimary()
    {
        var tok = Peek();

        // Scalar * matrix: (expr) * name. The scalar is a parenthesized scalar expression.
        if (tok.Kind == TokenKind.LParen)
        {
            // Look ahead far enough to disambiguate "(scalar)*name" from a
            // parenthesized matrix expression. We commit to scalar*matrix only
            // when we see `)` followed by `*`.
            var savedPos = _pos;
            Advance(); // (
            var scalar = ParseExpression();
            if (scalar is null) return null;
            if (!Match(TokenKind.RParen))
            {
                ErrorAt(Peek(), ErrExpectedToken, "expected ')'");
                _pos = savedPos;
                return null;
            }
            if (Match(TokenKind.Asterisk))
            {
                var matrix = ParseMatPrimary();
                if (matrix is null) return null;
                return new MatRhsScalarMul(SpanBetween(tok.Span, matrix.Span), scalar, matrix);
            }
            // Otherwise: a parenthesized expression is not a valid MAT rhs.
            ErrorAt(tok, ErrUnsupportedSyntax,
                "parenthesized scalar in MAT rhs must be followed by '*' and a matrix name");
            return null;
        }

        if (Match(TokenKind.KwInv))
        {
            if (!ExpectKind(TokenKind.LParen, "'(' after INV")) return null;
            var inner = ParseMatPrimary();
            if (inner is null) return null;
            if (!ExpectKind(TokenKind.RParen, "')'")) return null;
            return new MatRhsInv(SpanFrom(tok, Previous().Span), inner);
        }

        if (Match(TokenKind.KwTrn))
        {
            if (!ExpectKind(TokenKind.LParen, "'(' after TRN")) return null;
            var inner = ParseMatPrimary();
            if (inner is null) return null;
            if (!ExpectKind(TokenKind.RParen, "')'")) return null;
            return new MatRhsTrn(SpanFrom(tok, Previous().Span), inner);
        }

        if (Match(TokenKind.KwIdn)) return new MatRhsConst(tok.Span, MatConstKind.Identity);
        if (Match(TokenKind.KwZer)) return new MatRhsConst(tok.Span, MatConstKind.Zeros);
        if (Match(TokenKind.KwCon)) return new MatRhsConst(tok.Span, MatConstKind.Ones);

        // NUL$ comes through the lexer as a StringIdentifier with text "NUL$".
        if (tok.Kind == TokenKind.StringIdentifier
            && string.Equals(tok.Text, "NUL$", StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            return new MatRhsConst(tok.Span, MatConstKind.NullString);
        }

        if (tok.Kind == TokenKind.Identifier || tok.Kind == TokenKind.StringIdentifier)
        {
            Advance();
            var isString = tok.Kind == TokenKind.StringIdentifier;
            var name = NameWithoutDollar(tok.Text);
            return new MatRhsName(tok.Span, name, isString);
        }

        ErrorAt(tok, ErrExpectedExpression, "expected MAT rhs (name, IDN/ZER/CON/NUL$, INV(...), TRN(...), or scalar * name)");
        return null;
    }
}
