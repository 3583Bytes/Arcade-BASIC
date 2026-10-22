using System.Runtime.CompilerServices;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Vm;

/// <summary>
/// The VM's operand stack. It holds <see cref="Value"/>s, and native numbers (OPTION
/// ARITHMETIC NATIVE) unboxed, so arithmetic on them allocates nothing: an entry is a
/// number when its reference is null. <see cref="Pop"/> and <see cref="Peek"/> box one
/// on demand for code that wants a <see cref="Value"/>; the hot paths use the
/// <c>Number</c> members instead.
/// </summary>
internal sealed class OperandStack
{
    private Value?[] _refs;
    private double[] _numbers;
    private int _count;

    public OperandStack(int capacity)
    {
        _refs = new Value?[capacity];
        _numbers = new double[capacity];
    }

    public int Count => _count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Push(Value value)
    {
        if (_count == _refs.Length) Grow();
        _refs[_count++] = value;
    }

    /// <summary>Push a native number, unboxed.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushNumber(double value)
    {
        if (_count == _refs.Length) Grow();
        _numbers[_count++] = value;   // its reference is already null
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Value Pop()
    {
        var i = --_count;
        var r = _refs[i];
        if (r is null) return NumericValue.FromDouble(_numbers[i]);
        _refs[i] = null;
        return r;
    }

    public Value Peek()
    {
        var r = _refs[_count - 1];
        return r ?? NumericValue.FromDouble(_numbers[_count - 1]);
    }

    /// <summary>Pop a number as a double — for native arithmetic, where every number is
    /// native.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double PopNumber()
    {
        var i = --_count;
        var r = _refs[i];
        if (r is null) return _numbers[i];
        _refs[i] = null;
        return ((NumericValue)r).D;
    }

    /// <summary>Pop the top entry if it's an unboxed number.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPopUnboxed(out double value)
    {
        var i = _count - 1;
        if (_refs[i] is null)
        {
            value = _numbers[i];
            _count = i;
            return true;
        }
        value = 0;
        return false;
    }

    /// <summary>Pop the top two entries if both are native numbers (unboxed or not),
    /// <paramref name="a"/> the lower.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPopNativePair(out double a, out double b)
    {
        int ib = _count - 1, ia = _count - 2;
        Value? rb = _refs[ib], ra = _refs[ia];
        if ((rb is null || rb is NumericValue { IsNative: true }) && (ra is null || ra is NumericValue { IsNative: true }))
        {
            b = rb is null ? _numbers[ib] : ((NumericValue)rb).D;
            a = ra is null ? _numbers[ia] : ((NumericValue)ra).D;
            _refs[ib] = _refs[ia] = null;
            _count = ia;
            return true;
        }
        a = b = 0;
        return false;
    }

    /// <summary>Pop a number and say whether it's zero (BASIC false).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool PopIsZero()
    {
        var i = --_count;
        var r = _refs[i];
        if (r is null) return _numbers[i] == 0;
        _refs[i] = null;
        return ((NumericValue)r).IsZero;
    }

    /// <summary>Pop a subscript (truncated toward zero, as NumericValue.ToInt32 does).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int PopInt32()
    {
        var i = --_count;
        var r = _refs[i];
        if (r is null) return (int)_numbers[i];
        _refs[i] = null;
        return ((NumericValue)r).ToInt32();
    }

    /// <summary>Push a copy of the top entry (an unboxed number stays unboxed).</summary>
    public void Dup()
    {
        if (_count == _refs.Length) Grow();
        _refs[_count] = _refs[_count - 1];
        _numbers[_count] = _numbers[_count - 1];
        _count++;
    }

    /// <summary>Drop entries down to <paramref name="count"/>.</summary>
    public void Truncate(int count)
    {
        if (count >= _count) return;
        Array.Clear(_refs, count, _count - count);
        _count = count;
    }

    private void Grow()
    {
        Array.Resize(ref _refs, _refs.Length * 2);
        Array.Resize(ref _numbers, _numbers.Length * 2);
    }
}
