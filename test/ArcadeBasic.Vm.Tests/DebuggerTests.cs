using FluentAssertions;
using ArcadeBasic.Compiler;
using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Runtime;
using ArcadeBasic.Sema;
using ArcadeBasic.Vm;

namespace ArcadeBasic.Vm.Tests;

/// <summary>
/// The VM's debugging hooks: <see cref="BasicVm.Breakpoints"/>, <see cref="BasicVm.PauseAt"/>
/// and <see cref="BasicVm.CallStack"/> — a stepped host stops before a statement
/// (<see cref="StepStatus.Paused"/>), looks at the variables, and carries on.
/// </summary>
public class DebuggerTests
{
    private static (BasicVm Vm, StringWriter Output) Vm(string source)
    {
        var file = new SourceFile("debug.bas", source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        diags.HasErrors.Should().BeFalse(string.Join("\n", diags.All.Select(d => d.Render(false))));
        var output = new StringWriter { NewLine = "\n" };
        return (new BasicVm(BasicCompiler.Compile(program, info), output, new StringReader("")), output);
    }

    /// <summary>Step until it pauses or ends; the line it paused at (0 if it ended).</summary>
    private static int Next(BasicVm vm)
    {
        for (var i = 0; i < 100; i++)
        {
            var r = vm.Step(10_000);
            if (r.Status == StepStatus.Paused) return vm.CurrentLine;
            if (r.Status == StepStatus.Halted) return 0;
        }
        throw new InvalidOperationException("never paused or ended");
    }

    private static string Value(DebugFrame frame, int slot) => frame.Variables.Get(slot) switch
    {
        NumericValue n => n.V.ToString(),
        StringValue s => s.V,
        var v => v.ToString(),
    };

    [Fact]
    public void ABreakpointStopsBeforeItsLineEveryTimeAround()
    {
        var (vm, output) = Vm("FOR I = 1 TO 3\n  PRINT I;\nNEXT I\nPRINT \"done\"\n");
        vm.Breakpoints.Add(2);

        Next(vm).Should().Be(2);
        output.ToString().Should().BeEmpty();          // before the line, not after
        Next(vm).Should().Be(2);
        Next(vm).Should().Be(2);
        output.ToString().Should().Be(" 1  2 ");
        Next(vm).Should().Be(0);
        output.ToString().Should().Be(" 1  2  3 done\n");
    }

    [Fact]
    public void ALineIsOneStopEvenWithStatementsInsideIt()
    {
        // The single-line IF and the PRINT inside it are two statements on one line.
        var (vm, output) = Vm("FOR I = 1 TO 2\nIF I > 0 THEN PRINT \"x\";\nNEXT I\n");
        vm.Breakpoints.Add(2);
        Next(vm).Should().Be(2);
        Next(vm).Should().Be(2);
        output.ToString().Should().Be("x");
        Next(vm).Should().Be(0);
    }

    [Fact]
    public void StepOverRunsACallAndIntoGoesInsideIt()
    {
        const string source = "LET A = 1\nCALL TWICE(A)\nPRINT A\nEND\nSUB TWICE(X)\n  LET X = X * 2\nEND SUB\n";

        var (over, _) = Vm(source);
        over.Breakpoints.Add(2);
        Next(over).Should().Be(2);
        over.PauseAt(DebugStep.Over);
        Next(over).Should().Be(3);                      // the SUB ran without stopping

        var (into, _) = Vm(source);
        into.Breakpoints.Add(2);
        Next(into).Should().Be(2);
        into.PauseAt(DebugStep.Into);
        Next(into).Should().Be(6);                      // inside TWICE
        var stack = into.CallStack();
        stack.Select(f => (f.Name, f.Line)).Should().Equal(("SUB TWICE", 6), ("", 2));
        Value(stack[0], 0).Should().Be("1");            // X, its first slot

        into.PauseAt(DebugStep.Out);
        Next(into).Should().Be(3);                      // back in the main program
        into.CallStack().Should().ContainSingle();
    }

    [Fact]
    public void StepOverRunsAGosubAndStepOutLeavesOne()
    {
        const string source = "GOSUB 100\nPRINT \"back\"\nEND\n100 PRINT \"in\"\nPRINT \"still in\"\nRETURN\n";

        var (over, output) = Vm(source);
        over.PauseAt(DebugStep.Pause);
        Next(over).Should().Be(1);                      // the first statement
        over.PauseAt(DebugStep.Over);
        Next(over).Should().Be(2);
        output.ToString().Should().Be("in\nstill in\n");

        var (outOf, _) = Vm(source);
        outOf.Breakpoints.Add(4);
        Next(outOf).Should().Be(4);
        outOf.PauseAt(DebugStep.Out);
        Next(outOf).Should().Be(2);
    }

    [Fact]
    public void PauseStopsARunningProgramAtItsNextStatement()
    {
        var (vm, _) = Vm("LET N = 0\nDO\n  LET N = N + 1\nLOOP\n");
        vm.Step(5_000).Status.Should().Be(StepStatus.Running);   // an endless loop
        vm.PauseAt(DebugStep.Pause);
        Next(vm).Should().BeOneOf(2, 3, 4);
        var n = Value(vm.CallStack()[0], 0);
        vm.Step(5_000);                                  // carries on from there
        Value(vm.CallStack()[0], 0).Should().NotBe(n);
    }

    [Fact]
    public void TheCompilerRecordsEachStatementsLine()
    {
        var file = new SourceFile("lines.bas", "! a comment\nLET A = 1\n\nPRINT A\nSUB S\n  PRINT 2\nEND SUB\n");
        var diags = new DiagnosticBag();
        var program = new BasicParser(new BasicLexer(file, diags).Lex(), file, diags).ParseProgram();
        var compiled = BasicCompiler.Compile(program, Analyzer.Analyze(program, diags));
        compiled.Main.StatementLines.Should().Equal(2, 4, 5);    // (the main program passes the SUB's header)
        compiled.Subs[0].Body.StatementLines.Should().Equal(6);
    }
}
