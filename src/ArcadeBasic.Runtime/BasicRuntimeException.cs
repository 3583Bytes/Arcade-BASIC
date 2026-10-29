namespace ArcadeBasic.Runtime;

/// <summary>
/// A BASIC exception raised while running: the type code (EXTYPE) and its text. The
/// VM catches it and hands it to the innermost WHEN handler, or ends the program.
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
