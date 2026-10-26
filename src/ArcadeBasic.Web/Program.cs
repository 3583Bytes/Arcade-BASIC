using System.Runtime.InteropServices.JavaScript;
using ArcadeBasic.Compiler;
using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Runtime;
using ArcadeBasic.Sema;
using ArcadeBasic.Vm;
using ArcadeBasic.Web;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("browser")]

// The page (wwwroot/main.js) owns the loop: every animation frame it calls Step,
// waits out SLEEPs, shows output, and draws the newest frame. Everything runs on
// the browser's one thread — nothing here blocks.
Console.WriteLine("Arcade BASIC playground loaded");

internal static partial class Playground
{
    private const string ExamplePrefix = "Examples.";

    private static readonly CanvasGraphicsDevice Screen = new();
    private static readonly BrowserKeyboard Keyboard = new();
    private static readonly BrowserAudio Audio = new();
    private static BasicVm? s_vm;
    private static StringWriter s_output = new() { NewLine = "\n" };
    private static LineInputQueue s_input = new();
    private static StepResult s_last;

    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.String>>]
    internal static string[] Examples() =>
        [.. typeof(Playground).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ExamplePrefix, StringComparison.Ordinal))
            .Select(n => n[ExamplePrefix.Length..])
            .Order(StringComparer.Ordinal)];

    [JSExport]
    internal static string ExampleSource(string name)
    {
        using var stream = typeof(Playground).Assembly.GetManifestResourceStream(ExamplePrefix + name);
        if (stream is null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Compile <paramref name="source"/> and get it ready to step: "" on
    /// success, otherwise the diagnostics to show.</summary>
    [JSExport]
    internal static string Load(string source)
    {
        s_vm = null;
        var file = new SourceFile("program.bas", source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        var info = diags.HasErrors ? null : Analyzer.Analyze(program, diags);
        if (info is null || diags.HasErrors)
            return string.Join("\n", diags.All.Select(d => d.Render(useColor: false)));

        ArcadeBasic.Bytecode.Program compiled;
        try
        {
            compiled = BasicCompiler.Compile(program, info);
        }
        catch (BasicCompiler.UnsupportedFeatureException ex)
        {
            return ex.Message;
        }

        s_output = new StringWriter { NewLine = "\n" };
        s_input = new LineInputQueue();
        Screen.Reset();
        Keyboard.Clear();
        Audio.Clear();
        s_last = default;
        s_vm = new BasicVm(compiled, s_output, s_input, Screen, Keyboard, Audio);
        RememberScopes(compiled, info);
        return "";
    }

    private static string[] s_checkMessages = [];

    /// <summary>Check <paramref name="source"/> without running it, for the editor's error
    /// marks: (from, to, severity) per problem, as character offsets, severity 0 a note,
    /// 1 a warning, 2 an error. <see cref="CheckMessages"/> has their texts.</summary>
    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    internal static double[] Check(string source)
    {
        var file = new SourceFile("program.bas", source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        var info = diags.HasErrors ? null : Analyzer.Analyze(program, diags);
        var problems = new List<double>();
        var messages = new List<string>();
        foreach (var d in diags.All)
        {
            problems.Add(d.Span.Start);
            problems.Add(d.Span.End);
            problems.Add((int)d.Severity);
            messages.Add(d.Hint is null ? $"{d.Message} [{d.Code}]" : $"{d.Message} [{d.Code}]\n{d.Hint}");
        }
        if (info is not null && !diags.HasErrors)
        {
            try
            {
                RememberStatementLines(BasicCompiler.Compile(program, info));
            }
            catch (BasicCompiler.UnsupportedFeatureException ex)
            {
                problems.AddRange([0, 0, (int)DiagnosticSeverity.Error]);
                messages.Add(ex.Message);
            }
        }
        s_checkMessages = [.. messages];
        return [.. problems];
    }

    /// <summary>The texts of the problems the last <see cref="Check"/> found.</summary>
    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.String>>]
    internal static string[] CheckMessages() => s_checkMessages;

    /// <summary>The words the editor colours and completes: the reserved words, then
    /// (after an empty string) the supplied functions and constants.</summary>
    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.String>>]
    internal static string[] Words() => [.. BasicLexer.ReservedWords, "", .. Analyzer.BuiltinNames];

    /// <summary>Run up to <paramref name="budget"/> instructions; returns the <see cref="StepStatus"/>.</summary>
    [JSExport]
    internal static int Step(int budget)
    {
        if (s_vm is null) return (int)StepStatus.Halted;
        s_last = s_vm.Step(budget);
        if (s_last.Status == StepStatus.Halted) Screen.Flush();   // show the final picture, SLEEP or not
        return (int)s_last.Status;
    }

    /// <summary>Stop the program: further steps report it halted.</summary>
    [JSExport]
    internal static void Stop() => s_vm = null;

    [JSExport]
    internal static double SleepSeconds() => s_last.SleepSeconds;

    [JSExport]
    internal static string Error() => s_last.Error ?? "";

    /// <summary>The source line the program is on — after an <see cref="Error"/>, the line that raised it.</summary>
    [JSExport]
    internal static int CurrentLine() => s_vm?.CurrentLine ?? 0;

    [JSExport]
    internal static double Instructions() => s_vm?.InstructionsExecuted ?? 0;

    /// <summary>Everything printed since the last call.</summary>
    [JSExport]
    internal static string TakeOutput()
    {
        var text = s_output.ToString();
        s_output.GetStringBuilder().Clear();
        return text;
    }

    [JSExport]
    internal static void ProvideInput(string line) => s_input.Provide(line);

    /// <summary>The tones played since the last call (see <see cref="BrowserAudio.Take"/>).</summary>
    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    internal static double[] TakeTones() => Audio.Take();

    /// <summary>How many tones the page is still playing, for GW-BASIC's background-music limit.</summary>
    [JSExport]
    internal static void SetTonesPlaying(int count) => Audio.SetPlaying(count);

    /// <summary>After <see cref="StepStatus.AwaitingAudio"/>: resume once no more than this many
    /// tones are left to play (0: all of them).</summary>
    [JSExport]
    internal static int MaxQueuedTones() => s_last.MaxQueuedTones;

    /// <summary>The newest complete frame's commands (see <see cref="CanvasGraphicsDevice"/>), or empty if unchanged.</summary>
    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    internal static double[] TakeFrame() => Screen.TakeFrame();

    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.String>>]
    internal static string[] FrameText() => Screen.FrameText;

    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    internal static double[] FrameCells() => Screen.FrameCells;

    [JSExport]
    internal static void KeyDown(string code, string key, bool repeat) => Keyboard.Down(code, key, repeat);

    [JSExport]
    internal static void KeyUp(string code) => Keyboard.Up(code);

    [JSExport]
    internal static void ReleaseKeys() => Keyboard.ReleaseAll();

    // Pointer positions are in the canvas's unit square, origin bottom-left.
    [JSExport]
    internal static void PointerMove(double x, double y) => Screen.PointerMove(x, y);

    [JSExport]
    internal static void PointerDown(double x, double y) => Screen.PointerDown(x, y);

    [JSExport]
    internal static void PointerUp(double x, double y) => Screen.PointerUp(x, y);

    [JSExport]
    internal static void PointerCancel() => Screen.PointerCancel();
}
