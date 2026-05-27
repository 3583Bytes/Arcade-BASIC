using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser.Ast;

namespace ArcadeBasic.Parser;

/// <summary>
/// Expression-grammar half of the parser. Precedence climbing — one method per
/// precedence level, lowest-precedence at the top, recursing into the next.
///
/// Precedence (loosest to tightest), per ISO/IEC 10279:
///   EQV, IMP, OR/XOR/BOR/BXOR, AND/BAND, NOT, relational, &amp; (concat),
///   +/-, *,/,MOD,REMAINDER, unary +/-, ^, primary.
/// </summary>
public sealed partial class BasicParser
{
    /// <summary>Public entry point used by the statement parser.</summary>
    private Expr? ParseExpression() => ParseEqv();

    private Expr? ParseEqv()
    {
        var left = ParseImp();
        if (left is null) return null;
        while (Match(TokenKind.KwEqv))
        {
            var right = ParseImp();
            if (right is null) return null;
            left = new BinaryExpr(SpanBetween(left.Span, right.Span), BinaryOp.Eqv, left, right);
        }
        return left;
    }

    private Expr? ParseImp()
    {
        var left = ParseOr();
        if (left is null) return null;
        while (Match(TokenKind.KwImp))
        {
            var right = ParseOr();
            if (right is null) return null;
            left = new BinaryExpr(SpanBetween(left.Span, right.Span), BinaryOp.Imp, left, right);
        }
        return left;
    }

    private Expr? ParseOr()
    {
        var left = ParseAnd();
        if (left is null) return null;
        while (true)
        {
            BinaryOp? op = Peek().Kind switch
            {
                TokenKind.KwOr => BinaryOp.Or,
                TokenKind.KwXor => BinaryOp.Xor,
                TokenKind.KwBor => BinaryOp.Bor,
                TokenKind.KwBxor => BinaryOp.Bxor,
                _ => null,
            };
            if (op is null) break;
            Advance();
            var right = ParseAnd();
            if (right is null) return null;
            left = new BinaryExpr(SpanBetween(left.Span, right.Span), op.Value, left, right);
        }
        return left;
    }

    private Expr? ParseAnd()
    {
        var left = ParseNot();
        if (left is null) return null;
        while (true)
        {
            BinaryOp? op = Peek().Kind switch
            {
                TokenKind.KwAnd => BinaryOp.And,
                TokenKind.KwBand => BinaryOp.Band,
                _ => null,
            };
            if (op is null) break;
            Advance();
            var right = ParseNot();
            if (right is null) return null;
            left = new BinaryExpr(SpanBetween(left.Span, right.Span), op.Value, left, right);
        }
        return left;
    }

    private Expr? ParseNot()
    {
        if (Match(TokenKind.KwNot))
        {
            var start = Previous();
            var inner = ParseNot();
            if (inner is null) return null;
            return new UnaryExpr(SpanBetween(start.Span, inner.Span), UnaryOp.Not, inner);
        }
        if (Match(TokenKind.KwBnot))
        {
            var start = Previous();
            var inner = ParseNot();
            if (inner is null) return null;
            return new UnaryExpr(SpanBetween(start.Span, inner.Span), UnaryOp.BNot, inner);
        }
        return ParseRelational();
    }

    private Expr? ParseRelational()
    {
        var left = ParseConcat();
        if (left is null) return null;

        // Relational operators are left-associative; chained relationals
        // (a < b < c) just parse left-to-right and produce a value the parser
        // doesn't object to. Sema may flag this later.
        while (true)
        {
            BinaryOp? op = Peek().Kind switch
            {
                TokenKind.Equal => BinaryOp.Equal,
                TokenKind.NotEqual => BinaryOp.NotEqual,
                TokenKind.Less => BinaryOp.Less,
                TokenKind.LessEqual => BinaryOp.LessEqual,
                TokenKind.Greater => BinaryOp.Greater,
                TokenKind.GreaterEqual => BinaryOp.GreaterEqual,
                _ => null,
            };
            if (op is null) break;
            Advance();
            var right = ParseConcat();
            if (right is null) return null;
            left = new BinaryExpr(SpanBetween(left.Span, right.Span), op.Value, left, right);
        }
        return left;
    }

    private Expr? ParseConcat()
    {
        var left = ParseAdditive();
        if (left is null) return null;
        while (Match(TokenKind.Ampersand))
        {
            var right = ParseAdditive();
            if (right is null) return null;
            left = new BinaryExpr(SpanBetween(left.Span, right.Span), BinaryOp.Concat, left, right);
        }
        return left;
    }

