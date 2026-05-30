# How `arcade-basic build` works

> One line: **the resulting binary is a self-extracting bytecode VM, not native
> machine code for your BASIC program.** The runtime (lexer, parser, sema, VM,
> builtins) is AOT-compiled native code; your program is compiled to bytecode
> and stapled onto the end of that runtime as a trailer.

This doc explains how a `.bas` file becomes a standalone executable, what's
actually inside the resulting binary, and why this architecture was chosen
over a true native-codegen approach.

## Anatomy of a bundled binary

```
┌──────────────────────────────────────────────────────────────┐
│ AOT-compiled native runtime (~4 MB, Mach-O / ELF / PE)       │
│                                                              │
│   • Lexer, parser, two-pass semantic analyzer                │
│   • Stack-based bytecode VM (~80 opcodes)                    │
│   • Runtime values (Singulink BigDecimal), builtins,         │
│     picture-string formatter, file/channel I/O               │
│   • Startup glue (Program.Main) that detects whether the     │
│     binary contains a bundled program                        │
├──────────────────────────────────────────────────────────────┤
│ Your compiled bytecode                                       │
│                                                              │
│   • Per-chunk opcode stream (main + each SUB / FUNCTION /    │
│     DEF body)                                                │
│   • Number and string constant pools                         │
│   • DATA pool                                                │
│   • Bytecode format version (currently 3)                    │
├──────────────────────────────────────────────────────────────┤
│ 12-byte trailer:  [ u32 payload length ][ "FB-BCEND" ]       │
└──────────────────────────────────────────────────────────────┘
```

For a typical small program, the runtime is ~4 MB and your bytecode is
~hundreds of bytes to a few KB. `hello.bas` adds about 300 bytes to the
runtime. `matrix.bas` (DIM + MAT + transpose + inverse) adds about 600.

The runtime portion is identical across every `arcade-basic build` for a
given platform — it's just the AOT-published CLI binary, byte for byte,
with payload appended. Diff two binaries built from different `.bas` files
and only the trailer differs.

## Build pipeline

`arcade-basic build foo.bas -o foo` runs this sequence inside the CLI
(`src/ArcadeBasic.Cli/Program.cs:200-254`):

1. **Lex / parse / analyze** the source file the same way `run` and `vm`
   would, producing an `AstProgram` and a `SemanticInfo` side-table.
2. **Compile** to bytecode via `BasicCompiler.Compile(program, info)` — see
   [`docs/architecture.md`](architecture.md) for the lowering rules. This
   returns a `Bytecode.Program` (main chunk + each SUB/FUNCTION/DEF chunk +
   data pool + builtin-name table).
3. **Serialize** the bytecode to a `byte[]` via
   `BytecodeSerializer.Serialize`. The format is versioned (`magic = 'FBCX'`,
   currently `version = 3`); incompatible serializer changes bump the version,
   and the deserializer rejects mismatched payloads with
   `unsupported version N`.
4. **Read the running CLI binary** (`Environment.ProcessPath`) into memory
   as the *stub*. Strip any pre-existing payload so we don't grow the binary
   on each rebuild.
5. **Append** the new payload + length + magic trailer via
   `EmbeddedPayload.Append`.
6. **`chmod +x`** the result on Unix-like systems (no-op on Windows).

The output is one self-contained file. No `.NET` install required on the
target machine — NativeAOT bakes the .NET runtime's bits directly into the
binary.

## Startup pipeline

When you run `./foo`, the very first thing `Main` does
(`src/ArcadeBasic.Cli/Program.cs:17-31`):

1. Call `EmbeddedPayload.TryRead(Environment.ProcessPath)` — seek to the
   last 12 bytes, check for the `FB-BCEND` magic.
2. **If found**: read the trailer's length field, seek back to read those
   payload bytes, deserialize via `BytecodeSerializer.Deserialize`, hand the
   `Bytecode.Program` to `new BasicVm(program, stdout, stdin).Run()`. Exit
   with the VM's return code. The CLI argv dispatcher is never reached.
3. **If not found**: fall through to the normal CLI handling — same binary
   acts as `arcade-basic` with its `run` / `vm` / `repl` / `build` /
   `lex` / `parse` / `analyze` subcommands.

So one file plays two roles. A pre-tagged `arcade-basic` from the releases
page is the dev-side CLI; bundle a program into it and the same machine
code becomes that program's launcher.

## Why this design and not true native code?

The "right" answer for an extremely fast, self-contained binary would be:
lower the AST directly to LLVM IR (or x86-64 / ARM64 assembly), let LLVM
optimize, link to a tiny runtime. That's what Go, Rust, and GraalVM
native-image do.

