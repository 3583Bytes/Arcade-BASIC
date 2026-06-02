# Architecture

Arcade BASIC is a from-scratch implementation of the ISO/IEC 10279:1991 language, organised as a classic compiler-and-interpreter pipeline plus a stack-based bytecode VM. This document is the orientation map: if you have ten minutes and want to understand how a `.bas` file becomes program output, read this first.

## The pipeline

```
                ┌──────────────────────────────────────────────────────────┐
                │  source.bas                                              │
                └────────────────────────────┬─────────────────────────────┘
                                             │
                                             ▼
                       ┌─────────────────────────────────────────┐
   ArcadeBasic.Lexer ──► │ Tokens (TokenKind + Text + SourceSpan)  │
                       └────────────────────┬────────────────────┘
                                            ▼
                       ┌─────────────────────────────────────────┐
   ArcadeBasic.Parser ─► │ Program (immutable AST: record class    │
                       │ hierarchy under Stmt / Expr / PrintItem)│
                       └────────────────────┬────────────────────┘
                                            ▼
                       ┌─────────────────────────────────────────┐
   ArcadeBasic.Sema ───► │ SemanticInfo: Scope tree + Resolutions  │
                       │ + ExpressionTypes + LineLabels +        │
                       │ DataPool + CallTargets                  │
                       └────────────────────┬────────────────────┘
                                            │
                ┌───────────────────────────┴───────────────────────────┐
                ▼                                                       ▼
   ┌─────────────────────────────────┐         ┌─────────────────────────────────┐
   │ ArcadeBasic.Interpreter           │         │ ArcadeBasic.Compiler              │
   │ Tree-walking interpreter        │         │ AST → Bytecode                  │
   │ FlowControl-typed returns       │         │ (compiles a subset of the       │
   │ Full-feature support            │         │  interpreter's surface)         │
   └────────────────┬────────────────┘         └────────────────┬────────────────┘
                    │                                           │
                    │                                           ▼
                    │                          ┌─────────────────────────────────┐
                    │                          │ ArcadeBasic.Vm                    │
                    │                          │ Stack VM over a Chunk           │
                    │                          └────────────────┬────────────────┘
                    │                                           │
                    │                                           ▼
                    │                          ┌─────────────────────────────────┐
                    │                          │ ArcadeBasic.Cli `build`           │
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
- **`vm`/`build` path:** AST → bytecode → stack VM. Matches the tree-walker byte-for-byte on every example program in the repo. The compiler does deferred backfill for forward `GOTO`/`GOSUB` across statement boundaries, `PRINT TAB(n)` is its own opcode, `CallDef` is fully wired, and nested MAT constants resolve their shape from the assignment target via `MatPushConst`. The `build` subcommand appends the serialised bytecode payload to the running CLI binary and chmods it executable.
- **`repl` path:** interactive accumulating session. Each accepted fragment is appended to a growing source buffer; on every turn the whole buffer is re-lexed / parsed / analyzed / executed against a captured `TextWriter`, and only the tail of new output is emitted. Variables and DATA pool state persist because the program runs end-to-end every turn. Implementation lives in `src/ArcadeBasic.Cli/BasicRepl.cs`.

## Project graph

```
ArcadeBasic.Core   ◄── used by everything
     ▲
     │ SourceFile, SourceSpan, DiagnosticBag
     │
ArcadeBasic.Lexer
     ▲
     │ TokenKind, Token
     │
ArcadeBasic.Parser
     ▲ ▲
     │ │ Stmt / Expr / Program AST + AstPrinter
     │ │
     │ ArcadeBasic.Sema
     │      ▲
     │      │ SemanticInfo, Scope, Symbol, ResolvedRef
     │      │
     │      ├─► ArcadeBasic.Interpreter ──► ArcadeBasic.Runtime
     │      │                              (Value, ActivationRecord,
     │      │                               BuiltinImpls, FlowControl,
     │      │                               BasicFile, PictureFormat)
     │      │
     │      └─► ArcadeBasic.Compiler ──► ArcadeBasic.Bytecode (Chunk, Opcode, Serializer)
     │                                 │
     │                                 ▼
     │                                 ArcadeBasic.Vm
     │
     └─► ArcadeBasic.Cli (top-level orchestration, AOT self-extracting stub)
