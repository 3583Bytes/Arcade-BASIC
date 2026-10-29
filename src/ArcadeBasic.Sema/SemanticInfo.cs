using ArcadeBasic.Parser.Ast;

namespace ArcadeBasic.Sema;

/// <summary>Result of running the analyzer over a Program. Consumed by later phases.</summary>
public sealed class SemanticInfo
{
    /// <summary>The top-level (program) scope. Other scopes attach as parents/children.</summary>
    public required Scope ProgramScope { get; init; }

    /// <summary>Map from each name-bearing expression to its resolution. Reference-keyed.</summary>
    public required IReadOnlyDictionary<Expr, ResolvedRef> Resolutions { get; init; }

    /// <summary>Static type tag (numeric or string) per expression. Reference-keyed.</summary>
    public required IReadOnlyDictionary<Expr, BasicType> ExpressionTypes { get; init; }

    /// <summary>The main program's DATA, in source order (its internal procedures' included).</summary>
    public IReadOnlyList<DataItem> DataPool => DataPools[0];

    /// <summary>Each program-unit's DATA (ISO 10279: a READ reads its own unit's): the main
    /// program's first, then each MODULE's, in the order they appear.</summary>
    public required IReadOnlyList<IReadOnlyList<DataItem>> DataPools { get; init; }

    /// <summary>The array each DIM (or a module's SHARE / PUBLIC) item declares. Reference-keyed.</summary>
    public required IReadOnlyDictionary<DimSpec, ArraySymbol> Arrays { get; init; }

    /// <summary>Which of <see cref="DataPools"/> each READ, RESTORE and MAT READ uses. Reference-keyed.</summary>
    public required IReadOnlyDictionary<Stmt, int> DataUnits { get; init; }

    /// <summary>Line-label → statement map for GOTO/GOSUB/RESTORE resolution.</summary>
    public required IReadOnlyDictionary<int, Stmt> LineLabels { get; init; }

    /// <summary>CallStmt → resolved SubSymbol. Walks scope chain at sema time so cross-module CALLs work.</summary>
    public required IReadOnlyDictionary<CallStmt, SubSymbol> CallTargets { get; init; }

    /// <summary>Per-module local scope. PUBLIC declarations are also re-exported into ProgramScope, but private ones live only here. The compiler needs this to enumerate module-private callables when emitting bytecode.</summary>
    public required IReadOnlyDictionary<ModuleStmt, Scope> ModuleScopes { get; init; }

    /// <summary>Numeric representation for the whole program: <see cref="ArithmeticMode.Native"/>
    /// (IEEE double) when an OPTION ARITHMETIC NATIVE appears anywhere, otherwise
    /// <see cref="ArithmeticMode.Decimal"/>.</summary>
    public ArithmeticMode Arithmetic { get; init; } = ArithmeticMode.Decimal;

    /// <summary>Lookup the resolution for a given expression. Returns ResolvedError for unresolved names.</summary>
    public ResolvedRef Resolve(Expr expr) =>
        Resolutions.TryGetValue(expr, out var r) ? r : new ResolvedError("not resolved");

    /// <summary>Lookup the static type of an expression. Defaults to Numeric for unresolved nodes.</summary>
    public BasicType TypeOf(Expr expr) =>
        ExpressionTypes.TryGetValue(expr, out var t) ? t : BasicType.Numeric;
}

public enum BasicType
{
    Numeric,
    String,
}
