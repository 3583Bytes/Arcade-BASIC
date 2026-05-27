using FluentAssertions;
using FullBasic.Compiler;
using FullBasic.Core;
using FullBasic.Lexer;
using FullBasic.Parser;
using FullBasic.Sema;
using FullBasic.Vm;

namespace FullBasic.Vm.Tests;

/// <summary>
/// Phase-9 VM tests. Covers the supported subset (no arrays, no MAT, no I/O
/// beyond PRINT, no exceptions, no modules, no PRINT USING, no DEF). For
/// these scenarios the VM should match the tree-walker's output.
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
        FullBasic.Bytecode.Program compiled;
        try
        {
            compiled = BasicCompiler.Compile(program, info);
        }
        catch (BasicCompiler.UnsupportedFeatureException ex)
        {
            return (ex.Message, 1);
        }
        var sw = new StringWriter();
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

    // -- Unsupported feature errors ------------------------------------

    [Fact]
    public void DimRejected()
    {
        var (output, exit) = Run("DIM A(5)");
        exit.Should().Be(1);
        output.Should().Contain("not yet supported");
    }

    [Fact]
    public void MatRejected()
    {
        var (output, exit) = Run("DIM A(3, 3)\nMAT A = ZER");
        exit.Should().Be(1);
        output.Should().Contain("not yet supported");
    }
}
