using ArcadeBasic.Core;
using ArcadeBasic.Parser.Ast;

namespace ArcadeBasic.Sema;

/// <summary>
/// Two-pass semantic analyzer.
///
/// Pass 1 collects top-level / hoisted declarations: SUB / FUNCTION / DEF
/// signatures, DIM array declarations, line labels, DATA items, and the
/// builtin/constant registry. This makes forward references work — Pass 2
/// can resolve a call to a SUB defined later in the file.
///
/// Pass 2 walks every statement and expression, introducing implicit
/// variables on first reference, resolving names to symbols, doing basic
/// numeric-vs-string type checking, and resolving line labels for GOTO/GOSUB.
/// Resolution info is stashed in a side table keyed by AST node identity.
/// </summary>
public sealed class Analyzer
{
    // Diagnostic codes (FB03xx range = sema)
    public const string ErrUndefinedName = "FB0301";
    public const string ErrTypeMismatch = "FB0302";
    public const string ErrArityMismatch = "FB0303";
    public const string ErrDuplicateDeclaration = "FB0304";
    public const string ErrUndefinedLineLabel = "FB0305";
    public const string ErrInvalidAssignmentTarget = "FB0306";
    public const string ErrCannotCall = "FB0307";
    public const string WarnImplicitVariable = "FB0308";
    public const string ErrInvalidStringOp = "FB0309";

    private readonly DiagnosticBag _diags;
    private readonly Dictionary<Expr, ResolvedRef> _resolutions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr, BasicType> _types = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, Stmt> _labels = new();
    private readonly List<DataItem> _dataPool = new();
    private readonly Dictionary<ModuleStmt, Scope> _moduleScopes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<CallStmt, SubSymbol> _callTargets = new(ReferenceEqualityComparer.Instance);

    private Analyzer(DiagnosticBag diagnostics)
    {
        _diags = diagnostics;
    }

    public static SemanticInfo Analyze(Program program, DiagnosticBag diagnostics)
    {
        var a = new Analyzer(diagnostics);
        var scope = new Scope(ScopeKind.Program);
        a.PreloadBuiltins(scope);
        a.Pass1(program.Statements, scope);
        a.Pass2(program.Statements, scope);

        return new SemanticInfo
        {
            ProgramScope = scope,
            Resolutions = a._resolutions,
            ExpressionTypes = a._types,
            DataPool = a._dataPool,
            LineLabels = a._labels,
            CallTargets = a._callTargets,
            ModuleScopes = a._moduleScopes,
        };
    }

    private void PreloadBuiltins(Scope scope)
    {
        foreach (var sym in Builtins.All())
        {
            scope.Declare(Scope.Key(sym.Name, sym.IsString), sym);
        }
    }

    // -- Pass 1: collect declarations -----------------------------------

    private void Pass1(IEnumerable<Stmt> stmts, Scope scope)
    {
        foreach (var stmt in stmts)
        {
            Pass1Stmt(stmt, scope);
        }
    }

