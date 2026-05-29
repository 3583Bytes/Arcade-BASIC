using FluentAssertions;
using ArcadeBasic.Core;
using ArcadeBasic.Interpreter;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;

namespace ArcadeBasic.Interpreter.Tests;

/// <summary>Phase-8a tests for PRINT USING.</summary>
public class PrintUsingTests
{
    private static string Run(string source)
    {
        var file = new SourceFile("test.bas", source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        if (diags.HasErrors)
        {
            return string.Join("\n", diags.All.Select(d => d.Render(false)));
        }
        var sw = new StringWriter { NewLine = "\n" };
        new BasicInterpreter(program, info, sw, new StringReader("")).Run();
        return sw.ToString();
    }

    // -- Basic numeric formatting ---------------------------------------

    [Fact]
    public void IntegerWidth5()
    {
        Run("PRINT USING \"#####\": 42").TrimEnd().Should().Be("   42");
    }

    [Fact]
    public void DecimalDefault()
    {
        Run("PRINT USING \"##.##\": 3.14").TrimEnd().Should().Be(" 3.14");
    }

    [Fact]
    public void DecimalRoundsHalfAway()
    {
        Run("PRINT USING \"##.##\": 3.145").TrimEnd().Should().Be(" 3.15");
        Run("PRINT USING \"##.##\": 3.155").TrimEnd().Should().Be(" 3.16");
    }

    [Fact]
    public void NegativeWithoutSignSlot()
    {
        // Spec: a numeric field without a sign placeholder still shows '-' for
        // negatives by consuming one of the digit slots.
        var output = Run("PRINT USING \"##.##\": -3.14").TrimEnd();
        output.Should().Contain("3.14");
        output.Should().Contain("-");
    }

    [Fact]
    public void ZeroFillIntPart()
    {
        Run("PRINT USING \"00.00\": 3.14").TrimEnd().Should().Be("03.14");
    }

    [Fact]
    public void ExplicitPlusSign()
    {
        Run("PRINT USING \"+##.##\": 3.14").TrimEnd().Should().Be("+ 3.14");
        Run("PRINT USING \"+##.##\": -3.14").TrimEnd().Should().Be("- 3.14");
    }

    [Fact]
    public void OverflowFillsAsterisks()
    {
        // 1000 won't fit in two integer digits.
        var output = Run("PRINT USING \"##.##\": 1000").TrimEnd();
        output.Should().StartWith("**");
    }

    // -- String formatting ----------------------------------------------

    [Fact]
    public void StringLeftJustified()
    {
        Run("PRINT USING \"<########\": \"hi\"").TrimEnd().Should().Be("hi");
    }

    [Fact]
    public void StringRightJustified()
    {
        Run("PRINT USING \">########\": \"hi\"").TrimEnd().Should().Be("      hi");
    }

    [Fact]
    public void StringCentered()
    {
        // Width 8, "hi" length 2, so 3 spaces on each side.
        var output = Run("PRINT USING \"=########\": \"hi\"");
        // strip trailing newline only; preserve content padding
        var trimmed = output.TrimEnd('\n', '\r');
        trimmed.Should().Be("   hi   ");
    }

    [Fact]
    public void StringTruncatesIfTooLong()
    {
        Run("PRINT USING \"<###\": \"hello\"").TrimEnd().Should().Be("hel");
    }

    // -- Literal text + multi-field ------------------------------------

    [Fact]
    public void LiteralTextPassesThrough()
    {
        Run("PRINT USING \"x = ##.##\": 42").TrimEnd().Should().Contain("42.00").And.Contain("x =");
    }

    [Fact]
    public void TwoNumericFieldsInOneFormat()
    {
        // "## ##" — two 2-digit fields separated by a space.
        Run("PRINT USING \"## + ## = ##\": 1, 2, 3").TrimEnd().Should().Contain("1").And.Contain("2").And.Contain("3");
    }

    [Fact]
    public void FormatCyclesIfMoreItemsThanFields()
    {
        // Single ## field, three values — repeats three times.
        var output = Run("PRINT USING \"##\": 1, 2, 3").TrimEnd();
        output.Should().Contain("1");
        output.Should().Contain("2");
        output.Should().Contain("3");
    }

    // -- Realistic table output ----------------------------------------

    [Fact]
    public void TabularOutput()
    {
        const string src = """
            FOR I = 1 TO 3
              PRINT USING "row ##: ###.##": I, I * 1.5
            NEXT I
            """;
        var output = Run(src);
        var lines = output.Split('\n').Where(l => l.Trim().Length > 0).ToArray();
        lines.Should().HaveCount(3);
        lines[0].Should().Contain("row").And.Contain("1.50");
        lines[1].Should().Contain("row").And.Contain("3.00");
        lines[2].Should().Contain("row").And.Contain("4.50");
    }
}
