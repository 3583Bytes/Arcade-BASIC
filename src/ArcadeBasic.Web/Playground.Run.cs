using System.Runtime.InteropServices.JavaScript;
using ArcadeBasic.Runtime;
using ArcadeBasic.Vm;
using ArcadeBasic.Web;
using BcProgram = ArcadeBasic.Bytecode.Program;

// Running a program, for the IDE and the player alike. The page owns the loop: every
// animation frame it calls Step, waits out SLEEPs, shows output, and draws the newest
// frame (wwwroot/runner.js, screen.js). Everything runs on the browser's one thread —
// nothing here blocks.
internal static partial class Playground
{
    private static readonly CanvasGraphicsDevice Screen = new();
    private static readonly BrowserKeyboard Keyboard = new();
    private static readonly BrowserAudio Audio = new();
    private static BasicVm? s_vm;
    private static StringWriter s_output = new() { NewLine = "\n" };
    private static LineInputQueue s_input = new();
    private static StepResult s_last;

    /// <summary>Get <paramref name="compiled"/> ready to step, on fresh devices.</summary>
    private static void Begin(BcProgram compiled)
    {
        s_output = new StringWriter { NewLine = "\n" };
        s_input = new LineInputQueue();
        Screen.Reset();
        Keyboard.Clear();
        Audio.Clear();
        s_last = default;
        s_vm = new BasicVm(compiled, s_output, s_input, Screen, Keyboard, Audio);
    }

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
