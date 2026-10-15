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
/// <see cref="BasicVm.Step"/>: the program pauses — instead of blocking the host —
/// on SLEEP, on INPUT with nothing queued, on foreground audio, and when the
/// instruction budget runs out, and carries on from exactly there.
/// </summary>
public class StepTests
{
    private static BasicVm Vm(string source, TextWriter output, TextReader? input = null,
        IGraphicsDevice? graphics = null, IAudioDevice? audio = null)
    {
        var file = new SourceFile("step.bas", source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        diags.HasErrors.Should().BeFalse(string.Join("\n", diags.All.Select(d => d.Render(false))));
        return new BasicVm(BasicCompiler.Compile(program, info), output, input ?? new StringReader(""), graphics, audio: audio);
    }

    private static StringWriter Output() => new() { NewLine = "\n" };

    [Fact]
    public void SleepPausesAfterFlushingTheFrame()
    {
        var output = Output();
        var gfx = new RecordingGraphicsDevice();
        var vm = Vm("SET WINDOW 0, 1, 0, 1\nGRAPH LINES: 0, 0; 1, 1\nSLEEP 0.25\nPRINT \"after\"\n", output, graphics: gfx);

        vm.Step(10_000).Should().Be(new StepResult(StepStatus.Sleeping, SleepSeconds: 0.25));
        gfx.Transcript.Should().EndWith("FLUSH\n");   // the host can show the frame while it waits
        output.ToString().Should().BeEmpty();

        vm.Step(10_000).Status.Should().Be(StepStatus.Halted);
        output.ToString().Should().Be("after\n");
    }

    [Fact]
    public void SleepZeroDoesNotPause()
    {
        var output = Output();
        var vm = Vm("PRINT \"a\"\nSLEEP 0\nPRINT \"b\"\n", output);
        vm.Step(10_000).Should().Be(new StepResult(StepStatus.Halted));
        output.ToString().Should().Be("a\nb\n");
    }

    [Fact]
    public void InputPausesUntilALineIsQueuedAndPromptsOnce()
    {
        var output = Output();
        var input = new LineInputQueue();
        var vm = Vm("INPUT \"Age\"; A\nPRINT A * 2\n", output, input);

        vm.Step(10_000).Status.Should().Be(StepStatus.NeedsInput);
        output.ToString().Should().Be("Age ");
        vm.Step(10_000).Status.Should().Be(StepStatus.NeedsInput);   // still waiting; no second prompt
        output.ToString().Should().Be("Age ");

        input.Provide("21");
        vm.Step(10_000).Should().Be(new StepResult(StepStatus.Halted));
        output.ToString().Should().Be("Age  42 \n");
    }

    [Fact]
    public void BadInputAsksAgainAcrossPauses()
    {
        var output = Output();
        var input = new LineInputQueue();
        var vm = Vm("INPUT A, B\nPRINT A + B\n", output, input);

        vm.Step(10_000).Status.Should().Be(StepStatus.NeedsInput);
        input.Provide("1");                                            // one value short
        vm.Step(10_000).Status.Should().Be(StepStatus.NeedsInput);
        input.Provide("x, 2");                                         // not numeric
        vm.Step(10_000).Status.Should().Be(StepStatus.NeedsInput);
        input.Provide("3, 4");
        vm.Step(10_000).Status.Should().Be(StepStatus.Halted);

        output.ToString().Should().Be(
            "? Not enough data — redo from start.\n" +
            "? 'x' is not numeric — redo from start.\n" +
            "?  7 \n");
    }

    [Fact]
    public void MatInputKeepsItsValuesAcrossPauses()
    {
        var output = Output();
        var input = new LineInputQueue();
        var vm = Vm("OPTION BASE 1\nDIM A(3)\nMAT INPUT A\nLINE INPUT L$\nPRINT A(1) + A(2) + A(3); L$\n", output, input);

        vm.Step(10_000).Status.Should().Be(StepStatus.NeedsInput);
        input.Provide("1, 2");                                         // two of three
        vm.Step(10_000).Status.Should().Be(StepStatus.NeedsInput);
        input.Provide("3");
        vm.Step(10_000).Status.Should().Be(StepStatus.NeedsInput);     // now LINE INPUT
        input.Provide("a, b");
        vm.Step(10_000).Status.Should().Be(StepStatus.Halted);

        output.ToString().Should().Be("? ? ?  6 a, b\n");
    }

    [Fact]
    public void EndOfQueuedInputRaisesTheInputException()
    {
        var input = new LineInputQueue();
        input.Complete();
        var output = Output();
        var vm = Vm("WHEN EXCEPTION IN\nINPUT A\nUSE\nPRINT EXTYPE\nEND WHEN\n", output, input);
        vm.Step(10_000).Should().Be(new StepResult(StepStatus.Halted));
        output.ToString().Should().Be("?  4003 \n");

        var unhandled = Vm("LINE INPUT L$\n", Output(), input);
        var result = unhandled.Step(10_000);
        result.Status.Should().Be(StepStatus.Halted);
        result.ExitCode.Should().Be(1);
        result.Error.Should().Be("unhandled exception type 4003 at line 1: LINE INPUT: end of input stream");
    }

    [Fact]
    public void ForegroundAudioPausesAfterEmittingAndBackgroundDoesNot()
    {
        var output = Output();
        var audio = new RecordingAudioDevice();
        var vm = Vm("SOUND 440, 9.1\nPRINT \"a\"\nPLAY \"MB C\"\nPRINT \"b\"\n", output, audio: audio);

        vm.Step(10_000).Status.Should().Be(StepStatus.AwaitingAudio);
        audio.Transcript.Should().Be("TONE 440.0000 0.5000 0.0000\n");   // queued before the pause
        output.ToString().Should().BeEmpty();

        vm.Step(10_000).Status.Should().Be(StepStatus.Halted);         // MB: no wait
        output.ToString().Should().Be("a\nb\n");
    }

    [Fact]
    public void TheBudgetBoundsEveryStepEvenThroughExceptions()
    {
        // An endless loop — and one that raises and handles an exception every pass,
        // which unwinds through the dispatcher — must still hand control back.
        foreach (var src in new[]
                 {
                     "DO\nLOOP\n",
                     "DO\nWHEN EXCEPTION IN\nCAUSE EXCEPTION 1\nUSE\nEND WHEN\nLOOP\n",
                 })
        {
            var vm = Vm(src, Output());
            for (var i = 0; i < 100; i++)
                vm.Step(1_000).Status.Should().Be(StepStatus.Running);
        }
    }

    [Fact]
    public void AHaltedProgramStaysHalted()
    {
        var vm = Vm("CAUSE EXCEPTION 42\n", Output());
        var first = vm.Step(10_000);
        first.Should().Be(new StepResult(StepStatus.Halted, ExitCode: 1,
            Error: "unhandled exception type 42 at line 1: user-raised exception 42"));
        vm.Step(10_000).Should().Be(first);
        vm.Run().Should().Be(1);
    }

    [Fact]
    public void InstructionsAreCountedTheSameHoweverTheyAreSliced()
    {
        const string src = "FOR I = 1 TO 50\nLET X = X + I\nNEXT I\nSLEEP 0.1\nPRINT X\n";
        var whole = Vm(src, Output());
        while (whole.Step(1_000_000).Status != StepStatus.Halted) { }

        var sliced = Vm(src, Output());
        var steps = 0;
        while (sliced.Step(1).Status != StepStatus.Halted) steps++;

        whole.InstructionsExecuted.Should().BeGreaterThan(50);
        sliced.InstructionsExecuted.Should().Be(whole.InstructionsExecuted);
        steps.Should().Be((int)whole.InstructionsExecuted - 1);   // the last instruction is the one that halts
    }

    /// <summary>A surface the test clicks on by hand.</summary>
    private sealed class Clickable : IGraphicsDevice, IPointer
    {
        public readonly Queue<PointerEvent> Events = new();
        public int Flushes;
        public bool TryGetPosition(out GfxPoint p) { p = default; return false; }
        public bool TryReadEvent(out PointerEvent e) => Events.TryDequeue(out e);
        public void Clear() { }
        public void SetLineStyle(int style) { }
        public void SetPointStyle(int style) { }
        public void SetColor(GfxColorTarget target, int colorIndex) { }
        public void DrawPoints(IReadOnlyList<GfxPoint> points) { }
        public void DrawLines(IReadOnlyList<GfxPoint> polyline) { }
        public void FillArea(IReadOnlyList<GfxPoint> polygon) { }
        public void DrawText(GfxPoint at, string text) { }
        public void Flush() => Flushes++;
        public int MaxColor => 15;
        public int MaxLineStyle => 3;
        public int MaxPointStyle => 3;
        public GfxDeviceSize DeviceSize => new(1, 1, "OTHER");
    }

    [Fact]
    public void LocatePointPausesUntilThereIsAClick()
    {
        var output = Output();
        var screen = new Clickable();
        var vm = Vm("SET WINDOW 0, 10, 0, 10\nLOCATE POINT : X, Y\nPRINT X; Y\n", output, graphics: screen);

        vm.Step(10_000).Status.Should().Be(StepStatus.NeedsPoint);
        screen.Flushes.Should().BeGreaterThan(0);                  // the picture is up while it waits
        vm.Step(10_000).Status.Should().Be(StepStatus.NeedsPoint);

        screen.Events.Enqueue(new PointerEvent(true, new GfxPoint(0.3, 0.7)));
        vm.Step(10_000).Status.Should().Be(StepStatus.Halted);
        output.ToString().Should().Be(" 3  7 \n");
    }

    [Fact]
    public void StepNeedsAPositiveBudget()
    {
        var vm = Vm("PRINT 1\n", Output());
        vm.Invoking(v => v.Step(0)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RunStopsWhenCancelled()
    {
        var vm = Vm("DO\nLOOP\n", Output());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        vm.Invoking(v => v.Run(cts.Token)).Should().Throw<OperationCanceledException>();
        vm.Step(10_000).Should().Be(new StepResult(StepStatus.Halted, ExitCode: 2, Error: "program cancelled"));
    }

    [Fact]
    public async Task RunWaitsForAQueuedLine()
    {
        // Run() over a non-blocking reader polls until the host (here, another
        // thread) queues the line.
        var output = Output();
        var input = new LineInputQueue();
        var vm = Vm("LINE INPUT L$\nPRINT \"got \"; L$\n", output, input);

        var run = Task.Run(() => vm.Run());
        await Task.Delay(30);
        input.Provide("hello");

        (await run.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(0);
        output.ToString().Should().Be("? got hello\n");
    }
}
