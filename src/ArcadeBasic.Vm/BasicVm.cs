using System.Globalization;
using System.Text;
using ArcadeBasic.Bytecode;
using ArcadeBasic.Runtime;
using Singulink.Numerics;
using BcProgram = ArcadeBasic.Bytecode.Program;

namespace ArcadeBasic.Vm;

/// <summary>
/// Phase-9 stack-based bytecode VM. Same Value record types and activation-
/// record model as the tree-walker, so shared helpers (BuiltinImpls,
/// FormatNumeric) compose without translation.
/// </summary>
public sealed class BasicVm
{
    private readonly BcProgram _program;
    private readonly TextWriter _out;
    private readonly TextReader _in;

    private const int DefaultZoneWidth = 16;

    /// <summary>Cursor into <see cref="BcProgram.DataPool"/>. Reset on each Run(); advanced by READ / MatRead; rewound by RESTORE.</summary>
    private int _dataCursor;

    /// <summary>Per-run file channel table for OPEN / PRINT # / INPUT # / CLOSE.</summary>
    private readonly ChannelTable _channels = new();

    /// <summary>Stack of active exception handlers — pushed by BeginWhen, popped by PopHandler or by an exception dispatch.</summary>
    private readonly Stack<HandlerFrame> _handlerStack = new();

    /// <summary>Currently-active exception inside a USE body. Read by EXTYPE / EXLINE / EXTEXT$.</summary>
    private BasicException? _currentException;

    /// <summary>Source line of the statement currently executing. Updated by LineNote; used as EXLINE when an exception fires.</summary>
    private int _currentLine;

    /// <summary>PC at which CONTINUE should resume — the start of the statement immediately after the one that raised. Set by <see cref="Dispatch"/> from the catching frame's StmtEndPc.</summary>
    private int _currentContinuePc;

    private readonly record struct HandlerFrame(int UsePc, int StackBaseline);

    /// <summary>Current column of the output line, for PRINT's comma zones and TAB.
    /// Program-wide rather than per chunk: a PRINT ending in ';' or ',' inside a SUB
    /// leaves the caller's next PRINT on the same line, at the same column.</summary>
    private int _col;

    private readonly IGraphicsDevice _graphics;
    private readonly GraphicsState _gfx = new();
    private readonly IKeyboard _keyboard;

    /// <summary>INKEY$'s presses and KEYDOWN's held keys, over <see cref="_keyboard"/>.</summary>
    private readonly KeyboardState _keys;

    /// <summary>LOCATE POINT's and GET MOUSE's view of the graphics device's pointer, if it has one.</summary>
    private readonly PointerState _pointer;

    /// <summary>Which picks LOCATE POINT accepts: points inside the current window.</summary>
    private readonly Func<GfxPoint, bool> _pickable;
    private readonly IAudioDevice _audio;
    private readonly AudioState _audioState = new();

    /// <summary>RND's sequence for this run; reseeded by RANDOMIZE.</summary>
    private readonly BasicRandom _random;

    /// <summary>What <see cref="_audioState"/> plays through: passes tones to the
    /// device but turns a foreground statement's blocking drain into a pause.</summary>
    private readonly DeferredDrainAudio _audioPort;
    private readonly IAudioQueue? _audioQueue;

    /// <summary>OPTION ARITHMETIC NATIVE: numbers the program creates (constants,
    /// INPUT/READ data, builtin results) are doubles instead of decimals.</summary>
    private readonly bool _native;
    private readonly NumericValue _zero;
    private readonly NumericValue _one;

    /// <summary>Each chunk's code and numeric constant pool, materialised once (the
    /// constants in the program's representation) when the chunk is first entered.</summary>
    private readonly Dictionary<Chunk, ChunkRuntime> _chunks = new(ReferenceEqualityComparer.Instance);

    private sealed record class ChunkRuntime(byte[] Code, NumericValue[] Constants);

    /// <summary>Calls deeper than this raise exception 5000 (storage exhausted), which
    /// a WHEN can catch — instead of running the host out of memory.</summary>
    internal const int MaxCallDepth = 100_000;

    /// <summary>The call stack: the main program at the bottom, then one frame per
    /// active SUB / FUNCTION / DEF call. Calls push a frame rather than recursing in
    /// C#, so BASIC recursion depth doesn't depend on the host thread's stack.</summary>
    private readonly Stack<CallFrame> _frames = new();

    /// <summary>The operand stack, shared by every frame. A frame's own values sit
    /// above its <see cref="CallFrame.StackBase"/>.</summary>
    private readonly OperandStack _stack = new(256);

    private ActivationRecord _programFrame = null!;

    /// <summary>One executing chunk: where it is, its locals, and what to undo when
    /// it returns.</summary>
    private sealed class CallFrame(
        Chunk chunk, ChunkRuntime runtime, ActivationRecord locals,
        bool returnsValue, int returnSlot, bool returnsString,
        int handlerBase, int stackBase)
    {
        public readonly Chunk Chunk = chunk;
        public readonly byte[] Code = runtime.Code;
        public readonly NumericValue[] Constants = runtime.Constants;
        public readonly ActivationRecord Locals = locals;

        /// <summary>FUNCTION and DEF frames push their return slot onto the caller's
        /// operand stack when they leave; SUB and main frames push nothing.</summary>
        public readonly bool ReturnsValue = returnsValue;
        public readonly int ReturnSlot = returnSlot;
        public readonly bool ReturnsString = returnsString;

        /// <summary>Handlers below this depth belong to callers: an exception this
        /// frame can't handle unwinds it and is dispatched in the caller.</summary>
        public readonly int HandlerBase = handlerBase;

        /// <summary>Operand-stack depth on entry; leaving the frame discards anything above it.</summary>
        public readonly int StackBase = stackBase;

        /// <summary>Where this frame resumes. The dispatch loop works on a local copy and
        /// saves it here whenever it leaves the frame — for a call, or a pause.</summary>
        public int Pc;

        /// <summary>PC of the statement after the one executing (from LineNote) —
        /// where CONTINUE resumes if this frame's handler catches an exception.</summary>
        public int StmtEndPc;

        /// <summary>GOSUB return PCs. GOSUB/RETURN never cross chunk boundaries.</summary>
        public Stack<int>? Gosubs;

        /// <summary>The line of the statement this frame is on, and that statement's
        /// LineNote — so a debugger can tell a new line from the next statement on the same one.</summary>
        public int Line;
        public int NotePc = -1;
    }

    public BasicVm(BcProgram program, TextWriter @out, TextReader @in,
        IGraphicsDevice? graphics = null, IKeyboard? keyboard = null,
        IAudioDevice? audio = null, int? randomizeSeed = null)
    {
        _program = program;
        _out = @out;
        _in = @in;
        _graphics = graphics ?? NullGraphicsDevice.Instance;
        _keyboard = keyboard ?? NullKeyboard.Instance;
        _keys = new KeyboardState(_keyboard);
        _pointer = new PointerState(_graphics);
        _pickable = p => _gfx.InWindow(_gfx.ToProblem(p));
        _audio = audio ?? NullAudioDevice.Instance;
        _audioPort = new DeferredDrainAudio(_audio);
        _audioQueue = _audio as IAudioQueue;
        _random = new BasicRandom(randomizeSeed);
        _lineReader = @in as INonBlockingLineReader;
        _native = program.NativeArithmetic;
        _zero = NumericValue.Zeroed(_native);
        _one = NumericValue.From(1, _native);
        _builtinKinds = new BuiltinKind[program.BuiltinNames.Count];
        _builtinFns = new BuiltinImpls.BuiltinFn?[program.BuiltinNames.Count];
        for (var i = 0; i < program.BuiltinNames.Count; i++)
        {
            var name = program.BuiltinNames[i].ToUpperInvariant();
            _builtinKinds[i] = name switch
            {
                "EXTYPE" => BuiltinKind.Extype,
                "EXLINE" => BuiltinKind.Exline,
                "EXTEXT" => BuiltinKind.Extext,
                "INKEY" => BuiltinKind.Inkey,
                "KEYDOWN" => BuiltinKind.Keydown,
                "RND" => BuiltinKind.Rnd,
                "ABS" => BuiltinKind.Abs,
                "SGN" => BuiltinKind.Sgn,
                "INT" => BuiltinKind.Int,
                "SQR" => BuiltinKind.Sqr,
                "SIN" => BuiltinKind.Sin,
                "COS" => BuiltinKind.Cos,
                "TAN" => BuiltinKind.Tan,
                "ATN" => BuiltinKind.Atn,
                _ => BuiltinKind.Table,
            };
            _builtinFns[i] = BuiltinImpls.All.TryGetValue(name, out var fn) ? fn : null;
        }
    }

    /// <summary>What each of the program's builtins is, worked out once: the ones the VM
    /// answers itself, the numeric ones it computes unboxed under OPTION ARITHMETIC
    /// NATIVE, and the rest (BuiltinImpls' table).</summary>
    private enum BuiltinKind { Table, Extype, Exline, Extext, Inkey, Keydown, Rnd, Abs, Sgn, Int, Sqr, Sin, Cos, Tan, Atn }

    private readonly BuiltinKind[] _builtinKinds;
    private readonly BuiltinImpls.BuiltinFn?[] _builtinFns;

    /// <summary>Run the program to the end on this thread, doing the waiting itself —
    /// SLEEP sleeps, INPUT blocks on the reader, foreground audio drains. Returns the
    /// exit code (0 normal end, 1 unhandled exception — reported on stderr).</summary>
    public int Run() => Run(CancellationToken.None);