    private void Pass1Stmt(Stmt stmt, Scope scope)
    {
        // Record line label → stmt mapping (program-level only is sufficient
        // for GOTO/GOSUB; nested labels are unusual but we don't reject them).
        if (stmt.Label is { } label)
        {
            if (!_labels.TryAdd(label, stmt))
            {
                _diags.Error(ErrDuplicateDeclaration, stmt.Span,
                    $"line label {label} appears more than once");
            }
        }

        switch (stmt)
        {
            case DimStmt dim:
                foreach (var spec in dim.Specs)
                {
                    DeclareArray(scope, spec);
                }
                break;

            case DataStmt data:
                _dataPool.AddRange(data.Items);
                break;

            case SubStmt sub:
            {
                var bodyScope = new Scope(ScopeKind.Sub, scope);
                DeclareParams(bodyScope, sub.Params);
                var sym = new SubSymbol(sub.Name, sub.Params, bodyScope, sub);
                if (!scope.Declare(Scope.Key(sub.Name, isString: false), sym))
                {
                    _diags.Error(ErrDuplicateDeclaration, sub.Span,
                        $"SUB '{sub.Name}' redeclares an existing name");
                }
                Pass1(sub.Body, bodyScope);
                break;
            }

            case FunctionStmt fn:
            {
                var bodyScope = new Scope(ScopeKind.Function, scope);
                DeclareParams(bodyScope, fn.Params);
                // The function's own name is also a slot in its body scope —
                // assignments to it inside the body are how we set the return value.
                bodyScope.Declare(Scope.Key(fn.Name, fn.IsString),
                    new VariableSymbol(fn.Name, fn.IsString, bodyScope.AllocateSlot()));
                var sym = new FunctionSymbol(fn.Name, fn.IsString, fn.Params, bodyScope, fn);
                if (!scope.Declare(Scope.Key(fn.Name, fn.IsString), sym))
                {
                    _diags.Error(ErrDuplicateDeclaration, fn.Span,
                        $"FUNCTION '{fn.Name}' redeclares an existing name");
                }
                Pass1(fn.Body, bodyScope);
                break;
            }

            case DefStmt def:
            {
                // Build the DEF's body scope here so it has a stable identity
                // both phases can refer to: sema's Pass2 resolves references
                // against it, and the bytecode compiler uses it as _currentScope
                // when emitting the body so ScopeDepth resolves params at depth 0.
                var defScope = new Scope(ScopeKind.Def, scope);
                foreach (var p in def.Params)
                {
                    defScope.Declare(Scope.Key(p.Name, p.IsString),
                        new ParamSymbol(p.Name, p.IsString, defScope.AllocateSlot(), p.IsArray));
                }
                var sym = new DefSymbol(def.Name, def.IsString, def.Params, defScope, def);
                if (!scope.Declare(Scope.Key(def.Name, def.IsString), sym))
                {
                    _diags.Error(ErrDuplicateDeclaration, def.Span,
                        $"DEF '{def.Name}' redeclares an existing name");
                }
                break;
            }

            case IfStmt ifs:
                Pass1(ifs.ThenBlock, scope);
                foreach (var ei in ifs.ElseIfs) Pass1(ei.Body, scope);
                if (ifs.ElseBlock is not null) Pass1(ifs.ElseBlock, scope);
                break;

            case ForStmt f: Pass1(f.Body, scope); break;
            case DoStmt d: Pass1(d.Body, scope); break;
            case SelectStmt s:
                foreach (var c in s.Cases) Pass1(c.Body, scope);
                if (s.CaseElse is not null) Pass1(s.CaseElse, scope);
                break;

            case HandlerStmt h:
                if (!scope.Declare(Scope.Key(h.Name, isString: false), new HandlerSymbol(h.Name, h)))
                {
                    _diags.Error(ErrDuplicateDeclaration, h.Span,
                        $"HANDLER '{h.Name}' redeclares an existing name");
                }
                Pass1(h.Body, scope);
                break;

            case WhenStmt w:
                Pass1(w.InBody, scope);
                if (w.UseBody is not null) Pass1(w.UseBody, scope);
                break;

            case ModuleStmt mod:
                Pass1Module(mod, scope);
                break;
        }
    }

    private void Pass1Module(ModuleStmt mod, Scope parent)
    {
        var modScope = new Scope(ScopeKind.Module, parent);
        _moduleScopes[mod] = modScope;
        Pass1(mod.Body, modScope);

        // Re-export PUBLIC SUB/FUNCTION/DEF symbols into the parent scope so
        // they can be called from outside the module. Module-private
        // declarations stay only in modScope.
        foreach (var (key, sym) in modScope.Symbols.ToList())
        {
            var isPublic = sym switch
            {
                SubSymbol ss => ss.Stmt.IsPublic,
                FunctionSymbol fs => fs.Stmt.IsPublic,
                DefSymbol ds => ds.Stmt.IsPublic,
                _ => false,
            };
            if (isPublic && parent.LocalLookup(key) is null)
            {
                parent.Declare(key, sym);
            }
        }
    }

    private void DeclareArray(Scope scope, DimSpec spec)
    {
        var key = Scope.Key(spec.Name, spec.IsString);
        if (scope.LocalLookup(key) is not null)
        {
            _diags.Error(ErrDuplicateDeclaration, spec.Span,
                $"'{spec.Name}{(spec.IsString ? "$" : "")}' is declared more than once");
            return;
        }
        var slot = scope.AllocateSlot();
        scope.Declare(key, new ArraySymbol(spec.Name, spec.IsString, slot, spec));
    }

    private void DeclareParams(Scope scope, IReadOnlyList<Param> ps)
    {
        foreach (var p in ps)
        {
            var key = Scope.Key(p.Name, p.IsString);
            if (scope.LocalLookup(key) is not null)
            {
                _diags.Error(ErrDuplicateDeclaration, p.Span,
                    $"parameter '{p.Name}' is declared more than once");
                continue;
            }
            scope.Declare(key, new ParamSymbol(p.Name, p.IsString, scope.AllocateSlot(), p.IsArray));
        }
    }

    // -- Pass 2: resolve references -------------------------------------

    private void Pass2(IEnumerable<Stmt> stmts, Scope scope)
    {
        foreach (var stmt in stmts)
        {
            AnalyzeStmt(stmt, scope);
        }
    }

