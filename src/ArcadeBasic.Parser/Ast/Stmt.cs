using ArcadeBasic.Core;

namespace ArcadeBasic.Parser.Ast;

/// <summary>
/// Base for all statement AST nodes. Carries the source span and an optional
/// integer line label (the leading number on the source line, if present).
/// </summary>
public abstract record class Stmt(SourceSpan Span)
{
    public int? Label { get; init; }
}

// -- Simple statements ----------------------------------------------------

/// <summary>Assignment: LET target = value, or just target = value.</summary>
public sealed record class AssignStmt(SourceSpan Span, Expr Target, Expr Value, bool ExplicitLet) : Stmt(Span);

/// <summary>PRINT statement.</summary>
public sealed record class PrintStmt(SourceSpan Span, IReadOnlyList<PrintItem> Items) : Stmt(Span);

/// <summary>PRINT USING format$ : items — formatted print using a picture string (Editing module).</summary>
public sealed record class PrintUsingStmt(SourceSpan Span, Expr Format, IReadOnlyList<Expr> Items) : Stmt(Span);

/// <summary>INPUT statement.</summary>
public sealed record class InputStmt(SourceSpan Span, Expr? Prompt, bool PromptIsSemicolon, IReadOnlyList<Expr> Targets) : Stmt(Span);

/// <summary>LINE INPUT statement — reads a whole line into a string variable.</summary>
public sealed record class LineInputStmt(SourceSpan Span, Expr? Prompt, bool PromptIsSemicolon, Expr Target) : Stmt(Span);

/// <summary>READ statement.</summary>
public sealed record class ReadStmt(SourceSpan Span, IReadOnlyList<Expr> Targets) : Stmt(Span);

/// <summary>DATA statement.</summary>
public sealed record class DataStmt(SourceSpan Span, IReadOnlyList<DataItem> Items) : Stmt(Span);

/// <summary>RESTORE [label]</summary>
public sealed record class RestoreStmt(SourceSpan Span, Expr? LabelTarget) : Stmt(Span);

/// <summary>GOTO label</summary>
public sealed record class GotoStmt(SourceSpan Span, Expr LabelTarget) : Stmt(Span);

/// <summary>GOSUB label</summary>
public sealed record class GosubStmt(SourceSpan Span, Expr LabelTarget) : Stmt(Span);

/// <summary>RETURN — from GOSUB, FUNCTION, or SUB.</summary>
public sealed record class ReturnStmt(SourceSpan Span) : Stmt(Span);

/// <summary>STOP — terminate program with a stop status.</summary>
public sealed record class StopStmt(SourceSpan Span) : Stmt(Span);

/// <summary>END — physical program terminator (must be the last line per spec).</summary>
public sealed record class EndStmt(SourceSpan Span) : Stmt(Span);

/// <summary>END IF / END FOR / END SUB / END FUNCTION / END SELECT — block terminators.
/// Sema verifies the kind matches the enclosing block.</summary>
public sealed record class EndBlockStmt(SourceSpan Span, EndBlockKind Kind) : Stmt(Span);

public enum EndBlockKind
{
    EndIf,
    EndSelect,
    EndSub,
    EndFunction,
    EndDef,
    EndWhen,
    EndModule,
}

/// <summary>RUN — start program execution (in REPL/interactive contexts).</summary>
public sealed record class RunStmt(SourceSpan Span) : Stmt(Span);

/// <summary>RANDOMIZE [seed]</summary>
public sealed record class RandomizeStmt(SourceSpan Span, Expr? Seed) : Stmt(Span);

/// <summary>REM comment line. Comment text retained for formatter use.</summary>
public sealed record class RemStmt(SourceSpan Span, string Comment) : Stmt(Span);

/// <summary>DIM declaration: DIM A(10), B(2 TO 5, 1 TO 10), S$(20).</summary>
public sealed record class DimStmt(SourceSpan Span, IReadOnlyList<DimSpec> Specs) : Stmt(Span);

/// <summary>OPTION BASE 0 / OPTION BASE 1.</summary>
public sealed record class OptionBaseStmt(SourceSpan Span, int Base) : Stmt(Span);

/// <summary>OPTION ARITHMETIC DECIMAL / NATIVE — arithmetic mode declaration.</summary>
public sealed record class OptionArithmeticStmt(SourceSpan Span, ArithmeticMode Mode) : Stmt(Span);

public enum ArithmeticMode
{
    Decimal,
    Native,
    Fixed,
}

// -- Block statements -----------------------------------------------------

/// <summary>Block IF: IF cond THEN ... ELSEIF cond THEN ... ELSE ... END IF.
/// Single-line IF/THEN form is parsed into the ThenBlock with no Else.</summary>
public sealed record class IfStmt(
    SourceSpan Span,
    Expr Condition,
    IReadOnlyList<Stmt> ThenBlock,
    IReadOnlyList<ElseIfClause> ElseIfs,
    IReadOnlyList<Stmt>? ElseBlock) : Stmt(Span);

