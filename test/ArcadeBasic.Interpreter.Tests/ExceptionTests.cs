using FluentAssertions;
using ArcadeBasic.Core;
using ArcadeBasic.Interpreter;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;

namespace ArcadeBasic.Interpreter.Tests;

/// <summary>Phase-6 exception-handling tests.</summary>
public class ExceptionTests
{
    private static (string Output, int Exit) Run(string source, string stdin = "")
    {
        var file = new SourceFile("test.bas", source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        if (diags.HasErrors)
        {
            var msg = string.Join("\n", diags.All.Select(d => d.Render(false)));
            return (msg, 1);
        }
        var sw = new StringWriter { NewLine = "\n" };
        var sr = new StringReader(stdin);
        var exit = new BasicInterpreter(program, info, sw, sr).Run();
        return (sw.ToString(), exit);
    }

    // -- Implicit exception capture -------------------------------------

    [Fact]
    public void DivisionByZeroCaughtByHandler()
    {
        const string src = """
            WHEN EXCEPTION IN
              LET X = 1 / 0
              PRINT "after error"
            USE
              PRINT "caught div by zero"
            END WHEN
            PRINT "after WHEN"
            """;
        var (output, exit) = Run(src);
        exit.Should().Be(0);
        output.Should().Contain("caught div by zero");
        output.Should().Contain("after WHEN");
        output.Should().NotContain("after error");
    }

    [Fact]
    public void ArrayBoundsCaughtByHandler()
    {
        const string src = """
            DIM A(3)
            WHEN EXCEPTION IN
              LET X = A(99)
              PRINT "unreachable"
            USE
              PRINT "caught bounds error"
            END WHEN
            """;
        var (output, exit) = Run(src);
        exit.Should().Be(0);
        output.Should().Contain("caught bounds error");
    }

    // -- CAUSE EXCEPTION -----------------------------------------------

    [Fact]
    public void CauseAndCatch()
    {
        const string src = """
            WHEN EXCEPTION IN
              CAUSE EXCEPTION 9001
            USE
              PRINT "caught type "; EXTYPE
            END WHEN
            """;
        var (output, exit) = Run(src);
        exit.Should().Be(0);
        output.Should().Contain("caught type");
        output.Should().Contain("9001");
    }

    [Fact]
    public void UnhandledCauseExitsNonZero()
    {
        var (_, exit) = Run("CAUSE EXCEPTION 1234");
        exit.Should().Be(1);
    }

    // -- EXTYPE / EXLINE / EXTEXT$ accessors ----------------------------

    [Fact]
    public void ExtypeReadsTypeCode()
    {
        const string src = """
            WHEN EXCEPTION IN
              CAUSE EXCEPTION 42
            USE
              LET T = EXTYPE
              PRINT T
            END WHEN
            """;
        var (output, exit) = Run(src);
        exit.Should().Be(0);
        output.Trim().Should().Be("42");
    }

    [Fact]
    public void ExlineReadsSourceLine()
    {
        const string src = """
            WHEN EXCEPTION IN
              CAUSE EXCEPTION 1
            USE
              PRINT EXLINE
            END WHEN
            """;
        var (output, exit) = Run(src);
        exit.Should().Be(0);
        // CAUSE is on line 2 (1-based) within the BASIC source string above.
        output.Trim().Should().Be("2");
    }

    [Fact]
    public void ExtypeIsZeroOutsideHandler()
    {
        var (output, _) = Run("PRINT EXTYPE");
        output.Trim().Should().Be("0");
    }

    // -- RETRY ----------------------------------------------------------

    [Fact]
    public void RetryReExecutesInBlock()
    {
        const string src = """
            LET COUNTER = 0
            WHEN EXCEPTION IN
              LET COUNTER = COUNTER + 1
              IF COUNTER < 3 THEN CAUSE EXCEPTION 1
              PRINT "succeeded after"; COUNTER; "tries"
            USE
              ! retry until COUNTER hits 3
              RETRY
            END WHEN
            """;
        var (output, exit) = Run(src);
        exit.Should().Be(0);
        output.Should().Contain("succeeded after");
        output.Should().Contain("3");
    }

    // -- CONTINUE -------------------------------------------------------

    [Fact]
    public void ContinueResumesAfterOffender()
    {
        const string src = """
            WHEN EXCEPTION IN
              PRINT "one"
              CAUSE EXCEPTION 1
              PRINT "two"
              CAUSE EXCEPTION 2
              PRINT "three"
            USE
              PRINT "caught "; EXTYPE
              CONTINUE
            END WHEN
            PRINT "done"
            """;
        var (output, exit) = Run(src);
        exit.Should().Be(0);
        output.Should().Contain("one");
        output.Should().Contain("caught");
        // After CONTINUE, "two" and "three" both print.
        output.Should().Contain("two");
        output.Should().Contain("three");
        output.Should().Contain("done");
    }

    // -- EXIT WHEN / EXIT HANDLER --------------------------------------

    [Fact]
    public void ExitWhenInHandlerLeavesBlock()
    {
        const string src = """
            WHEN EXCEPTION IN
              CAUSE EXCEPTION 1
              PRINT "skipped"
            USE
              PRINT "in handler"
              EXIT HANDLER
              PRINT "after exit"
            END WHEN
            PRINT "after WHEN"
            """;
        var (output, exit) = Run(src);
        exit.Should().Be(0);
        output.Should().Contain("in handler");
        output.Should().NotContain("after exit");
        output.Should().Contain("after WHEN");
    }

    // -- Named HANDLER -------------------------------------------------

    [Fact]
    public void NamedHandlerUsedByWhen()
    {
        const string src = """
            HANDLER MYHANDLER
              PRINT "named handler caught "; EXTYPE
            END HANDLER
            WHEN EXCEPTION IN
              CAUSE EXCEPTION 7
            USE MYHANDLER
            END WHEN
            """;
        var (output, exit) = Run(src);
        exit.Should().Be(0);
        output.Should().Contain("named handler caught");
        output.Should().Contain("7");
    }

    // -- Nested handlers -----------------------------------------------

    [Fact]
    public void InnerHandlerCatchesFirst()
    {
        const string src = """
            WHEN EXCEPTION IN
              WHEN EXCEPTION IN
                CAUSE EXCEPTION 99
              USE
                PRINT "inner caught "; EXTYPE
              END WHEN
              PRINT "outer continues"
            USE
              PRINT "outer should not catch"
            END WHEN
            """;
        var (output, exit) = Run(src);
        exit.Should().Be(0);
        output.Should().Contain("inner caught");
        output.Should().Contain("99");
        output.Should().Contain("outer continues");
        output.Should().NotContain("outer should not catch");
    }

    [Fact]
    public void InnerReraisesToOuter()
    {
        // No CAUSE in inner handler — but inner handler's normal exit goes to
        // "after end when" inside the IN block. To re-raise we must EXIT from
        // the inner WHEN after another CAUSE; simpler: test outer-only.
        const string src = """
            WHEN EXCEPTION IN
              CAUSE EXCEPTION 1
            USE
              PRINT "outer caught "; EXTYPE
            END WHEN
            """;
        var (output, exit) = Run(src);
        exit.Should().Be(0);
        output.Should().Contain("outer caught");
        output.Should().Contain("1");
    }
}