    private void AnalyzeStmt(Stmt stmt, Scope scope)
    {
        switch (stmt)
        {
            case AssignStmt a:
                AnalyzeAssignment(a, scope);
                break;

            case PrintUsingStmt pu:
                ExpectType(pu.Format, AnalyzeExpr(pu.Format, scope), BasicType.String, "PRINT USING format");
                foreach (var item in pu.Items) AnalyzeExpr(item, scope);
                break;

            case PrintStmt p:
                foreach (var item in p.Items)
                {
                    switch (item)
                    {
                        case PrintExprItem ei: AnalyzeExpr(ei.Value, scope); break;
                        case PrintTab t:
                            var ty = AnalyzeExpr(t.Column, scope);
                            ExpectType(t.Column, ty, BasicType.Numeric, "TAB column");
                            break;
                    }
                }
                break;

            case InputStmt i:
                if (i.Prompt is not null) AnalyzeExpr(i.Prompt, scope);
                foreach (var t in i.Targets) AnalyzeAssignableTarget(t, scope);
                break;

            case LineInputStmt li:
                if (li.Prompt is not null) AnalyzeExpr(li.Prompt, scope);
                AnalyzeAssignableTarget(li.Target, scope);
                if (TypeOfTarget(li.Target) != BasicType.String)
                {
                    _diags.Error(ErrTypeMismatch, li.Target.Span,
                        "LINE INPUT target must be a string variable");
                }
                break;

            case ReadStmt r:
                foreach (var t in r.Targets) AnalyzeAssignableTarget(t, scope);
                break;

            case DataStmt: /* already collected in Pass1 */ break;

            case RestoreStmt rs:
                if (rs.LabelTarget is not null)
                {
                    AnalyzeExpr(rs.LabelTarget, scope);
                    if (rs.LabelTarget is NumberExpr n && int.TryParse(n.Text, out var lbl) && !_labels.ContainsKey(lbl))
                    {
                        _diags.Error(ErrUndefinedLineLabel, n.Span, $"line label {lbl} not found");
                    }
                }
                break;

            case GotoStmt g: AnalyzeLabelTarget(g.LabelTarget, scope); break;
            case GosubStmt g: AnalyzeLabelTarget(g.LabelTarget, scope); break;

            case ReturnStmt: case StopStmt: case EndStmt: case EndBlockStmt: case RunStmt:
            case RemStmt: case OptionBaseStmt: case OptionArithmeticStmt:
            case ExitStmt: case NextStmt: case LoopStmt:
                break;

            case RandomizeStmt rnd:
                if (rnd.Seed is not null)
                {
                    var ty = AnalyzeExpr(rnd.Seed, scope);
                    ExpectType(rnd.Seed, ty, BasicType.Numeric, "RANDOMIZE seed");
                }
                break;

            case DimStmt dim:
                // Bounds expressions evaluated at runtime; resolve names here.
                foreach (var spec in dim.Specs)
                {
                    foreach (var b in spec.Bounds)
                    {
                        if (b.Lower is not null)
                        {
                            var ty = AnalyzeExpr(b.Lower, scope);
                            ExpectType(b.Lower, ty, BasicType.Numeric, "array lower bound");
                        }
                        var tu = AnalyzeExpr(b.Upper, scope);
                        ExpectType(b.Upper, tu, BasicType.Numeric, "array upper bound");
                    }
                }
                break;

            case IfStmt ifs:
                var ct = AnalyzeExpr(ifs.Condition, scope);
                ExpectType(ifs.Condition, ct, BasicType.Numeric, "IF condition");
                foreach (var t in ifs.ThenBlock) AnalyzeStmt(t, scope);
                foreach (var ei in ifs.ElseIfs)
                {
                    var et = AnalyzeExpr(ei.Condition, scope);
                    ExpectType(ei.Condition, et, BasicType.Numeric, "ELSEIF condition");
                    foreach (var t in ei.Body) AnalyzeStmt(t, scope);
                }
                if (ifs.ElseBlock is not null) foreach (var t in ifs.ElseBlock) AnalyzeStmt(t, scope);
                break;

            case ForStmt f:
                IntroduceVariableIfNeeded(f.Variable, scope);
                _resolutions[f.Variable] = ResolveNameRef(f.Variable, scope) ?? new ResolvedError("for-var");
                _types[f.Variable] = BasicType.Numeric;
                ExpectType(f.From, AnalyzeExpr(f.From, scope), BasicType.Numeric, "FOR from-value");
                ExpectType(f.To, AnalyzeExpr(f.To, scope), BasicType.Numeric, "FOR to-value");
                if (f.Step is not null) ExpectType(f.Step, AnalyzeExpr(f.Step, scope), BasicType.Numeric, "FOR step");
                foreach (var t in f.Body) AnalyzeStmt(t, scope);
                break;

            case DoStmt dst:
                if (dst.Pre is not null) AnalyzeExpr(dst.Pre.Condition, scope);
                foreach (var t in dst.Body) AnalyzeStmt(t, scope);
                if (dst.Post is not null) AnalyzeExpr(dst.Post.Condition, scope);
                break;

            case SelectStmt sl:
                AnalyzeExpr(sl.Subject, scope);
                foreach (var c in sl.Cases)
                {
                    foreach (var v in c.Values)
                    {
                        switch (v)
                        {
                            case CaseValue cv: AnalyzeExpr(cv.Value, scope); break;
                            case CaseRange cr: AnalyzeExpr(cr.Lo, scope); AnalyzeExpr(cr.Hi, scope); break;
                            case CaseIs ci: AnalyzeExpr(ci.Value, scope); break;
                        }
                    }
                    foreach (var t in c.Body) AnalyzeStmt(t, scope);
                }
                if (sl.CaseElse is not null) foreach (var t in sl.CaseElse) AnalyzeStmt(t, scope);
                break;

            case SubStmt sub:
            {
                if (scope.LocalLookup(Scope.Key(sub.Name, false)) is SubSymbol ss)
                {
                    foreach (var t in sub.Body) AnalyzeStmt(t, ss.BodyScope);
                }
                break;
            }
            case FunctionStmt fn:
            {
                if (scope.LocalLookup(Scope.Key(fn.Name, fn.IsString)) is FunctionSymbol fs)
                {
                    foreach (var t in fn.Body) AnalyzeStmt(t, fs.BodyScope);
                }
                break;
            }
            case DefStmt def:
            {
                // The defScope is built once in Pass1 (and stored on DefSymbol)
                // so name resolution and the bytecode compiler agree on which
                // Scope owns the parameters.
                if (scope.LocalLookup(Scope.Key(def.Name, def.IsString)) is not DefSymbol defSym)
                    break;
                var defScope = defSym.BodyScope;
                if (def.SingleLineBody is not null)
                {
                    var bt = AnalyzeExpr(def.SingleLineBody, defScope);
                    var expected = def.IsString ? BasicType.String : BasicType.Numeric;
                    ExpectType(def.SingleLineBody, bt, expected, $"DEF {def.Name} body");
                }
                if (def.MultiLineBody is not null)
                {
                    foreach (var t in def.MultiLineBody) AnalyzeStmt(t, defScope);
                }
                break;
            }
            case CallStmt call:
            {
                var sym = scope.Lookup(Scope.Key(call.Name, isString: false));
                if (sym is not SubSymbol ss)
                {
                    _diags.Error(ErrUndefinedName, call.Span, $"SUB '{call.Name}' is not defined");
                }
                else
                {
                    _callTargets[call] = ss;
                    if (ss.Params.Count != call.Args.Count)
                    {
                        _diags.Error(ErrArityMismatch, call.Span,
                            $"SUB '{call.Name}' expects {ss.Params.Count} arg(s), got {call.Args.Count}");
                    }
                }
                foreach (var a in call.Args) AnalyzeExpr(a, scope);
                break;
            }
            case MatAssignStmt mat:
                CheckMatTarget(mat.TargetName, mat.TargetIsString, mat.Span, scope);
                CheckMatRhs(mat.Rhs, mat.TargetIsString, scope);
                break;

            case MatRedimStmt mr:
                CheckMatTarget(mr.TargetName, mr.TargetIsString, mr.Span, scope);
                foreach (var b in mr.Bounds)
                {
                    if (b.Lower is not null) ExpectType(b.Lower, AnalyzeExpr(b.Lower, scope), BasicType.Numeric, "MAT REDIM lower bound");
                    ExpectType(b.Upper, AnalyzeExpr(b.Upper, scope), BasicType.Numeric, "MAT REDIM upper bound");
                }
                break;

            case MatInputStmt mi: CheckMatTarget(mi.TargetName, mi.TargetIsString, mi.Span, scope); break;
            case MatPrintStmt mp: CheckMatTarget(mp.TargetName, mp.TargetIsString, mp.Span, scope); break;
            case MatReadStmt mrd: CheckMatTarget(mrd.TargetName, mrd.TargetIsString, mrd.Span, scope); break;

            case WhenStmt w:
                foreach (var t in w.InBody) AnalyzeStmt(t, scope);
                if (w.UseBody is not null)
                {
                    foreach (var t in w.UseBody) AnalyzeStmt(t, scope);
                }
                else if (w.UseHandlerName is not null)
                {
                    var hsym = scope.Lookup(Scope.Key(w.UseHandlerName, isString: false));
                    if (hsym is not HandlerSymbol)
                    {
                        _diags.Error(ErrUndefinedName, w.Span,
                            $"HANDLER '{w.UseHandlerName}' is not defined");
                    }
                }
                break;

            case HandlerStmt hs:
                foreach (var t in hs.Body) AnalyzeStmt(t, scope);
                break;

            case CauseStmt cause:
                ExpectType(cause.Type, AnalyzeExpr(cause.Type, scope), BasicType.Numeric, "CAUSE EXCEPTION type");
                break;

            case RetryStmt: case ContinueResumeStmt:
                // Validity (must be inside a USE handler) is enforced at runtime.
                break;

            case ModuleStmt mod:
                if (_moduleScopes.TryGetValue(mod, out var modScope))
                {
                    foreach (var t in mod.Body) AnalyzeStmt(t, modScope);
                }
                break;

            case OpenStmt op:
                ExpectType(op.Channel, AnalyzeExpr(op.Channel, scope), BasicType.Numeric, "OPEN channel");
                ExpectType(op.Name, AnalyzeExpr(op.Name, scope), BasicType.String, "OPEN file name");
                break;
            case CloseStmt cs:
                ExpectType(cs.Channel, AnalyzeExpr(cs.Channel, scope), BasicType.Numeric, "CLOSE channel");
                break;
            case PrintFileStmt pf:
                ExpectType(pf.Channel, AnalyzeExpr(pf.Channel, scope), BasicType.Numeric, "PRINT # channel");
                foreach (var item in pf.Items)
                {
                    if (item is PrintExprItem ei) AnalyzeExpr(ei.Value, scope);
                }
                break;
            case InputFileStmt ifs:
                ExpectType(ifs.Channel, AnalyzeExpr(ifs.Channel, scope), BasicType.Numeric, "INPUT # channel");
                foreach (var t in ifs.Targets) AnalyzeAssignableTarget(t, scope);
                break;
            case LineInputFileStmt li2:
                ExpectType(li2.Channel, AnalyzeExpr(li2.Channel, scope), BasicType.Numeric, "LINE INPUT # channel");
                AnalyzeAssignableTarget(li2.Target, scope);
                if (TypeOfTarget(li2.Target) != BasicType.String)
                {
                    _diags.Error(ErrTypeMismatch, li2.Target.Span,
                        "LINE INPUT # target must be a string variable");
                }
                break;

            default:
                // Other statement kinds we don't yet handle in sema (file I/O,
                // exception handlers, modules) fall through silently for now.
                break;
        }
    }

