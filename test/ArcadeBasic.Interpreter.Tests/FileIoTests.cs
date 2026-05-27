using FluentAssertions;
using ArcadeBasic.Core;
using ArcadeBasic.Interpreter;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;

namespace ArcadeBasic.Interpreter.Tests;

/// <summary>
/// Phase-5 file I/O tests. Use a per-test temp directory so we don't pollute
/// $PWD; programs receive the path via DATA + READ so the BASIC source stays
/// portable.
/// </summary>
public class FileIoTests : IDisposable
{
    private readonly string _tmpDir;

    public FileIoTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "fb-io-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { /* best effort */ }
    }

    private string TempFile(string name) => Path.Combine(_tmpDir, name);

    private (string Output, int Exit, DiagnosticBag Diagnostics) Run(string source, string stdin = "")
    {
        var file = new SourceFile("test.bas", source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        if (diags.HasErrors)
        {
            return (string.Join("\n", diags.All.Select(d => d.Render(false))), 1, diags);
        }
        var sw = new StringWriter();
        var sr = new StringReader(stdin);
        var exit = new BasicInterpreter(program, info, sw, sr).Run();
        return (sw.ToString(), exit, diags);
    }

    // -- Round-trips ----------------------------------------------------

    [Fact]
    public void WriteThenRead()
    {
        var path = TempFile("rt.txt");
        var src = $"""
            OPEN #1: NAME "{path}", ACCESS OUTPUT
            PRINT #1: "hello"
            PRINT #1: "world"
            CLOSE #1
            OPEN #1: NAME "{path}", ACCESS INPUT
            LINE INPUT #1: A$
            LINE INPUT #1: B$
            CLOSE #1
            PRINT A$ & " " & B$
            """;
        var (output, exit, _) = Run(src);
        exit.Should().Be(0);
        output.Trim().Should().Be("hello world");
    }

    [Fact]
    public void NumericRoundtrip()
    {
        var path = TempFile("nums.txt");
        var src = $"""
            OPEN #2: NAME "{path}", ACCESS OUTPUT
            FOR I = 1 TO 5
              PRINT #2: I
            NEXT I
            CLOSE #2
            OPEN #2: NAME "{path}", ACCESS INPUT
            LET S = 0
            FOR I = 1 TO 5
              INPUT #2: N
              LET S = S + N
            NEXT I
            CLOSE #2
            PRINT S
            """;
        var (output, exit, _) = Run(src);
        exit.Should().Be(0);
        output.Trim().Should().Be("15");
    }

    [Fact]
    public void CommaSeparatedFieldsOnSameLine()
    {
        var path = TempFile("csv.txt");
        File.WriteAllText(path, "1,2,3\n4,5,6\n");
        var src = $"""
            OPEN #3: NAME "{path}", ACCESS INPUT
            INPUT #3: A, B, C
            INPUT #3: D, E, F
            CLOSE #3
            PRINT A + B + C + D + E + F
            """;
        var (output, exit, _) = Run(src);
        exit.Should().Be(0);
        output.Trim().Should().Be("21");
    }

    [Fact]
    public void LineInputReadsWholeLineIncludingCommas()
    {
        var path = TempFile("line.txt");
        File.WriteAllText(path, "first, with comma\nsecond line\n");
        var src = $"""
            OPEN #1: NAME "{path}", ACCESS INPUT
            LINE INPUT #1: L$
            CLOSE #1
            PRINT L$
            """;
        var (output, exit, _) = Run(src);
        exit.Should().Be(0);
        output.Trim().Should().Be("first, with comma");
    }

    // -- Error cases ----------------------------------------------------

    [Fact]
    public void OpeningNonexistentForInputFails()
    {
        var path = TempFile("missing.txt");
        var src = $"""
            OPEN #1: NAME "{path}", ACCESS INPUT
            """;
        var (_, exit, _) = Run(src);
        exit.Should().Be(1);
    }

    [Fact]
    public void ReadingPastEndOfFileFails()
    {
        var path = TempFile("short.txt");
        File.WriteAllText(path, "only one line\n");
        var src = $"""
            OPEN #1: NAME "{path}", ACCESS INPUT
            LINE INPUT #1: A$
            LINE INPUT #1: B$
            CLOSE #1
            """;
        var (_, exit, _) = Run(src);
        exit.Should().Be(1);
    }

    [Fact]
    public void Channel0IsReserved()
    {
        var path = TempFile("zero.txt");
        var src = $"""
            OPEN #0: NAME "{path}", ACCESS OUTPUT
            """;
        var (_, exit, _) = Run(src);
        exit.Should().Be(1);
    }

    [Fact]
    public void DoubleOpenFails()
    {
        var path = TempFile("double.txt");
        var src = $"""
            OPEN #1: NAME "{path}", ACCESS OUTPUT
            OPEN #1: NAME "{path}", ACCESS OUTPUT
            """;
        var (_, exit, _) = Run(src);
        exit.Should().Be(1);
    }

    // -- CREATE clause --------------------------------------------------

    [Fact]
    public void CreateNewSucceedsForFreshFile()
    {
        var path = TempFile("new.txt");
        var src = $"""
            OPEN #1: NAME "{path}", ACCESS OUTPUT, CREATE NEW
            PRINT #1: "fresh"
            CLOSE #1
            """;
        var (_, exit, _) = Run(src);
        exit.Should().Be(0);
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void CreateNewFailsForExistingFile()
    {
        var path = TempFile("exists.txt");
        File.WriteAllText(path, "previously");
        var src = $"""
            OPEN #1: NAME "{path}", ACCESS OUTPUT, CREATE NEW
            """;
        var (_, exit, _) = Run(src);
        exit.Should().Be(1);
    }

    [Fact]
    public void CreateOldFailsForMissingFile()
    {
        var path = TempFile("ghost.txt");
        var src = $"""
            OPEN #1: NAME "{path}", ACCESS INPUT, CREATE OLD
            """;
        var (_, exit, _) = Run(src);
        exit.Should().Be(1);
    }
}
