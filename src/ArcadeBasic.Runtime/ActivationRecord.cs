namespace ArcadeBasic.Runtime;

/// <summary>
/// One frame of variable storage. Slots are indexed by the integers handed out
/// by <c>Scope.AllocateSlot</c> at sema time. The <see cref="Parent"/> link
/// is the static parent (lexically enclosing scope's frame), used to walk
/// outwards on outer-scope reads.
///
/// The VM can keep a native number (OPTION ARITHMETIC NATIVE) in a slot unboxed,
/// with <see cref="SetNumber"/>, so assignments don't allocate; the slot then reads
/// back through <see cref="Get"/> as an ordinary <see cref="NumericValue"/>.
/// </summary>
public sealed class ActivationRecord
{
    /// <summary>In <see cref="Slots"/>: this slot's value is the number in <c>_numbers</c>.</summary>
    private static readonly Value s_unboxed = new UnboxedNumber();

    private double[]? _numbers;

    public ActivationRecord(int frameSize, ActivationRecord? parent)
    {
        Slots = new Value?[frameSize];
        Parent = parent;
    }

    public Value?[] Slots { get; }

    public ActivationRecord? Parent { get; }

    /// <summary>Read a slot from this exact frame.</summary>
    public Value Get(int slot)
    {
        var v = Slots[slot];
        if (ReferenceEquals(v, s_unboxed)) return NumericValue.FromDouble(_numbers![slot]);
        return v ?? throw new InvalidOperationException($"slot {slot} not initialized");
    }

    /// <summary>Read a slot, returning a spec-default if uninitialized.</summary>
    public Value GetOrDefault(int slot, Value @default)
    {
        var v = Slots[slot];
        if (ReferenceEquals(v, s_unboxed)) return NumericValue.FromDouble(_numbers![slot]);
        return v ?? @default;
    }

    /// <summary>Write a slot in this exact frame.</summary>
    public void Set(int slot, Value value) => Slots[slot] = value;

    /// <summary>Whether the slot has been assigned at least once.</summary>
    public bool IsSet(int slot) => Slots[slot] is not null;

    /// <summary>Write a native number into a slot without boxing it.</summary>
    public void SetNumber(int slot, double value)
    {
        (_numbers ??= new double[Slots.Length])[slot] = value;
        Slots[slot] = s_unboxed;
    }

    /// <summary>Read a slot that holds an unboxed number (see <see cref="SetNumber"/>).</summary>
    public bool TryGetUnboxed(int slot, out double value)
    {
        if (ReferenceEquals(Slots[slot], s_unboxed))
        {
            value = _numbers![slot];
            return true;
        }
        value = 0;
        return false;
    }

    private sealed record class UnboxedNumber : Value;
}
