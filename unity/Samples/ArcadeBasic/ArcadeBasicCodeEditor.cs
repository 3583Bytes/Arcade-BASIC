using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArcadeBasic;
using ArcadeBasic.Bytecode;
using ArcadeBasic.Compiler;
using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcadeBasic.Samples
{
    /// <summary>
    /// In-game code editor for Arcade BASIC. Two panes side-by-side:
    /// <list type="bullet">
    ///   <item><description><b>Editor pane</b> — multi-line input with a live line-number gutter and syntax-highlighted overlay (keywords, strings, numbers, labels are colored).</description></item>
    ///   <item><description><b>Output pane</b> — scrollable transcript that streams program output as the interpreter prints, plus diagnostics and exit info.</description></item>
    /// </list>
    /// Each <b>Run</b> compiles and executes the editor's full source fresh — no
    /// accumulated session, no stateful surprises. <b>Stop</b> cancels a running
    /// program via a <see cref="CancellationToken"/> the interpreter checks
    /// between statements. <b>Examples</b> dropdown loads a <c>.bas</c>
    /// <see cref="TextAsset"/> from <c>Resources/&lt;<see cref="resourcesPath"/>&gt;/</c> into the editor.
    ///
    /// Generate a ready-to-play scene from <c>Window &#x2192; Arcade BASIC &#x2192; Samples &#x2192; Create BASIC IDE Scene</c>,
    /// or wire the fields manually.
    /// </summary>
    [AddComponentMenu("Arcade BASIC/Code Editor")]
    public sealed class ArcadeBasicCodeEditor : MonoBehaviour
    {
        [Header("Editor")]
        public TMP_InputField inputField;
        [Tooltip("TMP_Text rendered behind the invisible input text. Receives the syntax-highlighted source.")]
        public TMP_Text highlightOverlay;
        [Tooltip("Vertical gutter that mirrors the input's line count (1, 2, 3, ...).")]
        public TMP_Text gutterText;

        [Header("Output")]
        public TMP_Text outputText;
        public ScrollRect outputScroll;

        [Header("Tabs")]
        [Tooltip("Source pane root; SetActive(true) when Source tab selected.")]
        public GameObject sourcePane;
        [Tooltip("Output pane root; SetActive(true) when Output tab selected.")]
        public GameObject outputPane;
        public Button sourceTabButton;
        public Button outputTabButton;
        [Tooltip("Background color for the active tab button.")]
        public Color tabActiveColor = new Color(0.18f, 0.20f, 0.24f, 1f);
        [Tooltip("Background color for the inactive tab button.")]
        public Color tabInactiveColor = new Color(0.10f, 0.11f, 0.13f, 1f);
        [Tooltip("Text color used for the active tab's label.")]
        public Color tabActiveText = new Color(0.95f, 0.97f, 1f, 1f);
        [Tooltip("Text color used for inactive tab labels.")]
        public Color tabInactiveText = new Color(0.55f, 0.58f, 0.64f, 1f);

        [Header("Menus")]
        [Tooltip("Top-level button that opens the File menu.")]
        public Button fileMenuButton;
        [Tooltip("Top-level button that opens the Run menu.")]
        public Button runMenuButton;
        [Tooltip("Container of the File menu items. SetActive(true) when the menu is open.")]
        public GameObject fileMenuPanel;
        [Tooltip("Container of the Run menu items. SetActive(true) when the menu is open.")]
        public GameObject runMenuPanel;
        [Tooltip("Fullscreen click-catcher (excluding the menu bar) that closes any open menu when clicked.")]
        public Button menuBlocker;

        [Header("Menu items")]
        public Button fileNewItem;
        public Button fileOpenItem;
        public Button fileSaveItem;
        public Button fileSaveAsItem;
        public Button fileQuitItem;
        public Button runRunItem;
        public Button runCompileItem;
        public Button runBuildItem;
        public Button runStopItem;
        public Button runClearItem;

        [Header("Build standalone (optional)")]
        [Tooltip("Path to an `arcade-basic` AOT binary used as the build stub. Leave empty to auto-locate via PATH; if still not found, the Editor will prompt with a file picker.")]
        public string buildStubPath = string.Empty;

        [Header("INPUT bar (optional)")]
        [Tooltip("Single-line input field below the output pane. When the program runs an INPUT or LINE INPUT, this is enabled and focused; the user types their line and presses Enter to submit.")]
        public TMP_InputField inputLineField;
        [Tooltip("Optional prompt label shown next to the input line (e.g. \"? \"). The label is hidden while no INPUT is pending.")]
        public TMP_Text inputLinePromptLabel;

        [Header("Problems pane (optional)")]
        [Tooltip("Container anchored to the bottom of the Source pane. Auto-opens when compile or runtime diagnostics are produced; X button hides it, Copy button copies the diagnostic text to the system clipboard.")]
        public GameObject problemsPanel;
        [Tooltip("Title label at the top of the Problems pane. Updated to \"Problems (N)\" when diagnostics arrive.")]
        public TMP_Text problemsTitleLabel;
        [Tooltip("Body text displaying the diagnostics, one per line.")]
        public TMP_Text problemsText;
        [Tooltip("Hides the Problems pane on click.")]
        public Button problemsCloseButton;
        [Tooltip("Copies every line in the Problems pane to the system clipboard on click.")]
        public Button problemsCopyButton;

        [Header("Status")]
        public TMP_Text statusText;

        [Header("Optional legacy controls")]
        [Tooltip("Optional. Disabled while a program is running. Old wiring; nothing wires this in the menu-only layout.")]
        public Button runButton;
        [Tooltip("Optional. Old wiring.")]
        public Button stopButton;
        [Tooltip("Optional. Old wiring.")]
        public Button clearOutputButton;
        [Tooltip("Optional. Old wiring.")]
        public Button saveButton;
        [Tooltip("Optional. Used as the save-as target on Player builds; null in the menu-only Editor layout.")]
        public TMP_InputField filenameField;
        [Tooltip("Optional. Old wiring; superseded by the File menu.")]
        public TMP_Dropdown exampleDropdown;

        [Header("Behaviour")]
        [Tooltip("Cap on output transcript length (characters). 0 disables the cap.")]
        public int outputCharCap = 32000;
        [Tooltip("Where to load example .bas TextAssets from. Resources path, no leading slash.")]
        public string resourcesPath = "ArcadeBasicSamples";
        [Tooltip("Subfolder under Application.persistentDataPath where user-saved programs live.")]
        public string savedSubfolder = "ArcadeBasicSaved";
        [TextArea(2, 6)]
        [Tooltip("Seed source loaded into the editor on Awake.")]
        public string startingSource =
            "! Click Run (or press Ctrl/Cmd + Enter) to execute.\n" +
            "! Try the Examples dropdown for prebuilt programs.\n\n" +
            "FOR I = 1 TO 5\n" +
            "  PRINT \"square of \"; I; \" = \"; I * I\n" +
            "NEXT I\n" +
            "END\n";

        // --- Run state ---
        readonly StringBuilder _output = new();
        CancellationTokenSource _cts;
        Task<RunResult> _runTask;
        ThreadSafeWriter _liveWriter;
        int _liveCursor;

        TextAsset[] _examples = Array.Empty<TextAsset>();
        string _currentFilePath;   // absolute path on disk; null = untitled
        int _activeTab;            // 0 = source, 1 = output
        string _baseline = string.Empty;   // last-saved source; IsModified compares against it

        // --- INPUT handshake (main thread <-> Task thread) ---
        volatile bool _inputPending;       // BASIC task is blocked waiting for a line
        bool _inputBarActivated;           // UI already in "input mode"; don't re-prompt every Update
        string _inputResult;
        readonly ManualResetEventSlim _inputDone = new(initialState: false);

        string SavedDir => Path.Combine(Application.persistentDataPath, savedSubfolder);

        void Awake()
        {
            // Legacy controls (only active if a scene happens to wire them).
            if (runButton != null) runButton.onClick.AddListener(Run);
            if (stopButton != null) { stopButton.onClick.AddListener(Stop); stopButton.gameObject.SetActive(false); }
            if (clearOutputButton != null) clearOutputButton.onClick.AddListener(ClearOutput);
            if (saveButton != null) saveButton.onClick.AddListener(SaveCurrent);

            // Menus.
            if (fileMenuButton != null) fileMenuButton.onClick.AddListener(() => ToggleMenu(fileMenuPanel));
            if (runMenuButton != null) runMenuButton.onClick.AddListener(() => ToggleMenu(runMenuPanel));
            if (menuBlocker != null) menuBlocker.onClick.AddListener(CloseAllMenus);
            if (fileNewItem != null)    fileNewItem.onClick.AddListener(()    => { NewFile();        CloseAllMenus(); });
            if (fileOpenItem != null)   fileOpenItem.onClick.AddListener(()   => { OpenFileDialog(); CloseAllMenus(); });
            if (fileSaveItem != null)   fileSaveItem.onClick.AddListener(()   => { SaveCurrent();    CloseAllMenus(); });
            if (fileSaveAsItem != null) fileSaveAsItem.onClick.AddListener(() => { SaveAs();         CloseAllMenus(); });
            if (fileQuitItem != null)   fileQuitItem.onClick.AddListener(()   => { Quit();           CloseAllMenus(); });
            if (runRunItem != null)     runRunItem.onClick.AddListener(()     => { Run();            CloseAllMenus(); });
            if (runCompileItem != null) runCompileItem.onClick.AddListener(() => { CompileOnly();    CloseAllMenus(); });
            if (runBuildItem != null)   runBuildItem.onClick.AddListener(()   => { BuildStandalone(); CloseAllMenus(); });
            if (runStopItem != null)    runStopItem.onClick.AddListener(()    => { Stop();           CloseAllMenus(); });
            if (runClearItem != null)   runClearItem.onClick.AddListener(()   => { ClearOutput();    CloseAllMenus(); });
            CloseAllMenus();

            if (inputLineField != null)
            {
                inputLineField.onSubmit.AddListener(OnInputLineSubmitted);
                SetInputBarVisible(false);
            }

            if (problemsCloseButton != null) problemsCloseButton.onClick.AddListener(HideProblems);
            if (problemsCopyButton != null)  problemsCopyButton.onClick.AddListener(CopyProblems);
            HideProblems();

            if (sourceTabButton != null) sourceTabButton.onClick.AddListener(() => SelectTab(0));
            if (outputTabButton != null) outputTabButton.onClick.AddListener(() => SelectTab(1));
            SelectTab(0);   // initial: show source

            try { Directory.CreateDirectory(SavedDir); } catch { /* read-only env, ignore */ }

            if (inputField != null)
            {
                inputField.onValueChanged.AddListener(OnSourceChanged);
                if (!string.IsNullOrEmpty(startingSource)) inputField.text = startingSource;
                // Force one initial pass so the gutter + highlight reflect the
                // current text even if no onValueChanged fired.
                OnSourceChanged(inputField.text);
                // Treat the starting source as the clean baseline so IsModified
                // doesn't trip on a fresh, untouched scene.
                _baseline = inputField.text ?? string.Empty;
            }

            if (exampleDropdown != null)
            {
                RepopulateOpenMenu();
                exampleDropdown.onValueChanged.AddListener(OnOpenSelected);
            }

            SetStatus("Ready");
        }

        void Update()
        {
            DrainLiveOutput();

            // INPUT handshake: BASIC task asked for a line — turn on the
            // input bar and focus it. Idempotent across frames via _inputBarActivated.
            if (_inputPending && !_inputBarActivated)
            {
                _inputBarActivated = true;
                SetInputBarVisible(true);
                if (inputLineField != null)
                {
                    inputLineField.text = string.Empty;
                    inputLineField.ActivateInputField();
                    inputLineField.Select();
                }
                SelectTab(1);   // surface the output pane so the user can see context for the prompt
            }

            if (_runTask != null && _runTask.IsCompleted) FinishRun();
            HandleHotkeys();
        }

        void HandleHotkeys()
        {
            // Ctrl/Cmd+Enter runs the program regardless of which tab is active
            // and regardless of focus, so the user can hit it from the Output
            // tab without first switching back to Source.
            bool mod = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                    || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            if (mod && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                Run();
            }
        }

        // =====================================================================
        // Run lifecycle
        // =====================================================================

        /// <summary>Compile and run the current editor source. Wired to the Run button.</summary>
        public void Run()
        {
            if (_runTask != null) return;
            if (inputField == null || outputText == null) return;

            string source = inputField.text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(source))
            {
                AppendOutput("[nothing to run]\n");
                SelectTab(1);
                return;
            }

            SelectTab(1);   // auto-switch to Output tab so the user sees results stream in
            _cts = new CancellationTokenSource();
            _liveWriter = new ThreadSafeWriter();
            _liveCursor = 0;

            SetStatus("Running...");
            if (runButton != null) runButton.interactable = false;
            if (stopButton != null) stopButton.gameObject.SetActive(true);

            var token = _cts.Token;
            var writer = _liveWriter;
            // INPUT readers run on the BASIC task thread; the reader marshals
            // to the main thread via the _inputPending / _inputDone handshake.
            var stdin = new MainThreadInputReader(this, token);

            _runTask = Task.Run(() =>
            {
                try
                {
                    var res = BasicEngine.Run(source, writer, stdin: stdin, cancel: token);
                    return new RunResult(res.ExitCode, res.Diagnostics);
                }
                catch (Exception ex)
                {
                    return new RunResult(1, new[] { "host exception: " + ex.Message });
                }
            }, token);
        }

        /// <summary>Cancel the in-flight program. Wired to the Stop button.</summary>
        public void Stop()
        {
            try { _cts?.Cancel(); } catch (ObjectDisposedException) { /* race with FinishRun */ }
            // Unblock any pending INPUT reader so the task can observe cancellation.
            _inputDone.Set();
        }

        /// <summary>Wipe the output pane. Wired to the Clear Output button.</summary>
        public void ClearOutput()
        {
            _output.Clear();
            if (outputText != null) outputText.text = string.Empty;
        }

        void DrainLiveOutput()
        {
            if (_liveWriter == null) return;
            var snapshot = _liveWriter.Snapshot(out int totalLen);
            if (totalLen > _liveCursor)
            {
                AppendOutput(snapshot.Substring(_liveCursor, totalLen - _liveCursor));
                _liveCursor = totalLen;
            }
        }

        void FinishRun()
        {
            DrainLiveOutput();

            var task = _runTask;
            var cts = _cts;
            _runTask = null;
            _liveWriter = null;
            _cts = null;

            var result = task.Result;
            // Diagnostics flow to the Problems pane (auto-opens) rather than
            // streaming into the output transcript — matches the TUI IDE.
            // Status markers like [cancelled] / [exit N] still go to output.
            if (result.Diagnostics != null && result.Diagnostics.Count > 0)
                SetProblems(result.Diagnostics);
            switch (result.ExitCode)
            {
                case 0:  SetStatus("Ready"); break;
                case 2:  AppendOutput("[cancelled]\n"); SetStatus("Cancelled"); break;
                default: AppendOutput("[exit " + result.ExitCode + "]\n"); SetStatus("Error"); break;
            }

            try { cts?.Dispose(); } catch { /* ignore */ }

            // Clear any leftover INPUT-handshake state in case the program ended mid-prompt.
            _inputPending = false;
            _inputBarActivated = false;
            SetInputBarVisible(false);

            if (runButton != null) runButton.interactable = true;
            if (stopButton != null) stopButton.gameObject.SetActive(false);
            ScrollOutputToBottom();
        }

        // =====================================================================
        // New / Compile / INPUT bar
        // =====================================================================

        /// <summary>Reset the editor to an empty buffer. Prompts to save if the
        /// current buffer has unsaved changes (Editor build only — Player builds
        /// fall back to a one-click-bypass via the status line).</summary>
        public void NewFile()
        {
            if (_runTask != null) return;
            if (IsModified)
            {
#if UNITY_EDITOR
                int choice = UnityEditor.EditorUtility.DisplayDialogComplex(
                    "Unsaved changes",
                    "The current buffer has unsaved changes. Save now?",
                    "Save", "Cancel", "Discard");
                if (choice == 1) return;                        // Cancel
                if (choice == 0)
                {
                    SaveCurrent();
                    if (IsModified) return;                     // SaveAs cancelled → still dirty
                }
                // choice == 2 → Discard, fall through
#else
                // Player build: no native dialog. Print a hint and require a
                // second New click within the dirty window to confirm.
                if (!_newConfirmArmed)
                {
                    _newConfirmArmed = true;
                    SetStatus("Unsaved changes — click New again to discard");
                    return;
                }
#endif
            }
            _newConfirmArmed = false;
            if (inputField != null) inputField.text = string.Empty;
            _currentFilePath = null;
            _baseline = string.Empty;
            UpdateFilenameDisplay();
            SetStatus("New file");
            SelectTab(0);
        }
        bool _newConfirmArmed;

        /// <summary>Has the buffer changed since the last load/save?</summary>
        public bool IsModified =>
            (inputField != null ? inputField.text ?? string.Empty : string.Empty) != _baseline;

        /// <summary>Exit the IDE — stops Play mode inside the Unity Editor,
        /// or calls <see cref="Application.Quit"/> in a built Player. Prompts
        /// to save first (same way <see cref="NewFile"/> does) when the
        /// buffer is dirty.</summary>
        public void Quit()
        {
            if (IsModified)
            {
#if UNITY_EDITOR
                int choice = UnityEditor.EditorUtility.DisplayDialogComplex(
                    "Unsaved changes",
                    "The current buffer has unsaved changes. Save before quitting?",
                    "Save", "Cancel", "Discard");
                if (choice == 1) return;                        // Cancel
                if (choice == 0)
                {
                    SaveCurrent();
                    if (IsModified) return;                     // SaveAs cancelled → still dirty
                }
                // choice == 2 → Discard, fall through
#else
                if (!_quitConfirmArmed)
                {
                    _quitConfirmArmed = true;
                    SetStatus("Unsaved changes — click Quit again to discard");
                    return;
                }
#endif
            }
            _quitConfirmArmed = false;

            // Cancel any in-flight run so the BASIC task thread doesn't
            // outlive the Editor / Player.
            try { _cts?.Cancel(); } catch { /* race with FinishRun */ }
            _inputDone.Set();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
        bool _quitConfirmArmed;

        /// <summary>Compile-only: lex + parse + sema with no execution. Diagnostics
        /// surface in the output pane and the status text. Faster than Run for
        /// catching syntax/sema errors before kicking off a long-running program.</summary>
        public void CompileOnly()
        {
            if (_runTask != null) return;
            if (inputField == null) return;

            var source = inputField.text ?? string.Empty;
            var file = new SourceFile("<editor>", source);
            var diags = new DiagnosticBag();

            var tokens = new BasicLexer(file, diags).Lex();
            if (!diags.HasErrors)
            {
                var program = new BasicParser(tokens, file, diags).ParseProgram();
                if (!diags.HasErrors) Analyzer.Analyze(program, diags);
            }

            if (diags.HasErrors)
            {
                SetProblems(diags.All.Select(d => d.Render(useColor: false)).ToList());
                SetStatus("Compile failed");
            }
            else
            {
                HideProblems();
                SetStatus(diags.All.Count > 0 ? "Compiled OK (warnings)" : "Compiled OK");
            }
        }

        /// <summary>Build a self-contained native binary from the current source —
        /// same flow as the CLI's `arcade-basic build` and the TUI IDE's F7.
        /// Editor-only: needs to call <c>EditorUtility.SaveFilePanel</c> and the
        /// file produced is a desktop executable, not something a Player runtime
        /// can host. In Player builds this prints a status hint and bails.</summary>
        public void BuildStandalone()
        {
            if (_runTask != null) return;
            if (inputField == null) return;

#if !UNITY_EDITOR
            SetStatus("Build only available in Editor");
            return;
#else
            // 1. Compile to bytecode; surface any errors in the output pane.
            var source = inputField.text ?? string.Empty;
            var file = new SourceFile("<editor>", source);
            var diags = new DiagnosticBag();
            var tokens = new BasicLexer(file, diags).Lex();
            ArcadeBasic.Parser.Ast.Program ast = null;
            SemanticInfo info = null;
            if (!diags.HasErrors)
            {
                ast = new BasicParser(tokens, file, diags).ParseProgram();
                if (!diags.HasErrors) info = Analyzer.Analyze(ast, diags);
            }
            if (diags.HasErrors || ast == null || info == null)
            {
                SetProblems(diags.All.Select(d => d.Render(useColor: false)).ToList());
                SetStatus("Build failed");
                return;
            }

            ArcadeBasic.Bytecode.Program compiled;
            try
            {
                compiled = BasicCompiler.Compile(ast, info);
            }
            catch (Exception ex)
            {
                SelectTab(1);
                AppendOutput("Build error: " + ex.Message + "\n");
                SetStatus("Build failed");
                return;
            }

            // 2. Save dialog for the output binary — this comes first so the
            // user sees the dialog they expect (where to put the result),
            // not the stub-picker that only fires the first time.
            string defaultName = !string.IsNullOrEmpty(_currentFilePath)
                ? Path.GetFileNameWithoutExtension(_currentFilePath)
                : "program";
            string ext = Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "";
            string outputPath = UnityEditor.EditorUtility.SaveFilePanel(
                "Build standalone Arcade BASIC binary",
                DefaultBrowseDir(),
                defaultName,
                ext);
            if (string.IsNullOrEmpty(outputPath))
            {
                SetStatus("Build cancelled");
                return;
            }

            // 3. Find an `arcade-basic` AOT binary to use as the stub. Tried
            // automatically first; if nothing matches, show an explanatory
            // dialog before opening the file picker so the user knows the
            // second dialog is for the stub, not the output.
            string stub = LocateBuildStub();
            if (string.IsNullOrEmpty(stub))
            {
                bool pick = UnityEditor.EditorUtility.DisplayDialog(
                    "Locate arcade-basic",
                    "To build a standalone binary, Arcade BASIC needs to find an `arcade-basic` AOT executable to use as the runtime stub.\n\n" +
                    "It wasn't found on your PATH or in the Build Stub Path inspector field. Click Browse to point at one (any platform-matching `arcade-basic` from a release ZIP, or one built locally via `scripts/build-bundle.sh`).",
                    "Browse...", "Cancel");
                if (!pick) { SetStatus("Build cancelled"); return; }

                stub = UnityEditor.EditorUtility.OpenFilePanel(
                    "Locate `arcade-basic` AOT binary",
                    DefaultBrowseDir(), "");
                if (string.IsNullOrEmpty(stub) || !File.Exists(stub))
                {
                    SetStatus("Build cancelled");
                    return;
                }
                buildStubPath = stub;     // remember for next time this session
            }

            // 4. Append the serialized bytecode to a copy of the stub. Same
            // FB-BCEND framing the CLI uses at startup to find the payload.
            try
            {
                byte[] payload = BytecodeSerializer.Serialize(compiled);
                byte[] stubBytes = File.ReadAllBytes(stub);

                // If the stub already has an embedded payload (someone pointed
                // at an already-built binary), strip it so we don't grow on rebuild.
                var existing = EmbeddedPayload.TryRead(stub);
                if (existing != null)
                {
                    int strip = existing.Length + 12;
                    Array.Resize(ref stubBytes, stubBytes.Length - strip);
                }

                using (var fs = File.Create(outputPath))
                {
                    fs.Write(stubBytes, 0, stubBytes.Length);
                    EmbeddedPayload.Append(fs, payload);
                }

                // chmod +x on Unix-like platforms — File.SetUnixFileMode isn't
                // in .NET Standard 2.1, so shell out to /bin/chmod.
                if (Application.platform != RuntimePlatform.WindowsEditor)
                {
                    try
                    {
                        var p = System.Diagnostics.Process.Start("/bin/chmod", "755 \"" + outputPath + "\"");
                        p?.WaitForExit();
                    }
                    catch { /* best effort */ }
                }

                long outBytes = new FileInfo(outputPath).Length;
                SetStatus($"Built {Path.GetFileName(outputPath)} ({outBytes:N0} bytes, payload {payload.Length:N0})");
            }
            catch (Exception ex)
            {
                Debug.LogError("[ArcadeBasic.Editor] build failed: " + ex);
                SetProblems(new[] { "Build failed: " + ex.Message });
                SetStatus("Build failed");
            }
#endif
        }

#if UNITY_EDITOR
        /// <summary>Find an `arcade-basic` AOT binary. Order:
        /// (1) explicit <see cref="buildStubPath"/>, (2) the
        /// <c>Stubs/arcade-basic-&lt;rid&gt;</c> binary that ships with the
        /// imported sample, (3) anywhere on <c>PATH</c>. Returns null if
        /// nothing matches — the caller pops a file picker.</summary>
        string LocateBuildStub()
        {
            if (!string.IsNullOrEmpty(buildStubPath) && File.Exists(buildStubPath))
                return buildStubPath;

            // (2) Bundled sample stub: the package's auto-imported ArcadeBasic
            // sample includes a Stubs/ folder with arcade-basic-<rid> binaries.
            // We find ourselves on disk via MonoScript, then look for
            // Stubs/arcade-basic-<rid> next door.
            var bundled = LocateBundledStub();
            if (!string.IsNullOrEmpty(bundled)) return bundled;

            // (3) PATH walk — useful for users who installed arcade-basic
            // globally (homebrew, /usr/local/bin, etc.).
            string exe = Application.platform == RuntimePlatform.WindowsEditor
                ? "arcade-basic.exe" : "arcade-basic";
            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var dir in pathEnv.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    var candidate = Path.Combine(dir, exe);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            return null;
        }

        /// <summary>Resolve the bundled sample stub for the host RID. Walks
        /// up from this script's location to find a sibling <c>Stubs/</c>
        /// folder; chmods the file +x on Unix-like hosts so the build can
        /// actually exec it after the sample import (Unity strips +x bits).</summary>
        string LocateBundledStub()
        {
            string rid = HostRid();
            if (string.IsNullOrEmpty(rid)) return null;

            string scriptPath = UnityEditor.AssetDatabase.GetAssetPath(
                UnityEditor.MonoScript.FromMonoBehaviour(this));
            if (string.IsNullOrEmpty(scriptPath)) return null;

            string scriptDir = Path.GetDirectoryName(scriptPath);
            if (string.IsNullOrEmpty(scriptDir)) return null;

            string ext = rid == "win-x64" ? ".exe" : string.Empty;
            string candidate = Path.Combine(scriptDir, "Stubs", "arcade-basic-" + rid + ext);
            if (!File.Exists(candidate)) return null;

            EnsureExecutable(candidate);
            return candidate;
        }

        static string HostRid()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor) return "win-x64";
            if (Application.platform == RuntimePlatform.LinuxEditor)   return "linux-x64";
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                return SystemInfo.processorType.StartsWith("Apple", StringComparison.Ordinal)
                    ? "osx-arm64" : "osx-x64";
            }
            return null;
        }

        static void EnsureExecutable(string path)
        {
            if (Application.platform == RuntimePlatform.WindowsEditor) return;
            try
            {
                var p = System.Diagnostics.Process.Start("/bin/chmod", "755 \"" + path + "\"");
                p?.WaitForExit();
            }
            catch { /* best effort */ }
        }
