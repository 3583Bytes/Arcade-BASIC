using ArcadeBasic.Compiler;
using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;

namespace ArcadeBasic.Tui;

/// <summary>
/// Validate-only compile: lex → parse → sema → bytecode-emit, no execution.
/// Used by the Compile menu item to give the user a fast syntax/type check
/// without committing to a full run.
/// </summary>
internal static class CompileService
{
    public sealed record Result(bool Ok, IReadOnlyList<string> Diagnostics);

    public static Result Validate(string source)
    {
        var file = new SourceFile("<editor>", source);
        var diags = new DiagnosticBag();

        var tokens = new BasicLexer(file, diags).Lex();
        if (diags.HasErrors) return new Result(false, Render(diags));

        var program = new BasicParser(tokens, file, diags).ParseProgram();
        if (diags.HasErrors) return new Result(false, Render(diags));

        var info = Analyzer.Analyze(program, diags);
        if (diags.HasErrors) return new Result(false, Render(diags));

        try
        {
            BasicCompiler.Compile(program, info);
        }
        catch (BasicCompiler.UnsupportedFeatureException ex)
        {
            return new Result(false, Render(diags).Concat(new[] { "compile error: " + ex.Message }).ToList());
        }
        catch (Exception ex)
        {
            return new Result(false, Render(diags).Concat(new[] { "compile error: " + ex.Message }).ToList());
        }

        return new Result(true, Render(diags));
    }

    private static IReadOnlyList<string> Render(DiagnosticBag diags) =>
        diags.All.Select(d => d.Render(useColor: false)).ToList();
}
