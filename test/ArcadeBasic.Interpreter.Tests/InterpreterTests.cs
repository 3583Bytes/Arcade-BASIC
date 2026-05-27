using FluentAssertions;
using FullBasic.Core;
using FullBasic.Interpreter;
using FullBasic.Lexer;
using FullBasic.Parser;
using FullBasic.Sema;

namespace FullBasic.Interpreter.Tests;

public class InterpreterTests
{
    private static (string Output, int ExitCode, DiagnosticBag Diagnostics) Run(string source, string stdin = "")
    {
        var file = new SourceFile("test.bas", source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);

        if (diags.HasErrors) return ("", 1, diags);

        var output = new StringWriter();
        var input = new StringReader(stdin);
        var interp = new BasicInterpreter(program, info, output, input);
        var exit = interp.Run();
        return (output.ToString(), exit, diags);
    }

    /// <summary>Splits and trims; drops empty lines (so trailing newlines don't leak into Last()).</summary>
    private static string[] Lines(string s) =>
        s.Split('\n').Select(l => l.TrimEnd('\r', ' ')).Where(l => l.Length > 0).ToArray();

    // -- Assignment + print ---------------------------------------------

    [Fact]
    public void HelloWorld()
    {
        var (output, _, _) = Run("PRINT \"hello\"");
        output.Trim().Should().Be("hello");
    }

    [Fact]
    public void NumericPrint()
    {
        var (output, _, _) = Run("PRINT 42");
        output.Trim().Should().Be("42");
    }

    [Fact]
    public void MultipleAssignmentsAndPrint()
    {
        var (output, _, _) = Run("LET X = 10\nLET Y = 20\nPRINT X + Y");
        output.Trim().Should().Be("30");
    }

    [Fact]
    public void StringConcatPrints()
    {
        var (output, _, _) = Run("LET S$ = \"foo\" & \"bar\"\nPRINT S$");
        output.Trim().Should().Be("foobar");
    }

    [Fact]
    public void PrintSemicolonSuppressesNewline()
    {
        var (output, _, _) = Run("PRINT \"a\";\nPRINT \"b\"");
        output.Trim().Should().Be("ab");
    }

    [Fact]
    public void PrintCommaPadsToZone()
    {
        var (output, _, _) = Run("PRINT 1, 2");
        // Zone-1 + zone-2 alignment; both numbers get leading-space + trailing-space.
        // " 1 " + 13 spaces + " 2 " = 19 chars total.
        var line = output.TrimEnd('\n', '\r');
        line.Length.Should().BeGreaterThan(15);
        line.Should().Contain("1");
        line.Should().Contain("2");
    }

    // -- Arithmetic ------------------------------------------------------

    [Theory]
    [InlineData("PRINT 1 + 2 * 3", "7")]
    [InlineData("PRINT (1 + 2) * 3", "9")]
    [InlineData("PRINT 2 ^ 10", "1024")]
    [InlineData("PRINT 10 / 4", "2.5")]
    [InlineData("PRINT 10 - 3", "7")]
    [InlineData("PRINT -5 + 3", "-2")]
    [InlineData("PRINT 10 MOD 3", "1")]
    public void Arithmetic(string program, string expected)
    {
        var (output, _, _) = Run(program);
        output.Trim().Should().Be(expected);
    }

    [Fact]
    public void DivisionByZeroIsRuntimeError()
    {
        var (_, exit, _) = Run("LET X = 1 / 0");
        exit.Should().Be(1);
    }

    [Fact]
    public void DecimalArithmeticIsExact()
    {
        // 0.1 + 0.2 should be exactly 0.3 with BigDecimal — not 0.30000000000000004.
        var (output, _, _) = Run("PRINT 0.1 + 0.2");
        output.Trim().Should().Be("0.3");
    }

    // -- Control flow ----------------------------------------------------

    [Fact]
    public void IfThenSingleLine()
    {
        var (output, _, _) = Run("IF 1 > 0 THEN PRINT \"yes\"");
        output.Trim().Should().Be("yes");
    }

