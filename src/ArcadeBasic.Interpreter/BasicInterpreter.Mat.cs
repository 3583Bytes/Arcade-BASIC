using System.Globalization;
using ArcadeBasic.Parser.Ast;
using ArcadeBasic.Runtime;
using ArcadeBasic.Sema;
using Singulink.Numerics;

namespace ArcadeBasic.Interpreter;

/// <summary>
/// MAT statement execution. Per Q10:
///   - Always compute into a temp before assigning (kills aliasing).
///   - Reuse target buffer if dims match; otherwise re-allocate (re-dim).
///   - String MAT supports only assign / NUL$ / REDIM / I/O.
///
/// The numeric kernels (multiply, transpose, LU inverse, REDIM overlap walk,
/// matrix print) live in <see cref="MatOps"/> so the bytecode VM can share
/// them; this file is just the dispatcher that wires statement execution to
/// those kernels and handles the symbol-table / activation-record glue.
/// </summary>
public sealed partial class BasicInterpreter
{
    // -- Top-level MAT statement dispatchers ------------------------------

    private FlowControl ExecMatAssign(MatAssignStmt stmt, ActivationRecord frame)
    {
        var sym = LookupArraySymbol(stmt.TargetName, stmt.TargetIsString);
        var current = TryReadArray(sym, frame);

        if (stmt.TargetIsString)
        {
            var (data, bounds) = EvalStringMatRhs(stmt.Rhs, frame, MatOps.BoundsOf(current));
            WriteSlot(frame, sym.OwnerScope!, sym.Slot, new StringArrayValue(data, bounds));
        }
        else
        {
            var (data, bounds) = EvalNumericMatRhs(stmt.Rhs, frame, MatOps.BoundsOf(current));
            WriteSlot(frame, sym.OwnerScope!, sym.Slot, new NumericArrayValue(data, bounds));
        }
        return FlowControl.Continue;
    }

    private FlowControl ExecMatRedim(MatRedimStmt stmt, ActivationRecord frame)
    {
        var sym = LookupArraySymbol(stmt.TargetName, stmt.TargetIsString);
        var current = TryReadArray(sym, frame);

        var rank = stmt.Bounds.Count;
        var lower = new int[rank];
        var upper = new int[rank];
        for (var i = 0; i < rank; i++)
        {
            lower[i] = stmt.Bounds[i].Lower is null ? _optionBase : (int)EvalNumeric(stmt.Bounds[i].Lower!, frame);
            upper[i] = (int)EvalNumeric(stmt.Bounds[i].Upper, frame);
            if (upper[i] < lower[i])
            {
                throw new BasicRuntimeException(6001,
                    $"MAT REDIM {stmt.TargetName}: upper bound {upper[i]} less than lower bound {lower[i]}");
            }
        }
        var newBounds = new Bounds(lower, upper);

        if (stmt.TargetIsString)
        {
            var newData = new string[newBounds.Length];
            for (var i = 0; i < newData.Length; i++) newData[i] = "";
            if (current is StringArrayValue oldS) MatOps.PreserveStringElements(oldS, newData, newBounds);
            WriteSlot(frame, sym.OwnerScope!, sym.Slot, new StringArrayValue(newData, newBounds));
        }
        else
        {
            var newData = new BigDecimal[newBounds.Length];
            if (current is NumericArrayValue oldN) MatOps.PreserveNumericElements(oldN, newData, newBounds);
            WriteSlot(frame, sym.OwnerScope!, sym.Slot, new NumericArrayValue(newData, newBounds));
        }
        return FlowControl.Continue;
    }

    private FlowControl ExecMatInput(MatInputStmt stmt, ActivationRecord frame)
    {
        var sym = LookupArraySymbol(stmt.TargetName, stmt.TargetIsString);
        var current = TryReadArray(sym, frame)
            ?? throw new BasicRuntimeException(6004, $"MAT INPUT requires {stmt.TargetName} to be DIM-ed first");

        var n = MatOps.BoundsOf(current)!.Length;
        var values = new List<string>();
        while (values.Count < n)
        {
            _out.Write("? "); _out.Flush();
            var line = _in.ReadLine() ?? throw new BasicRuntimeException(6005, "MAT INPUT: end of input");
            foreach (var part in line.Split(','))
            {
                var t = part.Trim();
                if (t.Length > 0) values.Add(t);
                if (values.Count == n) break;
            }
        }

        if (stmt.TargetIsString)
        {
            var sarr = (StringArrayValue)current;
            for (var i = 0; i < n; i++) sarr.Data[i] = values[i];
        }
        else
        {
            var narr = (NumericArrayValue)current;
            for (var i = 0; i < n; i++)
            {
                if (!BigDecimal.TryParse(values[i], NumberStyles.Any, CultureInfo.InvariantCulture, out var bd))
                {
                    throw new BasicRuntimeException(4002, $"MAT INPUT: '{values[i]}' is not numeric");
                }
                narr.Data[i] = bd;
            }
        }
        return FlowControl.Continue;
    }

    private FlowControl ExecMatPrint(MatPrintStmt stmt, ActivationRecord frame)
    {
        var sym = LookupArraySymbol(stmt.TargetName, stmt.TargetIsString);
        var current = TryReadArray(sym, frame)
            ?? throw new BasicRuntimeException(6004, $"MAT PRINT requires {stmt.TargetName} to be DIM-ed first");

        if (current is NumericArrayValue narr) MatOps.PrintMatrix(_out, narr.Data, narr.Bounds, FormatNumeric);
        else if (current is StringArrayValue sarr) MatOps.PrintMatrix(_out, sarr.Data, sarr.Bounds, s => s);
        return FlowControl.Continue;
    }

