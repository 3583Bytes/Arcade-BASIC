using System.Globalization;
using System.Text;
using FullBasic.Parser.Ast;
using FullBasic.Runtime;
using FullBasic.Sema;
using Singulink.Numerics;

namespace FullBasic.Interpreter;

/// <summary>
/// Tree-walking interpreter for the Phase-3 interpreter-core subset of Full
/// BASIC. Consumes a parsed Program plus a SemanticInfo (resolutions, frame
/// sizes, line-label map, DATA pool) and executes it. Output goes to the
/// configured TextWriter; INPUT reads from the configured TextReader.
///
/// Out of scope this PR (matches parser/sema scope): file I/O, MAT, exception
/// handling (WHEN/USE/HANDLER), modules, picture/graphics. Runtime errors
/// signal via BasicRuntimeException for now; Phase 6 replaces this with
/// FlowControl.Cause once the handler machinery exists.
/// </summary>
public sealed partial class BasicInterpreter
{
    private readonly Program _program;
    private readonly SemanticInfo _info;
    private readonly TextWriter _out;
    private readonly TextReader _in;

    private ActivationRecord _programFrame;
    private int _dataCursor;
    private int _optionBase;
    private readonly ChannelTable _channels = new();

    public BasicInterpreter(Program program, SemanticInfo info, TextWriter @out, TextReader @in)
    {
        _program = program;
        _info = info;
        _out = @out;
        _in = @in;
        _programFrame = new ActivationRecord(info.ProgramScope.FrameSize, parent: null);
    }

    /// <summary>Run the program. Returns 0 on normal termination, 1 on runtime error.</summary>
    public int Run()
    {
        try
        {
            // DIM statements at program level need to allocate arrays before
            // any references to them. The simplest deterministic order is:
            // in source order, executed alongside the regular flow. So we
            // just execute the program top-to-bottom, and DIMs happen as
            // they are encountered. Forward refs to arrays before their
            // DIM are caught at sema (UndefinedName on the access site is
            // not emitted because pass-1 hoists the array symbol — but at
            // runtime the slot is still null when we read it). Detect that
            // and report a clear error.
            var fc = ExecuteStatementList(_program.Statements, _programFrame);
            if (fc is FlowControl.Cause c)
            {
                _out.Flush();
                Console.Error.WriteLine(
                    $"unhandled exception type {c.Exception.Type} at line {c.Exception.Line}: {c.Exception.Text}");
                return 1;
            }
            return fc switch
            {
                FlowControl.End or FlowControl.Stop or FlowControl.Next => 0,
                _ => 0, // unhandled flow at top level — treat as normal end
            };
        }
        catch (BasicRuntimeException ex)
        {
            _out.Flush();
            Console.Error.WriteLine($"runtime error [{ex.TypeCode}]: {ex.Message}");
            return 1;
        }
        finally
        {
            _channels.Dispose();
        }
    }

    // -- Statement-list executor -----------------------------------------

    private FlowControl ExecuteStatementList(IReadOnlyList<Stmt> stmts, ActivationRecord frame)
    {
        var labelMap = BuildLabelMap(stmts);
        var gosubReturnStack = new Stack<int>();
        var pc = 0;
        while (pc < stmts.Count)
        {
            var stmt = stmts[pc];
            var fc = ExecStmt(stmt, frame);
            switch (fc)
            {
                case FlowControl.Next: pc++; break;
                case FlowControl.Goto g:
                    if (labelMap.TryGetValue(g.Label, out var gi)) { pc = gi; break; }
                    return fc; // out of this list — propagate
                case FlowControl.Gosub gs:
                    if (labelMap.TryGetValue(gs.Label, out var idx))
                    {
                        gosubReturnStack.Push(pc + 1);
                        pc = idx;
                        break;
                    }
                    return fc;
                case FlowControl.Return r:
                    if (gosubReturnStack.Count > 0) { pc = gosubReturnStack.Pop(); break; }
                    return r; // function/sub return — caller handles
                case FlowControl.End or FlowControl.Stop: return fc;
                case FlowControl.Exit: return fc;
                case FlowControl.Cause: return fc;     // propagate to handler / top-level
                case FlowControl.Retry: return fc;     // only valid inside a handler body
                case FlowControl.Resume: return fc;
                default: pc++; break;
            }
        }
        return FlowControl.Continue;
    }

    private static Dictionary<int, int> BuildLabelMap(IReadOnlyList<Stmt> stmts)
    {
        var m = new Dictionary<int, int>();
        for (var i = 0; i < stmts.Count; i++)
        {
            if (stmts[i].Label is { } l) m[l] = i;
        }
        return m;
    }

    // -- Statement dispatch ----------------------------------------------