    private void CheckMatTarget(string name, bool isString, SourceSpan span, Scope scope)
    {
        var sym = scope.Lookup(Scope.Key(name, isString));
        if (sym is null)
        {
            _diags.Error(ErrUndefinedName, span,
                $"MAT target '{name}{(isString ? "$" : "")}' is undeclared",
                "explicit DIM is required for arrays");
        }
        else if (sym is not ArraySymbol)
        {
            _diags.Error(ErrInvalidAssignmentTarget, span,
                $"MAT target '{name}' must be an array");
        }
    }

    private void CheckMatRhs(MatRhs rhs, bool targetIsString, Scope scope)
    {
        switch (rhs)
        {
            case MatRhsName n:
                if (n.IsString != targetIsString)
                {
                    _diags.Error(ErrTypeMismatch, n.Span,
                        $"MAT operand '{n.Name}' type does not match target");
                }
                if (scope.Lookup(Scope.Key(n.Name, n.IsString)) is not ArraySymbol)
                {
                    _diags.Error(ErrUndefinedName, n.Span,
                        $"MAT operand '{n.Name}' is not a declared array");
                }
                break;

            case MatRhsBinary b:
                if (targetIsString)
                {
                    _diags.Error(ErrInvalidStringOp, b.Span,
                        "MAT arithmetic is not allowed on string arrays");
                    return;
                }
                CheckMatRhs(b.Left, targetIsString, scope);
                CheckMatRhs(b.Right, targetIsString, scope);
                break;

            case MatRhsScalarMul sm:
                if (targetIsString)
                {
                    _diags.Error(ErrInvalidStringOp, sm.Span,
                        "MAT scalar multiply is not allowed on string arrays");
                    return;
                }
                ExpectType(sm.Scalar, AnalyzeExpr(sm.Scalar, scope), BasicType.Numeric, "MAT scalar multiplier");
                CheckMatRhs(sm.Matrix, targetIsString, scope);
                break;

            case MatRhsInv:
            case MatRhsTrn:
                if (targetIsString)
                {
                    _diags.Error(ErrInvalidStringOp, rhs.Span,
                        "MAT INV / TRN are not allowed on string arrays");
                    return;
                }
                CheckMatRhs(rhs is MatRhsInv inv ? inv.Operand : ((MatRhsTrn)rhs).Operand, targetIsString, scope);
                break;

            case MatRhsConst c:
                var stringConst = c.Kind == MatConstKind.NullString;
                if (stringConst != targetIsString)
                {
                    _diags.Error(ErrTypeMismatch, c.Span,
                        $"MAT constant {c.Kind} requires {(stringConst ? "string" : "numeric")} target");
                }
                break;
        }
    }

