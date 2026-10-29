using ArcadeBasic.Bytecode;
using ArcadeBasic.Cli;
using ArcadeBasic.Cli.Audio;
using ArcadeBasic.Compiler;
using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Runtime;
using ArcadeBasic.Sema;
using ArcadeBasic.Vm;
using Singulink.Numerics;

// Phase 10: if this binary has a bundled bytecode payload, run it directly
// without consulting argv (the user invoked the compiled BASIC program, not
// the CLI). Sub-commands of the CLI still work in the unbundled stub.
{
    var selfPath = Environment.ProcessPath;
    if (!string.IsNullOrEmpty(selfPath))
    {
        var payload = EmbeddedPayload.TryRead(selfPath);
        if (payload is not null)
        {
            try
            {
                var compiled = BytecodeSerializer.Deserialize(payload);
                return RunWithConsoleGraphics((input, gfx, kb) =>
                    new BasicVm(compiled, Console.Out, input, gfx, kb).Run());
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"bundled program failed to load: {ex.Message}");
                return 1;
            }
        }
    }
}

return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0)
    {
        PrintUsage();
        return 0;
    }

    return args[0] switch
    {
        "--version" or "-v" => PrintVersion(),
        "--bigdecimal-spike" => RunBigDecimalSpike(),
        "lex" => RunLex(args.AsSpan(1)),
        "parse" => RunParse(args.AsSpan(1)),
        "analyze" => RunAnalyze(args.AsSpan(1)),
        "run" => RunProgram(args.AsSpan(1)),
        "vm" => RunProgram(args.AsSpan(1)),   // its old name, from when run had its own engine
        "build" => RunBuild(args.AsSpan(1)),
        "repl" => new BasicRepl().Run(),
        "--help" or "-h" => PrintUsage(),
        _ => UnknownCommand(args[0]),
    };
}

static int PrintVersion()
{
    Console.WriteLine("arcade-basic 1.0.0");
    return 0;
}

static int PrintUsage()
{
    Console.WriteLine("""
        usage: arcade-basic <command> [args]

        commands:
          lex <file>            Run the lexer over <file> and print the token stream.
          parse <file>          Lex + parse <file> and pretty-print the AST.
          analyze <file>        Lex + parse + analyze; print symbol summary.
          run <file>            Compile <file> and run it (vm does the same).
          build <file> [-o <out>]  Compile to a self-contained binary that bundles the VM.
          repl                  Start an interactive Arcade BASIC session.
          --version             Print version info.
          --bigdecimal-spike    Run the BigDecimal smoke test.
          --help                Print this help.
        """);
    return 0;
}

static int UnknownCommand(string cmd)
{
    Console.Error.WriteLine($"unknown command: {cmd}");
    PrintUsage();
    return 2;
}

static int RunLex(ReadOnlySpan<string> args)
{
    if (args.Length != 1)
    {
        Console.Error.WriteLine("usage: arcade-basic lex <file>");
        return 2;
    }

    var path = args[0];
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"file not found: {path}");
        return 1;
    }

    var content = File.ReadAllText(path);
    var file = new SourceFile(path, content);
    var diags = new DiagnosticBag();
    var lexer = new BasicLexer(file, diags);
    var tokens = lexer.Lex();

    var useColor = !Console.IsErrorRedirected;
    foreach (var diag in diags.All)
    {
        Console.Error.Write(diag.Render(useColor));
    }

    foreach (var tok in tokens)
    {
        var (line, col) = tok.Span.StartPosition.LineCol;
        var displayText = tok.Text
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
        Console.WriteLine($"{line,4}:{col,-3}  {tok.Kind,-22}  {displayText}");
    }

    return diags.HasErrors ? 1 : 0;
}

static int RunParse(ReadOnlySpan<string> args)
{
    if (args.Length != 1)
    {
        Console.Error.WriteLine("usage: arcade-basic parse <file>");
        return 2;
    }

    var path = args[0];
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"file not found: {path}");
        return 1;
    }

    var content = File.ReadAllText(path);
    var file = new SourceFile(path, content);
    var diags = new DiagnosticBag();
    var tokens = new BasicLexer(file, diags).Lex();
    var program = new BasicParser(tokens, file, diags).ParseProgram();

    var useColor = !Console.IsErrorRedirected;
    foreach (var diag in diags.All)
    {
        Console.Error.Write(diag.Render(useColor));
    }

    Console.Write(AstPrinter.Print(program));

    return diags.HasErrors ? 1 : 0;
}

