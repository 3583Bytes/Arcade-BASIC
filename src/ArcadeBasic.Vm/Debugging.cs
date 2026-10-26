using ArcadeBasic.Bytecode;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Vm;

/// <summary>Where <see cref="BasicVm.PauseAt"/> stops next, counting from the statement the
/// program is on (statements later on the same line count as part of it).</summary>
public enum DebugStep
{
    /// <summary>Before the very next statement, wherever it is — a "pause" button.</summary>
    Pause,

    /// <summary>Before the next line run, including inside a SUB, FUNCTION, DEF or GOSUB
    /// the current one calls.</summary>
    Into,

    /// <summary>Before the next line run here or in a caller — running any call or GOSUB
    /// the current line makes to the end.</summary>
    Over,

    /// <summary>Before the next line run once the current SUB, FUNCTION, DEF or GOSUB
    /// has returned.</summary>
    Out,
}

/// <summary>One call in progress, for a debugger: its code, its name (<c>SUB DRAW</c>; empty
/// for the main program), the line it's on, and its variables by slot — the analyzer's
/// scopes (<c>SemanticInfo</c>) say which name each slot holds.</summary>
public readonly record struct DebugFrame(Chunk Chunk, string Name, int Line, ActivationRecord Variables);
