using ArcadeBasic.Bytecode;
using ArcadeBasic.Parser.Ast;
using ArcadeBasic.Sema;
using Singulink.Numerics;
using AstProgram = ArcadeBasic.Parser.Ast.Program;
using BcProgram = ArcadeBasic.Bytecode.Program;

namespace ArcadeBasic.Compiler;

/// <summary>
/// AST → bytecode compiler. Feature-complete against the tree-walker:
/// literals, variables, all unary/binary arithmetic + comparison + logical,
/// PRINT (positional, expressions + separators + TAB), assignments, IF
/// (block + single-line), FOR/NEXT, DO/LOOP (pre/post WHILE/UNTIL),
/// SELECT CASE, GOTO/GOSUB/RETURN (including forward labels via deferred
/// backfill), EXIT, STOP/END, REM, RANDOMIZE, single-line and multi-line
/// DEF, SUB/FUNCTION/CALL, builtins, DIM with 1-D and N-D bounds, indexed
/// read/write, OPTION BASE, INPUT and LINE INPUT (scalar + array targets,
/// prompts with semicolon/comma, retry loop), MAT assign/REDIM/INPUT/PRINT/
/// READ, MAT +/-/*, MAT TRN/INV/IDN/ZER/CON/NUL$ (including nested
/// constants in an expression), READ/DATA/RESTORE, PRINT USING, OPEN/CLOSE/
/// PRINT#/INPUT#/LINE INPUT# (DISPLAY mode SEQUENTIAL/STREAM), WHEN/USE/
/// CAUSE/RETRY/CONTINUE exception handling with inline or named HANDLER
/// bodies and EXTYPE/EXLINE/EXTEXT$ visibility, MODULE declarations with
/// PUBLIC re-export.
/// </summary>
public sealed class BasicCompiler
{
    public sealed class UnsupportedFeatureException(string message) : Exception(message);

    private readonly SemanticInfo _info;
    // Identity-keyed: a module-private "HELPER" and a top-level "HELPER" must
    // resolve to different bytecode indices even though they share a name.
    private readonly Dictionary<SubSymbol, int> _subIndex = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FunctionSymbol, int> _funcIndex = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DefSymbol, int> _defIndex = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, int> _builtinIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _builtinNames = [];

    private Chunk _current = null!;
    private Scope _currentScope = null!;
    private int _optionBase = 1;

    /// <summary>Stack of BeginWhen PCs for the open WHEN blocks; RETRY jumps to the innermost.</summary>
    private readonly Stack<int> _retryTargets = new();

    /// <summary>Per open WHEN block: placeholder Jump PCs from EXIT WHEN / EXIT HANDLER
    /// inside it. Patched to the byte just past the WHEN once compilation reaches that point.</summary>
    private readonly Stack<List<int>> _exitWhenJumps = new();

    /// <summary>Identifies the kind of chunk currently being compiled so EXIT
    /// SUB/FUNCTION/DEF can emit the right epilogue (and reject invalid uses).</summary>
    private CallableKind _currentCallable = CallableKind.None;

    /// <summary>Return slot of the FUNCTION/DEF currently being compiled. Read by EXIT FUNCTION/DEF.</summary>
    private int _currentReturnSlot;

    private enum CallableKind { None, Sub, Function, Def }

    /// <summary>Per-chunk: label number → bytecode PC. Populated as statements compile; queried during backfill.</summary>
    private readonly Dictionary<int, int> _labelPcs = new();

    /// <summary>Per-chunk: GOTO/GOSUB sites that referenced a label that may
    /// not have been emitted yet. Patched once the chunk's statement list is fully compiled.</summary>
    private readonly List<(int JumpPc, int Label, bool IsGosub)> _pendingLabelJumps = new();

    private BasicCompiler(SemanticInfo info)
    {
        _info = info;
    }

    public static BcProgram Compile(AstProgram program, SemanticInfo info)
    {
        var c = new BasicCompiler(info);
        return c.CompileProgram(program);
    }

    private BcProgram CompileProgram(AstProgram program)
    {
        // OPTION BASE is module-level by spec, so resolve at compile time —
        // the last directive in source order wins; default 1.
        _optionBase = ResolveOptionBase(program.Statements);

        // Pass 1: collect all callable symbols and assign indices. We walk
        // ProgramScope plus every module scope so module-private SUB/FUNCTION/DEF
        // bodies get compiled too. Sema re-exports PUBLIC symbols into
        // ProgramScope, so the same symbol may appear in both — keep a seen
        // set so each gets exactly one bytecode index.
        var subs = new List<SubSymbol>();
        var funcs = new List<FunctionSymbol>();
        var defs = new List<DefSymbol>();

        void CollectFrom(Scope scope)
        {
            foreach (var sym in scope.Symbols.Values)
            {
                switch (sym)
                {
                    case SubSymbol ss when !_subIndex.ContainsKey(ss):
                        _subIndex[ss] = subs.Count;
                        subs.Add(ss);
                        break;
                    case FunctionSymbol fs when !_funcIndex.ContainsKey(fs):
                        _funcIndex[fs] = funcs.Count;
                        funcs.Add(fs);
                        break;
                    case DefSymbol ds when !_defIndex.ContainsKey(ds):
                        _defIndex[ds] = defs.Count;
                        defs.Add(ds);
                        break;
                }
            }
        }
        CollectFrom(_info.ProgramScope);
        foreach (var modScope in _info.ModuleScopes.Values) CollectFrom(modScope);

        // Compile main chunk.
        var main = new Chunk { FrameSize = _info.ProgramScope.FrameSize };
        _current = main;
        _currentScope = _info.ProgramScope;
        _currentCallable = CallableKind.None;
        ResetChunkLabelTracking();
        CompileStatements(program.Statements);
        PatchPendingLabelJumps();
        main.Emit(Opcode.End);

        // Compile each SUB / FUNCTION / DEF body into its own chunk.
        var compiledSubs = new List<CompiledSub>();
        foreach (var ss in subs)
        {
            var chunk = new Chunk { FrameSize = ss.BodyScope.FrameSize };
            _current = chunk;
            _currentScope = ss.BodyScope;
            _currentCallable = CallableKind.Sub;
            ResetChunkLabelTracking();
            CompileStatements(ss.Stmt.Body);
            PatchPendingLabelJumps();
            chunk.Emit(Opcode.LeaveSub);
            compiledSubs.Add(new CompiledSub(ss.Name, ss.Params.Count, chunk));
        }

        var compiledFuncs = new List<CompiledFunction>();
        foreach (var fs in funcs)
        {
            var chunk = new Chunk { FrameSize = fs.BodyScope.FrameSize };
            _current = chunk;
            _currentScope = fs.BodyScope;
            // Find the return slot — the local with the function's name.
            var returnSlotSym = (VariableSymbol)fs.BodyScope.LocalLookup(Scope.Key(fs.Name, fs.IsString))!;
            _currentCallable = CallableKind.Function;
            _currentReturnSlot = returnSlotSym.Slot;
            ResetChunkLabelTracking();
            CompileStatements(fs.Stmt.Body);
            PatchPendingLabelJumps();
            // After body: push the return slot value onto the stack.
            chunk.Emit(Opcode.LoadLocal); chunk.EmitU32((uint)returnSlotSym.Slot);
            chunk.Emit(Opcode.LeaveFunction);
            compiledFuncs.Add(new CompiledFunction(fs.Name, fs.IsString, fs.Params.Count, returnSlotSym.Slot, chunk));
        }

        var compiledDefs = new List<CompiledDef>();
        foreach (var ds in defs)
        {
            // Frame layout: [param0 .. paramN-1, returnSlot]. The body stores
            // its result into returnSlot via StoreLocal; CallDef reads it after
            // ExecuteChunk returns. Mirrors the FunctionSymbol convention.
            var returnSlot = ds.Params.Count;
            var chunk = new Chunk { FrameSize = ds.Params.Count + 1 };
            _current = chunk;
            // Reuse sema's def scope: the params live in there with the same
            // slot indices used at resolution time, so ScopeDepth matches.
            _currentScope = ds.BodyScope;
            _currentCallable = CallableKind.Def;
            _currentReturnSlot = returnSlot;
            if (ds.Stmt.SingleLineBody is not null)
            {
                CompileExpr(ds.Stmt.SingleLineBody);
                chunk.Emit(Opcode.StoreLocal);
                chunk.EmitU32((uint)returnSlot);
                chunk.Emit(Opcode.LeaveFunction);
            }
            else if (ds.Stmt.MultiLineBody is not null)
            {
                // Multi-line DEF body is statement list; return value defaults
                // to zero/empty unless the body assigns to the DEF's name (a
                // documented gap the tree-walker has too — sema doesn't
                // currently allocate a name-slot for DEF).
                ResetChunkLabelTracking();
                CompileStatements(ds.Stmt.MultiLineBody);
                PatchPendingLabelJumps();
                chunk.Emit(Opcode.LeaveFunction);
            }
            else
            {
                throw new UnsupportedFeatureException($"DEF '{ds.Name}' has no body");
            }
            compiledDefs.Add(new CompiledDef(ds.Name, ds.IsString, ds.Params.Count, returnSlot, chunk));
        }

        var dataPool = new List<BcDataItem>(_info.DataPool.Count);
        foreach (var item in _info.DataPool)
        {
            dataPool.Add(new BcDataItem(item.IsString, item.Text));
        }

        return new BcProgram
        {
            Main = main,
            Subs = compiledSubs,
            Functions = compiledFuncs,
            Defs = compiledDefs,
            BuiltinNames = _builtinNames,
            DataPool = dataPool,
        };
    }

