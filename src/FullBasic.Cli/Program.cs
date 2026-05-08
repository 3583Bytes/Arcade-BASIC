using FullBasic.Core;
using FullBasic.Interpreter;
using FullBasic.Lexer;
using FullBasic.Parser;
using FullBasic.Sema;
using Singulink.Numerics;

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
        "--help" or "-h" => PrintUsage(),
        _ => UnknownCommand(args[0]),
    };
}

static int PrintVersion()
{
    Console.WriteLine("full-basic 0.0.0 (Phase 1)");
    return 0;
}

static int PrintUsage()
{
    Console.WriteLine("""
        usage: full-basic <command> [args]

        commands:
          lex <file>            Run the lexer over <file> and print the token stream.
          parse <file>          Lex + parse <file> and pretty-print the AST.
          analyze <file>        Lex + parse + analyze; print symbol summary.
          run <file>            Lex + parse + analyze + run the program.
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
        Console.Error.WriteLine("usage: full-basic lex <file>");
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
        Console.Error.WriteLine("usage: full-basic parse <file>");
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

static int RunAnalyze(ReadOnlySpan<string> args)
{
    if (args.Length != 1)
    {
        Console.Error.WriteLine("usage: full-basic analyze <file>");
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
    if (args.Length < 1)
    {
        Console.Error.WriteLine("usage: full-basic run <main-file> [module-file ...]");
        return 2;
    }

    foreach (var path in args)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"file not found: {path}");
            return 1;
        }
    }

    // Lex + parse each file. Modules go first in the combined statement list
    // so they're declaration-registered before the main file's executable
    // body runs (declarations don't execute, so this only affects ordering
    // of any future module-level init we add).
    var diags = new DiagnosticBag();
    var allStatements = new List<FullBasic.Parser.Ast.Stmt>();
    SourceFile? mainFile = null;
    var moduleFiles = new List<FullBasic.Parser.Ast.Program>();

    var mainPath = args[0];
    foreach (var path in args)
    {
        var content = File.ReadAllText(path);
        var file = new SourceFile(path, content);
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        if (path == mainPath)
        {
            mainFile = file;
        }
        else
        {
            moduleFiles.Add(program);
        }
        // Defer combining — we want modules first.
    }

    // Now combine: modules first, then main.
    foreach (var mod in moduleFiles) allStatements.AddRange(mod.Statements);
    // Re-parse main last (we already parsed it above; loop above stored
    // module programs but discarded the main one — re-find it).
    var mainContent = File.ReadAllText(mainPath);
    var mainFileObj = mainFile ?? new SourceFile(mainPath, mainContent);
    var mainTokens = new BasicLexer(mainFileObj, diags).Lex();
    var mainProgram = new BasicParser(mainTokens, mainFileObj, diags).ParseProgram();
    allStatements.AddRange(mainProgram.Statements);

    var combined = new FullBasic.Parser.Ast.Program(mainProgram.Span, allStatements);
    var info = Analyzer.Analyze(combined, diags);

    var useColor = !Console.IsErrorRedirected;
    foreach (var diag in diags.All)
    {
        Console.Error.Write(diag.Render(useColor));
    }
    if (diags.HasErrors) return 1;

    var interp = new BasicInterpreter(combined, info, Console.Out, Console.In);
    return interp.Run();
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
