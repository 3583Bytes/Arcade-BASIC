# In-game code editor sample

A drop-in Unity scene that gives your game a syntax-highlighted Arcade BASIC editor and a live output pane. Each Run executes the editor's full source fresh — no hidden session state.

```
┌──────────────────────────────────────────────────────────────────────────┐
│ File  Run                                                                │  Menu bar
├────────┬────────┬────────────────────────────────────────────────────────┤
│ Source │ Output │                                                        │  Tab bar
├────────┴────────┴────────────────────────────────────────────────────────┤
│ 1│ FOR I = 1 TO 5                                                        │
│ 2│   PRINT "square of "; I; " = "; I * I                                 │
│ 3│ NEXT I                                                                │  Active tab
│ 4│ END                                                                   │  (everything left over)
│ 5│ |                                                                     │
│  │                                                                       │
├──┴───────────────────────────────────────────────────────────────────────┤
│ Ready                                                                    │  Footer (status)
└──────────────────────────────────────────────────────────────────────────┘
```

Three menus at the top — **File** (New, Open, Save, Save As, Quit), **Run** (Run, Compile, Build standalone, Stop, Clear Output), and **Help** (About) — pop out small panels when clicked, and a fullscreen click-blocker dismisses them when you click anywhere else. Keyboard shortcuts mirror the TUI IDE: **F5** Run, **F6** Compile, **F7** Build, **Esc** Stop, **Ctrl/Cmd+Enter** Run; the status line shows a live **Ln L, Col C** while you edit, and **Tab** inserts two spaces. Three tabs below swap which pane fills the content area: **Source** (gutter + syntax-highlighted editor), **Output** (scrollable transcript with a single-line input bar at the bottom that activates whenever the program runs an `INPUT`), or **Graphics** (an arcade-style screen for §13 `GRAPH`/`SET` drawing). Clicking **Run** auto-switches to Output so you watch the program execute live; the first §13 draw auto-switches to Graphics. The footer is just a status line: `Ready`, `Saved foo.bas`, `Running...`, `Cancelled`, error text. Keywords are blue, strings orange, numbers green, line labels gray, colored by the project's own `BasicLexer` so the highlighting always matches what the interpreter will parse.

## Prerequisites

| What | Provided how |
|---|---|
| **TextMeshPro** package | Auto-installed — declared as a package dependency. |
| **TMP Essentials** (default font + shaders) | One-time **Window → TextMeshPro → Import TMP Essentials** (see note below). A package can't import these for you. |
| **Build Standalone** AOT stubs (per-OS native binaries) | Bundled in `Stubs/` **when you install the official release package** (it ships `win-x64` / `osx-x64` / `osx-arm64` / `linux-x64`). A git-URL/UPM install can't carry native binaries — there, point the IDE at an `arcade-basic` binary (file picker) or run `scripts/copy-stubs.sh`. |

## Setup

Import the sample on demand: **Window → Package Manager → Arcade BASIC Interpreter → Samples → Import** (next to **Arcade BASIC IDE**). Unity copies it into `Assets/Samples/Arcade BASIC Interpreter/<version>/Arcade BASIC IDE/`.

Then:

1. **Open the scene**: `…/Arcade BASIC IDE/Scene/ArcadeBasicIDE.unity` — or run **Window → Arcade BASIC → Samples → Open BASIC IDE Scene**, which finds and opens it for you.
2. **Press Play**, click **Run** (or **Ctrl/Cmd + Enter**).

If you want to start from scratch instead, drop the **ArcadeBasicIDE** prefab (`…/Arcade BASIC IDE/Prefab/ArcadeBasicIDE.prefab`) into any Canvas under an empty scene.

> **TextMeshPro Essentials.** If text appears as pink boxes, Unity prompted you to import TMP Essentials but you skipped it. Re-run **Window → TextMeshPro → Import TMP Essential Resources**, then reopen the scene.

## How it works