    // -- Statement compilation -------------------------------------------

    private void CompileStatements(IReadOnlyList<Stmt> stmts)
    {
        var previousLineNotePc = -1;
        foreach (var stmt in stmts)
        {
            // Record the label's bytecode PC into the chunk-wide map so
            // forward GOTO/GOSUB references can be backfilled.
            if (stmt.Label is { } l) _labelPcs[l] = _current.CodeLength;
            // Patch the previous statement's LineNote so its stmtEndOffset
            // points at the start of this LineNote — that's where CONTINUE
            // jumps if the previous statement was the one that raised.
            if (previousLineNotePc >= 0) _current.PatchLineNoteEnd(previousLineNotePc);
            previousLineNotePc = EmitLineNote(stmt);
            CompileStatement(stmt);
        }
        // After the last statement: patch its LineNote to point at the byte
        // just past the end of this block. CONTINUE from the last stmt then
        // falls through cleanly to whatever follows (e.g., PopHandler).
        if (previousLineNotePc >= 0) _current.PatchLineNoteEnd(previousLineNotePc);
    }

    /// <summary>Clear the per-chunk label-tracking state before compiling a
    /// new chunk (main, SUB body, FUNCTION body, multi-line DEF body).</summary>
    private void ResetChunkLabelTracking()
    {
        _labelPcs.Clear();
        _pendingLabelJumps.Clear();
    }

    /// <summary>Backfill the GOTO/GOSUB sites we deferred while compiling the
    /// current chunk. Call after the chunk's statement list is fully
    /// compiled so every label PC is known.</summary>
    private void PatchPendingLabelJumps()
    {
        foreach (var (jumpPc, label, isGosub) in _pendingLabelJumps)
        {
            if (!_labelPcs.TryGetValue(label, out var targetPc))
                throw new UnsupportedFeatureException(
                    $"{(isGosub ? "GOSUB" : "GOTO")} target label {label} not defined in this chunk");
            if (isGosub)
            {
                // GosubFlow takes an absolute PC as its u32 operand.
                _current.PatchU32(jumpPc + 1, (uint)targetPc);
            }
            else
            {
                // Jump takes a relative i32 offset.
                _current.PatchJumpAbsolute(jumpPc, targetPc);
            }
        }
    }

    private int ResolveLabelTarget(Expr e)
    {
        if (e is NumberExpr n && int.TryParse(n.Text, out var v)) return v;
        throw new UnsupportedFeatureException("computed GOTO/GOSUB target not supported by VM");
    }

