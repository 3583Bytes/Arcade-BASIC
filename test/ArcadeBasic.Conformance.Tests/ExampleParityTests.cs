using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;
using ArcadeBasic.Interpreter;
using ArcadeBasic.Compiler;
using ArcadeBasic.Vm;
using AstProgram = ArcadeBasic.Parser.Ast.Program;

namespace ArcadeBasic.Conformance.Tests;

/// <summary>
/// Engine-parity coverage for the bundled example programs. The whole design
/// rests on the tree-walking interpreter and the bytecode VM producing
/// byte-identical output, so this fixture guards that invariant end-to-end on
/// real programs (not just unit snippets).
///
/// Two tiers:
///   * <see cref="InterpreterAndVmAgree"/> — for examples that run to completion
///     deterministically (given the supplied stdin), assert identical output and
///     exit code from both engines.
///   * <see cref="EveryExampleCompilesOnBothEngines"/> — for *every* example,
///     assert it analyzes cleanly and the VM compiler accepts it. This catches
///     "feature works in the interpreter but the VM compiler chokes" gaps even
///     for input-driven or RND-seeded programs we can't compare by output.
/// </summary>
public class ExampleParityTests
{
    /// <summary>(file, stdin) for examples that terminate deterministically.</summary>
    public static IEnumerable<object[]> DeterministicExamples() =>
    [
        ["hello.bas", ""],
        ["factorial.bas", ""],
        ["fibonacci.bas", ""],
        ["primes.bas", ""],
        ["strings.bas", ""],
        ["matrix.bas", ""],
        ["exception.bas", ""],
        ["formatted.bas", ""],
        ["modules.bas", ""],
        ["pi.bas", ""],
        ["mandelbrot.bas", ""],        // pure compute, no RND/input — both engines must match byte-for-byte
        ["guess.bas", "7\n"],          // TARGET is hard-coded to 7 — no RND
    ];

    public static IEnumerable<object[]> AllExamples() =>
        Directory.GetFiles(ExamplesDir(), "*.bas")
            .OrderBy(f => f)
            .Select(f => new object[] { Path.GetFileName(f) });

    [Theory]
    [MemberData(nameof(DeterministicExamples))]
    public void InterpreterAndVmAgree(string name, string stdin)
    {
        var (program, info) = FrontEnd(name);

        var (interpOut, interpExit) = RunInterpreter(program, info, stdin);
        var (vmOut, vmExit) = RunVm(program, info, stdin);

        Assert.Equal(interpOut, vmOut);
        Assert.Equal(interpExit, vmExit);
    }

    [Theory]
    [MemberData(nameof(AllExamples))]
    public void EveryExampleCompilesOnBothEngines(string name)
    {
        // The interpreter accepts everything by construction; the gap we guard
        // against is the VM compiler rejecting a feature (e.g. a statement it
        // hasn't learned to lower yet).
        var (program, info) = FrontEnd(name);
        var ex = Record.Exception(() => BasicCompiler.Compile(program, info));
        Assert.Null(ex);
    }

    [Fact]
    public void TerminalLineInputAgreesOnBothEngines()
    {
        // Regression: terminal LINE INPUT (no file channel) was unimplemented in
        // the interpreter (it fell through to the default no-op) while the VM read
        // it — a silent parity gap. Both must now read the whole line, commas
        // included, and emit identical prompts ("? " bare, a single space for the
        // ';' form).
        const string source =
            "LINE INPUT A$\n" +
            "LINE INPUT \"\"; B$\n" +
            "PRINT \"[\" & A$ & \"][\" & B$ & \"]\"\n";
        const string stdin = "hello, world\nsecond, line\n";

        var (program, info) = FrontEndSource("lineinput.bas", source);
        var (interpOut, interpExit) = RunInterpreter(program, info, stdin);
        var (vmOut, vmExit) = RunVm(program, info, stdin);

        Assert.Equal(interpOut, vmOut);
        Assert.Equal(interpExit, vmExit);
        Assert.Contains("[hello, world][second, line]", interpOut);  // whole lines, commas kept
    }

