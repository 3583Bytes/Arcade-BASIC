using ArcadeBasic.Runtime;

namespace ArcadeBasic.Runtime.Tests;

/// <summary>An unboxed number in a slot (the VM's) reads back as an ordinary value.</summary>
public class ActivationRecordTests
{
    [Fact]
    public void AnUnboxedNumberReadsBackAsANativeValue()
    {
        var frame = new ActivationRecord(2, parent: null);
        Assert.False(frame.IsSet(0));
        frame.SetNumber(0, 1.5);
        Assert.True(frame.IsSet(0));
        Assert.True(frame.TryGetUnboxed(0, out var d) && d == 1.5);
        var v = Assert.IsType<NumericValue>(frame.Get(0));
        Assert.True(v.IsNative);
        Assert.Equal(1.5, v.D);
        Assert.Equal(1.5, ((NumericValue)frame.GetOrDefault(0, NumericValue.Zero)).D);

        frame.Set(0, new StringValue("x"));               // an ordinary value replaces it
        Assert.False(frame.TryGetUnboxed(0, out _));
        Assert.Equal("x", ((StringValue)frame.Get(0)).V);
        Assert.Same(NumericValue.Zero, frame.GetOrDefault(1, NumericValue.Zero));
    }
}
