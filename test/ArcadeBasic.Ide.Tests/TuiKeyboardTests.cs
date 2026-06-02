using ArcadeBasic.Ide;

namespace ArcadeBasic.Ide.Tests;

public class TuiKeyboardTests
{
    [Fact]
    public void DrainsInFifoOrderAndEmptiesWhenDone()
    {
        var kb = new TuiKeyboard();
        Assert.Equal("", kb.ReadKey());        // nothing buffered → ""
        kb.Enqueue("a");
        kb.Enqueue("b");
        Assert.Equal("a", kb.ReadKey());
        Assert.Equal("b", kb.ReadKey());
        Assert.Equal("", kb.ReadKey());        // drained
    }

    [Fact]
    public void IgnoresEmptyEnqueuesAndClearsPending()
    {
        var kb = new TuiKeyboard();
        kb.Enqueue("");                         // ignored
        kb.Enqueue("x");
        kb.Clear();
        Assert.Equal("", kb.ReadKey());
    }
}