static int RunBuild(ReadOnlySpan<string> args)
{
    string? source = null;
    string? output = null;
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == "-o" && i + 1 < args.Length) { output = args[i + 1]; i++; }
        else if (source is null) source = args[i];
        else { Console.Error.WriteLine("usage: arcade-basic build <source> [-o <output>]"); return 2; }
    }
    if (source is null)
    {
        Console.Error.WriteLine("usage: arcade-basic build <source> [-o <output>]");
        return 2;
    }
    if (!File.Exists(source))
    {
        Console.Error.WriteLine($"file not found: {source}");
        return 1;
    }

    output ??= Path.ChangeExtension(source, null) ?? "a.out";
    if (string.IsNullOrEmpty(output) || output == source) output = "a.out";

    // Lex + parse (with its libraries) + sema + compile to bytecode.
    var diags = new DiagnosticBag();
    var program = LoadProgram(source, diags);
    var info = Analyzer.Analyze(program, diags);

    var useColor = !Console.IsErrorRedirected;
    foreach (var diag in diags.All) Console.Error.Write(diag.Render(useColor));
    if (diags.HasErrors) return 1;

    ArcadeBasic.Bytecode.Program compiled;
    try
    {
        compiled = BasicCompiler.Compile(program, info);
    }
    catch (BasicCompiler.UnsupportedFeatureException ex)
    {
        Console.Error.WriteLine($"build error: {ex.Message}");
        return 1;
    }

    var payload = BytecodeSerializer.Serialize(compiled);

    // Locate the running CLI binary; we use it as the VM stub.
    var stubPath = Environment.ProcessPath;
    if (string.IsNullOrEmpty(stubPath) || !File.Exists(stubPath))
    {
        Console.Error.WriteLine("build error: cannot locate the running CLI binary to use as a stub");
        return 1;
    }

    var stubBytes = File.ReadAllBytes(stubPath);

    // If our stub already has an embedded payload (e.g. user re-bundling),
    // strip it so we don't grow the binary on each rebuild.
    var existing = EmbeddedPayload.TryRead(stubPath);
    if (existing is not null)
    {
        var strip = existing.Length + 12;
        Array.Resize(ref stubBytes, stubBytes.Length - strip);
    }

    using (var fs = File.Create(output))
    {
        fs.Write(stubBytes, 0, stubBytes.Length);
        EmbeddedPayload.Append(fs, payload);
    }

    if (!OperatingSystem.IsWindows())
    {
        try
        {
            File.SetUnixFileMode(output,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch { /* best effort — chmod failure is non-fatal */ }
    }

    Console.WriteLine($"wrote {output} ({new FileInfo(output).Length:N0} bytes, payload {payload.Length:N0} bytes)");
    return 0;
}

static int RunAnalyze(ReadOnlySpan<string> args)
{
    if (args.Length != 1)
    {
        Console.Error.WriteLine("usage: arcade-basic analyze <file>");
        return 2;
    }

    var path = args[0];
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"file not found: {path}");
        return 1;
    }

    var diags = new DiagnosticBag();
    var program = LoadProgram(path, diags);
    var info = Analyzer.Analyze(program, diags);

    var useColor = !Console.IsErrorRedirected;
    foreach (var diag in diags.All)
    {
        Console.Error.Write(diag.Render(useColor));
    }

    Console.WriteLine($"Program scope ({info.ProgramScope.FrameSize} slots):");
    foreach (var (key, sym) in info.ProgramScope.Symbols.OrderBy(kv => kv.Key))
    {
        if (sym is BuiltinSymbol or ConstantSymbol) continue;
        Console.WriteLine($"  {key,-20}  {sym.GetType().Name}");
    }

    Console.WriteLine();
    Console.WriteLine($"DATA pool: {info.DataPool.Count} item(s)");
    foreach (var item in info.DataPool)
    {
        Console.WriteLine($"  {(item.IsString ? "string" : "number"),-7}  {item.Text}");
    }

    Console.WriteLine();
    Console.WriteLine($"Line labels: {info.LineLabels.Count}");
    foreach (var (lbl, stmt) in info.LineLabels.OrderBy(kv => kv.Key))
    {
        Console.WriteLine($"  {lbl,5}  {stmt.GetType().Name}");
    }

    return diags.HasErrors ? 1 : 0;
}