| Piece | What it does |
|---|---|
| **Editor pane** | A `TMP_InputField` whose own text component is rendered fully transparent. A sibling `TMP_Text` "highlight overlay" — same font/size/position — displays the colored version. The caret tracks the invisible field; the user sees the colored overlay. |
| **Line number gutter** | A `TMP_Text` to the left of the editor, updated on every `onValueChanged` to reflect the current line count. Top-right aligned, monospace. |
| **Syntax highlighting** | `BasicSyntaxHighlighter.Highlight(string)` runs `BasicLexer` on the source and emits TMP rich text with `<color>` tags per token kind. Token text is wrapped in `<noparse>` so a literal `<` in a string can't break parsing. |
| **Live streaming output** | The interpreter runs on a background `Task` writing into a thread-safe buffer; the main thread drains it in `Update` and appends to the output transcript. Long-running `FOR` loops feel responsive, not frozen. |
| **Graphics screen** (§13) | `GRAPH`/`SET` statements draw into a `RasterGraphicsDevice` (ARGB pixel buffer) on the BASIC thread; the main thread copies it into a `Texture2D` on a `RawImage` each frame (serialized by one lock; rows flipped for Unity). The Graphics tab + screen are **built at runtime**, so no prefab wiring is required. Auto-shows on the first draw. Resolution auto-sizes to the pane unless you set **Graphics Resolution** in the Inspector. |
| **Real-time input** (`INKEY$`) | While a program runs, key presses are forwarded to the `INKEY$` buffer on the main thread (a thread-safe queue the BASIC thread drains). Printable ASCII 32–126 pass through as one-character strings; the arrow keys map to `CHR$(0)` + 72/80/75/77 — identical to the TUI/CLI. Suppressed while an `INPUT` line is pending, and while a Ctrl/Cmd shortcut is held (so IDE hotkeys win). With `SLEEP` driving the frame rate, real-time games (Space Invaders) play right in the IDE. **Requires** the project's *Active Input Handling* (Project Settings → Player) to be **Input Manager (Old)** or **Both** — the sample reads keys via the legacy `Input` class. |
| **Stop button** | A `CancellationToken` is checked between BASIC statements and at the top of every `FOR`/`DO`/`GOSUB` iteration. Clicking Stop signals it; `BasicEngine.Run` returns `ExitCode = 2` and the output shows `[cancelled]`. Hidden when no program is running. |
| **New (File menu)** | Resets the editor to an empty buffer. In the Unity Editor, prompts via `EditorUtility.DisplayDialogComplex` if the current buffer has unsaved changes (Save / Cancel / Discard). In Player builds, falls back to a "click New again to confirm" pattern over the status line. |
| **Quit (File menu)** | Editor: stops Play mode via `EditorApplication.isPlaying = false`. Player: calls `Application.Quit`. Same unsaved-changes prompt as New (Save / Cancel / Discard in Editor; click-Quit-again-to-confirm in Player). Any in-flight Run is cancelled cleanly so the BASIC task thread doesn't outlive the host. |
| **Compile (Run menu)** | Lex → parse → semantic-analysis only, no execution. Surfaces compile errors in the output pane and updates the status line — `Compiled OK` / `Compile failed`. Useful for catching typos in a long program before kicking off a slow run. |
| **Build standalone (Run menu)** | Editor-only. Pops a **target-platform menu** — Windows (x64), macOS (Apple Silicon), macOS (Intel), Linux (x64) — then compiles the source to bytecode and appends it to that platform's `arcade-basic` AOT stub. The bytecode is OS-agnostic, so you can build for any platform from one machine: the output's OS is just whichever stub you pick. A platform is enabled only if its stub is available; the host platform (marked *this platform*) additionally falls back to the `buildStubPath` field, `PATH`, or a file picker. The resulting binary runs with no .NET install on the target. **Cross-OS caveat:** a Unix binary built on Windows can't be marked executable there — the recipient runs `chmod +x` (and macOS Gatekeeper may need `xattr -d com.apple.quarantine` / right-click → Open, since the binary is unsigned). |
| **INPUT bar** | A `TMP_InputField` pinned to the bottom of the Output pane. Hidden while idle. When the BASIC program runs an `INPUT` or `LINE INPUT`, the bar appears + auto-focuses; you type a line, press Enter, the text is echoed into the transcript, and the interpreter resumes. Stop while waiting cancels cleanly (exit code 2, not a 4003 EOF error). |
| **Problems pane** | A strip at the bottom of the Source pane that auto-opens when compile or runtime diagnostics arrive — same pattern as the TUI IDE's Problems pane. Title shows the count (`Problems (3)`). **Copy** sends every diagnostic line to the system clipboard via `GUIUtility.systemCopyBuffer`. **✕** closes the pane without losing the editor underneath. A clean Run with no diagnostics auto-hides any previously-open pane. |
| **Open dropdown** | First entry is **📂 Browse...** → opens the native OS open dialog (Editor only) so you can load any `.bas` file from anywhere on disk. Subsequent entries (📦) are the bundled examples in `Resources/ArcadeBasicSamples/`. |
| **Filename field** | Single-line display of the current file (filename without extension). Auto-fills when you load via Browse or pick an example. Editable; on Player builds it doubles as the save-as target. |
| **Save button** | If the editor's content came from a file on disk, writes back to that same path. If it's a fresh/example program with no path, opens the native **Save As** dialog (Editor) or writes to `<persistentDataPath>/ArcadeBasicSaved/<filename>.bas` using the filename field (Player). Overwrites without warning if a same-named file already exists. |
| **Clear button** | Wipes the output pane only. The editor source is untouched. |

