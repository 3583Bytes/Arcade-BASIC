using ArcadeBasic.Core;

namespace ArcadeBasic.Parser.Ast;

/// <summary>
/// MODULE name ... END MODULE — a named module (ISO 10279): its variables (SHARE,
/// PUBLIC), its procedures, and statements that run before the main program to set
/// it up. A SUB/FUNCTION/DEF is private to the module unless marked PUBLIC; the
/// module's code doesn't see the main program's variables.
/// </summary>
public sealed record class ModuleStmt(SourceSpan Span, string Name, IReadOnlyList<Stmt> Body) : Stmt(Span);

/// <summary>SHARE or PUBLIC at a module's top level: its variables — scalars, and arrays
/// with bounds (as DIM gives them). SHARE ones are for the module's own code; PUBLIC ones
/// also for any program-unit that says DECLARE PUBLIC.</summary>
public sealed record class ModuleVarsStmt(SourceSpan Span, bool IsPublic, IReadOnlyList<DimSpec> Items) : Stmt(Span);

/// <summary>DECLARE PUBLIC: use variables that a module made PUBLIC.</summary>
public sealed record class DeclarePublicStmt(SourceSpan Span, IReadOnlyList<PublicName> Names) : Stmt(Span);

/// <summary>One name in DECLARE PUBLIC; an array is written with parentheses: <c>BOARD()</c>.</summary>
public sealed record class PublicName(SourceSpan Span, string Name, bool IsString, bool IsArray);

/// <summary>LIBRARY "file" [, "file" …] — other source files whose modules and procedures
/// join the program (True BASIC's statement; ISO 10279 leaves how program-units are found
/// to the implementation). The loader reads them; running it does nothing.</summary>
public sealed record class LibraryStmt(SourceSpan Span, IReadOnlyList<string> Files) : Stmt(Span);
