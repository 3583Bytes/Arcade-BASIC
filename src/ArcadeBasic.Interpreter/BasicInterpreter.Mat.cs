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
///   - Naive triple-loop multiply (BigDecimal cost dominates).
///   - LU decomposition with partial pivoting for INV.
///   - String MAT supports only assign / NUL$ / REDIM / I/O.
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
            var (data, bounds) = EvalStringMatRhs(stmt.Rhs, frame, BoundsOf(current));
            WriteSlot(frame, sym.OwnerScope!, sym.Slot, new StringArrayValue(data, bounds));
        }
        else
        {
            var (data, bounds) = EvalNumericMatRhs(stmt.Rhs, frame, BoundsOf(current));
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
            // Default-initialize and preserve elements that fit.
            for (var i = 0; i < newData.Length; i++) newData[i] = "";
            if (current is StringArrayValue oldS) PreserveStringElements(oldS, newData, newBounds);
            WriteSlot(frame, sym.OwnerScope!, sym.Slot, new StringArrayValue(newData, newBounds));
        }
        else
        {
            var newData = new BigDecimal[newBounds.Length];
            if (current is NumericArrayValue oldN) PreserveNumericElements(oldN, newData, newBounds);
            WriteSlot(frame, sym.OwnerScope!, sym.Slot, new NumericArrayValue(newData, newBounds));
        }
        return FlowControl.Continue;
    }

    private FlowControl ExecMatInput(MatInputStmt stmt, ActivationRecord frame)
    {
        var sym = LookupArraySymbol(stmt.TargetName, stmt.TargetIsString);
        var current = TryReadArray(sym, frame)
            ?? throw new BasicRuntimeException(6004, $"MAT INPUT requires {stmt.TargetName} to be DIM-ed first");

        var n = BoundsOf(current)!.Length;
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

        if (current is NumericArrayValue narr) PrintMatrix(narr.Data, narr.Bounds, FormatNumeric);
        else if (current is StringArrayValue sarr) PrintMatrix(sarr.Data, sarr.Bounds, s => s);
        return FlowControl.Continue;
    }

    private void PrintMatrix<T>(T[] data, Bounds bounds, Func<T, string> fmt)
    {
        if (bounds.Rank == 1)
        {
            for (var i = 0; i < data.Length; i++)
            {
                _out.Write(fmt(data[i]));
            }
            _out.WriteLine();
            return;
        }
        if (bounds.Rank == 2)
        {
            var rows = bounds.Upper[0] - bounds.Lower[0] + 1;
            var cols = bounds.Upper[1] - bounds.Lower[1] + 1;
            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < cols; c++)
                {
                    _out.Write(fmt(data[r * cols + c]));
                }
                _out.WriteLine();
            }
            _out.WriteLine();
            return;
        }
        // Higher-dimensional: just print row-major with spaces.
        for (var i = 0; i < data.Length; i++)
        {
            _out.Write(fmt(data[i]));
        }
        _out.WriteLine();
    }

    private FlowControl ExecMatRead(MatReadStmt stmt, ActivationRecord frame)
    {
        var sym = LookupArraySymbol(stmt.TargetName, stmt.TargetIsString);
        var current = TryReadArray(sym, frame)
            ?? throw new BasicRuntimeException(6004, $"MAT READ requires {stmt.TargetName} to be DIM-ed first");

        var n = BoundsOf(current)!.Length;
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
                        MatBinaryKind.Add => MatElementWise(l, lb, r, rb, (a, c) => a + c, "+"),
                        MatBinaryKind.Subtract => MatElementWise(l, lb, r, rb, (a, c) => a - c, "-"),
                        MatBinaryKind.Multiply => MatMultiply(l, lb, r, rb),
                        _ => throw new BasicRuntimeException(0, $"unsupported MAT op {b.Op}"),
                    };
                }

            case MatRhsScalarMul sm:
                {
                    var k = ((NumericValue)EvalExpr(sm.Scalar, frame)).V;
                    var (m, mb) = EvalNumericMatRhs(sm.Matrix, frame, targetBounds);
                    var result = new BigDecimal[m.Length];
                    for (var i = 0; i < m.Length; i++) result[i] = k * m[i];
                    return (result, mb);
                }

            case MatRhsInv inv:
                {
                    var (m, mb) = EvalNumericMatRhs(inv.Operand, frame, targetBounds);
                    return (MatInverse(m, mb), mb);
                }

            case MatRhsTrn trn:
                {
                    var (m, mb) = EvalNumericMatRhs(trn.Operand, frame, targetBounds);
                    return MatTranspose(m, mb);
                }

            case MatRhsConst c:
                {
                    if (targetBounds is null)
                        throw new BasicRuntimeException(6004,
                            "MAT constant rhs requires the target to be DIM-ed first");
                    return c.Kind switch
                    {
                        MatConstKind.Identity => (MatIdentity(targetBounds), targetBounds),
                        MatConstKind.Zeros => (new BigDecimal[targetBounds.Length], targetBounds),
                        MatConstKind.Ones => (MatFill(targetBounds, BigDecimal.One), targetBounds),
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

    // -- Numeric MAT building blocks -------------------------------------

    private static (BigDecimal[], Bounds) MatElementWise(
        BigDecimal[] l, Bounds lb, BigDecimal[] r, Bounds rb,
        Func<BigDecimal, BigDecimal, BigDecimal> op, string label)
    {
        if (lb.Rank != rb.Rank || !LowersEqual(lb, rb) || !UppersEqual(lb, rb))
        {
            throw new BasicRuntimeException(6010,
                $"MAT {label}: operand shapes do not match");
        }
        var result = new BigDecimal[l.Length];
        for (var i = 0; i < l.Length; i++) result[i] = op(l[i], r[i]);
        return (result, lb);
    }

    private static (BigDecimal[], Bounds) MatMultiply(BigDecimal[] l, Bounds lb, BigDecimal[] r, Bounds rb)
    {
        if (lb.Rank != 2 || rb.Rank != 2)
            throw new BasicRuntimeException(6011, "MAT *: both operands must be 2-D");
        var lRows = lb.Upper[0] - lb.Lower[0] + 1;
        var lCols = lb.Upper[1] - lb.Lower[1] + 1;
        var rRows = rb.Upper[0] - rb.Lower[0] + 1;
        var rCols = rb.Upper[1] - rb.Lower[1] + 1;
        if (lCols != rRows)
            throw new BasicRuntimeException(6012,
                $"MAT *: inner dimensions disagree ({lCols} vs {rRows})");

        var bounds = new Bounds([lb.Lower[0], rb.Lower[1]], [lb.Upper[0], rb.Upper[1]]);
        var result = new BigDecimal[lRows * rCols];
        for (var i = 0; i < lRows; i++)
        {
            for (var k = 0; k < lCols; k++)
            {
                var lv = l[i * lCols + k];
                if (lv == BigDecimal.Zero) continue;
                for (var j = 0; j < rCols; j++)
                {
                    result[i * rCols + j] += lv * r[k * rCols + j];
                }
            }
        }
        return (result, bounds);
    }

    private static (BigDecimal[], Bounds) MatTranspose(BigDecimal[] m, Bounds mb)
    {
        if (mb.Rank != 2) throw new BasicRuntimeException(6013, "TRN requires a 2-D matrix");
        var rows = mb.Upper[0] - mb.Lower[0] + 1;
        var cols = mb.Upper[1] - mb.Lower[1] + 1;
        var result = new BigDecimal[m.Length];
        for (var i = 0; i < rows; i++)
        for (var j = 0; j < cols; j++)
        {
            result[j * rows + i] = m[i * cols + j];
        }
        var bounds = new Bounds([mb.Lower[1], mb.Lower[0]], [mb.Upper[1], mb.Upper[0]]);
        return (result, bounds);
    }

    /// <summary>LU decomposition with partial pivoting.</summary>
    private static BigDecimal[] MatInverse(BigDecimal[] m, Bounds mb)
    {
        if (mb.Rank != 2) throw new BasicRuntimeException(6014, "INV requires a 2-D matrix");
        var n = mb.Upper[0] - mb.Lower[0] + 1;
        if (n != mb.Upper[1] - mb.Lower[1] + 1)
            throw new BasicRuntimeException(6015, "INV requires a square matrix");

        // Build [A | I] augmented matrix as doubles for speed; final result back to BigDecimal.
        var a = new BigDecimal[n, 2 * n];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++) a[i, j] = m[i * n + j];
            a[i, n + i] = BigDecimal.One;
        }

        for (var col = 0; col < n; col++)
        {
            // Partial pivoting: find the row with the largest absolute value in column `col`.
            var pivotRow = col;
            var pivotMag = BigDecimal.Abs(a[col, col]);
            for (var r = col + 1; r < n; r++)
            {
                var mag = BigDecimal.Abs(a[r, col]);
                if (mag > pivotMag) { pivotMag = mag; pivotRow = r; }
            }
            if (pivotMag == BigDecimal.Zero)
                throw new BasicRuntimeException(6016, "INV: matrix is singular");
            if (pivotRow != col)
            {
                for (var j = 0; j < 2 * n; j++) (a[col, j], a[pivotRow, j]) = (a[pivotRow, j], a[col, j]);
            }

            // Scale pivot row.
            var pv = a[col, col];
            for (var j = 0; j < 2 * n; j++)
            {
                a[col, j] = BigDecimal.Divide(a[col, j], pv, 30, RoundingMode.MidpointToEven);
            }

            // Eliminate column in other rows.
            for (var r = 0; r < n; r++)
            {
                if (r == col) continue;
                var f = a[r, col];
                if (f == BigDecimal.Zero) continue;
                for (var j = 0; j < 2 * n; j++)
                {
                    a[r, j] -= f * a[col, j];
                }
            }
        }

        // Round each element to clean up the noise from chained divisions.
        // We compute at 30+ digits but the human-visible answer typically has
        // far fewer significant digits — round trailing 9s/0s.
        var result = new BigDecimal[n * n];
        for (var i = 0; i < n; i++)
        for (var j = 0; j < n; j++)
        {
            result[i * n + j] = BigDecimal.Round(a[i, n + j], 25, RoundingMode.MidpointToEven);
        }
        return result;
    }

    private static BigDecimal[] MatIdentity(Bounds b)
    {
        if (b.Rank != 2 || (b.Upper[0] - b.Lower[0]) != (b.Upper[1] - b.Lower[1]))
            throw new BasicRuntimeException(6017, "IDN requires a square 2-D target");
        var n = b.Upper[0] - b.Lower[0] + 1;
        var data = new BigDecimal[n * n];
        for (var i = 0; i < n; i++) data[i * n + i] = BigDecimal.One;
        return data;
    }

    private static BigDecimal[] MatFill(Bounds b, BigDecimal value)
    {
        var data = new BigDecimal[b.Length];
        for (var i = 0; i < data.Length; i++) data[i] = value;
        return data;
    }

    // -- Element preservation for REDIM ----------------------------------

    private static void PreserveNumericElements(NumericArrayValue old, BigDecimal[] newData, Bounds newBounds)
    {
        if (old.Bounds.Rank != newBounds.Rank) return;
        WalkOverlap(old.Bounds, newBounds, (oldIdx, newIdx) =>
            newData[newIdx] = old.Data[oldIdx]);
    }

    private static void PreserveStringElements(StringArrayValue old, string[] newData, Bounds newBounds)
    {
        if (old.Bounds.Rank != newBounds.Rank) return;
        WalkOverlap(old.Bounds, newBounds, (oldIdx, newIdx) =>
            newData[newIdx] = old.Data[oldIdx]);
    }

    private static void WalkOverlap(Bounds oldB, Bounds newB, Action<int, int> copy)
    {
        var rank = oldB.Rank;
        var idx = new int[rank];
        for (var i = 0; i < rank; i++) idx[i] = Math.Max(oldB.Lower[i], newB.Lower[i]);
        var max = new int[rank];
        for (var i = 0; i < rank; i++) max[i] = Math.Min(oldB.Upper[i], newB.Upper[i]);

        while (true)
        {
            try
            {
                copy(oldB.IndexOf(idx), newB.IndexOf(idx));
            }
            catch
            {
                /* skip out-of-range; shouldn't happen given bounds clamp above */
            }

            // Increment idx like an odometer; carry on overflow.
            var dim = rank - 1;
            while (dim >= 0)
            {
                idx[dim]++;
                if (idx[dim] <= max[dim]) break;
                idx[dim] = Math.Max(oldB.Lower[dim], newB.Lower[dim]);
                dim--;
            }
            if (dim < 0) return;
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

    private static Bounds? BoundsOf(Value? v) => v switch
    {
        NumericArrayValue n => n.Bounds,
        StringArrayValue s => s.Bounds,
        _ => null,
    };

    private Value RequireArray(string name, bool isString, ActivationRecord frame)
    {
        var sym = LookupArraySymbol(name, isString);
        return TryReadArray(sym, frame)
            ?? throw new BasicRuntimeException(6004, $"array '{name}' has not been DIM-ed");
    }

    private static bool LowersEqual(Bounds a, Bounds b)
    {
        for (var i = 0; i < a.Rank; i++) if (a.Lower[i] != b.Lower[i]) return false;
        return true;
    }

    private static bool UppersEqual(Bounds a, Bounds b)
    {
        for (var i = 0; i < a.Rank; i++) if (a.Upper[i] != b.Upper[i]) return false;
        return true;
    }
}