## Build standalone — stub binaries

The Build Standalone command needs an `arcade-basic` AOT executable to use
as the runtime stub. The released package (downloaded via UPM or the
release zip) ships a pre-built `arcade-basic` for every supported RID
inside the sample's `Stubs/` folder:

```
Stubs/
├── arcade-basic-linux-x64
├── arcade-basic-osx-arm64
├── arcade-basic-osx-x64
└── arcade-basic-win-x64.exe
```

Once you import the sample, the IDE finds the matching binary for your
Editor's host RID automatically and `chmods +x` it on Unix. No setup.

**If you're working in-repo** (e.g. you added the package via Unity's
"Add package from disk"), the `Stubs/` folder is empty until you
populate it. The repo includes a script that does this for the host
platform in one command:

```sh
unity/scripts/copy-stubs.sh                 # host RID only (fast)
unity/scripts/copy-stubs.sh --all           # build for all four shipped RIDs
unity/scripts/copy-stubs.sh osx-arm64       # one specific RID
```

Each invocation runs `dotnet publish ... -p:PublishAot=true` and copies
the resulting binary into `Stubs/` with the right RID-tagged filename.
Requires the .NET 9 SDK on your machine.

> **Lockstep**: `copy-stubs.sh` also re-runs `copy-dlls.sh` first, so the
> bytecode-serializer version in `unity/Runtime/Plugins/*.dll` (used by the
> IDE to write payloads) always matches the bytecode-deserializer version
> baked into the AOT stub (used by the resulting binary to read them).
> If you ever see `bundled program failed to load: bytecode: unsupported
> version <n>` after rebuilding source, that's the symptom of those two
> falling out of sync — just rerun `unity/scripts/copy-stubs.sh`.

## Performance notes

- **Syntax highlighting** runs on every keystroke. The lexer is O(n) over source length; for typical BASIC programs (~100 lines) it's sub-millisecond and unnoticeable. For pasting in Lunar Lander (300+ lines) it's still <5 ms.
- **Numeric arithmetic** is arbitrary-precision (`Singulink.Numerics.BigDecimal`) — great for spec conformance, slow for tight loops. Use BASIC for high-level game scripting, not per-frame physics.

## Manual setup

If you don't want to use the scene builder, drop `ArcadeBasicCodeEditor` on any GameObject and fill these fields:

| Required | Field | Type |
|---|---|---|
| ✓ | `inputField` | `TMP_InputField` (multi-line) |
| ✓ | `highlightOverlay` | `TMP_Text` (sibling of inputField's text component, same font/size, `richText = true`) |
| ✓ | `outputText` | `TMP_Text` |
|   | `gutterText` | `TMP_Text` to the left of the editor |
|   | `outputScroll` | `ScrollRect` enclosing `outputText` (enables auto-scroll on output) |
|   | `runButton`, `stopButton`, `clearOutputButton`, `saveButton` | `Button`s |
|   | `filenameField` | `TMP_InputField` (single-line) — current document name + save-as target |
|   | `exampleDropdown` | `TMP_Dropdown` (renamed mentally to "Open", but the field name kept for back-compat) |
|   | `statusText` | `TMP_Text` (Ready / Running... / Saved name / Loaded name / Cancelled / Error) |

The key invariant for the highlight overlay: it must be a sibling of the input field's text component (typically inside the input's `Text Area`) with **identical font, font size, character spacing, line spacing, and margins**. Any mismatch and the caret drifts off the visible characters. If you're hand-wiring this, copy the field values from each other after creation.