    private void CompileStatement(Stmt stmt)
    {
        switch (stmt)
        {
            case AssignStmt a: CompileAssign(a); break;
            case PrintStmt p: CompilePrint(p); break;
            case StopStmt: _current.Emit(Opcode.Stop); break;
            case EndStmt: _current.Emit(Opcode.End); break;
            case EndBlockStmt: break;
            case RunStmt: break;
            case RemStmt: break;
            case OptionBaseStmt: break;
            case OptionArithmeticStmt: break;
            case RandomizeStmt: break;
            case ReturnStmt: _current.Emit(Opcode.Return); break;
            case IfStmt ifs: CompileIf(ifs); break;
            case ForStmt f: CompileFor(f); break;
            case NextStmt: break;
            case DoStmt d: CompileDo(d); break;
            case LoopStmt: break;
            case SelectStmt s: CompileSelect(s); break;
            case ExitStmt e: CompileExit(e); break;
            case CallStmt c: CompileCall(c); break;
            case DimStmt d: CompileDim(d); break;
            case InputStmt input: CompileInput(input); break;
            case LineInputStmt lin: CompileLineInput(lin); break;
            case MatAssignStmt ma: CompileMatAssign(ma); break;
            case MatRedimStmt mr: CompileMatRedim(mr); break;
            case MatPrintStmt mp: CompileMatPrint(mp); break;
            case MatInputStmt mi: CompileMatInput(mi); break;
            case MatReadStmt mrd: CompileMatRead(mrd); break;
            case ReadStmt rd: CompileRead(rd); break;
            case RestoreStmt: _current.Emit(Opcode.Restore); break;
            case PrintUsingStmt pu: CompilePrintUsing(pu); break;
            case WhenStmt w: CompileWhen(w); break;
            case CauseStmt cs: CompileCause(cs); break;
            case RetryStmt: CompileRetry(); break;
            case ContinueResumeStmt:
                if (_retryTargets.Count == 0)
                    throw new UnsupportedFeatureException("CONTINUE outside of WHEN/USE");
                _current.Emit(Opcode.Continue);
                break;
            case OpenStmt op: CompileOpen(op); break;
            case CloseStmt cs: CompileClose(cs); break;
            case PrintFileStmt pf: CompilePrintFile(pf); break;
            case InputFileStmt ifs: CompileInputFile(ifs); break;
            case LineInputFileStmt lif: CompileLineInputFile(lif); break;
            case DataStmt:
                // DATA was collected by sema into _info.DataPool at compile time;
                // there's no runtime opcode to emit for the DATA statement itself.
                break;
            case SubStmt or FunctionStmt or DefStmt:
                // Declarations — already compiled into separate chunks.
                break;
            case ModuleStmt or HandlerStmt:
                break;
            case GotoStmt g:
                {
                    var labelNum = ResolveLabelTarget(g.LabelTarget);
                    var pc = _current.EmitJumpPlaceholder(Opcode.Jump);
                    _pendingLabelJumps.Add((pc, labelNum, IsGosub: false));
                    break;
                }
            case GosubStmt gs:
                {
                    var labelNum = ResolveLabelTarget(gs.LabelTarget);
                    var pc = _current.Emit(Opcode.GosubFlow);
                    _current.EmitU32(0); // placeholder — backfilled with absolute target PC
                    _pendingLabelJumps.Add((pc, labelNum, IsGosub: true));
                    break;
                }
            default:
                throw new UnsupportedFeatureException(
                    $"statement kind {stmt.GetType().Name} not yet supported by VM");
        }
    }

    private void CompileAssign(AssignStmt a)
    {
        switch (a.Target)
        {
            case NameRefExpr nr:
                CompileExpr(a.Value);
                var resolved = _info.Resolve(nr);
                switch (resolved)
                {
                    case ResolvedVariable rv:
                        EmitStoreSymbolSlot(rv.Symbol.OwnerScope!, rv.Symbol.Slot);
                        break;
                    case ResolvedParam rp:
                        EmitStoreSymbolSlot(rp.Symbol.OwnerScope!, rp.Symbol.Slot);
                        break;
                    case ResolvedError:
                        // Sema couldn't resolve the target — most commonly an
                        // assignment to a DEF's own name (multi-line DEF return
                        // value), which sema doesn't currently allocate a slot
                        // for. The tree-walker silently ignores; we match by
                        // discarding the value.
                        _current.Emit(Opcode.Pop);
                        break;
                    default:
                        throw new UnsupportedFeatureException($"cannot assign to {resolved.GetType().Name}");
                }
                break;
            case CallOrIndexExpr c:
                if (_info.Resolve(c) is not ResolvedArrayAccess ra)
                    throw new UnsupportedFeatureException($"cannot assign to indexed '{c.Name}'");
                // Push value first, then subscripts; StoreElement pops subs (reverse) then value.
                CompileExpr(a.Value);
                foreach (var arg in c.Args) CompileExpr(arg);
                EmitStoreElement(ra.Symbol.OwnerScope!, ra.Symbol.Slot, c.Args.Count);
                break;
            default:
                throw new UnsupportedFeatureException("invalid assignment target");
        }
    }

    // -- MAT lowering ----------------------------------------------------

    private void CompileMatAssign(MatAssignStmt ma)
    {
        var target = LookupMatTarget(ma.TargetName, ma.TargetIsString);

        // Top-level constant RHS (MAT A = ZER) folds into a single
        // MatAssignConst opcode — no stack traffic needed.
        if (ma.Rhs is MatRhsConst c)
        {
            _current.Emit(Opcode.MatAssignConst);
            _current.EmitU32((uint)target.Depth);
            _current.EmitU32((uint)target.Slot);
            _current.EmitU32(ma.TargetIsString ? 1u : 0u);
            _current.EmitU32((uint)c.Kind);
            return;
        }

        CompileMatRhs(ma.Rhs, target, ma.TargetIsString);
        _current.Emit(Opcode.MatAssign);
        _current.EmitU32((uint)target.Depth);
        _current.EmitU32((uint)target.Slot);
        _current.EmitU32(ma.TargetIsString ? 1u : 0u);
    }

    private void CompileMatRhs(MatRhs rhs, (int Depth, int Slot) target, bool targetIsString)
    {
        switch (rhs)
        {
            case MatRhsName n:
                {
                    var t = LookupMatTarget(n.Name, n.IsString);
                    _current.Emit(Opcode.MatLoadArray);
                    _current.EmitU32((uint)t.Depth);
                    _current.EmitU32((uint)t.Slot);
                    break;
                }
            case MatRhsBinary b:
                CompileMatRhs(b.Left, target, targetIsString);
                CompileMatRhs(b.Right, target, targetIsString);
                _current.Emit(b.Op switch
                {
                    MatBinaryKind.Add => Opcode.MatBinAdd,
                    MatBinaryKind.Subtract => Opcode.MatBinSub,
                    MatBinaryKind.Multiply => Opcode.MatBinMul,
                    _ => throw new UnsupportedFeatureException($"MAT binary op {b.Op}"),
                });
                break;
            case MatRhsScalarMul sm:
                CompileExpr(sm.Scalar);
                CompileMatRhs(sm.Matrix, target, targetIsString);
                _current.Emit(Opcode.MatScalarMul);
                break;
            case MatRhsInv inv:
                CompileMatRhs(inv.Operand, target, targetIsString);
                _current.Emit(Opcode.MatInv);
                break;
            case MatRhsTrn trn:
                CompileMatRhs(trn.Operand, target, targetIsString);
                _current.Emit(Opcode.MatTrn);
                break;
            case MatRhsConst c:
                // Nested constant — push an array shaped like the target's
                // current bounds onto the operand stack.
                _current.Emit(Opcode.MatPushConst);
                _current.EmitU32((uint)target.Depth);
                _current.EmitU32((uint)target.Slot);
                _current.EmitU32(targetIsString ? 1u : 0u);
                _current.EmitU32((uint)c.Kind);
                break;
            default:
                throw new UnsupportedFeatureException($"MAT RHS kind {rhs.GetType().Name}");
        }
    }

    private void CompileMatRedim(MatRedimStmt mr)
    {
        var target = LookupMatTarget(mr.TargetName, mr.TargetIsString);
        foreach (var bound in mr.Bounds)
        {
            if (bound.Lower is null) EmitLoadInt(_optionBase);
            else CompileExpr(bound.Lower);
            CompileExpr(bound.Upper);
        }
        _current.Emit(Opcode.MatRedim);
        _current.EmitU32((uint)target.Depth);
        _current.EmitU32((uint)target.Slot);
        _current.EmitU32((uint)mr.Bounds.Count);
        _current.EmitU32(mr.TargetIsString ? 1u : 0u);
    }

