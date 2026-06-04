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
- `ArcadeBasic.BasicEngine` static class as a one-call embedding entry point.
- `Arcade BASIC IDE` sample: an in-game BASIC IDE built entirely at runtime by `ArcadeBasicCodeEditor.Awake()` (see `ArcadeBasicUIBuilder.cs`). No prefab, no scene asset, no GUID drift.
  - **Menus**: File (New / Open / Save / Save As / Quit), Run (Run / Compile / Build standalone / Stop / Clear Output), Help (About).
  - **Source pane**: gutter + syntax-highlighted TMP_InputField + scrollbar, all sharing one ScrollRect so the gutter scrolls with the code and stays clipped to the visible source. Avoids the TMP 3.0.x `UpdateScrollbar()` graphic-rebuild collision by using the ScrollRect's scrollbar instead of `TMP_InputField.verticalScrollbar`.
  - **Output pane**: scrollable transcript with sticky-bottom auto-scroll (stays pinned to new output unless the user has scrolled up to read older lines).
  - **INPUT bar**: permanently visible at the bottom of the Output pane (matches TUI IDE). Read-only and blank-prompted when idle; flips to editable + focused with a `? ` prompt when the program runs `INPUT` / `LINE INPUT`.
  - **Problems pane**: bottom strip in the Source pane that auto-opens on diagnostics, with Copy + close (X) controls.
  - **About modal**: centered dialog dismissed by clicking OK.
  - **EventSystem**: auto-created if absent, picks `InputSystemUIInputModule` when the new Input System package is present, falls back to `StandaloneInputModule` otherwise.
  - Import via **Package Manager → Samples → Import**, then run **Window → Arcade BASIC → Samples → Create BASIC IDE Scene**.
- Unity editor integration (`Editor/` folder):
  - `Window → Arcade BASIC → Console` — REPL-style editor window with run + persistence.
  - `Assets → Create → Arcade BASIC → {Empty Program, Hello World, FOR Loop Demo, Function Demo}` — `.bas` templates.
  - `.bas` ScriptedImporter + custom inspector (source preview, ▶ Run button, output panel).
  - `Assets → Run Arcade BASIC Program` — context-menu runner for the selected `.bas` asset.
  - `Window → Arcade BASIC → Documentation / Conformance / About` — quick links.