    /// <summary>(source, expected output) for FOR / SELECT CASE semantics. Regression:
    /// the VM re-evaluated the FOR limit and step every iteration (and treated every
    /// step as positive, so a STEP -1 loop never ran — breaking snake.bas and
    /// tetris.bas in standalone builds), re-ran the SELECT CASE subject for every
    /// CASE comparison, and the tree-walker ignored assignments to the control
    /// variable inside the body.</summary>
    public static IEnumerable<object[]> LoopSemantics() =>
    [
        // negative step counts down; v ends one step past the limit
        ["FOR I = 5 TO 1 STEP -1\nPRINT I;\nNEXT I\nPRINT\nPRINT I\n", " 5  4  3  2  1 \n 0 \n"],
        // ISO 10279 §8.3.5: the limit is evaluated once, on entry
        ["LET N = 3\nFOR I = 1 TO N\nPRINT I;\nLET N = 6\nNEXT I\nPRINT\n", " 1  2  3 \n"],
        // … and so is the step
        ["LET S = 1\nFOR I = 1 TO 4 STEP S\nPRINT I;\nLET S = 2\nNEXT I\nPRINT\n", " 1  2  3  4 \n"],
        // NEXT adds the step to the control variable's *current* value
        ["FOR I = 1 TO 10\nPRINT I;\nIF I = 3 THEN LET I = 8\nNEXT I\nPRINT\n", " 1  2  3  9  10 \n"],
        // a fractional negative step, and an empty range runs zero times
        ["FOR X = 1 TO 0 STEP -0.25\nPRINT X;\nNEXT X\nFOR Y = 1 TO 0\nPRINT Y;\nNEXT Y\nPRINT\n",
         " 1  0.75  0.5  0.25  0 \n"],
        // the SELECT CASE subject is evaluated exactly once
        ["LET CALLS = 0\nSELECT CASE NEXTV(0)\nCASE 1\nPRINT \"one\"\nCASE 2 TO 2\nPRINT \"two\"\n" +
         "CASE 3\nPRINT \"three\"\nEND SELECT\nPRINT CALLS\nEND\n" +
         "FUNCTION NEXTV(X)\nLET CALLS = CALLS + 1\nLET NEXTV = 3\nEND FUNCTION\n", "three\n 1 \n"],
    ];

    [Theory]
    [MemberData(nameof(LoopSemantics))]
    public void LoopAndSelectSemanticsAgreeOnBothEngines(string source, string expected)
    {
        var (program, info) = FrontEndSource("loops.bas", source);
        var (interpOut, interpExit) = RunInterpreter(program, info, "");
        var (vmOut, vmExit) = RunVm(program, info, "");

        Assert.Equal(expected, interpOut);
        Assert.Equal(expected, vmOut);
        Assert.Equal(0, interpExit);
        Assert.Equal(0, vmExit);
    }

    [Theory]
    [MemberData(nameof(DeterministicExamples))]
    public void NativeArithmeticPrintsTheSameAsDecimal(string name, string stdin)
    {
        // OPTION ARITHMETIC NATIVE applies program-wide wherever it appears, so
        // appending it leaves every line number (and so EXLINE) where it was. At
        // PRINT's 9-digit display these programs can't tell double from decimal.
        var source = File.ReadAllText(Path.Combine(ExamplesDir(), name));
        var (decProgram, decInfo) = FrontEndSource(name, source);
        var (program, info) = FrontEndSource(name, source + "\nOPTION ARITHMETIC NATIVE\n");
        Assert.Equal(ArcadeBasic.Parser.Ast.ArithmeticMode.Native, info.Arithmetic);

        var (decimalOut, _) = RunInterpreter(decProgram, decInfo, stdin);
        var (interpOut, interpExit) = RunInterpreter(program, info, stdin);
        var (vmOut, vmExit) = RunVm(program, info, stdin);

        Assert.Equal(decimalOut, interpOut);
        Assert.Equal(interpOut, vmOut);
        Assert.Equal(interpExit, vmExit);
    }

    /// <summary>(source, expected under DECIMAL, expected under NATIVE). Each snippet
    /// runs under both arithmetic modes on both engines.</summary>
    public static IEnumerable<object[]> ArithmeticSemantics() =>
    [
        // negative and fractional powers, INF/MAXNUM — all used to escape as .NET exceptions
        ["PRINT 2 ^ -1; 2 ^ -3; 10 ^ -5.5\n", " 0.5  0.125  0.0000031622777 \n", " 0.5  0.125  0.0000031622777 \n"],
        ["PRINT MAXNUM > 1E307; INF = MAXNUM\n", "-1 -1 \n", "-1 -1 \n"],
        // ISO 10279: STR$ is PRINT's representation without the surrounding spaces
        ["PRINT STR$(1/3) & \"|\" & STR$(-2.5)\n", "0.33333333|-2.5\n", "0.33333333|-2.5\n"],
        // NATIVE is binary floating point: decimal fractions are inexact …
        ["IF 0.1 + 0.2 = 0.3 THEN PRINT \"equal\" ELSE PRINT \"not equal\"\n", "equal\n", "not equal\n"],
        // … integers are exact only up to 2^53 …
        ["PRINT 2 ^ 53 + 1 - 2 ^ 53\n", " 1 \n", " 0 \n"],
        // … and arrays hold doubles too ((1/3)*3 rounds back to exactly 1)
        ["DIM A(3)\nLET A(2) = 1 / 3\nPRINT A(2) * 3 = 1\n", " 0 \n", "-1 \n"],
        // MOD takes the divisor's sign, REMAINDER the dividend's
        ["PRINT 7 MOD -3; -7 MOD 3; 7.5 MOD 2; -7 REMAINDER 3\n", "-2  2  1.5 -1 \n", "-2  2  1.5 -1 \n"],
        // MAT A = B copies (the VM used to leave A sharing B's storage)
        ["DIM A(3), B(3)\nMAT B = CON\nMAT A = B\nLET B(1) = 5\nPRINT A(1); B(1)\n", " 1  5 \n", " 1  5 \n"],
        // domain errors raise the spec's exceptions instead of crashing
        ["WHEN EXCEPTION IN\nPRINT SQR(-1)\nUSE\nPRINT EXTYPE\nEND WHEN\n", " 3005 \n", " 3005 \n"],
        ["WHEN EXCEPTION IN\nPRINT (-8) ^ 0.5\nUSE\nPRINT EXTYPE\nEND WHEN\n", " 3002 \n", " 3002 \n"],
        ["WHEN EXCEPTION IN\nPRINT 0 ^ -1\nUSE\nPRINT EXTYPE\nEND WHEN\n", " 3003 \n", " 3003 \n"],
        ["WHEN EXCEPTION IN\nPRINT ASIN(2)\nUSE\nPRINT EXTYPE\nEND WHEN\n", " 3007 \n", " 3007 \n"],
        // decimal is unbounded; a native overflow raises 1002
        ["LET X = 1E300\nWHEN EXCEPTION IN\nPRINT X * X > 0\nUSE\nPRINT \"overflow\"; EXTYPE\nEND WHEN\n",
         "-1 \n", "overflow 1002 \n"],
    ];

