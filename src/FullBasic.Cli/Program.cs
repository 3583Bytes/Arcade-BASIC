using FullBasic.Core;
using FullBasic.Lexer;
using FullBasic.Parser;
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
