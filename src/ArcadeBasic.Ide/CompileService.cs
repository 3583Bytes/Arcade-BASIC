using ArcadeBasic.Bytecode;
using ArcadeBasic.Compiler;
using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;

namespace ArcadeBasic.Ide;

/// <summary>
/// Compile the editor source through lex → parse → sema → bytecode-emit, with
/// no execution. <see cref="Validate"/> is used by the Compile menu item for
/// a fast syntax/type check; <see cref="Compile"/> returns the bytecode
/// program too so the Build menu can write it to a standalone binary.
/// </summary>
internal static class CompileService
{
    public sealed record Result(bool Ok, IReadOnlyList<string> Diagnostics, ArcadeBasic.Bytecode.Program? Program);

    public static Result Validate(string source) => Compile(source);

    public static Result Compile(string source)
    {
        var file = new SourceFile("<editor>", source);
        var diags = new DiagnosticBag();

        var tokens = new BasicLexer(file, diags).Lex();
        if (diags.HasErrors) return new Result(false, Render(diags), null);

        var program = new BasicParser(tokens, file, diags).ParseProgram();
        if (diags.HasErrors) return new Result(false, Render(diags), null);

        var info = Analyzer.Analyze(program, diags);
        if (diags.HasErrors) return new Result(false, Render(diags), null);

        ArcadeBasic.Bytecode.Program compiled;
        try
        {
            compiled = BasicCompiler.Compile(program, info);
        }
        catch (BasicCompiler.UnsupportedFeatureException ex)
        {
            return new Result(false, Render(diags).Concat(new[] { "compile error: " + ex.Message }).ToList(), null);
        }
        catch (Exception ex)
        {
            return new Result(false, Render(diags).Concat(new[] { "compile error: " + ex.Message }).ToList(), null);
        }

        return new Result(true, Render(diags), compiled);
    }

    private static IReadOnlyList<string> Render(DiagnosticBag diags) =>
        diags.All.Select(d => d.Render(useColor: false)).ToList();
}
