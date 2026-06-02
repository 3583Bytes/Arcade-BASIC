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
    PrintTab,              // pop numeric column target (1-based); pad spaces if target > current col

    // -- Constants for spec-defined identifiers --
    LoadConstantPi,
    LoadConstantEps,
    LoadConstantInf,
    LoadConstantMaxnum,

    // -- Exception handling --
    //
    //   LineNote   operand: u32 line, i32 stmtEndOffset (relative to next pc).
    //              Updates _currentLine (used by EXLINE) and the local
    //              stmtEndPc register (used by CONTINUE — the PC to resume
    //              at if the enclosing USE body chooses to skip past the
    //              statement that just raised). Compiler emits one before
    //              each statement and patches stmtEndOffset to point at the
    //              start of the following statement (or just past the end
    //              of the enclosing statement list).
    //
    //   BeginWhen  operand: i32 useOffset (relative to next pc). Pushes a
    //              handler frame {usePc, operand-stack-baseline}; an exception
    //              raised before the matching PopHandler pops the frame,
    //              truncates the stack, and jumps to usePc.
    //
    //   PopHandler operand: none. Pops the topmost handler. Emitted at the
    //              end of the IN body (normal exit). Use bodies fall through.
    //
    //   Cause      operand: none. Pops a numeric type from the stack and
    //              throws a BasicRuntimeException carrying that type. The
    //              same dispatch path that catches runtime errors handles it.
    //
    //   Retry      operand: i32 beginWhenOffset (relative to next pc). Jumps
    //              back to the matching BeginWhen so its push of a fresh
    //              handler re-executes; in-body runs again from the top.
    //
    //   Continue   operand: none. Inside a USE body, jumps to _currentContinuePc
    //              — the PC of the statement immediately after the one that
    //              raised the exception. Snapshot of the local stmtEndPc
    //              taken when the exception was dispatched.
    LineNote,
    BeginWhen,
    PopHandler,
    Cause,
    Retry,
    Continue,

    // -- File I/O (DISPLAY mode, SEQUENTIAL/STREAM) --
    //
    //   Open       operand: u32 access, u32 organization, u32 create
    //              stack (top last): name, channel
    //              kind values match Parser.Ast.OpenAccess / OpenOrganization /
    //              OpenCreate ordinals.
    //
    //   Close      stack: channel
    //
    //   PrintFile  operand: u32 itemCount, then per item:
    //                  u32 kind  (0=ExprNumeric, 1=ExprString, 2=Comma, 3=Semicolon)
    //              stack (top last): values for Expr items in declaration order,
    //              then channel on top.
    //
    //   InputFile  operand: u32 targetCount, then per target:
    //                  u32 depth, u32 slot, u32 isString, u32 rank
    //              stack (top last): for each array target, `rank` subscripts;
    //              then channel.
    //
    //   LineInputFile  operand: u32 depth, u32 slot, u32 rank
    //                  stack (top last): for an array target, `rank` subscripts;
    //                  then channel. (Always string-typed.)
    //
    //   LineInput      operand: u32 suppressQuestionMark, u32 depth, u32 slot, u32 rank
    //                  stack (top last): for an array target, `rank` subscripts.
    //                  Reads a whole line from stdin (after printing the prompt
    //                  suffix " " or "? "); the prompt text itself is emitted
    //                  as a separate PrintString. Always string-typed.
    Open,
    Close,
    PrintFile,
    InputFile,
    LineInputFile,
    LineInput,

    // -- PRINT USING --
    //   operand: u32 itemCount
    //   stack (top last): format string, item_0, item_1, ..., item_{count-1}
    //   effect: PictureFormat.Apply(Parse(format), items) → write + newline
    PrintUsing,

    // -- READ / DATA / RESTORE --
    // DATA items are baked into BcProgram.DataPool at compile time; the VM
    // tracks a single _dataCursor advanced by READ / MatRead and reset by
    // Restore. Read mirrors Input's per-target descriptor layout (no prompt /
    // retry — DATA parse errors are fatal per spec).
    //
    //   Read operand: u32 targetCount
    //                 for each target:
    //                   u32 depth
    //                   u32 slot
    //                   u32 isString
    //                   u32 rank          (0 for scalar, else array)
    //   stack: for each array target in declaration order, `rank` subscripts.
    //
    //   Restore operand: none. Mirrors the tree-walker — RESTORE label is
    //                    parsed but treated as plain RESTORE (cursor → 0).
    //
    //   MatRead operand: u32 depth, u32 slot, u32 isString. Fills the array
    //                    with consecutive DATA items.
    Read,
    Restore,
    MatRead,

    // -- MAT --
    // The RHS of a MAT assignment is lowered to a postfix sequence of these
    // opcodes; array values flow through the stack. Constant RHS forms (ZER,
    // IDN, CON, NUL$) read the target's current bounds, so they're folded
    // into MatAssignConst which takes the target slot inline.
    //
    // MatBinAdd/Sub/Mul: pop two arrays (RHS on top), push elementwise/matrix
    // product. MatScalarMul: pop array (top) then numeric, push scaled array.
    // MatTrn/MatInv: pop array, push result.
    MatLoadArray,          // operand: u32 depth, u32 slot — push the array Value from the slot
    MatBinAdd,             // pop two arrays, push elementwise sum
    MatBinSub,             // pop two arrays, push elementwise difference
    MatBinMul,             // pop two arrays, push matrix product (2-D × 2-D)
    MatScalarMul,          // pop array, then numeric scalar; push scaled array
    MatTrn,                // pop 2-D array, push transpose
    MatInv,                // pop square 2-D array, push inverse
    MatAssign,             // operand: u32 depth, u32 slot, u32 isString — pop array, store
    MatAssignConst,        // operand: u32 depth, u32 slot, u32 isString, u32 kind (0=IDN,1=ZER,2=CON,3=NUL$ — matches Parser.Ast.MatConstKind ordinals)
    MatPushConst,          // operand: u32 depth, u32 slot, u32 isString, u32 kind — same as MatAssignConst but PUSHES the constant array (for nested-const RHS like MAT C = ZER + B)
    MatRedim,              // operand: u32 depth, u32 slot, u32 rank, u32 isString — pops 2*rank bounds
    MatPrint,              // operand: u32 depth, u32 slot
    MatInput,              // operand: u32 depth, u32 slot, u32 isString

    // -- INPUT --
    // The whole INPUT statement (prompt suffix + ReadLine + parse + retry +
    // assign) is one fat opcode. The prompt text itself is emitted as a
    // separate PrintString just before, so the operand only needs the suffix
    // mode plus per-target target descriptors.
    //
    //   operand: u32 suppressQuestionMark (0 = print "? ", 1 = print " ")
    //            u32 targetCount
    //            for each target:
    //              u32 depth      — 0 for local frame, else walk parent chain
    //              u32 slot
    //              u32 isString
    //              u32 rank       — 0 for scalar, else array rank
    //
    //   stack (top last): for each array target in declaration order, `rank`
    //                     subscript values (last subscript on top).
    Input,

    // -- Arrays --
    // Bounds, subscripts, and values flow through the stack; opcode operands
    // carry slot, rank, and (for Dim*) the numeric-vs-string flag. The Outer
    // variants prepend a u32 depth and walk the static link, mirroring
    // LoadOuter/StoreOuter for scalars.
    DimArray,              // operand: u32 slot, u32 rank, u32 isString — stack (top last): lo_0,hi_0, lo_1,hi_1, ...
    DimArrayOuter,         // operand: u32 depth, u32 slot, u32 rank, u32 isString
    LoadElement,           // operand: u32 slot, u32 rank — stack (top last): sub_0, sub_1, ..., sub_{rank-1}
    LoadElementOuter,      // operand: u32 depth, u32 slot, u32 rank
    StoreElement,          // operand: u32 slot, u32 rank — stack (top last): value, sub_0, ..., sub_{rank-1}
    StoreElementOuter,     // operand: u32 depth, u32 slot, u32 rank

    // -- Graphics (§13) --
    // Operands flow through the stack; opcodes drive the same shared
    // GraphicsState the tree-walker uses, so output is byte-identical.
    GfxSetBounds,          // operand: u32 rectKind (matches Parser.Ast.GfxRectKind) — stack (top last): left, right, bottom, top
    GfxSetClip,            // stack: onOff (string)
    GfxSetStyle,           // operand: u32 prim (0=point,1=line) — stack: index
    GfxSetColor,           // operand: u32 target (matches GfxColorTarget) — stack: index
    GfxClear,              // CLEAR
    GfxDraw,               // operand: u32 geometry (0=points,1=lines,2=area), u32 count — stack: x0,y0, x1,y1, … (2*count)
    GfxText,               // operand: u32 hasImage (0/1), u32 itemCount — stack: atX, atY, then [text] or [image, items…]
    GfxAskValue,           // operand: u32 query (matches GfxQuery), u32 index — pushes the queried value

    // -- Misc --
    Stop,                  // STOP statement
    End,                   // END statement
    Sleep,                 // SLEEP statement — stack: seconds (numeric); pauses execution
    Nop,
}
