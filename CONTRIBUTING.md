# Contributing to Arcade BASIC

Thanks for picking this up. This document covers the developer loop, project layout, and concrete recipes for the most common changes you'll want to make.

If you haven't already, skim [`docs/architecture.md`](docs/architecture.md) first — it explains the pipeline that the recipes below plug into.

## Prerequisites

- **.NET 9 SDK** (`dotnet --version` ≥ 9.0).
- An editor that understands C# 13 + nullable reference types. Rider, VS, or VS Code + C# Dev Kit all work.
- The repo is plain Git; no submodules, no LFS.

## The developer loop

```sh
# One-shot build (Debug).
dotnet build

# Run the entire test suite. ~1–2 s; runs in parallel.
dotnet test

# Run a single test project for fast iteration.
dotnet test test/ArcadeBasic.Parser.Tests

# Invoke the CLI on a source file.
dotnet run --project src/ArcadeBasic.Cli -- run examples/hello.bas

# Interactive REPL — fastest loop for trying out a one-liner.
dotnet run --project src/ArcadeBasic.Cli -- repl

# Inspect intermediate stages.
dotnet run --project src/ArcadeBasic.Cli -- lex     examples/factorial.bas
dotnet run --project src/ArcadeBasic.Cli -- parse   examples/factorial.bas
dotnet run --project src/ArcadeBasic.Cli -- analyze examples/factorial.bas

# AOT-publish.
dotnet publish src/ArcadeBasic.Cli -c Release -r <rid>
# → src/ArcadeBasic.Cli/bin/Release/net9.0/<rid>/publish/arcade-basic
```

Release builds set `TreatWarningsAsErrors=true`, so anything that builds cleanly in Debug must also be warning-free for CI.

## Project layout

```
src/
  ArcadeBasic.Core/          # SourceFile, Position, Diagnostic
  ArcadeBasic.Lexer/         # Tokens, keyword table
  ArcadeBasic.Parser/        # Recursive-descent parser, AST (record class hierarchy)
  ArcadeBasic.Sema/          # Two-pass analyzer, Scope, Symbol, SemanticInfo
  ArcadeBasic.Runtime/       # Value, ActivationRecord, FlowControl, BuiltinImpls, BasicFile
  ArcadeBasic.Interpreter/   # Tree-walking interpreter (feature-complete)
  ArcadeBasic.Bytecode/      # Opcode enum, Chunk, BytecodeSerializer
  ArcadeBasic.Compiler/      # AST → bytecode lowering (feature-parity with interpreter)
  ArcadeBasic.Vm/            # Stack-based bytecode VM
  ArcadeBasic.Cli/           # Command dispatch + self-extracting AOT stub

test/
  ArcadeBasic.{Lexer,Parser,Sema,Interpreter,Vm,Runtime,Conformance}.Tests/

examples/                  # Sample programs runnable via `arcade-basic run`
testdata/{conformance,golden}/

docs/
  architecture.md          # Pipeline + per-project tour
  conformance.md           # ISO 10279 deviations
```

`Directory.Build.props` sets `Nullable=enable`, `LangVersion=13`, `AnalysisLevel=latest`, and `InternalsVisibleTo` from each source project to its sibling test project. So sema/interpreter internals are visible from tests without having to make them `public`.

### Target frameworks

The library projects multi-target `net9.0;netstandard2.1`. The CLI is single-target `net9.0` (it uses APIs that only exist on .NET 6+). Test projects are single-target `net9.0`.

When adding code to a library project, prefer APIs that exist on both targets. If you need a `net9.0`-only API, either:

- Wrap the call in `#if NET5_0_OR_GREATER` (or a more specific version guard) with a netstandard fallback, **or**
- Push the call out into `ArcadeBasic.Cli`, where it's free to use anything.

The repo-root `Polyfill.cs` is included automatically into any non-`net5.0+` build (see `Directory.Build.props`). It supplies `IsExternalInit`, `RequiredMemberAttribute`, `CompilerFeatureRequiredAttribute`, `SetsRequiredMembersAttribute`, and `ReferenceEqualityComparer`. Add to it if you find another `required`/`init`/record-related feature that doesn't compile on netstandard.

## Common recipes

### Add a built-in function

Two places to touch:

1. **`src/ArcadeBasic.Sema/Builtins.cs`** — register the name, return type, and argument types. The `BuiltinSignature` records min/max arity and each argument's expected type tag (`Numeric` / `String` / `Any`).

   ```csharp
   yield return new BuiltinSymbol("ATAN2", IsString: false,
       new BuiltinSignature(2, 2,
           [BuiltinArgType.Numeric, BuiltinArgType.Numeric]));
   ```

2. **`src/ArcadeBasic.Runtime/BuiltinImpls.cs`** — add the runtime implementation. The dictionary key is the name (case-insensitive lookup); the value is `Value[] -> Value`.

   ```csharp
   t["ATAN2"] = args =>
       Num(FromDouble(Math.Atan2(ToDouble(args[0]), ToDouble(args[1]))));
   ```

3. **Test** in `test/ArcadeBasic.Interpreter.Tests/InterpreterTests.cs`. Each test typically runs a tiny `.bas` snippet via `Run("PRINT ATAN2(1, 1)")` and asserts on the trimmed stdout.

