using FullBasic.Core;

namespace FullBasic.Parser.Ast;

/// <summary>Base for all expression AST nodes.</summary>
public abstract record class Expr(SourceSpan Span);

/// <summary>Numeric literal. Text preserved verbatim from source; sema converts to BigDecimal.</summary>
public sealed record class NumberExpr(SourceSpan Span, string Text) : Expr(Span);

/// <summary>String literal. Value already has surrounding quotes stripped and "" escapes resolved.</summary>
public sealed record class StringExpr(SourceSpan Span, string Value) : Expr(Span);

/// <summary>Reference to a simple variable: FOO or FOO$.</summary>
public sealed record class NameRefExpr(SourceSpan Span, string Name, bool IsString) : Expr(Span);

/// <summary>
/// Subscripted variable or function call: FOO(i, j). At parse time we can't always
/// tell apart array indexing from a built-in function call; sema resolves this.
/// </summary>
public sealed record class CallOrIndexExpr(SourceSpan Span, string Name, bool IsString, IReadOnlyList<Expr> Args) : Expr(Span);

/// <summary>Parenthesized expression. Preserved so source positions stay precise.</summary>
public sealed record class ParenExpr(SourceSpan Span, Expr Inner) : Expr(Span);

/// <summary>Unary expression (prefix only: + - NOT BNOT).</summary>
public sealed record class UnaryExpr(SourceSpan Span, UnaryOp Op, Expr Operand) : Expr(Span);

/// <summary>Binary expression: arithmetic, relational, logical, bitwise, string concat.</summary>
public sealed record class BinaryExpr(SourceSpan Span, BinaryOp Op, Expr Left, Expr Right) : Expr(Span);

public enum UnaryOp
{
    Plus,
    Negate,
    Not,
    BNot,
}

public enum BinaryOp
{
    // Arithmetic
    Add,
    Subtract,
    Multiply,
    Divide,
    Power,
    Mod,
    Remainder,

    // String
    Concat,

    // Relational
    Equal,
    NotEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,

    // Logical
    And,
    Or,
    Xor,
    Imp,
    Eqv,

    // Bitwise
    Band,
    Bor,
    Bxor,
}
