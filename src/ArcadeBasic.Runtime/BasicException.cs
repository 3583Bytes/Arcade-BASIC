namespace ArcadeBasic.Runtime;

/// <summary>
/// A BASIC-level exception (the value the spec exposes via EXTYPE/EXLINE/EXTEXT$).
/// Distinct from BasicRuntimeException, which is the C# exception we use to
/// raise one: the VM makes this from it, with the line, when a handler catches it.
/// </summary>
public sealed record class BasicException(int Type, int Line, string Text, string? File = null)
{
    /// <summary>"line 12", or "line 12 in textures.bas" outside the main program's file.</summary>
    public string Where => File is null ? $"line {Line}" : $"line {Line} in {File}";
}
