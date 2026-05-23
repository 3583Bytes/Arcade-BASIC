# Full BASIC

[![CI](https://github.com/OWNER/REPO/actions/workflows/ci.yml/badge.svg)](https://github.com/OWNER/REPO/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/OWNER/REPO?label=release)](https://github.com/OWNER/REPO/releases/latest)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![Spec: ISO 10279](https://img.shields.io/badge/spec-ISO%2010279%3A1991-blue)](https://www.iso.org/standard/18305.html)

> Replace `OWNER/REPO` in the badges above with your GitHub `owner/repo` once the project is pushed.

A Full BASIC (ISO/IEC 10279:1991, ANSI X3.113-1987) interpreter and compiler in C#.

## Documentation

- [**Architecture**](docs/architecture.md) — the pipeline, project graph, key data structures.
- [**Contributing**](CONTRIBUTING.md) — build/test loop and concrete recipes for adding builtins, statements, opcodes.
- [**Conformance**](docs/conformance.md) — known deviations from ISO 10279:1991 and implementation-defined choices.
- [**Examples**](examples/README.md) — sample programs with a feature matrix across tree-walker and bytecode VM.

## Status

Three execution paths land a Full BASIC program in different forms:

- **`full-basic run <file>`** — tree-walking interpreter. The most complete front end: arrays, MAT, file I/O, exception handling, modules, `PRINT USING`, `INPUT`.
- **`full-basic vm <file>`** — compile to stack bytecode and run on the VM. Currently a subset of the tree-walker (no arrays/MAT, no file I/O, no exceptions, no modules, no `PRINT USING`, no `INPUT`).
- **`full-basic build <file> [-o out]`** — bundle the VM and the compiled program into a single self-contained native binary (Phase 10, Path E). Same feature subset as `vm`.

Pipeline phases 0–10 are all merged. Optional Phase 8 modules are partial: `PRINT USING` (8a) is done; graphics + picture (SVG backend) and fixed-decimal are not.

See [`examples/README.md`](examples/README.md) for a feature matrix across the sample programs.

## Build

Requires .NET 9 SDK.

```sh
dotnet build                                       # debug build
dotnet test                                        # all unit + integration tests
dotnet run --project src/FullBasic.Cli -- run <f>  # invoke the CLI

# AOT-publish a standalone CLI for the current platform
dotnet publish src/FullBasic.Cli -c Release /p:PublishAot=true
# → ./publish/aot/FullBasic.Cli
```

## CLI

```
full-basic <command> [args]

  lex <file>              tokenize and print the token stream
  parse <file>            lex + parse, pretty-print the AST
  analyze <file>          lex + parse + sema, print symbol/DATA summary
  run <file> [mod ...]    tree-walking interpreter (most complete)
  vm <file>               compile to bytecode and run on the VM (subset)
  build <file> [-o out]   produce a self-contained native binary
  --version
  --bigdecimal-spike      BigDecimal AOT smoke test
```

## Running the example programs

The 13 sample programs in [`examples/`](examples/) exercise different parts of the language. Pick one and run it through any of the three execution paths:

```sh
# Tree-walking interpreter — supports every example
dotnet run --project src/FullBasic.Cli -- run examples/hello.bas
dotnet run --project src/FullBasic.Cli -- run examples/factorial.bas
dotnet run --project src/FullBasic.Cli -- run examples/matrix.bas
dotnet run --project src/FullBasic.Cli -- run examples/exception.bas
dotnet run --project src/FullBasic.Cli -- run examples/modules.bas
dotnet run --project src/FullBasic.Cli -- run examples/guess.bas    # reads from stdin
dotnet run --project src/FullBasic.Cli -- run examples/startrek.bas # Super Star Trek (Ahl 1978)
dotnet run --project src/FullBasic.Cli -- run examples/lunar.bas    # Lunar Lander (Storer 1969)

# Bytecode VM — only the examples marked ✓ in examples/README.md
dotnet run --project src/FullBasic.Cli -- vm examples/hello.bas
dotnet run --project src/FullBasic.Cli -- vm examples/factorial.bas
dotnet run --project src/FullBasic.Cli -- vm examples/strings.bas
dotnet run --project src/FullBasic.Cli -- vm examples/pi.bas
```

Inspect intermediate stages of any program:

```sh
dotnet run --project src/FullBasic.Cli -- lex     examples/factorial.bas
dotnet run --project src/FullBasic.Cli -- parse   examples/factorial.bas
dotnet run --project src/FullBasic.Cli -- analyze examples/factorial.bas
```

After an AOT publish, use the native CLI directly (much faster startup) and produce a standalone binary for any VM-compatible example:

```sh
dotnet publish src/FullBasic.Cli -c Release /p:PublishAot=true

./publish/aot/FullBasic.Cli run examples/primes.bas
./publish/aot/FullBasic.Cli vm  examples/pi.bas

./publish/aot/FullBasic.Cli build examples/factorial.bas -o factorial
./factorial
```

## Architecture

| Project | Purpose |
|---|---|
| `FullBasic.Core` | source files, positions, diagnostics |
| `FullBasic.Lexer` | tokenizer |
| `FullBasic.Parser` | recursive-descent parser → immutable AST (`abstract record class`) |
| `FullBasic.Sema` | two-pass analyzer; symbol/scope resolution attached as a side table |
| `FullBasic.Runtime` | numeric/string/array values (Singulink `BigDecimal`), builtins, picture-string formatter, file I/O |
| `FullBasic.Interpreter` | tree-walking interpreter with explicit handler stack and `FlowControl` returns |
| `FullBasic.Bytecode` | opcode enum, chunk format, serializer |
| `FullBasic.Compiler` | AST → bytecode lowering |
| `FullBasic.Vm` | stack-based VM |
| `FullBasic.Cli` | command dispatcher + Phase-10 self-extracting stub |

Locked architectural decisions (decimal library, value hierarchy, handler-stack design, MAT semantics, etc.) live alongside the code; behavioural deviations from the spec will be tracked in `docs/conformance.md`.

## Conformance

Tested against spec-derived programs written from ISO 10279 section numbers. The NBS Minimal BASIC Test Programs corpus (NBSIR 78-1420) was originally planned as the oracle but is **deferred** — the archive.org OCR is too column-shredded for clean programmatic extraction. Revisiting it requires re-OCRing the PDFs or transcribing by hand.

## License

TBD.
