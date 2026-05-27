namespace ArcadeBasic.Runtime;

/// <summary>
/// Phase-3 stand-in for the spec's exception machinery. Phase 6 will replace
/// thrown C# exceptions with FlowControl.Cause so user WHEN/USE/HANDLER
/// blocks can catch them. Until then, runtime errors propagate via this
/// exception and the CLI top-level prints them.
/// </summary>
public sealed class BasicRuntimeException : Exception
{
    public BasicRuntimeException(int typeCode, string message) : base(message)
    {
        TypeCode = typeCode;
    }

    /// <summary>Spec-defined exception type code (e.g. 1001 division-by-zero, 1002 array-bounds).</summary>
    public int TypeCode { get; }
}