    private void AnalyzeAssignment(AssignStmt a, Scope scope)
    {
        AnalyzeAssignableTarget(a.Target, scope);
        var rhsType = AnalyzeExpr(a.Value, scope);
        var lhsType = TypeOfTarget(a.Target);
        if (lhsType != rhsType)
        {
            _diags.Error(ErrTypeMismatch, a.Span,
                $"cannot assign {rhsType.ToString().ToLowerInvariant()} value to {lhsType.ToString().ToLowerInvariant()} target");
        }
    }

    private void AnalyzeAssignableTarget(Expr target, Scope scope)
    {
        switch (target)
        {
            case NameRefExpr n:
                IntroduceVariableIfNeeded(n, scope);
                _resolutions[n] = ResolveNameRef(n, scope) ?? new ResolvedError("name");
                _types[n] = n.IsString ? BasicType.String : BasicType.Numeric;
                break;

            case CallOrIndexExpr c:
                {
                    // Subscripted assignment: target must be an array.
                    var sym = scope.Lookup(Scope.Key(c.Name, c.IsString));
                    if (sym is null)
                    {
                        // Implicit array introduction is a spec feature; emit a
                        // warning and decline to introduce here (DIM is required
                        // in our impl; sema warnings on usage would be in pass 1).
                        _diags.Error(ErrUndefinedName, c.Span,
                            $"undeclared array '{c.Name}{(c.IsString ? "$" : "")}'",
                            "explicit DIM is required for arrays in this implementation");
                        _resolutions[c] = new ResolvedError("array");
                    }
                    else if (sym is ArraySymbol arr)
                    {
                        _resolutions[c] = new ResolvedArrayAccess(arr);
                    }
                    else
                    {
                        _diags.Error(ErrInvalidAssignmentTarget, c.Span,
                            $"'{c.Name}' is not an array (cannot assign to indexed value)");
                        _resolutions[c] = new ResolvedError("not-array");
                    }
                    foreach (var idx in c.Args)
                    {
                        var ty = AnalyzeExpr(idx, scope);
                        ExpectType(idx, ty, BasicType.Numeric, "array index");
                    }
                    _types[c] = c.IsString ? BasicType.String : BasicType.Numeric;
                    break;
                }

            default:
                _diags.Error(ErrInvalidAssignmentTarget, target.Span,
                    "assignment target must be a variable name or array element");
                break;
        }
    }

