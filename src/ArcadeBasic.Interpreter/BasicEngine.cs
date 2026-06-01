using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;

namespace ArcadeBasic;

/// <summary>
/// One-call embedding entry point for hosts (Unity, web playgrounds, custom
/// scripting consoles, etc.) that don't want to wire the Lexer → Parser →
/// Sema → Interpreter pipeline themselves.
///
/// <example>
/// <code>
/// using ArcadeBasic;
///
/// var sb = new System.Text.StringBuilder();
/// using var stdout = new System.IO.StringWriter(sb);
///
/// var result = BasicEngine.Run("PRINT 6 * 7", stdout);
///
/// Debug.Log(sb.ToString());        // " 42 "
/// Debug.Log(result.ExitCode);      // 0
/// Debug.Log(result.Diagnostics);   // empty list
/// </code>
/// </example>
/// </summary>
public static class BasicEngine
{
    /// <summary>
    /// Result of <see cref="Run(string, TextWriter, TextReader, string)"/>.
    /// </summary>
    /// <param name="ExitCode">0 on normal termination, 1 on parse/sema/runtime failure.</param>
    /// <param name="Diagnostics">Rendered text of every compile-time diagnostic (errors + warnings).</param>
    public sealed record class Result(int ExitCode, IReadOnlyList<string> Diagnostics);

    /// <summary>
    /// Lex / parse / analyze / run <paramref name="source"/> in one call.
    /// </summary>
    /// <param name="source">The Arcade BASIC program text.</param>
    /// <param name="stdout">Where PRINT / PRINT USING / MAT PRINT output goes. Cannot be null.</param>
    /// <param name="stdin">Optional source for INPUT / LINE INPUT. Defaults to <see cref="TextReader.Null"/>.</param>
    /// <param name="filename">Filename to attribute in diagnostics. Defaults to "&lt;embedded&gt;".</param>
    /// <param name="cancel">Cancellation token observed between BASIC statements. When triggered the program halts and the result reports <see cref="Result.ExitCode"/> = 2.</param>
    /// <returns>Exit code + collected diagnostic messages. ExitCode is 0 on success, 1 on parse/sema/runtime failure, 2 if the program was cancelled mid-flight.</returns>
    public static Result Run(string source, TextWriter stdout, TextReader? stdin = null, string filename = "<embedded>", CancellationToken cancel = default, ArcadeBasic.Runtime.IGraphicsDevice? graphics = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (stdout is null) throw new ArgumentNullException(nameof(stdout));

        var file = new SourceFile(filename, source);
        var diags = new DiagnosticBag();

        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();

        if (diags.HasErrors)
        {
            return new Result(1, RenderDiagnostics(diags));
        }

        var info = Analyzer.Analyze(program, diags);
        if (diags.HasErrors)
        {
            return new Result(1, RenderDiagnostics(diags));
        }

        var interp = new ArcadeBasic.Interpreter.BasicInterpreter(program, info, stdout, stdin ?? TextReader.Null, cancel, graphics);
        int exitCode;
        try
        {
            exitCode = interp.Run();
        }
        catch (OperationCanceledException)
        {
            return new Result(2, RenderDiagnostics(diags).Concat(new[] { "program cancelled" }).ToList());
        }
        catch (Exception ex)
        {
            return new Result(1, RenderDiagnostics(diags).Concat(new[] { $"runtime error: {ex.Message}" }).ToList());
        }

        return new Result(exitCode, RenderDiagnostics(diags));
    }

    /// <summary>
    /// Convenience overload that collects PRINT output into a string instead of a TextWriter.
    /// </summary>
    public static Result Run(string source, out string output, TextReader? stdin = null, string filename = "<embedded>", CancellationToken cancel = default)
    {
        using var sw = new StringWriter();
        var result = Run(source, sw, stdin, filename, cancel);
        output = sw.ToString();
        return result;
    }

    private static IReadOnlyList<string> RenderDiagnostics(DiagnosticBag diags) =>
        diags.All.Select(d => d.Render(useColor: false)).ToList();
}
