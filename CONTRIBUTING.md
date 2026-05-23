# Contributing to Full BASIC

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
dotnet test test/FullBasic.Parser.Tests

# Invoke the CLI on a source file.
dotnet run --project src/FullBasic.Cli -- run examples/hello.bas

# Inspect intermediate stages.
dotnet run --project src/FullBasic.Cli -- lex     examples/factorial.bas
dotnet run --project src/FullBasic.Cli -- parse   examples/factorial.bas
dotnet run --project src/FullBasic.Cli -- analyze examples/factorial.bas

# AOT-publish.
dotnet publish src/FullBasic.Cli -c Release /p:PublishAot=true
# → publish/aot/FullBasic.Cli
```

Release builds set `TreatWarningsAsErrors=true`, so anything that builds cleanly in Debug must also be warning-free for CI.

## Project layout

```
src/
  FullBasic.Core/          # SourceFile, Position, Diagnostic
  FullBasic.Lexer/         # Tokens, keyword table
  FullBasic.Parser/        # Recursive-descent parser, AST (record class hierarchy)
  FullBasic.Sema/          # Two-pass analyzer, Scope, Symbol, SemanticInfo
  FullBasic.Runtime/       # Value, ActivationRecord, FlowControl, BuiltinImpls, BasicFile
  FullBasic.Interpreter/   # Tree-walking interpreter (feature-complete)
  FullBasic.Bytecode/      # Opcode enum, Chunk, BytecodeSerializer
  FullBasic.Compiler/      # AST → bytecode lowering (subset of interpreter)
  FullBasic.Vm/            # Stack-based bytecode VM
  FullBasic.Cli/           # Command dispatch + self-extracting AOT stub

test/
  FullBasic.{Lexer,Parser,Sema,Interpreter,Vm,Runtime,Conformance}.Tests/

examples/                  # Sample programs runnable via `full-basic run`
testdata/{conformance,golden}/

docs/
  architecture.md          # Pipeline + per-project tour
  conformance.md           # ISO 10279 deviations
```

`Directory.Build.props` sets `Nullable=enable`, `LangVersion=13`, `AnalysisLevel=latest`, and `InternalsVisibleTo` from each source project to its sibling test project. So sema/interpreter internals are visible from tests without having to make them `public`.

## Common recipes

### Add a built-in function

Two places to touch:

1. **`src/FullBasic.Sema/Builtins.cs`** — register the name, return type, and argument types. The `BuiltinSignature` records min/max arity and each argument's expected type tag (`Numeric` / `String` / `Any`).

   ```csharp
   yield return new BuiltinSymbol("ATAN2", IsString: false,
       new BuiltinSignature(2, 2,
           [BuiltinArgType.Numeric, BuiltinArgType.Numeric]));
   ```

2. **`src/FullBasic.Runtime/BuiltinImpls.cs`** — add the runtime implementation. The dictionary key is the name (case-insensitive lookup); the value is `Value[] -> Value`.

   ```csharp
   t["ATAN2"] = args =>
       Num(FromDouble(Math.Atan2(ToDouble(args[0]), ToDouble(args[1]))));
   ```

3. **Test** in `test/FullBasic.Interpreter.Tests/InterpreterTests.cs`. Each test typically runs a tiny `.bas` snippet via `Run("PRINT ATAN2(1, 1)")` and asserts on the trimmed stdout.

That's it — no parser changes needed. The parser already recognises `Identifier(args)` as a generic call form; sema dispatches based on what the name resolves to.

### Add a statement keyword

This is the heaviest extension. Six touch points:

1. **`src/FullBasic.Lexer/TokenKind.cs`** — add a new enum entry, e.g. `KwSwap`.
2. **`src/FullBasic.Lexer/Keywords.cs`** — wire the spelling to the token kind.
3. **`src/FullBasic.Parser/Ast/Stmt.cs`** — define a new `sealed record class SwapStmt(...) : Stmt(Span)`.
4. **`src/FullBasic.Parser/BasicParser.cs`** — add a `Parse*` method and add its dispatch entry in `ParseStatement`:
   ```csharp
   TokenKind.KwSwap => ParseSwap(),
   ```
5. **`src/FullBasic.Interpreter/BasicInterpreter.cs`** (`ExecStmtImpl` switch) and possibly `BasicInterpreter.Statements.cs` — add an `ExecSwap` handler returning `FlowControl`.
6. **Tests** under `test/FullBasic.Parser.Tests` (round-trip the AST) and `test/FullBasic.Interpreter.Tests` (observe the effect).

If you want VM coverage, add an `Opcode.Swap` (already exists for the stack op — pick a different name for your statement), extend `BasicCompiler.cs` to lower the AST node, and extend `BasicVm.cs` to dispatch.

### Add a bytecode opcode

1. **`src/FullBasic.Bytecode/Opcode.cs`** — add the enum entry in the appropriate section (stack / arithmetic / control flow / etc.).
2. **`src/FullBasic.Vm/BasicVm.cs`** — add a `case Opcode.X:` to the dispatch loop. Push/pop operands explicitly; the VM's stack is `Stack<Value>`.
3. **`src/FullBasic.Compiler/BasicCompiler.cs`** — emit the opcode from the relevant AST lowering path.
4. **Test** in `test/FullBasic.Vm.Tests/`. Tests compile a snippet, run it on the VM, and assert on captured output.

If the opcode takes operands, encode them with the existing LEB128 helpers in `BytecodeSerializer.cs` and document the encoding in the comment beside the enum value.

### Add an example program

1. Drop the `.bas` file in `examples/`.
2. Add a row to the table in `examples/README.md`.
3. If it's a notable program (Star Trek / Lunar Lander tier), also add a one-liner to the "Running the example programs" section of the top-level `README.md`.

Programs must work via `full-basic run`. If they also work on the VM, mark them ✓ in the matrix; otherwise leave the VM column as `—`.

## Debugging tips

- `lex <file>` prints the token stream — useful when a parser error doesn't match what you typed.
- `parse <file>` pretty-prints the AST — useful when sema or the interpreter does something surprising.
- `analyze <file>` shows the program-scope symbol table, DATA pool, and line label map.
- All diagnostics carry a stable code (`FB0xxx`). Grep `src/FullBasic.Sema/Analyzer.cs` for the code to find where it's raised.
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

Pull requests for features should land tests in the same PR. Pull requests for bug fixes should include a regression test that fails before the fix and passes after. Sema/interpreter changes should also run the example programs in `examples/` end-to-end — they're the integration coverage we don't have a CI job for yet.
