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

    /// <summary>PC at which CONTINUE should resume — the start of the statement immediately after the one that raised. Set by the exception-dispatch catch from the chunk-local stmtEndPc.</summary>
    private int _currentContinuePc;

    private readonly record struct HandlerFrame(int UsePc, int StackBaseline);

    private readonly IGraphicsDevice _graphics;
    private readonly GraphicsState _gfx = new();
    private readonly IKeyboard _keyboard;
    private readonly IAudioDevice _audio;
    private readonly AudioState _audioState = new();

    public BasicVm(BcProgram program, TextWriter @out, TextReader @in,
        IGraphicsDevice? graphics = null, IKeyboard? keyboard = null,
        IAudioDevice? audio = null)
    {
        _program = program;
        _out = @out;
        _in = @in;
        _graphics = graphics ?? NullGraphicsDevice.Instance;
        _keyboard = keyboard ?? NullKeyboard.Instance;
        _audio = audio ?? NullAudioDevice.Instance;
    }

    public int Run()
    {
        try
        {
            _dataCursor = 0;
            var programFrame = new ActivationRecord(_program.Main.FrameSize, parent: null);
            ExecuteChunk(_program.Main, programFrame, programFrame);
            return 0;
        }
        catch (BasicRuntimeException ex)
        {
            _out.Flush();
            // Match the tree-walker's "unhandled exception" format so the two
            // engines produce identical stderr for an unhandled runtime error.
            // _currentLine is the most recent LineNote, which matches the
            // failing statement when the exception originates in user code.
            Console.Error.WriteLine(
                $"unhandled exception type {ex.TypeCode} at line {_currentLine}: {ex.Message}");
            return 1;
        }
        finally
        {
            _channels.Dispose();
        }
    }

    /// <summary>Returns true if the chunk exited via End/Stop (program halts).</summary>
    private bool ExecuteChunk(Chunk chunk, ActivationRecord frame, ActivationRecord programFrame)
    {
        var code = chunk.Code;
        var stack = new Stack<Value>(64);
        var pc = 0;
        var col = 0; // current PRINT column for zone padding
        var pendingNewline = false;
        // Handlers pushed below this depth belong to an enclosing chunk — if a
        // BasicRuntimeException is thrown here and the topmost handler isn't ours,
        // we rethrow to let the caller's loop dispatch it.
        var entryHandlerDepth = _handlerStack.Count;
        // PC of the next statement after the one currently executing. Updated
        // by LineNote, snapshotted into _currentContinuePc on exception dispatch.
        // Kept chunk-local so a CALL into a SUB doesn't clobber the outer
        // chunk's view of where CONTINUE should resume.
        var stmtEndPc = 0;
        // GOSUB return-PC stack — local to this chunk. Pushed by GosubFlow,
        // popped by Return. GOSUB/RETURN never cross chunk boundaries.
        var gosubStack = new Stack<int>();

        while (pc < code.Count)
        {
            try
            {
            var op = (Opcode)code[pc++];
            switch (op)
            {
                case Opcode.Halt:
                case Opcode.Stop:
                case Opcode.End:
                    while (_handlerStack.Count > entryHandlerDepth) _handlerStack.Pop();
                    return true;
                case Opcode.Nop: break;
                case Opcode.Sleep:
                    {
                        // Frame boundary: present what's drawn, then pause.
                        _graphics.Flush();
                        var secs = (double)((NumericValue)stack.Pop()).V;
                        if (secs > 0)
                            System.Threading.Thread.Sleep((int)Math.Min(secs * 1000.0, int.MaxValue));
                        break;
                    }

                // -- Audio (SOUND/BEEP/PLAY): drive the same AudioState as the tree-walker --
                case Opcode.Sound:
                    {
                        _graphics.Flush();
                        var dur = (double)((NumericValue)stack.Pop()).V;   // pushed last → on top
                        var freq = (double)((NumericValue)stack.Pop()).V;
                        _audioState.EmitSound(freq, dur, _audio);
                        break;
                    }
                case Opcode.Beep:
                    _graphics.Flush();
                    _audioState.EmitBeep(_audio);
                    break;
                case Opcode.Play:
                    {
                        _graphics.Flush();
                        var notes = ((StringValue)stack.Pop()).V;
                        _audioState.EmitPlay(notes, _audio);
                        break;
                    }

                // -- Graphics (§13): drive the same GraphicsState as the tree-walker --
                case Opcode.GfxSetBounds:
                    {
                        var kind = (int)ReadU32(code, ref pc);
                        var t = GraphicsState.ToCoord(((NumericValue)stack.Pop()).V);
                        var b = GraphicsState.ToCoord(((NumericValue)stack.Pop()).V);
                        var r = GraphicsState.ToCoord(((NumericValue)stack.Pop()).V);
                        var l = GraphicsState.ToCoord(((NumericValue)stack.Pop()).V);
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
                        var n = GraphicsState.ToIndex(((NumericValue)stack.Pop()).V);
                        if (prim == 0) { _gfx.PointStyle = n; _graphics.SetPointStyle(n); }
                        else { _gfx.LineStyle = n; _graphics.SetLineStyle(n); }
                        break;
                    }
                case Opcode.GfxSetColor:
                    {
                        var tgt = (GfxColorTarget)(int)ReadU32(code, ref pc);
                        var n = GraphicsState.ToIndex(((NumericValue)stack.Pop()).V);
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
                            var y = GraphicsState.ToCoord(((NumericValue)stack.Pop()).V);
                            var x = GraphicsState.ToCoord(((NumericValue)stack.Pop()).V);
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
                        var ay = GraphicsState.ToCoord(((NumericValue)stack.Pop()).V);
                        var ax = GraphicsState.ToCoord(((NumericValue)stack.Pop()).V);
                        _gfx.EmitText(new GfxPoint(ax, ay), text, _graphics);
                        break;
                    }
                case Opcode.GfxAskValue:
                    {
                        var q = (GfxQuery)(int)ReadU32(code, ref pc);
                        var index = (int)ReadU32(code, ref pc);
                        stack.Push(_gfx.Query(q, index, _graphics));
                        break;
                    }

                case Opcode.Pop: stack.Pop(); break;
                case Opcode.Dup: stack.Push(stack.Peek()); break;
                case Opcode.Swap:
                    var s1 = stack.Pop();
                    var s2 = stack.Pop();
                    stack.Push(s1); stack.Push(s2);
                    break;

                case Opcode.LoadConstNumber:
                    stack.Push(new NumericValue(chunk.Numbers[(int)ReadU32(code, ref pc)]));
                    break;
                case Opcode.LoadConstString:
                    stack.Push(new StringValue(chunk.Strings[(int)ReadU32(code, ref pc)]));
                    break;
                case Opcode.LoadZero: stack.Push(NumericValue.Zero); break;
                case Opcode.LoadOne: stack.Push(NumericValue.One); break;
                case Opcode.LoadMinusOne: stack.Push(NumericValue.MinusOne); break;

                case Opcode.LoadLocal:
                    {
                        var slot = (int)ReadU32(code, ref pc);
                        stack.Push(frame.GetOrDefault(slot, NumericValue.Zero));
                        break;
                    }
                case Opcode.StoreLocal:
                    {
                        var slot = (int)ReadU32(code, ref pc);
                        frame.Set(slot, stack.Pop());
                        break;
                    }
                case Opcode.LoadOuter:
                    {
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var f = frame;
                        for (var i = 0; i < depth && f is not null; i++) f = f.Parent;
                        f ??= programFrame;
                        stack.Push(f.GetOrDefault(slot, NumericValue.Zero));
                        break;
                    }
                case Opcode.StoreOuter:
                    {
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var f = frame;
                        for (var i = 0; i < depth && f is not null; i++) f = f.Parent;
                        f ??= programFrame;
                        f.Set(slot, stack.Pop());
                        break;
                    }

                case Opcode.Add: BinaryNumeric(stack, Numbers.Add); break;
                case Opcode.Sub: BinaryNumeric(stack, Numbers.Subtract); break;
                case Opcode.Mul: BinaryNumeric(stack, Numbers.Multiply); break;
                case Opcode.Div: BinaryNumeric(stack, (a, b) =>
                {
                    if (b == BigDecimal.Zero) throw new BasicRuntimeException(1001, "division by zero");
                    return BigDecimal.Divide(a, b, 30, RoundingMode.MidpointToEven);
                }); break;
                case Opcode.Pow: BinaryNumeric(stack, Pow); break;
                case Opcode.Mod: BinaryNumeric(stack, (a, b) =>
                {
                    if (b == BigDecimal.Zero) throw new BasicRuntimeException(1001, "MOD by zero");
                    return a - BigDecimal.Floor(a / b) * b;
                }); break;
                case Opcode.Rem: BinaryNumeric(stack, (a, b) =>
                {
                    if (b == BigDecimal.Zero) throw new BasicRuntimeException(1001, "REMAINDER by zero");
                    return a - BigDecimal.Truncate(a / b) * b;
                }); break;
                case Opcode.Concat:
                    {
                        var br = ((StringValue)stack.Pop()).V;
                        var bl = ((StringValue)stack.Pop()).V;
                        stack.Push(new StringValue(bl + br));
                        break;
                    }
                case Opcode.Neg:
                    stack.Push(new NumericValue(-((NumericValue)stack.Pop()).V));
                    break;

                case Opcode.Eq: Compare(stack, (a, b) => a == b, (a, b) => a == b); break;
                case Opcode.Ne: Compare(stack, (a, b) => a != b, (a, b) => a != b); break;
                case Opcode.Lt: Compare(stack, (a, b) => a < b, (a, b) => string.CompareOrdinal(a, b) < 0); break;
                case Opcode.Le: Compare(stack, (a, b) => a <= b, (a, b) => string.CompareOrdinal(a, b) <= 0); break;
                case Opcode.Gt: Compare(stack, (a, b) => a > b, (a, b) => string.CompareOrdinal(a, b) > 0); break;
                case Opcode.Ge: Compare(stack, (a, b) => a >= b, (a, b) => string.CompareOrdinal(a, b) >= 0); break;

                case Opcode.And: BinaryNumeric(stack, (a, b) =>
                    a != BigDecimal.Zero && b != BigDecimal.Zero ? -BigDecimal.One : BigDecimal.Zero); break;
                case Opcode.Or: BinaryNumeric(stack, (a, b) =>
                    a != BigDecimal.Zero || b != BigDecimal.Zero ? -BigDecimal.One : BigDecimal.Zero); break;
                case Opcode.Xor: BinaryNumeric(stack, (a, b) =>
                    (a != BigDecimal.Zero) != (b != BigDecimal.Zero) ? -BigDecimal.One : BigDecimal.Zero); break;
                case Opcode.Not:
                    {
                        var v = ((NumericValue)stack.Pop()).V;
                        stack.Push(v == BigDecimal.Zero ? NumericValue.One : NumericValue.Zero);
                        break;
                    }
                case Opcode.Imp: BinaryNumeric(stack, (a, b) =>
                    a == BigDecimal.Zero || b != BigDecimal.Zero ? -BigDecimal.One : BigDecimal.Zero); break;
                case Opcode.Eqv: BinaryNumeric(stack, (a, b) =>
                    (a != BigDecimal.Zero) == (b != BigDecimal.Zero) ? -BigDecimal.One : BigDecimal.Zero); break;
                case Opcode.Band: BinaryNumeric(stack, (a, b) =>
                    BigDecimal.Parse(((long)a & (long)b).ToString())); break;
                case Opcode.Bor: BinaryNumeric(stack, (a, b) =>
                    BigDecimal.Parse(((long)a | (long)b).ToString())); break;
                case Opcode.Bxor: BinaryNumeric(stack, (a, b) =>
                    BigDecimal.Parse(((long)a ^ (long)b).ToString())); break;
                case Opcode.Bnot:
                    {
                        var v = (long)((NumericValue)stack.Pop()).V;
                        stack.Push(new NumericValue(BigDecimal.Parse((~v).ToString())));
                        break;
                    }

                case Opcode.Jump:
                    {
                        var off = ReadI32(code, ref pc);
                        pc += off;
                        break;
                    }
                case Opcode.JumpIfTrue:
                    {
                        var off = ReadI32(code, ref pc);
                        if (((NumericValue)stack.Pop()).V != BigDecimal.Zero) pc += off;
                        break;
                    }
                case Opcode.JumpIfFalse:
                    {
                        var off = ReadI32(code, ref pc);
                        if (((NumericValue)stack.Pop()).V == BigDecimal.Zero) pc += off;
                        break;
                    }
                case Opcode.GosubFlow:
                    {
                        // Operand is the absolute PC of the GOSUB target.
                        var target = (int)ReadU32(code, ref pc);
                        gosubStack.Push(pc); // resume here after RETURN
                        pc = target;
                        break;
                    }
                case Opcode.Return:
                    {
                        if (gosubStack.Count == 0)
                            throw new BasicRuntimeException(3001, "RETURN without GOSUB");
                        pc = gosubStack.Pop();
                        break;
                    }

                case Opcode.CallBuiltin:
                    {
                        var bid = (int)ReadU32(code, ref pc);
                        var argc = (int)ReadU32(code, ref pc);
                        var name = _program.BuiltinNames[bid];
                        var args = new Value[argc];
                        for (var i = argc - 1; i >= 0; i--) args[i] = stack.Pop();
                        // EXTYPE/EXLINE/EXTEXT$ must see the VM's current
                        // exception. BuiltinImpls has zero-returning stubs for
                        // them (they don't have access to interpreter state),
                        // so we intercept here before the BuiltinImpls dispatch.
                        if (string.Equals(name, "EXTYPE", StringComparison.OrdinalIgnoreCase))
                        {
                            stack.Push(_currentException is null
                                ? NumericValue.Zero
                                : new NumericValue(BigDecimal.Parse(_currentException.Type.ToString(CultureInfo.InvariantCulture))));
                        }
                        else if (string.Equals(name, "EXLINE", StringComparison.OrdinalIgnoreCase))
                        {
                            stack.Push(_currentException is null
                                ? NumericValue.Zero
                                : new NumericValue(BigDecimal.Parse(_currentException.Line.ToString(CultureInfo.InvariantCulture))));
                        }
                        else if (string.Equals(name, "EXTEXT", StringComparison.OrdinalIgnoreCase))
                        {
                            stack.Push(_currentException is null
                                ? StringValue.Empty
                                : new StringValue(_currentException.Text));
                        }
                        else if (string.Equals(name, "INKEY", StringComparison.OrdinalIgnoreCase))
                        {
                            stack.Push(new StringValue(_keyboard.ReadKey()));   // non-blocking key poll
                        }
                        else if (BuiltinImpls.All.TryGetValue(name, out var fn))
                        {
                            stack.Push(fn(args));
                        }
                        else
                        {
                            throw new BasicRuntimeException(0, $"builtin '{name}' not implemented");
                        }
                        break;
                    }
                case Opcode.CallSub:
                    {
                        var sid = (int)ReadU32(code, ref pc);
                        var argc = (int)ReadU32(code, ref pc);
                        var sub = _program.Subs[sid];
                        var args = new Value[argc];
                        for (var i = argc - 1; i >= 0; i--) args[i] = stack.Pop();
                        var subFrame = new ActivationRecord(sub.Body.FrameSize, programFrame);
                        for (var i = 0; i < argc; i++) subFrame.Set(i, args[i]);
                        ExecuteChunk(sub.Body, subFrame, programFrame);
                        break;
                    }
                case Opcode.CallFunction:
                    {
                        var fid = (int)ReadU32(code, ref pc);
                        var argc = (int)ReadU32(code, ref pc);
                        var fn = _program.Functions[fid];
                        var args = new Value[argc];
                        for (var i = argc - 1; i >= 0; i--) args[i] = stack.Pop();
                        var fnFrame = new ActivationRecord(fn.Body.FrameSize, programFrame);
                        for (var i = 0; i < argc; i++) fnFrame.Set(i, args[i]);
                        // Run; the function chunk's body ends with LoadLocal(returnSlot) + LeaveFunction
                        // which means whatever the body left in the return slot bubbles up.
                        // Capture by re-running the chunk and reading the slot.
                        ExecuteChunk(fn.Body, fnFrame, programFrame);
                        stack.Push(fnFrame.GetOrDefault(fn.ReturnSlot,
                            fn.IsString ? StringValue.Empty : NumericValue.Zero));
                        break;
                    }
                case Opcode.CallDef:
                    {
                        var did = (int)ReadU32(code, ref pc);
                        var argc = (int)ReadU32(code, ref pc);
                        var def = _program.Defs[did];
                        var args = new Value[argc];
                        for (var i = argc - 1; i >= 0; i--) args[i] = stack.Pop();
                        // DEF body's parent scope is the caller's frame (so the
                        // body can reference the caller's outer names), unlike
                        // SUB/FUNCTION whose parent is programFrame.
                        var defFrame = new ActivationRecord(def.Body.FrameSize, frame);
                        for (var i = 0; i < argc; i++) defFrame.Set(i, args[i]);
                        ExecuteChunk(def.Body, defFrame, programFrame);
                        stack.Push(defFrame.GetOrDefault(def.ReturnSlot,
                            def.IsString ? StringValue.Empty : NumericValue.Zero));
                        break;
                    }
                case Opcode.LeaveSub:
                case Opcode.LeaveFunction:
                    while (_handlerStack.Count > entryHandlerDepth) _handlerStack.Pop();
                    return false;

                case Opcode.PrintNumber:
                    {
                        var v = ((NumericValue)stack.Pop()).V;
                        var text = FormatNumeric(v);
                        _out.Write(text);
                        col += text.Length;
                        pendingNewline = true;
                        break;
                    }
                case Opcode.PrintString:
                    {
                        var s = ((StringValue)stack.Pop()).V;
                        _out.Write(s);
                        col += s.Length;
                        pendingNewline = true;
                        break;
                    }
                case Opcode.PrintNewline:
                    _out.WriteLine();
                    col = 0;
                    pendingNewline = false;
                    break;
                case Opcode.PrintZonePad:
                    {
                        var next = ((col / DefaultZoneWidth) + 1) * DefaultZoneWidth;
                        for (var i = col; i < next; i++) _out.Write(' ');
                        col = next;
                        break;
                    }
                case Opcode.PrintTab:
                    {
                        // BASIC TAB(n) is 1-based; clamp negatives to column 0,
                        // and never move backwards (TAB to before current column is a no-op).
                        var target = (int)((NumericValue)stack.Pop()).V - 1;
                        if (target < 0) target = 0;
                        if (target > col)
                        {
                            for (var i = col; i < target; i++) _out.Write(' ');
                            col = target;
                        }
                        break;
                    }

                case Opcode.LineNote:
                    {
                        _currentLine = (int)ReadU32(code, ref pc);
                        var endOffset = ReadI32(code, ref pc);
                        stmtEndPc = pc + endOffset;
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
                    if (_handlerStack.Count > entryHandlerDepth) _handlerStack.Pop();
                    break;
                case Opcode.Cause:
                    {
                        var type = (int)((NumericValue)stack.Pop()).V;
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
                        var channel = (int)((NumericValue)stack.Pop()).V;
                        OpenChannel(channel, name, access, create, rectype);
                        break;
                    }
                case Opcode.Close:
                    {
                        var channel = (int)((NumericValue)stack.Pop()).V;
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
                        var channel = (int)((NumericValue)stack.Pop()).V;
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
                        var channel = (int)((NumericValue)stack.Pop()).V;
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
                        var suppressQuestionMark = ReadU32(code, ref pc) != 0;
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var rank = (int)ReadU32(code, ref pc);
                        var indices = PopIndices(stack, rank);
                        _out.Write(suppressQuestionMark ? " " : "? ");
                        _out.Flush();
                        var line = _in.ReadLine()
                            ?? throw new BasicRuntimeException(4003, "LINE INPUT: end of input stream");
                        var desc = new InputTargetDesc(depth, slot, IsString: true, rank);
                        AssignInputTarget(ResolveOuter(frame, programFrame, depth), desc, indices, new StringValue(line));
                        col = 0;
                        pendingNewline = false;
                        break;
                    }
                case Opcode.LineInputFile:
                    {
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var rank = (int)ReadU32(code, ref pc);
                        var channel = (int)((NumericValue)stack.Pop()).V;
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
                        var channel = (int)((NumericValue)stack.Pop()).V;
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
                        var channel = (int)((NumericValue)stack.Pop()).V;
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
                        col = 0;
                        pendingNewline = false;
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
                                narr.Data[i] = bd;
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
                        if (arr is not (NumericArrayValue or StringArrayValue))
                            throw new BasicRuntimeException(6004, "MAT operand: array has not been DIM-ed");
                        stack.Push(arr);
                        break;
                    }
                case Opcode.MatBinAdd: MatBinaryNumeric(stack, (a, ab, b, bb) => MatOps.ElementWise(a, ab, b, bb, (x, y) => x + y, "+")); break;
                case Opcode.MatBinSub: MatBinaryNumeric(stack, (a, ab, b, bb) => MatOps.ElementWise(a, ab, b, bb, (x, y) => x - y, "-")); break;
                case Opcode.MatBinMul: MatBinaryNumeric(stack, MatOps.Multiply); break;
                case Opcode.MatScalarMul:
                    {
                        var matrix = (NumericArrayValue)stack.Pop();
                        var scalar = ((NumericValue)stack.Pop()).V;
                        stack.Push(new NumericArrayValue(MatOps.ScalarMultiply(scalar, matrix.Data), matrix.Bounds));
                        break;
                    }
                case Opcode.MatTrn:
                    {
                        var m = (NumericArrayValue)stack.Pop();
                        var (data, bounds) = MatOps.Transpose(m.Data, m.Bounds);
                        stack.Push(new NumericArrayValue(data, bounds));
                        break;
                    }
                case Opcode.MatInv:
                    {
                        var m = (NumericArrayValue)stack.Pop();
                        stack.Push(new NumericArrayValue(MatOps.Inverse(m.Data, m.Bounds), m.Bounds));
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
                            upper[i] = (int)((NumericValue)stack.Pop()).V;
                            lower[i] = (int)((NumericValue)stack.Pop()).V;
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
                            var newData = new BigDecimal[newBounds.Length];
                            if (current is NumericArrayValue oldN) MatOps.PreserveNumericElements(oldN, newData, newBounds);
                            target.Set(slot, new NumericArrayValue(newData, newBounds));
                        }
                        break;
                    }
                case Opcode.MatPrint:
                    {
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var arr = ResolveOuter(frame, programFrame, depth).GetOrDefault(slot, NumericValue.Zero);
                        if (arr is NumericArrayValue narr) MatOps.PrintMatrix(_out, narr.Data, narr.Bounds, FormatNumeric);
                        else if (arr is StringArrayValue sarr) MatOps.PrintMatrix(_out, sarr.Data, sarr.Bounds, s => s);
                        else throw new BasicRuntimeException(6004, "MAT PRINT requires the target to be DIM-ed first");
                        col = 0;
                        pendingNewline = false;
                        break;
                    }
                case Opcode.MatInput:
                    {
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var isString = ReadU32(code, ref pc) != 0;
                        var arr = ResolveOuter(frame, programFrame, depth).GetOrDefault(slot, NumericValue.Zero);
                        if (arr is not (NumericArrayValue or StringArrayValue))
                            throw new BasicRuntimeException(6004, "MAT INPUT requires the target to be DIM-ed first");
                        PerformMatInput(arr, isString);
                        col = 0;
                        pendingNewline = false;
                        break;
                    }

                case Opcode.Input:
                    {
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
                        // Subscripts were pushed in target order; pop in reverse so
                        // we end up with one int[] per target keyed by declaration index.
                        var indices = new int[targetCount][];
                        for (var i = targetCount - 1; i >= 0; i--)
                        {
                            indices[i] = PopIndices(stack, descs[i].Rank);
                        }
                        // Prompt text (if any) was already emitted via PrintString
                        // before this opcode. col was updated then; PerformInput
                        // resets col after its own writes since INPUT always
                        // consumes a full line.
                        col = PerformInput(descs, indices, suppressQuestionMark, frame, programFrame);
                        pendingNewline = false;
                        break;
                    }

                case Opcode.DimArray:
                    {
                        var slot = (int)ReadU32(code, ref pc);
                        var rank = (int)ReadU32(code, ref pc);
                        var isString = ReadU32(code, ref pc) != 0;
                        AllocArray(stack, frame, slot, rank, isString);
                        break;
                    }
                case Opcode.DimArrayOuter:
                    {
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var rank = (int)ReadU32(code, ref pc);
                        var isString = ReadU32(code, ref pc) != 0;
                        AllocArray(stack, ResolveOuter(frame, programFrame, depth), slot, rank, isString);
                        break;
                    }
                case Opcode.LoadElement:
                    {
                        var slot = (int)ReadU32(code, ref pc);
                        var rank = (int)ReadU32(code, ref pc);
                        stack.Push(ReadElement(stack, frame, slot, rank));
                        break;
                    }
                case Opcode.LoadElementOuter:
                    {
                        var depth = (int)ReadU32(code, ref pc);
                        var slot = (int)ReadU32(code, ref pc);
                        var rank = (int)ReadU32(code, ref pc);
                        stack.Push(ReadElement(stack, ResolveOuter(frame, programFrame, depth), slot, rank));
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
                    stack.Push(BuiltinImpls.EvalConstant("PI"));
                    break;
                case Opcode.LoadConstantEps:
                    stack.Push(BuiltinImpls.EvalConstant("EPS"));
                    break;
                case Opcode.LoadConstantInf:
                    stack.Push(BuiltinImpls.EvalConstant("INF"));
                    break;
                case Opcode.LoadConstantMaxnum:
                    stack.Push(BuiltinImpls.EvalConstant("MAXNUM"));
                    break;

                default:
                    throw new BasicRuntimeException(0, $"unimplemented opcode {op}");
            }
            }
            catch (BasicRuntimeException ex)
            {
                // If no handler in this chunk's scope, propagate up the call chain
                // to a caller's ExecuteChunk (or out to Run() for unhandled).
                if (_handlerStack.Count <= entryHandlerDepth) throw;
                var top = _handlerStack.Pop();
                while (stack.Count > top.StackBaseline) stack.Pop();
                _currentException = new BasicException(ex.TypeCode, _currentLine, ex.Message);
                // Snapshot the chunk-local stmtEndPc so a CONTINUE inside the
                // USE body resumes at the statement after the one that raised.
                _currentContinuePc = stmtEndPc;
                pc = top.UsePc;
            }
        }

        // Reaching the end of the chunk naturally — clean up any handlers we
        // opened but didn't formally PopHandler (a defensive measure; well-formed
        // bytecode shouldn't leave anything dangling).
        while (_handlerStack.Count > entryHandlerDepth) _handlerStack.Pop();
        _ = pendingNewline;
        return false;
    }

    /// <summary>Build a numeric or string constant array matching the
    /// given <paramref name="bounds"/>. Kind values match Parser.Ast.MatConstKind:
    /// 0=Identity (IDN), 1=Zeros (ZER), 2=Ones (CON), 3=NullString (NUL$).</summary>
    private static Value BuildMatConst(uint kind, bool isString, Bounds bounds)
    {
        return (kind, isString) switch
        {
            (0u, false) => new NumericArrayValue(MatOps.Identity(bounds), bounds),
            (1u, false) => new NumericArrayValue(new BigDecimal[bounds.Length], bounds),
            (2u, false) => new NumericArrayValue(MatOps.Fill(bounds, BigDecimal.One), bounds),
            (3u, true) => new StringArrayValue(FillStrings(bounds.Length, ""), bounds),
            _ => throw new BasicRuntimeException(0,
                $"MAT constant kind {kind} not valid for {(isString ? "string" : "numeric")} target"),
        };
    }

    private static void MatBinaryNumeric(
        Stack<Value> stack,
        Func<BigDecimal[], Bounds, BigDecimal[], Bounds, (BigDecimal[] Data, Bounds Bounds)> op)
    {
        var r = (NumericArrayValue)stack.Pop();
        var l = (NumericArrayValue)stack.Pop();
        var (data, bounds) = op(l.Data, l.Bounds, r.Data, r.Bounds);
        stack.Push(new NumericArrayValue(data, bounds));
    }

    private static string[] FillStrings(int length, string value)
    {
        var arr = new string[length];
        for (var i = 0; i < length; i++) arr[i] = value;
        return arr;
    }

    private void PerformMatInput(Value arr, bool isString)
    {
        // Mirrors BasicInterpreter.ExecMatInput: prompt with "? " per line and
        // collect comma-separated fields until the array is filled. Errors
        // line up with the tree-walker (4002 non-numeric, 6005 EOF).
        var n = MatOps.BoundsOf(arr)!.Length;
        var values = new List<string>();
        while (values.Count < n)
        {
            _out.Write("? "); _out.Flush();
            var line = _in.ReadLine()
                ?? throw new BasicRuntimeException(6005, "MAT INPUT: end of input");
            foreach (var part in line.Split(','))
            {
                var t = part.Trim();
                if (t.Length > 0) values.Add(t);
                if (values.Count == n) break;
            }
        }
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
                narr.Data[i] = bd;
            }
        }
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
                        var target = (int)((NumericValue)stackValues[nextStackItem++]).V - 1;
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
                v = new NumericValue(bd);
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
                v = new NumericValue(bd);
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
        return new NumericValue(bd);
    }

    private readonly record struct InputTargetDesc(int Depth, int Slot, bool IsString, int Rank);

    private int PerformInput(
        InputTargetDesc[] descs,
        int[][] indices,
        bool suppressQuestionMark,
        ActivationRecord frame,
        ActivationRecord programFrame)
    {
        // Bad-input retry loop. Mirrors BasicInterpreter.ExecInput; same error
        // codes and user-facing messages so the VM and tree-walker agree.
        while (true)
        {
            _out.Write(suppressQuestionMark ? " " : "? ");
            _out.Flush();

            var rawLine = _in.ReadLine();
            if (rawLine is null)
                throw new BasicRuntimeException(4003, "INPUT: end of input stream");

            var fields = rawLine.Split(',');
            if (fields.Length < descs.Length)
            {
                _out.WriteLine("Not enough data — redo from start.");
                continue;
            }

            var parsed = new Value[descs.Length];
            var badField = -1;
            for (var i = 0; i < descs.Length; i++)
            {
                var raw = fields[i].Trim();
                if (descs[i].IsString)
                {
                    parsed[i] = new StringValue(raw);
                }
                else if (BigDecimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var bd))
                {
                    parsed[i] = new NumericValue(bd);
                }
                else
                {
                    badField = i;
                    break;
                }
            }

            if (badField >= 0)
            {
                _out.WriteLine($"'{fields[badField].Trim()}' is not numeric — redo from start.");
                continue;
            }

            for (var i = 0; i < descs.Length; i++)
            {
                var target = ResolveOuter(frame, programFrame, descs[i].Depth);
                AssignInputTarget(target, descs[i], indices[i], parsed[i]);
            }
            return 0;
        }
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
            if (arr is NumericArrayValue narr) { narr.Data[narr.Bounds.IndexOf(indices)] = ((NumericValue)value).V; return; }
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

    private static void AllocArray(Stack<Value> stack, ActivationRecord target, int slot, int rank, bool isString)
    {
        var lower = new int[rank];
        var upper = new int[rank];
        // Bounds were pushed left-to-right per dimension: lo_0, hi_0, lo_1, hi_1, ...
        // Popping reverses, so iterate from the highest dim downward.
        for (var i = rank - 1; i >= 0; i--)
        {
            upper[i] = (int)((NumericValue)stack.Pop()).V;
            lower[i] = (int)((NumericValue)stack.Pop()).V;
            if (upper[i] < lower[i])
                throw new BasicRuntimeException(6001,
                    $"DIM: upper bound {upper[i]} less than lower bound {lower[i]}");
        }
        var bounds = new Bounds(lower, upper);
        Value array = isString
            ? new StringArrayValue(new string[bounds.Length], bounds)
            : new NumericArrayValue(new BigDecimal[bounds.Length], bounds);
        target.Set(slot, array);
    }

    private static Value ReadElement(Stack<Value> stack, ActivationRecord frame, int slot, int rank)
    {
        var indices = PopIndices(stack, rank);
        var arr = frame.GetOrDefault(slot, NumericValue.Zero);
        try
        {
            if (arr is NumericArrayValue narr) return new NumericValue(narr.Data[narr.Bounds.IndexOf(indices)]);
            if (arr is StringArrayValue sarr) return new StringValue(sarr.Data[sarr.Bounds.IndexOf(indices)] ?? "");
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

    private static void WriteElement(Stack<Value> stack, ActivationRecord frame, int slot, int rank)
    {
        var indices = PopIndices(stack, rank);
        var value = stack.Pop();
        var arr = frame.GetOrDefault(slot, NumericValue.Zero);
        try
        {
            if (arr is NumericArrayValue narr) { narr.Data[narr.Bounds.IndexOf(indices)] = ((NumericValue)value).V; return; }
            if (arr is StringArrayValue sarr) { sarr.Data[sarr.Bounds.IndexOf(indices)] = ((StringValue)value).V; return; }
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

    private static int[] PopIndices(Stack<Value> stack, int rank)
    {
        var indices = new int[rank];
        for (var i = rank - 1; i >= 0; i--) indices[i] = (int)((NumericValue)stack.Pop()).V;
        return indices;
    }

    private static uint ReadU32(IReadOnlyList<byte> code, ref int pc)
    {
        uint v = code[pc++];
        v |= (uint)code[pc++] << 8;
        v |= (uint)code[pc++] << 16;
        v |= (uint)code[pc++] << 24;
        return v;
    }

    private static int ReadI32(IReadOnlyList<byte> code, ref int pc) => (int)ReadU32(code, ref pc);

    private static void BinaryNumeric(Stack<Value> stack, Func<BigDecimal, BigDecimal, BigDecimal> op)
    {
        var b = ((NumericValue)stack.Pop()).V;
        var a = ((NumericValue)stack.Pop()).V;
        stack.Push(new NumericValue(op(a, b)));
    }

    private static void Compare(Stack<Value> stack,
        Func<BigDecimal, BigDecimal, bool> numericOp,
        Func<string, string, bool> stringOp)
    {
        var b = stack.Pop();
        var a = stack.Pop();
        bool result = (a, b) switch
        {
            (NumericValue x, NumericValue y) => numericOp(x.V, y.V),
            (StringValue x, StringValue y) => stringOp(x.V, y.V),
            _ => throw new BasicRuntimeException(0, "type mismatch in comparison"),
        };
        stack.Push(result ? NumericValue.MinusOne : NumericValue.Zero);
    }

    private static BigDecimal Pow(BigDecimal a, BigDecimal b)
    {
        if (b == BigDecimal.Truncate(b) && b >= int.MinValue && b <= int.MaxValue)
        {
            return BigDecimal.Pow(a, (int)b);
        }
        var ad = double.Parse(a.ToString(), CultureInfo.InvariantCulture);
        var bd = double.Parse(b.ToString(), CultureInfo.InvariantCulture);
        return BigDecimal.Parse(Math.Pow(ad, bd).ToString("R", CultureInfo.InvariantCulture));
    }

    private static string FormatNumeric(BigDecimal x) => DisplayFormat.FormatNumeric(x);
}
