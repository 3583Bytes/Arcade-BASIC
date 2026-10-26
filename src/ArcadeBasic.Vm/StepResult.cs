namespace ArcadeBasic.Vm;

/// <summary>Why <see cref="BasicVm.Step"/> returned control to the host.</summary>
public enum StepStatus
{
    /// <summary>The instruction budget ran out. Call <see cref="BasicVm.Step"/> again.</summary>
    Running,

    /// <summary><c>SLEEP</c>: what the program drew has been flushed to the graphics
    /// device; resume after <see cref="StepResult.SleepSeconds"/>.</summary>
    Sleeping,

    /// <summary><c>INPUT</c>, <c>LINE INPUT</c> or <c>MAT INPUT</c> is waiting for a
    /// line its non-blocking reader doesn't have yet (the prompt is already printed).
    /// Queue one, then resume. Only a reader implementing
    /// <see cref="Runtime.INonBlockingLineReader"/> pauses; a plain
    /// <see cref="TextReader"/> blocks inside <see cref="BasicVm.Step"/> instead.</summary>
    NeedsInput,

    /// <summary>A foreground <c>SOUND</c>, <c>BEEP</c> or <c>PLAY</c> wants its audio to
    /// finish before the program continues — or background music has more tones queued
    /// than GW-BASIC's buffer holds (an <see cref="Runtime.IAudioQueue"/> device). Resume
    /// once no more than <see cref="StepResult.MaxQueuedTones"/> are left to play
    /// (<see cref="BasicVm.Run()"/> calls the device's <c>Flush()</c>).</summary>
    AwaitingAudio,

    /// <summary>The program has ended — normally, by an unhandled exception
    /// (<see cref="StepResult.Error"/>), or by cancellation. Further steps return the
    /// same result.</summary>
    Halted,

    /// <summary><c>LOCATE POINT</c> / <c>GET POINT</c> is waiting for the user to pick a point
    /// on the graphics device's pointer (<see cref="Runtime.IPointer"/>); the frame being
    /// pointed at has been flushed. Resume once the device has a button press.</summary>
    NeedsPoint,

    /// <summary>Stopped before a statement for a debugger: a breakpoint
    /// (<see cref="BasicVm.Breakpoints"/>) or a <see cref="BasicVm.PauseAt"/>.
    /// <see cref="BasicVm.CurrentLine"/> is its line and <see cref="BasicVm.CallStack"/>
    /// shows the variables; the next <see cref="BasicVm.Step"/> runs it.</summary>
    Paused,
}

/// <summary>The outcome of one <see cref="BasicVm.Step"/>.</summary>
/// <param name="Status">Why the step returned.</param>
/// <param name="SleepSeconds">For <see cref="StepStatus.Sleeping"/>: how long to wait before resuming.</param>
/// <param name="ExitCode">For <see cref="StepStatus.Halted"/>: 0 on a normal end, 1 after an
/// unhandled exception, 2 if cancelled.</param>
/// <param name="Error">For <see cref="StepStatus.Halted"/>: the unhandled exception, formatted as the
/// CLI reports it (<c>unhandled exception type N at line L: text</c>); null otherwise.</param>
/// <param name="MaxQueuedTones">For <see cref="StepStatus.AwaitingAudio"/>: resume once no more than
/// this many tones are left to play — 0 after a foreground statement (all of it),
/// <see cref="Runtime.AudioState.BackgroundCapacity"/> when background music overfilled the buffer.</param>
public readonly record struct StepResult(StepStatus Status, double SleepSeconds = 0, int ExitCode = 0, string? Error = null,
    int MaxQueuedTones = 0)
{
    internal static readonly StepResult Running = new(StepStatus.Running);
    internal static readonly StepResult NeedsInput = new(StepStatus.NeedsInput);
    internal static readonly StepResult AwaitingAudio = new(StepStatus.AwaitingAudio);
    internal static readonly StepResult AudioQueueFull = new(StepStatus.AwaitingAudio, MaxQueuedTones: Runtime.AudioState.BackgroundCapacity);
    internal static readonly StepResult NeedsPoint = new(StepStatus.NeedsPoint);
    internal static readonly StepResult Paused = new(StepStatus.Paused);
    internal static readonly StepResult Ended = new(StepStatus.Halted);

    internal static StepResult Sleep(double seconds) => new(StepStatus.Sleeping, SleepSeconds: seconds);
    internal static StepResult Failed(int exitCode, string error) => new(StepStatus.Halted, ExitCode: exitCode, Error: error);
}
