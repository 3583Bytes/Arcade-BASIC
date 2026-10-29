# Changelog

What each release changed, for someone using Arcade BASIC. A release's notes on GitHub
are its section here (the release workflow copies it), followed by the commits since
the last one. Releases before 2.0.0 are on the
[releases page](https://github.com/3583Bytes/Arcade-BASIC/releases).

## 2.0.0

The biggest release so far: an IDE in the browser, one engine for everything, and a
language with mouse input, held keys, raster graphics and programs in several files.

### The IDE is in the browser

**[Open it](https://3583bytes.github.io/Arcade-BASIC/)** — nothing to install:

- an editor that checks your program as you type, with tabs, your own files (open,
  save, a whole folder), and six themes (QBasic blue and Commodore 64 among them);
- a debugger: breakpoints, stepping over, into and out, the variables as they change;
- a pixel editor for the pictures and textures kept in `DATA`, colour swatches and a
  palette, and a tune editor for `PLAY`;
- an outline and folding for long programs, and go to definition;
- sound, and the files programs write kept in the browser (File → Program files…);
- links that carry a whole program: one to open it in the editor, and a *play link*
  that opens straight into it, no editor.

### One engine

Every program now runs on the bytecode VM — in the browser, with `arcade-basic run`
(`vm` still works: it's the same), in the REPL, and in standalone binaries. The
tree-walking interpreter is gone.

- It's faster than the old `run`: up to 2.4× in our benchmarks, most with `OPTION
  ARITHMETIC NATIVE`.
- Deep recursion raises exception 5000 (past 100,000 calls), which `WHEN` can catch,
  instead of crashing `run`.
- The REPL keeps going after a line that stops with an error.

### The language

- **Programs in several files:** `LIBRARY "file.bas"` brings in another file's
  modules and procedures; modules have `PUBLIC` and `SHARE` variables, `DECLARE
  PUBLIC`, start-up code and their own `DATA` (ISO 10279).
- **Held keys:** `KEYDOWN(key$)`, for games that move while a key is down.
- **The mouse:** `LOCATE POINT` / `GET POINT` (ANSI X3.113) and `GET MOUSE x, y,
  state`.
- **Raster graphics and colours:** `MAT GRAPH CELLS` draws an array as an image (with
  see-through cells, for sprites); `SET` / `ASK COLOR MIX` change any of 256 colours.
- **`OPTION ARITHMETIC NATIVE`:** hardware doubles instead of exact decimals, for
  speed.
- **`RND`** gives the same sequence every run until `RANDOMIZE`; `RANDOMIZE n` repeats
  the sequence for `n`.
- **Fixes:** `STOP` and errors inside procedures end the program as they should; the
  `PRINT` column carries across statements and procedures; `CONTINUE` leaves the
  `WHEN` block protected, and a `WHEN` inside a handler no longer disturbs the
  exception it's handling; a `GOSUB` from a `WHEN` block returns into it; `FOR`
  evaluates its limit and step once; `MAT A = B` copies; `SQR(-1)`, `(-8) ^ 0.5` and
  their kind raise BASIC exceptions instead of crashing; `STR$` formats as `PRINT`
  does; arrays start at 1 without `OPTION BASE`.

### In the terminal

Graphics draw with box-drawing lines, block fills and Braille, in up to 256 colours,
and text over a filled area keeps its colour.

### Examples

- `maze3d.bas` — a first-person 3D maze with textured walls, floor and ceiling, and
  sprites; a program in three files.
- `kanban.bas` — a Kanban board for your own tasks that saves itself as you go: drag
  cards, edit them, move them with the keys.
- `editor.bas` — a text editor for your own files, with tabs and the mouse.
- The games have sound effects.

### Desktop apps (preview)

`arcade-basic build game.bas --desktop` makes a program a desktop app: a window with
just the program — a Mac `.app`, or a Windows or Linux executable. For now it needs
the desktop player built yourself (`desktop/player/build.sh`; see
[docs/building.md](docs/building.md)); a later release will include it.

### Removed

- The terminal IDE (`arcade-basic-ide`) and the Unity package: the browser IDE
  replaces them. Releases now ship the command-line tool, `arcade-basic`, for Linux
  (x64), macOS (Apple Silicon) and Windows (x64).
- For programs embedding Arcade BASIC: the tree-walking interpreter
  (`ArcadeBasic.Interpreter`). `BasicEngine` is now in `ArcadeBasic.Vm` and runs the VM.

### Compatibility

- Standalone binaries built with 1.0.x keep running (each carries its own VM); build
  them again to get this version's fixes. The bytecode format is now version 9.
- Building from source needs the .NET 10 SDK.
- `arcade-basic --version` now reports the release (1.0.x always said 1.0.0).
