namespace FullBasic.Runtime;

/// <summary>
/// One frame of variable storage. Slots are indexed by the integers handed out
/// by <c>Scope.AllocateSlot</c> at sema time. The <see cref="Parent"/> link
/// is the static parent (lexically enclosing scope's frame), used to walk
/// outwards on outer-scope reads.
/// </summary>
public sealed class ActivationRecord
{
    public ActivationRecord(int frameSize, ActivationRecord? parent)
    {
        Slots = new Value?[frameSize];
        Parent = parent;
    }

    public Value?[] Slots { get; }

    public ActivationRecord? Parent { get; }

    /// <summary>Read a slot from this exact frame.</summary>
    public Value Get(int slot) =>
        Slots[slot] ?? throw new InvalidOperationException($"slot {slot} not initialized");

    /// <summary>Read a slot, returning a spec-default if uninitialized.</summary>
    public Value GetOrDefault(int slot, Value @default) =>
        Slots[slot] ?? @default;

    /// <summary>Write a slot in this exact frame.</summary>
    public void Set(int slot, Value value) => Slots[slot] = value;

    /// <summary>Whether the slot has been assigned at least once.</summary>
    public bool IsSet(int slot) => Slots[slot] is not null;
}
