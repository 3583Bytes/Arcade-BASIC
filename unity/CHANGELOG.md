# Changelog

All notable changes to the Unity package will be documented here. Versions follow the upstream project's semver.

## [0.1.0] — Unreleased

### Fixed
- Import error "Reference has errors 'Singulink.Numerics.BigDecimal'" — `copy-dlls.sh` now emits a `.meta` file next to every plugin DLL with `validateReferences: 0`. Unity 2021.3+ otherwise tries to bind netstandard2.1 `System.*` references against its bundled BCL and rejects the assembly when the metadata identity doesn't match exactly.

### Added
- Initial Unity package: pre-built netstandard2.1 DLLs for the Full BASIC libraries.
- `FullBasic.BasicEngine` static class as a one-call embedding entry point.
- `InGameConsole` sample: MonoBehaviour that runs a BASIC program from an inspector field.
- Unity editor integration (`Editor/` folder):
  - `Window → Full BASIC → Console` — REPL-style editor window with run + persistence.
  - `Assets → Create → Full BASIC → {Empty Program, Hello World, FOR Loop Demo, Function Demo}` — `.bas` templates.
  - `.bas` ScriptedImporter + custom inspector (source preview, ▶ Run button, output panel).
  - `Assets → Run Full BASIC Program` — context-menu runner for the selected `.bas` asset.
  - `Window → Full BASIC → Documentation / Conformance / About` — quick links.
