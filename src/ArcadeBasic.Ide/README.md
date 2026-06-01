# Arcade BASIC IDE (`arcade-basic-ide`)

A full-screen terminal IDE for Arcade BASIC — edit, run, and compile from one
keyboard-driven shell. Same shape as the Unity in-game console sample, but for
the terminal.

![Arcade BASIC IDE — startrek.bas loaded with the About dialog open](../../screenshots/ArcadeBasicIDEScreenshot.png)

## Running

From source:

```
dotnet run --project src/ArcadeBasic.Ide -- examples/hello.bas
```

From a published binary:

```
arcade-basic-ide                    # empty buffer
arcade-basic-ide examples/hello.bas # open a file
arcade-basic-ide --version          # print version and exit
arcade-basic-ide --help             # usage
```

The release pipeline publishes self-contained single-file binaries (no .NET
install required — the runtime is bundled into the executable) for
`linux-x64`, `osx-arm64`, `osx-x64`, and `win-x64`.

## Keys

| Key            | Action                              |
| -------------- | ----------------------------------- |
| F5             | Run the current source              |
| F6             | Compile-check (no execution)        |
| F7             | Build a standalone native binary    |
| Esc            | Stop a running program              |
| Ctrl-N         | New (empty) buffer                  |
| Ctrl-O         | Open a `.bas` file                  |
| Ctrl-S         | Save                                |
| Ctrl-L         | Clear the output pane               |
| Ctrl-Q         | Quit                                |

Every example in `/examples` is bundled into the binary and listed under
**File ▸ Examples**.

## Graphics & interactive input

A program that uses the §13 graphics statements (`SET WINDOW`/`GRAPH …`) draws
onto a **Graphics tab** — a Braille-cell canvas (2×4 dots per character cell).
The tab appears automatically the moment a program draws.

Both output surfaces have **their own `INPUT` field**: text programs read on the
Output tab, graphics programs read on the field beneath the Graphics canvas. So
an interactive draw → `INPUT` → redraw loop (e.g. [`kanban.bas`](../../examples/kanban.bas))
stays on one surface instead of jumping between tabs. The program shows its
prompts on the board with `GRAPH TEXT`; the field just collects the typed value.

Try [`examples/graphics.bas`](../../examples/graphics.bas) (a static drawing) or
[`examples/kanban.bas`](../../examples/kanban.bas) (an interactive board).

## Build standalone (F7)

**Run ▸ Build standalone…** (or F7) compiles the current buffer to a single
self-contained executable that contains both the bytecode VM and your
program. The result needs no .NET install and no separate Arcade BASIC
install — just the one file.

How it works: the IDE compiles the source, then locates an external
`arcade-basic` AOT binary to use as the *stub*, reads it, appends the
compiled bytecode + an `FB-BCEND` trailer, and chmods the result executable.
This is the same mechanism the CLI's `build` subcommand uses.

Because the IDE itself isn't AOT-compiled (Terminal.Gui v1 relies on
reflection), it can't use itself as the stub. It looks for `arcade-basic`
in two places:

1. Same directory as the running `arcade-basic-ide` binary.
2. Anywhere on `PATH`.

If neither finds it, the IDE shows an error pointing to the releases page.
The fix is to put `arcade-basic` next to `arcade-basic-ide` or add it to
your PATH; tagged releases ship both binaries for every supported RID.

## What's inside

| File                    | Role                                                              |
| ----------------------- | ----------------------------------------------------------------- |
| `Program.cs`            | Entry point; handles `--version` / `--help` before TUI bootstrap. |
| `TuiShell.cs`           | Menus, status bar, layout, file ops.                              |
| `SourcePane.cs`         | TextView + line-number gutter + highlight scheduling.             |
| `OutputPane.cs`         | Read-only scrollback (size-capped) + the bottom input line for INPUT. |
| `BrailleCanvas.cs`      | Graphics tab canvas: a Braille-cell (2×4 dots/cell) bitmap for §13 output. |
| `GraphicsPane.cs`       | Wraps the canvas + its own INPUT field (so graphics programs read here). |
| `TuiGraphicsDevice.cs`  | Maps §13 graphics onto the canvas via the shared `Rasterizer`.    |
| `IInputSink.cs`         | The `BeginRead`/`CancelRead` contract both panes implement.       |
| `InteractiveTextReader.cs` | `TextReader` for INPUT; marshals a read onto the active surface's field. |
| `RunController.cs`      | Task runner + drain pump; routes INPUT to the active surface (graphics vs text). |
| `SyntaxColorizer.cs`    | Token-kind → palette mapping (mirrors the Unity sample).          |
| `CompileService.cs`     | Lex → parse → sema → bytecode-emit; powers F6 and F7.             |
| `BuildService.cs`       | Append-bytecode-to-AOT-stub flow used by F7.                      |
| `ExamplesProvider.cs`   | Enumerates the bundled `.bas` files.                              |

## Implementation notes

- **Terminal.Gui v1.x** — the v2 line requires net10; sticking to v1 keeps the
  IDE on net9 like the rest of the solution.
- **No AOT** — Terminal.Gui leans on reflection, so this binary isn't
  AOT-published. The CLI still is.
- **BasicEngine** — every Run kicks off `ArcadeBasic.BasicEngine.Run` against
  a thread-safe `TextWriter` (and, for graphics, a `TuiGraphicsDevice`).
  Cancellation is checked between statements; the controller surfaces exit
  code 2 as `[cancelled]`.
- **Crash capture** — an unhandled UI-thread exception is caught by
  `TuiShell.Run`, which restores the terminal, prints the stack trace, and
  writes it to `<temp>/arcade-basic-ide-error.log` instead of dying silently.
- **Syntax highlighting** — classification logic and palette are in place
  (matches the Unity sample), but the per-token overlay in the editor still
  needs a `TextView.OnDrawContent` override. Tracked as a follow-up.