    private FlowControl ExecStmt(Stmt stmt, ActivationRecord frame)
    {
        try
        {
            return ExecStmtImpl(stmt, frame);
        }
        catch (BasicRuntimeException ex)
        {
            // Phase-6 conversion: runtime errors that escape expression
            // evaluation become FlowControl.Cause so user WHEN/USE handlers
            // can catch them. Top-level Run prints unhandled Cause flow.
            var line = stmt.Span.StartPosition.LineCol.Line;
            return new FlowControl.Cause(new BasicException(ex.TypeCode, line, ex.Message));
        }
    }

    private FlowControl ExecStmtImpl(Stmt stmt, ActivationRecord frame)
    {
        switch (stmt)
        {
            case AssignStmt a: return ExecAssign(a, frame);
            case PrintStmt p: return ExecPrint(p, frame);
            case InputStmt i: return ExecInput(i, frame);
            case ReadStmt r: return ExecRead(r, frame);
            case DataStmt: return FlowControl.Continue; // collected at sema time
            case RestoreStmt rs: return ExecRestore(rs);
            case GotoStmt g:
                {
                    var n = (int)EvalNumeric(g.LabelTarget, frame);
                    return new FlowControl.Goto(n);
                }
            case GosubStmt g:
                {
                    var n = (int)EvalNumeric(g.LabelTarget, frame);
                    return new FlowControl.Gosub(n);
                }
            case ReturnStmt: return new FlowControl.Return();
            case StopStmt: return FlowControl.Stopped;
            case EndStmt: return FlowControl.Ended;
            case EndBlockStmt: return FlowControl.Continue;
            case RunStmt: return FlowControl.Continue;
            case RemStmt: return FlowControl.Continue;
            case OptionBaseStmt ob: _optionBase = ob.Base; return FlowControl.Continue;
            case OptionArithmeticStmt: return FlowControl.Continue;
            case RandomizeStmt rnd: return ExecRandomize(rnd, frame);
            case DimStmt dim: return ExecDim(dim, frame);
            case IfStmt ifs: return ExecIf(ifs, frame);
            case ForStmt f: return ExecFor(f, frame);
            case NextStmt: return FlowControl.Continue;
            case DoStmt d: return ExecDo(d, frame);
            case LoopStmt: return FlowControl.Continue;
            case SelectStmt s: return ExecSelect(s, frame);
            case ExitStmt e: return new FlowControl.Exit(MapExit(e.Target));
            case CallStmt c: return ExecCall(c, frame);
            case MatAssignStmt ma: return ExecMatAssign(ma, frame);
            case MatRedimStmt mr: return ExecMatRedim(mr, frame);
            case MatInputStmt mi: return ExecMatInput(mi, frame);
            case MatPrintStmt mp: return ExecMatPrint(mp, frame);
            case MatReadStmt mrd: return ExecMatRead(mrd, frame);
            case OpenStmt op: return ExecOpen(op, frame);
            case CloseStmt cs: return ExecClose(cs, frame);
            case PrintFileStmt pf: return ExecPrintFile(pf, frame);
            case InputFileStmt ifs: return ExecInputFile(ifs, frame);
            case LineInputFileStmt li2: return ExecLineInputFile(li2, frame);
            case WhenStmt w: return ExecWhen(w, frame);
            case HandlerStmt: return FlowControl.Continue; // declaration only
            case CauseStmt cause: return ExecCause(cause, frame);
            case RetryStmt: return FlowControl.RetryFlow;
            case ContinueResumeStmt: return FlowControl.ResumeFlow;
            case SubStmt: return FlowControl.Continue; // declarations only execute when called
            case FunctionStmt: return FlowControl.Continue;
            case DefStmt: return FlowControl.Continue;
            default: return FlowControl.Continue;
        }
    }

    private static ExitKind MapExit(ExitTarget t) => t switch
    {
        ExitTarget.For => ExitKind.For,
        ExitTarget.Do => ExitKind.Do,
        ExitTarget.Sub => ExitKind.Sub,
        ExitTarget.Function => ExitKind.Function,
        ExitTarget.Def => ExitKind.Def,
        ExitTarget.Select => ExitKind.Select,
        ExitTarget.When => ExitKind.When,
        ExitTarget.Handler => ExitKind.Handler,
        _ => ExitKind.Do,
    };

    // -- Expression evaluation -------------------------------------------

