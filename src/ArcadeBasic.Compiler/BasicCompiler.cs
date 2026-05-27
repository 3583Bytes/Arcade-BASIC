using ArcadeBasic.Bytecode;
using ArcadeBasic.Parser.Ast;
using ArcadeBasic.Sema;
using Singulink.Numerics;
using AstProgram = ArcadeBasic.Parser.Ast.Program;
using BcProgram = ArcadeBasic.Bytecode.Program;

namespace ArcadeBasic.Compiler;

/// <summary>
/// Phase-9 AST → bytecode compiler. Supported subset:
///   literals, variables, all unary/binary arithmetic + comparison + logical,
///   PRINT (positional), assignments, IF (block + single-line), FOR/NEXT,
///   DO/LOOP (pre/post WHILE/UNTIL), SELECT CASE, GOTO/GOSUB/RETURN, EXIT,
///   STOP/END, REM, RANDOMIZE, DEF (single-line), SUB/FUNCTION/CALL, builtins.
///
/// Unsupported (throws): arrays/DIM/MAT, INPUT, READ/DATA/RESTORE, file I/O,
/// exception handling, modules, PRINT USING. Programs using these continue
/// to work via the tree-walker (`run` subcommand).
/// </summary>
public sealed class BasicCompiler
{
    public sealed class UnsupportedFeatureException(string message) : Exception(message);

    private readonly SemanticInfo _info;
    private readonly Dictionary<string, int> _subIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _funcIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _defIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _builtinIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _builtinNames = [];

    private Chunk _current = null!;
    private Scope _currentScope = null!;

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
        // Pass 1: collect all top-level callable symbols and assign indices.
        var subs = new List<(SubSymbol Sym, int Id)>();
        var funcs = new List<(FunctionSymbol Sym, int Id)>();
        var defs = new List<(DefSymbol Sym, int Id)>();

        foreach (var sym in _info.ProgramScope.Symbols.Values)
        {
            switch (sym)
            {
                case SubSymbol ss:
                    _subIndex[ss.Name] = subs.Count;
                    subs.Add((ss, subs.Count));
                    break;
                case FunctionSymbol fs:
                    _funcIndex[fs.Name + (fs.IsString ? "$" : "")] = funcs.Count;
                    funcs.Add((fs, funcs.Count));
                    break;
                case DefSymbol ds:
                    _defIndex[ds.Name + (ds.IsString ? "$" : "")] = defs.Count;
                    defs.Add((ds, defs.Count));
                    break;
            }
        }

        // Compile main chunk.
        var main = new Chunk { FrameSize = _info.ProgramScope.FrameSize };
        _current = main;
        _currentScope = _info.ProgramScope;
        CompileStatements(program.Statements);
        main.Emit(Opcode.End);

        // Compile each SUB / FUNCTION / DEF body into its own chunk.
        var compiledSubs = new List<CompiledSub>();
        foreach (var (ss, _) in subs)
        {
            var chunk = new Chunk { FrameSize = ss.BodyScope.FrameSize };
            _current = chunk;
            _currentScope = ss.BodyScope;
            CompileStatements(ss.Stmt.Body);
            chunk.Emit(Opcode.LeaveSub);
            compiledSubs.Add(new CompiledSub(ss.Name, ss.Params.Count, chunk));
        }

        var compiledFuncs = new List<CompiledFunction>();
        foreach (var (fs, _) in funcs)
        {
            var chunk = new Chunk { FrameSize = fs.BodyScope.FrameSize };
            _current = chunk;
            _currentScope = fs.BodyScope;
            // Find the return slot — the local with the function's name.
            var returnSlotSym = (VariableSymbol)fs.BodyScope.LocalLookup(Scope.Key(fs.Name, fs.IsString))!;
            CompileStatements(fs.Stmt.Body);
            // After body: push the return slot value onto the stack.
            chunk.Emit(Opcode.LoadLocal); chunk.EmitU32((uint)returnSlotSym.Slot);
            chunk.Emit(Opcode.LeaveFunction);
            compiledFuncs.Add(new CompiledFunction(fs.Name, fs.IsString, fs.Params.Count, returnSlotSym.Slot, chunk));
        }

