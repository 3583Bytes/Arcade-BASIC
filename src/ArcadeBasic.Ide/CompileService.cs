using ArcadeBasic.Bytecode;
using ArcadeBasic.Compiler;
using ArcadeBasic.Core;
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

    public static Result Validate(string source, string? folder = null) => Compile(source, folder);

    /// <param name="folder">Where LIBRARY files are (the open file's folder), if anywhere.</param>
    public static Result Compile(string source, string? folder = null)
    {
        var diags = new DiagnosticBag();
        var libraries = Libraries(folder);
        var program = ProgramLoader.Load(new SourceFile("<editor>", source),
            (name, _) => libraries(name) is { } text ? new SourceFile(name, text) : null, diags);
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

    /// <summary>LIBRARY "name": the file next to the open one — or, for a bundled example,
    /// the bundled library.</summary>
    public static Func<string, string?> Libraries(string? folder) => name =>
    {
        var path = Path.Combine(folder ?? Directory.GetCurrentDirectory(), name);
        return File.Exists(path) ? File.ReadAllText(path) : ExamplesProvider.Source(name);
    };

    private static IReadOnlyList<string> Render(DiagnosticBag diags) =>
        diags.All.Select(d => d.Render(useColor: false)).ToList();
}