    private Value EvalExpr(Expr expr, ActivationRecord frame)
    {
        switch (expr)
        {
            case NumberExpr n:
                return new NumericValue(BigDecimal.Parse(n.Text, CultureInfo.InvariantCulture));

            case StringExpr s:
                return new StringValue(s.Value);

            case ParenExpr p:
                return EvalExpr(p.Inner, frame);

            case NameRefExpr nr:
                return EvalNameRef(nr, frame);

            case CallOrIndexExpr c:
                return EvalCallOrIndex(c, frame);

            case UnaryExpr u:
                return EvalUnary(u, frame);

            case BinaryExpr b:
                return EvalBinary(b, frame);

            default:
                throw new BasicRuntimeException(0, $"unsupported expression {expr.GetType().Name}");
        }
    }

    private BigDecimal EvalNumeric(Expr e, ActivationRecord frame) => e is null
        ? throw new BasicRuntimeException(0, "expected numeric expression")
        : ((NumericValue)EvalExpr(e, frame)).V;

    private string EvalString(Expr e, ActivationRecord frame) =>
        ((StringValue)EvalExpr(e, frame)).V;

    private Value EvalNameRef(NameRefExpr nr, ActivationRecord frame)
    {
        var resolved = _info.Resolve(nr);
        return resolved switch
        {
            ResolvedVariable rv => ReadSlot(frame, rv.Symbol.OwnerScope!, rv.Symbol.Slot, rv.Symbol.IsString),
            ResolvedParam rp => ReadSlot(frame, rp.Symbol.OwnerScope!, rp.Symbol.Slot, rp.Symbol.IsString),
            ResolvedConstant rc => BuiltinImpls.EvalConstant(rc.Symbol.Name),
            ResolvedBuiltinCall rb => CallBuiltin(rb.Symbol, []),
            _ => throw new BasicRuntimeException(0, $"unresolved name '{nr.Name}'"),
        };
    }

    private Value EvalCallOrIndex(CallOrIndexExpr c, ActivationRecord frame)
    {
        var resolved = _info.Resolve(c);
        return resolved switch
        {
            ResolvedArrayAccess ra => ReadArray(frame, ra.Symbol, c.Args),
            ResolvedBuiltinCall rb => CallBuiltin(rb.Symbol, EvalArgs(c.Args, frame)),
            ResolvedFunctionCall rf => CallFunction(rf.Symbol, EvalArgs(c.Args, frame)),
            ResolvedDefCall rd => CallDef(rd.Symbol, EvalArgs(c.Args, frame), frame),
            _ => throw new BasicRuntimeException(0, $"cannot resolve call/index '{c.Name}'"),
        };
    }

    private Value[] EvalArgs(IReadOnlyList<Expr> args, ActivationRecord frame)
    {
        var result = new Value[args.Count];
        for (var i = 0; i < args.Count; i++) result[i] = EvalExpr(args[i], frame);
        return result;
    }

    private Value EvalUnary(UnaryExpr u, ActivationRecord frame)
    {
        var inner = EvalExpr(u.Operand, frame);
        var n = ((NumericValue)inner).V;
        return u.Op switch
        {
            UnaryOp.Plus => inner,
            UnaryOp.Negate => new NumericValue(-n),
            UnaryOp.Not => new NumericValue(n == BigDecimal.Zero ? BigDecimal.One : BigDecimal.Zero),
            UnaryOp.BNot => new NumericValue(BigDecimal.Parse((~(long)n).ToString())),
            _ => throw new BasicRuntimeException(0, $"unsupported unary {u.Op}"),
        };
    }