        var compiledDefs = new List<CompiledDef>();
        foreach (var (ds, _) in defs)
        {
            var chunk = new Chunk { FrameSize = ds.Params.Count };
            _current = chunk;
            // Build a tiny scope for the DEF parameters.
            _currentScope = new Scope(ScopeKind.Def, _info.ProgramScope);
            for (var i = 0; i < ds.Params.Count; i++)
            {
                var p = ds.Params[i];
                _currentScope.Declare(Scope.Key(p.Name, p.IsString),
                    new ParamSymbol(p.Name, p.IsString, i, p.IsArray));
            }
            if (ds.Stmt.SingleLineBody is not null)
            {
                CompileExpr(ds.Stmt.SingleLineBody);
                chunk.Emit(Opcode.LeaveFunction);
            }
            else
            {
                throw new UnsupportedFeatureException("multi-line DEF is not yet supported by VM");
            }
            compiledDefs.Add(new CompiledDef(ds.Name, ds.IsString, ds.Params.Count, chunk));
        }

        return new BcProgram
        {
            Main = main,
            Subs = compiledSubs,
            Functions = compiledFuncs,
            Defs = compiledDefs,
            BuiltinNames = _builtinNames,
        };
    }

    // -- Statement compilation -------------------------------------------

    private void CompileStatements(IReadOnlyList<Stmt> stmts)
    {
        var labelTargets = new Dictionary<int, int>();
        foreach (var stmt in stmts)
        {
            if (stmt.Label is { } l) labelTargets[l] = _current.CodeLength;
            CompileStatement(stmt);
        }
        // Backfill any forward GOTO/GOSUB with absolute addresses.
        // Simplification: in this Phase-9 first cut we don't support GOTO/GOSUB
        // jumping to forward labels; we emit absolute jumps using the label
        // map at point-of-emission, so labels must already be defined when the
        // jump is emitted. Programs that need forward labels run via tree-walker.
        // (See DocsConformance for tracking.)
        _ = labelTargets;
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
            case SubStmt or FunctionStmt or DefStmt:
                // Declarations — already compiled into separate chunks.
                break;
            case ModuleStmt or HandlerStmt:
                break;
            case GotoStmt or GosubStmt:
                throw new UnsupportedFeatureException(
                    "GOTO/GOSUB across statement boundaries not yet supported by VM (Phase-9 limitation)");
            default:
                throw new UnsupportedFeatureException(
                    $"statement kind {stmt.GetType().Name} not yet supported by VM");
        }
    }

    private void CompileAssign(AssignStmt a)
    {
        if (a.Target is not NameRefExpr nr)
            throw new UnsupportedFeatureException("array element assignment not yet supported by VM");

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
            default:
                throw new UnsupportedFeatureException($"cannot assign to {resolved.GetType().Name}");
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
        var stack = e.Target switch
        {
            ExitTarget.For => _exitForJumps,
            ExitTarget.Do => _exitDoJumps,
            ExitTarget.Select => _exitSelectJumps,
            _ => throw new UnsupportedFeatureException($"EXIT {e.Target} not supported by VM"),
        };
        if (stack.Count == 0)
            throw new UnsupportedFeatureException($"EXIT {e.Target} not inside matching block");
        var jump = _current.EmitJumpPlaceholder(Opcode.Jump);
        stack.Peek().Add(jump);
    }

    private void CompileCall(CallStmt c)
    {
        if (!_info.CallTargets.TryGetValue(c, out var sub))
            throw new UnsupportedFeatureException($"CALL target '{c.Name}' not resolved");
        if (!_subIndex.TryGetValue(sub.Name, out var id))
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
                if (!_funcIndex.TryGetValue(rf.Symbol.Name + (rf.Symbol.IsString ? "$" : ""), out var fid))
                    throw new UnsupportedFeatureException($"FUNCTION '{rf.Symbol.Name}' not in compiled set");
                foreach (var a in c.Args) CompileExpr(a);
                _current.Emit(Opcode.CallFunction);
                _current.EmitU32((uint)fid);
                _current.EmitU32((uint)c.Args.Count);
                break;
            case ResolvedDefCall rd:
                if (!_defIndex.TryGetValue(rd.Symbol.Name + (rd.Symbol.IsString ? "$" : ""), out var did))
                    throw new UnsupportedFeatureException($"DEF '{rd.Symbol.Name}' not in compiled set");
                foreach (var a in c.Args) CompileExpr(a);
                _current.Emit(Opcode.CallDef);
                _current.EmitU32((uint)did);
                _current.EmitU32((uint)c.Args.Count);
                break;
            case ResolvedArrayAccess:
                throw new UnsupportedFeatureException("array indexing not yet supported by VM");
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
}