```

`ArcadeBasic.Cli` depends on every other project; everything else avoids cyclic references by sitting under one of the upstream projects.

## Stages, one by one

### ArcadeBasic.Lexer

Hand-rolled, character-by-character tokenizer. Produces `Token` records carrying a `TokenKind`, the original text, and a `SourceSpan`. A few notable quirks:

- **Line labels are first-class tokens.** A leading integer at the start of a logical line is lexed as `TokenKind.LineLabel`, not `NumericLiteral`. This is what lets `100 LET X = 1` survive as a labeled statement.
- **`$`-suffixed identifiers are a distinct kind.** `A$` is `StringIdentifier`, not `Identifier` followed by `$`. Same name letter resolves to a different symbol in a string-vs-numeric pair.
- **`!` and `REM` both start comments.** `!` runs to end-of-line; `REM` keeps the rest of the token text so the parser can preserve the comment payload.
- **Keywords are case-insensitive** (matched via a hashtable in `Keywords.cs`); identifiers are stored case-preserving but compared case-insensitively in sema.

### ArcadeBasic.Parser

Recursive descent. Each statement family has its own `Parse*` method dispatched from `ParseStatement` based on the leading token kind. The output is a `Program` containing an ordered list of `Stmt`s, each potentially carrying:

- A `SourceSpan` (for diagnostics)
- An optional `Label` (the line-number integer prefix, if present)
- Type-specific payload as `IReadOnlyList<...>` of children

Highlights:

- **AST is `abstract record class` + sealed records** — pattern-matching with exhaustiveness in `switch` expressions across `Stmt`, `Expr`, `PrintItem`, `MatStmt`, etc.
- **Single-line vs block `IF`** is disambiguated by whether `THEN` is followed by a newline (block) or a statement (single-line). Single-line `IF` supports `:`-chained statements in both `THEN` and `ELSE`.
- **`:`-separated multi-statements** are parsed at the program/block level; `ParseLabeledStatement` consumes a label and a single statement, then peeks for `:` to continue on the same logical line.
- **Block terminators** (`NEXT`, `END IF`, `LOOP`, `END SELECT`) may carry a leading label. `ParseStatementBlock` preserves it as a labeled no-op (`RemStmt`) appended to the end of the block body, so the label survives as a valid GOTO/GOSUB target — a jump lands at the end of the body and then the loop's increment/test runs (`NEXT`/`LOOP`) or control falls past the block (`END IF`/`END SELECT`). This is the same effect as the older `<label> REM` workaround, applied automatically.

### ArcadeBasic.Sema

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

### ArcadeBasic.Runtime

Domain types shared by both the interpreter and the VM:

- **`Value`** — sealed record class hierarchy: `NumericValue(BigDecimal)`, `StringValue(string)`, `NumericArrayValue`, `StringArrayValue`, `FileHandleValue`.
- **`ActivationRecord`** — frame storage. A `Value?[]` of size `frameSize`, indexed by `Slot`. Carries a `Parent` link for static-link walks (currently only program ↔ inner scope).
- **`FlowControl`** — non-local control-flow signal returned by every statement: `Next`, `Goto`, `Gosub`, `Return`, `Stop`, `End`, `Exit`, `Cause`, `Retry`, `Resume`. Avoids using .NET exceptions for BASIC-level control flow (important because `RETRY`/`CONTINUE` can't unwind through CLR stack frames cleanly).
- **`BuiltinImpls`** — concrete implementations of `SQR`, `SIN`, `LEN`, `MID$`, etc., keyed by name in an `IReadOnlyDictionary`.
- **`BasicFile`** — abstraction over a `Stream` for DISPLAY/INTERNAL/BYTE mode files.
- **`PictureFormat`** — parser and applier for `PRINT USING` picture strings.
- **§13 graphics core** — `GraphicsState` (window/viewport/clip + coordinate mapping and Cohen–Sutherland / Sutherland–Hodgman clipping), `Rasterizer` (Bresenham + scanline fill), and the `IGraphicsDevice` seam with backends `NullGraphicsDevice`, `RecordingGraphicsDevice` (tests), `AnsiGraphicsDevice` (Braille + ANSI terminal). See *device seams* below.
- **`IKeyboard`** — the seam behind `INKEY$` (non-blocking key poll); `NullKeyboard` is the default.

### ArcadeBasic.Interpreter

Tree-walking interpreter. The main loop is `ExecuteStatementList(stmts, frame)`: build a label map, walk a pc cursor, dispatch each statement to `ExecStmt`, and react to the returned `FlowControl`:

- `Next` → `pc++`
- `Goto(label)` → jump within this block, or propagate if label isn't local
- `Gosub(label)` → if local, push return PC and jump; otherwise call `RunGosubAtLabel` which runs the subroutine inline against the program-level statement list and returns when its outermost `RETURN` fires
- `Return` → pop the local gosub stack, or propagate (so `RunGosubAtLabel`'s loop can terminate)
- `End` / `Stop` / `Cause` / `Exit` / `Retry` / `Resume` → propagate

`ExecFor`, `ExecDo`, `ExecSelect`, `ExecWhen`, etc., are recursive — they call back into `ExecuteStatementList` for their bodies. The same `ActivationRecord` flows through all of them, so the FOR loop counter is reachable from inside any nested block or DEF FN body via static-link resolution.

### ArcadeBasic.Compiler + ArcadeBasic.Bytecode + ArcadeBasic.Vm

The non-interpreter path. `BasicCompiler.Compile(program, info)` walks the AST and emits a `Chunk` per program-level body, SUB, FUNCTION, and DEF. Each `Chunk` carries:

- A `byte[]` of opcode/operand stream
- A constants pool (`BigDecimal[]`, `string[]`)
- A frame size (max simultaneously-needed slots)

Opcodes are listed in `Opcode.cs`. They split into stack manipulation, constants, variable load/store (with static-link variants for outer scopes), arithmetic/string, comparison/logical/bitwise, control flow, calls, I/O, and a handful of compound ops. The encoding is one byte for the op + LEB128 for operands (small ints) or 4-byte indices for pool entries.

`BasicVm.Run()` is a switch dispatch over the opcode stream. The runtime uses the same `Value` types and `ActivationRecord` as the tree-walker, and the shared `MatOps` / `DisplayFormat` / `PictureFormat` / `ChannelTable` / `BuiltinImpls` helpers in `ArcadeBasic.Runtime` so the two engines produce byte-identical output on every program. The remaining `BasicCompiler.UnsupportedFeatureException` sites are defensive fallbacks for sema-invariants (e.g. "MAT target is not a known array") that well-formed input never reaches.

### ArcadeBasic.Cli + Phase-10 self-extracting binary

`Program.cs` dispatches sub-commands. The `build` command is the interesting bit:

1. Lex / parse / sema / compile the source.
2. Serialise the `Program` (chunks + constants) into a payload `byte[]`.
3. Read the running CLI binary into memory (the "stub").
4. If the stub already has a payload appended (rebuild), strip it.
5. Append the new payload plus a trailer recording its length + magic.
6. `chmod +x` on POSIX.

When that bundled binary runs, the top of `Main` checks for the trailer via `EmbeddedPayload.TryRead`. If found, it deserialises into a `Program`, hands it to the VM, and bypasses the CLI dispatcher entirely. If not found, it falls through to the normal `Run(args)` path. Same binary, two roles.

See [**standalone-builds.md**](standalone-builds.md) for a deeper write-up: the anatomy of a bundled binary, the build/startup pipelines step-by-step, tradeoffs vs. true native codegen, and comparisons to related approaches (PyInstaller, Electron, AppImage).

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

### Device seams: graphics and keyboard

The §13 graphics module and `INKEY$` both reach the outside world through small
interfaces injected into *both* engines, so all the device-independent logic
lives once in `ArcadeBasic.Runtime` and the interpreter and VM stay byte-for-byte
identical (the same pattern as `MatOps`/`PictureFormat`).

- **`IGraphicsDevice`** — the core (`GraphicsState`) does all coordinate mapping
  and clipping, then hands the backend already-clipped vector primitives in the
  normalized `[0,1]` device square. Backends: `NullGraphicsDevice` (default),
  `SvgGraphicsDevice` (`--svg`), `AnsiGraphicsDevice` (Braille + ANSI terminal —
  CLI/standalone), and the IDE's `TuiGraphicsDevice` (Terminal.Gui canvas).
  `RecordingGraphicsDevice` captures the primitive stream so the conformance
  suite can assert interpreter == VM. `SLEEP` calls `Flush()`, which is the frame
  boundary that makes the terminal backends present each frame of a game loop.
- **`IKeyboard`** — a non-blocking `ReadKey()` behind `INKEY$`; `ConsoleKeyboard`
  (CLI/standalone, via `Console.ReadKey`) and `TuiKeyboard` (the IDE captures keys
  with a global hook while a program runs) supply real keys, `NullKeyboard`
  yields `""` when there's no interactive keyboard.

Both interfaces are netstandard2.1- and IL2CPP-safe (no reflection, only simple
value types crossing the boundary), so the same code path serves the CLI,
standalone binaries, the IDE, and a future Unity backend.

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

The library projects (`ArcadeBasic.Core` through `ArcadeBasic.Vm` — everything except the CLI) **multi-target** `net9.0` and `netstandard2.1`. The CLI stays single-target `net9.0` because it uses `Environment.ProcessPath`, `File.SetUnixFileMode`, and AOT publishing — none of which exist on netstandard.

A small `Polyfill.cs` file at the repo root is conditionally compiled into every non-`net5.0+` build (see `Directory.Build.props`) and supplies:

- `System.Runtime.CompilerServices.IsExternalInit` — required for `record` types and `init` accessors
- `System.Runtime.CompilerServices.RequiredMemberAttribute` + `CompilerFeatureRequiredAttribute` and `System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute` — required for the `required` keyword
- `System.Collections.Generic.ReferenceEqualityComparer` — used by the analyzer's reference-keyed side tables

The libs are deliberately framework-thin: no `System.Text.Rune` (rewritten as surrogate-pair iteration over `string`), no `Stream.ReadExactly` (replaced by a small loop), no `Math.Log2` (uses `Math.Log(x, 2)`), no `MemoryExtensions.Contains` (uses `IndexOf` over `ReadOnlySpan`). This lets the same DLLs ship to Unity (Mono/IL2CPP), Xamarin, older .NET Framework hosts, and any other netstandard2.1 consumer.

`Singulink.Numerics.BigDecimal` (v3) ships netstandard2.1 targets, so the BigDecimal-heavy runtime works unchanged.

## Where to look next

- **Code entry points:** `src/ArcadeBasic.Lexer/Lexer.cs` for tokenizing, `src/ArcadeBasic.Parser/BasicParser.cs` for `ParseStatement` dispatch, `src/ArcadeBasic.Sema/Analyzer.cs` for the two-pass analyzer, `src/ArcadeBasic.Interpreter/BasicInterpreter.cs` for the `ExecuteStatementList` loop.
- **Adding things:** see [`CONTRIBUTING.md`](../CONTRIBUTING.md) for "how to add a builtin / statement / opcode" recipes.
- **What's spec-compliant vs implementation-defined:** see [`conformance.md`](conformance.md).
