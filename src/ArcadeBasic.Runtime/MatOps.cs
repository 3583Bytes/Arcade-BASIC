using Singulink.Numerics;

namespace ArcadeBasic.Runtime;

/// <summary>
/// Shared MAT math kernels. Pure functions that operate on flat
/// <see cref="BigDecimal"/>/<see cref="string"/> arrays plus a
/// <see cref="Bounds"/> descriptor — no symbol table, no I/O. Used by both
/// the tree-walking interpreter and the bytecode VM so the matrix algorithms
/// (multiply, transpose, LU inverse, REDIM with overlap-preservation) live
/// in exactly one place.
/// </summary>
public static class MatOps
{
    // -- Numeric building blocks -----------------------------------------

    public static (BigDecimal[] Data, Bounds Bounds) ElementWise(
        BigDecimal[] l, Bounds lb, BigDecimal[] r, Bounds rb,
        Func<BigDecimal, BigDecimal, BigDecimal> op, string label)
    {
        if (lb.Rank != rb.Rank || !LowersEqual(lb, rb) || !UppersEqual(lb, rb))
        {
            throw new BasicRuntimeException(6010, $"MAT {label}: operand shapes do not match");
        }
        var result = new BigDecimal[l.Length];
        for (var i = 0; i < l.Length; i++) result[i] = op(l[i], r[i]);
        return (result, lb);
    }

    public static (BigDecimal[] Data, Bounds Bounds) Multiply(BigDecimal[] l, Bounds lb, BigDecimal[] r, Bounds rb)
    {
        if (lb.Rank != 2 || rb.Rank != 2)
            throw new BasicRuntimeException(6011, "MAT *: both operands must be 2-D");
        var lRows = lb.Upper[0] - lb.Lower[0] + 1;
        var lCols = lb.Upper[1] - lb.Lower[1] + 1;
        var rRows = rb.Upper[0] - rb.Lower[0] + 1;
        var rCols = rb.Upper[1] - rb.Lower[1] + 1;
        if (lCols != rRows)
            throw new BasicRuntimeException(6012, $"MAT *: inner dimensions disagree ({lCols} vs {rRows})");

        var bounds = new Bounds(new[] { lb.Lower[0], rb.Lower[1] }, new[] { lb.Upper[0], rb.Upper[1] });
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

    public static (BigDecimal[] Data, Bounds Bounds) Transpose(BigDecimal[] m, Bounds mb)
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
        var bounds = new Bounds(new[] { mb.Lower[1], mb.Lower[0] }, new[] { mb.Upper[1], mb.Upper[0] });
        return (result, bounds);
    }

    /// <summary>LU decomposition with partial pivoting.</summary>
    public static BigDecimal[] Inverse(BigDecimal[] m, Bounds mb)
    {
        if (mb.Rank != 2) throw new BasicRuntimeException(6014, "INV requires a 2-D matrix");
        var n = mb.Upper[0] - mb.Lower[0] + 1;
        if (n != mb.Upper[1] - mb.Lower[1] + 1)
            throw new BasicRuntimeException(6015, "INV requires a square matrix");

        var a = new BigDecimal[n, 2 * n];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++) a[i, j] = m[i * n + j];
            a[i, n + i] = BigDecimal.One;
        }

        for (var col = 0; col < n; col++)
        {
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

            var pv = a[col, col];
            for (var j = 0; j < 2 * n; j++)
            {
                a[col, j] = BigDecimal.Divide(a[col, j], pv, 30, RoundingMode.MidpointToEven);
            }

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

        var result = new BigDecimal[n * n];
        for (var i = 0; i < n; i++)
        for (var j = 0; j < n; j++)
        {
            result[i * n + j] = BigDecimal.Round(a[i, n + j], 25, RoundingMode.MidpointToEven);
        }
        return result;
    }

    public static BigDecimal[] Identity(Bounds b)
    {
        if (b.Rank != 2 || (b.Upper[0] - b.Lower[0]) != (b.Upper[1] - b.Lower[1]))
            throw new BasicRuntimeException(6017, "IDN requires a square 2-D target");
        var n = b.Upper[0] - b.Lower[0] + 1;
        var data = new BigDecimal[n * n];
        for (var i = 0; i < n; i++) data[i * n + i] = BigDecimal.One;
        return data;
    }

    public static BigDecimal[] Fill(Bounds b, BigDecimal value)
    {
        var data = new BigDecimal[b.Length];
        for (var i = 0; i < data.Length; i++) data[i] = value;
        return data;
    }

    public static BigDecimal[] ScalarMultiply(BigDecimal k, BigDecimal[] m)
    {
        var result = new BigDecimal[m.Length];
        for (var i = 0; i < m.Length; i++) result[i] = k * m[i];
        return result;
    }

    // -- REDIM overlap preservation --------------------------------------

    public static void PreserveNumericElements(NumericArrayValue old, BigDecimal[] newData, Bounds newBounds)
    {
        if (old.Bounds.Rank != newBounds.Rank) return;
        WalkOverlap(old.Bounds, newBounds, (oldIdx, newIdx) => newData[newIdx] = old.Data[oldIdx]);
    }

    public static void PreserveStringElements(StringArrayValue old, string[] newData, Bounds newBounds)
    {
        if (old.Bounds.Rank != newBounds.Rank) return;
        WalkOverlap(old.Bounds, newBounds, (oldIdx, newIdx) => newData[newIdx] = old.Data[oldIdx]);
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

    // -- Print ------------------------------------------------------------

    /// <summary>MAT PRINT layout: rank 1 → one line; rank 2 → row-per-line + trailing blank; higher ranks fall back to row-major.</summary>
    public static void PrintMatrix<T>(TextWriter @out, T[] data, Bounds bounds, Func<T, string> fmt)
    {
        if (bounds.Rank == 1)
        {
            for (var i = 0; i < data.Length; i++) @out.Write(fmt(data[i]));
            @out.WriteLine();
            return;
        }
        if (bounds.Rank == 2)
        {
            var rows = bounds.Upper[0] - bounds.Lower[0] + 1;
            var cols = bounds.Upper[1] - bounds.Lower[1] + 1;
            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < cols; c++) @out.Write(fmt(data[r * cols + c]));
                @out.WriteLine();
            }
            @out.WriteLine();
            return;
        }
        for (var i = 0; i < data.Length; i++) @out.Write(fmt(data[i]));
        @out.WriteLine();
    }

    // -- Shape helpers ---------------------------------------------------

    public static Bounds? BoundsOf(Value? v) => v switch
    {
        NumericArrayValue n => n.Bounds,
        StringArrayValue s => s.Bounds,
        _ => null,
    };

    public static bool LowersEqual(Bounds a, Bounds b)
    {
        for (var i = 0; i < a.Rank; i++) if (a.Lower[i] != b.Lower[i]) return false;
        return true;
    }

    public static bool UppersEqual(Bounds a, Bounds b)
    {
        for (var i = 0; i < a.Rank; i++) if (a.Upper[i] != b.Upper[i]) return false;
        return true;
    }
}