    private Expr? ParseAdditive()
    {
        var left = ParseMultiplicative();
        if (left is null) return null;
        while (true)
        {
            BinaryOp? op = Peek().Kind switch
            {
                TokenKind.Plus => BinaryOp.Add,
                TokenKind.Minus => BinaryOp.Subtract,
                _ => null,
            };
            if (op is null) break;
            Advance();
            var right = ParseMultiplicative();
            if (right is null) return null;
            left = new BinaryExpr(SpanBetween(left.Span, right.Span), op.Value, left, right);
        }
        return left;
    }

    private Expr? ParseMultiplicative()
    {
        var left = ParseUnary();
        if (left is null) return null;
        while (true)
        {
            BinaryOp? op = Peek().Kind switch
            {
                TokenKind.Asterisk => BinaryOp.Multiply,
                TokenKind.Slash => BinaryOp.Divide,
                TokenKind.KwMod => BinaryOp.Mod,
                TokenKind.KwRemainder => BinaryOp.Remainder,
                _ => null,
            };
            if (op is null) break;
            Advance();
            var right = ParseUnary();
            if (right is null) return null;
            left = new BinaryExpr(SpanBetween(left.Span, right.Span), op.Value, left, right);
        }
        return left;
    }

    private Expr? ParseUnary()
    {
        if (Check(TokenKind.Plus) || Check(TokenKind.Minus))
        {
            var sign = Advance();
            var op = sign.Kind == TokenKind.Plus ? UnaryOp.Plus : UnaryOp.Negate;
            var operand = ParseUnary();
            if (operand is null) return null;
            return new UnaryExpr(SpanBetween(sign.Span, operand.Span), op, operand);
        }
        return ParsePower();
    }

    private Expr? ParsePower()
    {
        var left = ParsePrimary();
        if (left is null) return null;
        if (Match(TokenKind.Caret))
        {
            // Right-associative.
            var right = ParseUnary();
            if (right is null) return null;
            return new BinaryExpr(SpanBetween(left.Span, right.Span), BinaryOp.Power, left, right);
        }
        return left;
    }

    private Expr? ParsePrimary()
    {
        var tok = Peek();
        switch (tok.Kind)
        {
            case TokenKind.NumericLiteral:
                Advance();
                return new NumberExpr(tok.Span, tok.Text);

            case TokenKind.StringLiteral:
                Advance();
                return new StringExpr(tok.Span, UnescapeString(tok.Text));

            case TokenKind.LParen:
            {
                var open = Advance();
                var inner = ParseExpression();
                if (inner is null) return null;
                if (!ExpectKind(TokenKind.RParen, "')'")) return null;
                return new ParenExpr(SpanBetween(open.Span, Previous().Span), inner);
            }

            case TokenKind.Identifier:
            case TokenKind.StringIdentifier:
            {
                Advance();
                var name = NameWithoutDollar(tok.Text);
                var isString = tok.Kind == TokenKind.StringIdentifier;

                // Subscripted access or function call: same syntax. Sema disambiguates.
                if (Match(TokenKind.LParen))
                {
                    var args = ParseExpressionList(TokenKind.RParen);
                    if (!ExpectKind(TokenKind.RParen, "')'")) return null;
                    return new CallOrIndexExpr(SpanBetween(tok.Span, Previous().Span), name, isString, args);
                }
                return new NameRefExpr(tok.Span, name, isString);
            }

            // FN-prefixed user-defined function reference: FN xxx(args) or FNxxx(args).
            case TokenKind.KwFn:
            {
                var fnTok = Advance();
                if (Peek().Kind != TokenKind.Identifier && Peek().Kind != TokenKind.StringIdentifier)
                {
                    ErrorAt(Peek(), ErrExpectedToken, "expected function name after FN");
                    return null;
                }
                var nameTok = Advance();
                var isStr = nameTok.Kind == TokenKind.StringIdentifier;
                var rawName = NameWithoutDollar(nameTok.Text);
                var name = rawName.StartsWith("FN", StringComparison.OrdinalIgnoreCase) ? rawName : "FN" + rawName;
                var args = new List<Expr>();
                if (Match(TokenKind.LParen))
                {
                    args = ParseExpressionList(TokenKind.RParen);
                    if (!ExpectKind(TokenKind.RParen, "')'")) return null;
                }
                return new CallOrIndexExpr(SpanBetween(fnTok.Span, Previous().Span), name, isStr, args);
            }

            default:
                ErrorAt(tok, ErrExpectedExpression, $"expected an expression (got {tok.Kind})");
                return null;
        }
    }

    /// <summary>Parses a comma-separated list of expressions until <paramref name="terminator"/>.</summary>
    private List<Expr> ParseExpressionList(TokenKind terminator)
    {
        var list = new List<Expr>();
        if (Check(terminator)) return list;
        do
        {
            var e = ParseExpression();
            if (e is null) return list;
            list.Add(e);
        }
        while (Match(TokenKind.Comma));
        return list;
    }
}