public sealed record class ElseIfClause(SourceSpan Span, Expr Condition, IReadOnlyList<Stmt> Body);

/// <summary>FOR var = from TO to [STEP step] / NEXT var.</summary>
public sealed record class ForStmt(
    SourceSpan Span,
    NameRefExpr Variable,
    Expr From,
    Expr To,
    Expr? Step,
    IReadOnlyList<Stmt> Body) : Stmt(Span);

/// <summary>NEXT — block-terminator for FOR. Variable name optional in our parser; sema enforces match.</summary>
public sealed record class NextStmt(SourceSpan Span, NameRefExpr? Variable) : Stmt(Span);

/// <summary>DO / LOOP block. Pre-condition (DO WHILE/UNTIL) and post-condition (LOOP WHILE/UNTIL) both optional.</summary>
public sealed record class DoStmt(
    SourceSpan Span,
    DoCondition? Pre,
    IReadOnlyList<Stmt> Body,
    DoCondition? Post) : Stmt(Span);

public sealed record class DoCondition(bool IsUntil, Expr Condition);

/// <summary>LOOP — block-terminator for DO. May include trailing WHILE/UNTIL.</summary>
public sealed record class LoopStmt(SourceSpan Span, DoCondition? PostCondition) : Stmt(Span);

/// <summary>SELECT CASE subj / CASE values / CASE ELSE / END SELECT.</summary>
public sealed record class SelectStmt(
    SourceSpan Span,
    Expr Subject,
    IReadOnlyList<CaseClause> Cases,
    IReadOnlyList<Stmt>? CaseElse) : Stmt(Span);

public sealed record class CaseClause(SourceSpan Span, IReadOnlyList<CaseSpec> Values, IReadOnlyList<Stmt> Body);

/// <summary>A single CASE value. Either an exact value, a range (lo TO hi), or IS rel-op expr.</summary>
public abstract record class CaseSpec(SourceSpan Span);
public sealed record class CaseValue(SourceSpan Span, Expr Value) : CaseSpec(Span);
public sealed record class CaseRange(SourceSpan Span, Expr Lo, Expr Hi) : CaseSpec(Span);
public sealed record class CaseIs(SourceSpan Span, BinaryOp Op, Expr Value) : CaseSpec(Span);

/// <summary>EXIT FOR / DO / SUB / FUNCTION (and later WHEN/HANDLER).</summary>
public sealed record class ExitStmt(SourceSpan Span, ExitTarget Target) : Stmt(Span);

public enum ExitTarget
{
    For,
    Do,
    Sub,
    Function,
    Def,
    When,
    Handler,
    Select,
}

// -- Definitions: DEF / SUB / FUNCTION / CALL -----------------------------

/// <summary>Single-line DEF: DEF FNxxx[(params)] = expr.
/// Multi-line DEF: DEF FNxxx[(params)] / ... / END DEF.</summary>
public sealed record class DefStmt(
    SourceSpan Span,
    string Name,
    bool IsString,
    IReadOnlyList<Param> Params,
    Expr? SingleLineBody,
    IReadOnlyList<Stmt>? MultiLineBody) : Stmt(Span)
{
    public bool IsPublic { get; init; }
}

/// <summary>SUB name [(params)] / ... / END SUB.</summary>
public sealed record class SubStmt(
    SourceSpan Span,
    string Name,
    IReadOnlyList<Param> Params,
    IReadOnlyList<Stmt> Body) : Stmt(Span)
{
    /// <summary>True if declared with PUBLIC prefix inside a MODULE.</summary>
    public bool IsPublic { get; init; }
}

/// <summary>FUNCTION name [(params)] / ... / END FUNCTION.</summary>
public sealed record class FunctionStmt(
    SourceSpan Span,
    string Name,
    bool IsString,
    IReadOnlyList<Param> Params,
    IReadOnlyList<Stmt> Body) : Stmt(Span)
{
    public bool IsPublic { get; init; }
}

/// <summary>CALL name [(args)].</summary>
public sealed record class CallStmt(SourceSpan Span, string Name, IReadOnlyList<Expr> Args) : Stmt(Span);

// -- Helper records -------------------------------------------------------

public sealed record class Param(SourceSpan Span, string Name, bool IsString, bool IsArray);

public sealed record class DimSpec(SourceSpan Span, string Name, bool IsString, IReadOnlyList<DimBound> Bounds);

public sealed record class DimBound(SourceSpan Span, Expr? Lower, Expr Upper);

public sealed record class DataItem(SourceSpan Span, bool IsString, string Text);

/// <summary>An item in a PRINT list — either an expression, or a separator that affects formatting.</summary>
public abstract record class PrintItem(SourceSpan Span);
public sealed record class PrintExprItem(SourceSpan Span, Expr Value) : PrintItem(Span);
public sealed record class PrintTab(SourceSpan Span, Expr Column) : PrintItem(Span);
public sealed record class PrintComma(SourceSpan Span) : PrintItem(Span);
public sealed record class PrintSemicolon(SourceSpan Span) : PrintItem(Span);
