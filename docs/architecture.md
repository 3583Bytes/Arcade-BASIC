# Architecture

Full BASIC is a from-scratch implementation of the ISO/IEC 10279:1991 language, organised as a classic compiler-and-interpreter pipeline plus a stack-based bytecode VM. This document is the orientation map: if you have ten minutes and want to understand how a `.bas` file becomes program output, read this first.

## The pipeline

```
                ┌──────────────────────────────────────────────────────────┐
                │  source.bas                                              │
                └────────────────────────────┬─────────────────────────────┘
                                             │
                                             ▼
                       ┌─────────────────────────────────────────┐
   FullBasic.Lexer ──► │ Tokens (TokenKind + Text + SourceSpan)  │
                       └────────────────────┬────────────────────┘
                                            ▼
                       ┌─────────────────────────────────────────┐
   FullBasic.Parser ─► │ Program (immutable AST: record class    │
                       │ hierarchy under Stmt / Expr / PrintItem)│
                       └────────────────────┬────────────────────┘
                                            ▼
                       ┌─────────────────────────────────────────┐
   FullBasic.Sema ───► │ SemanticInfo: Scope tree + Resolutions  │
                       │ + ExpressionTypes + LineLabels +        │
                       │ DataPool + CallTargets                  │
                       └────────────────────┬────────────────────┘
                                            │
                ┌───────────────────────────┴───────────────────────────┐
                ▼                                                       ▼
   ┌─────────────────────────────────┐         ┌─────────────────────────────────┐
   │ FullBasic.Interpreter           │         │ FullBasic.Compiler              │
   │ Tree-walking interpreter        │         │ AST → Bytecode                  │
   │ FlowControl-typed returns       │         │ (compiles a subset of the       │
   │ Full-feature support            │         │  interpreter's surface)         │
   └────────────────┬────────────────┘         └────────────────┬────────────────┘
                    │                                           │
                    │                                           ▼
                    │                          ┌─────────────────────────────────┐
                    │                          │ FullBasic.Vm                    │
                    │                          │ Stack VM over a Chunk           │
                    │                          └────────────────┬────────────────┘
                    │                                           │
                    │                                           ▼
                    │                          ┌─────────────────────────────────┐
                    │                          │ FullBasic.Cli `build`           │
                    │                          │ Stub + appended payload =       │
                    │                          │ self-extracting native binary   │
                    │                          └─────────────────────────────────┘
                    ▼
   ┌─────────────────────────────────┐
   │ stdout / stdin / files          │
   └─────────────────────────────────┘
```

Three paths come out of the analyzer:

- **`run` path:** tree-walking interpreter, the most feature-complete. Supports the full surface — arrays, MAT, file I/O, exception handling, modules, `PRINT USING`, `INPUT`.
- **`vm`/`build` path:** AST → bytecode → stack VM. Currently a strict subset of the tree-walker (no arrays, MAT, files, exceptions, modules, `PRINT USING`, or `INPUT`). The `build` subcommand appends the serialised bytecode payload to the running CLI binary and chmods it executable.
- **`repl` path:** interactive accumulating session. Each accepted fragment is appended to a growing source buffer; on every turn the whole buffer is re-lexed / parsed / analyzed / executed against a captured `TextWriter`, and only the tail of new output is emitted. Variables and DATA pool state persist because the program runs end-to-end every turn. Implementation lives in `src/FullBasic.Cli/BasicRepl.cs`.

## Project graph

```
FullBasic.Core   ◄── used by everything
     ▲
     │ SourceFile, SourceSpan, DiagnosticBag
     │
FullBasic.Lexer
     ▲
     │ TokenKind, Token
     │
FullBasic.Parser
     ▲ ▲
     │ │ Stmt / Expr / Program AST + AstPrinter
     │ │
     │ FullBasic.Sema
     │      ▲
     │      │ SemanticInfo, Scope, Symbol, ResolvedRef
     │      │
     │      ├─► FullBasic.Interpreter ──► FullBasic.Runtime
     │      │                              (Value, ActivationRecord,
     │      │                               BuiltinImpls, FlowControl,
     │      │                               BasicFile, PictureFormat)
     │      │
     │      └─► FullBasic.Compiler ──► FullBasic.Bytecode (Chunk, Opcode, Serializer)
     │                                 │
     │                                 ▼
     │                                 FullBasic.Vm
     │
     └─► FullBasic.Cli (top-level orchestration, AOT self-extracting stub)
```

