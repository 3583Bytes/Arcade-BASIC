using System.Globalization;
using System.Text;
using ArcadeBasic.Parser.Ast;
using ArcadeBasic.Runtime;
using ArcadeBasic.Sema;
using Singulink.Numerics;

namespace ArcadeBasic.Interpreter;

/// <summary>
/// Statement-execution helpers split out for readability. Each method returns
/// a FlowControl that the outer ExecuteStatementList loop interprets.
/// </summary>
public sealed partial class BasicInterpreter
{
    // -- Assignment ------------------------------------------------------

    private FlowControl ExecAssign(AssignStmt a, ActivationRecord frame)
    {
        var value = EvalExpr(a.Value, frame);
        WriteAssignableTarget(a.Target, value, frame);
        return FlowControl.Continue;
    }

    private void WriteAssignableTarget(Expr target, Value value, ActivationRecord frame)
    {
        switch (target)
        {
            case NameRefExpr nr:
                {
                    var resolved = _info.Resolve(nr);
                    switch (resolved)
                    {
                        case ResolvedVariable rv:
                            WriteSlot(frame, rv.Symbol.OwnerScope!, rv.Symbol.Slot, value);
                            break;
                        case ResolvedParam rp:
                            WriteSlot(frame, rp.Symbol.OwnerScope!, rp.Symbol.Slot, value);
                            break;
                        default:
                            throw new BasicRuntimeException(0, $"cannot assign to '{nr.Name}'");
                    }
                    break;
                }
            case CallOrIndexExpr c:
                {
                    var resolved = _info.Resolve(c);
                    if (resolved is not ResolvedArrayAccess ra)
                    {
                        throw new BasicRuntimeException(0, $"cannot assign to indexed '{c.Name}'");
                    }
                    WriteArray(frame, ra.Symbol, c.Args, value);
                    break;
                }
            default:
                throw new BasicRuntimeException(0, "invalid assignment target");
        }
    }

    // -- PRINT -----------------------------------------------------------

    /// <summary>Default zone width for PRINT comma-separated items.</summary>
    private const int DefaultZoneWidth = 16;

    private FlowControl ExecPrint(PrintStmt p, ActivationRecord frame)
    {
        if (p.Items.Count == 0)
        {
            _out.WriteLine();
            return FlowControl.Continue;
        }

        var col = 0;
        var suppressNewline = false;
        var sb = new StringBuilder();

        for (var i = 0; i < p.Items.Count; i++)
        {
            var item = p.Items[i];
            switch (item)
            {
                case PrintExprItem ei:
                    var text = FormatForPrint(EvalExpr(ei.Value, frame));
                    sb.Append(text);
                    col += text.Length;
                    suppressNewline = false;
                    break;

                case PrintTab t:
                    var target = (int)EvalNumeric(t.Column, frame) - 1;
                    if (target < 0) target = 0;
                    if (target > col)
                    {
                        sb.Append(' ', target - col);
                        col = target;
                    }
                    suppressNewline = false;
                    break;

                case PrintComma:
                    // Pad to next zone boundary.
                    var next = ((col / DefaultZoneWidth) + 1) * DefaultZoneWidth;
                    sb.Append(' ', next - col);
                    col = next;
                    suppressNewline = i == p.Items.Count - 1;
                    break;

                case PrintSemicolon:
                    suppressNewline = i == p.Items.Count - 1;
                    break;
            }
        }

        if (suppressNewline) _out.Write(sb.ToString());
        else _out.WriteLine(sb.ToString());
        return FlowControl.Continue;
    }

    private static string FormatForPrint(Value v) => v switch
    {
        StringValue s => s.V,
        NumericValue n => FormatNumeric(n.V),
        _ => v.ToString() ?? "",
    };

    private FlowControl ExecPrintUsing(PrintUsingStmt stmt, ActivationRecord frame)
    {
        var format = EvalString(stmt.Format, frame);
        var values = stmt.Items.Select(e => EvalExpr(e, frame)).ToList();
        var parts = PictureFormat.Parse(format);
        _out.WriteLine(PictureFormat.Apply(parts, values));
        return FlowControl.Continue;
    }

