using ArcadeBasic.Core;

namespace ArcadeBasic.Parser.Ast;

/// <summary>OPEN #ch: NAME f$, ACCESS ?, ORGANIZATION ?, CREATE ?, RECTYPE ?
/// (RECSIZE/KEY are deferred to a follow-up).</summary>
public sealed record class OpenStmt(
    SourceSpan Span,
    Expr Channel,
    Expr Name,
    OpenAccess Access,
    OpenOrganization Organization,
    OpenCreate Create,
    OpenRecType RecType) : Stmt(Span);

/// <summary>CLOSE #ch.</summary>
public sealed record class CloseStmt(SourceSpan Span, Expr Channel) : Stmt(Span);

/// <summary>WRITE #ch: items — write exact-value (INTERNAL) records.</summary>
public sealed record class WriteFileStmt(SourceSpan Span, Expr Channel, IReadOnlyList<Expr> Items) : Stmt(Span);

/// <summary>READ #ch: targets — read exact-value (INTERNAL) records.</summary>
public sealed record class ReadFileStmt(SourceSpan Span, Expr Channel, IReadOnlyList<Expr> Targets) : Stmt(Span);

/// <summary>PRINT #ch: items.</summary>
public sealed record class PrintFileStmt(SourceSpan Span, Expr Channel, IReadOnlyList<PrintItem> Items) : Stmt(Span);

/// <summary>INPUT #ch: targets.</summary>
public sealed record class InputFileStmt(SourceSpan Span, Expr Channel, IReadOnlyList<Expr> Targets) : Stmt(Span);

/// <summary>LINE INPUT #ch: var$.</summary>
public sealed record class LineInputFileStmt(SourceSpan Span, Expr Channel, Expr Target) : Stmt(Span);

public enum OpenAccess
{
    /// <summary>ACCESS clause omitted — implementation default (OUTIN).</summary>
    Default,
    Input,
    Output,
    Outin,
}

public enum OpenOrganization
{
    /// <summary>ORGANIZATION clause omitted — implementation default (SEQUENTIAL).</summary>
    Default,
    Sequential,
    Stream,
    Random,
}

public enum OpenRecType
{
    /// <summary>RECTYPE omitted — default DISPLAY (text records).</summary>
    Default,
    /// <summary>DISPLAY — text records (PRINT #/INPUT #).</summary>
    Display,
    /// <summary>INTERNAL — exact-value binary records (WRITE #/READ #).</summary>
    Internal,
}

public enum OpenCreate
{
    /// <summary>CREATE clause omitted — implementation default (NEWOLD).</summary>
    Default,
    /// <summary>File must not exist; create it.</summary>
    New,
    /// <summary>File must exist.</summary>
    Old,
    /// <summary>Either: open if exists, create if not.</summary>
    NewOld,
}
