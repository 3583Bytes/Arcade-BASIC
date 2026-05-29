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
| Esc            | Stop a running program              |
| Ctrl-N         | New (empty) buffer                  |
| Ctrl-O         | Open a `.bas` file                  |
| Ctrl-S         | Save                                |
| Ctrl-L         | Clear the output pane               |
| Ctrl-Q         | Quit                                |

Every example in `/examples` is bundled into the binary and listed under
**File ▸ Examples**.

## What's inside

| File                    | Role                                                              |
| ----------------------- | ----------------------------------------------------------------- |
| `Program.cs`            | Entry point; handles `--version` / `--help` before TUI bootstrap. |
| `TuiShell.cs`           | Menus, status bar, layout, file ops.                              |
| `SourcePane.cs`         | TextView + line-number gutter + highlight scheduling.             |
| `OutputPane.cs`         | Read-only scrollback (size-capped) + the bottom input line for INPUT. |
| `RunController.cs`      | Task-based runner + thread-safe writer + main-loop drain pump.    |
| `SyntaxColorizer.cs`    | Token-kind → palette mapping (mirrors the Unity sample).          |
| `ExamplesProvider.cs`   | Enumerates the bundled `.bas` files.                              |

## Implementation notes

- **Terminal.Gui v1.x** — the v2 line requires net10; sticking to v1 keeps the
  IDE on net9 like the rest of the solution.
- **No AOT** — Terminal.Gui leans on reflection, so this binary isn't
  AOT-published. The CLI still is.
- **BasicEngine** — every Run kicks off `ArcadeBasic.BasicEngine.Run` against
  a thread-safe `TextWriter`. Cancellation is checked between statements;
  the controller surfaces exit code 2 as `[cancelled]`.
- **Syntax highlighting** — classification logic and palette are in place
  (matches the Unity sample), but the per-token overlay in the editor still
  needs a `TextView.OnDrawContent` override. Tracked as a follow-up.
