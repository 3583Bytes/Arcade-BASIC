using Terminal.Gui;

namespace ArcadeBasic.Ide;

/// <summary>
/// Top-level TUI host. Owns the menu bar, status bar, source pane, output pane,
/// and the <see cref="RunController"/> that drives BASIC program execution.
/// </summary>
internal sealed class TuiShell
{
    private readonly SourcePane _source = new();
    private readonly OutputPane _output = new();
    private readonly RunController _runner;
    private readonly StatusItem _statusItem;

    private TabView _tabs = null!;
    private TabView.Tab _sourceTab = null!;
    private TabView.Tab _outputTab = null!;

    private string? _currentFilePath;

    private TuiShell()
    {
        _statusItem = new StatusItem(Key.Null, "Ready", null);
        _runner = new RunController(_output, OnRunStateChanged, OnDiagnostics, OnInputRequested);
        _source.Problems.StatusMessage += SetStatus;
    }

    private void SetStatus(string message)
    {
        _statusItem.Title = message;
        Application.Top.SetNeedsDisplay();
    }

    public static int Run(string? initialFile)
    {
        Application.Init();
        try
        {
            var shell = new TuiShell();
            shell.Build(initialFile);
            Application.Run();
            return 0;
        }
        finally
        {
            Application.Shutdown();
        }
    }

    // -----------------------------------------------------------------------

    private void Build(string? initialFile)
    {
        var top = Application.Top;

        var menu = BuildMenu();
        var statusBar = new StatusBar(new[]
        {
            _statusItem,
            new StatusItem(Key.F5, "~F5~ Run", () => RunProgram()),
            new StatusItem(Key.Esc, "~Esc~ Stop", () => StopProgram()),
            new StatusItem(Key.CtrlMask | Key.S, "~^S~ Save", () => SaveCurrent()),
            new StatusItem(Key.CtrlMask | Key.O, "~^O~ Open", () => OpenDialog()),
            new StatusItem(Key.CtrlMask | Key.Q, "~^Q~ Quit", () => Application.RequestStop()),
        });

        _source.X = 0;
        _source.Y = 0;
        _source.Width = Dim.Fill();
        _source.Height = Dim.Fill();

        _output.X = 0;
        _output.Y = 0;
        _output.Width = Dim.Fill();
        _output.Height = Dim.Fill();

        _sourceTab = new TabView.Tab("Source", _source);
        _outputTab = new TabView.Tab("Output", _output);

        _tabs = new TabView
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill() - 1,
        };
        _tabs.AddTab(_sourceTab, true);
        _tabs.AddTab(_outputTab, false);

        // TabView selects a tab but doesn't always push focus into the inner
        // editable view — without this the terminal cursor stays parked on the
        // menu bar and typing into the editor looks blind.
        _tabs.SelectedTabChanged += (_, e) =>
        {
            if (e.NewTab == _sourceTab) RefocusEditor();
        };

        _source.CursorMoved += (line, col) =>
        {
            _statusItem.Title = _runner.IsRunning
                ? $"Running…   Ln {line}, Col {col}"
                : $"Ready      Ln {line}, Col {col}";
            statusBar.SetNeedsDisplay();
        };

        top.Add(menu, _tabs, statusBar);

        if (!string.IsNullOrEmpty(initialFile))
        {
            TryLoad(initialFile);
        }
        else
        {
            _source.SetText("! Press F5 to run.\n! Open the Run menu or File ▸ Examples for sample programs.\n\nPRINT \"Arcade BASIC by 3583Bytes.com\"\nEND\n");
        }

