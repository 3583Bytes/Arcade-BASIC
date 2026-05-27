using FullBasic.Core;

namespace FullBasic.Parser.Ast;

/// <summary>
/// MODULE name ... END MODULE — a named module containing declarations
/// (SUB/FUNCTION/DEF/DIM) plus optional initialization statements.
///
/// Per Q7-style scope rules: declarations inside the module are private
/// to the module by default. Adding the PUBLIC prefix to a SUB/FUNCTION/
/// DEF makes it callable from the main program and other modules.
/// </summary>
public sealed record class ModuleStmt(SourceSpan Span, string Name, IReadOnlyList<Stmt> Body) : Stmt(Span);
