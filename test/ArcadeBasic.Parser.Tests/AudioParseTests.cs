using FluentAssertions;
using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Parser.Ast;

namespace ArcadeBasic.Parser.Tests;

/// <summary>Parsing of the audio statements SOUND / BEEP / PLAY.</summary>
public class AudioParseTests
{
    private static (Program Program, DiagnosticBag Diagnostics) Parse(string source)
    {
        var file = new SourceFile("test.bas", source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var prog = new BasicParser(tokens, file, diags).ParseProgram();
        return (prog, diags);
    }

    private static T SingleStmt<T>(string source) where T : Stmt
    {
        var (prog, diags) = Parse(source);
        diags.HasErrors.Should().BeFalse();
        prog.Statements.Should().HaveCount(1);
        return prog.Statements[0].Should().BeOfType<T>().Subject;
    }

    [Fact]
    public void SoundParsesFrequencyAndDuration()
    {
        var s = SingleStmt<SoundStmt>("SOUND 440, 9");
        s.Frequency.Should().NotBeNull();
        s.Duration.Should().NotBeNull();
    }

    [Fact]
    public void SoundRequiresDuration()
    {
        // GW-BASIC SOUND takes both arguments; the comma + duration are required.
        var (_, diags) = Parse("SOUND 440");
        diags.HasErrors.Should().BeTrue();
    }

    [Fact]
    public void BeepParsesWithNoOperands()
    {
        SingleStmt<BeepStmt>("BEEP");
    }

    [Fact]
    public void PlayParsesNotesExpression()
    {
        var p = SingleStmt<PlayStmt>("PLAY \"CDEFG\"");
        p.Notes.Should().NotBeNull();
    }

    [Fact]
    public void PlayAcceptsAStringVariable()
    {
        // PLAY's operand is any string expression, not just a literal.
        var (prog, diags) = Parse("LET M$ = \"CDE\"\nPLAY M$");
        diags.HasErrors.Should().BeFalse();
        prog.Statements[1].Should().BeOfType<PlayStmt>();
    }
}
