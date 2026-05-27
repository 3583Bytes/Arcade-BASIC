namespace ArcadeBasic.Runtime;

/// <summary>
/// A BASIC-level exception (the value the spec exposes via EXTYPE/EXLINE/EXTEXT$).
/// Distinct from BasicRuntimeException, which is the C# exception we use to
/// signal errors during expression evaluation. The interpreter converts the
/// latter into the former at statement boundaries before propagating
/// through FlowControl.Cause.
/// </summary>
public sealed record class BasicException(int Type, int Line, string Text);