That's it — no parser changes needed. The parser already recognises `Identifier(args)` as a generic call form; sema dispatches based on what the name resolves to.

### Add a statement keyword

This is the heaviest extension. Six touch points:

1. **`src/ArcadeBasic.Lexer/TokenKind.cs`** — add a new enum entry, e.g. `KwSwap`.
2. **`src/ArcadeBasic.Lexer/Keywords.cs`** — wire the spelling to the token kind.
3. **`src/ArcadeBasic.Parser/Ast/Stmt.cs`** — define a new `sealed record class SwapStmt(...) : Stmt(Span)`.
4. **`src/ArcadeBasic.Parser/BasicParser.cs`** — add a `Parse*` method and add its dispatch entry in `ParseStatement`:
   ```csharp
   TokenKind.KwSwap => ParseSwap(),
   ```
5. **`src/ArcadeBasic.Interpreter/BasicInterpreter.cs`** (`ExecStmtImpl` switch) and possibly `BasicInterpreter.Statements.cs` — add an `ExecSwap` handler returning `FlowControl`.
6. **Tests** under `test/ArcadeBasic.Parser.Tests` (round-trip the AST) and `test/ArcadeBasic.Interpreter.Tests` (observe the effect).

If you want VM coverage, add an `Opcode.Swap` (already exists for the stack op — pick a different name for your statement), extend `BasicCompiler.cs` to lower the AST node, and extend `BasicVm.cs` to dispatch.

### Add a bytecode opcode

1. **`src/ArcadeBasic.Bytecode/Opcode.cs`** — add the enum entry in the appropriate section (stack / arithmetic / control flow / etc.).
2. **`src/ArcadeBasic.Vm/BasicVm.cs`** — add a `case Opcode.X:` to the dispatch loop. Push/pop operands explicitly; the VM's stack is `Stack<Value>`.
3. **`src/ArcadeBasic.Compiler/BasicCompiler.cs`** — emit the opcode from the relevant AST lowering path.
4. **Test** in `test/ArcadeBasic.Vm.Tests/`. Tests compile a snippet, run it on the VM, and assert on captured output.

If the opcode takes operands, encode them with the existing LEB128 helpers in `BytecodeSerializer.cs` and document the encoding in the comment beside the enum value.

### Add an example program

1. Drop the `.bas` file in `examples/`.
2. Add a row to the table in `examples/README.md`.
3. If it's a notable program (Star Trek / Lunar Lander tier), also add a one-liner to the "Running the example programs" section of the top-level `README.md`.

Programs must work via `arcade-basic run`. If they also work on the VM, mark them ✓ in the matrix; otherwise leave the VM column as `—`.

## Debugging tips

- `lex <file>` prints the token stream — useful when a parser error doesn't match what you typed.
- `parse <file>` pretty-prints the AST — useful when sema or the interpreter does something surprising.
- `analyze <file>` shows the program-scope symbol table, DATA pool, and line label map.
- `repl` is the fastest loop for trying a one-line conjecture without touching disk. Multi-line blocks (FOR/DO/IF/SUB/...) are accepted; `.list` shows the accumulated session, `.clear` resets it.
- All diagnostics carry a stable code (`FB0xxx`). Grep `src/ArcadeBasic.Sema/Analyzer.cs` for the code to find where it's raised.
- For interpreter behaviour, sprinkle `Console.Error.WriteLine(...)` in `ExecStmtImpl` or the relevant `Exec*` helper. Interpreter output goes to a configurable `TextWriter`, but stderr is separate and won't pollute test goldens.
- Most tests use `Run(source)` helpers that pipe stdin and capture stdout — see `InterpreterTests.cs` for the pattern.

## Coding conventions

- **C# 13, nullable enabled, implicit usings.** Don't `using System;` — it's already there.
- **Records over classes** for data carriers (AST nodes, values, symbols). `sealed record class` for closed hierarchies.
- **Pattern matching over `if/is` chains.** Most dispatch is a `switch` expression or pattern-matching `switch` statement.
- **No comments that restate the code.** Comments explain *why*, not *what*. The codebase aims for self-explanatory names and lets surprising decisions earn their `//` line.
- **Tests use xUnit + FluentAssertions + Verify.** Goldens go through Verify; one-off assertions use `output.Should().Contain(...)` etc.
- **Diagnostic codes are stable.** When you add an error path, assign a new `FB0xxx` constant in the appropriate file and grep the codebase to make sure it's unique.
- **Public surface stays small.** `internal` by default, with `[InternalsVisibleTo]` for tests handled in `Directory.Build.props`.

## Reporting bugs / suggesting changes

For a behaviour question, the most useful issue includes:

- The minimal `.bas` snippet that reproduces the problem.
- What `dotnet run -- run <file>` actually prints.
- What you expected, with a citation to ISO 10279 if it's a spec-conformance question.

Pull requests for features should land tests in the same PR. Pull requests for bug fixes should include a regression test that fails before the fix and passes after. The example programs in `examples/` are integration coverage: `ArcadeBasic.Conformance.Tests` runs each one through both engines and asserts the tree-walker and the bytecode VM agree byte-for-byte (deterministic examples) and that every example compiles on the VM, and CI smoke-runs them through both `run` and `vm`. If you add a statement or builtin, add an example or extend an existing one so that parity net covers it.