static int RunProgram(ReadOnlySpan<string> args)
{
    var files = ParseRenderOptions(args, out var svgPath, out var wavPath);
    if (files.Count < 1)
    {
        Console.Error.WriteLine("usage: arcade-basic run <main-file> [module-file ...] [--svg <out.svg>] [--wav <out.wav>]");
        return 2;
    }

    foreach (var path in files)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"file not found: {path}");
            return 1;
        }
    }

    // The main file with its libraries (LIBRARY), and any module files named after it,
    // whose statements go first — so their modules are set up before the main program.
    var diags = new DiagnosticBag();
    var mainProgram = LoadProgram(files[0], diags);
    var allStatements = new List<ArcadeBasic.Parser.Ast.Stmt>();
    foreach (var path in files.Skip(1))
    {
        var file = new SourceFile(path, File.ReadAllText(path));
        allStatements.AddRange(new BasicParser(new BasicLexer(file, diags).Lex(), file, diags).ParseProgram().Statements);
    }
    allStatements.AddRange(mainProgram.Statements);

    var combined = new ArcadeBasic.Parser.Ast.Program(mainProgram.Span, allStatements);
    var info = Analyzer.Analyze(combined, diags);

    var useColor = !Console.IsErrorRedirected;
    foreach (var diag in diags.All)
    {
        Console.Error.Write(diag.Render(useColor));
    }
    if (diags.HasErrors) return 1;

    ArcadeBasic.Bytecode.Program compiled;
    try
    {
        compiled = BasicCompiler.Compile(combined, info);
    }
    catch (BasicCompiler.UnsupportedFeatureException ex)
    {
        // Reached only by programs that survive sema but then break one of the
        // compiler's own rules (a bug to report, rather than the program's fault).
        Console.Error.WriteLine($"compile error: {ex.Message}");
        return 1;
    }

    var (audio, wav) = MakeAudio(wavPath);
    try
    {
        if (svgPath is not null)
        {
            var svg = new SvgGraphicsDevice();
            var exit = new BasicVm(compiled, Console.Out, Console.In, svg, null, audio).Run();
            File.WriteAllText(svgPath, svg.ToSvg());
            Console.Error.WriteLine($"wrote {svgPath}");
            return exit;
        }
        return RunWithConsoleGraphics((input, gfx, kb) =>
            new BasicVm(compiled, Console.Out, input, gfx, kb, audio).Run());
    }
    finally
    {
        (audio as IDisposable)?.Dispose();   // drain + close any real-time sink
        WriteWav(wavPath, wav);
    }
}

/// <summary>Choose the audio backend: a WAV recorder when <c>--wav</c> is given,
/// otherwise real-time playback on an interactive terminal (silent — no device —
/// when output is piped/redirected, mirroring the graphics backend).</summary>
static (IAudioDevice? audio, WavAudioDevice? wav) MakeAudio(string? wavPath)
{
    if (wavPath is not null) { var w = new WavAudioDevice(); return (w, w); }
    if (!Console.IsOutputRedirected)
        return (new RealtimeAudioDevice(() => PcmSinks.CreateDefault(AudioWarn), AudioWarn), null);
    return (null, null);
}

/// <summary>Surface a non-fatal audio failure (no device, backend init failure, a
/// mid-run write error) as a one-line stderr note so a silent run is explainable
/// rather than mysterious. Audio is best-effort — this never aborts the program.</summary>
static void AudioWarn(string message, Exception? error)
    => Console.Error.WriteLine(error is null ? $"audio: {message}" : $"audio: {message} ({error.Message})");