    private void IntroduceVariableIfNeeded(NameRefExpr n, Scope scope)
    {
        var key = Scope.Key(n.Name, n.IsString);
        if (scope.Lookup(key) is not null) return;
        var slot = scope.AllocateSlot();
        scope.Declare(key, new VariableSymbol(n.Name, n.IsString, slot));
    }

    private void AnalyzeLabelTarget(Expr target, Scope scope)
    {
        AnalyzeExpr(target, scope);
        if (target is NumberExpr ne && int.TryParse(ne.Text, out var lbl) && !_labels.ContainsKey(lbl))
        {
            _diags.Error(ErrUndefinedLineLabel, ne.Span, $"line label {lbl} not found");
        }
    }

    // -- Expressions -----------------------------------------------------

    private BasicType AnalyzeExpr(Expr expr, Scope scope)
    {
        BasicType ty;
        switch (expr)
        {
            case NumberExpr: ty = BasicType.Numeric; break;
            case StringExpr: ty = BasicType.String; break;
            case ParenExpr p: ty = AnalyzeExpr(p.Inner, scope); break;

            case NameRefExpr n:
                ty = AnalyzeNameRef(n, scope);
                break;

            case CallOrIndexExpr c:
                ty = AnalyzeCallOrIndex(c, scope);
                break;

            case UnaryExpr u:
            {
                var inner = AnalyzeExpr(u.Operand, scope);
                if (u.Op is UnaryOp.Plus or UnaryOp.Negate or UnaryOp.Not or UnaryOp.BNot)
                {
                    ExpectType(u.Operand, inner, BasicType.Numeric, $"unary {u.Op}");
                }
                ty = BasicType.Numeric;
                break;
            }

            case BinaryExpr b:
                ty = AnalyzeBinary(b, scope);
                break;

            default:
                ty = BasicType.Numeric;
                break;
        }
        _types[expr] = ty;
        return ty;
    }