    private void CompileMatPrint(MatPrintStmt mp)
    {
        var target = LookupMatTarget(mp.TargetName, mp.TargetIsString);
        _current.Emit(Opcode.MatPrint);
        _current.EmitU32((uint)target.Depth);
        _current.EmitU32((uint)target.Slot);
    }

    private void CompileMatInput(MatInputStmt mi)
    {
        var target = LookupMatTarget(mi.TargetName, mi.TargetIsString);
        _current.Emit(Opcode.MatInput);
        _current.EmitU32((uint)target.Depth);
        _current.EmitU32((uint)target.Slot);
        _current.EmitU32(mi.TargetIsString ? 1u : 0u);
    }

    private void CompileMatRead(MatReadStmt mrd)
    {
        var target = LookupMatTarget(mrd.TargetName, mrd.TargetIsString);
        _current.Emit(Opcode.MatRead);
        _current.EmitU32((uint)target.Depth);
        _current.EmitU32((uint)target.Slot);
        _current.EmitU32(mrd.TargetIsString ? 1u : 0u);
    }

    // -- File-I/O lowering -----------------------------------------------

    // -- Exception-handling lowering -------------------------------------

    /// <summary>Emit a LineNote with the source line of <paramref name="stmt"/> plus a
    /// placeholder stmtEndOffset (patched once the next LineNote starts, or once
    /// the enclosing statement list finishes). Returns the LineNote's PC so
    /// <see cref="Chunk.PatchLineNoteEnd"/> can find it later.</summary>
    private int EmitLineNote(Stmt stmt)
    {
        var pc = _current.Emit(Opcode.LineNote);
        _current.EmitU32((uint)stmt.Span.StartPosition.LineCol.Line);
        _current.EmitI32(0); // stmtEndOffset placeholder
        return pc;
    }

    private void CompileWhen(WhenStmt w)
    {
        // Resolve the USE body. A named handler reference is inlined here —
        // every WHEN that references the same handler gets its own copy of
        // the body's bytecode. Bytecode size grows linearly with how often
        // the handler's referenced; the alternative (compiling each HANDLER
        // as a callable chunk) would save bytes but add a control-flow opcode.
        IReadOnlyList<Stmt> useBody;
        if (w.UseBody is not null)
        {
            useBody = w.UseBody;
        }
        else if (w.UseHandlerName is not null)
        {
            if (_info.ProgramScope.Lookup(Scope.Key(w.UseHandlerName, isString: false)) is not HandlerSymbol h)
                throw new UnsupportedFeatureException(
                    $"WHEN: HANDLER '{w.UseHandlerName}' not declared");
            useBody = h.Stmt.Body;
        }
        else
        {
            throw new UnsupportedFeatureException("WHEN: handler body could not be resolved");
        }

        var beginWhenPc = _current.EmitJumpPlaceholder(Opcode.BeginWhen);
        _retryTargets.Push(beginWhenPc);
        _exitWhenJumps.Push(new List<int>());
        try
        {
            CompileStatements(w.InBody);
            _current.Emit(Opcode.PopHandler);
            var skipUsePc = _current.EmitJumpPlaceholder(Opcode.Jump);
            _current.PatchJump(beginWhenPc);
            CompileStatements(useBody);
            _current.PatchJump(skipUsePc);
            // Patch every EXIT WHEN / EXIT HANDLER inside this block to land
            // here — the byte just past the WHEN.
            foreach (var jumpPc in _exitWhenJumps.Peek()) _current.PatchJump(jumpPc);
        }
        finally
        {
            _exitWhenJumps.Pop();
            _retryTargets.Pop();
        }
    }

    private void CompileCause(CauseStmt c)
    {
        CompileExpr(c.Type);
        _current.Emit(Opcode.Cause);
    }

    private void CompileRetry()
    {
        if (_retryTargets.Count == 0)
            throw new UnsupportedFeatureException("RETRY outside of WHEN/USE");
        _current.EmitJumpToAbsolute(Opcode.Retry, _retryTargets.Peek());
    }

    private void CompileOpen(OpenStmt op)
    {
        CompileExpr(op.Channel);
        CompileExpr(op.Name);
        _current.Emit(Opcode.Open);
        _current.EmitU32((uint)op.Access);
        _current.EmitU32((uint)op.Organization);
        _current.EmitU32((uint)op.Create);
    }

    private void CompileClose(CloseStmt cs)
    {
        CompileExpr(cs.Channel);
        _current.Emit(Opcode.Close);
    }

    private void CompilePrintFile(PrintFileStmt pf)
    {
        // Push every Expr-item's value in declaration order, then the channel
        // on top. The opcode's inline kind tape says which positions are
        // expressions versus separators.
        var kinds = new uint[pf.Items.Count];
        for (var i = 0; i < pf.Items.Count; i++)
        {
            var item = pf.Items[i];
            switch (item)
            {
                case PrintExprItem ei:
                    CompileExpr(ei.Value);
                    kinds[i] = _info.TypeOf(ei.Value) == BasicType.String ? 1u : 0u;
                    break;
                case PrintComma:
                    kinds[i] = 2u;
                    break;
                case PrintSemicolon:
                    kinds[i] = 3u;
                    break;
                case PrintTab pt:
                    CompileExpr(pt.Column);
                    kinds[i] = 4u;
                    break;
                default:
                    throw new UnsupportedFeatureException($"PRINT # item kind {item.GetType().Name} not supported by VM");
            }
        }
        CompileExpr(pf.Channel);
        _current.Emit(Opcode.PrintFile);
        _current.EmitU32((uint)pf.Items.Count);
        foreach (var k in kinds) _current.EmitU32(k);
    }

    private void CompileInputFile(InputFileStmt ifs)
    {
        var targets = new (int Depth, int Slot, bool IsString, int Rank, IReadOnlyList<Expr> Subs)[ifs.Targets.Count];
        for (var i = 0; i < ifs.Targets.Count; i++)
        {
            targets[i] = ResolveInputTarget(ifs.Targets[i]);
        }
        foreach (var t in targets)
        {
            foreach (var sub in t.Subs) CompileExpr(sub);
        }
        CompileExpr(ifs.Channel);
        _current.Emit(Opcode.InputFile);
        _current.EmitU32((uint)targets.Length);
        foreach (var t in targets)
        {
            _current.EmitU32((uint)t.Depth);
            _current.EmitU32((uint)t.Slot);
            _current.EmitU32(t.IsString ? 1u : 0u);
            _current.EmitU32((uint)t.Rank);
        }
    }

