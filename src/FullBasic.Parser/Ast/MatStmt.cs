using FullBasic.Core;

namespace FullBasic.Parser.Ast;

// -- MAT statements -------------------------------------------------------

/// <summary>MAT name = rhs — assignment, optionally redims if shapes differ.</summary>
public sealed record class MatAssignStmt(SourceSpan Span, string TargetName, bool TargetIsString, MatRhs Rhs) : Stmt(Span);

/// <summary>MAT REDIM name(bounds, ...) — re-dimensions preserving in-bounds elements.</summary>
public sealed record class MatRedimStmt(SourceSpan Span, string TargetName, bool TargetIsString, IReadOnlyList<DimBound> Bounds) : Stmt(Span);

/// <summary>MAT INPUT name — read array elements from stdin.</summary>
public sealed record class MatInputStmt(SourceSpan Span, string TargetName, bool TargetIsString) : Stmt(Span);

/// <summary>MAT PRINT name — print all array elements with a sensible layout.</summary>
public sealed record class MatPrintStmt(SourceSpan Span, string TargetName, bool TargetIsString) : Stmt(Span);

/// <summary>MAT READ name — read array elements from the DATA pool.</summary>
public sealed record class MatReadStmt(SourceSpan Span, string TargetName, bool TargetIsString) : Stmt(Span);

// -- MAT right-hand-side expressions --------------------------------------

/// <summary>Base record for the right-hand side of a MAT assignment.</summary>
public abstract record class MatRhs(SourceSpan Span);

/// <summary>Reference to another array.</summary>
public sealed record class MatRhsName(SourceSpan Span, string Name, bool IsString) : MatRhs(Span);

/// <summary>Element-wise arithmetic (Add/Subtract) or matrix multiply (Multiply).</summary>
public sealed record class MatRhsBinary(SourceSpan Span, MatBinaryKind Op, MatRhs Left, MatRhs Right) : MatRhs(Span);

/// <summary>Scalar * matrix — scalar must be parenthesized per spec: (expr) * mat-name.</summary>
public sealed record class MatRhsScalarMul(SourceSpan Span, Expr Scalar, MatRhs Matrix) : MatRhs(Span);

/// <summary>INV(matrix) — matrix inversion.</summary>
public sealed record class MatRhsInv(SourceSpan Span, MatRhs Operand) : MatRhs(Span);

/// <summary>TRN(matrix) — transpose.</summary>
public sealed record class MatRhsTrn(SourceSpan Span, MatRhs Operand) : MatRhs(Span);

/// <summary>IDN/ZER/CON/NUL$ — constant-fill rhs.</summary>
public sealed record class MatRhsConst(SourceSpan Span, MatConstKind Kind) : MatRhs(Span);

public enum MatBinaryKind
{
    Add,
    Subtract,
    Multiply,
}

public enum MatConstKind
{
    Identity,    // IDN
    Zeros,       // ZER
    Ones,        // CON
    NullString,  // NUL$
}