    private Value EvalBinary(BinaryExpr b, ActivationRecord frame)
    {
        if (b.Op == BinaryOp.Concat)
        {
            return new StringValue(EvalString(b.Left, frame) + EvalString(b.Right, frame));
        }

        var lt = _info.TypeOf(b.Left);
        var rt = _info.TypeOf(b.Right);

        // Relational: both sides may be string or numeric.
        if (b.Op is BinaryOp.Equal or BinaryOp.NotEqual or BinaryOp.Less
                 or BinaryOp.LessEqual or BinaryOp.Greater or BinaryOp.GreaterEqual)
        {
            if (lt == BasicType.String && rt == BasicType.String)
            {
                var ls = EvalString(b.Left, frame);
                var rs = EvalString(b.Right, frame);
                var cmp = string.CompareOrdinal(ls, rs);
                return BoolValue(b.Op switch
                {
                    BinaryOp.Equal => cmp == 0,
                    BinaryOp.NotEqual => cmp != 0,
                    BinaryOp.Less => cmp < 0,
                    BinaryOp.LessEqual => cmp <= 0,
                    BinaryOp.Greater => cmp > 0,
                    BinaryOp.GreaterEqual => cmp >= 0,
                    _ => false,
                });
            }
            else
            {
                var ln = EvalNumeric(b.Left, frame);
                var rn = EvalNumeric(b.Right, frame);
                return BoolValue(b.Op switch
                {
                    BinaryOp.Equal => ln == rn,
                    BinaryOp.NotEqual => ln != rn,
                    BinaryOp.Less => ln < rn,
                    BinaryOp.LessEqual => ln <= rn,
                    BinaryOp.Greater => ln > rn,
                    BinaryOp.GreaterEqual => ln >= rn,
                    _ => false,
                });
            }
        }

        // All other binaries are numeric.
        var a = EvalNumeric(b.Left, frame);
        var bv = EvalNumeric(b.Right, frame);
        return b.Op switch
        {
            BinaryOp.Add => new NumericValue(a + bv),
            BinaryOp.Subtract => new NumericValue(a - bv),
            BinaryOp.Multiply => new NumericValue(a * bv),
            BinaryOp.Divide => bv == BigDecimal.Zero
                ? throw new BasicRuntimeException(1001, "division by zero")
                : new NumericValue(BigDecimal.Divide(a, bv, 30, RoundingMode.MidpointToEven)),
            BinaryOp.Power => new NumericValue(Pow(a, bv)),
            BinaryOp.Mod => bv == BigDecimal.Zero
                ? throw new BasicRuntimeException(1001, "MOD by zero")
                : new NumericValue(a - BigDecimal.Floor(a / bv) * bv),
            BinaryOp.Remainder => bv == BigDecimal.Zero
                ? throw new BasicRuntimeException(1001, "REMAINDER by zero")
                : new NumericValue(a - BigDecimal.Truncate(a / bv) * bv),
            BinaryOp.And => BoolValue(NonZero(a) && NonZero(bv)),
            BinaryOp.Or => BoolValue(NonZero(a) || NonZero(bv)),
            BinaryOp.Xor => BoolValue(NonZero(a) != NonZero(bv)),
            BinaryOp.Imp => BoolValue(!NonZero(a) || NonZero(bv)),
            BinaryOp.Eqv => BoolValue(NonZero(a) == NonZero(bv)),
            BinaryOp.Band => new NumericValue(BigDecimal.Parse(((long)a & (long)bv).ToString())),
            BinaryOp.Bor => new NumericValue(BigDecimal.Parse(((long)a | (long)bv).ToString())),
            BinaryOp.Bxor => new NumericValue(BigDecimal.Parse(((long)a ^ (long)bv).ToString())),
            _ => throw new BasicRuntimeException(0, $"unsupported binary {b.Op}"),
        };
    }

    private static BigDecimal Pow(BigDecimal a, BigDecimal b)
    {
        // Integer exponent: use BigDecimal.Pow.
        if (b == BigDecimal.Truncate(b) && b >= int.MinValue && b <= int.MaxValue)
        {
            var n = (int)b;
            return BigDecimal.Pow(a, n);
        }
        // Otherwise approximate via doubles.
        var ad = double.Parse(a.ToString(), CultureInfo.InvariantCulture);
        var bd = double.Parse(b.ToString(), CultureInfo.InvariantCulture);
        return BigDecimal.Parse(Math.Pow(ad, bd).ToString("R", CultureInfo.InvariantCulture));
    }

    // -- Slot read/write helpers -----------------------------------------

    private Value ReadSlot(ActivationRecord frame, Scope ownerScope, int slot, bool isString)
    {
        var f = ResolveFrameForScope(frame, ownerScope);
        return f.GetOrDefault(slot, isString ? StringValue.Empty : NumericValue.Zero);
    }

    private void WriteSlot(ActivationRecord frame, Scope ownerScope, int slot, Value value)
    {
        var f = ResolveFrameForScope(frame, ownerScope);
        f.Set(slot, value);
    }

    /// <summary>
    /// Given the currently-active frame and the scope a symbol was declared in,
    /// walk the static-link chain to find the matching frame. For Phase 3 we
    /// only have program scope + sub/function/def scopes, so the chain is
    /// shallow (current → program).
    /// </summary>
    private ActivationRecord ResolveFrameForScope(ActivationRecord current, Scope ownerScope)
    {
        for (var f = current; f is not null; f = f.Parent)
        {
            if (f == _programFrame && ownerScope.Kind == ScopeKind.Program) return f;
            if (f != _programFrame && ownerScope.Kind != ScopeKind.Program)
            {
                // Inner scope frame (sub/function/def). Match by frame size? We
                // identify the right frame by the fact that locals are declared
                // there. The simplest heuristic: locals are always in `current`,
                // outer (program-level) is _programFrame. With no nesting beyond
                // one level (Phase 3), this is correct.
                return f;
            }
        }
        // Default: program frame.
        return _programFrame;
    }

    private static NumericValue BoolValue(bool b) => b ? NumericValue.MinusOne : NumericValue.Zero;

    private static bool NonZero(BigDecimal v) => v != BigDecimal.Zero;
}
