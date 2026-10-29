using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;
using ArcadeBasic.Compiler;
using ArcadeBasic.Vm;
using AstProgram = ArcadeBasic.Parser.Ast.Program;

namespace ArcadeBasic.Conformance.Tests;

/// <summary>
/// The bundled example programs, end to end, and the language semantics they rest
/// on (snippets with the output they must give). Two tiers for the examples:
///   * <see cref="EachExamplePrintsWhatItAlwaysHas"/> — for examples that run to
///     completion deterministically (given the supplied stdin), the output matches
///     the one recorded in ExampleOutputs/ (first recorded from the tree-walking
///     interpreter, the reference engine until the VM became the only one).
///   * <see cref="EveryExampleCompiles"/> — for *every* example, it analyzes cleanly
///     and the compiler accepts it, even the input-driven or RND-seeded ones.
/// </summary>
[Collection(ScoreFiles.Collection)]
public class ExampleTests
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
    public void EachExamplePrintsWhatItAlwaysHas(string name, string stdin)
    {
        var (program, info) = FrontEnd(name);
        var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ExampleOutputs", Path.ChangeExtension(name, ".txt")))
            .Replace("\r\n", "\n");   // (a Windows checkout may have turned the newlines into CRLF)

        Assert.Equal((expected, 0), RunVm(program, info, stdin));
    }

    /// <summary>Pausing after every single instruction — and at every INPUT, fed its
    /// line only once the VM asks — must not change what a program does: the VM's
    /// whole state survives between steps.</summary>
    [Theory]
    [MemberData(nameof(DeterministicExamples))]
    public void SteppingOneInstructionAtATimeMatchesRun(string name, string stdin)
    {
        var (program, info) = FrontEnd(name);
        var (runOut, runExit) = RunVm(program, info, stdin);
        var (stepOut, stepExit) = StepVm(program, info, stdin, budget: 1);

        Assert.Equal(runOut, stepOut);
        Assert.Equal(runExit, stepExit);
    }

    [Theory]
    [MemberData(nameof(CallAndPrintSemantics))]
    public void CallAndPrintSemanticsHoldWhenSteppedOneInstructionAtATime(string source, string stdin, string expected, int expectedExit)
    {
        var (program, info) = FrontEndSource("calls.bas", source);
        Assert.Equal((expected, expectedExit), StepVm(program, info, stdin, budget: 1));
    }

    /// <summary>(source, expected). RND without RANDOMIZE repeats the same sequence
    /// every run (ECMA-116 §5.4.4) — pinned here, so changing the generator is a
    /// deliberate act — and RANDOMIZE n (an extension) repeats per n. Each runs
    /// under both arithmetic modes.</summary>
    public static IEnumerable<object[]> RandomSemantics() =>
    [
        ["PRINT RND; RND; RND\n", " 0.86770936  0.12033515  0.22254771 \n"],
        ["RANDOMIZE 42\nPRINT RND; RND; RND\n", " 0.3664867  0.33911364  0.67551857 \n"],
        // one sequence for the whole program, not one per program unit
        ["PRINT RND\nCALL S\nEND\nSUB S\nPRINT RND\nEND SUB\n", " 0.86770936 \n 0.12033515 \n"],
        // the seed expression is evaluated (the VM used to skip RANDOMIZE entirely)
        ["RANDOMIZE F(42)\nPRINT RND\nEND\nFUNCTION F(X)\nPRINT \"seed\";\nLET F = X\nEND FUNCTION\n", "seed 0.3664867 \n"],
    ];

    [Theory]
    [MemberData(nameof(RandomSemantics))]
    public void RandomSequencesRepeat(string source, string expected)
    {
        foreach (var src in new[] { source, source + "OPTION ARITHMETIC NATIVE\n" })
        {
            var (program, info) = FrontEndSource("rnd.bas", src);
            Assert.Equal((expected, 0), RunVm(program, info, ""));
        }
    }

    [Fact]
    public void RandomizeIsUnpredictableUnlessTheHostSuppliesASeed()
    {
        var (program, info) = FrontEndSource("randomize.bas", "RANDOMIZE\nPRINT RND; RND; RND\n");

        // From the clock: a new sequence every run (and never the default one).
        var runs = new[] { RunVm(program, info, ""), RunVm(program, info, ""), RunVm(program, info, ""), RunVm(program, info, "") }
            .Select(r => r.Output).ToList();
        Assert.Equal(runs.Count, runs.Distinct().Count());
        Assert.DoesNotContain(" 0.86770936  0.12033515  0.22254771 \n", runs);

        // From a host seed: the same run every time; another seed, another run.
        var seeded = RunVm(program, info, "", randomizeSeed: 7);
        Assert.Equal(seeded, RunVm(program, info, "", randomizeSeed: 7));
        Assert.NotEqual(seeded, RunVm(program, info, "", randomizeSeed: 8));

        // A host seed only stands in for the clock: without RANDOMIZE nothing changes.
        var (plain, plainInfo) = FrontEndSource("plain.bas", "PRINT RND\n");
        Assert.Equal(" 0.86770936 \n", RunVm(plain, plainInfo, "", randomizeSeed: 7).Output);
    }

    /// <summary>The real-time games, which loop on INKEY$ and SLEEP.</summary>
    public static IEnumerable<object[]> RealTimeGames() =>
    [
        ["invaders.bas"],
        ["snake.bas"],
        ["breakout.bas"],
        ["tetris.bas"],
    ];

    /// <summary>With no keyboard each game plays itself to the end, and with a
    /// randomize seed its RND repeats — so stepping one instruction at a time must
    /// print and draw exactly what large steps do, frame for frame. Each run starts
    /// without the game's saved high score, which it would otherwise show as HI.</summary>
    [Theory]
    [MemberData(nameof(RealTimeGames))]
    public void RealTimeGamesPlayTheSameWhenSteppedOneInstructionAtATime(string name)
    {
        var (program, info) = FrontEnd(name);
        var largeGfx = new ArcadeBasic.Runtime.RecordingGraphicsDevice();
        var smallGfx = new ArcadeBasic.Runtime.RecordingGraphicsDevice();

        (string Output, int Exit) large, small;
        try
        {
            ScoreFiles.Delete(name);
            large = StepVm(program, info, "", budget: 100_000, randomizeSeed: 1, graphics: largeGfx);
            ScoreFiles.Delete(name);
            small = StepVm(program, info, "", budget: 1, randomizeSeed: 1, graphics: smallGfx);
        }
        finally
        {
            ScoreFiles.Delete(name);
        }

        Assert.Equal(large, small);
        Assert.Contains("FLUSH", largeGfx.Transcript);          // it really drew frames
        Assert.True(largeGfx.Transcript == smallGfx.Transcript, $"{name}: the drawn frames differ");
    }

    /// <summary>The games' sound effects are background music (<c>PLAY "MB…"</c>): a game
    /// playing itself to the end makes sound, finishing with its game-over tune, and never
    /// waits for any of it (a foreground statement would pause the game).</summary>
    [Theory]
    [InlineData("invaders.bas", "130.8128")]     // ... G E C: the last note, O3 C
    [InlineData("snake.bas", "97.9989")]         // ... C O2 G
    [InlineData("breakout.bas", "130.8128")]
    [InlineData("tetris.bas", "130.8128")]
    public void RealTimeGamesMakeSoundWithoutWaitingForIt(string name, string lastNote)
    {
        var (program, info) = FrontEnd(name);
        var audio = new ArcadeBasic.Runtime.RecordingAudioDevice();
        var paused = 0;
        try
        {
            ScoreFiles.Delete(name);
            StepVm(program, info, "", budget: 100_000, randomizeSeed: 1, audio: audio,
                onStep: r => { if (r.Status == StepStatus.AwaitingAudio) paused++; });
        }
        finally
        {
            ScoreFiles.Delete(name);
        }

        var tones = audio.Transcript.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(tones.Length > 3, $"{name}: {tones.Length} tones");
        Assert.StartsWith($"TONE {lastNote} ", tones[^1]);
        Assert.Equal(0, paused);
    }

    [Theory]
    [MemberData(nameof(AllExamples))]
    public void EveryExampleCompiles(string name)
    {
        // The gap we guard against is the compiler rejecting a feature sema
        // accepts (e.g. a statement it hasn't learned to lower yet).
        var (program, info) = FrontEnd(name);
        var ex = Record.Exception(() => BasicCompiler.Compile(program, info));
        Assert.Null(ex);
    }

    [Fact]
    public void TerminalLineInputReadsWholeLines()
    {
        // Regression: terminal LINE INPUT (no file channel) was once unimplemented
        // in the tree-walker. It reads the whole line, commas included, with the
        // prompt "? " bare and a single space for the ';' form.
        const string source =
            "LINE INPUT A$\n" +
            "LINE INPUT \"\"; B$\n" +
            "PRINT \"[\" & A$ & \"][\" & B$ & \"]\"\n";
        const string stdin = "hello, world\nsecond, line\n";

        var (program, info) = FrontEndSource("lineinput.bas", source);
        Assert.Equal(("?  [hello, world][second, line]\n", 0), RunVm(program, info, stdin));   // whole lines, commas kept
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
    public void LoopAndSelectSemanticsHold(string source, string expected)
    {
        var (program, info) = FrontEndSource("loops.bas", source);
        Assert.Equal((expected, 0), RunVm(program, info, ""));
    }

    /// <summary>(source, stdin, expected output, expected exit code). Procedure calls
    /// and PRINT state: the tree-walker used to drop a callee's STOP and unhandled
    /// exceptions (the caller ran on; a FUNCTION returned 0), both engines restarted
    /// the PRINT column inside a SUB, and the tree-walker restarted it at every PRINT
    /// and buffered the line, so a FUNCTION printing mid-PRINT came out first.</summary>
    public static IEnumerable<object[]> CallAndPrintSemantics() =>
    [
        // STOP inside a SUB ends the program (ECMA-116 §4.2.4, §9.1.4)
        ["CALL S\nPRINT \"after\"\nEND\nSUB S\nPRINT \"in\"\nSTOP\nEND SUB\n", "", "in\n", 0],
        // … and inside a FUNCTION, mid-expression
        ["PRINT \"a\"\nLET Y = G(1)\nPRINT \"after\"\nEND\nFUNCTION G(X)\nSTOP\nLET G = X\nEND FUNCTION\n", "", "a\n", 0],
        // … two calls deep, and a WHEN around the call doesn't intercept it
        ["WHEN EXCEPTION IN\nCALL OUTER\nUSE\nPRINT \"handler\"\nEND WHEN\nPRINT \"after\"\nEND\n" +
         "SUB OUTER\nCALL INNER\nPRINT \"outer\"\nEND SUB\nSUB INNER\nSTOP\nEND SUB\n", "", "", 0],
        // an exception a SUB doesn't handle reaches the WHEN around the CALL
        ["WHEN EXCEPTION IN\nCALL S\nPRINT \"after call\"\nUSE\nPRINT \"caught\"\nEND WHEN\nEND\n" +
         "SUB S\nCAUSE EXCEPTION 42\nPRINT \"rest\"\nEND SUB\n", "", "caught\n", 0],
        // … and CONTINUE resumes after the CALL
        ["WHEN EXCEPTION IN\nCALL S\nPRINT \"next\"\nUSE\nCONTINUE\nEND WHEN\nEND\nSUB S\nCAUSE EXCEPTION 7\nEND SUB\n",
         "", "next\n", 0],
        // unhandled in a FUNCTION, it stops the program instead of returning 0
        ["PRINT G(0)\nPRINT \"after\"\nEND\nFUNCTION G(X)\nLET G = 1 / X\nEND FUNCTION\n", "", "", 1],
        // raised two calls deep, mid-expression in every frame, and caught in the middle one
        ["PRINT 100 + OUTERF(0); 100 + OUTERF(2)\nEND\n" +
         "FUNCTION OUTERF(X)\nWHEN EXCEPTION IN\nLET OUTERF = 10 + INNERF(X)\nUSE\nLET OUTERF = -1\nEND WHEN\nEND FUNCTION\n" +
         "FUNCTION INNERF(X)\nLET INNERF = 1 / X\nEND FUNCTION\n", "", " 99  110.5 \n", 0],
        // RETRY in a SUB's handler re-runs a CALL that raised
        ["CALL S\nEND\nSUB S\nLET N = 0\nWHEN EXCEPTION IN\nLET N = N + 1\nCALL FAILS(N)\nPRINT \"ok after\"; N\n" +
         "USE\nRETRY\nEND WHEN\nEND SUB\nSUB FAILS(K)\nIF K < 3 THEN CAUSE EXCEPTION 1\nEND SUB\n", "", "ok after 3 \n", 0],
        // EXIT SUB from inside a WHEN closes that handler: a later exception in the caller is unhandled
        ["CALL S\nCAUSE EXCEPTION 9\nEND\nSUB S\nWHEN EXCEPTION IN\nEXIT SUB\nUSE\nPRINT \"stale handler\"\nEND WHEN\nEND SUB\n",
         "", "", 1],
        // the PRINT column carries across statements: comma zones …
        ["PRINT \"AAAAAAAAAAAAAAAAAAAA\";\nPRINT \"B\",\nPRINT \"C\"\n", "", "AAAAAAAAAAAAAAAAAAAAB           C\n", 0],
        // … and TAB
        ["PRINT \"XYZ\";\nPRINT TAB(10); \"T\"\n", "", "XYZ      T\n", 0],
        // … into a SUB
        ["PRINT \"AAAAAAAAAAAAAAAAAAAA\";\nCALL T\nPRINT \"C\"\nEND\nSUB T\nPRINT \"B\",\nEND SUB\n", "",
         "AAAAAAAAAAAAAAAAAAAAB           C\n", 0],
        // … and back out of one
        ["CALL T\nPRINT TAB(10); \"T\"\nEND\nSUB T\nPRINT \"XYZ\";\nEND SUB\n", "", "XYZ      T\n", 0],
        // a FUNCTION's own PRINT lands between the items around its call
        ["PRINT \"a\"; G(1)\nEND\nFUNCTION G(X)\nPRINT \"in\";\nLET G = X\nEND FUNCTION\n", "", "ain 1 \n", 0],
        // the INPUT reply ends the line, so the next PRINT starts at column 0
        ["PRINT \"ABC\";\nINPUT X\nPRINT \"D\", \"E\"\n", "5\n", "ABC? D               E\n", 0],
    ];

    [Theory]
    [MemberData(nameof(CallAndPrintSemantics))]
    public void CallAndPrintSemanticsHold(string source, string stdin, string expected, int expectedExit)
    {
        var (program, info) = FrontEndSource("calls.bas", source);
        Assert.Equal((expected, expectedExit), RunVm(program, info, stdin));
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

        Assert.Equal(RunVm(decProgram, decInfo, stdin), RunVm(program, info, stdin));
    }

    /// <summary>(source, expected under DECIMAL, expected under NATIVE). Each snippet
    /// runs under both arithmetic modes.</summary>
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
        // the VM's unboxed native paths keep the same semantics: + overflows, / by zero ...
        ["LET X = 1E308\nWHEN EXCEPTION IN\nPRINT X + X > 0\nUSE\nPRINT \"overflow\"; EXTYPE\nEND WHEN\n",
         "-1 \n", "overflow 1002 \n"],
        ["LET Z = 0\nWHEN EXCEPTION IN\nPRINT 1 / Z\nUSE\nPRINT EXTYPE\nEND WHEN\n", " 1001 \n", " 1001 \n"],
        // ... the numeric builtins ...
        ["PRINT INT(-2.5); ABS(-3); SGN(-0.5); SGN(0); SQR(16); INT(7); ATN(0); COS(0)\n",
         "-3  3 -1  0  4  7  0  1 \n", "-3  3 -1  0  4  7  0  1 \n"],
        // ... a number through a FUNCTION's return slot, SELECT CASE and an array ...
        ["FUNCTION F(X)\nLET F = X * 2\nEND FUNCTION\nLET Y = 3 + 4\nPRINT F(Y); Y\nSELECT CASE Y - 4\nCASE 3\nPRINT \"three\"\nEND SELECT\n" +
         "DIM A(2)\nLET A(1) = Y / 2\nLET A(2) = A(1) + A(1)\nPRINT A(2) = Y\n",
         " 14  7 \nthree\n-1 \n", " 14  7 \nthree\n-1 \n"],
        // ... and a condition mixing a string comparison with numeric ones
        ["LET A$ = \"b\"\nLET N = 2\nIF A$ > \"a\" AND N > 1 AND NOT N = 3 THEN PRINT \"yes\"\n", "yes\n", "yes\n"],
    ];

    [Theory]
    [MemberData(nameof(ArithmeticSemantics))]
    public void ArithmeticModesGiveTheirOwnResults(string source, string expectedDecimal, string expectedNative)
    {
        foreach (var (src, expected) in new[] { (source, expectedDecimal), (source + "OPTION ARITHMETIC NATIVE\n", expectedNative) })
        {
            var (program, info) = FrontEndSource("arith.bas", src);
            Assert.Equal(expected, RunVm(program, info, "").Output);
        }
    }

    [Fact]
    public void InternalFileRoundTripsExactly()
    {
        // INTERNAL records (WRITE #/READ #) preserve the exact value — including
        // precision well beyond the 9-digit display rounding.
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

            var (output, exit) = RunVm(program, info, "");

            Assert.Equal(0, exit);
            var lines = output.Replace("\r", "").Split('\n');
            Assert.Equal("0", lines[0].Trim());       // exact round-trip past display precision
            Assert.Equal("hi, there", lines[1]);       // string verbatim (comma preserved)
        }
        finally
        {
            File.Delete(path);
        }
    }

    // -- helpers ---------------------------------------------------------

    /// <summary>With no OPTION BASE, arrays start at subscript 1 (ANSI X3.113 §7.1.1) —
    /// the tree-walker used to start them at 0.</summary>
    [Theory]
    [InlineData("DIM A(3)\nMAT PRINT A\nMAT REDIM A(2, 2)\nMAT PRINT A\n", " 0  0  0 \n 0  0 \n 0  0 \n\n")]
    [InlineData("OPTION BASE 0\nDIM A(3)\nMAT PRINT A\n", " 0  0  0  0 \n")]
    public void ArraysStartAtOneUnlessOptionBaseSaysZero(string source, string expected)
    {
        var (program, info) = FrontEndSource("base.bas", source);
        Assert.Equal((expected, 0), RunVm(program, info, ""));
    }

    /// <summary>An example, with the libraries it names (LIBRARY) from the examples folder.</summary>
    private static (AstProgram Program, SemanticInfo Info) FrontEnd(string name)
    {
        var diags = new DiagnosticBag();
        var program = ProgramLoader.Load(new SourceFile(name, File.ReadAllText(Path.Combine(ExamplesDir(), name))), (library, _) =>
        {
            var path = Path.Combine(ExamplesDir(), library);
            return File.Exists(path) ? new SourceFile(library, File.ReadAllText(path)) : null;
        }, diags);
        var info = Analyzer.Analyze(program, diags);
        Assert.False(diags.HasErrors,
            $"{name} produced analysis diagnostics:\n{string.Join("\n", diags.All.Select(d => d.Render(false)))}");
        return (program, info);
    }

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

    private static (string Output, int Exit) RunVm(AstProgram program, SemanticInfo info, string stdin, int? randomizeSeed = null)
    {
        var compiled = BasicCompiler.Compile(program, info);
        var sw = new StringWriter { NewLine = "\n" };
        var exit = new BasicVm(compiled, sw, new StringReader(stdin), randomizeSeed: randomizeSeed).Run();
        return (sw.ToString(), exit);
    }

    /// <summary>Drive the VM the way a browser or game host would: Step until it
    /// halts, skipping the waits, and queueing a line of <paramref name="stdin"/> only
    /// when an INPUT asks for one (end of input once it runs out).</summary>
    private static (string Output, int Exit) StepVm(AstProgram program, SemanticInfo info, string stdin, int budget,
        int? randomizeSeed = null, ArcadeBasic.Runtime.IGraphicsDevice? graphics = null,
        ArcadeBasic.Runtime.IAudioDevice? audio = null, Action<StepResult>? onStep = null)
    {
        var compiled = BasicCompiler.Compile(program, info);
        var sw = new StringWriter { NewLine = "\n" };
        var pending = new StringReader(stdin);
        var input = new ArcadeBasic.Runtime.LineInputQueue();
        var vm = new BasicVm(compiled, sw, input, graphics, audio: audio, randomizeSeed: randomizeSeed);
        for (long instructions = 0; ; instructions += budget)
        {
            Assert.True(instructions < MaxSteppedInstructions, $"still running after {MaxSteppedInstructions:N0} instructions");
            var r = vm.Step(budget);
            onStep?.Invoke(r);
            if (r.Status == StepStatus.Halted) return (sw.ToString(), r.ExitCode);
            if (r.Status == StepStatus.NeedsInput)
            {
                if (pending.ReadLine() is { } line) input.Provide(line);
                else input.Complete();
            }
        }
    }

    private const long MaxSteppedInstructions = 200_000_000;

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

/// <summary>The games save their high score to <c>&lt;game&gt;.score</c> in the current
/// directory and show it on screen, so tests that run them share that file: they sit
/// in one collection (never run concurrently) and delete it around each run.</summary>
internal static class ScoreFiles
{
    public const string Collection = "example score files";

    public static void Delete(string game)
    {
        try { File.Delete(Path.ChangeExtension(game, ".score")); } catch (IOException) { /* best effort */ }
    }
}
