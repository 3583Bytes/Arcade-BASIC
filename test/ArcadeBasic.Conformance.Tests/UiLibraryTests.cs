using ArcadeBasic.Core;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;
using ArcadeBasic.Compiler;
using ArcadeBasic.Vm;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Conformance.Tests;

/// <summary>
/// examples/ui.bas, the components library: programs that use it (LIBRARY "ui.bas"), run
/// on a recording device — whose text is GfxTextMetrics' estimate, so a monospace
/// character is 0.6 of its height: at UIBODY-like size 10, 6 points across.
/// </summary>
[Collection(ScoreFiles.Collection)]
public class UiLibraryTests
{
    private static string Run(string source)
    {
        var diags = new DiagnosticBag();
        var program = ProgramLoader.Load(new SourceFile("try.bas", "LIBRARY \"ui.bas\"\n" + source), (name, _) =>
        {
            var path = Path.Combine(ExamplesDir(), name);
            return File.Exists(path) ? new SourceFile(path, File.ReadAllText(path)) : null;
        }, diags);
        var info = Analyzer.Analyze(program, diags);
        Assert.False(diags.HasErrors, string.Join("\n", diags.All.Select(d => d.Render(false))));
        var printed = new StringWriter();
        new BasicVm(BasicCompiler.Compile(program, info), printed, TextReader.Null, new RecordingGraphicsDevice(maxColor: 255)).Run();
        return printed.ToString();
    }

    [Fact]
    public void TextWrapsAtSpacesAndBreaksAWordTooLongForALine()
    {
        var printed = Run("""
            DIM L$(8), P(8)
            CALL UIBEGIN
            LET N = UIWRAP("one two three four abcdefghijklmnop", 60, 10, "MONO", L$, P)
            FOR I = 1 TO N
              PRINT L$(I); "|"; P(I)
            NEXT I
            PRINT UIWRAP("", 60, 10, "MONO", L$, P); "["; L$(1); "]"
            """);
        Assert.Equal("one two| 1 \nthree four| 9 \nabcdefghij| 20 \nklmnop| 30 \n 1 []\n", printed);
    }

    [Fact]
    public void TextIsCutToFitWithAnEllipsis()
    {
        var printed = Run("""
            CALL UIBEGIN
            PRINT UIFIT$("abcdefghij", 60, 10, "MONO")
            PRINT UIFIT$("abcdefghijkl", 60, 10, "MONO")
            PRINT UIFIT$("abcdefgh ijk", 60, 10, "MONO")
            PRINT UIWIDTH("abc", 10, "MONO")
            """);
        Assert.Equal("abcdefghij\nabcdefghi…\nabcdefgh…\n 18 \n", printed);   // (no space before the "…")
    }

    [Fact]
    public void TypingEditsAtTheCaret()
    {
        var printed = Run("""
            DECLARE PUBLIC UITYPED$, UICARET
            CALL UIEDITSTART("abc")
            LET R = UIEDITKEY(CHR$(0) & CHR$(75))     ! left
            LET R = UIEDITKEY("X")
            LET R = UIEDITKEY(CHR$(0) & CHR$(71))     ! Home
            LET R = UIEDITKEY(CHR$(0) & CHR$(83))     ! Delete
            PRINT UITYPED$; UICARET
            LET R = UIEDITKEY(CHR$(0) & CHR$(79))     ! End
            LET R = UIEDITKEY(CHR$(8))                ! Backspace
            PRINT UITYPED$; UICARET; UIEDITKEY(CHR$(13)); UIEDITKEY(CHR$(27))
            """);
        Assert.Equal("bXc 0 \nbX 2  1 -1 \n", printed);
    }

    private static string ExamplesDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "examples"))) dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir ?? throw new DirectoryNotFoundException("examples"), "examples");
    }
}