    private BasicType AnalyzeNameRef(NameRefExpr n, Scope scope)
    {
        var key = Scope.Key(n.Name, n.IsString);
        var sym = scope.Lookup(key);
        if (sym is null)
        {
            // Read-before-write: introduce as a default-initialized variable
            // in the nearest non-builtin scope. Spec-allowed but we warn.
            // Implicit declarations from a *read* go to program scope, so
            // names referenced inside DEF/SUB/FUNCTION bodies close over the
            // surrounding program-level variables rather than allocating a
            // shadowed local.
            var declScope = ProgramScope(scope);
            var slot = declScope.AllocateSlot();
            var v = new VariableSymbol(n.Name, n.IsString, slot) { OwnerScope = declScope };
            declScope.Declare(key, v);
            _diags.Warning(WarnImplicitVariable, n.Span,
                $"implicit declaration of '{n.Name}{(n.IsString ? "$" : "")}'",
                "consider DIM-ing arrays or LET-ing scalars before use");
            _resolutions[n] = new ResolvedVariable(v);
            return n.IsString ? BasicType.String : BasicType.Numeric;
        }

        switch (sym)
        {
            case VariableSymbol v: _resolutions[n] = new ResolvedVariable(v); return v.IsString ? BasicType.String : BasicType.Numeric;
            case ParamSymbol p: _resolutions[n] = new ResolvedParam(p); return p.IsString ? BasicType.String : BasicType.Numeric;
            case ConstantSymbol c: _resolutions[n] = new ResolvedConstant(c); return c.IsString ? BasicType.String : BasicType.Numeric;
            case BuiltinSymbol bsym when bsym.Signature.MinArgs == 0:
                // 0-arg builtin used without parens — equivalent to a parameterless call.
                _resolutions[n] = new ResolvedBuiltinCall(bsym);
                return bsym.IsString ? BasicType.String : BasicType.Numeric;
            case ArraySymbol:
                _diags.Error(ErrCannotCall, n.Span,
                    $"'{n.Name}' is an array; index it with parentheses");
                _resolutions[n] = new ResolvedError("array-without-index");
                return n.IsString ? BasicType.String : BasicType.Numeric;
            default:
                _diags.Error(ErrCannotCall, n.Span, $"'{n.Name}' cannot be used as a value");
                _resolutions[n] = new ResolvedError("non-value");
                return BasicType.Numeric;
        }
    }

    private BasicType AnalyzeCallOrIndex(CallOrIndexExpr c, Scope scope)
    {
        var key = Scope.Key(c.Name, c.IsString);
        var sym = scope.Lookup(key);

        // Inside a FUNCTION body, the function name itself is also a slot
        // (for setting the return value), but a *call* using that name should
        // bind to the parent-scope FunctionSymbol — that's how recursion works.
        if (sym is VariableSymbol vsym
            && vsym.OwnerScope?.Parent?.Lookup(key) is FunctionSymbol fnInParent
            && fnInParent.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase))
        {
            sym = fnInParent;
        }

        // Resolve args first so partial errors still record argument types.
        var argTypes = new BasicType[c.Args.Count];
        for (var i = 0; i < c.Args.Count; i++)
        {
            argTypes[i] = AnalyzeExpr(c.Args[i], scope);
        }

        if (sym is null)
        {
            _diags.Error(ErrUndefinedName, c.Span,
                $"undefined name '{c.Name}{(c.IsString ? "$" : "")}'");
            _resolutions[c] = new ResolvedError("undefined");
            return c.IsString ? BasicType.String : BasicType.Numeric;
        }

