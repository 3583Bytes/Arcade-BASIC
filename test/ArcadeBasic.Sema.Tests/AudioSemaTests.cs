using FluentAssertions;
using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;

namespace ArcadeBasic.Sema.Tests;

/// <summary>Type-checking of the audio statements SOUND / BEEP / PLAY.</summary>
public class AudioSemaTests
{
    private static DiagnosticBag Analyze(string source)
    {
        var file = new SourceFile("test.bas", source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        Analyzer.Analyze(program, diags);
        return diags;
    }

    [Fact]
    public void SoundAcceptsNumericArguments()
    {
        Analyze("SOUND 440, 9").HasErrors.Should().BeFalse();
    }

    [Fact]
    public void SoundRejectsStringFrequency()
    {
        Analyze("SOUND \"x\", 9").HasErrors.Should().BeTrue();
    }

    [Fact]
    public void SoundRejectsStringDuration()
    {
        Analyze("SOUND 440, \"x\"").HasErrors.Should().BeTrue();
    }

    [Fact]
    public void BeepHasNoOperandsAndAnalyzesClean()
    {
        Analyze("BEEP").HasErrors.Should().BeFalse();
    }

    [Fact]
    public void PlayAcceptsStringNotes()
    {
        Analyze("PLAY \"CDE\"").HasErrors.Should().BeFalse();
        Analyze("LET M$ = \"CDE\"\nPLAY M$").HasErrors.Should().BeFalse();
    }

    [Fact]
    public void PlayRejectsNumericNotes()
    {
        Analyze("PLAY 42").HasErrors.Should().BeTrue();
    }
}
