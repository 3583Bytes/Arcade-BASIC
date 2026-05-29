# Changelog

All notable changes to the Unity package will be documented here. Versions follow the upstream project's semver.

## [0.1.0] — Unreleased

### Fixed
- Import error "Reference has errors 'Singulink.Numerics.BigDecimal'" — `copy-dlls.sh` now emits a `.meta` file next to every plugin DLL with `validateReferences: 0`. Unity 2021.3+ otherwise tries to bind netstandard2.1 `System.*` references against its bundled BCL and rejects the assembly when the metadata identity doesn't match exactly.

### Added
- Initial Unity package: pre-built netstandard2.1 DLLs for the Arcade BASIC libraries.
- `ArcadeBasic.BasicEngine` static class as a one-call embedding entry point.
- `ArcadeBasic` sample: an in-game BASIC IDE — syntax-highlighted source pane, scrollable output transcript, single-line INPUT bar, File menu (New / Open / Save / Save As / Quit) and Run menu (Run / Compile / Build standalone / Stop / Clear Output), plus a Problems pane that mirrors the TUI IDE. **Auto-imported** with the package — open `Samples/ArcadeBasic/Scene/ArcadeBasicIDE.unity` or drop the `ArcadeBasicIDE` prefab into your own scene.
- Unity editor integration (`Editor/` folder):
  - `Window → Arcade BASIC → Console` — REPL-style editor window with run + persistence.
  - `Assets → Create → Arcade BASIC → {Empty Program, Hello World, FOR Loop Demo, Function Demo}` — `.bas` templates.
  - `.bas` ScriptedImporter + custom inspector (source preview, ▶ Run button, output panel).
  - `Assets → Run Arcade BASIC Program` — context-menu runner for the selected `.bas` asset.
  - `Window → Arcade BASIC → Documentation / Conformance / About` — quick links.