#endif

        /// <summary>Populate and show the Problems pane. Empties / closes
        /// it when <paramref name="diagnostics"/> is null or empty. Switches
        /// to the Source tab so the pane is visible.</summary>
        public void SetProblems(IReadOnlyList<string> diagnostics)
        {
            if (problemsPanel == null) return;
            int count = diagnostics?.Count ?? 0;
            if (count == 0) { HideProblems(); return; }
            if (problemsTitleLabel != null)
                problemsTitleLabel.text = $"Problems ({count})";
            if (problemsText != null)
                problemsText.text = string.Join("\n", diagnostics);
            problemsPanel.SetActive(true);
            SelectTab(0);
        }

        /// <summary>Hide the Problems pane (called from its X button).</summary>
        public void HideProblems()
        {
            if (problemsPanel != null) problemsPanel.SetActive(false);
        }

        /// <summary>Copy the Problems pane's contents to the system clipboard
        /// via <see cref="GUIUtility.systemCopyBuffer"/>. Updates the status
        /// line with a confirmation.</summary>
        public void CopyProblems()
        {
            if (problemsText == null) return;
            string body = problemsText.text ?? string.Empty;
            if (string.IsNullOrEmpty(body)) { SetStatus("No problems to copy"); return; }
            GUIUtility.systemCopyBuffer = body;
            // "Problems (3)" → 3
            int count = 0;
            foreach (var ch in body) if (ch == '\n') count++;
            if (body.Length > 0) count++;
            SetStatus($"Copied {count} problem{(count == 1 ? "" : "s")} to clipboard");
        }

        void SetInputBarVisible(bool visible)
        {
            if (inputLineField != null)
            {
                inputLineField.gameObject.SetActive(visible);
                inputLineField.interactable = visible;
                if (!visible) inputLineField.text = string.Empty;
            }
            if (inputLinePromptLabel != null)
            {
                inputLinePromptLabel.gameObject.SetActive(visible);
                if (visible) inputLinePromptLabel.text = "? ";
            }
        }

        void OnInputLineSubmitted(string text)
        {
            if (!_inputPending) return;
            _inputResult = text ?? string.Empty;
            _inputPending = false;
            _inputBarActivated = false;
            SetInputBarVisible(false);
            // Echo the user's line into the transcript so the program's output
            // reads naturally (prompt → user typed → next line).
            AppendOutput(_inputResult + "\n");
            _inputDone.Set();
        }

        // =====================================================================
        // Editor: gutter + highlight overlay
        // =====================================================================

        void OnSourceChanged(string text)
        {
            UpdateGutter(text);
            UpdateHighlight(text);
        }

        void UpdateGutter(string text)
        {
            if (gutterText == null) return;
            int lines = 1;
            if (text != null)
            {
                for (int i = 0; i < text.Length; i++)
                    if (text[i] == '\n') lines++;
            }
            var sb = new StringBuilder(lines * 4);
            for (int i = 1; i <= lines; i++) sb.Append(i).Append('\n');
            gutterText.text = sb.ToString();
        }

        void UpdateHighlight(string text)
        {
            if (highlightOverlay == null) return;
            highlightOverlay.text = BasicSyntaxHighlighter.Highlight(text);
        }

        // =====================================================================
        // Open dropdown (Browse... + bundled examples) + Save
        // =====================================================================

        /// <summary>
        /// Rebuild the Open dropdown. First entry after the placeholder is a
        /// "Browse..." action that opens the native OS file dialog (Editor
        /// only). The rest are bundled examples loaded from
        /// <see cref="Resources.LoadAll{T}(string)"/> at <see cref="resourcesPath"/>.
        /// </summary>
        public void RepopulateOpenMenu()
        {
            if (exampleDropdown == null) return;

            _examples = Resources.LoadAll<TextAsset>(resourcesPath) ?? Array.Empty<TextAsset>();

            exampleDropdown.ClearOptions();
            var labels = new List<string> { "Open ▾", "📂 Browse..." };
            foreach (var ex in _examples) labels.Add("📦 " + ex.name);
            exampleDropdown.AddOptions(labels);
            exampleDropdown.SetValueWithoutNotify(0);
        }

        void OnOpenSelected(int index)
        {
            if (index <= 0) return;
            if (index == 1)
            {
                OpenFileDialog();
            }
            else
            {
                int exIdx = index - 2;
                if (exIdx < 0 || exIdx >= _examples.Length) return;
                var ex = _examples[exIdx];
                inputField.text = ex.text;
                inputField.MoveTextStart(false);
                _currentFilePath = null;          // examples aren't on disk; Save will prompt
                if (filenameField != null) filenameField.text = ex.name;
                SetStatus("Loaded example " + ex.name);
            }
            exampleDropdown.SetValueWithoutNotify(0);
        }

        /// <summary>
        /// Pop the native OS open dialog (Editor only) and load the chosen
        /// <c>.bas</c> file. Remembers the path so subsequent Saves write back
        /// to the same file.
        /// </summary>
        public void OpenFileDialog()
        {
#if UNITY_EDITOR
            string path = UnityEditor.EditorUtility.OpenFilePanel(
                "Open Arcade BASIC program", DefaultBrowseDir(), "bas");
            if (string.IsNullOrEmpty(path)) return;
            LoadFromPath(path);
#else
            SetStatus("Browse only available in Editor");
#endif
        }

        /// <summary>
        /// Save to the currently-open path if there is one; otherwise open the
        /// native Save As dialog (Editor) or fall back to writing
        /// <c>persistentDataPath/&lt;filename&gt;.bas</c> using the filename field
        /// (Player builds).
        /// </summary>
        public void SaveCurrent()
        {
            if (inputField == null) return;

            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                WriteToPath(_currentFilePath);
                return;
            }