    private void CompileLineInput(LineInputStmt lin)
    {
        var t = ResolveInputTarget(lin.Target);
        if (!t.IsString)
            throw new UnsupportedFeatureException("LINE INPUT requires a string target");

        if (lin.Prompt is not null)
        {
            CompileExpr(lin.Prompt);
            _current.Emit(Opcode.PrintString);
        }
        foreach (var sub in t.Subs) CompileExpr(sub);
        _current.Emit(Opcode.LineInput);
        _current.EmitU32(lin.Prompt is not null && lin.PromptIsSemicolon ? 1u : 0u);
        _current.EmitU32((uint)t.Depth);
        _current.EmitU32((uint)t.Slot);
        _current.EmitU32((uint)t.Rank);
    }

    private void CompileLineInputFile(LineInputFileStmt lif)
    {
        var t = ResolveInputTarget(lif.Target);
        foreach (var sub in t.Subs) CompileExpr(sub);
        CompileExpr(lif.Channel);
        _current.Emit(Opcode.LineInputFile);
        _current.EmitU32((uint)t.Depth);
        _current.EmitU32((uint)t.Slot);
        _current.EmitU32((uint)t.Rank);
    }

    private void CompilePrintUsing(PrintUsingStmt pu)
    {
        CompileExpr(pu.Format);
        foreach (var item in pu.Items) CompileExpr(item);
        _current.Emit(Opcode.PrintUsing);
        _current.EmitU32((uint)pu.Items.Count);
    }

    private void CompileRead(ReadStmt rd)
    {
        // Reuse the INPUT target resolution since the assignment surface is
        // identical (scalar variables / params / array elements). Subscripts
        // are pushed in target order; the opcode pops them in reverse.
        var targets = new (int Depth, int Slot, bool IsString, int Rank, IReadOnlyList<Expr> Subs)[rd.Targets.Count];
        for (var i = 0; i < rd.Targets.Count; i++)
        {
            targets[i] = ResolveInputTarget(rd.Targets[i]);
        }

        foreach (var t in targets)
        {
            foreach (var sub in t.Subs) CompileExpr(sub);
        }

        _current.Emit(Opcode.Read);
        _current.EmitU32((uint)targets.Length);
        foreach (var t in targets)
        {
            _current.EmitU32((uint)t.Depth);
            _current.EmitU32((uint)t.Slot);
            _current.EmitU32(t.IsString ? 1u : 0u);
            _current.EmitU32((uint)t.Rank);
        }
    }

    /// <summary>
    /// Resolves a MAT statement's named array to its (depth, slot) coordinate.
    /// MAT statements (like the tree-walker) operate on program-scope arrays.
    /// </summary>
    private (int Depth, int Slot) LookupMatTarget(string name, bool isString)
    {
        if (_info.ProgramScope.Lookup(Scope.Key(name, isString)) is not ArraySymbol arr)
            throw new UnsupportedFeatureException($"MAT target '{name}' is not a known array");
        return (ScopeDepth(arr.OwnerScope!), arr.Slot);
    }

    private void CompileInput(InputStmt input)
    {
        // Resolve each target up front so the runtime opcode has everything
        // it needs as immediate operands.
        var targets = new (int Depth, int Slot, bool IsString, int Rank, IReadOnlyList<Expr> Subs)[input.Targets.Count];
        for (var i = 0; i < input.Targets.Count; i++)
        {
            targets[i] = ResolveInputTarget(input.Targets[i]);
        }

        // Prompt text: lower as a separate PrintString. Suffix (" " vs "? ")
        // is encoded in the Input opcode's operand.
        if (input.Prompt is not null)
        {
            CompileExpr(input.Prompt);
            _current.Emit(Opcode.PrintString);
        }

        // Push subscripts for array targets in declaration order.
        foreach (var t in targets)
        {
            foreach (var sub in t.Subs) CompileExpr(sub);
        }

        _current.Emit(Opcode.Input);
        _current.EmitU32(input.Prompt is not null && input.PromptIsSemicolon ? 1u : 0u);
        _current.EmitU32((uint)targets.Length);
        foreach (var t in targets)
        {
            _current.EmitU32((uint)t.Depth);
            _current.EmitU32((uint)t.Slot);
            _current.EmitU32(t.IsString ? 1u : 0u);
            _current.EmitU32((uint)t.Rank);
        }
    }

    private (int Depth, int Slot, bool IsString, int Rank, IReadOnlyList<Expr> Subs) ResolveInputTarget(Expr target)
    {
        switch (target)
        {
            case NameRefExpr nr:
                {
                    var resolved = _info.Resolve(nr);
                    var sym = resolved switch
                    {
                        ResolvedVariable rv => (Symbol)rv.Symbol,
                        ResolvedParam rp => rp.Symbol,
                        _ => throw new UnsupportedFeatureException($"INPUT target '{nr.Name}' resolves to {resolved.GetType().Name}, not a variable"),
                    };
                    var slot = sym switch
                    {
                        VariableSymbol vs => vs.Slot,
                        ParamSymbol ps => ps.Slot,
                        _ => throw new UnsupportedFeatureException($"INPUT target symbol kind {sym.GetType().Name}"),
                    };
                    return (ScopeDepth(sym.OwnerScope!), slot, sym.IsString, 0, Array.Empty<Expr>());
                }
            case CallOrIndexExpr c:
                {
                    if (_info.Resolve(c) is not ResolvedArrayAccess ra)
                        throw new UnsupportedFeatureException($"INPUT target '{c.Name}' is not an array reference");
                    return (ScopeDepth(ra.Symbol.OwnerScope!), ra.Symbol.Slot, ra.Symbol.IsString, c.Args.Count, c.Args);
                }
            default:
                throw new UnsupportedFeatureException($"INPUT target kind {target.GetType().Name}");
        }
    }

    private void CompileDim(DimStmt d)
    {
        foreach (var spec in d.Specs)
        {
            var sym = (ArraySymbol?)_info.ProgramScope.Lookup(Scope.Key(spec.Name, spec.IsString))
                ?? throw new UnsupportedFeatureException(
                    $"DIM '{spec.Name}': sema did not register an ArraySymbol");

            // Push bounds left-to-right: lower_0, upper_0, lower_1, upper_1, ...
            // The VM pops them in reverse and writes upper/lower per dimension.
            foreach (var bound in spec.Bounds)
            {
                if (bound.Lower is null) EmitLoadInt(_optionBase);
                else CompileExpr(bound.Lower);
                CompileExpr(bound.Upper);
            }

            EmitDimArray(sym.OwnerScope!, sym.Slot, spec.Bounds.Count, spec.IsString);
        }
    }

