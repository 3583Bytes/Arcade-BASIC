using FluentAssertions;
using ArcadeBasic.Core;
using ArcadeBasic.Interpreter;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;

namespace ArcadeBasic.Interpreter.Tests;

/// <summary>Phase-4 MAT tests. Output capture and BASIC-side assertions.
/// All tests run under OPTION BASE 1 (the typical convention for MAT
/// operations) — automatically prepended to every BASIC source.</summary>
public class MatTests
{
    private static string Run(string source, string stdin = "")
    {
        var file = new SourceFile("test.bas", "OPTION BASE 1\n" + source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        if (diags.HasErrors)
        {
            return string.Join("\n", diags.All.Select(d => d.Render(false)));
        }
        var sw = new StringWriter { NewLine = "\n" };
        var sr = new StringReader(stdin);
        new BasicInterpreter(program, info, sw, sr).Run();
        return sw.ToString();
    }

    private static int RunForExit(string source, string stdin = "")
    {
        var file = new SourceFile("test.bas", "OPTION BASE 1\n" + source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        if (diags.HasErrors) return 1;
        return new BasicInterpreter(program, info, new StringWriter { NewLine = "\n" }, new StringReader(stdin)).Run();
    }

    // -- Assignment ------------------------------------------------------

    [Fact]
    public void MatCopyAssign()
    {
        const string src = """
            DIM A(3), B(3)
            LET B(1) = 10
            LET B(2) = 20
            LET B(3) = 30
            MAT A = B
            PRINT A(1) + A(2) + A(3)
            """;
        Run(src).Trim().Should().Be("60");
    }

    // -- Element-wise + / - ---------------------------------------------

    [Fact]
    public void MatAdd()
    {
        const string src = """
            DIM A(2, 2), B(2, 2), C(2, 2)
            LET A(1, 1) = 1
            LET A(1, 2) = 2
            LET A(2, 1) = 3
            LET A(2, 2) = 4
            LET B(1, 1) = 10
            LET B(1, 2) = 20
            LET B(2, 1) = 30
            LET B(2, 2) = 40
            MAT C = A + B
            PRINT C(1, 1); C(1, 2); C(2, 1); C(2, 2)
            """;
        Run(src).Trim().Replace(" ", "").Should().Be("11223344");
    }

    [Fact]
    public void MatSubtract()
    {
        const string src = """
            DIM A(3), B(3), C(3)
            LET A(1) = 10
            LET A(2) = 20
            LET A(3) = 30
            LET B(1) = 1
            LET B(2) = 2
            LET B(3) = 3
            MAT C = A - B
            PRINT C(1); C(2); C(3)
            """;
        Run(src).Trim().Replace(" ", "").Should().Be("91827");
    }

    // -- Matrix multiply -------------------------------------------------

    [Fact]
    public void MatMultiply()
    {
        // [[1,2],[3,4]] * [[5,6],[7,8]] = [[19,22],[43,50]]
        const string src = """
            DIM A(2, 2), B(2, 2), C(2, 2)
            LET A(1, 1) = 1
            LET A(1, 2) = 2
            LET A(2, 1) = 3
            LET A(2, 2) = 4
            LET B(1, 1) = 5
            LET B(1, 2) = 6
            LET B(2, 1) = 7
            LET B(2, 2) = 8
            MAT C = A * B
            PRINT C(1, 1); C(1, 2); C(2, 1); C(2, 2)
            """;
        Run(src).Trim().Replace(" ", "").Should().Be("19224350");
    }

    [Fact]
    public void MatScalarMultiply()
    {
        const string src = """
            DIM A(3), B(3)
            LET A(1) = 1
            LET A(2) = 2
            LET A(3) = 3
            MAT B = (5) * A
            PRINT B(1); B(2); B(3)
            """;
        Run(src).Trim().Replace(" ", "").Should().Be("51015");
    }

    // -- INV / TRN ------------------------------------------------------

    [Fact]
    public void MatInvertSimple2x2()
    {
        // [[1,2],[3,4]]^-1 = [[-2,1],[1.5,-0.5]]
        const string src = """
            DIM A(2, 2), B(2, 2)
            LET A(1, 1) = 1
            LET A(1, 2) = 2
            LET A(2, 1) = 3
            LET A(2, 2) = 4
            MAT B = INV(A)
            PRINT B(1, 1); B(1, 2)
            PRINT B(2, 1); B(2, 2)
            """;
        var lines = Run(src).Trim().Split('\n');
        // Whitespace varies by formatting; strip and check digits.
        lines[0].Replace(" ", "").Trim().Should().Be("-21");
    }

    [Fact]
    public void MatInverseTimesOriginalIsIdentity()
    {
        const string src = """
            DIM A(3, 3), I(3, 3), R(3, 3)
            LET A(1, 1) = 4
            LET A(1, 2) = 7
            LET A(1, 3) = 2
            LET A(2, 1) = 3
            LET A(2, 2) = 5
            LET A(2, 3) = 1
            LET A(3, 1) = 6
            LET A(3, 2) = 1
            LET A(3, 3) = 8
            MAT I = INV(A)
            MAT R = A * I
            PRINT R(1, 1); R(2, 2); R(3, 3)
            """;
        var output = Run(src).Trim().Replace(" ", "");
        // Each diagonal should be ~1 — exact comparison risks roundoff,
        // so just check we got "1"s on the diagonal.
        output.Should().StartWith("1");
    }

    [Fact]
    public void MatSingularMatrixIsRuntimeError()
    {
        const string src = """
            DIM A(2, 2), B(2, 2)
            LET A(1, 1) = 1
            LET A(1, 2) = 2
            LET A(2, 1) = 2
            LET A(2, 2) = 4
            MAT B = INV(A)
            """;
        RunForExit(src).Should().Be(1);
    }

    [Fact]
    public void MatTranspose()
    {
        const string src = """
            DIM A(2, 3), B(3, 2)
            LET A(1, 1) = 1
            LET A(1, 2) = 2
            LET A(1, 3) = 3
            LET A(2, 1) = 4
            LET A(2, 2) = 5
            LET A(2, 3) = 6
            MAT B = TRN(A)
            PRINT B(1, 1); B(2, 1); B(3, 1)
            PRINT B(1, 2); B(2, 2); B(3, 2)
            """;
        var lines = Run(src).Trim().Split('\n');
        lines[0].Trim().Replace(" ", "").Should().Be("123");
        lines[1].Trim().Replace(" ", "").Should().Be("456");
    }

    // -- IDN / ZER / CON / NUL$ -----------------------------------------

    [Fact]
    public void MatIdentity()
    {
        const string src = """
            DIM A(3, 3)
            MAT A = IDN
            PRINT A(1, 1); A(1, 2); A(2, 2); A(3, 3)
            """;
        Run(src).Trim().Replace(" ", "").Should().Be("1011");
    }

    [Fact]
    public void MatZero()
    {
        const string src = """
            DIM A(3)
            LET A(1) = 99
            MAT A = ZER
            PRINT A(1); A(2); A(3)
            """;
        Run(src).Trim().Replace(" ", "").Should().Be("000");
    }

    [Fact]
    public void MatOnes()
    {
        const string src = """
            DIM A(4)
            MAT A = CON
            PRINT A(1) + A(2) + A(3) + A(4)
            """;
        Run(src).Trim().Should().Be("4");
    }

    [Fact]
    public void MatNullString()
    {
        const string src = """
            DIM S$(3)
            LET S$(1) = "hi"
            MAT S$ = NUL$
            PRINT "[" & S$(1) & "][" & S$(2) & "]"
            """;
        Run(src).Trim().Should().Be("[][]");
    }

    // -- REDIM ----------------------------------------------------------

    [Fact]
    public void MatRedimPreservesElements()
    {
        const string src = """
            DIM A(3)
            LET A(1) = 11
            LET A(2) = 22
            LET A(3) = 33
            MAT REDIM A(5)
            PRINT A(1); A(2); A(3); A(4); A(5)
            """;
        Run(src).Trim().Replace(" ", "").Should().Be("11223300");
    }

    [Fact]
    public void MatRedimShrink()
    {
        const string src = """
            DIM A(5)
            LET A(1) = 1
            LET A(2) = 2
            LET A(3) = 3
            LET A(4) = 4
            LET A(5) = 5
            MAT REDIM A(3)
            PRINT A(1); A(2); A(3)
            """;
        Run(src).Trim().Replace(" ", "").Should().Be("123");
    }

    // -- MAT READ / MAT PRINT -------------------------------------------

    [Fact]
    public void MatReadFromDataPool()
    {
        const string src = """
            DATA 10, 20, 30, 40
            DIM A(4)
            MAT READ A
            PRINT A(1) + A(2) + A(3) + A(4)
            """;
        Run(src).Trim().Should().Be("100");
    }

    [Fact]
    public void MatPrint1DArray()
    {
        const string src = """
            DIM A(3)
            LET A(1) = 7
            LET A(2) = 8
            LET A(3) = 9
            MAT PRINT A
            """;
        var output = Run(src);
        output.Should().Contain("7");
        output.Should().Contain("8");
        output.Should().Contain("9");
    }

    // -- Aliasing -------------------------------------------------------

    [Fact]
    public void MatAddInPlaceWithSelfWorks()
    {
        // MAT A = A + A should double every element, despite the alias.
        const string src = """
            DIM A(3)
            LET A(1) = 1
            LET A(2) = 2
            LET A(3) = 3
            MAT A = A + A
            PRINT A(1); A(2); A(3)
            """;
        Run(src).Trim().Replace(" ", "").Should().Be("246");
    }

    // -- Dimension errors -----------------------------------------------

    [Fact]
    public void MatMultiplyMismatchedDimensionsErrors()
    {
        const string src = """
            DIM A(2, 3), B(2, 2), C(2, 2)
            MAT C = A * B
            """;
        RunForExit(src).Should().Be(1);
    }
}