#if UNITY_EDITOR
            SaveAs();
#else
            // Player fallback — type a name in the filename field, save to persistentDataPath.
            var raw = filenameField != null ? filenameField.text : null;
            raw = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
            if (raw == null) { SetStatus("Type a filename to save"); return; }

            string name = SanitizeFilename(raw);
            string fullName = name.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) ? name : name + ".bas";
            string path = Path.Combine(SavedDir, fullName);
            try { Directory.CreateDirectory(SavedDir); } catch { }
            WriteToPath(path);
#endif
        }

        /// <summary>Pop the native Save As dialog (Editor only).</summary>
        public void SaveAs()
        {
#if UNITY_EDITOR
            string defaultName = !string.IsNullOrEmpty(_currentFilePath)
                ? Path.GetFileNameWithoutExtension(_currentFilePath)
                : (filenameField != null && !string.IsNullOrWhiteSpace(filenameField.text)
                    ? SanitizeFilename(filenameField.text.Trim())
                    : "untitled");
            string path = UnityEditor.EditorUtility.SaveFilePanel(
                "Save Arcade BASIC program", DefaultBrowseDir(), defaultName, "bas");
            if (string.IsNullOrEmpty(path)) return;
            WriteToPath(path);
#else
            SetStatus("Save As only available in Editor");
#endif
        }

        void LoadFromPath(string path)
        {
            try
            {
                inputField.text = File.ReadAllText(path);
                inputField.MoveTextStart(false);
                _currentFilePath = path;
                _baseline = inputField.text ?? string.Empty;
                UpdateFilenameDisplay();
                SetStatus("Loaded " + Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                Debug.LogError("[ArcadeBasic.Editor] load failed: " + ex);
                SetStatus("Load failed");
            }
        }

        void WriteToPath(string path)
        {
            try
            {
                File.WriteAllText(path, inputField.text ?? string.Empty);
                _currentFilePath = path;
                _baseline = inputField.text ?? string.Empty;
                UpdateFilenameDisplay();
                SetStatus("Saved " + Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                Debug.LogError("[ArcadeBasic.Editor] save failed: " + ex);
                SetStatus("Save failed");
            }
        }

        void UpdateFilenameDisplay()
        {
            if (filenameField == null) return;
            filenameField.text = !string.IsNullOrEmpty(_currentFilePath)
                ? Path.GetFileNameWithoutExtension(_currentFilePath)
                : string.Empty;
        }

        string DefaultBrowseDir()
        {
            if (!string.IsNullOrEmpty(_currentFilePath)) return Path.GetDirectoryName(_currentFilePath);
            return Application.persistentDataPath;
        }

        static string SanitizeFilename(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.') sb.Append(c);
                else sb.Append('_');
            }
            var result = sb.ToString().Trim('.', '_', '-');
            return string.IsNullOrEmpty(result) ? "untitled" : result;
        }

        // =====================================================================
        // Output helpers
        // =====================================================================

        void AppendOutput(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _output.Append(text);
            if (outputCharCap > 0 && _output.Length > outputCharCap)
                _output.Remove(0, _output.Length - outputCharCap);
            if (outputText != null) outputText.text = _output.ToString();
        }

        void ScrollOutputToBottom()
        {
            if (outputScroll == null) return;
            Canvas.ForceUpdateCanvases();
            outputScroll.verticalNormalizedPosition = 0f;
        }

        void SetStatus(string s) { if (statusText != null) statusText.text = s; }

        // =====================================================================
        // Tabs
        // =====================================================================

        /// <summary>Switch the visible tab. 0 = source, 1 = output.</summary>
        public void SelectTab(int idx)
        {
            _activeTab = Mathf.Clamp(idx, 0, 1);
            if (sourcePane != null) sourcePane.SetActive(_activeTab == 0);
            if (outputPane != null) outputPane.SetActive(_activeTab == 1);
            ApplyTabVisuals(sourceTabButton, _activeTab == 0);
            ApplyTabVisuals(outputTabButton, _activeTab == 1);
        }

        void ApplyTabVisuals(Button btn, bool active)
        {
            if (btn == null) return;
            var img = btn.image != null ? btn.image : btn.GetComponent<Image>();
            if (img != null) img.color = active ? tabActiveColor : tabInactiveColor;
            var label = btn.GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (label != null) label.color = active ? tabActiveText : tabInactiveText;
        }

        // =====================================================================
        // Menus
        // =====================================================================

        GameObject _openMenu;

        /// <summary>Toggle a menu panel. Closes any other open menu first.</summary>
        public void ToggleMenu(GameObject panel)
        {
            if (panel == null) return;
            if (_openMenu == panel) { CloseAllMenus(); return; }
            CloseAllMenus();
            panel.SetActive(true);
            _openMenu = panel;
            if (menuBlocker != null) menuBlocker.gameObject.SetActive(true);
        }

        /// <summary>Close every popup menu (including the blocker).</summary>
        public void CloseAllMenus()
        {
            if (fileMenuPanel != null) fileMenuPanel.SetActive(false);
            if (runMenuPanel != null) runMenuPanel.SetActive(false);
            if (menuBlocker != null) menuBlocker.gameObject.SetActive(false);
            _openMenu = null;
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        readonly struct RunResult
        {
            public readonly int ExitCode;
            public readonly IReadOnlyList<string> Diagnostics;
            public RunResult(int exit, IReadOnlyList<string> diags) { ExitCode = exit; Diagnostics = diags; }
        }

        /// <summary>Thread-safe text sink the interpreter (on a Task) writes to while the main thread polls.</summary>
        sealed class ThreadSafeWriter : TextWriter
        {
            readonly StringBuilder _sb = new();
            readonly object _lock = new();
            public override Encoding Encoding => Encoding.UTF8;
            public override void Write(char value) { lock (_lock) _sb.Append(value); }
            public override void Write(string value) { lock (_lock) _sb.Append(value); }
            public override void Write(char[] buffer, int index, int count) { lock (_lock) _sb.Append(buffer, index, count); }
            public string Snapshot(out int totalLen) { lock (_lock) { totalLen = _sb.Length; return _sb.ToString(); } }
        }

        /// <summary>
        /// stdin for the interpreter. ReadLine runs on the BASIC Task thread;
        /// it signals the main thread (via <see cref="_inputPending"/>), blocks on
        /// <see cref="_inputDone"/>, and returns the user-submitted text. Stop
        /// fires the cancellation token, which we observe to throw
        /// OperationCanceledException — same clean-exit path the engine catches
        /// to surface exit code 2, rather than the 4003 "INPUT: end of input"
        /// runtime error that a plain null return would trigger.
        /// </summary>
        sealed class MainThreadInputReader : TextReader
        {
            readonly ArcadeBasicCodeEditor _editor;
            readonly CancellationToken _cancel;
            public MainThreadInputReader(ArcadeBasicCodeEditor editor, CancellationToken cancel)
            {
                _editor = editor;
                _cancel = cancel;
            }
            public override string ReadLine()
            {
                _cancel.ThrowIfCancellationRequested();
                _editor._inputResult = null;
                _editor._inputDone.Reset();
                _editor._inputPending = true;
                _editor._inputDone.Wait();
                _cancel.ThrowIfCancellationRequested();
                return _editor._inputResult;
            }
            public override int Read()
            {
                var line = ReadLine();
                return string.IsNullOrEmpty(line) ? -1 : line[0];
            }
        }
    }

    /// <summary>
    /// Converts Arcade BASIC source into a TextMeshPro rich-text string with
    /// color tags applied per token. Uses the project's own lexer (in the
    /// netstandard2.1 plugin DLLs) so the highlighting always matches what the
    /// interpreter will actually parse.
    /// </summary>
    public static class BasicSyntaxHighlighter
    {
        // VS Code "Dark+"–inspired palette.
        const string ColKeyword = "#569CD6";  // blue
        const string ColString  = "#CE9178";  // orange
        const string ColNumber  = "#B5CEA8";  // light green
        const string ColLabel   = "#858585";  // muted gray
        const string ColOp      = "#D4D4D4";  // off-white (operators / punct — default)

        public static string Highlight(string source)
        {
            if (string.IsNullOrEmpty(source)) return string.Empty;

            List<Token> tokens;
            try
            {
                var file = new SourceFile("<editor>", source);
                var diags = new DiagnosticBag();
                tokens = new BasicLexer(file, diags).Lex();
            }
            catch
            {
                return Escape(source);
            }

            var sb = new StringBuilder(source.Length + 256);
            int cursor = 0;
            foreach (var tok in tokens)
            {
                if (tok.Kind == TokenKind.EndOfFile) break;
                int start = tok.Span.Start;
                int len = tok.Span.Length;
                if (len == 0) continue;

                // Pass through any whitespace/skipped chars between tokens unchanged.
                if (start > cursor) sb.Append(Escape(source.Substring(cursor, start - cursor)));

                string color = ColorFor(tok.Kind);
                string text = Escape(source.Substring(start, len));
                if (color != null)
                {
                    sb.Append("<color=").Append(color).Append('>').Append(text).Append("</color>");
                }
                else
                {
                    sb.Append(text);
                }
                cursor = start + len;
            }
            if (cursor < source.Length) sb.Append(Escape(source.Substring(cursor)));
            return sb.ToString();
        }

        static string ColorFor(TokenKind kind)
        {
            // All BASIC reserved words are Kw* in the lexer's enum.
            if (kind.ToString().StartsWith("Kw", StringComparison.Ordinal)) return ColKeyword;
            return kind switch
            {
                TokenKind.StringLiteral  => ColString,
                TokenKind.NumericLiteral => ColNumber,
                TokenKind.LineLabel      => ColLabel,
                _ => null,
            };
        }

        // TMP rich text uses < and >; <noparse>...</noparse> tells TMP not to
        // interpret tags inside, so we can safely include any user text.
        static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.IndexOf('<') < 0 && s.IndexOf('>') < 0) return s;
            return "<noparse>" + s + "</noparse>";
        }
    }
}