        // Force a visible terminal cursor and focus the editor so the user
        // can start typing immediately on launch.
        Application.Driver?.SetCursorVisibility(CursorVisibility.Default);
        _source.Editor.SetFocus();
    }

    private MenuBar BuildMenu()
    {
        var fileExamples = new MenuBarItem("E_xamples",
            ExamplesProvider.All
                .Select(ex => new MenuItem(ex.Name, string.Empty, () => LoadExample(ex)))
                .DefaultIfEmpty(new MenuItem("(none bundled)", string.Empty, null) { CanExecute = () => false })
                .ToArray());

        return new MenuBar(new MenuBarItem[]
        {
            new("_File", new MenuItem[]
            {
                new("_New", string.Empty, NewFile, shortcut: Key.CtrlMask | Key.N),
                new("_Open...", string.Empty, OpenDialog, shortcut: Key.CtrlMask | Key.O),
                new("_Save", string.Empty, SaveCurrent, shortcut: Key.CtrlMask | Key.S),
                new("Save _As...", string.Empty, SaveAs),
                null!,
                fileExamples,
                null!,
                new("_Quit", string.Empty, () => Application.RequestStop(), shortcut: Key.CtrlMask | Key.Q),
            }),
            new("_Run", new MenuItem[]
            {
                new("_Run", string.Empty, RunProgram, shortcut: Key.F5),
                new("_Compile", string.Empty, CompileProgram, shortcut: Key.F6),
                new("_Build standalone...", string.Empty, BuildStandalone, shortcut: Key.F7),
                new("_Stop", string.Empty, StopProgram, shortcut: Key.Esc),
                new("_Clear output", string.Empty, () => _output.ClearOutput(), shortcut: Key.CtrlMask | Key.L),
            }),
            new("_Help", new MenuItem[]
            {
                new("_About", string.Empty, About),
            }),
        });
    }

    // ---- Run / Stop --------------------------------------------------------

    private void RunProgram()
    {
        if (_runner.IsRunning) return;
        _output.ClearOutput();
        _tabs.SelectedTab = _outputTab;
        _runner.Run(_source.GetText());
    }

    private void StopProgram()
    {
        if (_runner.IsRunning) _runner.Stop();
    }

    private void OnDiagnostics(IReadOnlyList<string> diagnostics)
    {
        _source.Problems.SetDiagnostics(diagnostics);
        if (diagnostics.Count > 0)
        {
            _source.SetProblemsVisible(true);
            _tabs.SelectedTab = _sourceTab;
            RefocusEditor();
        }
    }

    private void CompileProgram()
    {
        if (_runner.IsRunning) return;

        var result = CompileService.Validate(_source.GetText());
        _source.Problems.SetDiagnostics(result.Diagnostics);

        if (!result.Ok || result.Diagnostics.Count > 0)
        {
            _source.SetProblemsVisible(true);
            _tabs.SelectedTab = _sourceTab;
        }

        _statusItem.Title = result.Ok ? "Compiled OK" : "Compile failed";
        Application.Top.SetNeedsDisplay();
        RefocusEditor();
    }

    private void BuildStandalone()
    {
        if (_runner.IsRunning) return;

        // Step 1: compile. Surface any errors in the Problems pane the same
        // way Run / Compile do, so the user fixes them before retrying.
        var result = CompileService.Compile(_source.GetText());
        _source.Problems.SetDiagnostics(result.Diagnostics);
        if (!result.Ok || result.Program is null)
        {
            _source.SetProblemsVisible(true);
            _tabs.SelectedTab = _sourceTab;
            _statusItem.Title = "Build failed (see Problems)";
            Application.Top.SetNeedsDisplay();
            RefocusEditor();
            return;
        }

        // Step 2: confirm we can find the AOT stub. If not, tell the user
        // exactly what to do — no point opening the Save dialog only to
        // fail at the very end.
        var stub = BuildService.LocateStub();
        if (stub is null)
        {
            MessageBox.ErrorQuery(70, 10, "Build standalone",
                "Could not find an `arcade-basic` AOT binary to use as the build stub.\n\n" +
                "Fix: download the matching `arcade-basic` from the releases page and either:\n" +
                "  • drop it next to arcade-basic-ide, or\n" +
                "  • add it to your PATH.",
                "OK");
            RefocusEditor();
            return;
        }

        // Step 3: ask where to write the output. Default name derived from
        // the currently-loaded file (or "program" if nothing is loaded yet).
        var defaultName = _currentFilePath is null
            ? "program"
            : Path.GetFileNameWithoutExtension(_currentFilePath);
        if (OperatingSystem.IsWindows()) defaultName += ".exe";

        var dlg = new SaveDialog("Build standalone",
            $"Save self-contained binary (stub: {Path.GetFileName(stub)})")
        {
            FilePath = defaultName,
        };
        string? outputPath;
        try
        {
            Application.Run(dlg);
            if (dlg.Canceled)
            {
                _statusItem.Title = "Build cancelled";
                Application.Top.SetNeedsDisplay();
                return;
            }
            outputPath = dlg.FilePath?.ToString();
        }
        finally
        {
            RefocusEditor();
        }
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            _statusItem.Title = "Build cancelled";
            Application.Top.SetNeedsDisplay();
            return;
        }

        // Step 4: do the build. BuildService handles stub-reading, payload
        // append, and chmod — same flow as the CLI's build subcommand.
        var buildResult = BuildService.Build(result.Program, outputPath, stub);
        if (buildResult.Ok)
        {
            _statusItem.Title = $"Built {Path.GetFileName(outputPath)} ({buildResult.OutputBytes:N0} bytes)";
        }
        else
        {
            MessageBox.ErrorQuery(70, 8, "Build failed", buildResult.Message, "OK");
            _statusItem.Title = "Build failed";
        }
        Application.Top.SetNeedsDisplay();
        RefocusEditor();
    }

    /// <summary>
    /// Restore editor focus and force the terminal cursor visible. Terminal.Gui
    /// occasionally drops cursor visibility after a Dialog/MessageBox closes
    /// or when focus moves to a non-editable view; calling this after every
    /// such interaction keeps the cursor reliably visible.
    /// </summary>
    private void RefocusEditor()
    {
        if (_tabs.SelectedTab == _sourceTab)
        {
            _source.Editor.SetFocus();
        }
        Application.Driver?.SetCursorVisibility(CursorVisibility.Default);
    }

    private void OnInputRequested()
    {
        _tabs.SelectedTab = _outputTab;
    }

    private void OnRunStateChanged(RunController.RunState state)
    {
        _statusItem.Title = state switch
        {
            RunController.RunState.Running => "Running…",
            RunController.RunState.Cancelled => "Cancelled",
            RunController.RunState.Failed => "Error",
            RunController.RunState.Succeeded => "Ready",
            _ => "Ready",
        };
        Application.Top.SetNeedsDisplay();
    }

    // ---- File operations ---------------------------------------------------

    private void NewFile()
    {
        if (!ConfirmDiscardChanges()) return;
        _source.SetText(string.Empty);
        _currentFilePath = null;
        _source.SetTitle("untitled");
        _statusItem.Title = "New file";
        if (_tabs.SelectedTab == _outputTab) _tabs.SelectedTab = _sourceTab;
        RefocusEditor();
    }

    /// <summary>
    /// If the buffer has unsaved changes, prompt the user. Returns true when
    /// the caller should proceed with the destructive action (user saved or
    /// chose to discard), false on Cancel / Esc / a cancelled SaveAs.
    /// </summary>
    private bool ConfirmDiscardChanges()
    {
        if (!_source.IsModified) return true;
        var choice = MessageBox.Query(
            60, 7,
            "Unsaved changes",
            "The current file has unsaved changes. Save now?",
            "Save", "Discard", "Cancel");
        return choice switch
        {
            0 => SaveAndCheck(),
            1 => true,
            _ => false,
        };

        bool SaveAndCheck()
        {
            SaveCurrent();
            // If SaveAs was cancelled the buffer is still dirty — treat as cancel.
            return !_source.IsModified;
        }
    }

    private void OpenDialog()
    {
        var dlg = new OpenDialog("Open program", "Select an Arcade BASIC file")
        {
            AllowsMultipleSelection = false,
            CanChooseDirectories = false,
            AllowedFileTypes = new[] { ".bas" },
        };
        try
        {
            Application.Run(dlg);
            if (dlg.Canceled) return;
            var path = dlg.FilePaths.FirstOrDefault();
            if (!string.IsNullOrEmpty(path)) TryLoad(path);
        }
        finally
        {
            RefocusEditor();
        }
    }

    private void TryLoad(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            _source.SetText(text);
            _currentFilePath = path;
            _source.SetTitle(Path.GetFileName(path));
            if (_tabs.SelectedTab == _outputTab) _tabs.SelectedTab = _sourceTab;
        }
        catch (Exception ex)
        {
            MessageBox.ErrorQuery("Open failed", ex.Message, "OK");
        }
        finally
        {
            RefocusEditor();
        }
    }

    private void SaveCurrent()
    {
        if (string.IsNullOrEmpty(_currentFilePath))
        {
            SaveAs();
            return;
        }
        WriteTo(_currentFilePath);
    }

    private void SaveAs()
    {
        var dlg = new SaveDialog("Save program", "Choose where to save")
        {
            AllowedFileTypes = new[] { ".bas" },
        };
        try
        {
            Application.Run(dlg);
            if (dlg.Canceled) return;
            var path = dlg.FilePath?.ToString();
            if (string.IsNullOrEmpty(path)) return;
            if (!path.EndsWith(".bas", StringComparison.OrdinalIgnoreCase)) path += ".bas";
            WriteTo(path);
        }
        finally
        {
            RefocusEditor();
        }
    }

    private void WriteTo(string path)
    {
        try
        {
            File.WriteAllText(path, _source.GetText());
            _currentFilePath = path;
            _source.SetTitle(Path.GetFileName(path));
            _source.MarkClean();
            _statusItem.Title = "Saved " + Path.GetFileName(path);
        }
        catch (Exception ex)
        {
            MessageBox.ErrorQuery("Save failed", ex.Message, "OK");
        }
        finally
        {
            RefocusEditor();
        }
    }

    private void LoadExample(ExamplesProvider.Example example)
    {
        _source.SetText(example.Source);
        _currentFilePath = null;
        _source.SetTitle(example.Name + " (example)");
        _statusItem.Title = "Loaded example " + example.Name;
        if (_tabs.SelectedTab == _outputTab) _tabs.SelectedTab = _sourceTab;
        RefocusEditor();
    }

    private void About()
    {
        try
        {
            MessageBox.Query(
                60, 12,
                "About",
                $"Arcade BASIC by 3583Bytes.com\n" +
                $"arcade-basic-ide {TuiInfo.Version}\n\n" +
                "A full-screen editor + runner for Arcade BASIC.\n" +
                "F5 runs · Esc stops · Ctrl-S saves · Ctrl-Q quits.",
                "OK");
        }
        finally
        {
            RefocusEditor();
        }
    }
}