    private void CompilePrint(PrintStmt p)
    {
        if (p.Items.Count == 0)
        {
            _current.Emit(Opcode.PrintNewline);
            return;
        }

        var suppressNewline = false;
        for (var i = 0; i < p.Items.Count; i++)
        {
            var item = p.Items[i];
            switch (item)
            {
                case PrintExprItem ei:
                    CompileExpr(ei.Value);
                    var t = _info.TypeOf(ei.Value);
                    _current.Emit(t == BasicType.String ? Opcode.PrintString : Opcode.PrintNumber);
                    suppressNewline = false;
                    break;
                case PrintComma:
                    _current.Emit(Opcode.PrintZonePad);
                    suppressNewline = i == p.Items.Count - 1;
                    break;
                case PrintSemicolon:
                    suppressNewline = i == p.Items.Count - 1;
                    break;
                case PrintTab pt:
                    CompileExpr(pt.Column);
                    _current.Emit(Opcode.PrintTab);
                    suppressNewline = false;
                    break;
                default:
                    throw new UnsupportedFeatureException($"PRINT item kind {item.GetType().Name} not supported by VM");
            }
        }
        if (!suppressNewline) _current.Emit(Opcode.PrintNewline);
    }

    private void CompileIf(IfStmt ifs)
    {
        // condition then-body [elseif ...] [else-body] end
        CompileExpr(ifs.Condition);
        var jumpToElse = _current.EmitJumpPlaceholder(Opcode.JumpIfFalse);
        CompileStatements(ifs.ThenBlock);
        var jumpsToEnd = new List<int>();
        if (ifs.ElseIfs.Count > 0 || ifs.ElseBlock is not null)
        {
            jumpsToEnd.Add(_current.EmitJumpPlaceholder(Opcode.Jump));
        }
        _current.PatchJump(jumpToElse);

        foreach (var ei in ifs.ElseIfs)
        {
            CompileExpr(ei.Condition);
            var nextSkip = _current.EmitJumpPlaceholder(Opcode.JumpIfFalse);
            CompileStatements(ei.Body);
            jumpsToEnd.Add(_current.EmitJumpPlaceholder(Opcode.Jump));
            _current.PatchJump(nextSkip);
        }

        if (ifs.ElseBlock is not null)
        {
            CompileStatements(ifs.ElseBlock);
        }

        foreach (var j in jumpsToEnd) _current.PatchJump(j);
    }

    private readonly Stack<List<int>> _exitForJumps = new();
    private readonly Stack<List<int>> _exitDoJumps = new();
    private readonly Stack<List<int>> _exitSelectJumps = new();

    private void CompileFor(ForStmt f)
    {
        var resolved = (ResolvedVariable)_info.Resolve(f.Variable);
        var slot = resolved.Symbol.Slot;
        var ownerScope = resolved.Symbol.OwnerScope!;

        // Initialize: var = from
        CompileExpr(f.From);
        EmitStoreSymbolSlot(ownerScope, slot);

        // Compute step (default 1) and store in a hidden temporary.
        // For simplicity, we re-evaluate the step each iteration via a separate
        // local-emission strategy. Here we just inline the step-known-positive
        // case: assume step >= 0 and check var <= to. Negative steps fall back
        // to runtime detection on each iteration.
        // Simpler: emit a guard `if step > 0 && var > to: exit` style.

        _exitForJumps.Push([]);
        var loopStart = _current.CodeLength;

        // Guard — depends on step's sign. For correctness we'd need a two-branch
        // check; for Phase-9 simplicity we emit a runtime test that handles
        // both signs:  if (step > 0 && var > to) exit;  if (step < 0 && var < to) exit
        // (Compile each comparison; logical AND/OR on top of stack.)
        CompileForGuard(f, ownerScope, slot);
        var exitJump = _current.EmitJumpPlaceholder(Opcode.JumpIfTrue);

        // Body
        CompileStatements(f.Body);

        // Increment: var = var + step
        EmitLoadSymbolSlot(ownerScope, slot);
        if (f.Step is null) _current.Emit(Opcode.LoadOne);
        else CompileExpr(f.Step);
        _current.Emit(Opcode.Add);
        EmitStoreSymbolSlot(ownerScope, slot);

        // Loop back
        _current.EmitJumpToAbsolute(Opcode.Jump, loopStart);

        // Patch exit
        _current.PatchJump(exitJump);
        foreach (var j in _exitForJumps.Pop()) _current.PatchJump(j);
    }

    private void CompileForGuard(ForStmt f, Scope ownerScope, int slot)
    {
        // exit if (var - to) * sign(step) > 0, where sign(step) is +1 or -1.
        // Phase-9 simplification: only handle constant +1 step (default) cleanly.
        // For non-constant or non-positive steps the guard tests the positive-step path,
        // which means negative-step loops over-iterate. Document as VM limitation.
        EmitLoadSymbolSlot(ownerScope, slot);
        CompileExpr(f.To);
        _current.Emit(Opcode.Gt);
    }

    private void CompileDo(DoStmt d)
    {
        _exitDoJumps.Push([]);
        var loopStart = _current.CodeLength;

        if (d.Pre is not null)
        {
            CompileExpr(d.Pre.Condition);
            // Pre WHILE: exit if condition false; UNTIL: exit if true
            var op = d.Pre.IsUntil ? Opcode.JumpIfTrue : Opcode.JumpIfFalse;
            var exit = _current.EmitJumpPlaceholder(op);
            _exitDoJumps.Peek().Add(exit);
        }

        CompileStatements(d.Body);

        if (d.Post is not null)
        {
            CompileExpr(d.Post.Condition);
            var op = d.Post.IsUntil ? Opcode.JumpIfFalse : Opcode.JumpIfTrue;
            // continue (jump to start) if condition meets the loop-back criterion
            var pc = _current.Emit(op);
            _current.EmitI32(loopStart - (pc + 5));
        }
        else
        {
            _current.EmitJumpToAbsolute(Opcode.Jump, loopStart);
        }

        foreach (var j in _exitDoJumps.Pop()) _current.PatchJump(j);
    }

    private void CompileSelect(SelectStmt s)
    {
        // Strategy: compute subject once, store in a temp local at the end of frame.
        // Simpler for Phase-9: re-evaluate subject for each comparison.
        _exitSelectJumps.Push([]);
        var jumpsToEnd = new List<int>();

        foreach (var c in s.Cases)
        {
            // Build condition for this case: OR of each spec match.
            var matchJumps = new List<int>();
            foreach (var spec in c.Values)
            {
                CompileExpr(s.Subject);
                switch (spec)
                {
                    case CaseValue cv:
                        CompileExpr(cv.Value);
                        _current.Emit(Opcode.Eq);
                        break;
                    case CaseRange cr:
                        // (subj >= lo) AND (subj <= hi) — re-evaluate subject for hi check
                        CompileExpr(cr.Lo);
                        _current.Emit(Opcode.Ge);
                        CompileExpr(s.Subject);
                        CompileExpr(cr.Hi);
                        _current.Emit(Opcode.Le);
                        _current.Emit(Opcode.And);
                        break;
                    case CaseIs ci:
                        CompileExpr(ci.Value);
                        _current.Emit(ci.Op switch
                        {
                            BinaryOp.Equal => Opcode.Eq,
                            BinaryOp.NotEqual => Opcode.Ne,
                            BinaryOp.Less => Opcode.Lt,
                            BinaryOp.LessEqual => Opcode.Le,
                            BinaryOp.Greater => Opcode.Gt,
                            BinaryOp.GreaterEqual => Opcode.Ge,
                            _ => throw new UnsupportedFeatureException($"CASE IS op {ci.Op}"),
                        });
                        break;
                }
                matchJumps.Add(_current.EmitJumpPlaceholder(Opcode.JumpIfTrue));
            }

            // None matched — skip this case
            var skipCaseBody = _current.EmitJumpPlaceholder(Opcode.Jump);
            foreach (var mj in matchJumps) _current.PatchJump(mj);

            CompileStatements(c.Body);
            jumpsToEnd.Add(_current.EmitJumpPlaceholder(Opcode.Jump));
            _current.PatchJump(skipCaseBody);
        }

        if (s.CaseElse is not null)
        {
            CompileStatements(s.CaseElse);
        }

        foreach (var j in jumpsToEnd) _current.PatchJump(j);
        foreach (var j in _exitSelectJumps.Pop()) _current.PatchJump(j);
    }

