using FullBasic.Core;

namespace FullBasic.Parser.Ast;

/// <summary>
/// WHEN EXCEPTION IN ... USE ... END WHEN, with USE either an inline statement
/// list or a reference to a named HANDLER. Exactly one of UseBody / UseHandlerName
/// is non-null on a well-formed WhenStmt.
/// </summary>
public sealed record class WhenStmt(
    SourceSpan Span,
    IReadOnlyList<Stmt> InBody,
    IReadOnlyList<Stmt>? UseBody,
    string? UseHandlerName) : Stmt(Span);

/// <summary>HANDLER name ... END HANDLER — a named, reusable exception handler.</summary>
public sealed record class HandlerStmt(SourceSpan Span, string Name, IReadOnlyList<Stmt> Body) : Stmt(Span);

/// <summary>CAUSE EXCEPTION expr — raise a numeric exception type.</summary>
public sealed record class CauseStmt(SourceSpan Span, Expr Type) : Stmt(Span);

/// <summary>RETRY — restart the IN body of the enclosing WHEN block.</summary>
public sealed record class RetryStmt(SourceSpan Span) : Stmt(Span);

/// <summary>
/// CONTINUE — resume the IN body of the enclosing WHEN block immediately
/// after the statement that raised. Named ContinueResumeStmt to avoid
/// confusion with the FlowControl "go to next statement" signal.
/// </summary>
public sealed record class ContinueResumeStmt(SourceSpan Span) : Stmt(Span);