    /// <summary>BASIC-style numeric formatting. Delegates to <see cref="DisplayFormat.FormatNumeric"/>
    /// so the bytecode VM produces byte-identical output.</summary>
    private static string FormatNumeric(BigDecimal x) => DisplayFormat.FormatNumeric(x);

    // -- INPUT -----------------------------------------------------------

    private FlowControl ExecInput(InputStmt input, ActivationRecord frame)
    {
        // Bad-input retry loop: re-prompt with "Redo from start" if the user
        // supplies too few fields or a non-numeric value for a numeric target.
        // Matches conventional BASIC behaviour; the ISO 10279 exception (4002)
        // is only raised once the input stream is exhausted (ReadLine -> null).
        while (true)
        {
            if (input.Prompt is not null)
            {
                _out.Write(EvalString(input.Prompt, frame));
                if (input.PromptIsSemicolon) _out.Write(' ');
                else _out.Write("? ");
            }
            else
            {
                _out.Write("? ");
            }
            _out.Flush();

            var rawLine = _in.ReadLine();
            if (rawLine is null)
            {
                throw new BasicRuntimeException(4003, "INPUT: end of input stream");
            }
            var fields = rawLine.Split(',');
            if (fields.Length < input.Targets.Count)
            {
                _out.WriteLine("Not enough data — redo from start.");
                continue;
            }

            var parsed = new Value[input.Targets.Count];
            var badField = -1;
            for (var i = 0; i < input.Targets.Count; i++)
            {
                var target = input.Targets[i];
                var raw = fields[i].Trim();
                var isString = TargetIsString(target);
                if (isString)
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

            for (var i = 0; i < input.Targets.Count; i++)
            {
                WriteAssignableTarget(input.Targets[i], parsed[i], frame);
            }
            return FlowControl.Continue;
        }
    }

    // LINE INPUT reads a whole line (commas and all) into a single string target.
    // The prompt mirrors the VM's Opcode.LineInput exactly so the two engines
    // stay byte-identical: the prompt expression (if any) is printed, then a
    // trailing "? " unless the prompt used the ';' form (then a single space).
    private FlowControl ExecLineInput(LineInputStmt stmt, ActivationRecord frame)
    {
        if (stmt.Prompt is not null)
        {
            _out.Write(EvalString(stmt.Prompt, frame));
        }
        _out.Write(stmt.Prompt is not null && stmt.PromptIsSemicolon ? " " : "? ");
        _out.Flush();

        var line = _in.ReadLine()
            ?? throw new BasicRuntimeException(4003, "LINE INPUT: end of input stream");
        WriteAssignableTarget(stmt.Target, new StringValue(line), frame);
        return FlowControl.Continue;
    }

    private bool TargetIsString(Expr e) => e switch
    {
        NameRefExpr n => n.IsString,
        CallOrIndexExpr c => c.IsString,
        _ => false,
    };

    // -- READ / RESTORE --------------------------------------------------

    private FlowControl ExecRead(ReadStmt r, ActivationRecord frame)
    {
        foreach (var target in r.Targets)
        {
            if (_dataCursor >= _info.DataPool.Count)
            {
                throw new BasicRuntimeException(5001, "READ: DATA pool exhausted");
            }
            var item = _info.DataPool[_dataCursor++];
            var isString = TargetIsString(target);
            Value value;
            if (isString)
            {
                value = new StringValue(item.IsString ? item.Text : item.Text);
            }
            else
            {
                if (!BigDecimal.TryParse(item.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var bd))
                {
                    throw new BasicRuntimeException(5002,
                        $"READ: data item '{item.Text}' is not numeric");
                }
                value = new NumericValue(bd);
            }
            WriteAssignableTarget(target, value, frame);
        }
        return FlowControl.Continue;
    }

    private FlowControl ExecRestore(RestoreStmt rs)
    {
        if (rs.LabelTarget is null)
        {
            _dataCursor = 0;
            return FlowControl.Continue;
        }
        // Phase-3 simplification: only support RESTORE 0 / RESTORE n where n
        // is a source-line label whose statement carries the cursor. Without
        // a per-DATA-line index, we just reset to 0 for any label.
        _dataCursor = 0;
        return FlowControl.Continue;
    }

    // -- RANDOMIZE -------------------------------------------------------

    private FlowControl ExecRandomize(RandomizeStmt rnd, ActivationRecord frame)
    {
        // Phase-3 stand-in: nothing reseeds the BuiltinImpls RND yet. Documented gap.
        if (rnd.Seed is not null) _ = EvalNumeric(rnd.Seed, frame);
        return FlowControl.Continue;
    }

    private FlowControl ExecSleep(SleepStmt slp, ActivationRecord frame)
    {
        // SLEEP is the frame boundary in a real-time loop: present the frame
        // drawn so far (the console backend paints here; the IDE redraws on its
        // own pump), then pause.
        _graphics.Flush();
        var secs = (double)EvalNumeric(slp.Seconds, frame);
        if (secs <= 0) return FlowControl.Continue;
        // Sleep in short slices so a cancellation (e.g. the IDE's Stop) is
        // observed promptly rather than after the full delay.
        var remaining = (int)Math.Min(secs * 1000.0, int.MaxValue);
        while (remaining > 0 && !_cancel.IsCancellationRequested)
        {
            var slice = Math.Min(remaining, 50);
            System.Threading.Thread.Sleep(slice);
            remaining -= slice;
        }
        return FlowControl.Continue;
    }

    // -- DIM -------------------------------------------------------------

    private FlowControl ExecDim(DimStmt dim, ActivationRecord frame)
    {
        foreach (var spec in dim.Specs)
        {
            var sym = (ArraySymbol?)_info.ProgramScope.Lookup(Scope.Key(spec.Name, spec.IsString))
                ?? throw new BasicRuntimeException(0, $"array '{spec.Name}' not registered by sema");

            var rank = spec.Bounds.Count;
            var lower = new int[rank];
            var upper = new int[rank];
            for (var i = 0; i < rank; i++)
            {
                lower[i] = spec.Bounds[i].Lower is null ? _optionBase : (int)EvalNumeric(spec.Bounds[i].Lower!, frame);
                upper[i] = (int)EvalNumeric(spec.Bounds[i].Upper, frame);
                if (upper[i] < lower[i])
                {
                    throw new BasicRuntimeException(6001,
                        $"DIM {spec.Name}: upper bound {upper[i]} less than lower bound {lower[i]}");
                }
            }
            var bounds = new Bounds(lower, upper);
            Value array = spec.IsString
                ? new StringArrayValue(new string[bounds.Length], bounds)
                : new NumericArrayValue(new BigDecimal[bounds.Length], bounds);
            // Initialize numeric arrays to BigDecimal.Zero (default(BigDecimal) is 0 already).
            WriteSlot(frame, sym.OwnerScope!, sym.Slot, array);
        }
        return FlowControl.Continue;
    }

    // -- IF --------------------------------------------------------------

    private FlowControl ExecIf(IfStmt ifs, ActivationRecord frame)
    {
        if (Truthy(EvalExpr(ifs.Condition, frame)))
        {
            return ExecuteStatementList(ifs.ThenBlock, frame);
        }
        foreach (var ei in ifs.ElseIfs)
        {
            if (Truthy(EvalExpr(ei.Condition, frame)))
            {
                return ExecuteStatementList(ei.Body, frame);
            }
        }
        if (ifs.ElseBlock is not null)
        {
            return ExecuteStatementList(ifs.ElseBlock, frame);
        }
        return FlowControl.Continue;
    }

    private static bool Truthy(Value v) => v switch
    {
        NumericValue n => n.V != BigDecimal.Zero,
        StringValue s => s.V.Length > 0,
        _ => false,
    };

    // -- FOR -------------------------------------------------------------

    private FlowControl ExecFor(ForStmt f, ActivationRecord frame)
    {
        var from = EvalNumeric(f.From, frame);
        var to = EvalNumeric(f.To, frame);
        var step = f.Step is null ? BigDecimal.One : EvalNumeric(f.Step, frame);
        if (step == BigDecimal.Zero)
            throw new BasicRuntimeException(6002, "FOR step cannot be zero");

        // The loop variable is resolved to a slot in the same scope as `frame`.
        var resolved = (ResolvedVariable)_info.Resolve(f.Variable);
        WriteSlot(frame, resolved.Symbol.OwnerScope!, resolved.Symbol.Slot, new NumericValue(from));

        while (true)
        {
            _cancel.ThrowIfCancellationRequested();
            var current = ((NumericValue)ReadSlot(frame, resolved.Symbol.OwnerScope!,
                resolved.Symbol.Slot, false)).V;
            if (step > BigDecimal.Zero && current > to) break;
            if (step < BigDecimal.Zero && current < to) break;

            var fc = ExecuteStatementList(f.Body, frame);
            if (fc is FlowControl.Exit ex && ex.Kind == ExitKind.For) return FlowControl.Continue;
            if (fc is FlowControl.End or FlowControl.Stop or FlowControl.Return) return fc;
            if (fc is FlowControl.Goto or FlowControl.Gosub) return fc;

            var next = current + step;
            WriteSlot(frame, resolved.Symbol.OwnerScope!, resolved.Symbol.Slot, new NumericValue(next));
        }
        return FlowControl.Continue;
    }

    // -- DO --------------------------------------------------------------

    private FlowControl ExecDo(DoStmt d, ActivationRecord frame)
    {
        while (true)
        {
            _cancel.ThrowIfCancellationRequested();
            if (d.Pre is not null)
            {
                var c = Truthy(EvalExpr(d.Pre.Condition, frame));
                if (d.Pre.IsUntil ? c : !c) break;
            }

            var fc = ExecuteStatementList(d.Body, frame);
            if (fc is FlowControl.Exit ex && ex.Kind == ExitKind.Do) return FlowControl.Continue;
            if (fc is FlowControl.End or FlowControl.Stop or FlowControl.Return) return fc;
            if (fc is FlowControl.Goto or FlowControl.Gosub) return fc;

            if (d.Post is not null)
            {
                var c = Truthy(EvalExpr(d.Post.Condition, frame));
                if (d.Post.IsUntil ? c : !c) break;
            }
        }
        return FlowControl.Continue;
    }

    // -- SELECT CASE -----------------------------------------------------

    private FlowControl ExecSelect(SelectStmt s, ActivationRecord frame)
    {
        var subj = EvalExpr(s.Subject, frame);
        foreach (var c in s.Cases)
        {
            foreach (var spec in c.Values)
            {
                if (CaseMatches(spec, subj, frame))
                {
                    var fc = ExecuteStatementList(c.Body, frame);
                    if (fc is FlowControl.Exit ex && ex.Kind == ExitKind.Select) return FlowControl.Continue;
                    return fc;
                }
            }
        }
        if (s.CaseElse is not null)
        {
            var fc = ExecuteStatementList(s.CaseElse, frame);
            if (fc is FlowControl.Exit ex && ex.Kind == ExitKind.Select) return FlowControl.Continue;
            return fc;
        }
        return FlowControl.Continue;
    }

    private bool CaseMatches(CaseSpec spec, Value subj, ActivationRecord frame)
    {
        switch (spec)
        {
            case CaseValue cv: return ValueEquals(subj, EvalExpr(cv.Value, frame));
            case CaseRange cr:
                {
                    var lo = EvalExpr(cr.Lo, frame);
                    var hi = EvalExpr(cr.Hi, frame);
                    if (subj is NumericValue n && lo is NumericValue ln && hi is NumericValue hn)
                    {
                        return n.V >= ln.V && n.V <= hn.V;
                    }
                    if (subj is StringValue ss && lo is StringValue sl && hi is StringValue sh)
                    {
                        return string.CompareOrdinal(ss.V, sl.V) >= 0
                            && string.CompareOrdinal(ss.V, sh.V) <= 0;
                    }
                    return false;
                }
            case CaseIs ci:
                {
                    var rhs = EvalExpr(ci.Value, frame);
                    return CompareWithOp(subj, rhs, ci.Op);
                }
        }
        return false;
    }

    private static bool ValueEquals(Value a, Value b) =>
        (a, b) switch
        {
            (NumericValue x, NumericValue y) => x.V == y.V,
            (StringValue x, StringValue y) => x.V == y.V,
            _ => false,
        };

    private static bool CompareWithOp(Value a, Value b, BinaryOp op) =>
        (a, b) switch
        {
            (NumericValue x, NumericValue y) => op switch
            {
                BinaryOp.Equal => x.V == y.V,
                BinaryOp.NotEqual => x.V != y.V,
                BinaryOp.Less => x.V < y.V,
                BinaryOp.LessEqual => x.V <= y.V,
                BinaryOp.Greater => x.V > y.V,
                BinaryOp.GreaterEqual => x.V >= y.V,
                _ => false,
            },
            (StringValue x, StringValue y) => op switch
            {
                BinaryOp.Equal => x.V == y.V,
                BinaryOp.NotEqual => x.V != y.V,
                BinaryOp.Less => string.CompareOrdinal(x.V, y.V) < 0,
                BinaryOp.LessEqual => string.CompareOrdinal(x.V, y.V) <= 0,
                BinaryOp.Greater => string.CompareOrdinal(x.V, y.V) > 0,
                BinaryOp.GreaterEqual => string.CompareOrdinal(x.V, y.V) >= 0,
                _ => false,
            },
            _ => false,
        };

    // -- CALL / function invocation --------------------------------------

    private FlowControl ExecCall(CallStmt c, ActivationRecord frame)
    {
        // Use sema's resolved target so cross-module/scope CALLs work — the
        // SubSymbol may live in a module scope not reachable via ProgramScope.
        if (!_info.CallTargets.TryGetValue(c, out var sym))
        {
            throw new BasicRuntimeException(0, $"undefined SUB '{c.Name}'");
        }
        var args = EvalArgs(c.Args, frame);
        InvokeSubOrFunction(sym.BodyScope, sym.Stmt.Params, args, sym.Stmt.Body, returnSlot: -1);
        return FlowControl.Continue;
    }

    private Value CallFunction(FunctionSymbol fs, Value[] args)
    {
        // Function-name slot was allocated *after* params at sema time.
        var nameSlot = ((VariableSymbol)fs.BodyScope.LocalLookup(Scope.Key(fs.Name, fs.IsString))!).Slot;
        var rv = InvokeSubOrFunction(fs.BodyScope, fs.Stmt.Params, args, fs.Stmt.Body, returnSlot: nameSlot);
        return rv ?? (fs.IsString ? StringValue.Empty : NumericValue.Zero);
    }

    private Value? InvokeSubOrFunction(Scope bodyScope, IReadOnlyList<Param> ps, Value[] args, IReadOnlyList<Stmt> body, int returnSlot)
    {
        if (ps.Count != args.Length)
            throw new BasicRuntimeException(6003, $"argument count mismatch: expected {ps.Count}, got {args.Length}");

        var callFrame = new ActivationRecord(bodyScope.FrameSize, parent: _programFrame);
        for (var i = 0; i < ps.Count; i++)
        {
            // Param slots are 0..N-1 in source order.
            callFrame.Set(i, args[i]);
        }
        var fc = ExecuteStatementList(body, callFrame);
        // FunctionStmt assigns to the function-name slot to return a value.
        if (returnSlot >= 0 && callFrame.IsSet(returnSlot)) return callFrame.Get(returnSlot);
        return null;
    }

    private Value CallDef(DefSymbol ds, Value[] args, ActivationRecord callerFrame)
    {
        if (ds.Stmt.Params.Count != args.Length)
            throw new BasicRuntimeException(6003,
                $"argument count mismatch in {ds.Name}: expected {ds.Stmt.Params.Count}, got {args.Length}");

        // DEF gets a tiny activation record holding only the params. The body
        // can also reference enclosing program-scope names via the parent link.
        var defFrame = new ActivationRecord(ds.Stmt.Params.Count, parent: callerFrame);
        for (var i = 0; i < ds.Stmt.Params.Count; i++)
        {
            defFrame.Set(i, args[i]);
        }
        if (ds.Stmt.SingleLineBody is not null)
        {
            return EvalExpr(ds.Stmt.SingleLineBody, defFrame);
        }
        if (ds.Stmt.MultiLineBody is not null)
        {
            // Multi-line DEF returns by assigning to the DEF's name (treated like FUNCTION).
            // Sema doesn't currently allocate a name-slot for DEF; we simulate it by
            // capturing assignments to the DEF's name into a sentinel local.
            // For Phase 3 we evaluate the body and return zero/empty if no assignment
            // captures it. (A follow-up will tighten this.)
            ExecuteStatementList(ds.Stmt.MultiLineBody, defFrame);
            return ds.IsString ? StringValue.Empty : NumericValue.Zero;
        }
        return ds.IsString ? StringValue.Empty : NumericValue.Zero;
    }

    private Value CallBuiltin(BuiltinSymbol b, Value[] args)
    {
        // Exception accessors read from the interpreter's current-exception
        // slot rather than the static stub registry. Only meaningful inside
        // a USE handler body; default to spec-safe zero/empty otherwise.
        switch (b.Name.ToUpperInvariant())
        {
            case "EXTYPE":
                return _currentException is null
                    ? NumericValue.Zero
                    : new NumericValue(BigDecimal.Parse(_currentException.Type.ToString()));
            case "EXLINE":
                return _currentException is null
                    ? NumericValue.Zero
                    : new NumericValue(BigDecimal.Parse(_currentException.Line.ToString()));
            case "EXTEXT":
                return _currentException is null
                    ? StringValue.Empty
                    : new StringValue(_currentException.Text);
            case "INKEY":
                // Non-blocking keyboard poll — reads from the injected keyboard
                // source, not the static builtin registry.
                return new StringValue(_keyboard.ReadKey());
        }

        if (!BuiltinImpls.All.TryGetValue(b.Name, out var fn))
        {
            throw new BasicRuntimeException(0, $"builtin '{b.Name}' has no implementation");
        }
        return fn(args);
    }

    // -- Array read/write ------------------------------------------------

    private Value ReadArray(ActivationRecord frame, ArraySymbol sym, IReadOnlyList<Expr> indices)
    {
        var arrVal = ReadSlot(frame, sym.OwnerScope!, sym.Slot, sym.IsString);
        try
        {
            if (arrVal is NumericArrayValue narr)
            {
                return new NumericValue(narr.Data[narr.Bounds.IndexOf(EvalIndices(indices, frame))]);
            }
            if (arrVal is StringArrayValue sarr)
            {
                return new StringValue(sarr.Data[sarr.Bounds.IndexOf(EvalIndices(indices, frame))] ?? "");
            }
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new BasicRuntimeException(1002, $"array '{sym.Name}' subscript: {ex.Message}");
        }
        catch (ArgumentException ex)
        {
            throw new BasicRuntimeException(1003, $"array '{sym.Name}' subscript: {ex.Message}");
        }
        throw new BasicRuntimeException(0, $"array '{sym.Name}' not allocated; missing DIM");
    }

    private void WriteArray(ActivationRecord frame, ArraySymbol sym, IReadOnlyList<Expr> indices, Value value)
    {
        var arrVal = ReadSlot(frame, sym.OwnerScope!, sym.Slot, sym.IsString);
        try
        {
            if (arrVal is NumericArrayValue narr)
            {
                var idx = narr.Bounds.IndexOf(EvalIndices(indices, frame));
                narr.Data[idx] = ((NumericValue)value).V;
                return;
            }
            if (arrVal is StringArrayValue sarr)
            {
                var idx = sarr.Bounds.IndexOf(EvalIndices(indices, frame));
                sarr.Data[idx] = ((StringValue)value).V;
                return;
            }
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new BasicRuntimeException(1002, $"array '{sym.Name}' subscript: {ex.Message}");
        }
        catch (ArgumentException ex)
        {
            throw new BasicRuntimeException(1003, $"array '{sym.Name}' subscript: {ex.Message}");
        }
        throw new BasicRuntimeException(0, $"array '{sym.Name}' not allocated; missing DIM");
    }

    private int[] EvalIndices(IReadOnlyList<Expr> indices, ActivationRecord frame)
    {
        var arr = new int[indices.Count];
        for (var i = 0; i < indices.Count; i++) arr[i] = (int)EvalNumeric(indices[i], frame);
        return arr;
    }
}
