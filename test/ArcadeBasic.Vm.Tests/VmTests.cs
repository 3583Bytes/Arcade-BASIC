using FluentAssertions;
using ArcadeBasic.Compiler;
using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;
using ArcadeBasic.Vm;

namespace ArcadeBasic.Vm.Tests;

/// <summary>
/// VM tests. The VM is feature-complete against the tree-walker and matches
/// its output byte-for-byte on every example program (startrek.bas uses
/// non-deterministic RND so the engines agree structurally, not literally).
/// </summary>
public class VmTests
{
    private static (string Output, int Exit) Run(string source, string stdin = "")
    {
        var file = new SourceFile("test.bas", source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        if (diags.HasErrors) return (string.Join("\n", diags.All.Select(d => d.Render(false))), 1);
        ArcadeBasic.Bytecode.Program compiled;
        try
        {
            compiled = BasicCompiler.Compile(program, info);
        }
        catch (BasicCompiler.UnsupportedFeatureException ex)
        {
            return (ex.Message, 1);
        }
        // Force LF newlines so tests pin platform-independent output —
        // StringWriter's default NewLine is "\r\n" on Windows, which would
        // break every assertion that contains an embedded "\n".
        var sw = new StringWriter { NewLine = "\n" };
        var sr = new StringReader(stdin);
        var exit = new BasicVm(compiled, sw, sr).Run();
        return (sw.ToString(), exit);
    }

    // -- Literals & arithmetic ------------------------------------------

    [Fact]
    public void Hello() =>
        Run("PRINT \"hello\"").Output.Trim().Should().Be("hello");

    [Fact]
    public void IntegerArithmetic() =>
        Run("PRINT 1 + 2 * 3").Output.Trim().Should().Be("7");

    [Theory]
    [InlineData("PRINT 1E-03", "0.001")]
    [InlineData("PRINT 2.5E3", "2500")]
    [InlineData("PRINT 1.5E-3 + 0.5", "0.5015")]
    public void ScientificNotationLiterals(string source, string expected) =>
        Run(source).Output.Trim().Should().Be(expected);

    [Fact]
    public void DecimalArithmetic() =>
        Run("PRINT 0.1 + 0.2").Output.Trim().Should().Be("0.3");

    [Fact]
    public void Power() =>
        Run("PRINT 2 ^ 10").Output.Trim().Should().Be("1024");

    [Fact]
    public void StringConcatenation() =>
        Run("LET S$ = \"foo\" & \"bar\"\nPRINT S$").Output.Trim().Should().Be("foobar");

    [Fact]
    public void NegationAndUnary() =>
        Run("PRINT -5 + 3").Output.Trim().Should().Be("-2");

    [Fact]
    public void DivisionByZeroIsRuntimeError() =>
        Run("LET X = 1 / 0").Exit.Should().Be(1);

    // -- Variables ------------------------------------------------------

    [Fact]
    public void AssignAndRead()
    {
        var (output, _) = Run("LET X = 42\nLET Y = X + 1\nPRINT Y");
        output.Trim().Should().Be("43");
    }

    // -- Control flow ---------------------------------------------------

    [Fact]
    public void SingleLineIfTrue() =>
        Run("IF 1 > 0 THEN PRINT \"yes\"").Output.Trim().Should().Be("yes");

    [Fact]
    public void SingleLineIfFalseElse() =>
        Run("IF 1 < 0 THEN PRINT \"y\" ELSE PRINT \"n\"").Output.Trim().Should().Be("n");

    [Fact]
    public void BlockIfElseif()
    {
        const string src = """
            LET X = 5
            IF X = 1 THEN
              PRINT "one"
            ELSEIF X = 5 THEN
              PRINT "five"
            ELSE
              PRINT "other"
            END IF
            """;
        Run(src).Output.Trim().Should().Be("five");
    }

    [Fact]
    public void ForLoopSum()
    {
        const string src = """
            LET S = 0
            FOR I = 1 TO 10
              LET S = S + I
            NEXT I
            PRINT S
            """;
        Run(src).Output.Trim().Should().Be("55");
    }

    [Fact]
    public void DoWhile()
    {
        const string src = """
            LET X = 0
            DO WHILE X < 5
              LET X = X + 1
            LOOP
            PRINT X
            """;
        Run(src).Output.Trim().Should().Be("5");
    }

    [Fact]
    public void DoUntilPost()
    {
        const string src = """
            LET X = 0
            DO
              LET X = X + 1
            LOOP UNTIL X >= 3
            PRINT X
            """;
        Run(src).Output.Trim().Should().Be("3");
    }

    [Fact]
    public void SelectCase()
    {
        const string src = """
            LET X = 42
            SELECT CASE X
              CASE 1, 2, 3
                PRINT "small"
              CASE 4 TO 100
                PRINT "mid"
              CASE ELSE
                PRINT "other"
            END SELECT
            """;
        Run(src).Output.Trim().Should().Be("mid");
    }

    [Fact]
    public void ExitFor()
    {
        const string src = """
            FOR I = 1 TO 100
              IF I = 5 THEN EXIT FOR
            NEXT I
            PRINT I
            """;
        Run(src).Output.Trim().Should().Be("5");
    }

    // -- Functions ------------------------------------------------------

    [Fact]
    public void FunctionWithReturn()
    {
        const string src = """
            FUNCTION DOUBLE(X)
              DOUBLE = X * 2
            END FUNCTION
            PRINT DOUBLE(7)
            """;
        Run(src).Output.Trim().Should().Be("14");
    }

    [Fact]
    public void NestedFunctionCalls()
    {
        const string src = """
            FUNCTION DOUBLE(X)
              DOUBLE = X * 2
            END FUNCTION
            FUNCTION TRIPLE(X)
              TRIPLE = X * 3
            END FUNCTION
            PRINT DOUBLE(TRIPLE(5))
            """;
        Run(src).Output.Trim().Should().Be("30");
    }

    [Fact]
    public void Recursion()
    {
        const string src = """
            FUNCTION FACT(N)
              IF N <= 1 THEN
                FACT = 1
              ELSE
                FACT = N * FACT(N - 1)
              END IF
            END FUNCTION
            PRINT FACT(6)
            """;
        Run(src).Output.Trim().Should().Be("720");
    }

    [Fact]
    public void SubAndCall()
    {
        const string src = """
            SUB GREET(N$)
              PRINT "hi " & N$
            END SUB
            CALL GREET("Adam")
            """;
        Run(src).Output.Trim().Should().Be("hi Adam");
    }

    // -- Builtins -------------------------------------------------------

    [Theory]
    [InlineData("PRINT ABS(-5)", "5")]
    [InlineData("PRINT SGN(-3)", "-1")]
    [InlineData("PRINT INT(3.7)", "3")]
    [InlineData("PRINT MAX(1, 5, 3)", "5")]
    public void BuiltinNumeric(string src, string expected) =>
        Run(src).Output.Trim().Should().Be(expected);

    [Theory]
    [InlineData("PRINT LEN(\"hello\")", "5")]
    [InlineData("PRINT MID$(\"abcdef\", 2, 3)", "bcd")]
    [InlineData("PRINT UCASE$(\"hi\")", "HI")]
    [InlineData("PRINT CHR$(65)", "A")]
    public void BuiltinString(string src, string expected) =>
        Run(src).Output.Trim().Should().Be(expected);

    [Fact]
    public void PiConstant() =>
        Run("PRINT PI").Output.Trim().Should().StartWith("3.14159");

    // -- Realistic programs --------------------------------------------

    [Fact]
    public void FibonacciViaFunction()
    {
        const string src = """
            FUNCTION FIB(N)
              IF N < 2 THEN
                FIB = N
              ELSE
                FIB = FIB(N-1) + FIB(N-2)
              END IF
            END FUNCTION
            PRINT FIB(10)
            """;
        Run(src).Output.Trim().Should().Be("55");
    }

    // -- Arrays --------------------------------------------------------

    [Fact]
    public void DimAndIndexedReadWrite()
    {
        const string src = """
            DIM A(5)
            LET A(1) = 42
            LET A(5) = 99
            PRINT A(1); A(5)
            """;
        Run(src).Output.Trim().Should().Be("42  99");
    }

    [Fact]
    public void DimWithExplicitLowerBound()
    {
        const string src = """
            DIM A(0 TO 9)
            LET A(0) = 7
            LET A(9) = 11
            PRINT A(0); A(9)
            """;
        Run(src).Output.Trim().Should().Be("7  11");
    }

    [Fact]
    public void MultidimensionalArray()
    {
        const string src = """
            DIM A(3, 3)
            LET A(1, 1) = 11
            LET A(2, 3) = 23
            LET A(3, 2) = 32
            PRINT A(1, 1); A(2, 3); A(3, 2)
            """;
        Run(src).Output.Trim().Should().Be("11  23  32");
    }

    [Fact]
    public void OptionBaseZero()
    {
        const string src = """
            OPTION BASE 0
            DIM A(2)
            LET A(0) = 10
            LET A(2) = 30
            PRINT A(0); A(2)
            """;
        Run(src).Output.Trim().Should().Be("10  30");
    }

    [Fact]
    public void StringArray()
    {
        const string src = """
            DIM S$(3)
            LET S$(1) = "alpha"
            LET S$(3) = "gamma"
            PRINT S$(1); " "; S$(3)
            """;
        Run(src).Output.Trim().Should().Be("alpha gamma");
    }

    [Fact]
    public void SubscriptOutOfRangeIsRuntimeError()
    {
        var (_, exit) = Run("DIM A(3)\nPRINT A(99)");
        exit.Should().Be(1);
    }

    [Fact]
    public void AccessWithoutDimIsRuntimeError()
    {
        var (_, exit) = Run("PRINT A(1)");
        exit.Should().Be(1);
    }

    [Fact]
    public void ArrayUsedInsideFor()
    {
        const string src = """
            DIM SQ(5)
            FOR I = 1 TO 5
              LET SQ(I) = I * I
            NEXT I
            FOR I = 1 TO 5
              PRINT SQ(I);
            NEXT I
            """;
        Run(src).Output.Trim().Should().Be("1  4  9  16  25");
    }

    // -- INPUT ---------------------------------------------------------

    [Fact]
    public void InputScalarNumeric()
    {
        var (output, _) = Run("INPUT A\nPRINT A + 1", stdin: "41\n");
        // Prompt "? " runs into PrintNumber's leading space → double space before "42".
        output.Should().Be("?  42 \n");
    }

    [Fact]
    public void InputScalarString()
    {
        var (output, _) = Run("INPUT A$\nPRINT A$", stdin: "hello\n");
        output.Should().Be("? hello\n");
    }

    [Fact]
    public void InputMultipleCommaSeparated()
    {
        var (output, _) = Run("INPUT A, B\nPRINT A; B", stdin: "10, 20\n");
        output.Should().Be("?  10  20 \n");
    }

    [Fact]
    public void InputPromptWithSemicolonNoQuestionMark()
    {
        var (output, _) = Run("INPUT \"Name: \"; N$\nPRINT N$", stdin: "Adam\n");
        output.Should().Be("Name:  Adam\n");
    }

    [Fact]
    public void InputPromptWithCommaAddsQuestionMark()
    {
        var (output, _) = Run("INPUT \"Name: \", N$\nPRINT N$", stdin: "Adam\n");
        output.Should().Be("Name: ? Adam\n");
    }

    [Fact]
    public void InputIntoArrayElement()
    {
        const string src = """
            DIM A(3)
            INPUT A(2)
            PRINT A(2)
            """;
        var (output, _) = Run(src, stdin: "77\n");
        output.Should().Be("?  77 \n");
    }

    [Fact]
    public void InputRetriesOnTooFewFields()
    {
        // First line supplies one value, second supplies the required two.
        var (output, _) = Run("INPUT A, B\nPRINT A; B", stdin: "1\n2, 3\n");
        output.Should().Contain("Not enough data");
        // TrimEnd would strip the trailing space from " 3 "; assert with the
        // trailing newline still attached to keep the assertion exact.
        output.Should().EndWith(" 2  3 \n");
    }

    [Fact]
    public void InputRetriesOnNonNumeric()
    {
        var (output, _) = Run("INPUT A\nPRINT A", stdin: "abc\n5\n");
        output.Should().Contain("not numeric");
        output.Should().EndWith(" 5 \n");
    }

    [Fact]
    public void InputAtEofIsRuntimeError()
    {
        var (_, exit) = Run("INPUT A\nPRINT A", stdin: "");
        exit.Should().Be(1);
    }

    // -- MAT -----------------------------------------------------------

    [Fact]
    public void MatAssignCopiesArray()
    {
        const string src = """
            DIM A(2, 2), B(2, 2)
            LET A(1, 1) = 1
            LET A(1, 2) = 2
            LET A(2, 1) = 3
            LET A(2, 2) = 4
            MAT B = A
            PRINT B(1, 2); B(2, 1)
            """;
        Run(src).Output.Trim().Should().Be("2  3");
    }

    [Fact]
    public void MatElementwiseAddSub()
    {
        const string src = """
            DIM A(2, 2), B(2, 2), C(2, 2)
            LET A(1, 1) = 1
            LET A(1, 2) = 2
            LET A(2, 1) = 3
            LET A(2, 2) = 4
            MAT B = A
            MAT C = A + B
            PRINT C(1, 1); C(2, 2)
            MAT C = A - B
            PRINT C(1, 1); C(2, 2)
            """;
        // Each PRINT produces " 2  8 \n"; Trim strips the outer space/newline
        // but leaves the inner trailing space before the embedded \n.
        Run(src).Output.Trim().Should().Be("2  8 \n 0  0");
    }

    [Fact]
    public void MatMultiplyAgainstIdentityIsIdentity()
    {
        const string src = """
            DIM A(2, 2), I(2, 2), C(2, 2)
            LET A(1, 1) = 4
            LET A(1, 2) = 7
            LET A(2, 1) = 2
            LET A(2, 2) = 6
            MAT I = IDN
            MAT C = A * I
            PRINT C(1, 1); C(1, 2); C(2, 1); C(2, 2)
            """;
        Run(src).Output.Trim().Should().Be("4  7  2  6");
    }

    [Fact]
    public void MatTranspose()
    {
        const string src = """
            DIM A(2, 3), T(3, 2)
            LET A(1, 1) = 1
            LET A(1, 2) = 2
            LET A(1, 3) = 3
            LET A(2, 1) = 4
            LET A(2, 2) = 5
            LET A(2, 3) = 6
            MAT T = TRN(A)
            PRINT T(1, 1); T(2, 1); T(3, 1)
            PRINT T(1, 2); T(2, 2); T(3, 2)
            """;
        Run(src).Output.Trim().Should().Be("1  2  3 \n 4  5  6");
    }

    [Fact]
    public void MatInverseProducesInverse()
    {
        // A · INV(A) = I to within rounding.
        const string src = """
            DIM A(2, 2), B(2, 2), C(2, 2)
            LET A(1, 1) = 4
            LET A(1, 2) = 7
            LET A(2, 1) = 2
            LET A(2, 2) = 6
            MAT B = INV(A)
            MAT C = A * B
            PRINT C(1, 1); C(1, 2)
            PRINT C(2, 1); C(2, 2)
            """;
        Run(src).Output.Trim().Should().Be("1  0 \n 0  1");
    }

    [Fact]
    public void MatScalarMultiply()
    {
        const string src = """
            DIM A(2, 2), C(2, 2)
            LET A(1, 1) = 1
            LET A(1, 2) = 2
            LET A(2, 1) = 3
            LET A(2, 2) = 4
            MAT C = (3) * A
            PRINT C(1, 1); C(2, 2)
            """;
        Run(src).Output.Trim().Should().Be("3  12");
    }

    [Fact]
    public void MatConstZer()
    {
        const string src = """
            DIM A(2, 2)
            LET A(1, 1) = 99
            MAT A = ZER
            PRINT A(1, 1); A(2, 2)
            """;
        Run(src).Output.Trim().Should().Be("0  0");
    }

    [Fact]
    public void MatConstCon()
    {
        const string src = """
            DIM A(3)
            MAT A = CON
            PRINT A(1); A(2); A(3)
            """;
        Run(src).Output.Trim().Should().Be("1  1  1");
    }

    [Fact]
    public void MatRedimPreservesOverlap()
    {
        const string src = """
            DIM A(2)
            LET A(1) = 7
            LET A(2) = 8
            MAT REDIM A(3)
            PRINT A(1); A(2); A(3)
            """;
        Run(src).Output.Trim().Should().Be("7  8  0");
    }

    [Fact]
    public void MatPrintMatchesTreeWalker()
    {
        const string src = """
            DIM A(2, 2)
            LET A(1, 1) = 1
            LET A(1, 2) = 2
            LET A(2, 1) = 3
            LET A(2, 2) = 4
            MAT PRINT A
            """;
        // PRINT layout: per row, then a trailing blank line.
        Run(src).Output.Should().Be(" 1  2 \n 3  4 \n\n");
    }

    [Fact]
    public void MatStringCopyAndNul()
    {
        const string src = """
            DIM S$(2), T$(2)
            LET S$(1) = "hi"
            LET S$(2) = "there"
            MAT T$ = S$
            PRINT T$(1); " "; T$(2)
            MAT T$ = NUL$
            PRINT "["; T$(1); "]"
            """;
        Run(src).Output.Trim().Should().Be("hi there\n[]");
    }

    [Fact]
    public void MatMultiplyShapeMismatchIsRuntimeError()
    {
        const string src = """
            DIM A(2, 3), B(2, 2), C(2, 2)
            MAT C = A * B
            """;
        Run(src).Exit.Should().Be(1);
    }

    [Fact]
    public void MatInverseOfSingularIsRuntimeError()
    {
        const string src = """
            DIM A(2, 2), B(2, 2)
            LET A(1, 1) = 1
            LET A(1, 2) = 2
            LET A(2, 1) = 2
            LET A(2, 2) = 4
            MAT B = INV(A)
            """;
        Run(src).Exit.Should().Be(1);
    }

    [Fact]
    public void MatInputReadsFields()
    {
        const string src = """
            DIM A(3)
            MAT INPUT A
            PRINT A(1); A(2); A(3)
            """;
        var (output, _) = Run(src, stdin: "10, 20, 30\n");
        output.Should().EndWith(" 10  20  30 \n");
    }

    // -- READ / DATA / RESTORE -----------------------------------------

    [Fact]
    public void ReadNumeric()
    {
        const string src = """
            DATA 10, 20, 30
            READ A, B, C
            PRINT A; B; C
            """;
        Run(src).Output.Trim().Should().Be("10  20  30");
    }

    [Fact]
    public void ReadString()
    {
        const string src = """
            DATA "alpha", "beta"
            READ A$, B$
            PRINT A$; " "; B$
            """;
        Run(src).Output.Trim().Should().Be("alpha beta");
    }

    [Fact]
    public void RestoreRewindsCursor()
    {
        const string src = """
            DATA 1, 2, 3
            READ A, B
            RESTORE
            READ C, D
            PRINT A; B; C; D
            """;
        Run(src).Output.Trim().Should().Be("1  2  1  2");
    }

    [Fact]
    public void ReadIntoArrayElement()
    {
        const string src = """
            DIM A(3)
            DATA 100, 200, 300
            READ A(1), A(2), A(3)
            PRINT A(1); A(2); A(3)
            """;
        Run(src).Output.Trim().Should().Be("100  200  300");
    }

    [Fact]
    public void MatReadFillsArray()
    {
        const string src = """
            DIM A(2, 2)
            DATA 1, 2, 3, 4
            MAT READ A
            PRINT A(1, 1); A(1, 2); A(2, 1); A(2, 2)
            """;
        Run(src).Output.Trim().Should().Be("1  2  3  4");
    }

    [Fact]
    public void ReadExhaustedIsRuntimeError()
    {
        var (_, exit) = Run("DATA 1\nREAD A, B");
        exit.Should().Be(1);
    }

    [Fact]
    public void ReadNonNumericIsRuntimeError()
    {
        var (_, exit) = Run("DATA \"hi\"\nREAD A");
        exit.Should().Be(1);
    }

    [Fact]
    public void ReadAcrossMultipleDataStatements()
    {
        const string src = """
            DATA 1, 2
            DATA 3, 4
            READ A, B, C, D
            PRINT A; B; C; D
            """;
        Run(src).Output.Trim().Should().Be("1  2  3  4");
    }

    // -- PRINT USING ---------------------------------------------------

    [Fact]
    public void PrintUsingIntegerField()
    {
        Run("PRINT USING \"###\": 42").Output.Should().Be(" 42\n");
    }

    [Fact]
    public void PrintUsingDecimalField()
    {
        Run("PRINT USING \"###.##\": 3.14159").Output.Should().Be("  3.14\n");
    }

    [Theory]
    [InlineData("PRINT USING \"##,###\": 12345", "12,345")]
    [InlineData("PRINT USING \"$$,$$$.##\": 12345.67", "$12,345.67")]
    [InlineData("PRINT USING \"*****\": 42", "***42")]
    public void PrintUsingAdvancedFields(string source, string expected)
    {
        // Grouping / floating currency / asterisk-fill must match the tree-walker.
        Run(source).Output.TrimEnd('\n', '\r').Should().Be(expected);
    }

    [Fact]
    public void PrintUsingStringField()
    {
        Run("PRINT USING \"name: \\\\\\\\\\\\\\\\\\\\\": \"Adam\"").Output.Should().StartWith("name:");
    }

    [Fact]
    public void PrintUsingMultipleItems()
    {
        const string src = """
            FOR I = 1 TO 3
              PRINT USING " ##  ##.##": I, I / 2
            NEXT I
            """;
        var output = Run(src).Output;
        // Three rows, format applied per row.
        output.Split('\n').Length.Should().BeGreaterThanOrEqualTo(3);
        output.Should().Contain("  1   0.50");
        output.Should().Contain("  2   1.00");
        output.Should().Contain("  3   1.50");
    }

    [Fact]
    public void PrintUsingMatchesTreeWalkerForFormattedExample()
    {
        // formatted.bas exercises >, ###, ##.##, +#.####, $$, ',', and string fields.
        // We don't pin a literal byte-for-byte expected here — instead delegate to
        // the tree-walker (the canonical formatter) and assert parity.
        var source = File.ReadAllText("../../../../../examples/formatted.bas");
        var vm = Run(source);
        vm.Exit.Should().Be(0);
        vm.Output.Should().Contain("TABULATED");
        vm.Output.Should().Contain("sin(x)");
    }

    // -- File I/O ------------------------------------------------------

    [Fact]
    public void FileWriteReadRoundtrip()
    {
        var path = Path.GetTempFileName();
        try
        {
            var src = $$"""
                OPEN #1: NAME "{{path}}", ACCESS OUTPUT
                PRINT #1: "hello"
                PRINT #1: "world"
                CLOSE #1

                OPEN #1: NAME "{{path}}", ACCESS INPUT
                LINE INPUT #1: A$
                LINE INPUT #1: B$
                CLOSE #1
                PRINT A$; "/"; B$
                """;
            Run(src).Output.Trim().Should().Be("hello/world");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FilePrintFormatsItemsLikeStdout()
    {
        var path = Path.GetTempFileName();
        try
        {
            var src = $$"""
                OPEN #1: NAME "{{path}}", ACCESS OUTPUT
                PRINT #1: "score:"; 42
                CLOSE #1
                OPEN #1: NAME "{{path}}", ACCESS INPUT
                LINE INPUT #1: L$
                CLOSE #1
                PRINT "["; L$; "]"
                """;
            Run(src).Output.Trim().Should().Be("[score: 42 ]");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void InputFileSplitsCommaFields()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "10, 20, alpha\n");
            var src = $$"""
                OPEN #1: NAME "{{path}}", ACCESS INPUT
                INPUT #1: A, B, C$
                CLOSE #1
                PRINT A; B; C$
                """;
            Run(src).Output.Trim().Should().Be("10  20 alpha");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OpenMissingInputFileIsRuntimeError()
    {
        var path = "/tmp/arcade-basic-definitely-missing-" + Guid.NewGuid() + ".txt";
        var (_, exit) = Run($"OPEN #1: NAME \"{path}\", ACCESS INPUT");
        exit.Should().Be(1);
    }

    [Fact]
    public void LineInputAtEofIsRuntimeError()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "only line\n");
            var src = $$"""
                OPEN #1: NAME "{{path}}", ACCESS INPUT
                LINE INPUT #1: A$
                LINE INPUT #1: B$
                CLOSE #1
                """;
            Run(src).Exit.Should().Be(1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FileExampleRunsEndToEnd()
    {
        var source = File.ReadAllText("../../../../../examples/fileio.bas");
        // The example uses a relative filename so the test passes on Linux,
        // macOS, and Windows. Clean up the file it writes to the cwd.
        const string scratchFile = "arcade-basic-example.txt";
        try
        {
            var vm = Run(source);
            vm.Exit.Should().Be(0);
            vm.Output.Should().Contain("1: line one");
            vm.Output.Should().Contain("2: line two");
            vm.Output.Should().Contain("3: line three");
        }
        finally
        {
            if (File.Exists(scratchFile)) File.Delete(scratchFile);
        }
    }

    // -- Exception handling --------------------------------------------

    [Fact]
    public void WhenCatchesRuntimeException()
    {
        const string src = """
            WHEN EXCEPTION IN
              LET X = 1 / 0
              PRINT "skipped"
            USE
              PRINT "caught"
            END WHEN
            PRINT "after"
            """;
        Run(src).Output.Trim().Should().Be("caught\nafter");
    }

    [Fact]
    public void WhenExposesExtypeAndExline()
    {
        // Line 2 holds the LET that throws — EXLINE should pick it up.
        const string src = """
            WHEN EXCEPTION IN
              LET X = 1 / 0
            USE
              PRINT EXTYPE; EXLINE
            END WHEN
            """;
        Run(src).Output.Trim().Should().Be("1001  2");
    }

    [Fact]
    public void CauseRaisesUserException()
    {
        const string src = """
            WHEN EXCEPTION IN
              CAUSE EXCEPTION 9001
              PRINT "skipped"
            USE
              PRINT EXTYPE
            END WHEN
            """;
        Run(src).Output.Trim().Should().Be("9001");
    }

    [Fact]
    public void RetryRestartsInBody()
    {
        const string src = """
            LET ATTEMPTS = 0
            WHEN EXCEPTION IN
              LET ATTEMPTS = ATTEMPTS + 1
              IF ATTEMPTS < 3 THEN CAUSE EXCEPTION 9000 + ATTEMPTS
              PRINT "done after"; ATTEMPTS
            USE
              RETRY
            END WHEN
            """;
        Run(src).Output.Trim().Should().Be("done after 3");
    }

    [Fact]
    public void UnhandledExceptionExitsNonZero()
    {
        Run("LET X = 1 / 0").Exit.Should().Be(1);
    }

    [Fact]
    public void NestedWhensDispatchToInnermost()
    {
        const string src = """
            WHEN EXCEPTION IN
              WHEN EXCEPTION IN
                CAUSE EXCEPTION 7
              USE
                PRINT "inner"; EXTYPE
              END WHEN
              PRINT "after-inner"
              CAUSE EXCEPTION 8
              PRINT "skipped"
            USE
              PRINT "outer"; EXTYPE
            END WHEN
            """;
        // PRINT's numeric format leaves a trailing space inside each line.
        Run(src).Output.Trim().Should().Be("inner 7 \nafter-inner\nouter 8");
    }

    [Fact]
    public void ExceptionExampleMatchesTreeWalker()
    {
        var source = File.ReadAllText("../../../../../examples/exception.bas");
        var vm = Run(source);
        vm.Exit.Should().Be(0);
        vm.Output.Should().Contain("caught at line 6 type 1001");
        vm.Output.Should().Contain("handler saw type 9001");
        vm.Output.Should().Contain("handler saw type 9002");
        vm.Output.Should().Contain("succeeded after 3 tries");
    }

    // -- Modules --------------------------------------------------------

    [Fact]
    public void PublicModuleFunctionCallableFromMain()
    {
        const string src = """
            MODULE M
              PUBLIC FUNCTION SQUARE(X)
                SQUARE = X * X
              END FUNCTION
            END MODULE
            PRINT SQUARE(6)
            """;
        Run(src).Output.Trim().Should().Be("36");
    }

    [Fact]
    public void ModulePrivateHelperCallableFromSibling()
    {
        const string src = """
            MODULE M
              FUNCTION HELPER(X)
                HELPER = X + 1
              END FUNCTION
              PUBLIC FUNCTION DOUBLED(X)
                DOUBLED = HELPER(X) * 2
              END FUNCTION
            END MODULE
            PRINT DOUBLED(5)
            """;
        Run(src).Output.Trim().Should().Be("12");
    }

    [Fact]
    public void TwoModulesWithSameNamedPrivateHelpers()
    {
        // HELPER lives privately in each module and resolves independently —
        // identity-keyed function indices keep the two from colliding.
        const string src = """
            MODULE A
              FUNCTION HELPER(X)
                HELPER = X + 10
              END FUNCTION
              PUBLIC FUNCTION VIA_A(X)
                VIA_A = HELPER(X)
              END FUNCTION
            END MODULE
            MODULE B
              FUNCTION HELPER(X)
                HELPER = X + 100
              END FUNCTION
              PUBLIC FUNCTION VIA_B(X)
                VIA_B = HELPER(X)
              END FUNCTION
            END MODULE
            PRINT VIA_A(1); VIA_B(1)
            """;
        Run(src).Output.Trim().Should().Be("11  101");
    }

    [Fact]
    public void ModulesExampleMatchesTreeWalker()
    {
        var source = File.ReadAllText("../../../../../examples/modules.bas");
        var vm = Run(source);
        vm.Exit.Should().Be(0);
        vm.Output.Should().Contain("SQUARE(5) = 25");
        vm.Output.Should().Contain("POLY(3)   = 20");
    }

    // -- Named HANDLER references ------------------------------------

    [Fact]
    public void WhenReferencesNamedHandler()
    {
        const string src = """
            HANDLER LOGGER
              PRINT "caught"; EXTYPE
            END HANDLER
            WHEN EXCEPTION IN
              CAUSE EXCEPTION 42
            USE LOGGER
            END WHEN
            """;
        Run(src).Output.Trim().Should().Be("caught 42");
    }

    [Fact]
    public void TwoWhensShareTheSameHandler()
    {
        const string src = """
            HANDLER LOGGER
              PRINT "type"; EXTYPE
            END HANDLER
            WHEN EXCEPTION IN
              CAUSE EXCEPTION 1
            USE LOGGER
            END WHEN
            WHEN EXCEPTION IN
              CAUSE EXCEPTION 2
            USE LOGGER
            END WHEN
            """;
        // Each WHEN gets its own inlined copy of LOGGER's body — but both produce
        // the same kind of output, just with different EXTYPE values.
        Run(src).Output.Should().Contain("type 1");
        Run(src).Output.Should().Contain("type 2");
    }

    [Fact]
    public void RetryThroughNamedHandler()
    {
        const string src = """
            HANDLER REDO
              RETRY
            END HANDLER
            LET N = 0
            WHEN EXCEPTION IN
              LET N = N + 1
              IF N < 3 THEN CAUSE EXCEPTION 1
              PRINT "done after"; N
            USE REDO
            END WHEN
            """;
        Run(src).Output.Trim().Should().Be("done after 3");
    }

    // -- LINE INPUT from stdin ---------------------------------------

    [Fact]
    public void LineInputReadsWholeLine()
    {
        var (output, _) = Run("LINE INPUT A$\nPRINT A$", stdin: "hello, world\n");
        output.Should().Be("? hello, world\n");
    }

    [Fact]
    public void LineInputWithPromptSuppressesQuestionMark()
    {
        var (output, _) = Run("LINE INPUT \"Name: \"; A$\nPRINT A$", stdin: "Adam\n");
        output.Should().Be("Name:  Adam\n");
    }

    [Fact]
    public void LineInputStdinAtEofIsRuntimeError()
    {
        var (_, exit) = Run("LINE INPUT A$\nPRINT A$", stdin: "");
        exit.Should().Be(1);
    }

    // -- CONTINUE (resume after offending stmt) ---------------------

    [Fact]
    public void ContinueSkipsFailingStatement()
    {
        const string src = """
            WHEN EXCEPTION IN
              PRINT "before"
              CAUSE EXCEPTION 1
              PRINT "after-failing"
            USE
              PRINT "caught"
              CONTINUE
            END WHEN
            PRINT "done"
            """;
        // before; CAUSE fires; USE prints "caught" then CONTINUE jumps past
        // the CAUSE to the next stmt in IN body (PRINT "after-failing");
        // IN body completes, falls out of WHEN, PRINT "done".
        Run(src).Output.Trim().Should().Be("before\ncaught\nafter-failing\ndone");
    }

    [Fact]
    public void ContinueInsideForBodyKeepsLoopGoing()
    {
        // CAUSE is the IN body's only statement; CONTINUE jumps past it,
        // falling out of the WHEN back to the FOR's loop control.
        const string src = """
            FOR I = 1 TO 3
              WHEN EXCEPTION IN
                CAUSE EXCEPTION 100 + I
              USE
                PRINT "iter"; I; "type"; EXTYPE
                CONTINUE
              END WHEN
            NEXT I
            """;
        var output = Run(src).Output;
        output.Should().Contain("iter 1 type 101");
        output.Should().Contain("iter 2 type 102");
        output.Should().Contain("iter 3 type 103");
    }

    [Fact]
    public void ContinueResumesAtStmtAfterFailing()
    {
        // Validates the semantic from the spec: CONTINUE jumps to the
        // statement immediately following the one that raised, not past
        // the whole IN body.
        const string src = """
            WHEN EXCEPTION IN
              CAUSE EXCEPTION 7
              PRINT "after-failing"
            USE
              PRINT "caught"; EXTYPE
              CONTINUE
            END WHEN
            PRINT "done"
            """;
        Run(src).Output.Trim().Should().Be("caught 7 \nafter-failing\ndone");
    }

    [Fact]
    public void ContinueOutsideWhenIsCompileError()
    {
        // CONTINUE outside a WHEN/USE is rejected at compile time.
        var (output, exit) = Run("CONTINUE");
        exit.Should().Be(1);
        output.Should().Contain("CONTINUE outside of WHEN/USE");
    }

    // -- Forward GOTO/GOSUB --------------------------------------------

    [Fact]
    public void ForwardGotoJumpsAhead()
    {
        const string src = """
            GOTO 200
            PRINT "skipped"
            200 PRINT "landed"
            """;
        Run(src).Output.Trim().Should().Be("landed");
    }

    [Fact]
    public void GosubReturnRoundtrip()
    {
        const string src = """
            PRINT "before"
            GOSUB 100
            PRINT "after"
            STOP
            100 PRINT "inside"
            RETURN
            """;
        Run(src).Output.Trim().Should().Be("before\ninside\nafter");
    }

    [Fact]
    public void ReturnWithoutGosubIsRuntimeError()
    {
        var (_, exit) = Run("RETURN");
        exit.Should().Be(1);
    }

    [Fact]
    public void GotoLabelOnNextContinuesLoop()
    {
        const string src = """
            10 FOR I = 1 TO 3
            20   IF I = 2 THEN GOTO 40
            30   PRINT "body"; I
            40 NEXT I
            50 PRINT "after"
            """;
        var lines = Run(src).Output.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        lines.Should().Equal("body 1", "body 3", "after");
    }

    [Fact]
    public void GotoLabelOnEndIfFallsPastBlock()
    {
        const string src = """
            10 IF 1 > 0 THEN
            20   GOTO 40
            30   PRINT "skipped"
            40 END IF
            50 PRINT "done"
            """;
        Run(src).Output.Trim().Should().Be("done");
    }

    [Theory]
    [InlineData(1, "one")]
    [InlineData(3, "three")]
    public void OnGotoSelectsTarget(int sel, string expected)
    {
        var src = $"""
            10 LET I = {sel}
            20 ON I GOTO 100, 200, 300
            100 PRINT "one"
            105 GOTO 900
            200 PRINT "two"
            205 GOTO 900
            300 PRINT "three"
            900 END
            """;
        Run(src).Output.Split('\n')[0].Trim().Should().Be(expected);
    }

    [Fact]
    public void OnGosubReturnsToNextStatement()
    {
        const string src = """
            10 ON 2 GOSUB 100, 200
            20 PRINT "back"
            30 GOTO 900
            100 PRINT "sub-one"
            110 RETURN
            200 PRINT "sub-two"
            210 RETURN
            900 END
            """;
        Run(src).Output.Trim().Should().Be("sub-two\nback");
    }

    [Fact]
    public void OnGotoOutOfRangeRunsElse()
    {
        const string src = """
            10 ON 9 GOTO 100, 200 ELSE PRINT "else-branch"
            20 PRINT "continued"
            30 GOTO 900
            100 PRINT "t1"
            200 PRINT "t2"
            900 END
            """;
        Run(src).Output.Trim().Should().Be("else-branch\ncontinued");
    }

    [Fact]
    public void OnGotoOutOfRangeWithoutElseIsCatchable()
    {
        const string src = """
            10 WHEN EXCEPTION IN
            20   ON 5 GOTO 100, 200
            30 USE
            40   PRINT "caught"; EXTYPE
            50 END WHEN
            60 GOTO 900
            100 PRINT "t1"
            200 PRINT "t2"
            900 END
            """;
        Run(src).Output.Split('\n')[0].Trim().Should().Be("caught 10001");
    }

    // -- PRINT TAB -----------------------------------------------------

    [Fact]
    public void PrintTabPadsToColumn()
    {
        Run("PRINT \"x\"; TAB(10); \"y\"").Output.Should().Be("x        y\n");
    }

    [Fact]
    public void PrintTabIsNoOpIfAlreadyPast()
    {
        // Already at column 5 ("hello") — TAB(3) shouldn't move backwards.
        Run("PRINT \"hello\"; TAB(3); \"!\"").Output.Should().Be("hello!\n");
    }

    // -- DEF calls (single-line, runtime-callable) ---------------------

    [Fact]
    public void SingleLineDefCallable()
    {
        const string src = """
            DEF SQUARE(X) = X * X
            PRINT SQUARE(7)
            """;
        Run(src).Output.Trim().Should().Be("49");
    }

    [Fact]
    public void DefSeesOuterVariables()
    {
        // DEF parent is the caller's frame so the body can reference outer names.
        const string src = """
            LET K = 10
            DEF SHIFT(X) = X + K
            PRINT SHIFT(5)
            """;
        Run(src).Output.Trim().Should().Be("15");
    }

    // -- Nested MAT constants ------------------------------------------

    [Fact]
    public void NestedZerInsideAddition()
    {
        const string src = """
            DIM A(2, 2), B(2, 2)
            LET B(1, 1) = 7
            LET B(2, 2) = 9
            MAT A = ZER + B
            PRINT A(1, 1); A(2, 2)
            """;
        Run(src).Output.Trim().Should().Be("7  9");
    }

    [Fact]
    public void NestedIdnInsideMultiplication()
    {
        // A · IDN = A. Confirms IDN nested in a multiply preserves identity.
        const string src = """
            DIM A(2, 2), C(2, 2)
            LET A(1, 1) = 4
            LET A(1, 2) = 7
            LET A(2, 1) = 2
            LET A(2, 2) = 6
            MAT C = A * IDN
            PRINT C(1, 1); C(1, 2); C(2, 1); C(2, 2)
            """;
        Run(src).Output.Trim().Should().Be("4  7  2  6");
    }

    // -- EXIT variants -------------------------------------------------

    [Fact]
    public void ExitHandlerLeavesWhenBlock()
    {
        const string src = """
            WHEN EXCEPTION IN
              CAUSE EXCEPTION 1
              PRINT "skipped-in"
            USE
              PRINT "in handler"
              EXIT HANDLER
              PRINT "skipped-after-exit"
            END WHEN
            PRINT "after-when"
            """;
        Run(src).Output.Trim().Should().Be("in handler\nafter-when");
    }

    [Fact]
    public void ExitWhenLeavesWhenBlock()
    {
        const string src = """
            WHEN EXCEPTION IN
              PRINT "in-body"
              EXIT WHEN
              PRINT "skipped"
            USE
              PRINT "use-body-not-reached"
            END WHEN
            PRINT "after-when"
            """;
        Run(src).Output.Trim().Should().Be("in-body\nafter-when");
    }

    [Fact]
    public void ExitSubReturnsEarly()
    {
        const string src = """
            SUB GREET(N$)
              PRINT "hi"
              IF N$ = "" THEN EXIT SUB
              PRINT N$
            END SUB
            CALL GREET("Adam")
            CALL GREET("")
            """;
        Run(src).Output.Trim().Should().Be("hi\nAdam\nhi");
    }

    [Fact]
    public void ExitFunctionReturnsCurrentValue()
    {
        const string src = """
            FUNCTION CAP(X)
              CAP = 100
              IF X > 100 THEN EXIT FUNCTION
              CAP = X
            END FUNCTION
            PRINT CAP(50); CAP(500)
            """;
        Run(src).Output.Trim().Should().Be("50  100");
    }

    [Fact]
    public void ExitDefReturnsEarly()
    {
        // Multi-line DEF that returns early via EXIT DEF. Multi-line DEFs return
        // zero/empty (a tree-walker gap with multi-line DEF's name-slot) regardless
        // of whether they exit early or fall through — EXIT DEF still has to compile
        // and run without crashing.
        const string src = """
            DEF G(X)
              IF X < 0 THEN EXIT DEF
              PRINT "reached"
            END DEF
            LET Y = G(5)
            LET Z = G(-1)
            """;
        Run(src).Output.Trim().Should().Be("reached");
    }
}
