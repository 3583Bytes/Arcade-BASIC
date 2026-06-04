# Changelog

All notable changes to the Unity package will be documented here. Versions follow the upstream project's semver.

## [0.1.0] — Unreleased

### Fixed
- Import error "Reference has errors 'Singulink.Numerics.BigDecimal'" — `copy-dlls.sh` now emits a `.meta` file next to every plugin DLL with `validateReferences: 0`. Unity 2021.3+ otherwise tries to bind netstandard2.1 `System.*` references against its bundled BCL and rejects the assembly when the metadata identity doesn't match exactly.

### Added
- §13 graphics rendering to a `Texture2D`. `RasterGraphicsDevice` (in `ArcadeBasic.Runtime`) rasterizes `GRAPH LINES`/`AREA`/`POINTS`/`TEXT` into an ARGB pixel buffer with a shared 5×7 `BitmapFont`; the `BasicScreen` component (`Runtime/UnityGraphics`) runs a program and blits the buffer onto a `Renderer`'s material. Handles static programs (run-to-completion); real-time/interactive programs (a worker-thread driver + `INKEY$` keyboard) are a planned follow-up.
- `link.xml` preserving the engine assemblies under IL2CPP managed-code stripping.
- `LICENSE.md` and `Third Party Notices.md` (Singulink.Numerics, MIT) inside the package.
- Initial Unity package: pre-built netstandard2.1 DLLs for the Arcade BASIC libraries.
- `com.unity.textmeshpro` (3.0.6) declared as a package dependency, so the **Arcade BASIC IDE** sample compiles on a fresh project on any OS without manual setup. (TMP *Essentials* — the default font/shaders — still need a one-time **Window → TextMeshPro → Import TMP Essentials**; on Unity 6 TMP has merged into `com.unity.ugui`, so verify the dependency resolves there.)
- `ArcadeBasic.BasicEngine` static class as a one-call embedding entry point.
- `Arcade BASIC IDE` sample: a drop-in in-game BASIC IDE shipped as a ready-to-play **scene + prefab** (the `ArcadeBasicCodeEditor` MonoBehaviour with its UI wired in the Inspector).
  - **Menus**: File (New / Open / Save / Save As / Quit), Run (Run / Compile / Build standalone / Stop / Clear Output), Help (About modal).
  - **Editor niceties** (TUI parity): F5 Run / F6 Compile / F7 Build / Esc Stop hotkeys (alongside Ctrl/Cmd+Enter); a live `Ln L, Col C` readout in the status line while editing; Tab inserts two spaces instead of moving focus.
  - **Grouped examples**: the Open dropdown groups bundled programs under headers (Graphics / Games / Basics) driven by a `@category <Name>` tag in each `.bas`. The console TUI IDE reads the same tag and renders the groups as nested `Examples ▸ …` submenus.
  - **Source pane**: line-number gutter + a syntax-highlighted TMP overlay (the editor's own text is transparent; the overlay is colored by the project's own `BasicLexer`, so highlighting always matches what the interpreter parses).
  - **Output pane**: scrollable transcript with sticky-bottom auto-scroll (stays pinned to new output unless the user has scrolled up to read older lines).
  - **Graphics pane** (§13): a third tab that renders `GRAPH`/`SET` drawing into a `Texture2D` "screen" (built at runtime over `RasterGraphicsDevice`), auto-shown the first time a program draws.
  - **Real-time input** (`INKEY$`): while a program runs, key presses are forwarded to the `INKEY$` buffer — printable ASCII 32–126 plus the arrow keys (`CHR$(0)` + 72/80/75/77), matching the TUI/CLI byte-for-byte — so `SLEEP`-driven games (e.g. Space Invaders) play inside the IDE. Suppressed while an `INPUT` line is pending or a Ctrl/Cmd shortcut is held. Requires the project's Active Input Handling to include the (old) Input Manager.
  - **INPUT bar**: single-line field at the bottom of the Output pane; hidden while idle, then auto-shown + focused when the program runs `INPUT` / `LINE INPUT`, echoing the typed line into the transcript.
  - **Problems pane**: bottom strip in the Source pane that auto-opens on compile/runtime diagnostics, titled `Problems (N)`, with Copy + close (✕) controls.
  - **Build standalone** (Editor-only): compiles the source to bytecode and appends it to a located `arcade-basic` AOT stub — same flow as the CLI `build` subcommand and the TUI IDE's F7. Per-RID stub binaries ship under the sample's `Stubs/` folder.
  - Import via **Package Manager → Samples → Import**, then open the sample's `Scene/ArcadeBasicIDE.unity` (or run **Window → Arcade BASIC → Samples → Open BASIC IDE Scene**).
- Unity editor integration (`Editor/` folder):
  - `Window → Arcade BASIC → Console` — REPL-style editor window with run + persistence.
  - `Assets → Create → Arcade BASIC → {Empty Program, Hello World, FOR Loop Demo, Function Demo}` — `.bas` templates.
  - `.bas` ScriptedImporter + custom inspector (source preview, ▶ Run button, output panel).
  - `Assets → Run Arcade BASIC Program` — context-menu runner for the selected `.bas` asset.
  - `Window → Arcade BASIC → Documentation / Conformance / About` — quick links.
