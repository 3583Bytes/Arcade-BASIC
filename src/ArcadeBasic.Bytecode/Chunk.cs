using Singulink.Numerics;

namespace ArcadeBasic.Bytecode;

/// <summary>
/// A single executable chunk of bytecode: an opcode buffer, the constants pool
/// it references, and metadata about frame slots needed to run it. Each SUB,
/// FUNCTION, DEF and the program-level body each get their own Chunk.
/// </summary>
public sealed class Chunk
{
    private readonly List<byte> _code = [];
    private readonly List<BigDecimal> _numbers = [];
    private readonly List<string> _strings = [];

    public int FrameSize { get; set; }
    public int CodeLength => _code.Count;
    public IReadOnlyList<byte> Code => _code;
    public IReadOnlyList<BigDecimal> Numbers => _numbers;
    public IReadOnlyList<string> Strings => _strings;

    public byte[] ToArray() => [.. _code];

    public int Emit(Opcode op)
    {
        var pc = _code.Count;
        _code.Add((byte)op);
        return pc;
    }

    public void EmitU32(uint v)
    {
        _code.Add((byte)(v & 0xFF));
        _code.Add((byte)((v >> 8) & 0xFF));
        _code.Add((byte)((v >> 16) & 0xFF));
        _code.Add((byte)((v >> 24) & 0xFF));
    }

    public void EmitI32(int v) => EmitU32((uint)v);

    public int EmitJumpPlaceholder(Opcode op)
    {
        var pc = Emit(op);
        EmitI32(0);
        return pc;
    }

    /// <summary>Patch a jump instruction at <paramref name="pc"/> so it lands at the current end-of-code.</summary>
    public void PatchJump(int pc)
    {
        // Operand starts at pc + 1. Offset is relative to next pc (= pc + 5).
        var target = _code.Count;
        var offset = target - (pc + 5);
        _code[pc + 1] = (byte)(offset & 0xFF);
        _code[pc + 2] = (byte)((offset >> 8) & 0xFF);
        _code[pc + 3] = (byte)((offset >> 16) & 0xFF);
        _code[pc + 4] = (byte)((offset >> 24) & 0xFF);
    }

    /// <summary>Patch a LineNote opcode at <paramref name="pc"/> so its
    /// stmtEndOffset points to the current end-of-code. Layout is
    /// [opcode][u32 line][i32 stmtEndOffset] = 9 bytes; the offset is
    /// relative to the byte just past it (= pc + 9).</summary>
    public void PatchLineNoteEnd(int pc)
    {
        var target = _code.Count;
        var offset = target - (pc + 9);
        _code[pc + 5] = (byte)(offset & 0xFF);
        _code[pc + 6] = (byte)((offset >> 8) & 0xFF);
        _code[pc + 7] = (byte)((offset >> 16) & 0xFF);
        _code[pc + 8] = (byte)((offset >> 24) & 0xFF);
    }

    /// <summary>Overwrite the 4 bytes starting at <paramref name="operandStart"/> with
    /// <paramref name="value"/>. Used by forward-jump backfilling (GOTO/GOSUB to
    /// labels that weren't known when the opcode was emitted).</summary>
    public void PatchU32(int operandStart, uint value)
    {
        _code[operandStart + 0] = (byte)(value & 0xFF);
        _code[operandStart + 1] = (byte)((value >> 8) & 0xFF);
        _code[operandStart + 2] = (byte)((value >> 16) & 0xFF);
        _code[operandStart + 3] = (byte)((value >> 24) & 0xFF);
    }

    /// <summary>Patch a Jump-style relative-offset opcode at <paramref name="pc"/>
    /// so it lands at <paramref name="targetPc"/>. Operand layout: 1 byte opcode
    /// + 4-byte i32 offset relative to the byte just past it (= pc + 5).</summary>
    public void PatchJumpAbsolute(int pc, int targetPc)
    {
        var offset = targetPc - (pc + 5);
        PatchU32(pc + 1, (uint)offset);
    }

    public void EmitJumpToAbsolute(Opcode op, int absolutePc)
    {
        var pc = Emit(op);
        var offset = absolutePc - (pc + 5);
        EmitI32(offset);
    }

    public uint AddNumberConstant(BigDecimal v)
    {
        var idx = _numbers.IndexOf(v);
        if (idx < 0) { _numbers.Add(v); idx = _numbers.Count - 1; }
        return (uint)idx;
    }

    public uint AddStringConstant(string s)
    {
        var idx = _strings.IndexOf(s);
        if (idx < 0) { _strings.Add(s); idx = _strings.Count - 1; }
        return (uint)idx;
    }
}

/// <summary>
/// One compiled program — main code plus per-callable chunks. Cross-chunk
/// indices in the bytecode reference these by position in the lists.
/// </summary>
public sealed class Program
{
    public required Chunk Main { get; init; }
    public required IReadOnlyList<CompiledSub> Subs { get; init; }
    public required IReadOnlyList<CompiledFunction> Functions { get; init; }
    public required IReadOnlyList<CompiledDef> Defs { get; init; }
    public required IReadOnlyList<string> BuiltinNames { get; init; }
    /// <summary>Items collected from all DATA statements, in source order. Read via READ / MAT READ; rewound via RESTORE.</summary>
    public required IReadOnlyList<BcDataItem> DataPool { get; init; }
}

public sealed record class CompiledSub(string Name, int ParamCount, Chunk Body);
public sealed record class CompiledFunction(string Name, bool IsString, int ParamCount, int ReturnSlot, Chunk Body);
public sealed record class CompiledDef(string Name, bool IsString, int ParamCount, int ReturnSlot, Chunk Body);
public sealed record class BcDataItem(bool IsString, string Text);