/// <summary>Write the rendered WAV to <paramref name="path"/> if a --wav device was used.</summary>
static void WriteWav(string? path, WavAudioDevice? audio)
{
    if (path is null || audio is null) return;
    File.WriteAllBytes(path, audio.ToBytes());
    Console.Error.WriteLine($"wrote {path}");
}

/// <summary>Run an engine with terminal graphics when stdout/stdin are an
/// interactive terminal: a presenting reader shows each frame before input, and
/// the terminal is always restored afterward. Falls back to the no-op device
/// (today's behaviour) when not interactive.</summary>
static int RunWithConsoleGraphics(Func<TextReader, IGraphicsDevice?, IKeyboard?, int> run)
{
    var device = ConsoleGraphics.TryCreate();
    // A console device means an interactive terminal, so INKEY$ gets a live
    // keyboard too (works even for a text-mode game that draws nothing).
    IKeyboard? keyboard = device is null ? null : new ConsoleKeyboard();
    TextReader input = device is null ? Console.In : new PresentingTextReader(Console.In, device);
    try
    {
        return run(input, device, keyboard);
    }
    finally
    {
        ConsoleGraphics.Finish(device, input, keyboard);
    }
}

/// <summary>Extract the optional "--svg &lt;path&gt;" and "--wav &lt;path&gt;"
/// output options, returning the remaining (file) arguments.</summary>
static List<string> ParseRenderOptions(ReadOnlySpan<string> args, out string? svgPath, out string? wavPath)
{
    svgPath = null;
    wavPath = null;
    var rest = new List<string>();
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == "--svg" && i + 1 < args.Length) { svgPath = args[++i]; }
        else if (args[i] == "--wav" && i + 1 < args.Length) { wavPath = args[++i]; }
        else rest.Add(args[i]);
    }
    return rest;
}

static int RunBigDecimalSpike()
{
    var failures = 0;
    void Check(string label, bool ok)
    {
        Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {label}");
        if (!ok) failures++;
    }

    Console.WriteLine("BigDecimal spike:");

    var a = BigDecimal.Parse("1.1");
    var b = BigDecimal.Parse("2.2");
    Check("1.1 + 2.2 == 3.3 exactly", (a + b) == BigDecimal.Parse("3.3"));

    var third = BigDecimal.Divide(BigDecimal.One, BigDecimal.Parse("3"), 10, RoundingMode.MidpointToEven);
    Check("1/3 to 10 digits = 0.3333333333", third == BigDecimal.Parse("0.3333333333"));

    var halfEven1 = BigDecimal.Round(BigDecimal.Parse("0.5"), 0, RoundingMode.MidpointToEven);
    Check("0.5 -> 0 (banker's rounding to even)", halfEven1 == BigDecimal.Zero);

    var halfEven2 = BigDecimal.Round(BigDecimal.Parse("1.5"), 0, RoundingMode.MidpointToEven);
    Check("1.5 -> 2 (banker's rounding to even)", halfEven2 == BigDecimal.Parse("2"));

    var halfUp = BigDecimal.Round(BigDecimal.Parse("0.5"), 0, RoundingMode.MidpointAwayFromZero);
    Check("0.5 -> 1 (half away from zero)", halfUp == BigDecimal.One);

    var big = BigDecimal.Parse("123456789012345678901234567890");
    Check("very large value parses + roundtrips", big.ToString() == "123456789012345678901234567890");

    var verySmall = BigDecimal.Parse("0.000000000000000000001");
    Check("very small value parses + roundtrips", verySmall.ToString() == "0.000000000000000000001");

    Console.WriteLine(failures == 0 ? "spike: PASS" : $"spike: FAIL ({failures} failures)");
    return failures == 0 ? 0 : 1;
}

/// <summary>A program and the libraries its LIBRARY statements name (and theirs), each found
/// in the folder of the file that names it.</summary>
static ArcadeBasic.Parser.Ast.Program LoadProgram(string path, DiagnosticBag diags) =>
    ProgramLoader.Load(new SourceFile(path, File.ReadAllText(path)), (name, from) =>
    {
        var library = Path.IsPathRooted(name) ? name : Path.Combine(Path.GetDirectoryName(from.Path) ?? "", name);
        return File.Exists(library) ? new SourceFile(library, File.ReadAllText(library)) : null;
    }, diags);
