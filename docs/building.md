# Building Arcade BASIC from source

This guide covers building everything yourself on Linux, macOS, and Windows:

- the **CLI** (`arcade-basic`) — a native, self-contained executable (NativeAOT),
- the **IDE** (`arcade-basic-ide`) — a self-contained single-file terminal app,
- a **standalone `.bas` binary** — your program bundled into a runnable executable.

Pre-built binaries for every platform are attached to each
[tagged release](https://github.com/3583Bytes/Arcade-BASIC/releases); build
from source only if you want to hack on it or target a platform/arch we don't
publish.

---

## 1. Prerequisites

### .NET 9 SDK (required for everything)

The repo pins the SDK major version in [`global.json`](../global.json) (`9.0.1xx`).
Any `9.0.1xx` SDK works.

| Platform | Get the SDK |
|---|---|
| All | <https://dotnet.microsoft.com/download/dotnet/9.0> |
| Windows | `winget install Microsoft.DotNet.SDK.9` |
| Linux | distro packages, or the [install script](https://learn.microsoft.com/dotnet/core/install/linux) |
| macOS | the official installer above, or Homebrew: `brew install dotnet@9` |

> **Homebrew note (macOS):** `dotnet@9` is *keg-only*, so it isn't put on your
> `PATH` automatically. Either use the official installer (which does), or add
> the keg yourself:
> ```sh
> export PATH="$(brew --prefix dotnet@9)/bin:$PATH"
> export DOTNET_ROOT="$(brew --prefix dotnet@9)/libexec"
> ```

Verify:

```sh
dotnet --list-sdks      # should list a 9.0.1xx SDK
```

### Native toolchain (only for the AOT CLI / standalone binaries)

NativeAOT invokes the platform's C toolchain to link the final binary. You
**don't** need this to run from source, run tests, or publish the IDE — only to
build the CLI (`arcade-basic`) or a standalone `.bas` binary.

| Platform | Install |
|---|---|
| Linux (Debian/Ubuntu) | `sudo apt-get install -y clang zlib1g-dev` (plus `build-essential`) |
| macOS | `xcode-select --install` (Command Line Tools — provides `clang`/`ld`) |
| Windows | Visual Studio 2022 with the **Desktop development with C++** workload (MSVC + Windows SDK) |

---

## 2. Runtime identifiers (RIDs)

Publishing is per-target. Pick the RID for your platform:

| Platform | RID | Binary suffix |
|---|---|---|
| Linux x64 | `linux-x64` | *(none)* |
| Linux ARM64 | `linux-arm64` | *(none)* |
| macOS Intel | `osx-x64` | *(none)* |
| macOS Apple Silicon | `osx-arm64` | *(none)* |
| Windows x64 | `win-x64` | `.exe` |

> **Cross-compilation:** NativeAOT can **not** cross-compile to a different OS
> (build Linux binaries on Linux, etc.). For a different *architecture* on the
> same OS, build on a matching-arch machine — our release pipeline builds
> `osx-x64` on an Intel runner and `osx-arm64` on an Apple-Silicon runner. The
> self-contained IDE publish is more forgiving but is still best built per RID.

---

## 3. Clone, build, test (all platforms)

```sh
git clone https://github.com/3583Bytes/Arcade-BASIC.git arcade-basic
cd arcade-basic

dotnet build -c Release           # warnings are errors in Release
dotnet test  -c Release           # full test suite
dotnet run --project src/ArcadeBasic.Cli -- run examples/hello.bas
```

Run the IDE straight from source:

```sh
dotnet run --project src/ArcadeBasic.Ide                       # empty buffer
dotnet run --project src/ArcadeBasic.Ide -- examples/hello.bas # open a file
```

---

## 4. Build the CLI (`arcade-basic`, native AOT)

`PublishAot` is set in the CLI's `.csproj`, so **do not** pass
`-p:PublishAot=true` on the command line — doing so promotes it to a global
MSBuild property that propagates into the `netstandard2.1` builds of the
multi-targeted libraries, which can't be AOT-compiled.

```sh
dotnet publish src/ArcadeBasic.Cli -c Release -r <RID> -o publish/cli
```

Result: `publish/cli/arcade-basic` (`arcade-basic.exe` on Windows), a ~4 MB
self-contained native binary — no .NET install needed on the target.

Per platform:

```sh
# Linux x64
dotnet publish src/ArcadeBasic.Cli -c Release -r linux-x64  -o publish/cli
# macOS Intel
dotnet publish src/ArcadeBasic.Cli -c Release -r osx-x64    -o publish/cli
# macOS Apple Silicon
dotnet publish src/ArcadeBasic.Cli -c Release -r osx-arm64  -o publish/cli
# Windows x64 (PowerShell or cmd — single line)
dotnet publish src/ArcadeBasic.Cli -c Release -r win-x64 -o publish/cli
```

Smoke-test:

```sh
./publish/cli/arcade-basic --version
./publish/cli/arcade-basic run examples/hello.bas
```

(Omitting `-o` puts the binary at
`src/ArcadeBasic.Cli/bin/Release/net9.0/<RID>/publish/arcade-basic`.)

---

## 5. Build the IDE (`arcade-basic-ide`, self-contained single file)

The IDE is **not** AOT-compiled (Terminal.Gui relies on reflection), so it ships
as a self-contained single-file binary with the .NET runtime bundled inside. No
native toolchain required.

```sh
dotnet publish src/ArcadeBasic.Ide \
  --configuration Release \
  --runtime <RID> \
  --self-contained \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  --output publish/ide
```

Windows (single line):

```powershell
dotnet publish src/ArcadeBasic.Ide --configuration Release --runtime win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --output publish/ide
```

Result: `publish/ide/arcade-basic-ide` (~64 MB). It's a terminal (TUI) app — run
it inside a terminal:

```sh
./publish/ide/arcade-basic-ide examples/startrek.bas
```

See [`../src/ArcadeBasic.Ide/README.md`](../src/ArcadeBasic.Ide/README.md) for
implementation notes.

---

## 6. Build a standalone `.bas` binary

`arcade-basic build` bundles your compiled program onto the end of an AOT CLI
binary, producing one self-contained executable (see
[standalone-builds.md](standalone-builds.md) for the anatomy).

### From the CLI

```sh
./publish/cli/arcade-basic build examples/hello.bas -o hello
./hello
```

### From the IDE (F7)

The IDE itself isn't AOT'd, so it can't bundle *itself*. It needs a native
`arcade-basic` (the AOT CLI from §4) to use as the stub, and looks for it:

1. **next to the `arcade-basic-ide` binary**, then
2. **on your `PATH`**.

If F7 reports *"Could not find an `arcade-basic` AOT binary"*, build the CLI (§4)
and make it findable — either copy it next to the IDE:

```sh
cp publish/cli/arcade-basic publish/ide/        # beside arcade-basic-ide
```

or put it on your `PATH` once (works for every IDE instance):

```sh
cp publish/cli/arcade-basic /usr/local/bin/     # macOS/Linux
```

When you run the IDE via `dotnet run`, the "IDE binary" is the build apphost
under `src/ArcadeBasic.Ide/bin/<Config>/net9.0/`, so the `PATH` approach is the
simplest in that case.

---

## 7. Platform notes

### macOS — distributing to other machines

Self-contained and standalone binaries are **unsigned and un-notarized**. They
run fine where you built them, but a Mac that *downloads* one (browser, email,
AirDrop) will quarantine it and refuse to open it. The recipient clears it with:

```sh
xattr -dr com.apple.quarantine ./arcade-basic-ide
```

For real distribution, `codesign` and notarize the binary.

### Windows

Use single-line commands (the `\` line-continuation above is bash). In
PowerShell you can use a backtick `` ` `` to continue lines if you prefer.

### Linux

If AOT linking fails, the usual cause is a missing `clang` or `zlib` dev package
— see the toolchain table in §1.

---

## Quick reference

| Artifact | Command | Output | Native toolchain? |
|---|---|---|---|
| Debug build | `dotnet build -c Release` | per-project `bin/` | no |
| Tests | `dotnet test -c Release` | — | no |
| CLI (AOT) | `dotnet publish src/ArcadeBasic.Cli -c Release -r <RID> -o publish/cli` | `publish/cli/arcade-basic` | **yes** |
| IDE (single file) | `dotnet publish src/ArcadeBasic.Ide -c Release -r <RID> --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/ide` | `publish/ide/arcade-basic-ide` | no |
| Standalone `.bas` | `arcade-basic build foo.bas -o foo` | `foo` | **yes** (uses the AOT CLI) |