    [Theory]
    [MemberData(nameof(ArithmeticSemantics))]
    public void ArithmeticModesAgreeOnBothEngines(string source, string expectedDecimal, string expectedNative)
    {
        foreach (var (src, expected) in new[] { (source, expectedDecimal), (source + "OPTION ARITHMETIC NATIVE\n", expectedNative) })
        {
            var (program, info) = FrontEndSource("arith.bas", src);
            var (interpOut, _) = RunInterpreter(program, info, "");
            var (vmOut, _) = RunVm(program, info, "");

            Assert.Equal(expected, interpOut);
            Assert.Equal(expected, vmOut);
        }
    }

    [Fact]
    public void InternalFileRoundTripsExactlyOnBothEngines()
    {
        // INTERNAL records (WRITE #/READ #) preserve the exact value — including
        // precision well beyond the 9-digit display rounding — and the two engines
        // agree byte-for-byte.
        var path = Path.GetTempFileName();
        try
        {
            var src =
                $"OPEN #1: NAME \"{path}\", ACCESS OUTPUT, RECTYPE INTERNAL\n" +
                "WRITE #1: 1.234567890123456789, \"hi, there\"\n" +
                "CLOSE #1\n" +
                $"OPEN #1: NAME \"{path}\", ACCESS INPUT, RECTYPE INTERNAL\n" +
                "READ #1: X, S$\n" +
                "CLOSE #1\n" +
                "PRINT X - 1.234567890123456789\n" +   // 0 only if the full precision round-tripped
                "PRINT S$\n";                          // exact, comma and all (READ # doesn't split)
            var (program, info) = FrontEndSource("internal.bas", src);

            var (iOut, iExit) = RunInterpreter(program, info, "");
            var (vOut, vExit) = RunVm(program, info, "");

            Assert.Equal(0, iExit);
            Assert.Equal(0, vExit);
            Assert.Equal(iOut, vOut);
            var lines = iOut.Replace("\r", "").Split('\n');
            Assert.Equal("0", lines[0].Trim());       // exact round-trip past display precision
            Assert.Equal("hi, there", lines[1]);       // string verbatim (comma preserved)
        }
        finally
        {
            File.Delete(path);
        }
    }

    // -- helpers ---------------------------------------------------------

    private static (AstProgram Program, SemanticInfo Info) FrontEnd(string name) =>
        FrontEndSource(name, File.ReadAllText(Path.Combine(ExamplesDir(), name)));

    private static (AstProgram Program, SemanticInfo Info) FrontEndSource(string name, string source)
    {
        var file = new SourceFile(name, source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        Assert.False(diags.HasErrors,
            $"{name} produced analysis diagnostics:\n{string.Join("\n", diags.All.Select(d => d.Render(false)))}");
        return (program, info);
    }

    private static (string Output, int Exit) RunInterpreter(AstProgram program, SemanticInfo info, string stdin)
    {
        var sw = new StringWriter { NewLine = "\n" };
        var exit = new BasicInterpreter(program, info, sw, new StringReader(stdin)).Run();
        return (sw.ToString(), exit);
    }

    private static (string Output, int Exit) RunVm(AstProgram program, SemanticInfo info, string stdin)
    {
        var compiled = BasicCompiler.Compile(program, info);
        var sw = new StringWriter { NewLine = "\n" };
        var exit = new BasicVm(compiled, sw, new StringReader(stdin)).Run();
        return (sw.ToString(), exit);
    }

    private static string ExamplesDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "examples");
            if (File.Exists(Path.Combine(candidate, "hello.bas"))) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("could not locate the examples/ directory above the test binary");
    }
}