    [Fact]
    public void IfThenElseSingleLine()
    {
        var (output, _, _) = Run("IF 1 < 0 THEN PRINT \"y\" ELSE PRINT \"n\"");
        output.Trim().Should().Be("n");
    }

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
        var (output, _, _) = Run(src);
        output.Trim().Should().Be("five");
    }

    [Fact]
    public void ForLoopSums()
    {
        const string src = """
            LET S = 0
            FOR I = 1 TO 10
              LET S = S + I
            NEXT I
            PRINT S
            """;
        var (output, _, _) = Run(src);
        output.Trim().Should().Be("55");
    }

    [Fact]
    public void ForLoopWithStep()
    {
        const string src = """
            LET S = 0
            FOR I = 0 TO 10 STEP 2
              LET S = S + I
            NEXT I
            PRINT S
            """;
        var (output, _, _) = Run(src);
        output.Trim().Should().Be("30");
    }

    [Fact]
    public void ForLoopNegativeStep()
    {
        const string src = """
            LET P = 1
            FOR I = 5 TO 1 STEP -1
              LET P = P * I
            NEXT I
            PRINT P
            """;
        var (output, _, _) = Run(src);
        output.Trim().Should().Be("120");
    }

    [Fact]
    public void DoWhileLoop()
    {
        const string src = """
            LET X = 0
            DO WHILE X < 5
              LET X = X + 1
            LOOP
            PRINT X
            """;
        var (output, _, _) = Run(src);
        output.Trim().Should().Be("5");
    }

    [Fact]
    public void DoLoopUntil()
    {
        const string src = """
            LET X = 0
            DO
              LET X = X + 1
            LOOP UNTIL X >= 3
            PRINT X
            """;
        var (output, _, _) = Run(src);
        output.Trim().Should().Be("3");
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
        var (output, _, _) = Run(src);
        output.Trim().Should().Be("5");
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
        var (output, _, _) = Run(src);
        output.Trim().Should().Be("mid");
    }

    [Fact]
    public void SelectCaseIs()
    {
        const string src = """
            LET X = 1000
            SELECT CASE X
              CASE IS > 100
                PRINT "big"
              CASE ELSE
                PRINT "other"
            END SELECT
            """;
        var (output, _, _) = Run(src);
        output.Trim().Should().Be("big");
    }

    // -- GOTO / GOSUB ---------------------------------------------------

    [Fact]
    public void GotoSkipsStatements()
    {
        const string src = """
            100 PRINT "first"
            110 GOTO 130
            120 PRINT "skipped"
            130 PRINT "last"
            """;
        var (output, _, _) = Run(src);
        var lines = Lines(output);
        lines[0].Trim().Should().Be("first");
        lines[1].Trim().Should().Be("last");
        lines.Length.Should().Be(2);
    }

    [Fact]
    public void GosubReturns()
    {
        const string src = """
            100 PRINT "before"
            110 GOSUB 200
            120 PRINT "after"
            130 GOTO 999
            200 PRINT "in sub"
            210 RETURN
            999 END
            """;
        var (output, _, _) = Run(src);
        var lines = Lines(output);
        lines[0].Trim().Should().Be("before");
        lines[1].Trim().Should().Be("in sub");
        lines[2].Trim().Should().Be("after");
    }

    // -- Builtins -------------------------------------------------------

    [Theory]
    [InlineData("PRINT ABS(-5)", "5")]
    [InlineData("PRINT SGN(-3)", "-1")]
    [InlineData("PRINT INT(3.7)", "3")]
    [InlineData("PRINT INT(-3.7)", "-4")]
    [InlineData("PRINT TRUNCATE(-3.7)", "-3")]
    [InlineData("PRINT MAX(1, 5, 3, 2)", "5")]
    [InlineData("PRINT MIN(1, 5, 3, 2)", "1")]
    public void BuiltinsNumeric(string program, string expected)
    {
        var (output, _, _) = Run(program);
        output.Trim().Should().Be(expected);
    }

    [Theory]
    [InlineData("PRINT LEN(\"hello\")", "5")]
    [InlineData("PRINT LEN(\"\")", "0")]
    [InlineData("PRINT MID$(\"abcdef\", 2, 3)", "bcd")]
    [InlineData("PRINT LEFT$(\"abcdef\", 3)", "abc")]
    [InlineData("PRINT RIGHT$(\"abcdef\", 3)", "def")]
    [InlineData("PRINT UCASE$(\"hello\")", "HELLO")]
    [InlineData("PRINT LCASE$(\"HELLO\")", "hello")]
    [InlineData("PRINT REPEAT$(\"ab\", 3)", "ababab")]
    [InlineData("PRINT CHR$(65)", "A")]
    [InlineData("PRINT ORD(\"A\")", "65")]
    public void BuiltinsString(string program, string expected)
    {
        var (output, _, _) = Run(program);
        output.Trim().Should().Be(expected);
    }

    [Fact]
    public void LenCountsCodepointsNotBytes()
    {
        // "π" is 1 codepoint, even though it's 2 UTF-8 bytes.
        var (output, _, _) = Run("PRINT LEN(\"π\")");
        output.Trim().Should().Be("1");
    }

    [Fact]
    public void EmojiSurrogatePairCountsAsOne()
    {
        // 😀 is U+1F600 — outside the BMP, encoded as a UTF-16 surrogate pair.
        var (output, _, _) = Run("PRINT LEN(\"😀\")");
        output.Trim().Should().Be("1");
    }

    [Fact]
    public void PiConstant()
    {
        var (output, _, _) = Run("PRINT PI");
        output.Trim().Should().StartWith("3.14159");
    }

    // -- SUB / FUNCTION / DEF -------------------------------------------

    [Fact]
    public void SubAndCall()
    {
        const string src = """
            SUB GREET(NAME$)
              PRINT "hi " & NAME$
            END SUB
            CALL GREET("Adam")
            """;
        var (output, _, _) = Run(src);
        output.Trim().Should().Be("hi Adam");
    }

    [Fact]
    public void Function()
    {
        const string src = """
            FUNCTION DOUBLE(X)
              DOUBLE = X * 2
            END FUNCTION
            PRINT DOUBLE(7)
            """;
        var (output, _, _) = Run(src);
        output.Trim().Should().Be("14");
    }

    [Fact]
    public void DefSingleLine()
    {
        const string src = """
            DEF FN SQUARE(X) = X * X
            PRINT FN SQUARE(6)
            """;
        var (output, _, _) = Run(src);
        output.Trim().Should().Be("36");
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
        var (output, _, _) = Run(src);
        output.Trim().Should().Be("30");
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
        var (output, _, _) = Run(src);
        output.Trim().Should().Be("720");
    }

    // -- DATA / READ ----------------------------------------------------

    [Fact]
    public void DataReadFlow()
    {
        const string src = """
            DATA 10, 20, 30
            READ A, B, C
            PRINT A + B + C
            """;
        var (output, _, _) = Run(src);
        output.Trim().Should().Be("60");
    }

    [Fact]
    public void DataMixedTypes()
    {
        const string src = """
            DATA "hello", 42
            READ S$, N
            PRINT S$ & " " & STR$(N)
            """;
        var (output, _, _) = Run(src);
        output.Trim().Should().Be("hello 42");
    }

    // -- DIM / arrays ---------------------------------------------------

    [Fact]
    public void ArrayReadWrite()
    {
        const string src = """
            DIM A(5)
            FOR I = 1 TO 5
              LET A(I) = I * I
            NEXT I
            PRINT A(3)
            """;
        var (output, _, _) = Run(src);
        output.Trim().Should().Be("9");
    }

    [Fact]
    public void StringArray()
    {
        const string src = """
            DIM N$(3)
            LET N$(1) = "a"
            LET N$(2) = "b"
            LET N$(3) = "c"
            PRINT N$(1) & N$(2) & N$(3)
            """;
        var (output, _, _) = Run(src);
        output.Trim().Should().Be("abc");
    }

    [Fact]
    public void TwoDimensionalArray()
    {
        const string src = """
            DIM M(2, 3)
            LET M(1, 1) = 11
            LET M(1, 2) = 12
            LET M(2, 3) = 23
            PRINT M(1, 1); M(1, 2); M(2, 3)
            """;
        var (output, _, _) = Run(src);
        output.Trim().Replace(" ", "").Should().Be("111223");
    }

    [Fact]
    public void ArrayOutOfBoundsIsRuntimeError()
    {
        var (_, exit, _) = Run("DIM A(5)\nLET X = A(99)");
        exit.Should().Be(1);
    }

    // -- INPUT ----------------------------------------------------------

    [Fact]
    public void InputReadsLine()
    {
        var (output, _, _) = Run("INPUT N\nPRINT N * 2", stdin: "21\n");
        // Prompt and PRINT output share a line in BASIC convention; just check value.
        output.Should().Contain("42");
    }

    [Fact]
    public void InputStringWithPrompt()
    {
        var (output, _, _) = Run("INPUT \"name? \"; N$\nPRINT \"hi \" & N$", stdin: "Adam\n");
        output.Should().Contain("hi Adam");
    }

    // -- Realistic programs ---------------------------------------------

    [Fact]
    public void Factorial()
    {
        const string src = """
            FUNCTION FACT(N)
              LET F = 1
              FOR I = 1 TO N
                LET F = F * I
              NEXT I
              FACT = F
            END FUNCTION
            PRINT FACT(10)
            """;
        var (output, _, _) = Run(src);
        output.Trim().Should().Be("3628800");
    }

    [Fact]
    public void Fibonacci()
    {
        const string src = """
            DIM F(10)
            LET F(1) = 1
            LET F(2) = 1
            FOR I = 3 TO 10
              LET F(I) = F(I - 1) + F(I - 2)
            NEXT I
            PRINT F(10)
            """;
        var (output, _, _) = Run(src);
        output.Trim().Should().Be("55");
    }

    [Fact]
    public void StringBuildup()
    {
        const string src = """
            LET S$ = ""
            FOR I = 1 TO 5
              LET S$ = S$ & STR$(I)
            NEXT I
            PRINT S$
            """;
        var (output, _, _) = Run(src);
        // STR$(N) prepends a space for non-negative numbers in BASIC convention.
        output.Trim().Replace(" ", "").Should().Be("12345");
    }
}
