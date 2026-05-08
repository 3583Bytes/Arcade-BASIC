using FullBasic.Core;

namespace FullBasic.Parser.Ast;

/// <summary>
/// Top-level program: a sequence of statements (which may include nested SUB,
/// FUNCTION, DEF blocks). Modules are not represented yet — Phase 7.
/// </summary>
public sealed record class Program(SourceSpan Span, IReadOnlyList<Stmt> Statements);