`FullBasic.Cli` depends on every other project; everything else avoids cyclic references by sitting under one of the upstream projects.

## Stages, one by one

### FullBasic.Lexer

Hand-rolled, character-by-character tokenizer. Produces `Token` records carrying a `TokenKind`, the original text, and a `SourceSpan`. A few notable quirks:

- **Line labels are first-class tokens.** A leading integer at the start of a logical line is lexed as `TokenKind.LineLabel`, not `NumericLiteral`. This is what lets `100 LET X = 1` survive as a labeled statement.
- **`$`-suffixed identifiers are a distinct kind.** `A$` is `StringIdentifier`, not `Identifier` followed by `$`. Same name letter resolves to a different symbol in a string-vs-numeric pair.
- **`!` and `REM` both start comments.** `!` runs to end-of-line; `REM` keeps the rest of the token text so the parser can preserve the comment payload.
- **Keywords are case-insensitive** (matched via a hashtable in `Keywords.cs`); identifiers are stored case-preserving but compared case-insensitively in sema.

### FullBasic.Parser

Recursive descent. Each statement family has its own `Parse*` method dispatched from `ParseStatement` based on the leading token kind. The output is a `Program` containing an ordered list of `Stmt`s, each potentially carrying:

- A `SourceSpan` (for diagnostics)
- An optional `Label` (the line-number integer prefix, if present)
- Type-specific payload as `IReadOnlyList<...>` of children

Highlights:

- **AST is `abstract record class` + sealed records** — pattern-matching with exhaustiveness in `switch` expressions across `Stmt`, `Expr`, `PrintItem`, `MatStmt`, etc.
- **Single-line vs block `IF`** is disambiguated by whether `THEN` is followed by a newline (block) or a statement (single-line). Single-line `IF` supports `:`-chained statements in both `THEN` and `ELSE`.
- **`:`-separated multi-statements** are parsed at the program/block level; `ParseLabeledStatement` consumes a label and a single statement, then peeks for `:` to continue on the same logical line.
- **Block terminators** (`NEXT`, `END IF`, `LOOP`, `END SELECT`) may carry a leading label, which `ParseStatementBlock` consumes and discards — labels on block terminators don't survive into the AST. Place GOSUB/GOTO targets on a normal statement, not on the terminator.

### FullBasic.Sema

Two-pass analyzer. Output: `SemanticInfo`, which is a bag of:

| Field | Purpose |
|---|---|
| `ProgramScope` | Root `Scope` for the program (frame size, symbols, parent links) |
| `Resolutions` | Map from each `Expr` to a `ResolvedRef` (variable, param, builtin, etc.) |
| `ExpressionTypes` | Static type tag (`Numeric` or `String`) per `Expr` |
| `DataPool` | All `DATA` items in source order |
| `LineLabels` | `int → Stmt` for GOTO/GOSUB/RESTORE resolution |
| `CallTargets` | `CallStmt → SubSymbol` resolved across modules |

**Pass 1** walks the top-level program and collects declarations: `DIM`, `SUB`, `FUNCTION`, `DEF FN`, `MODULE`, `PUBLIC`/`PRIVATE`. Forward references work because everything declared in pass 1 is visible during pass 2.