    /// <summary><see cref="Run()"/>, stopping with <see cref="OperationCanceledException"/>
    /// when <paramref name="cancel"/> fires (checked between slices of instructions and
    /// during SLEEP; a blocking reader's ReadLine can't be interrupted).</summary>
    public int Run(CancellationToken cancel)
    {
        try
        {
            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                var r = Step(RunSlice);
                switch (r.Status)
                {
                    case StepStatus.Halted:
                        if (r.Error is not null)
                        {
                            _out.Flush();
                            Console.Error.WriteLine(r.Error);
                        }
                        return r.ExitCode;
                    case StepStatus.Sleeping:
                        Wait(TimeSpan.FromMilliseconds(Math.Min(r.SleepSeconds * 1000.0, int.MaxValue)), cancel);
                        break;
                    case StepStatus.AwaitingAudio when r.MaxQueuedTones == 0:
                        _audio.Flush();   // real-time devices block until their queue drains
                        break;
                    case StepStatus.AwaitingAudio:
                        // Background music overfilled a device that can't block: its host is
                        // playing it elsewhere; wait for room.
                        while (_audioQueue!.Queued > r.MaxQueuedTones && !cancel.IsCancellationRequested)
                            Wait(InputPollInterval, cancel);
                        break;
                    case StepStatus.NeedsInput:
                    case StepStatus.NeedsPoint:
                        // Nothing to read yet (a line, or a pick): poll until it arrives.
                        Wait(InputPollInterval, cancel);
                        break;
                    // Running, or Paused at a breakpoint with no debugger to show: carry on.
                }
            }
        }
        finally
        {
            if (!_halted) Finish(StepResult.Failed(2, "program cancelled"));
        }
    }

    /// <summary>Instructions per <see cref="Step"/> inside <see cref="Run(CancellationToken)"/>:
    /// how often cancellation is checked.</summary>
    private const int RunSlice = 100_000;

    private static readonly TimeSpan InputPollInterval = TimeSpan.FromMilliseconds(10);

    private static void Wait(TimeSpan duration, CancellationToken cancel)
    {
        if (duration <= TimeSpan.Zero) return;
        if (cancel.CanBeCanceled)
        {
            cancel.WaitHandle.WaitOne(duration);
            cancel.ThrowIfCancellationRequested();
        }
        else
        {
            Thread.Sleep(duration);
        }
    }

    /// <summary>
    /// Run at most <paramref name="maxInstructions"/> bytecode instructions, returning
    /// early when the program sleeps, needs input, waits for audio, or ends. Nothing
    /// here blocks except a plain (blocking) <see cref="TextReader"/> or a device
    /// call, so a host that owns the main loop — a browser page, a game's frame
    /// update — calls this repeatedly and does the waiting itself. The first call
    /// starts the program. Not thread-safe: step a VM from one thread at a time.
    /// </summary>
    public StepResult Step(int maxInstructions)
    {
        if (maxInstructions < 1) throw new ArgumentOutOfRangeException(nameof(maxInstructions));
        if (_halted) return _final;
        _budget = maxInstructions;
        _debugging = _breakpoints.Count > 0 || _stepKind is not null || _resumeFrame is not null;
        try
        {
            while (true)
            {
                try
                {
                    if (!_started)
                    {
                        _started = true;
                        _programFrame = new ActivationRecord(_program.Main.FrameSize, parent: null);
                        PushFrame(_program.Main, _programFrame, returnsValue: false, returnSlot: 0, returnsString: false);
                    }
                    var r = RunFrames();
                    return r.Status == StepStatus.Halted ? Finish(r) : r;
                }
                catch (BasicRuntimeException ex)
                {
                    // Hand it to the innermost active WHEN handler, or end the program.
                    // _currentLine is the most recent LineNote: the failing statement.
                    // (Same "unhandled exception" text as the tree-walker.)
                    if (!Dispatch(ex))
                        return Finish(StepResult.Failed(1,
                            $"unhandled exception type {ex.TypeCode} at line {_currentLine}: {ex.Message}"));
                }
            }
        }
        catch
        {
            // Not a BASIC exception — a VM or device fault. Close the program's files on the way out.
            if (!_halted) Finish(StepResult.Failed(1, "the VM stopped on an internal error"));
            throw;
        }
        finally
        {
            // Each instruction run took one off the budget; a spent budget ends at -1.
            InstructionsExecuted += maxInstructions - Math.Max(_budget, 0);
        }
    }

    /// <summary>Bytecode instructions run so far, over every <see cref="Step"/> — for a
    /// host's speed readout or CPU meter.</summary>
    public long InstructionsExecuted { get; private set; }

    /// <summary>The source line (1-based) of the statement running — once halted by an
    /// unhandled exception, the statement that raised it (the line
    /// <see cref="StepResult.Error"/> names). For an editor to point at.</summary>
    public int CurrentLine => _currentLine;

    // -- Debugging --------------------------------------------------------------------

    /// <summary>Source lines to stop at. Before running the first statement of a line in
    /// the set, <see cref="Step"/> returns <see cref="StepStatus.Paused"/>. Change it
    /// between steps.</summary>
    public ISet<int> Breakpoints => _breakpoints;

    private readonly HashSet<int> _breakpoints = [];
    private bool _debugging;                  // any breakpoint, step or resume pending (set by Step)
    private DebugStep? _stepKind;
    private CallFrame? _stepFrame;
    private int _stepDepth;
    private CallFrame? _resumeFrame;          // paused before this statement: run it next time
    private int _resumePc;
    private Dictionary<Chunk, string>? _chunkNames;

    /// <summary>Stop again soon (see <see cref="DebugStep"/>): from a pause, a step; while
    /// running, a pause button. The next <see cref="Step"/> that gets there returns
    /// <see cref="StepStatus.Paused"/>.</summary>
    public void PauseAt(DebugStep step)
    {
        _stepKind = step;
        _stepFrame = _frames.Count > 0 ? _frames.Peek() : null;
        _stepDepth = Depth();
    }

    /// <summary>The calls in progress, innermost first, with their variables.</summary>
    public IReadOnlyList<DebugFrame> CallStack()
    {
        if (_chunkNames is null)
        {
            _chunkNames = new(ReferenceEqualityComparer.Instance) { [_program.Main] = "" };
            foreach (var s in _program.Subs) _chunkNames[s.Body] = "SUB " + s.Name;
            foreach (var f in _program.Functions) _chunkNames[f.Body] = "FUNCTION " + f.Name + (f.IsString ? "$" : "");
            foreach (var d in _program.Defs) _chunkNames[d.Body] = "DEF " + d.Name + (d.IsString ? "$" : "");
        }
        var frames = new List<DebugFrame>(_frames.Count);
        foreach (var f in _frames)
        {
            // (The innermost frame may be paused before its statement: that's the current line.)
            var line = frames.Count == 0 ? _currentLine : f.Line;
            frames.Add(new DebugFrame(f.Chunk, _chunkNames.TryGetValue(f.Chunk, out var name) ? name : "", line, f.Locals));
        }
        return frames;
    }

    /// <summary>How deep in calls and GOSUBs the program is.</summary>
    private int Depth()
    {
        var depth = 0;
        foreach (var f in _frames) depth += 1 + (f.Gosubs?.Count ?? 0);
        return depth;
    }

    /// <summary>At a statement's LineNote: should the program stop before it?</summary>
    private bool ShouldPause(CallFrame cf, int notePc, int line)
    {
        if (ReferenceEquals(cf, _resumeFrame) && notePc == _resumePc)
        {
            _resumeFrame = null;              // the statement it stopped at: run it now
            return false;
        }
        // A new line, rather than the next statement on the same one — or the same line
        // again, by a jump back (a loop).
        var newLine = line != cf.Line || notePc <= cf.NotePc;
        var pause = newLine && _breakpoints.Contains(line);
        if (!pause && _stepKind is { } kind)
        {
            pause = kind switch
            {
                DebugStep.Pause => true,
                DebugStep.Into => newLine || !ReferenceEquals(cf, _stepFrame),
                DebugStep.Over => (newLine || !ReferenceEquals(cf, _stepFrame)) && Depth() <= _stepDepth,
                _ => Depth() < _stepDepth,    // Out
            };
        }
        if (!pause) return false;
        _stepKind = null;
        _resumeFrame = cf;
        _resumePc = notePc;
        return true;
    }

    private bool _started;
    private bool _halted;
    private StepResult _final;

    /// <summary>Instructions left in the current <see cref="Step"/>.</summary>
    private int _budget;

    /// <summary>The program is over: release its files and keep the result for later steps.</summary>
    private StepResult Finish(StepResult result)
    {
        _halted = true;
        _final = result;
        _frames.Clear();
        _handlerStack.Clear();
        _channels.Dispose();
        return result;
    }

    /// <summary>Unwind to the frame owning the innermost handler and point it at the
    /// handler's USE body. Returns false when no handler is active (frames are gone).</summary>
    private bool Dispatch(BasicRuntimeException ex)
    {
        while (_frames.Count > 0 && _handlerStack.Count <= _frames.Peek().HandlerBase)
            _frames.Pop();
        if (_frames.Count == 0) return false;

        var cf = _frames.Peek();
        var top = _handlerStack.Pop();
        _stack.Truncate(top.StackBaseline);
        _currentException = new BasicException(ex.TypeCode, _currentLine, ex.Message);
        // A CONTINUE inside the USE body resumes at the statement after the one
        // that raised — in the catching frame, so after a CALL that raised.
        _currentContinuePc = cf.StmtEndPc;
        cf.Pc = top.UsePc;
        return true;
    }

    private void PushFrame(Chunk chunk, ActivationRecord locals, bool returnsValue, int returnSlot, bool returnsString)
    {
        if (_frames.Count >= MaxCallDepth)
            throw new BasicRuntimeException(5000, $"procedure calls nested more than {MaxCallDepth} deep");
        _frames.Push(new CallFrame(chunk, RuntimeOf(chunk), locals, returnsValue, returnSlot, returnsString,
            handlerBase: _handlerStack.Count, stackBase: _stack.Count));
    }

    /// <summary>Return from the current frame (LeaveSub / LeaveFunction, or running
    /// off the end of its code): drop the handlers and operand values it left, and
    /// hand a FUNCTION's or DEF's result to the caller. False if it was the main program.</summary>
    private bool LeaveFrame()
    {
        var done = _frames.Pop();
        while (_handlerStack.Count > done.HandlerBase) _handlerStack.Pop();
        _stack.Truncate(done.StackBase);
        if (_frames.Count == 0) return false;
        if (done.ReturnsValue)
            _stack.Push(done.Locals.GetOrDefault(done.ReturnSlot, done.ReturnsString ? StringValue.Empty : _zero));
        return true;
    }

    /// <summary>The dispatch loop. Works on locals copied from the current frame;
    /// a call or return saves <c>pc</c> into the frame and reloads them, and so does
    /// every pause, so the next <see cref="Step"/> picks up where this one stopped.</summary>
    private StepResult RunFrames()
    {
        var stack = _stack;
        var programFrame = _programFrame;
        CallFrame cf;
        Chunk chunk;
        byte[] code;
        NumericValue[] constants;
        ActivationRecord frame;
        int pc;

    SwitchFrame:
        cf = _frames.Peek();
        chunk = cf.Chunk;
        code = cf.Code;
        constants = cf.Constants;
        frame = cf.Locals;
        pc = cf.Pc;

        while (true)
        {
            if (--_budget < 0)
            {
                cf.Pc = pc;
                return StepResult.Running;
            }
            if (pc >= code.Length)
            {
                // Ran off the end of the chunk — the same as LeaveSub/LeaveFunction
                // (well-formed bytecode ends with one, or with End in main).
                if (!LeaveFrame()) return StepResult.Ended;
                goto SwitchFrame;
            }
            var op = (Opcode)code[pc++];
            switch (op)
            {
                case Opcode.Halt:
                case Opcode.Stop:
                case Opcode.End:
                    return StepResult.Ended;
                case Opcode.Nop: break;

                // -- Graphic input: LOCATE POINT / GET POINT, GET MOUSE --
                case Opcode.GfxInputDevice:
                    {
                        var device = ((NumericValue)stack.Pop()).ToInt32();
                        if (device != 1)
                            throw new BasicRuntimeException(11140, $"LOCATE POINT: graphic input device {device} is not present");
                        break;
                    }
                case Opcode.LocatePoint:
                    {
                        var opStart = pc - 1;
                        var descs = ReadTargetDescs(code, ref pc, 2);
                        if (!_pointer.Present)
                        {
                            // No pointing device: the standard has device 1 simulated from
                            // the keyboard (§13.4.4) — as INPUT x, y.
                            if (!TryReadInputLine("? ", out var rawLine))
                            {
                                cf.Pc = opStart;
                                return StepResult.NeedsInput;
                            }
                            if (rawLine is null) throw new BasicRuntimeException(4003, "INPUT: end of input stream");
                            var typed = ParseInputReply(rawLine, descs);
                            if (typed is null)
                            {
                                pc = opStart;   // "redo from start" printed: ask again
                                break;
                            }
                            AssignTargets(descs, typed, stack, frame, programFrame);
                            _col = 0;
                            break;
                        }
                        _graphics.Flush();   // show the picture being pointed at
                        if (!_pointer.TryLocate(_pickable, out var at))
                        {
                            cf.Pc = opStart;   // re-run this instruction once there's a press
                            return StepResult.NeedsPoint;
                        }
                        var point = _gfx.ToProblem(at);
                        AssignTargets(descs, [GraphicsState.Coordinate(point.X, _native), GraphicsState.Coordinate(point.Y, _native)],
                            stack, frame, programFrame);
                        break;
                    }
                case Opcode.GetMouse:
                    {
                        var descs = ReadTargetDescs(code, ref pc, 3);
                        var state = _pointer.Sample(out var at);
                        var point = state == PointerState.NoPointer ? default : _gfx.ToProblem(at);
                        AssignTargets(descs,
                            [GraphicsState.Coordinate(point.X, _native), GraphicsState.Coordinate(point.Y, _native), NumericValue.From(state, _native)],
                            stack, frame, programFrame);
                        break;
                    }
                case Opcode.Randomize:
                    if (ReadU32(code, ref pc) == 0) _random.Randomize();
                    else _random.Randomize(((NumericValue)stack.Pop()).D);   // RANDOMIZE n: an extension
                    break;
                case Opcode.ForPrep:
                    {
                        var limitSlot = (int)ReadU32(code, ref pc);
                        var stepSlot = (int)ReadU32(code, ref pc);
                        var step = stack.Pop();
                        var limit = stack.Pop();
                        if (((NumericValue)step).IsZero)
                            throw new BasicRuntimeException(6002, "FOR step cannot be zero");
                        frame.Set(limitSlot, limit);
                        frame.Set(stepSlot, step);
                        break;
                    }
                case Opcode.ForTest:
                    {
                        var limit = (NumericValue)frame.Get((int)ReadU32(code, ref pc));
                        var step = (NumericValue)frame.Get((int)ReadU32(code, ref pc));
                        var cmp = _native
                            ? stack.PopNumber().CompareTo(limit.D)
                            : Numbers.Compare((NumericValue)stack.Pop(), limit);
                        stack.Push(NumericValue.Bool(step.IsNegative ? cmp < 0 : cmp > 0, _native));
                        break;
                    }
                case Opcode.Sleep:
                    {
                        // Frame boundary: present what's drawn, then pause.
                        _graphics.Flush();
                        var secs = ((NumericValue)stack.Pop()).D;
                        if (secs > 0)
                        {
                            cf.Pc = pc;
                            return StepResult.Sleep(secs);
                        }
                        break;
                    }

                // -- Audio (SOUND/BEEP/PLAY): drive the same AudioState as the tree-walker --
                case Opcode.Sound:
                    {
                        _graphics.Flush();
                        var dur = ((NumericValue)stack.Pop()).D;   // pushed last → on top
                        var freq = ((NumericValue)stack.Pop()).D;
                        _audioState.EmitSound(freq, dur, _audioPort);
                        if (AudioWait() is { } audioWait) { cf.Pc = pc; return audioWait; }
                        break;
                    }
                case Opcode.Beep:
                    {
                        _graphics.Flush();
                        _audioState.EmitBeep(_audioPort);
                        if (AudioWait() is { } audioWait) { cf.Pc = pc; return audioWait; }
                        break;
                    }
                case Opcode.Play:
                    {
                        _graphics.Flush();
                        var notes = ((StringValue)stack.Pop()).V;
                        _audioState.EmitPlay(notes, _audioPort);
                        if (AudioWait() is { } audioWait) { cf.Pc = pc; return audioWait; }
                        break;
                    }

                // -- Graphics (§13): drive the same GraphicsState as the tree-walker --
                case Opcode.GfxSetBounds:
                    {
                        var kind = (int)ReadU32(code, ref pc);
                        var t = stack.PopNumber();
                        var b = stack.PopNumber();
                        var r = stack.PopNumber();
                        var l = stack.PopNumber();
                        switch (kind)
                        {
                            case 0: _gfx.SetWindow(l, r, b, t); break;
                            case 1: _gfx.SetViewport(l, r, b, t); break;
                            case 2: if (_gfx.SetDeviceWindow(l, r, b, t)) _graphics.Clear(); break;
                            case 3: if (_gfx.SetDeviceViewport(l, r, b, t)) _graphics.Clear(); break;
                        }
                        break;
                    }
                case Opcode.GfxSetClip:
                    {
                        var v = ((StringValue)stack.Pop()).V.Trim().ToUpperInvariant();
                        if (v == "ON") _gfx.ClipEnabled = true;
                        else if (v == "OFF") _gfx.ClipEnabled = false;
                        break;
                    }
                case Opcode.GfxSetStyle:
                    {
                        var prim = (int)ReadU32(code, ref pc);
                        var n = GraphicsState.ToIndex((NumericValue)stack.Pop());
                        if (prim == 0) { _gfx.PointStyle = n; _graphics.SetPointStyle(n); }
                        else { _gfx.LineStyle = n; _graphics.SetLineStyle(n); }
                        break;
                    }
                case Opcode.GfxSetColor:
                    {
                        var tgt = (GfxColorTarget)(int)ReadU32(code, ref pc);
                        var n = GraphicsState.ToIndex((NumericValue)stack.Pop());
                        switch (tgt)
                        {
                            case GfxColorTarget.Point: _gfx.PointColor = n; break;
                            case GfxColorTarget.Line: _gfx.LineColor = n; break;
                            case GfxColorTarget.Text: _gfx.TextColor = n; break;
                            case GfxColorTarget.Area: _gfx.AreaColor = n; break;
                        }
                        _graphics.SetColor(tgt, n);
                        break;
                    }
                case Opcode.GfxClear: _graphics.Clear(); break;
                case Opcode.GfxDraw:
                    {
                        var geom = (int)ReadU32(code, ref pc);
                        var count = (int)ReadU32(code, ref pc);
                        var pts = new GfxPoint[count];
                        for (var i = count - 1; i >= 0; i--)
                        {
                            var y = stack.PopNumber();
                            var x = stack.PopNumber();
                            pts[i] = new GfxPoint(x, y);
                        }
                        switch (geom)
                        {
                            case 0: _gfx.EmitPoints(pts, _graphics); break;
                            case 1: _gfx.EmitLines(pts, _graphics); break;
                            case 2: _gfx.EmitArea(pts, _graphics); break;
                        }
                        break;
                    }
                case Opcode.GfxText:
                    {
                        var hasImage = (int)ReadU32(code, ref pc);
                        var itemCount = (int)ReadU32(code, ref pc);
                        string text;
                        if (hasImage == 0)
                        {
                            text = ((StringValue)stack.Pop()).V;
                        }
                        else
                        {
                            var items = new Value[itemCount];
                            for (var i = itemCount - 1; i >= 0; i--) items[i] = stack.Pop();
                            var image = ((StringValue)stack.Pop()).V;
                            text = PictureFormat.Apply(PictureFormat.Parse(image), items);
                        }
                        var ay = stack.PopNumber();
                        var ax = stack.PopNumber();
                        _gfx.EmitText(new GfxPoint(ax, ay), text, _graphics);
                        break;
                    }
                case Opcode.GfxAskValue:
                    {
                        var q = (GfxQuery)(int)ReadU32(code, ref pc);
                        var index = (int)ReadU32(code, ref pc);
                        stack.Push(_gfx.Query(q, index, _graphics, _native));
                        break;
                    }
                case Opcode.GfxCells:
                    {
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var y2 = stack.PopNumber();
                        var x2 = stack.PopNumber();
                        var y1 = stack.PopNumber();
                        var x1 = stack.PopNumber();
                        if (ResolveOuter(frame, programFrame, depth).GetOrDefault(slot, NumericValue.Zero) is not NumericArrayValue cells)
                            throw new BasicRuntimeException(6004, "MAT GRAPH CELLS requires the array to be DIM-ed first");
                        _gfx.EmitCells(new GfxPoint(x1, y1), new GfxPoint(x2, y2), cells, _graphics);
                        break;
                    }
                case Opcode.GfxSetColorMix:
                    {
                        var blue = stack.PopNumber();
                        var green = stack.PopNumber();
                        var red = stack.PopNumber();
                        var index = GraphicsState.ToIndex((NumericValue)stack.Pop());
                        _gfx.SetColorMix(index, red, green, blue, _graphics);
                        break;
                    }
                case Opcode.GfxAskColorMix:
                    {
                        var component = (int)ReadU32(code, ref pc);
                        var index = GraphicsState.ToIndex((NumericValue)stack.Pop());
                        stack.Push(_gfx.QueryColorMix(index, component, _graphics, _native));
                        break;
                    }

                case Opcode.Pop: stack.Pop(); break;
                case Opcode.Dup: stack.Dup(); break;
                case Opcode.Swap:
                    var s1 = stack.Pop();
                    var s2 = stack.Pop();
                    stack.Push(s1); stack.Push(s2);
                    break;

                case Opcode.LoadConstNumber:
                    stack.Push(constants[(int)ReadU32(code, ref pc)]);
                    break;
                case Opcode.LoadConstString:
                    stack.Push(new StringValue(chunk.Strings[(int)ReadU32(code, ref pc)]));
                    break;
                case Opcode.LoadZero: stack.Push(_zero); break;
                case Opcode.LoadOne: stack.Push(_one); break;
                case Opcode.LoadMinusOne: stack.Push(NumericValue.Bool(true, _native)); break;

                // Native numbers stay unboxed from the operand stack to the variable
                // and back (see OperandStack and ActivationRecord.SetNumber).
                case Opcode.LoadLocal:
                    {
                        var slot = (int)ReadU32(code, ref pc);
                        if (frame.TryGetUnboxed(slot, out var n)) stack.PushNumber(n);
                        else stack.Push(frame.GetOrDefault(slot, _zero));
                        break;
                    }
                case Opcode.StoreLocal:
                    {
                        var slot = (int)ReadU32(code, ref pc);
                        if (stack.TryPopUnboxed(out var n)) frame.SetNumber(slot, n);
                        else frame.Set(slot, stack.Pop());
                        break;
                    }
                case Opcode.LoadOuter:
                    {
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var f = frame;
                        for (var i = 0; i < depth && f is not null; i++) f = f.Parent;
                        f ??= programFrame;
                        if (f.TryGetUnboxed(slot, out var n)) stack.PushNumber(n);
                        else stack.Push(f.GetOrDefault(slot, _zero));
                        break;
                    }
                case Opcode.StoreOuter:
                    {
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var f = frame;
                        for (var i = 0; i < depth && f is not null; i++) f = f.Parent;
                        f ??= programFrame;
                        if (stack.TryPopUnboxed(out var n)) f.SetNumber(slot, n);
                        else f.Set(slot, stack.Pop());
                        break;
                    }

                // Under OPTION ARITHMETIC NATIVE every number is a double: work on them
                // unboxed (Numbers.Native's overflow check, Numbers.Divide's zero check).
                case Opcode.Add:
                    if (_native) stack.PushNumber(Finite(stack.PopNumber() + stack.PopNumber()));
                    else Arithmetic(stack, Numbers.Add);
                    break;
                case Opcode.Sub:
                    if (_native) { var b = stack.PopNumber(); stack.PushNumber(Finite(stack.PopNumber() - b)); }
                    else Arithmetic(stack, Numbers.Subtract);
                    break;
                case Opcode.Mul:
                    if (_native) stack.PushNumber(Finite(stack.PopNumber() * stack.PopNumber()));
                    else Arithmetic(stack, Numbers.Multiply);
                    break;
                case Opcode.Div:
                    if (_native)
                    {
                        var b = stack.PopNumber();
                        if (b == 0) throw new BasicRuntimeException(1001, "division by zero");
                        stack.PushNumber(Finite(stack.PopNumber() / b));
                    }
                    else Arithmetic(stack, Numbers.Divide);
                    break;
                case Opcode.Pow: Arithmetic(stack, Numbers.Power); break;
                case Opcode.Mod: Arithmetic(stack, Numbers.Mod); break;
                case Opcode.Rem: Arithmetic(stack, Numbers.Remainder); break;
                case Opcode.Concat:
                    {
                        var br = ((StringValue)stack.Pop()).V;
                        var bl = ((StringValue)stack.Pop()).V;
                        stack.Push(new StringValue(bl + br));
                        break;
                    }
                case Opcode.Neg:
                    if (_native) stack.PushNumber(-stack.PopNumber());
                    else stack.Push(Numbers.Negate((NumericValue)stack.Pop()));
                    break;

                // Two native numbers compare directly; anything else (strings, decimals)
                // goes through Compare.
                case Opcode.Eq:
                    if (stack.TryPopNativePair(out var eqA, out var eqB)) stack.Push(NumericValue.Bool(eqA == eqB, _native));
                    else Compare(stack, c => c == 0);
                    break;
                case Opcode.Ne:
                    if (stack.TryPopNativePair(out var neA, out var neB)) stack.Push(NumericValue.Bool(neA != neB, _native));
                    else Compare(stack, c => c != 0);
                    break;
                case Opcode.Lt:
                    if (stack.TryPopNativePair(out var ltA, out var ltB)) stack.Push(NumericValue.Bool(ltA < ltB, _native));
                    else Compare(stack, c => c < 0);
                    break;
                case Opcode.Le:
                    if (stack.TryPopNativePair(out var leA, out var leB)) stack.Push(NumericValue.Bool(leA <= leB, _native));
                    else Compare(stack, c => c <= 0);
                    break;
                case Opcode.Gt:
                    if (stack.TryPopNativePair(out var gtA, out var gtB)) stack.Push(NumericValue.Bool(gtA > gtB, _native));
                    else Compare(stack, c => c > 0);
                    break;
                case Opcode.Ge:
                    if (stack.TryPopNativePair(out var geA, out var geB)) stack.Push(NumericValue.Bool(geA >= geB, _native));
                    else Compare(stack, c => c >= 0);
                    break;

                case Opcode.And: Logical(stack, (a, b) => a && b); break;
                case Opcode.Or: Logical(stack, (a, b) => a || b); break;
                case Opcode.Xor: Logical(stack, (a, b) => a != b); break;
                case Opcode.Not:
                    stack.Push(NumericValue.From(((NumericValue)stack.Pop()).IsZero ? 1 : 0, _native));
                    break;
                case Opcode.Imp: Logical(stack, (a, b) => !a || b); break;
                case Opcode.Eqv: Logical(stack, (a, b) => a == b); break;
                case Opcode.Band: Bitwise(stack, (a, b) => a & b); break;
                case Opcode.Bor: Bitwise(stack, (a, b) => a | b); break;
                case Opcode.Bxor: Bitwise(stack, (a, b) => a ^ b); break;
                case Opcode.Bnot:
                    stack.Push(NumericValue.From(~((NumericValue)stack.Pop()).ToInt64(), _native));
                    break;

                case Opcode.Jump:
                    {
                        var off = ReadI32(code, ref pc);
                        pc += off;
                        break;
                    }
                case Opcode.JumpIfTrue:
                    {
                        var off = ReadI32(code, ref pc);
                        if (!stack.PopIsZero()) pc += off;
                        break;
                    }
                case Opcode.JumpIfFalse:
                    {
                        var off = ReadI32(code, ref pc);
                        if (stack.PopIsZero()) pc += off;
                        break;
                    }
                case Opcode.GosubFlow:
                    {
                        // Operand is the absolute PC of the GOSUB target.
                        var target = (int)ReadU32(code, ref pc);
                        (cf.Gosubs ??= new Stack<int>()).Push(pc); // resume here after RETURN
                        pc = target;
                        break;
                    }
                case Opcode.Return:
                    {
                        if (cf.Gosubs is not { Count: > 0 })
                            throw new BasicRuntimeException(3001, "RETURN without GOSUB");
                        pc = cf.Gosubs.Pop();
                        break;
                    }

                case Opcode.CallBuiltin:
                    {
                        var bid = (int)ReadU32(code, ref pc);
                        var argc = (int)ReadU32(code, ref pc);
                        var kind = _builtinKinds[bid];
                        if (_native && argc == 1 && kind >= BuiltinKind.Abs)
                        {
                            // The numeric builtins, unboxed: what BuiltinImpls computes
                            // for a native argument, with its checks.
                            var x = stack.PopNumber();
                            stack.PushNumber(kind switch
                            {
                                BuiltinKind.Abs => Math.Abs(x),
                                BuiltinKind.Sgn => x == 0 ? 0 : x < 0 ? -1 : 1,
                                BuiltinKind.Int => Math.Floor(x),
                                BuiltinKind.Sqr => x < 0
                                    ? throw new BasicRuntimeException(3005, "SQR requires a non-negative argument")
                                    : RealResult("SQR", Math.Sqrt(x)),
                                BuiltinKind.Sin => RealResult("SIN", Math.Sin(x)),
                                BuiltinKind.Cos => RealResult("COS", Math.Cos(x)),
                                BuiltinKind.Tan => RealResult("TAN", Math.Tan(x)),
                                _ => RealResult("ATN", Math.Atan(x)),
                            });
                            break;
                        }
                        var args = new Value[argc];
                        for (var i = argc - 1; i >= 0; i--) args[i] = stack.Pop();
                        // EXTYPE/EXLINE/EXTEXT$ must see the VM's current
                        // exception. BuiltinImpls has zero-returning stubs for
                        // them (they don't have access to interpreter state),
                        // so we intercept here before the BuiltinImpls dispatch.
                        switch (kind)
                        {
                            case BuiltinKind.Extype:
                                stack.Push(NumericValue.From(_currentException?.Type ?? 0, _native));
                                break;
                            case BuiltinKind.Exline:
                                stack.Push(NumericValue.From(_currentException?.Line ?? 0, _native));
                                break;
                            case BuiltinKind.Extext:
                                stack.Push(_currentException is null
                                    ? StringValue.Empty
                                    : new StringValue(_currentException.Text));
                                break;
                            case BuiltinKind.Inkey:
                                stack.Push(new StringValue(_keys.ReadKey()));   // non-blocking key poll
                                break;
                            case BuiltinKind.Keydown:
                                stack.Push(NumericValue.Bool(_keys.IsDown(((StringValue)args[0]).V), _native));
                                break;
                            case BuiltinKind.Rnd:
                                stack.Push(_random.Next(_native));   // this run's sequence; any argument is ignored
                                break;
                            default:
                                var fn = _builtinFns[bid]
                                    ?? throw new BasicRuntimeException(0, $"builtin '{_program.BuiltinNames[bid]}' not implemented");
                                stack.Push(fn(args, _native));
                                break;
                        }
                        break;
                    }
                case Opcode.CallSub:
                    {
                        var sid = (int)ReadU32(code, ref pc);
                        var argc = (int)ReadU32(code, ref pc);
                        var sub = _program.Subs[sid];
                        var subFrame = new ActivationRecord(sub.Body.FrameSize, programFrame);
                        for (var i = argc - 1; i >= 0; i--) subFrame.Set(i, stack.Pop());
                        cf.Pc = pc;
                        PushFrame(sub.Body, subFrame, returnsValue: false, returnSlot: 0, returnsString: false);
                        goto SwitchFrame;
                    }
                case Opcode.CallFunction:
                    {
                        var fid = (int)ReadU32(code, ref pc);
                        var argc = (int)ReadU32(code, ref pc);
                        var fn = _program.Functions[fid];
                        var fnFrame = new ActivationRecord(fn.Body.FrameSize, programFrame);
                        for (var i = argc - 1; i >= 0; i--) fnFrame.Set(i, stack.Pop());
                        // The body assigns its result to the return slot; LeaveFunction
                        // pushes that slot onto this frame's operand stack.
                        cf.Pc = pc;
                        PushFrame(fn.Body, fnFrame, returnsValue: true, fn.ReturnSlot, fn.IsString);
                        goto SwitchFrame;
                    }
                case Opcode.CallDef:
                    {
                        var did = (int)ReadU32(code, ref pc);
                        var argc = (int)ReadU32(code, ref pc);
                        var def = _program.Defs[did];
                        // DEF body's parent scope is the caller's frame (so the
                        // body can reference the caller's outer names), unlike
                        // SUB/FUNCTION whose parent is programFrame.
                        var defFrame = new ActivationRecord(def.Body.FrameSize, frame);
                        for (var i = argc - 1; i >= 0; i--) defFrame.Set(i, stack.Pop());
                        cf.Pc = pc;
                        PushFrame(def.Body, defFrame, returnsValue: true, def.ReturnSlot, def.IsString);
                        goto SwitchFrame;
                    }
                case Opcode.LeaveSub:
                case Opcode.LeaveFunction:
                    if (!LeaveFrame()) return StepResult.Ended;
                    goto SwitchFrame;

                case Opcode.PrintNumber:
                    {
                        var v = ((NumericValue)stack.Pop()).V;
                        var text = FormatNumeric(v);
                        _out.Write(text);
                        _col += text.Length;
                        break;
                    }
                case Opcode.PrintString:
                    {
                        var s = ((StringValue)stack.Pop()).V;
                        _out.Write(s);
                        _col += s.Length;
                        break;
                    }
                case Opcode.PrintNewline:
                    _out.WriteLine();
                    _col = 0;
                    break;
                case Opcode.PrintZonePad:
                    {
                        var next = ((_col / DefaultZoneWidth) + 1) * DefaultZoneWidth;
                        for (var i = _col; i < next; i++) _out.Write(' ');
                        _col = next;
                        break;
                    }
                case Opcode.PrintTab:
                    {
                        // BASIC TAB(n) is 1-based; clamp negatives to column 0,
                        // and never move backwards (TAB to before current column is a no-op).
                        var target = ((NumericValue)stack.Pop()).ToInt32() - 1;
                        if (target < 0) target = 0;
                        if (target > _col)
                        {
                            for (var i = _col; i < target; i++) _out.Write(' ');
                            _col = target;
                        }
                        break;
                    }

                case Opcode.LineNote:
                    {
                        var notePc = pc - 1;
                        var line = (int)ReadU32(code, ref pc);
                        var endOffset = ReadI32(code, ref pc);
                        _currentLine = line;
                        if (_debugging && ShouldPause(cf, notePc, line))
                        {
                            cf.Pc = notePc;           // run the statement on the next Step
                            return StepResult.Paused;
                        }
                        cf.Line = line;
                        cf.NotePc = notePc;
                        cf.StmtEndPc = pc + endOffset;
                        break;
                    }
                case Opcode.BeginWhen:
                    {
                        var useOffset = ReadI32(code, ref pc);
                        var usePc = pc + useOffset;
                        _handlerStack.Push(new HandlerFrame(usePc, stack.Count));
                        break;
                    }
                case Opcode.PopHandler:
                    if (_handlerStack.Count > cf.HandlerBase) _handlerStack.Pop();
                    break;
                case Opcode.Cause:
                    {
                        var type = ((NumericValue)stack.Pop()).ToInt32();
                        throw new BasicRuntimeException(type, $"user-raised exception {type}");
                    }
                case Opcode.Retry:
                    {
                        var off = ReadI32(code, ref pc);
                        pc += off;
                        break;
                    }
                case Opcode.Continue:
                    pc = _currentContinuePc;
                    break;

                case Opcode.Open:
                    {
                        var access = ReadU32(code, ref pc);
                        var organization = ReadU32(code, ref pc);
                        var create = ReadU32(code, ref pc);
                        var rectype = ReadU32(code, ref pc);
                        _ = organization; // SEQUENTIAL and STREAM both map to DisplayFile; RANDOM is unsupported.
                        var name = ((StringValue)stack.Pop()).V;
                        var channel = ((NumericValue)stack.Pop()).ToInt32();
                        OpenChannel(channel, name, access, create, rectype);
                        break;
                    }
                case Opcode.Close:
                    {
                        var channel = ((NumericValue)stack.Pop()).ToInt32();
                        _channels.Close(channel);
                        break;
                    }
                case Opcode.PrintFile:
                    {
                        var itemCount = (int)ReadU32(code, ref pc);
                        var kinds = new uint[itemCount];
                        // Both ExprNumeric/ExprString (0/1) and Tab (4) consume a stack value.
                        var stackItemCount = 0;
                        for (var i = 0; i < itemCount; i++)
                        {
                            kinds[i] = ReadU32(code, ref pc);
                            if (kinds[i] <= 1 || kinds[i] == 4) stackItemCount++;
                        }
                        var channel = ((NumericValue)stack.Pop()).ToInt32();
                        var stackValues = new Value[stackItemCount];
                        for (var i = stackItemCount - 1; i >= 0; i--) stackValues[i] = stack.Pop();
                        PerformPrintFile(channel, kinds, stackValues);
                        break;
                    }
                case Opcode.InputFile:
                    {
                        var targetCount = (int)ReadU32(code, ref pc);
                        var descs = new InputTargetDesc[targetCount];
                        for (var i = 0; i < targetCount; i++)
                        {
                            descs[i] = new InputTargetDesc(
                                Depth: (int)ReadU32(code, ref pc),
                                Slot: (int)ReadU32(code, ref pc),
                                IsString: ReadU32(code, ref pc) != 0,
                                Rank: (int)ReadU32(code, ref pc));
                        }
                        var channel = ((NumericValue)stack.Pop()).ToInt32();
                        var indices = new int[targetCount][];
                        for (var i = targetCount - 1; i >= 0; i--)
                        {
                            indices[i] = PopIndices(stack, descs[i].Rank);
                        }
                        PerformInputFile(channel, descs, indices, frame, programFrame);
                        break;
                    }
                case Opcode.LineInput:
                    {
                        var opStart = pc - 1;
                        var suppressQuestionMark = ReadU32(code, ref pc) != 0;
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var rank = (int)ReadU32(code, ref pc);
                        if (!TryReadInputLine(suppressQuestionMark ? " " : "? ", out var line))
                        {
                            cf.Pc = opStart;   // re-run this instruction once a line is queued
                            return StepResult.NeedsInput;
                        }
                        if (line is null) throw new BasicRuntimeException(4003, "LINE INPUT: end of input stream");
                        var indices = PopIndices(stack, rank);
                        var desc = new InputTargetDesc(depth, slot, IsString: true, rank);
                        AssignInputTarget(ResolveOuter(frame, programFrame, depth), desc, indices, new StringValue(line));
                        _col = 0;
                        break;
                    }
                case Opcode.LineInputFile:
                    {
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var rank = (int)ReadU32(code, ref pc);
                        var channel = ((NumericValue)stack.Pop()).ToInt32();
                        var indices = PopIndices(stack, rank);
                        var file = _channels.Get(channel);
                        var line = file.ReadLine()
                            ?? throw new BasicRuntimeException(7020, $"LINE INPUT #{channel}: end of file");
                        var desc = new InputTargetDesc(depth, slot, IsString: true, rank);
                        AssignInputTarget(ResolveOuter(frame, programFrame, depth), desc, indices, new StringValue(line));
                        break;
                    }
                case Opcode.WriteFile:
                    {
                        var itemCount = (int)ReadU32(code, ref pc);
                        var channel = ((NumericValue)stack.Pop()).ToInt32();
                        var values = new Value[itemCount];
                        for (var i = itemCount - 1; i >= 0; i--) values[i] = stack.Pop();
                        PerformWriteFile(channel, values);
                        break;
                    }
                case Opcode.ReadFile:
                    {
                        var targetCount = (int)ReadU32(code, ref pc);
                        var descs = new InputTargetDesc[targetCount];
                        for (var i = 0; i < targetCount; i++)
                        {
                            descs[i] = new InputTargetDesc(
                                Depth: (int)ReadU32(code, ref pc),
                                Slot: (int)ReadU32(code, ref pc),
                                IsString: ReadU32(code, ref pc) != 0,
                                Rank: (int)ReadU32(code, ref pc));
                        }
                        var channel = ((NumericValue)stack.Pop()).ToInt32();
                        var indices = new int[targetCount][];
                        for (var i = targetCount - 1; i >= 0; i--) indices[i] = PopIndices(stack, descs[i].Rank);
                        PerformReadFile(channel, descs, indices, frame, programFrame);
                        break;
                    }

                case Opcode.PrintUsing:
                    {
                        var itemCount = (int)ReadU32(code, ref pc);
                        var items = new Value[itemCount];
                        for (var i = itemCount - 1; i >= 0; i--) items[i] = stack.Pop();
                        var format = ((StringValue)stack.Pop()).V;
                        var parts = PictureFormat.Parse(format);
                        _out.WriteLine(PictureFormat.Apply(parts, items));
                        _col = 0;
                        break;
                    }

                case Opcode.Read:
                    {
                        var targetCount = (int)ReadU32(code, ref pc);
                        var descs = new InputTargetDesc[targetCount];
                        for (var i = 0; i < targetCount; i++)
                        {
                            descs[i] = new InputTargetDesc(
                                Depth: (int)ReadU32(code, ref pc),
                                Slot: (int)ReadU32(code, ref pc),
                                IsString: ReadU32(code, ref pc) != 0,
                                Rank: (int)ReadU32(code, ref pc));
                        }
                        var indices = new int[targetCount][];
                        for (var i = targetCount - 1; i >= 0; i--)
                        {
                            indices[i] = PopIndices(stack, descs[i].Rank);
                        }
                        for (var i = 0; i < targetCount; i++)
                        {
                            var value = ReadNextDataValue(descs[i].IsString);
                            AssignInputTarget(ResolveOuter(frame, programFrame, descs[i].Depth), descs[i], indices[i], value);
                        }
                        break;
                    }
                case Opcode.Restore:
                    _dataCursor = 0;
                    break;
                case Opcode.MatRead:
                    {
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var isString = ReadU32(code, ref pc) != 0;
                        var arr = ResolveOuter(frame, programFrame, depth).GetOrDefault(slot, NumericValue.Zero);
                        if (arr is not (NumericArrayValue or StringArrayValue))
                            throw new BasicRuntimeException(6004, "MAT READ requires the target to be DIM-ed first");
                        var n = MatOps.BoundsOf(arr)!.Length;
                        if (isString)
                        {
                            var sarr = (StringArrayValue)arr;
                            for (var i = 0; i < n; i++)
                            {
                                if (_dataCursor >= _program.DataPool.Count)
                                    throw new BasicRuntimeException(5001, "MAT READ: DATA pool exhausted");
                                sarr.Data[i] = _program.DataPool[_dataCursor++].Text;
                            }
                        }
                        else
                        {
                            var narr = (NumericArrayValue)arr;
                            for (var i = 0; i < n; i++)
                            {
                                if (_dataCursor >= _program.DataPool.Count)
                                    throw new BasicRuntimeException(5001, "MAT READ: DATA pool exhausted");
                                var item = _program.DataPool[_dataCursor++];
                                if (!BigDecimal.TryParse(item.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var bd))
                                    throw new BasicRuntimeException(5002, $"MAT READ: '{item.Text}' is not numeric");
                                narr[i] = new NumericValue(bd);
                            }
                        }
                        break;
                    }

                case Opcode.MatLoadArray:
                    {
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var f = ResolveOuter(frame, programFrame, depth);
                        var arr = f.GetOrDefault(slot, NumericValue.Zero);
                        // Push a copy, like the tree-walker's MatRhsName: after MAT A = B the
                        // two arrays must not share storage.
                        stack.Push(arr switch
                        {
                            NumericArrayValue n => new NumericArrayValue((BigDecimal[])n.ToDecimals().Clone(), n.Bounds),
                            StringArrayValue s => new StringArrayValue((string[])s.Data.Clone(), s.Bounds),
                            _ => throw new BasicRuntimeException(6004, "MAT operand: array has not been DIM-ed"),
                        });
                        break;
                    }
                case Opcode.MatBinAdd: MatBinaryNumeric(stack, (a, ab, b, bb) => MatOps.ElementWise(a, ab, b, bb, (x, y) => x + y, "+")); break;
                case Opcode.MatBinSub: MatBinaryNumeric(stack, (a, ab, b, bb) => MatOps.ElementWise(a, ab, b, bb, (x, y) => x - y, "-")); break;
                case Opcode.MatBinMul: MatBinaryNumeric(stack, MatOps.Multiply); break;
                case Opcode.MatScalarMul:
                    {
                        var matrix = (NumericArrayValue)stack.Pop();
                        var scalar = ((NumericValue)stack.Pop()).V;
                        stack.Push(new NumericArrayValue(MatOps.ScalarMultiply(scalar, matrix.ToDecimals()), matrix.Bounds));
                        break;
                    }
                case Opcode.MatTrn:
                    {
                        var m = (NumericArrayValue)stack.Pop();
                        var (data, bounds) = MatOps.Transpose(m.ToDecimals(), m.Bounds);
                        stack.Push(new NumericArrayValue(data, bounds));
                        break;
                    }
                case Opcode.MatInv:
                    {
                        var m = (NumericArrayValue)stack.Pop();
                        stack.Push(new NumericArrayValue(MatOps.Inverse(m.ToDecimals(), m.Bounds), m.Bounds));
                        break;
                    }
                case Opcode.MatAssign:
                    {
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var isString = ReadU32(code, ref pc) != 0;
                        var newArr = stack.Pop();
                        if (isString && newArr is not StringArrayValue)
                            throw new BasicRuntimeException(0, "MAT assign: RHS is not a string array");
                        if (!isString && newArr is not NumericArrayValue)
                            throw new BasicRuntimeException(0, "MAT assign: RHS is not a numeric array");
                        // MAT kernels compute in decimal; store the result in the program's representation.
                        if (newArr is NumericArrayValue n) newArr = NumericArrayValue.FromDecimals(n.ToDecimals(), n.Bounds, _native);
                        ResolveOuter(frame, programFrame, depth).Set(slot, newArr);
                        break;
                    }
                case Opcode.MatAssignConst:
                    {
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var isString = ReadU32(code, ref pc) != 0;
                        var kind = ReadU32(code, ref pc);
                        var target = ResolveOuter(frame, programFrame, depth);
                        var current = target.GetOrDefault(slot, NumericValue.Zero);
                        var bounds = MatOps.BoundsOf(current)
                            ?? throw new BasicRuntimeException(6004,
                                "MAT constant rhs requires the target to be DIM-ed first");
                        target.Set(slot, BuildMatConst(kind, isString, bounds));
                        break;
                    }
                case Opcode.MatPushConst:
                    {
                        // Same as MatAssignConst but pushes the constant array
                        // onto the operand stack (so it can participate in a
                        // bigger MatRhs expression — e.g. `MAT C = ZER + B`).
                        // Reads the target's current bounds to know the shape.
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var isString = ReadU32(code, ref pc) != 0;
                        var kind = ReadU32(code, ref pc);
                        var target = ResolveOuter(frame, programFrame, depth);
                        var current = target.GetOrDefault(slot, NumericValue.Zero);
                        var bounds = MatOps.BoundsOf(current)
                            ?? throw new BasicRuntimeException(6004,
                                "MAT constant rhs requires the target to be DIM-ed first");
                        stack.Push(BuildMatConst(kind, isString, bounds));
                        break;
                    }
                case Opcode.MatRedim:
                    {
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var rank = (int)ReadU32(code, ref pc);
                        var isString = ReadU32(code, ref pc) != 0;
                        var lower = new int[rank];
                        var upper = new int[rank];
                        for (var i = rank - 1; i >= 0; i--)
                        {
                            upper[i] = ((NumericValue)stack.Pop()).ToInt32();
                            lower[i] = ((NumericValue)stack.Pop()).ToInt32();
                            if (upper[i] < lower[i])
                                throw new BasicRuntimeException(6001,
                                    $"MAT REDIM: upper bound {upper[i]} less than lower bound {lower[i]}");
                        }
                        var newBounds = new Bounds(lower, upper);
                        var target = ResolveOuter(frame, programFrame, depth);
                        var current = target.GetOrDefault(slot, NumericValue.Zero);
                        if (isString)
                        {
                            var newData = FillStrings(newBounds.Length, "");
                            if (current is StringArrayValue oldS) MatOps.PreserveStringElements(oldS, newData, newBounds);
                            target.Set(slot, new StringArrayValue(newData, newBounds));
                        }
                        else
                        {
                            var fresh = NumericArrayValue.Create(newBounds, _native);
                            if (current is NumericArrayValue oldN) MatOps.PreserveNumericElements(oldN, fresh);
                            target.Set(slot, fresh);
                        }
                        break;
                    }
                case Opcode.MatPrint:
                    {
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var arr = ResolveOuter(frame, programFrame, depth).GetOrDefault(slot, NumericValue.Zero);
                        if (arr is NumericArrayValue narr) MatOps.PrintMatrix(_out, narr.ToDecimals(), narr.Bounds, FormatNumeric);
                        else if (arr is StringArrayValue sarr) MatOps.PrintMatrix(_out, sarr.Data, sarr.Bounds, s => s);
                        else throw new BasicRuntimeException(6004, "MAT PRINT requires the target to be DIM-ed first");
                        _col = 0;
                        break;
                    }
                case Opcode.MatInput:
                    {
                        var opStart = pc - 1;
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var isString = ReadU32(code, ref pc) != 0;
                        var arr = ResolveOuter(frame, programFrame, depth).GetOrDefault(slot, NumericValue.Zero);
                        if (arr is not (NumericArrayValue or StringArrayValue))
                            throw new BasicRuntimeException(6004, "MAT INPUT requires the target to be DIM-ed first");
                        if (!TryCollectMatInput(MatOps.BoundsOf(arr)!.Length, out var values))
                        {
                            cf.Pc = opStart;   // the values so far wait in _matInputValues
                            return StepResult.NeedsInput;
                        }
                        AssignMatInput(arr, isString, values);
                        _col = 0;
                        break;
                    }

                case Opcode.Input:
                    {
                        var opStart = pc - 1;
                        var suppressQuestionMark = ReadU32(code, ref pc) != 0;
                        var targetCount = (int)ReadU32(code, ref pc);
                        var descs = new InputTargetDesc[targetCount];
                        for (var i = 0; i < targetCount; i++)
                        {
                            descs[i] = new InputTargetDesc(
                                Depth: (int)ReadU32(code, ref pc),
                                Slot: (int)ReadU32(code, ref pc),
                                IsString: ReadU32(code, ref pc) != 0,
                                Rank: (int)ReadU32(code, ref pc));
                        }
                        // Prompt text (if any) was already emitted via PrintString
                        // before this opcode; this adds the "? " (or " ").
                        if (!TryReadInputLine(suppressQuestionMark ? " " : "? ", out var rawLine))
                        {
                            cf.Pc = opStart;   // re-run this instruction once a line is queued
                            return StepResult.NeedsInput;
                        }
                        if (rawLine is null) throw new BasicRuntimeException(4003, "INPUT: end of input stream");
                        var parsed = ParseInputReply(rawLine, descs);
                        if (parsed is null)
                        {
                            pc = opStart;      // "redo from start" printed: prompt and read again
                            break;
                        }
                        // Subscripts were pushed in target order; pop in reverse so
                        // we end up with one int[] per target keyed by declaration index.
                        var indices = new int[targetCount][];
                        for (var i = targetCount - 1; i >= 0; i--)
                        {
                            indices[i] = PopIndices(stack, descs[i].Rank);
                        }
                        for (var i = 0; i < targetCount; i++)
                            AssignInputTarget(ResolveOuter(frame, programFrame, descs[i].Depth), descs[i], indices[i], parsed[i]);
                        _col = 0;   // the reply ended the line
                        break;
                    }

                case Opcode.DimArray:
                    {
                        var slot = (int)ReadU32(code, ref pc);
                        var rank = (int)ReadU32(code, ref pc);
                        var isString = ReadU32(code, ref pc) != 0;
                        AllocArray(stack, frame, slot, rank, isString, _native);
                        break;
                    }
                case Opcode.DimArrayOuter:
                    {
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var rank = (int)ReadU32(code, ref pc);
                        var isString = ReadU32(code, ref pc) != 0;
                        AllocArray(stack, ResolveOuter(frame, programFrame, depth), slot, rank, isString, _native);
                        break;
                    }
                case Opcode.LoadElement:
                    {
                        var slot = (int)ReadU32(code, ref pc);
                        var rank = (int)ReadU32(code, ref pc);
                        PushElement(stack, frame, slot, rank);
                        break;
                    }
                case Opcode.LoadElementOuter:
                    {
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var rank = (int)ReadU32(code, ref pc);
                        PushElement(stack, ResolveOuter(frame, programFrame, depth), slot, rank);
                        break;
                    }
                case Opcode.StoreElement:
                    {
                        var slot = (int)ReadU32(code, ref pc);
                        var rank = (int)ReadU32(code, ref pc);
                        WriteElement(stack, frame, slot, rank);
                        break;
                    }
                case Opcode.StoreElementOuter:
                    {
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var rank = (int)ReadU32(code, ref pc);
                        WriteElement(stack, ResolveOuter(frame, programFrame, depth), slot, rank);
                        break;
                    }

                case Opcode.LoadConstantPi:
                    stack.Push(BuiltinImpls.EvalConstant("PI", _native));
                    break;
                case Opcode.LoadConstantEps:
                    stack.Push(BuiltinImpls.EvalConstant("EPS", _native));
                    break;
                case Opcode.LoadConstantInf:
                    stack.Push(BuiltinImpls.EvalConstant("INF", _native));
                    break;
                case Opcode.LoadConstantMaxnum:
                    stack.Push(BuiltinImpls.EvalConstant("MAXNUM", _native));
                    break;

                default:
                    throw new BasicRuntimeException(0, $"unimplemented opcode {op}");
            }
        }
    }

    /// <summary>Build a numeric or string constant array matching the
    /// given <paramref name="bounds"/>. Kind values match Parser.Ast.MatConstKind:
    /// 0=Identity (IDN), 1=Zeros (ZER), 2=Ones (CON), 3=NullString (NUL$).</summary>
    private Value BuildMatConst(uint kind, bool isString, Bounds bounds)
    {
        return (kind, isString) switch
        {
            (0u, false) => NumericArrayValue.FromDecimals(MatOps.Identity(bounds), bounds, _native),
            (1u, false) => NumericArrayValue.Create(bounds, _native),
            (2u, false) => NumericArrayValue.FromDecimals(MatOps.Fill(bounds, BigDecimal.One), bounds, _native),
            (3u, true) => new StringArrayValue(FillStrings(bounds.Length, ""), bounds),
            _ => throw new BasicRuntimeException(0,
                $"MAT constant kind {kind} not valid for {(isString ? "string" : "numeric")} target"),
        };
    }

    private static void MatBinaryNumeric(
        OperandStack stack,
        Func<BigDecimal[], Bounds, BigDecimal[], Bounds, (BigDecimal[] Data, Bounds Bounds)> op)
    {
        var r = (NumericArrayValue)stack.Pop();
        var l = (NumericArrayValue)stack.Pop();
        var (data, bounds) = op(l.ToDecimals(), l.Bounds, r.ToDecimals(), r.Bounds);
        stack.Push(new NumericArrayValue(data, bounds));
    }

    private static string[] FillStrings(int length, string value)
    {
        var arr = new string[length];
        for (var i = 0; i < length; i++) arr[i] = value;
        return arr;
    }

    /// <summary>MAT INPUT's values read so far, kept across pauses for input.</summary>
    private List<string>? _matInputValues;

    /// <summary>Collect MAT INPUT's <paramref name="n"/> comma-separated values, prompting
    /// "? " per line (mirrors BasicInterpreter.ExecMatInput). False if a non-blocking
    /// reader runs dry first — the values so far are kept for the retry.</summary>
    private bool TryCollectMatInput(int n, out List<string> values)
    {
        values = _matInputValues ??= new List<string>();
        while (values.Count < n)
        {
            if (!TryReadInputLine("? ", out var line)) return false;
            if (line is null)
            {
                _matInputValues = null;
                throw new BasicRuntimeException(6005, "MAT INPUT: end of input");
            }
            foreach (var part in line.Split(','))
            {
                var t = part.Trim();
                if (t.Length > 0) values.Add(t);
                if (values.Count == n) break;
            }
        }
        _matInputValues = null;
        return true;
    }

    /// <summary>Store MAT INPUT's values; errors line up with the tree-walker (4002 non-numeric).</summary>
    private static void AssignMatInput(Value arr, bool isString, List<string> values)
    {
        var n = values.Count;
        if (isString)
        {
            var sarr = (StringArrayValue)arr;
            for (var i = 0; i < n; i++) sarr.Data[i] = values[i];
        }
        else
        {
            var narr = (NumericArrayValue)arr;
            for (var i = 0; i < n; i++)
            {
                if (!BigDecimal.TryParse(values[i], NumberStyles.Any, CultureInfo.InvariantCulture, out var bd))
                    throw new BasicRuntimeException(4002, $"MAT INPUT: '{values[i]}' is not numeric");
                narr[i] = new NumericValue(bd);
            }
        }
    }

    /// <summary>The prompt has been shown for the line now awaited, so a resumed
    /// INPUT doesn't print it twice.</summary>
    private bool _promptShown;

    /// <summary>Non-null when the input reader can report "no line yet" instead of blocking.</summary>
    private readonly INonBlockingLineReader? _lineReader;

    /// <summary>Print <paramref name="prompt"/> (once per line wanted) and read a line.
    /// False if a non-blocking reader has none yet; true with null at end of input.</summary>
    private bool TryReadInputLine(string prompt, out string? line)
    {
        if (!_promptShown)
        {
            _out.Write(prompt);
            _out.Flush();
            _promptShown = true;
        }
        if (_lineReader is not null)
        {
            if (!_lineReader.TryReadLine(out line)) return false;
        }
        else
        {
            line = _in.ReadLine();
        }
        _promptShown = false;
        return true;
    }

    // -- File-I/O helpers (mirror BasicInterpreter.File.cs) ---------------

    private void OpenChannel(int channel, string path, uint access, uint create, uint rectype)
    {
        // Access values: 0=Default, 1=Input, 2=Output, 3=Outin (match Parser.Ast.OpenAccess).
        // Create values: 0=Default, 1=New, 2=Old, 3=NewOld (match Parser.Ast.OpenCreate).
        var fileAccess = access switch
        {
            1u => FileAccess.Read,
            2u => FileAccess.Write,
            3u => FileAccess.ReadWrite,
            _ => FileAccess.ReadWrite,
        };
        var mode = create switch
        {
            1u => FileMode.CreateNew,
            2u => FileMode.Open,
            3u => FileMode.OpenOrCreate,
            _ => access == 1u ? FileMode.Open : FileMode.OpenOrCreate,
        };
        // ACCESS OUTPUT with no explicit CREATE truncates on open per spec.
        if (access == 2u && create == 0u) mode = FileMode.Create;
        try
        {
            // RecType values: 0=Default, 1=Display, 2=Internal (match Parser.Ast.OpenRecType).
            var file = new DisplayFile(path, mode, fileAccess) { IsInternal = rectype == 2u };
            _channels.Open(channel, file);
        }
        catch (FileNotFoundException ex)
        {
            throw new BasicRuntimeException(7010, $"file '{path}' not found: {ex.Message}");
        }
        catch (IOException ex)
        {
            throw new BasicRuntimeException(7011, $"OPEN failed for '{path}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new BasicRuntimeException(7012, $"OPEN denied for '{path}': {ex.Message}");
        }
    }

    private void PerformPrintFile(int channel, uint[] kinds, Value[] stackValues)
    {
        var file = _channels.Get(channel);
        var sb = new StringBuilder();
        var col = 0;
        var suppressNewline = false;
        var nextStackItem = 0;
        for (var i = 0; i < kinds.Length; i++)
        {
            switch (kinds[i])
            {
                case 0u: // ExprNumeric
                case 1u: // ExprString
                    {
                        var v = stackValues[nextStackItem++];
                        var text = v switch
                        {
                            StringValue s => s.V,
                            NumericValue n => FormatNumeric(n.V),
                            _ => v.ToString() ?? string.Empty,
                        };
                        sb.Append(text);
                        col += text.Length;
                        suppressNewline = false;
                        break;
                    }
                case 2u: // Comma → zone pad
                    {
                        var next = ((col / DefaultZoneWidth) + 1) * DefaultZoneWidth;
                        sb.Append(' ', next - col);
                        col = next;
                        suppressNewline = i == kinds.Length - 1;
                        break;
                    }
                case 3u: // Semicolon → no padding; suppresses trailing newline if last
                    suppressNewline = i == kinds.Length - 1;
                    break;
                case 4u: // Tab(n) → pad spaces to 1-based column n (no-op if already past)
                    {
                        var target = ((NumericValue)stackValues[nextStackItem++]).ToInt32() - 1;
                        if (target < 0) target = 0;
                        if (target > col)
                        {
                            sb.Append(' ', target - col);
                            col = target;
                        }
                        suppressNewline = false;
                        break;
                    }
            }
        }
        if (suppressNewline) file.Write(sb.ToString());
        else file.WriteLine(sb.ToString());
    }

    private void PerformInputFile(
        int channel,
        InputTargetDesc[] descs,
        int[][] indices,
        ActivationRecord frame,
        ActivationRecord programFrame)
    {
        var file = _channels.Get(channel);
        var line = file.ReadLine()
            ?? throw new BasicRuntimeException(7020, $"INPUT #{channel}: end of file");
        var fields = line.Split(',');
        if (fields.Length < descs.Length)
            throw new BasicRuntimeException(7021,
                $"INPUT #{channel}: line had {fields.Length} field(s), expected {descs.Length}");

        for (var i = 0; i < descs.Length; i++)
        {
            var raw = fields[i].Trim();
            Value v;
            if (descs[i].IsString)
            {
                if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
                    raw = raw[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
                v = new StringValue(raw);
            }
            else
            {
                if (!BigDecimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var bd))
                    throw new BasicRuntimeException(7022, $"INPUT #{channel}: '{raw}' is not numeric");
                v = NumericValue.From(bd, _native);
            }
            AssignInputTarget(ResolveOuter(frame, programFrame, descs[i].Depth), descs[i], indices[i], v);
        }
    }

    // -- INTERNAL (exact-value) records: WRITE # / READ # (mirror the interpreter) --

    private void PerformWriteFile(int channel, Value[] values)
    {
        var file = _channels.Get(channel);
        if (!file.IsInternal)
            throw new BasicRuntimeException(7030, $"WRITE #{channel}: channel is not open RECTYPE INTERNAL");
        foreach (var v in values) file.WriteLine(FormatInternal(v));
    }

    private void PerformReadFile(int channel, InputTargetDesc[] descs, int[][] indices,
        ActivationRecord frame, ActivationRecord programFrame)
    {
        var file = _channels.Get(channel);
        if (!file.IsInternal)
            throw new BasicRuntimeException(7030, $"READ #{channel}: channel is not open RECTYPE INTERNAL");
        for (var i = 0; i < descs.Length; i++)
        {
            var line = file.ReadLine()
                ?? throw new BasicRuntimeException(7020, $"READ #{channel}: end of file");
            Value v;
            if (descs[i].IsString)
                v = new StringValue(line);
            else if (BigDecimal.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out var bd))
                v = NumericValue.From(bd, _native);
            else
                throw new BasicRuntimeException(7022, $"READ #{channel}: '{line}' is not numeric");
            AssignInputTarget(ResolveOuter(frame, programFrame, descs[i].Depth), descs[i], indices[i], v);
        }
    }

    private static string FormatInternal(Value v) => v switch
    {
        NumericValue n => n.V.ToString(CultureInfo.InvariantCulture),
        StringValue s => s.V,
        _ => throw new BasicRuntimeException(7031, "WRITE #: unsupported value type"),
    };

    private Value ReadNextDataValue(bool isString)
    {
        if (_dataCursor >= _program.DataPool.Count)
            throw new BasicRuntimeException(5001, "READ: DATA pool exhausted");
        var item = _program.DataPool[_dataCursor++];
        if (isString) return new StringValue(item.Text);
        if (!BigDecimal.TryParse(item.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var bd))
            throw new BasicRuntimeException(5002, $"READ: data item '{item.Text}' is not numeric");
        return NumericValue.From(bd, _native);
    }

    private readonly record struct InputTargetDesc(int Depth, int Slot, bool IsString, int Rank);

    private static InputTargetDesc[] ReadTargetDescs(byte[] code, ref int pc, int count)
    {
        var descs = new InputTargetDesc[count];
        for (var i = 0; i < count; i++)
        {
            descs[i] = new InputTargetDesc(
                Depth: (int)ReadU32(code, ref pc),
                Slot: (int)ReadU32(code, ref pc),
                IsString: ReadU32(code, ref pc) != 0,
                Rank: (int)ReadU32(code, ref pc));
        }
        return descs;
    }

    /// <summary>Pop the targets' subscripts (pushed in target order) and assign each its value.</summary>
    private static void AssignTargets(InputTargetDesc[] descs, Value[] values, OperandStack stack,
        ActivationRecord frame, ActivationRecord programFrame)
    {
        var indices = new int[descs.Length][];
        for (var i = descs.Length - 1; i >= 0; i--) indices[i] = PopIndices(stack, descs[i].Rank);
        for (var i = 0; i < descs.Length; i++)
            AssignInputTarget(ResolveOuter(frame, programFrame, descs[i].Depth), descs[i], indices[i], values[i]);
    }

    /// <summary>Split and convert an INPUT reply, one value per target. A short or
    /// non-numeric reply prints the "redo from start" message and returns null.
    /// Mirrors BasicInterpreter.ExecInput: same messages, so the engines agree.</summary>
    private Value[]? ParseInputReply(string rawLine, InputTargetDesc[] descs)
    {
        var fields = rawLine.Split(',');
        if (fields.Length < descs.Length)
        {
            _out.WriteLine("Not enough data — redo from start.");
            return null;
        }

        var parsed = new Value[descs.Length];
        for (var i = 0; i < descs.Length; i++)
        {
            var raw = fields[i].Trim();
            if (descs[i].IsString)
            {
                parsed[i] = new StringValue(raw);
            }
            else if (BigDecimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var bd))
            {
                parsed[i] = NumericValue.From(bd, _native);
            }
            else
            {
                _out.WriteLine($"'{raw}' is not numeric — redo from start.");
                return null;
            }
        }
        return parsed;
    }

    private static void AssignInputTarget(ActivationRecord target, InputTargetDesc desc, int[] indices, Value value)
    {
        if (desc.Rank == 0)
        {
            target.Set(desc.Slot, value);
            return;
        }
        var arr = target.GetOrDefault(desc.Slot, NumericValue.Zero);
        try
        {
            if (arr is NumericArrayValue narr) { narr[narr.Bounds.IndexOf(indices)] = (NumericValue)value; return; }
            if (arr is StringArrayValue sarr) { sarr.Data[sarr.Bounds.IndexOf(indices)] = ((StringValue)value).V; return; }
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new BasicRuntimeException(1002, "INPUT array subscript: " + ex.Message);
        }
        catch (ArgumentException ex)
        {
            throw new BasicRuntimeException(1003, "INPUT array subscript: " + ex.Message);
        }
        throw new BasicRuntimeException(0, "INPUT into array not allocated; missing DIM");
    }

    private static ActivationRecord ResolveOuter(ActivationRecord frame, ActivationRecord programFrame, int depth)
    {
        var f = frame;
        for (var i = 0; i < depth && f is not null; i++) f = f.Parent!;
        return f ?? programFrame;
    }

    private static void AllocArray(OperandStack stack, ActivationRecord target, int slot, int rank, bool isString, bool native)
    {
        var lower = new int[rank];
        var upper = new int[rank];
        // Bounds were pushed left-to-right per dimension: lo_0, hi_0, lo_1, hi_1, ...
        // Popping reverses, so iterate from the highest dim downward.
        for (var i = rank - 1; i >= 0; i--)
        {
            upper[i] = ((NumericValue)stack.Pop()).ToInt32();
            lower[i] = ((NumericValue)stack.Pop()).ToInt32();
            if (upper[i] < lower[i])
                throw new BasicRuntimeException(6001,
                    $"DIM: upper bound {upper[i]} less than lower bound {lower[i]}");
        }
        var bounds = new Bounds(lower, upper);
        Value array = isString
            ? new StringArrayValue(new string[bounds.Length], bounds)
            : NumericArrayValue.Create(bounds, native);
        target.Set(slot, array);
    }

    /// <summary>Pop the subscripts and push the element (a native one unboxed).</summary>
    private static void PushElement(OperandStack stack, ActivationRecord frame, int slot, int rank)
    {
        Span<int> indices = stackalloc int[rank];
        for (var i = rank - 1; i >= 0; i--) indices[i] = stack.PopInt32();
        var arr = frame.GetOrDefault(slot, NumericValue.Zero);
        try
        {
            if (arr is NumericArrayValue narr)
            {
                var at = narr.Bounds.IndexOf(indices);
                if (narr.IsNative) stack.PushNumber(narr.GetNative(at));
                else stack.Push(narr[at]);
                return;
            }
            if (arr is StringArrayValue sarr)
            {
                stack.Push(new StringValue(sarr.Data[sarr.Bounds.IndexOf(indices)] ?? ""));
                return;
            }
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new BasicRuntimeException(1002, "array subscript: " + ex.Message);
        }
        catch (ArgumentException ex)
        {
            throw new BasicRuntimeException(1003, "array subscript: " + ex.Message);
        }
        throw new BasicRuntimeException(0, "array not allocated; missing DIM");
    }

    private static void WriteElement(OperandStack stack, ActivationRecord frame, int slot, int rank)
    {
        Span<int> indices = stackalloc int[rank];
        for (var i = rank - 1; i >= 0; i--) indices[i] = stack.PopInt32();
        var arr = frame.GetOrDefault(slot, NumericValue.Zero);
        try
        {
            if (arr is NumericArrayValue narr)
            {
                var at = narr.Bounds.IndexOf(indices);
                if (narr.IsNative && stack.TryPopUnboxed(out var n)) narr.SetNative(at, n);
                else narr[at] = (NumericValue)stack.Pop();
                return;
            }
            if (arr is StringArrayValue sarr) { sarr.Data[sarr.Bounds.IndexOf(indices)] = ((StringValue)stack.Pop()).V; return; }
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new BasicRuntimeException(1002, "array subscript: " + ex.Message);
        }
        catch (ArgumentException ex)
        {
            throw new BasicRuntimeException(1003, "array subscript: " + ex.Message);
        }
        throw new BasicRuntimeException(0, "array not allocated; missing DIM");
    }

    private static int[] PopIndices(OperandStack stack, int rank)
    {
        var indices = new int[rank];
        for (var i = rank - 1; i >= 0; i--) indices[i] = stack.PopInt32();
        return indices;
    }

    private static uint ReadU32(byte[] code, ref int pc)
    {
        var v = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(pc, 4));
        pc += 4;
        return v;
    }

    private static int ReadI32(byte[] code, ref int pc) => (int)ReadU32(code, ref pc);

    private ChunkRuntime RuntimeOf(Chunk chunk)
    {
        if (!_chunks.TryGetValue(chunk, out var rt))
        {
            rt = new ChunkRuntime(chunk.ToArray(), chunk.Numbers.Select(n => NumericValue.From(n, _native)).ToArray());
            _chunks[chunk] = rt;
        }
        return rt;
    }

    /// <summary>A numeric builtin's result, unless it isn't finite (BuiltinImpls' 1003).</summary>
    private static double RealResult(string fn, double r) =>
        double.IsFinite(r) ? r : throw new BasicRuntimeException(1003, $"{fn}: result overflows");

    /// <summary>A native result, unless it overflowed (Numbers.Native's exception 1002).</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static double Finite(double d) =>
        double.IsFinite(d) ? d : throw new BasicRuntimeException(1002, "numeric overflow");

    private static void Arithmetic(OperandStack stack, Func<NumericValue, NumericValue, NumericValue> op)
    {
        var b = (NumericValue)stack.Pop();
        var a = (NumericValue)stack.Pop();
        stack.Push(op(a, b));
    }

    private void Compare(OperandStack stack, Func<int, bool> holds)
    {
        var b = stack.Pop();
        var a = stack.Pop();
        var cmp = (a, b) switch
        {
            (NumericValue x, NumericValue y) => Numbers.Compare(x, y),
            (StringValue x, StringValue y) => string.CompareOrdinal(x.V, y.V),
            _ => throw new BasicRuntimeException(0, "type mismatch in comparison"),
        };
        stack.Push(NumericValue.Bool(holds(cmp), _native));
    }

    /// <summary>AND/OR/XOR/IMP/EQV over the operands' truth (nonzero = true).</summary>
    private void Logical(OperandStack stack, Func<bool, bool, bool> op)
    {
        var b = !((NumericValue)stack.Pop()).IsZero;
        var a = !((NumericValue)stack.Pop()).IsZero;
        stack.Push(NumericValue.Bool(op(a, b), _native));
    }

    private void Bitwise(OperandStack stack, Func<long, long, long> op)
    {
        var b = ((NumericValue)stack.Pop()).ToInt64();
        var a = ((NumericValue)stack.Pop()).ToInt64();
        stack.Push(NumericValue.From(op(a, b), _native));
    }

    private static string FormatNumeric(BigDecimal x) => DisplayFormat.FormatNumeric(x);

    /// <summary>After SOUND, BEEP or PLAY: a foreground statement waits for its audio, and
    /// background music that has overfilled GW-BASIC's buffer waits for room (a device that
    /// can't block says so through <see cref="IAudioQueue"/>; one that can blocks in Emit).</summary>
    private StepResult? AudioWait()
    {
        if (_audioPort.TakeDrainRequest()) return StepResult.AwaitingAudio;
        if (_audioQueue is { Queued: > AudioState.BackgroundCapacity }) return StepResult.AudioQueueFull;
        return null;
    }

    /// <summary>Forwards tones to the real device, but records a foreground statement's
    /// <c>Flush()</c> — AudioState's "wait until it has played" — instead of blocking in
    /// it, so the VM can pause with <see cref="StepStatus.AwaitingAudio"/> and leave
    /// the waiting to the host.</summary>
    private sealed class DeferredDrainAudio(IAudioDevice device) : IAudioDevice
    {
        private bool _drainRequested;

        public void Emit(ToneEvent tone) => device.Emit(tone);

        public void Flush() => _drainRequested = true;

        public bool TakeDrainRequest()
        {
            var requested = _drainRequested;
            _drainRequested = false;
            return requested;
        }
    }
}
