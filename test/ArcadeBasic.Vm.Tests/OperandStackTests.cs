using ArcadeBasic.Runtime;
using ArcadeBasic.Vm;

namespace ArcadeBasic.Vm.Tests;

/// <summary>
/// <see cref="OperandStack"/>: native numbers ride unboxed and come back as ordinary
/// <see cref="NumericValue"/>s whenever code asks for a <see cref="Value"/>.
/// </summary>
public class OperandStackTests
{
    [Fact]
    public void AnUnboxedNumberPopsAsANativeValue()
    {
        var s = new OperandStack(2);
        s.PushNumber(2.5);
        Assert.True(s.TryPopUnboxed(out var d) && d == 2.5);
        s.PushNumber(-4);
        var v = Assert.IsType<NumericValue>(s.Pop());
        Assert.True(v.IsNative);
        Assert.Equal(-4, v.D);
    }

    [Fact]
    public void ValuesStayAsTheyWere()
    {
        var s = new OperandStack(1);                     // grows as needed
        var text = new StringValue("hi");
        var number = NumericValue.FromDouble(7);
        s.Push(text);
        s.Push(number);
        Assert.False(s.TryPopUnboxed(out _));            // a boxed value isn't unboxed
        Assert.Same(number, s.Pop());
        Assert.Same(text, s.Pop());
        Assert.Equal(0, s.Count);
    }

    [Fact]
    public void APairIsNativeOnlyIfBothAreNativeNumbers()
    {
        var s = new OperandStack(4);
        s.Push(NumericValue.FromDouble(1));
        s.PushNumber(2);
        Assert.True(s.TryPopNativePair(out var a, out var b));
        Assert.Equal((1.0, 2.0), (a, b));

        s.Push(new StringValue("a"));
        s.Push(new StringValue("b"));
        Assert.False(s.TryPopNativePair(out _, out _));  // strings: left for Compare
        Assert.Equal(2, s.Count);

        s.Truncate(0);
        s.Push(NumericValue.One);                        // a decimal
        s.PushNumber(1);
        Assert.False(s.TryPopNativePair(out _, out _));
    }

    [Fact]
    public void DupKeepsANumberUnboxedAndTruncateDropsEntries()
    {
        var s = new OperandStack(2);
        s.PushNumber(3);
        s.Dup();
        Assert.True(s.TryPopUnboxed(out var x) && x == 3);
        Assert.True(s.TryPopUnboxed(out var y) && y == 3);
        s.Push(StringValue.Empty);
        s.PushNumber(1);
        s.Truncate(1);
        Assert.Same(StringValue.Empty, s.Peek());
        Assert.Equal(1, s.Count);
    }
}