    private FlowControl ExecMatRead(MatReadStmt stmt, ActivationRecord frame)
    {
        var sym = LookupArraySymbol(stmt.TargetName, stmt.TargetIsString);
        var current = TryReadArray(sym, frame)
            ?? throw new BasicRuntimeException(6004, $"MAT READ requires {stmt.TargetName} to be DIM-ed first");

        var n = MatOps.BoundsOf(current)!.Length;
        if (stmt.TargetIsString)
        {
            var sarr = (StringArrayValue)current;
            for (var i = 0; i < n; i++)
            {
                if (_dataCursor >= _info.DataPool.Count)
                    throw new BasicRuntimeException(5001, "MAT READ: DATA pool exhausted");
                sarr.Data[i] = _info.DataPool[_dataCursor++].Text;
            }
        }
        else
        {
            var narr = (NumericArrayValue)current;
            for (var i = 0; i < n; i++)
            {
                if (_dataCursor >= _info.DataPool.Count)
                    throw new BasicRuntimeException(5001, "MAT READ: DATA pool exhausted");
                var item = _info.DataPool[_dataCursor++];
                if (!BigDecimal.TryParse(item.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var bd))
                {
                    throw new BasicRuntimeException(5002, $"MAT READ: '{item.Text}' is not numeric");
                }
                narr.Data[i] = bd;
            }
        }
        return FlowControl.Continue;
    }

    // -- MAT RHS evaluators ----------------------------------------------

    private (BigDecimal[] Data, Bounds Bounds) EvalNumericMatRhs(MatRhs rhs, ActivationRecord frame, Bounds? targetBounds)
    {
        switch (rhs)
        {
            case MatRhsName n:
                var arr = (NumericArrayValue)RequireArray(n.Name, n.IsString, frame);
                return ((BigDecimal[])arr.Data.Clone(), arr.Bounds);

            case MatRhsBinary b:
                {
                    var (l, lb) = EvalNumericMatRhs(b.Left, frame, targetBounds);
                    var (r, rb) = EvalNumericMatRhs(b.Right, frame, targetBounds);
                    return b.Op switch
                    {
                        MatBinaryKind.Add => MatOps.ElementWise(l, lb, r, rb, (a, c) => a + c, "+"),
                        MatBinaryKind.Subtract => MatOps.ElementWise(l, lb, r, rb, (a, c) => a - c, "-"),
                        MatBinaryKind.Multiply => MatOps.Multiply(l, lb, r, rb),
                        _ => throw new BasicRuntimeException(0, $"unsupported MAT op {b.Op}"),
                    };
                }

            case MatRhsScalarMul sm:
                {
                    var k = ((NumericValue)EvalExpr(sm.Scalar, frame)).V;
                    var (m, mb) = EvalNumericMatRhs(sm.Matrix, frame, targetBounds);
                    return (MatOps.ScalarMultiply(k, m), mb);
                }

            case MatRhsInv inv:
                {
                    var (m, mb) = EvalNumericMatRhs(inv.Operand, frame, targetBounds);
                    return (MatOps.Inverse(m, mb), mb);
                }

            case MatRhsTrn trn:
                {
                    var (m, mb) = EvalNumericMatRhs(trn.Operand, frame, targetBounds);
                    return MatOps.Transpose(m, mb);
                }

            case MatRhsConst c:
                {
                    if (targetBounds is null)
                        throw new BasicRuntimeException(6004,
                            "MAT constant rhs requires the target to be DIM-ed first");
                    return c.Kind switch
                    {
                        MatConstKind.Identity => (MatOps.Identity(targetBounds), targetBounds),
                        MatConstKind.Zeros => (new BigDecimal[targetBounds.Length], targetBounds),
                        MatConstKind.Ones => (MatOps.Fill(targetBounds, BigDecimal.One), targetBounds),
                        _ => throw new BasicRuntimeException(0, $"MAT {c.Kind} not valid for numeric"),
                    };
                }

            default:
                throw new BasicRuntimeException(0, $"unsupported numeric MAT rhs {rhs.GetType().Name}");
        }
    }

    private (string[] Data, Bounds Bounds) EvalStringMatRhs(MatRhs rhs, ActivationRecord frame, Bounds? targetBounds)
    {
        switch (rhs)
        {
            case MatRhsName n:
                var arr = (StringArrayValue)RequireArray(n.Name, n.IsString, frame);
                return ((string[])arr.Data.Clone(), arr.Bounds);

            case MatRhsConst c when c.Kind == MatConstKind.NullString:
                {
                    if (targetBounds is null)
                        throw new BasicRuntimeException(6004,
                            "MAT NUL$ requires the target to be DIM-ed first");
                    var data = new string[targetBounds.Length];
                    for (var i = 0; i < data.Length; i++) data[i] = "";
                    return (data, targetBounds);
                }

            default:
                throw new BasicRuntimeException(0,
                    "string MAT rhs only supports assignment from another array or NUL$");
        }
    }

    // -- Helpers ---------------------------------------------------------

    private ArraySymbol LookupArraySymbol(string name, bool isString)
    {
        if (_info.ProgramScope.Lookup(Scope.Key(name, isString)) is not ArraySymbol arr)
            throw new BasicRuntimeException(0, $"'{name}' is not an array");
        return arr;
    }

    private Value? TryReadArray(ArraySymbol sym, ActivationRecord frame)
    {
        var v = ReadSlot(frame, sym.OwnerScope!, sym.Slot, sym.IsString);
        return v is NumericArrayValue or StringArrayValue ? v : null;
    }

    private Value RequireArray(string name, bool isString, ActivationRecord frame)
    {
        var sym = LookupArraySymbol(name, isString);
        return TryReadArray(sym, frame)
            ?? throw new BasicRuntimeException(6004, $"array '{name}' has not been DIM-ed");
    }
}
