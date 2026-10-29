using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;
using ArcadeBasic.Compiler;
using ArcadeBasic.Vm;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Conformance.Tests;

/// <summary>
/// INKEY$ and KEYDOWN: KEYDOWN reads held keys (exactly,
/// from a backend that knows them), doesn't consume INKEY$'s presses, and rejects
/// names that aren't keys with a catchable exception.
/// </summary>
public class KeyboardTests
{
    private sealed class HeldKeys(params string[] down) : IKeyboard, IKeyState
    {
        public string ReadKey() => "";
        public bool IsKeyDown(string key) => down.Contains(key);
    }

    private sealed class Presses(params string[] keys) : IKeyboard
    {
        private readonly Queue<string> _keys = new(keys);
        public string ReadKey() => _keys.Count > 0 ? _keys.Dequeue() : "";
    }

    /// <summary>Run <paramref name="source"/> with the keyboard <paramref name="keyboard"/> makes; what it prints.</summary>
    private static string Run(string source, Func<IKeyboard> keyboard)
    {
        var file = new SourceFile("keys.bas", source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        Assert.False(diags.HasErrors, string.Join("\n", diags.All.Select(d => d.Render(false))));

        var output = new StringWriter { NewLine = "\n" };
        new BasicVm(BasicCompiler.Compile(program, info), output, TextReader.Null, keyboard: keyboard()).Run();
        return output.ToString();
    }

    [Fact]
    public void KeydownReadsHeldKeysByAnyOfTheirNames()
    {
        var output = Run(
            "PRINT KEYDOWN(\"ArrowLeft\"); KEYDOWN(CHR$(0) & CHR$(75)); KEYDOWN(\"a\"); KEYDOWN(\"A\"); KEYDOWN(\"space\")\n",
            () => new HeldKeys("ArrowLeft", "a"));
        Assert.Equal("-1 -1 -1 -1  0 \n", output);
    }

    [Fact]
    public void KeydownLeavesPressesForInkey()
    {
        // Just pressed, so it reads as held — and INKEY$ still gets the press, once.
        var output = Run("LET D = KEYDOWN(\"x\")\nPRINT D; INKEY$; \"|\"; INKEY$; \"|\"\n", () => new Presses("x"));
        Assert.Equal("-1 x||\n", output);
    }

    [Fact]
    public void InkeyStillReadsOnePressPerCall()
    {
        var output = Run("FOR I = 1 TO 4\nPRINT \"[\" & INKEY$ & \"]\";\nNEXT I\nPRINT\n", () => new Presses("", "a", "", "b"));
        Assert.Equal("[][a][][b]\n", output);
    }

    [Fact]
    public void AnUnknownKeyNameRaisesACatchableException()
    {
        var output = Run(
            "WHEN EXCEPTION IN\nLET D = KEYDOWN(\"Lft\")\nUSE\nPRINT EXTYPE; EXTEXT$\nEND WHEN\n",
            () => new Presses());
        Assert.Equal(" 4000 KEYDOWN: \"Lft\" is not a key name\n", output);
    }
}
