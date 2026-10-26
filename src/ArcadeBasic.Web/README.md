# Arcade BASIC in the browser

A small IDE for Arcade BASIC that runs in the browser: an editor with a tab per
program, and the program's screen beside it. Programs run on the bytecode VM,
compiled to WebAssembly with the plain .NET WebAssembly SDK (no Blazor). The page
(`wwwroot/main.js`) owns the loop: every animation frame it calls `BasicVm.Step`
for up to ~10 ms, waits out `SLEEP`s, shows `PRINT` output, asks for `INPUT`, and
replays the newest frame onto a `<canvas>`. Keys go to the program while the
canvas has focus, with exact held-key state for `KEYDOWN`.

The editor is [CodeMirror 6](https://codemirror.net) set up for BASIC:

- **Colours** from the engine's own reserved words and supplied functions, with
  line numbers after `GOTO`/`GOSUB`/`THEN` coloured as labels.
- **Problems as you type**: the engine checks the program (lexer, parser,
  analyzer, compiler, no run) and underlines errors and warnings — an undeclared
  name is usually a typo. The status line counts them; F8 goes to the next.
- **Runtime errors** highlight the line that raised them.
- **Completion** of keywords, functions and the program's own names; **F12** or
  Ctrl/⌘-click goes to a line number, `SUB`, `FUNCTION` or `DEF`; find and
  replace, comment toggling, bracket matching.
- **Six themes** — Midnight, Daylight, QBasic blue, Commodore 64, green screen and
  amber — each a set of CSS variables in `wwwroot/style.css` that the editor
  shares. The first visit follows the system's light or dark setting.
- **Files**: New, Open, Save, Save as and drag-and-drop. In Chrome and Edge (the
  File System Access API) a program stays linked to its file and Save writes
  straight back to it, even after a reload; Firefox and Safari open with a file
  picker and save by downloading. A file's line endings are kept as they were.
- **Kept in this browser**: every open tab and its text is saved in
  `localStorage` as you type, so a reload brings it all back. Nothing is sent
  anywhere.
- **A debugger**: click beside a line number (or F9) for a breakpoint; the
  program stops before that line, which is marked, and the **Variables** tab shows
  every call in progress and its variables — arrays open to their elements, a
  small two-dimensional one as a grid (maze3d's map, say), and what the last step
  changed in colour. Point at a name for its value. Step over (F10), into (F11),
  out (Shift+F11), continue (F5) or pause a running program (F6); F10/F11 with
  nothing running starts it stopped at the first line. While a program runs, the
  Variables tab updates twice a second. Breakpoints are kept with the program.
- **Sound**: `SOUND`, `BEEP` and `PLAY` play through Web Audio as a square wave,
  like the PC speaker (`wwwroot/audio.js`); foreground music holds the program up
  until it's played and background (`MB`) music doesn't, up to GW-BASIC's 32 tones.
  The browser allows sound after the first click or key press (Run counts), and the
  Sound box turns it off. The arcade examples have sound effects.
- **Links**: the address names the example showing (`…/Arcade-BASIC/#breakout`),
  and *File → Copy a link* puts a whole program, compressed, in the address
  (`#code=…`).

It's live at <https://3583bytes.github.io/Arcade-BASIC/>, published by
[`.github/workflows/pages.yml`](../../.github/workflows/pages.yml) whenever `main`
changes (GitHub Pages must be set to deploy from GitHub Actions: Settings → Pages →
Source). With no address it opens what was open last time, or `maze3d`. It isn't
in `ArcadeBasic.sln`, so the CI workflow doesn't build it; the Pages one does.

## Run it

```sh
dotnet run --project src/ArcadeBasic.Web -c Release
# open the "App url: http://127.0.0.1:…/" it prints — pick an example, Run,
# click the screen to play. (The HTTPS dev-certificate warning is harmless; use the http URL.)
```

To host it anywhere static (GitHub Pages, `python3 -m http.server`), publish and
serve `bin/Release/net10.0/publish/wwwroot`:

```sh
dotnet publish -c Release src/ArcadeBasic.Web
python3 -m http.server -d src/ArcadeBasic.Web/bin/Release/net10.0/publish/wwwroot 8080
```

Publishing compiles the .NET code to WebAssembly ahead of time
(`RunAOTCompilation`), so it needs the WebAssembly build tools once:
`dotnet workload install wasm-tools` (run it outside the repo, with `sudo` if your
.NET install is system-wide). It takes about a minute and makes the download
about 2.2 MB compressed, against 1.65 MB for the plain build — and the VM about
ten times faster: in Firefox, native Mandelbrot runs in 15 ms rather than 150, and
`maze3d` takes 5 ms of each 40 ms frame rather than 16. `dotnet run` and
`dotnet build` don't compile ahead of time, so they stay quick for trying changes,
at the interpreter's speed.

`?bench=1` runs the measurements and POSTs them to `/results` (for a test server
that accepts them; a plain static server just ignores the request): compute
loops, then ten seconds each of `invaders`, `breakout` and `maze3d` for their
frame rate and VM time per frame. Add `&snapshot` to include each game's last
frame as a PNG. `?selftest=kanban` drags a card with real pointer events and
reports the board; `?selftest=page` reports which program the address opened and
the tabs; `?selftest=ide` (`wwwroot/selftest.js`) uses the editor as a person
would — colours, themes, problems, runtime errors, completion, go-to-definition,
tabs, Save (catching the download), line endings, links, sound timing and Web
Audio (oscillators started, with the test profile allowing autoplay), and the
debugger (breakpoints, stepping, the Variables tab, values on hover) — and reports
each step.
Run `?selftest=page` again in the same browser profile to see the tabs come back.

## The editor bundle

`wwwroot/lib/codemirror.js` is CodeMirror, bundled into one file with esbuild and
committed, so publishing needs no npm. To change which parts are in it
(`codemirror/entry.js`) or their versions (`codemirror/package.json`), rebuild it
with Node 18 or later:

```sh
cd src/ArcadeBasic.Web/codemirror
npm ci && npm run build
```

## Pieces

| File | Role |
|---|---|
| `Program.cs` | `[JSExport]` surface: check, load/compile, `Step`, output, input, frames, keys, stats, the error's line |
| `Playground.Debug.cs` | the debugger's exports: breakpoints, pause and step, the variables by name (as JSON) |
| `CanvasGraphicsDevice.cs` | §13 backend: records each frame as a flat command list the page replays |
| `BrowserKeyboard.cs` | `IKeyboard` + `IKeyState` from DOM `keydown`/`keyup` |
| `BrowserAudio.cs` | `IAudioDevice` + `IAudioQueue`: collects tones for the page |
| `wwwroot/main.js` | the loop, canvas drawing (optional phosphor glow), keyboard, input, menus, shortcuts, themes, links |
| `wwwroot/editor.js` | the CodeMirror editor: BASIC colouring, problems, runtime-error line, completion, go to definition, breakpoints, the paused line, values on hover |
| `wwwroot/documents.js` | the tabs: examples, files (open, save, drop), changes, what's kept in the browser |
| `wwwroot/audio.js` | sound: tones on a timeline, played as Web Audio square waves |
| `wwwroot/style.css` | the layout and the themes |
| `codemirror/` | builds `wwwroot/lib/codemirror.js` (npm; see above) |
