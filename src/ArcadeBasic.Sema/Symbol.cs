using ArcadeBasic.Core;
using ArcadeBasic.Parser.Ast;

namespace ArcadeBasic.Sema;

/// <summary>
/// Sealed hierarchy of symbol kinds. Each AST name reference resolves to one
/// of these. Slot-indexed activation records: variables/params have a
/// (scope, slot) coordinate, sub/function symbols name a body scope.
/// </summary>
public abstract record class Symbol(string Name, bool IsString)
{
    /// <summary>The scope that owns this symbol (where it was declared).</summary>
    public Scope? OwnerScope { get; init; }
}

/// <summary>A simple numeric or string variable. First-use binding.</summary>
public sealed record class VariableSymbol(string Name, bool IsString, int Slot) : Symbol(Name, IsString);

/// <summary>An array variable. Bounds resolved at runtime per DimSpec.</summary>
public sealed record class ArraySymbol(string Name, bool IsString, int Slot, DimSpec Spec) : Symbol(Name, IsString);

/// <summary>A SUB or FUNCTION parameter (in its own scope).</summary>
public sealed record class ParamSymbol(string Name, bool IsString, int Slot, bool IsArray) : Symbol(Name, IsString);

/// <summary>A SUB declaration. Has its own body scope.</summary>
public sealed record class SubSymbol(string Name, IReadOnlyList<Param> Params, Scope BodyScope, SubStmt Stmt) : Symbol(Name, IsString: false);

/// <summary>A FUNCTION declaration with its own body scope; return type from $ suffix.</summary>
public sealed record class FunctionSymbol(string Name, bool IsString, IReadOnlyList<Param> Params, Scope BodyScope, FunctionStmt Stmt) : Symbol(Name, IsString);

/// <summary>A DEF (single-line or multi-line user function). BodyScope owns the
/// param symbols so name resolution in the body has a stable Scope that other
/// phases (e.g. the bytecode compiler) can refer back to.</summary>
public sealed record class DefSymbol(string Name, bool IsString, IReadOnlyList<Param> Params, Scope BodyScope, DefStmt Stmt) : Symbol(Name, IsString);

/// <summary>A predefined supplied function (SIN, COS, MID$, etc.).</summary>
public sealed record class BuiltinSymbol(string Name, bool IsString, BuiltinSignature Signature) : Symbol(Name, IsString);

/// <summary>A named exception handler declared via HANDLER ... END HANDLER.</summary>
public sealed record class HandlerSymbol(string Name, HandlerStmt Stmt) : Symbol(Name, IsString: false);

/// <summary>A predefined constant identifier (PI, EPS, MAXNUM, INF).</summary>
public sealed record class ConstantSymbol(string Name, bool IsString) : Symbol(Name, IsString);

/// <summary>How a name is resolved at a particular reference site. Sema attaches one to each name-bearing Expr.</summary>
public abstract record class ResolvedRef;

public sealed record class ResolvedVariable(VariableSymbol Symbol) : ResolvedRef;
public sealed record class ResolvedParam(ParamSymbol Symbol) : ResolvedRef;
public sealed record class ResolvedArrayAccess(ArraySymbol Symbol) : ResolvedRef;
public sealed record class ResolvedBuiltinCall(BuiltinSymbol Symbol) : ResolvedRef;
public sealed record class ResolvedSubCall(SubSymbol Symbol) : ResolvedRef;
public sealed record class ResolvedFunctionCall(FunctionSymbol Symbol) : ResolvedRef;
public sealed record class ResolvedDefCall(DefSymbol Symbol) : ResolvedRef;
public sealed record class ResolvedConstant(ConstantSymbol Symbol) : ResolvedRef;
/// <summary>Resolution failed; an error has been emitted. Placeholder so downstream phases don't crash on missing keys.</summary>
public sealed record class ResolvedError(string Why) : ResolvedRef;

/// <summary>
/// Static description of a builtin function's argument shape. Arity is checked
/// at sema time; per-arg type checking is best-effort (numeric/string, with
/// AnyNumeric/AnyString sentinels for variadics).
/// </summary>
public sealed record class BuiltinSignature(int MinArgs, int MaxArgs, BuiltinArgType[] Args);

public enum BuiltinArgType
{
    Numeric,
    String,
    /// <summary>Either type accepted (used by string-vs-numeric polymorphic builtins, rare).</summary>
    Any,
}
