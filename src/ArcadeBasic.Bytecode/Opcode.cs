namespace ArcadeBasic.Bytecode;

/// <summary>
/// Phase-9 stack-based bytecode opcodes. Each instruction is one byte plus
/// optional operands (LEB128 ints for small numbers, 4-byte indices for
/// pool entries).
/// </summary>
public enum Opcode : byte
{
    // -- Stack manipulation --
    Halt,                  // terminate program
    Pop,                   // discard top of stack
    Dup,                   // duplicate top
    Swap,                  // swap top two

    // -- Constants --
    LoadConstNumber,       // operand: u32 pool index → push NumericValue
    LoadConstString,       // operand: u32 pool index → push StringValue
    LoadInt,               // operand: signed LEB128 → push small int as numeric
    LoadZero,              // push 0
    LoadOne,               // push 1
    LoadMinusOne,          // push -1 (BASIC's "true")

    // -- Variables (frame slots) --
    LoadLocal,             // operand: u32 slot
    StoreLocal,            // operand: u32 slot
    LoadOuter,             // operand: u32 depth, u32 slot — walk static link
    StoreOuter,            // operand: u32 depth, u32 slot

    // -- Arithmetic / string --
    Add, Sub, Mul, Div, Pow, Mod, Rem,
    Concat,
    Neg,                   // unary -

    // -- Comparison (push -1 / 0) --
    Eq, Ne, Lt, Le, Gt, Ge,

    // -- Logical --
    And, Or, Xor, Not,

    // -- Bitwise --
    Band, Bor, Bxor, Bnot,
    Imp, Eqv,

    // -- Control flow --
    Jump,                  // operand: signed i32 offset (relative to next pc)
    JumpIfTrue,            // pops top, jumps if non-zero
    JumpIfFalse,
    GosubFlow,             // operand: u32 absolute address — Gosub jump
    Return,                // pop return address from gosub stack

    // -- Calls --
    CallBuiltin,           // operand: u32 builtin index, u32 argc
    CallSub,               // operand: u32 sub index, u32 argc
    CallFunction,          // operand: u32 fn index, u32 argc → leaves return on stack
    CallDef,               // operand: u32 def index, u32 argc → leaves return on stack
    LeaveSub,              // SUB body completed — pop frame, no return value
    LeaveFunction,         // FUNCTION completed — pop frame, return value already in slot

    // -- I/O --
    PrintNumber,           // pop numeric, write
    PrintString,           // pop string, write
    PrintNewline,          // emit newline
    PrintZonePad,          // pad to next zone boundary

    // -- Constants for spec-defined identifiers --
    LoadConstantPi,
    LoadConstantEps,
    LoadConstantInf,
    LoadConstantMaxnum,

    // -- Misc --
    Stop,                  // STOP statement
    End,                   // END statement
    Nop,
}
