using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ArcadeBasic.Compiler;
using ArcadeBasic.Core;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;
using ArcadeBasic.Vm;

namespace ArcadeBasic;

/// <summary>
/// One-call embedding entry point for hosts (games, tools, custom
/// scripting consoles, etc.) that don't want to wire the Lexer → Parser →
/// Sema → Compiler → VM pipeline themselves. For a host with its own frame loop (a
/// game), compile with <see cref="BasicCompiler"/> and call <see cref="BasicVm.Step"/>
/// each frame instead: this runs the program to the end before it returns.
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
/// Console.WriteLine(sb.ToString());        // " 42 "
/// Console.WriteLine(result.ExitCode);      // 0
/// Console.WriteLine(result.Diagnostics);   // empty list
/// </code>
/// </example>
/// </summary>
public static class BasicEngine
{
    /// <summary>
    /// Result of <see cref="Run(string, TextWriter, TextReader, string)"/>.
    /// </summary>
    /// <param name="ExitCode">0 on normal termination, 1 on parse/sema/runtime failure.</param>
    /// <param name="Diagnostics">Rendered text of every compile-time diagnostic (errors + warnings), and
    /// of the exception that stopped the program, if one did.</param>
    public sealed record class Result(int ExitCode, IReadOnlyList<string> Diagnostics);

    /// <summary>
    /// Lex / parse / analyze / run <paramref name="source"/> in one call.
    /// </summary>
    /// <param name="source">The Arcade BASIC program text.</param>
    /// <param name="stdout">Where PRINT / PRINT USING / MAT PRINT output goes. Cannot be null.</param>
    /// <param name="stdin">Optional source for INPUT / LINE INPUT. Defaults to <see cref="TextReader.Null"/>.</param>
    /// <param name="filename">Filename to attribute in diagnostics. Defaults to "&lt;embedded&gt;".</param>
    /// <param name="cancel">Cancellation token, checked every 100,000 instructions and during SLEEP (a blocking INPUT can't be interrupted). When triggered the program halts and the result reports <see cref="Result.ExitCode"/> = 2.</param>
    /// <param name="libraries">For LIBRARY "name": the named file's source, or null if there's no such file.</param>
    /// <returns>Exit code + collected diagnostic messages. ExitCode is 0 on success, 1 on parse/sema/runtime failure, 2 if the program was cancelled mid-flight.</returns>
    public static Result Run(string source, TextWriter stdout, TextReader? stdin = null, string filename = "<embedded>", CancellationToken cancel = default, ArcadeBasic.Runtime.IGraphicsDevice? graphics = null, ArcadeBasic.Runtime.IKeyboard? keyboard = null, ArcadeBasic.Runtime.IAudioDevice? audio = null, Func<string, string?>? libraries = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (stdout is null) throw new ArgumentNullException(nameof(stdout));

        var file = new SourceFile(filename, source);
        var diags = new DiagnosticBag();

        var program = ProgramLoader.Load(file,
            libraries is null ? null : (name, _) => libraries(name) is { } text ? new SourceFile(name, text) : null, diags);

        if (diags.HasErrors)
        {
            return new Result(1, RenderDiagnostics(diags));
        }

        var info = Analyzer.Analyze(program, diags);
        if (diags.HasErrors)
        {
            return new Result(1, RenderDiagnostics(diags));
        }

        ArcadeBasic.Bytecode.Program compiled;
        try
        {
            compiled = BasicCompiler.Compile(program, info);
        }
        catch (BasicCompiler.UnsupportedFeatureException ex)
        {
            return new Result(1, RenderDiagnostics(diags).Concat(new[] { $"compile error: {ex.Message}" }).ToList());
        }

        var errors = new StringWriter();
        var vm = new BasicVm(compiled, stdout, stdin ?? TextReader.Null, graphics, keyboard, audio)
        {
            Errors = errors,
        };
        int exitCode;
        try
        {
            exitCode = vm.Run(cancel);
        }
        catch (OperationCanceledException)
        {
            return new Result(2, RenderDiagnostics(diags).Concat(new[] { "program cancelled" }).ToList());
        }
        catch (Exception ex)
        {
            return new Result(1, RenderDiagnostics(diags).Concat(new[] { $"runtime error: {ex.Message}" }).ToList());
        }

        var stopped = errors.ToString().Split('\n').Select(line => line.TrimEnd()).Where(line => line.Length > 0);
        return new Result(exitCode, RenderDiagnostics(diags).Concat(stopped).ToList());
    }

    /// <summary>
    /// Convenience overload that collects PRINT output into a string instead of a TextWriter.
    /// </summary>
    public static Result Run(string source, out string output, TextReader? stdin = null, string filename = "<embedded>", CancellationToken cancel = default, Func<string, string?>? libraries = null)
    {
        using var sw = new StringWriter();
        var result = Run(source, sw, stdin, filename, cancel, libraries: libraries);
        output = sw.ToString();
        return result;
    }

    private static IReadOnlyList<string> RenderDiagnostics(DiagnosticBag diags) =>
        diags.All.Select(d => d.Render(useColor: false)).ToList();
}