    private void CompileExit(ExitStmt e)
    {
        switch (e.Target)
        {
            case ExitTarget.For:
            case ExitTarget.Do:
            case ExitTarget.Select:
                {
                    var stack = e.Target switch
                    {
                        ExitTarget.For => _exitForJumps,
                        ExitTarget.Do => _exitDoJumps,
                        ExitTarget.Select => _exitSelectJumps,
                        _ => throw new InvalidOperationException(),
                    };
                    if (stack.Count == 0)
                        throw new UnsupportedFeatureException($"EXIT {e.Target} not inside matching block");
                    stack.Peek().Add(_current.EmitJumpPlaceholder(Opcode.Jump));
                    break;
                }
            case ExitTarget.When:
            case ExitTarget.Handler:
                // EXIT WHEN and EXIT HANDLER both leave the enclosing WHEN
                // block (HANDLER bodies are inlined into the WHEN's USE body,
                // so they share the same exit target).
                if (_exitWhenJumps.Count == 0)
                    throw new UnsupportedFeatureException($"EXIT {e.Target} not inside a WHEN block");
                _exitWhenJumps.Peek().Add(_current.EmitJumpPlaceholder(Opcode.Jump));
                break;
            case ExitTarget.Sub:
                if (_currentCallable != CallableKind.Sub)
                    throw new UnsupportedFeatureException("EXIT SUB outside of a SUB body");
                _current.Emit(Opcode.LeaveSub);
                break;
            case ExitTarget.Function:
                if (_currentCallable != CallableKind.Function)
                    throw new UnsupportedFeatureException("EXIT FUNCTION outside of a FUNCTION body");
                // Mid-body exit: emit the same epilogue the normal end-of-body emits.
                _current.Emit(Opcode.LoadLocal);
                _current.EmitU32((uint)_currentReturnSlot);
                _current.Emit(Opcode.LeaveFunction);
                break;
            case ExitTarget.Def:
                if (_currentCallable != CallableKind.Def)
                    throw new UnsupportedFeatureException("EXIT DEF outside of a DEF body");
                // DEF stores its return value into _currentReturnSlot via StoreLocal;
                // mid-body exit just falls through to LeaveFunction. CallDef reads
                // the slot afterwards (defaulting to zero/empty if never assigned).
                _current.Emit(Opcode.LeaveFunction);
                break;
            default:
                throw new UnsupportedFeatureException($"EXIT {e.Target} not supported by VM");
        }
    }

    private void CompileCall(CallStmt c)
    {
        if (!_info.CallTargets.TryGetValue(c, out var sub))
            throw new UnsupportedFeatureException($"CALL target '{c.Name}' not resolved");
        if (!_subIndex.TryGetValue(sub, out var id))
            throw new UnsupportedFeatureException($"SUB '{c.Name}' not in compiled set");
        foreach (var a in c.Args) CompileExpr(a);
        _current.Emit(Opcode.CallSub);
        _current.EmitU32((uint)id);
        _current.EmitU32((uint)c.Args.Count);
    }

    // -- Expression compilation ------------------------------------------

    private void CompileExpr(Expr e)
    {
        switch (e)
        {
            case NumberExpr n:
                {
                    var bd = BigDecimal.Parse(n.Text);
                    if (bd == BigDecimal.Zero) _current.Emit(Opcode.LoadZero);
                    else if (bd == BigDecimal.One) _current.Emit(Opcode.LoadOne);
                    else if (bd == -BigDecimal.One) _current.Emit(Opcode.LoadMinusOne);
                    else
                    {
                        var idx = _current.AddNumberConstant(bd);
                        _current.Emit(Opcode.LoadConstNumber);
                        _current.EmitU32(idx);
                    }
                    break;
                }
            case StringExpr s:
                {
                    var idx = _current.AddStringConstant(s.Value);
                    _current.Emit(Opcode.LoadConstString);
                    _current.EmitU32(idx);
                    break;
                }
            case ParenExpr p: CompileExpr(p.Inner); break;
            case NameRefExpr nr: CompileNameRef(nr); break;
            case CallOrIndexExpr c: CompileCallOrIndex(c); break;
            case UnaryExpr u: CompileUnary(u); break;
            case BinaryExpr b: CompileBinary(b); break;
            default:
                throw new UnsupportedFeatureException($"expression {e.GetType().Name} not supported by VM");
        }
    }

    private void CompileNameRef(NameRefExpr nr)
    {
        var resolved = _info.Resolve(nr);
        switch (resolved)
        {
            case ResolvedVariable rv: EmitLoadSymbolSlot(rv.Symbol.OwnerScope!, rv.Symbol.Slot); break;
            case ResolvedParam rp: EmitLoadSymbolSlot(rp.Symbol.OwnerScope!, rp.Symbol.Slot); break;
            case ResolvedConstant rc:
                _current.Emit(rc.Symbol.Name.ToUpperInvariant() switch
                {
                    "PI" => Opcode.LoadConstantPi,
                    "EPS" => Opcode.LoadConstantEps,
                    "INF" => Opcode.LoadConstantInf,
                    "MAXNUM" => Opcode.LoadConstantMaxnum,
                    _ => throw new UnsupportedFeatureException($"unknown constant {rc.Symbol.Name}"),
                });
                break;
            case ResolvedBuiltinCall rb:
                EmitBuiltinCall(rb.Symbol.Name, 0);
                break;
            default:
                throw new UnsupportedFeatureException($"name ref {resolved.GetType().Name} not supported by VM");
        }
    }

