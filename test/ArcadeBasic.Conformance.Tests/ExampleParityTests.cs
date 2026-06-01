using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;
using ArcadeBasic.Interpreter;
using ArcadeBasic.Compiler;
using ArcadeBasic.Vm;
using AstProgram = ArcadeBasic.Parser.Ast.Program;

namespace ArcadeBasic.Conformance.Tests;

/// <summary>
/// Engine-parity coverage for the bundled example programs. The whole design
/// rests on the tree-walking interpreter and the bytecode VM producing
/// byte-identical output, so this fixture guards that invariant end-to-end on
/// real programs (not just unit snippets).
///
/// Two tiers:
///   * <see cref="InterpreterAndVmAgree"/> — for examples that run to completion
///     deterministically (given the supplied stdin), assert identical output and
///     exit code from both engines.
///   * <see cref="EveryExampleCompilesOnBothEngines"/> — for *every* example,
///     assert it analyzes cleanly and the VM compiler accepts it. This catches
///     "feature works in the interpreter but the VM compiler chokes" gaps even
///     for input-driven or RND-seeded programs we can't compare by output.
/// </summary>
public class ExampleParityTests
{
    /// <summary>(file, stdin) for examples that terminate deterministically.</summary>
    public static IEnumerable<object[]> DeterministicExamples() =>
    [
        ["hello.bas", ""],
        ["factorial.bas", ""],
        ["fibonacci.bas", ""],
        ["primes.bas", ""],
        ["strings.bas", ""],
        ["matrix.bas", ""],
        ["exception.bas", ""],
        ["formatted.bas", ""],
        ["modules.bas", ""],
        ["pi.bas", ""],
        ["guess.bas", "7\n"],          // TARGET is hard-coded to 7 — no RND
    ];

    public static IEnumerable<object[]> AllExamples() =>
        Directory.GetFiles(ExamplesDir(), "*.bas")
            .OrderBy(f => f)
            .Select(f => new object[] { Path.GetFileName(f) });

    [Theory]
    [MemberData(nameof(DeterministicExamples))]
    public void InterpreterAndVmAgree(string name, string stdin)
    {
        var (program, info) = FrontEnd(name);

        var (interpOut, interpExit) = RunInterpreter(program, info, stdin);
        var (vmOut, vmExit) = RunVm(program, info, stdin);

        Assert.Equal(interpOut, vmOut);
        Assert.Equal(interpExit, vmExit);
    }

    [Theory]
    [MemberData(nameof(AllExamples))]
    public void EveryExampleCompilesOnBothEngines(string name)
    {
        // The interpreter accepts everything by construction; the gap we guard
        // against is the VM compiler rejecting a feature (e.g. a statement it
        // hasn't learned to lower yet).
        var (program, info) = FrontEnd(name);
        var ex = Record.Exception(() => BasicCompiler.Compile(program, info));
        Assert.Null(ex);
    }

    // -- helpers ---------------------------------------------------------

    private static (AstProgram Program, SemanticInfo Info) FrontEnd(string name)
    {
        var source = File.ReadAllText(Path.Combine(ExamplesDir(), name));
        var file = new SourceFile(name, source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        Assert.False(diags.HasErrors,
            $"{name} produced analysis diagnostics:\n{string.Join("\n", diags.All.Select(d => d.Render(false)))}");
        return (program, info);
    }

    private static (string Output, int Exit) RunInterpreter(AstProgram program, SemanticInfo info, string stdin)
    {
        var sw = new StringWriter { NewLine = "\n" };
        var exit = new BasicInterpreter(program, info, sw, new StringReader(stdin)).Run();
        return (sw.ToString(), exit);
    }

    private static (string Output, int Exit) RunVm(AstProgram program, SemanticInfo info, string stdin)
    {
        var compiled = BasicCompiler.Compile(program, info);
        var sw = new StringWriter { NewLine = "\n" };
        var exit = new BasicVm(compiled, sw, new StringReader(stdin)).Run();
        return (sw.ToString(), exit);
    }

    private static string ExamplesDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "examples");
            if (File.Exists(Path.Combine(candidate, "hello.bas"))) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("could not locate the examples/ directory above the test binary");
    }
}