This project chose differently because the actual user-visible win of
"native compilation" — *single file you can ship and double-click* — is
trivially achievable without the engineering cost of a codegen backend:

| | Self-extracting VM (this design) | True native (LLVM-style) |
|---|---|---|
| Implementation cost | small — one chunk format + a switch-dispatch VM | very large — codegen backend, calling conventions, register allocator, GC integration, ABI |
| Binary size (hello world) | ~4 MB (runtime dominates) | typically 50 KB – 2 MB |
| Build time | milliseconds (just serialise) | seconds (codegen + link) |
| Runtime speed | bytecode-interpreter speed | full machine code, optimised |
| `.bas` program size grows binary by | exactly the bytecode bytes (~hundreds to thousands) | depends what got inlined |
| Cross-compilation | free (the VM doesn't care) | needs per-target codegen + linker |
| Same binary runs *any* `.bas` | yes, just swap the trailer | no, each program is its own binary |
| Faithful Full BASIC semantics | trivial — Value types and BigDecimal already in the runtime | hard — language is dynamic-ish, has MAT, INPUT retry loops, etc. |

For an Arcade-BASIC-style language with arbitrary-precision arithmetic,
matrix operations, exception handling, and runtime-dimensioned arrays,
true native compilation is a large project. The self-extracting model
delivers ~90% of the perceived benefit (single-file distribution, no
install, fast incremental "compile") for ~10% of the work.

If raw execution speed ever becomes the bottleneck for a real-world
program, the next step is typically *not* full native codegen — it's
optimising the bytecode VM (computed-goto dispatch, super-instructions,
inline caches for builtins). Those tend to claw back most of the gap
without giving up the architectural simplicity.

## Related approaches

The self-extracting-bytecode-VM model is more common than people realise:

- **PyInstaller** — packages a Python interpreter + your `.py` files + a
  stub into one exe. Same conceptual model: bytecode interpreter + program
  data, appearing as one file.
- **Electron / NW.js** — your JS/HTML packaged alongside a Chromium and
  Node runtime in one bundle.
- **AppImage** — Linux distribution format that bundles an app + its
  dependencies into a single executable file via a self-mounting SquashFS
  image.
- **Java executable jars + `jpackage`** — bytecode + JVM bundled into a
  per-platform launcher.

What you typically *don't* see in this category is interpreter-based
languages doing real native codegen unless there's a specific perf
motivation (PyPy AOT, V8 snapshots, GraalVM). Most ship the runtime.

## Code map

If you want to read the actual implementation:

| What | Where |
|---|---|
| Build subcommand | [`src/ArcadeBasic.Cli/Program.cs`](../src/ArcadeBasic.Cli/Program.cs) `Build()` method around line 200 |
| Startup payload detection | Same file, `Main` around line 17 |
| Trailer format (append / read) | [`src/ArcadeBasic.Bytecode/BytecodeSerializer.cs`](../src/ArcadeBasic.Bytecode/BytecodeSerializer.cs) `EmbeddedPayload` static class |
| Bytecode format (versioned) | Same file, `BytecodeSerializer.Serialize` / `Deserialize` |
| AST → bytecode lowering | [`src/ArcadeBasic.Compiler/BasicCompiler.cs`](../src/ArcadeBasic.Compiler/BasicCompiler.cs) |
| Opcode dispatch | [`src/ArcadeBasic.Vm/BasicVm.cs`](../src/ArcadeBasic.Vm/BasicVm.cs) `ExecuteChunk` switch |
| NativeAOT toggle | [`src/ArcadeBasic.Cli/ArcadeBasic.Cli.csproj`](../src/ArcadeBasic.Cli/ArcadeBasic.Cli.csproj) — `<PublishAot>true</PublishAot>` |

## Trying it

```sh
# Build the AOT CLI once for your platform (no -p:PublishAot flag — it's
# in the csproj, and passing it on the command line would propagate as a
# global MSBuild property and fail the netstandard2.1 library builds).
dotnet publish src/ArcadeBasic.Cli -c Release -r osx-arm64 -o out

# Bundle a .bas into a standalone binary
out/arcade-basic build examples/factorial.bas -o factorial
./factorial

# Same source under the VM (interpreted, no AOT), for comparison
out/arcade-basic vm examples/factorial.bas

# Inspect the trailer of a bundled binary
tail -c 12 factorial | xxd
# 4-byte length, then "FB-BCEND" magic
```

`scripts/build-bundle.sh` does the AOT publish for both the CLI and the
IDE in one shot — see the script for the full recipe. The Unity sample's
"Build standalone" menu item uses the same pipeline, just driven from the
in-Editor IDE instead of the command line; see
[`unity/Samples~/ArcadeBasic/README.md`](../unity/Samples~/ArcadeBasic/README.md)
for the Unity-side specifics.