**Pass 2** walks every statement and expression, attaching resolutions to a side table keyed by `Expr` reference identity. Implicit declarations (a read of a name that isn't declared anywhere) become program-scope variables with a warning (`FB0308`).

**Symbols** are records carrying:

- `Name` and `IsString`
- A `Slot` index (frame-local; allocated by `Scope.AllocateSlot`)
- `OwnerScope` (set via `Scope.Declare`)

For `SUB` / `FUNCTION` / `DEF FN`, the symbol also holds a `BodyScope` and a back-pointer to the declaring `Stmt`.

### FullBasic.Runtime

Domain types shared by both the interpreter and the VM:

- **`Value`** — sealed record class hierarchy: `NumericValue(BigDecimal)`, `StringValue(string)`, `NumericArrayValue`, `StringArrayValue`, `FileHandleValue`.
- **`ActivationRecord`** — frame storage. A `Value?[]` of size `frameSize`, indexed by `Slot`. Carries a `Parent` link for static-link walks (currently only program ↔ inner scope).
- **`FlowControl`** — non-local control-flow signal returned by every statement: `Next`, `Goto`, `Gosub`, `Return`, `Stop`, `End`, `Exit`, `Cause`, `Retry`, `Resume`. Avoids using .NET exceptions for BASIC-level control flow (important because `RETRY`/`CONTINUE` can't unwind through CLR stack frames cleanly).
- **`BuiltinImpls`** — concrete implementations of `SQR`, `SIN`, `LEN`, `MID$`, etc., keyed by name in an `IReadOnlyDictionary`.
- **`BasicFile`** — abstraction over a `Stream` for DISPLAY/INTERNAL/BYTE mode files.
- **`PictureFormat`** — parser and applier for `PRINT USING` picture strings.

### FullBasic.Interpreter

Tree-walking interpreter. The main loop is `ExecuteStatementList(stmts, frame)`: build a label map, walk a pc cursor, dispatch each statement to `ExecStmt`, and react to the returned `FlowControl`:

- `Next` → `pc++`
- `Goto(label)` → jump within this block, or propagate if label isn't local
- `Gosub(label)` → if local, push return PC and jump; otherwise call `RunGosubAtLabel` which runs the subroutine inline against the program-level statement list and returns when its outermost `RETURN` fires
- `Return` → pop the local gosub stack, or propagate (so `RunGosubAtLabel`'s loop can terminate)
- `End` / `Stop` / `Cause` / `Exit` / `Retry` / `Resume` → propagate

`ExecFor`, `ExecDo`, `ExecSelect`, `ExecWhen`, etc., are recursive — they call back into `ExecuteStatementList` for their bodies. The same `ActivationRecord` flows through all of them, so the FOR loop counter is reachable from inside any nested block or DEF FN body via static-link resolution.

### FullBasic.Compiler + FullBasic.Bytecode + FullBasic.Vm

The non-interpreter path. `BasicCompiler.Compile(program, info)` walks the AST and emits a `Chunk` per program-level body, SUB, FUNCTION, and DEF. Each `Chunk` carries:

- A `byte[]` of opcode/operand stream
- A constants pool (`BigDecimal[]`, `string[]`)
- A frame size (max simultaneously-needed slots)

Opcodes are listed in `Opcode.cs`. They split into stack manipulation, constants, variable load/store (with static-link variants for outer scopes), arithmetic/string, comparison/logical/bitwise, control flow, calls, I/O, and a handful of compound ops. The encoding is one byte for the op + LEB128 for operands (small ints) or 4-byte indices for pool entries.

`BasicVm.Run()` is a switch dispatch over the opcode stream. The runtime uses the same `Value` types and `ActivationRecord` as the tree-walker. The VM intentionally rejects features it doesn't support yet (`BasicCompiler.UnsupportedFeatureException`); the CLI's `vm` and `build` commands report this and direct the user to `run` for full support.

### FullBasic.Cli + Phase-10 self-extracting binary

`Program.cs` dispatches sub-commands. The `build` command is the interesting bit:

1. Lex / parse / sema / compile the source.
2. Serialise the `Program` (chunks + constants) into a payload `byte[]`.
3. Read the running CLI binary into memory (the "stub").
4. If the stub already has a payload appended (rebuild), strip it.
5. Append the new payload plus a trailer recording its length + magic.
6. `chmod +x` on POSIX.

When that bundled binary runs, the top of `Main` checks for the trailer via `EmbeddedPayload.TryRead`. If found, it deserialises into a `Program`, hands it to the VM, and bypasses the CLI dispatcher entirely. If not found, it falls through to the normal `Run(args)` path. Same binary, two roles.

## Cross-cutting design choices

### Numeric type: `Singulink.Numerics.BigDecimal`

Arbitrary-precision decimal for every numeric value. No integer fast path — `42` and `42.0` are the same. The PRINT output rounds to 9 significant digits for display (`FormatNumeric` in `BasicInterpreter.Statements.cs`); internal arithmetic keeps full precision.

Why: ISO 10279 says numeric values are decimal, with implementation-defined precision and range. BigDecimal gets us the decimal semantics for free; the runtime cost is acceptable for a teaching/conformance interpreter.

### Strings are codepoint-aware

`LEN`, `MID$`, `LEFT$`, `RIGHT$`, `ORD`, `CHR$` all operate over Unicode codepoints, not C# `char` units. `LEN("π")` is 1; `LEN("😀")` is 1; surrogate pairs count as one codepoint. The underlying storage is C# `string`, but every accessor uses `EnumerateRunes` or equivalent.

This matters because the spec is silent on character set, and "byte position" semantics in MS-BASIC-era code give wrong answers for non-ASCII.

### No .NET exceptions for BASIC-level control flow

BASIC's `GOTO`, `GOSUB`, `EXIT`, `WHEN`/`USE`/`RETRY`/`CONTINUE` are all modelled as `FlowControl` return values from every statement. `RETRY` and `CONTINUE` (resume the IN body) can't be modelled by .NET unwinding semantics, so we don't use exceptions at all for control flow.

`BasicRuntimeException` exists for genuine implementation errors (division by zero, INPUT type mismatch when the stream is exhausted, etc.) and gets converted to `FlowControl.Cause` at the statement boundary so user `WHEN` handlers can catch it.

### Frame storage: slot-indexed activation records

Every scope (`Program`, `Sub`, `Function`, `Def`, `Module`) hands out integer slots from `AllocateSlot()` at sema time. At runtime, `ActivationRecord.Slots[i]` holds the `Value`. Reads/writes of a name compile to `(scope, slot)` pairs; the interpreter walks the static-link chain via `ResolveFrameForScope` to find the correct frame.

This buys us:

- O(1) variable access (no dictionary lookup at runtime)
- Clean separation between local SUB/FUNCTION/DEF frames and the program frame
- A direct mapping to the VM's `LoadLocal`/`LoadOuter` opcodes

### Diagnostics

Everything goes through `DiagnosticBag`. Errors carry a stable code (`FB0001`, `FB0002`, ...) and produce Rust-style snippets with a caret on the offending span. Color is enabled if stderr is a TTY.

The parser collects up to a configurable number of errors per file before giving up; `--strict` would upgrade warnings to errors (not yet wired into the CLI).

## Target frameworks

The library projects (`FullBasic.Core` through `FullBasic.Vm` — everything except the CLI) **multi-target** `net9.0` and `netstandard2.1`. The CLI stays single-target `net9.0` because it uses `Environment.ProcessPath`, `File.SetUnixFileMode`, and AOT publishing — none of which exist on netstandard.

A small `Polyfill.cs` file at the repo root is conditionally compiled into every non-`net5.0+` build (see `Directory.Build.props`) and supplies:

- `System.Runtime.CompilerServices.IsExternalInit` — required for `record` types and `init` accessors
- `System.Runtime.CompilerServices.RequiredMemberAttribute` + `CompilerFeatureRequiredAttribute` and `System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute` — required for the `required` keyword
- `System.Collections.Generic.ReferenceEqualityComparer` — used by the analyzer's reference-keyed side tables

The libs are deliberately framework-thin: no `System.Text.Rune` (rewritten as surrogate-pair iteration over `string`), no `Stream.ReadExactly` (replaced by a small loop), no `Math.Log2` (uses `Math.Log(x, 2)`), no `MemoryExtensions.Contains` (uses `IndexOf` over `ReadOnlySpan`). This lets the same DLLs ship to Unity (Mono/IL2CPP), Xamarin, older .NET Framework hosts, and any other netstandard2.1 consumer.

`Singulink.Numerics.BigDecimal` (v3) ships netstandard2.1 targets, so the BigDecimal-heavy runtime works unchanged.

## Where to look next

- **Code entry points:** `src/FullBasic.Lexer/Lexer.cs` for tokenizing, `src/FullBasic.Parser/BasicParser.cs` for `ParseStatement` dispatch, `src/FullBasic.Sema/Analyzer.cs` for the two-pass analyzer, `src/FullBasic.Interpreter/BasicInterpreter.cs` for the `ExecuteStatementList` loop.
- **Adding things:** see [`CONTRIBUTING.md`](../CONTRIBUTING.md) for "how to add a builtin / statement / opcode" recipes.
- **What's spec-compliant vs implementation-defined:** see [`conformance.md`](conformance.md).
