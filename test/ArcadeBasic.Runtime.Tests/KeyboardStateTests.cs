using ArcadeBasic.Runtime;

namespace ArcadeBasic.Runtime.Tests;

/// <summary>
/// <see cref="KeyboardState"/>: INKEY$ reads presses exactly as before; KEYDOWN
/// asks a key-state backend directly, and otherwise estimates held keys from
/// presses and auto-repeat on a (here, fake) clock.
/// </summary>
public class KeyboardStateTests
{
    /// <summary>A terminal-like keyboard: a queue of presses, nothing about releases.</summary>
    private sealed class Presses : IKeyboard
    {
        private readonly Queue<string> _keys = new();
        public int Reads { get; private set; }
        public void Press(params string[] keys) { foreach (var k in keys) _keys.Enqueue(k); }
        public string ReadKey() { Reads++; return _keys.Count > 0 ? _keys.Dequeue() : ""; }
    }

    /// <summary>A browser-like keyboard that knows what's held, and records what it was asked.</summary>
    private sealed class Held(params string[] down) : IKeyboard, IKeyState
    {
        public List<string> Asked { get; } = [];
        public string ReadKey() => "";
        public bool IsKeyDown(string key) { Asked.Add(key); return down.Contains(key); }
    }

    private double _now;
    private KeyboardState State(IKeyboard keyboard) => new(keyboard, () => _now);

    [Fact]
    public void InkeyReadsTheKeyboardOncePerCall()
    {
        // Scripted keyboards rely on this: "" entries mark idle frames.
        var kb = new Presses();
        kb.Press("", "a", "", "b");
        var keys = State(kb);
        Assert.Equal(["", "a", "", "b", ""], Enumerable.Range(0, 5).Select(_ => keys.ReadKey()));
        Assert.Equal(5, kb.Reads);
    }

    [Fact]
    public void KeydownLeavesThePressesForInkey()
    {
        var kb = new Presses();
        kb.Press("a", "b");
        var keys = State(kb);
        Assert.True(keys.IsDown("a"));
        Assert.True(keys.IsDown("b"));
        Assert.Equal("a", keys.ReadKey());
        Assert.Equal("b", keys.ReadKey());
        Assert.Equal("", keys.ReadKey());
    }

    [Fact]
    public void ATapReadsAsHeldBriefly()
    {
        var kb = new Presses();
        var keys = State(kb);
        kb.Press("a");
        Assert.True(keys.IsDown("a"));
        _now = 0.09;
        Assert.True(keys.IsDown("a"));
        _now = KeyboardState.TapSeconds;
        Assert.False(keys.IsDown("a"));
        Assert.False(keys.IsDown("d"));
    }

    [Fact]
    public void HoldingReadsAsHeldOnceRepeatsArriveAndStopsSoonAfterRelease()
    {
        var kb = new Presses();
        var keys = State(kb);
        kb.Press("a");
        Assert.True(keys.IsDown("a"));

        _now = 0.3;                                  // the OS hasn't started repeating yet
        Assert.False(keys.IsDown("a"));

        for (var ms = 500; ms < 1000; ms += 50)      // repeats every 50 ms; the last at 0.95 s
        {
            _now = ms / 1000.0;
            kb.Press("a");
            Assert.True(keys.IsDown("a"));
            _now = (ms + 25) / 1000.0;
            Assert.True(keys.IsDown("a"));           // between repeats
        }

        _now = 0.95 + 0.15;                          // released after the last repeat
        Assert.True(keys.IsDown("a"));
        _now = 0.95 + KeyboardState.RepeatSeconds + 0.001;
        Assert.False(keys.IsDown("a"));
    }

    [Theory]
    [InlineData("A", "a")]
    [InlineData("a", "A")]
    [InlineData("a", "KeyA")]
    [InlineData("a", "keya")]
    [InlineData("\0K", "ArrowLeft")]
    [InlineData("\0K", "ARROWLEFT")]
    [InlineData("\0M", "ArrowRight")]
    [InlineData("\0H", "ArrowUp")]
    [InlineData("\0P", "ArrowDown")]
    [InlineData(" ", "Space")]
    [InlineData("\r", "Enter")]
    [InlineData("\x1b", "Escape")]
    [InlineData("\t", "Tab")]
    [InlineData("\b", "Backspace")]
    [InlineData("1", "Digit1")]
    [InlineData("-", "Minus")]
    [InlineData("\0;", "F1")]
    public void APressMatchesEveryNameForItsKey(string pressed, string asked)
    {
        var kb = new Presses();
        kb.Press(pressed);
        Assert.True(State(kb).IsDown(asked));
    }

    [Theory]
    [InlineData("Lft", "\"Lft\"")]
    [InlineData("", "\"\"")]
    [InlineData("\0\u0001", "CHR$(0) & CHR$(1)")]
    [InlineData("ab", "\"ab\"")]
    public void AnUnknownNameIsACatchableError(string name, string shown)
    {
        var ex = Assert.Throws<BasicRuntimeException>(() => State(new Presses()).IsDown(name));
        Assert.Equal(4000, ex.TypeCode);
        Assert.Contains(shown, ex.Message);
    }

    [Fact]
    public void AKeyStateBackendIsAskedWithCanonicalNames()
    {
        var kb = new Held("ArrowLeft", "a");
        var keys = State(kb);
        Assert.True(keys.IsDown("arrowleft"));
        Assert.True(keys.IsDown("\0K"));
        Assert.True(keys.IsDown("A"));
        Assert.False(keys.IsDown("KeyA"));           // a position, not a character: the backend decides
        Assert.False(keys.IsDown(" "));
        Assert.Equal(["ArrowLeft", "ArrowLeft", "a", "KeyA", "Space"], kb.Asked);
    }
}