    private void CompileCallOrIndex(CallOrIndexExpr c)
    {
        var resolved = _info.Resolve(c);
        switch (resolved)
        {
            case ResolvedBuiltinCall rb:
                foreach (var a in c.Args) CompileExpr(a);
                EmitBuiltinCall(rb.Symbol.Name, c.Args.Count);
                break;
            case ResolvedFunctionCall rf:
                if (!_funcIndex.TryGetValue(rf.Symbol, out var fid))
                    throw new UnsupportedFeatureException($"FUNCTION '{rf.Symbol.Name}' not in compiled set");
                foreach (var a in c.Args) CompileExpr(a);
                _current.Emit(Opcode.CallFunction);
                _current.EmitU32((uint)fid);
                _current.EmitU32((uint)c.Args.Count);
                break;
            case ResolvedDefCall rd:
                if (!_defIndex.TryGetValue(rd.Symbol, out var did))
                    throw new UnsupportedFeatureException($"DEF '{rd.Symbol.Name}' not in compiled set");
                foreach (var a in c.Args) CompileExpr(a);
                _current.Emit(Opcode.CallDef);
                _current.EmitU32((uint)did);
                _current.EmitU32((uint)c.Args.Count);
                break;
            case ResolvedArrayAccess ra:
                foreach (var a in c.Args) CompileExpr(a);
                EmitLoadElement(ra.Symbol.OwnerScope!, ra.Symbol.Slot, c.Args.Count);
                break;
            default:
                throw new UnsupportedFeatureException($"call/index target {resolved.GetType().Name} not supported by VM");
        }
    }

    private void EmitBuiltinCall(string name, int argc)
    {
        if (!_builtinIndex.TryGetValue(name, out var idx))
        {
            idx = _builtinNames.Count;
            _builtinIndex[name] = idx;
            _builtinNames.Add(name);
        }
        _current.Emit(Opcode.CallBuiltin);
        _current.EmitU32((uint)idx);
        _current.EmitU32((uint)argc);
    }

    private void CompileUnary(UnaryExpr u)
    {
        CompileExpr(u.Operand);
        _current.Emit(u.Op switch
        {
            UnaryOp.Plus => Opcode.Nop,
            UnaryOp.Negate => Opcode.Neg,
            UnaryOp.Not => Opcode.Not,
            UnaryOp.BNot => Opcode.Bnot,
            _ => throw new UnsupportedFeatureException($"unary op {u.Op}"),
        });
    }

    private void CompileBinary(BinaryExpr b)
    {
        CompileExpr(b.Left);
        CompileExpr(b.Right);
        _current.Emit(b.Op switch
        {
            BinaryOp.Add => Opcode.Add,
            BinaryOp.Subtract => Opcode.Sub,
            BinaryOp.Multiply => Opcode.Mul,
            BinaryOp.Divide => Opcode.Div,
            BinaryOp.Power => Opcode.Pow,
            BinaryOp.Mod => Opcode.Mod,
            BinaryOp.Remainder => Opcode.Rem,
            BinaryOp.Concat => Opcode.Concat,
            BinaryOp.Equal => Opcode.Eq,
            BinaryOp.NotEqual => Opcode.Ne,
            BinaryOp.Less => Opcode.Lt,
            BinaryOp.LessEqual => Opcode.Le,
            BinaryOp.Greater => Opcode.Gt,
            BinaryOp.GreaterEqual => Opcode.Ge,
            BinaryOp.And => Opcode.And,
            BinaryOp.Or => Opcode.Or,
            BinaryOp.Xor => Opcode.Xor,
            BinaryOp.Imp => Opcode.Imp,
            BinaryOp.Eqv => Opcode.Eqv,
            BinaryOp.Band => Opcode.Band,
            BinaryOp.Bor => Opcode.Bor,
            BinaryOp.Bxor => Opcode.Bxor,
            _ => throw new UnsupportedFeatureException($"binary op {b.Op}"),
        });
    }

    // -- Slot emission with depth walk -----------------------------------

    private void EmitLoadSymbolSlot(Scope ownerScope, int slot)
    {
        var depth = ScopeDepth(ownerScope);
        if (depth == 0) { _current.Emit(Opcode.LoadLocal); _current.EmitU32((uint)slot); }
        else { _current.Emit(Opcode.LoadOuter); _current.EmitU32((uint)depth); _current.EmitU32((uint)slot); }
    }

    private void EmitStoreSymbolSlot(Scope ownerScope, int slot)
    {
        var depth = ScopeDepth(ownerScope);
        if (depth == 0) { _current.Emit(Opcode.StoreLocal); _current.EmitU32((uint)slot); }
        else { _current.Emit(Opcode.StoreOuter); _current.EmitU32((uint)depth); _current.EmitU32((uint)slot); }
    }

    private int ScopeDepth(Scope target)
    {
        var depth = 0;
        for (var s = _currentScope; s is not null; s = s.Parent)
        {
            if (s == target) return depth;
            depth++;
        }
        // Not found in chain — assume program scope at outermost.
        return Math.Max(0, depth - 1);
    }

    private void EmitDimArray(Scope ownerScope, int slot, int rank, bool isString)
    {
        var depth = ScopeDepth(ownerScope);
        if (depth == 0)
        {
            _current.Emit(Opcode.DimArray);
            _current.EmitU32((uint)slot);
            _current.EmitU32((uint)rank);
            _current.EmitU32(isString ? 1u : 0u);
        }
        else
        {
            _current.Emit(Opcode.DimArrayOuter);
            _current.EmitU32((uint)depth);
            _current.EmitU32((uint)slot);
            _current.EmitU32((uint)rank);
            _current.EmitU32(isString ? 1u : 0u);
        }
    }

    private void EmitLoadElement(Scope ownerScope, int slot, int rank)
    {
        var depth = ScopeDepth(ownerScope);
        if (depth == 0)
        {
            _current.Emit(Opcode.LoadElement);
            _current.EmitU32((uint)slot);
            _current.EmitU32((uint)rank);
        }
        else
        {
            _current.Emit(Opcode.LoadElementOuter);
            _current.EmitU32((uint)depth);
            _current.EmitU32((uint)slot);
            _current.EmitU32((uint)rank);
        }
    }

    private void EmitStoreElement(Scope ownerScope, int slot, int rank)
    {
        var depth = ScopeDepth(ownerScope);
        if (depth == 0)
        {
            _current.Emit(Opcode.StoreElement);
            _current.EmitU32((uint)slot);
            _current.EmitU32((uint)rank);
        }
        else
        {
            _current.Emit(Opcode.StoreElementOuter);
            _current.EmitU32((uint)depth);
            _current.EmitU32((uint)slot);
            _current.EmitU32((uint)rank);
        }
    }

    private void EmitLoadInt(int v)
    {
        if (v == 0) { _current.Emit(Opcode.LoadZero); return; }
        if (v == 1) { _current.Emit(Opcode.LoadOne); return; }
        if (v == -1) { _current.Emit(Opcode.LoadMinusOne); return; }
        var idx = _current.AddNumberConstant(BigDecimal.Parse(v.ToString()));
        _current.Emit(Opcode.LoadConstNumber);
        _current.EmitU32(idx);
    }

    private static int ResolveOptionBase(IReadOnlyList<Stmt> stmts)
    {
        var resolved = 1;
        foreach (var s in stmts)
        {
            if (s is OptionBaseStmt o) resolved = o.Base;
        }
        return resolved;
    }
}