        switch (sym)
        {
            case ArraySymbol arr:
                // Index args must be numeric.
                for (var i = 0; i < c.Args.Count; i++)
                {
                    ExpectType(c.Args[i], argTypes[i], BasicType.Numeric, "array index");
                }
                _resolutions[c] = new ResolvedArrayAccess(arr);
                return arr.IsString ? BasicType.String : BasicType.Numeric;

            case BuiltinSymbol bsym:
                CheckBuiltinSignature(c, bsym, argTypes);
                _resolutions[c] = new ResolvedBuiltinCall(bsym);
                return bsym.IsString ? BasicType.String : BasicType.Numeric;

            case FunctionSymbol fs:
                CheckArity(c, fs.Params.Count, c.Args.Count);
                _resolutions[c] = new ResolvedFunctionCall(fs);
                return fs.IsString ? BasicType.String : BasicType.Numeric;

            case DefSymbol ds:
                CheckArity(c, ds.Params.Count, c.Args.Count);
                _resolutions[c] = new ResolvedDefCall(ds);
                return ds.IsString ? BasicType.String : BasicType.Numeric;

            case SubSymbol:
                _diags.Error(ErrCannotCall, c.Span,
                    $"'{c.Name}' is a SUB; use CALL to invoke it");
                _resolutions[c] = new ResolvedError("sub-as-expr");
                return BasicType.Numeric;

            default:
                _diags.Error(ErrCannotCall, c.Span, $"'{c.Name}' cannot be called or indexed");
                _resolutions[c] = new ResolvedError("non-callable");
                return BasicType.Numeric;
        }
    }

    private void CheckBuiltinSignature(CallOrIndexExpr c, BuiltinSymbol b, BasicType[] argTypes)
    {
        var n = c.Args.Count;
        if (n < b.Signature.MinArgs || n > b.Signature.MaxArgs)
        {
            var range = b.Signature.MinArgs == b.Signature.MaxArgs
                ? b.Signature.MinArgs.ToString()
                : $"{b.Signature.MinArgs}–{(b.Signature.MaxArgs == int.MaxValue ? "..." : b.Signature.MaxArgs.ToString())}";
            _diags.Error(ErrArityMismatch, c.Span,
                $"'{b.Name}' expects {range} arg(s), got {n}");
            return;
        }
        for (var i = 0; i < n; i++)
        {
            // For variadic functions (e.g. MAX), use the last argument type
            // as the recurring expected type.
            var expected = i < b.Signature.Args.Length ? b.Signature.Args[i] : b.Signature.Args[^1];
            if (expected == BuiltinArgType.Any) continue;
            var want = expected == BuiltinArgType.String ? BasicType.String : BasicType.Numeric;
            if (argTypes[i] != want)
            {
                _diags.Error(ErrTypeMismatch, c.Args[i].Span,
                    $"argument {i + 1} of '{b.Name}' must be {want.ToString().ToLowerInvariant()}, got {argTypes[i].ToString().ToLowerInvariant()}");
            }
        }
    }

    private void CheckArity(CallOrIndexExpr c, int expected, int got)
    {
        if (expected != got)
        {
            _diags.Error(ErrArityMismatch, c.Span,
                $"'{c.Name}' expects {expected} arg(s), got {got}");
        }
    }

    private BasicType AnalyzeBinary(BinaryExpr b, Scope scope)
    {
        var lt = AnalyzeExpr(b.Left, scope);
        var rt = AnalyzeExpr(b.Right, scope);

        switch (b.Op)
        {
            case BinaryOp.Concat:
                if (lt != BasicType.String) _diags.Error(ErrInvalidStringOp, b.Left.Span, "left operand of '&' must be string");
                if (rt != BasicType.String) _diags.Error(ErrInvalidStringOp, b.Right.Span, "right operand of '&' must be string");
                return BasicType.String;

            case BinaryOp.Equal:
            case BinaryOp.NotEqual:
            case BinaryOp.Less:
            case BinaryOp.LessEqual:
            case BinaryOp.Greater:
            case BinaryOp.GreaterEqual:
                // Comparisons accept (numeric, numeric) or (string, string).
                if (lt != rt)
                {
                    _diags.Error(ErrTypeMismatch, b.Span,
                        $"cannot compare {lt.ToString().ToLowerInvariant()} with {rt.ToString().ToLowerInvariant()}");
                }
                return BasicType.Numeric; // BASIC comparison yields a numeric (-1/0)

            default:
                // Arithmetic / logical / bitwise — both sides must be numeric.
                if (lt != BasicType.Numeric) _diags.Error(ErrTypeMismatch, b.Left.Span,
                    $"left operand of {b.Op} must be numeric");
                if (rt != BasicType.Numeric) _diags.Error(ErrTypeMismatch, b.Right.Span,
                    $"right operand of {b.Op} must be numeric");
                return BasicType.Numeric;
        }
    }

    private ResolvedRef? ResolveNameRef(NameRefExpr n, Scope scope)
    {
        var sym = scope.Lookup(Scope.Key(n.Name, n.IsString));
        return sym switch
        {
            VariableSymbol v => new ResolvedVariable(v),
            ParamSymbol p => new ResolvedParam(p),
            ConstantSymbol c => new ResolvedConstant(c),
            BuiltinSymbol bsym when bsym.Signature.MinArgs == 0 => new ResolvedBuiltinCall(bsym),
            _ => null,
        };
    }

    private void ExpectType(Expr e, BasicType actual, BasicType expected, string what)
    {
        if (actual != expected)
        {
            _diags.Error(ErrTypeMismatch, e.Span,
                $"{what} must be {expected.ToString().ToLowerInvariant()} (got {actual.ToString().ToLowerInvariant()})");
        }
    }

    private BasicType TypeOfTarget(Expr target) => target switch
    {
        NameRefExpr n => n.IsString ? BasicType.String : BasicType.Numeric,
        CallOrIndexExpr c => c.IsString ? BasicType.String : BasicType.Numeric,
        _ => BasicType.Numeric,
    };

    private static Scope NearestVariableScope(Scope scope)
    {
        // Locals belong to the nearest non-Module scope; the program scope is
        // always available as a fallback. (Modules — Phase 7 — would shift
        // this logic; not yet relevant.)
        for (var s = scope; s is not null; s = s.Parent)
        {
            if (s.Kind != ScopeKind.Module) return s;
        }
        return scope;
    }

    /// <summary>
    /// Walk up to the Program scope. Used for implicit declarations of names
    /// read inside nested DEF/SUB/FUNCTION bodies, so that those bodies close
    /// over program-level variables instead of allocating a fresh local slot
    /// that shadows the caller's value.
    /// </summary>
    private static Scope ProgramScope(Scope scope)
    {
        for (var s = scope; s is not null; s = s.Parent)
        {
            if (s.Kind == ScopeKind.Program) return s;
        }
        return scope;
    }
}
