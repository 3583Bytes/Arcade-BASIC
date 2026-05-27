namespace ArcadeBasic.Sema;

/// <summary>
/// A lexical scope with a parent chain. Each scope manages a numeric slot
/// counter that gets handed out to variables/params/arrays as they're declared.
/// Lookups are case-insensitive; for string identifiers we suffix the name
/// with '$' so a numeric and string variable of the same letter coexist.
/// </summary>
public sealed class Scope
{
    private readonly Dictionary<string, Symbol> _symbols = new(StringComparer.OrdinalIgnoreCase);

    public Scope(ScopeKind kind, Scope? parent = null)
    {
        Kind = kind;
        Parent = parent;
    }

    public ScopeKind Kind { get; }

    public Scope? Parent { get; }

    /// <summary>Number of slots allocated in this frame so far.</summary>
    public int FrameSize { get; private set; }

    /// <summary>All symbols declared directly in this scope.</summary>
    public IReadOnlyDictionary<string, Symbol> Symbols => _symbols;

    /// <summary>Allocate the next slot index in this frame.</summary>
    public int AllocateSlot() => FrameSize++;

    /// <summary>Declare a symbol in this scope. Returns false on collision.</summary>
    public bool Declare(string keyName, Symbol symbol)
    {
        if (_symbols.ContainsKey(keyName)) return false;
        _symbols.Add(keyName, symbol with { OwnerScope = this });
        return true;
    }

    /// <summary>Look up only in this scope (no chain walk).</summary>
    public Symbol? LocalLookup(string keyName) =>
        _symbols.TryGetValue(keyName, out var sym) ? sym : null;

    /// <summary>Look up walking the parent chain.</summary>
    public Symbol? Lookup(string keyName)
    {
        for (var s = this; s is not null; s = s.Parent)
        {
            if (s._symbols.TryGetValue(keyName, out var sym))
            {
                return sym;
            }
        }
        return null;
    }

    /// <summary>
    /// Build the canonical key used for symbol lookup. For string identifiers we
    /// append '$' so 'A' (numeric) and 'A$' (string) coexist in the same scope.
    /// </summary>
    public static string Key(string name, bool isString) =>
        isString ? name.ToUpperInvariant() + "$" : name.ToUpperInvariant();
}

public enum ScopeKind
{
    Program,
    Sub,
    Function,
    Def,
    Module,
}
