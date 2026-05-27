using ArcadeBasic.Parser.Ast;
using ArcadeBasic.Runtime;
using ArcadeBasic.Sema;

namespace ArcadeBasic.Interpreter;

/// <summary>
/// Phase-6 exception-handling execution. WHEN/USE/HANDLER, CAUSE, RETRY, CONTINUE.
/// </summary>
public sealed partial class BasicInterpreter
{
    /// <summary>The currently-active exception, if any. Read by EXTYPE/EXLINE/EXTEXT$.</summary>
    private BasicException? _currentException;

    private FlowControl ExecWhen(WhenStmt w, ActivationRecord frame)
    {
        var useBody = ResolveHandlerBody(w)
            ?? throw new BasicRuntimeException(0, "WHEN: handler body could not be resolved");

        while (true) // restart point for RETRY
        {
            var needsRestart = false;
            for (var pc = 0; pc < w.InBody.Count; pc++)
            {
                var fc = ExecStmt(w.InBody[pc], frame);

                if (fc is FlowControl.Cause c)
                {
                    var hfc = RunHandlerBody(useBody, c.Exception, frame);
                    if (hfc is FlowControl.Retry) { needsRestart = true; break; }
                    if (hfc is FlowControl.Resume) continue;            // advance past the offending stmt
                    if (hfc is FlowControl.Next) return FlowControl.Continue; // normal exit from handler
                    if (hfc is FlowControl.Exit ex
                        && (ex.Kind == ExitKind.When || ex.Kind == ExitKind.Handler))
                    {
                        return FlowControl.Continue;
                    }
                    return hfc; // propagate Return/Goto/Stop/End/etc.
                }

                if (fc is not FlowControl.Next) return fc;
            }

            if (!needsRestart) return FlowControl.Continue;
        }
    }

    private IReadOnlyList<Stmt>? ResolveHandlerBody(WhenStmt w)
    {
        if (w.UseBody is not null) return w.UseBody;
        if (w.UseHandlerName is null) return null;
        var sym = _info.ProgramScope.Lookup(Scope.Key(w.UseHandlerName, isString: false));
        return (sym as HandlerSymbol)?.Stmt.Body;
    }

    private FlowControl RunHandlerBody(IReadOnlyList<Stmt> body, BasicException ex, ActivationRecord frame)
    {
        var prev = _currentException;
        _currentException = ex;
        try
        {
            return ExecuteStatementList(body, frame);
        }
        finally
        {
            _currentException = prev;
        }
    }

    private FlowControl ExecCause(CauseStmt cause, ActivationRecord frame)
    {
        var type = (int)EvalNumeric(cause.Type, frame);
        var line = cause.Span.StartPosition.LineCol.Line;
        return new FlowControl.Cause(new BasicException(type, line, $"user-raised exception {type}"));
    }
}
