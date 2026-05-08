namespace FullBasic.Runtime;

/// <summary>
/// Non-local control flow signal returned by every statement. The interpreter's
/// statement loops dispatch on this to implement GOTO/GOSUB/RETURN/EXIT/STOP
/// without using .NET exceptions. Cause/Retry/Continue are reserved for the
/// exception-handler machinery (Phase 6) and unused in Phase 3.
/// </summary>
public abstract record class FlowControl
{
    /// <summary>Continue with the next statement.</summary>
    public sealed record class Next : FlowControl;

    /// <summary>Jump to a labeled statement at this scope or an enclosing one.</summary>
    public sealed record class Goto(int Label) : FlowControl;

    /// <summary>GOSUB jump — caller will eventually RETURN.</summary>
    public sealed record class Gosub(int Label) : FlowControl;

    /// <summary>RETURN from a GOSUB or function/sub. WithValue carries an optional return value.</summary>
    public sealed record class Return(Value? Result = null) : FlowControl;

    /// <summary>STOP — terminate program normally with stop status.</summary>
    public sealed record class Stop : FlowControl;

    /// <summary>END — terminate program normally with end status.</summary>
    public sealed record class End : FlowControl;

    /// <summary>EXIT — exit a specific kind of enclosing block (FOR/DO/SUB/etc.).</summary>
    public sealed record class Exit(ExitKind Kind) : FlowControl;

    public static readonly Next Continue = new();
    public static readonly Stop Stopped = new();
    public static readonly End Ended = new();
}

public enum ExitKind
{
    For,
    Do,
    Sub,
    Function,
    Def,
    Select,
    When,
    Handler,
}