## Loading and saving files

The sample uses the **native OS file dialogs** when running inside the Unity Editor — `EditorUtility.OpenFilePanel` / `EditorUtility.SaveFilePanel`. You get a real Finder/Explorer/Files window, can navigate anywhere on disk, and can save to any folder you can write to. Click **Open ▾ → 📂 Browse...** to load; click **Save** on a freshly-typed program to get the Save As dialog.

Once you've loaded a file, the path sticks. Subsequent **Save** clicks write back to that same file without prompting — like every text editor you've used. The filename field reflects the current document (without the extension). Pick another example or Browse... to switch documents.

In a **built Player**, `UnityEditor.*` isn't available, so the buttons fall back gracefully:
- **Open ▾ → Browse...** shows "Browse only available in Editor".
- **Save** on an untitled program writes to `Application.persistentDataPath/ArcadeBasicSaved/<filename>.bas`, using whatever you've typed in the filename field. Sanitized to `[A-Za-z0-9_.\-]`.

`persistentDataPath` depends on the platform — on macOS it's `~/Library/Application Support/<company>/<product>/`, on Windows `%userprofile%\AppData\LocalLow\<company>\<product>\`. For real player-facing file I/O on desktop, drop in a plugin like [StandaloneFileBrowser](https://github.com/gkngkc/UnityStandaloneFileBrowser) and replace the `#if UNITY_EDITOR` branch — it's a single-file MIT-licensed wrapper around the native dialogs.

## Adding bundled examples

Drop `.bas` files into the imported sample's `Resources/ArcadeBasicSamples/` folder (`Assets/Samples/Arcade BASIC Interpreter/<version>/Arcade BASIC IDE/Resources/ArcadeBasicSamples/`). Unity imports them as `TextAsset`s via the package's `BasicScriptedImporter`; the Open dropdown rebuilds on `Awake` and shows them with a 📦 prefix. Use a sort-prefix (`01_Hello.bas`, `02_Pi.bas`, ...) for deterministic ordering within a group.

The Open dropdown **groups** examples under headers (Graphics, Games, Basics, …). A program's group comes from a `@category <Name>` tag in a leading comment — add one anywhere in the file's first comment line:

```basic
! my demo  @category Graphics
```

Untagged programs fall under **Basics**. Group order is Graphics → Games → Basics → anything else. (The console TUI IDE reads the same tag and renders the groups as nested submenus.)

## Feeding `INPUT` from C#

The scene-built sample already wires `INPUT` to the bottom input bar — `INPUT N`, `INPUT "Name: "; A$`, `INPUT A, B, C`, and `LINE INPUT A$` all just work. The interpreter runs on a background `Task`; when it asks for a line, the reader signals the main thread to enable + focus the input bar, then blocks until the user presses Enter. Echoing into the transcript happens automatically so the conversation reads naturally. Pressing **Stop** while the bar is waiting raises `OperationCanceledException` through the engine's clean-exit path (`ExitCode = 2`) — no `4003 INPUT: end of input stream` runtime error.

If you're integrating BASIC into your own UI (not using the bundled scene), pass any `TextReader` to `BasicEngine.Run`:

```csharp
using var stdin = new StringReader("42\nhello\n");           // canned test input
BasicEngine.Run(source, stdout, stdin: stdin, cancel: token);
```

For real interactive input, the `MainThreadInputReader` class inside `ArcadeBasicCodeEditor.cs` is a complete, copy-pasteable pattern: a `TextReader` that defers `ReadLine` to the main thread via a `ManualResetEventSlim` handshake and a `CancellationToken` for clean cancellation.
