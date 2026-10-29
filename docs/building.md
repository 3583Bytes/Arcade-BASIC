# Building Arcade BASIC from source

This guide covers building everything yourself on Linux, macOS, and Windows:

- the **CLI** (`arcade-basic`) — a native, self-contained executable (NativeAOT),
- a **standalone `.bas` binary** — your program bundled into a runnable executable.

The IDE runs in the browser; to build and serve it yourself, see
[`src/ArcadeBasic.Web/README.md`](../src/ArcadeBasic.Web/README.md).

Pre-built binaries for every platform are attached to each
[tagged release](https://github.com/3583Bytes/Arcade-BASIC/releases); build
from source only if you want to hack on it or target a platform/arch we don't
publish.

---

## 1. Prerequisites

### .NET 10 SDK (required for everything)

The repo pins the SDK major version in [`global.json`](../global.json) (`10.0`).
Any .NET 10 SDK (10.0.100 or later) works.

| Platform | Get the SDK |
|---|---|
| All | <https://dotnet.microsoft.com/download/dotnet/10.0> |
| Windows | `winget install Microsoft.DotNet.SDK.10` |
| Linux | distro packages, or the [install script](https://learn.microsoft.com/dotnet/core/install/linux) |
| macOS | the official installer above, or Homebrew: `brew install --cask dotnet-sdk` |

Verify:

```sh
dotnet --list-sdks      # should list a 10.0.x SDK
```

### Native toolchain (only for the AOT CLI / standalone binaries)

NativeAOT invokes the platform's C toolchain to link the final binary. You
**don't** need this to run from source or run tests — only to
build the CLI (`arcade-basic`) or a standalone `.bas` binary.

| Platform | Install |
|---|---|
| Linux (Debian/Ubuntu) | `sudo apt-get install -y clang zlib1g-dev` (plus `build-essential`) |
| macOS | `xcode-select --install` (Command Line Tools — provides `clang`/`ld`) |
| Windows | Visual Studio 2022 with the **Desktop development with C++** workload (MSVC + Windows SDK) |

---

## 2. Runtime identifiers (RIDs)

A **RID** (Runtime Identifier) names the target a binary is built for — its
operating system and CPU architecture, in the form `<os>-<arch>`. Publishing is
**per-target**: every `dotnet publish` command below takes `-r <RID>` (long form
`--runtime <RID>`), and you substitute **one** concrete value from this table —
the platform you want the binary to run on. There is no "build for all
platforms" option; run the publish once per target.

| Platform | RID | Binary suffix | In tagged releases? |
|---|---|---|:---:|
| Linux x64 | `linux-x64` | *(none)* | yes |
| Linux ARM64 | `linux-arm64` | *(none)* | no — build it yourself |
| macOS Intel | `osx-x64` | *(none)* | no — build it yourself |
| macOS Apple Silicon | `osx-arm64` | *(none)* | yes |
| Windows x64 | `win-x64` | `.exe` | yes |

So wherever a command shows `<RID>`, replace it literally — e.g. on an
Apple-Silicon Mac: `dotnet publish ... -r osx-arm64`.

> **Cross-compilation:** NativeAOT can **not** cross-compile to a different OS
> (build Linux binaries on Linux, etc.). For a different *architecture* on the
> same OS, build on a matching-arch machine — e.g. the release pipeline builds
> `osx-arm64` on an Apple-Silicon runner.
>
> **`osx-x64` (Intel Mac)** is a valid RID you can still build locally, but it is
> **no longer produced by the release pipeline** — download a release only for
> the platforms marked "yes" above; build the rest from source.

---

## 3. Clone, build, test (all platforms)

```sh
git clone https://github.com/3583Bytes/Arcade-BASIC.git arcade-basic
cd arcade-basic

dotnet build -c Release           # warnings are errors in Release
dotnet test  -c Release           # full test suite
dotnet run --project src/ArcadeBasic.Cli -- run examples/hello.bas
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
`src/ArcadeBasic.Cli/bin/Release/net10.0/<RID>/publish/arcade-basic`.)

---

## 5. Build a standalone `.bas` binary

`arcade-basic build` bundles your compiled program onto the end of an AOT CLI
binary, producing one self-contained executable (see
[standalone-builds.md](standalone-builds.md) for the anatomy).

```sh
./publish/cli/arcade-basic build examples/hello.bas -o hello
./hello
```

> **Graphics programs run standalone too.** A §13 graphics program (e.g.
> `examples/kanban.bas`) renders in the terminal when run via `arcade-basic
> run`/`vm` or as a standalone binary — the CLI draws with a Braille + ANSI
> backend.
> It activates only on an interactive terminal; piped/redirected output falls
> back to no rendering (use `--svg <file>` for headless output). Needs a
> terminal with Braille-glyph and ANSI/VT support (Windows Terminal, modern
> macOS/Linux terminals).

### A desktop app (`--desktop`)

`build --desktop` puts the program in the **desktop player** instead: a window
showing just the program, as in the browser (see
[standalone-builds.md](standalone-builds.md#desktop-apps---desktop)). Build the
player once, for the computer you're on — it needs the WebAssembly build tools
(`dotnet workload install wasm-tools`, run outside the repo, with `sudo` if your .NET
is system-wide) and [Rust](https://rustup.rs):

```sh
desktop/player/build.sh                          # → desktop/player/target/release/arcade-player
./publish/cli/arcade-basic build examples/maze3d.bas --desktop \
    --player desktop/player/target/release/arcade-player      # → maze3d.app on a Mac
open maze3d.app
```

Copy `arcade-player` next to `arcade-basic` and `--player` isn't needed. On Linux the
player also needs WebKitGTK (`libwebkit2gtk-4.1`), as every Tauri app does; Windows
10 and 11 have the web view it uses (WebView2) built in.

`build.sh --page <dir>` uses a player page already published (a site's `player/`)
instead of publishing one; `--universal` (on a Mac, after
`rustup target add aarch64-apple-darwin x86_64-apple-darwin`) builds for Apple Silicon
and Intel both, into `desktop/player/target/universal/arcade-player`. The Intel half
is linked with an ad hoc signature (`desktop/player/.cargo/config.toml`, which Cargo
reads because `build.sh` runs it from `desktop/player`), as Apple Silicon builds are by
default, so the web IDE can re-sign it; `build.sh` fails if any half of a Mac player
isn't signed.

For the web IDE's *File → Export…* to offer desktop apps, its site carries the players:

```sh
src/ArcadeBasic.Web/build-site.sh                       # → publish/site
desktop/player/build.sh --page publish/site/player --universal
desktop/player/add-to-site.sh publish/site desktop/player/target/universal/arcade-player
```

`add-to-site.sh` takes players for any OS (it tells them apart by their first bytes);
the Pages workflow builds one on each and adds all three, and on Windows checks that
Windows reads an icon written into its player (`desktop/player/icons/check-windows-icon.mjs`).

Arcade BASIC's icon is drawn as a picture in `desktop/player/icons/make-icons.mjs`,
which makes the player's icons from it (`icon.png`, `icon.ico`, `icon.icns`) and the
IDE's (`src/ArcadeBasic.Web/wwwroot/icon.png`). After changing it, run it with Node 22 or
later and rebuild the player and the CLI (which has `icon.icns` built in):

```sh
node desktop/player/icons/make-icons.mjs
```

---

## 6. Platform notes

### macOS — distributing to other machines

The CLI and standalone binaries are **unsigned and un-notarized**. They
run fine where you built them, but a Mac that *downloads* one (browser, email,
AirDrop) will quarantine it and refuse to open it. The recipient clears it with:

```sh
xattr -dr com.apple.quarantine ./hello
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
| Standalone `.bas` | `arcade-basic build foo.bas -o foo` | `foo` | **yes** (uses the AOT CLI) |
| Desktop player | `desktop/player/build.sh` | `desktop/player/target/release/arcade-player` | Rust, `wasm-tools` |
| Desktop app | `arcade-basic build foo.bas --desktop` | `foo.app` / `foo.exe` / `foo` | no (uses the player) |
| Website | `src/ArcadeBasic.Web/build-site.sh`, then `desktop/player/add-to-site.sh` | `publish/site` | `wasm-tools` |
