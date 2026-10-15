using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;
using ArcadeBasic.Interpreter;
using ArcadeBasic.Compiler;
using ArcadeBasic.Vm;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Conformance.Tests;

/// <summary>
/// INKEY$ and KEYDOWN agree on both engines: KEYDOWN reads held keys (exactly,
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

    /// <summary>Run <paramref name="source"/> on both engines, each with its own keyboard.</summary>
    private static (string Interpreter, string Vm) RunBoth(string source, Func<IKeyboard> keyboard)
    {
        var file = new SourceFile("keys.bas", source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        Assert.False(diags.HasErrors, string.Join("\n", diags.All.Select(d => d.Render(false))));

        var iOut = new StringWriter { NewLine = "\n" };
        new BasicInterpreter(program, info, iOut, TextReader.Null, keyboard: keyboard()).Run();
        var vOut = new StringWriter { NewLine = "\n" };
        new BasicVm(BasicCompiler.Compile(program, info), vOut, TextReader.Null, keyboard: keyboard()).Run();
        return (iOut.ToString(), vOut.ToString());
    }

    [Fact]
    public void KeydownReadsHeldKeysByAnyOfTheirNames()
    {
        var (i, v) = RunBoth(
            "PRINT KEYDOWN(\"ArrowLeft\"); KEYDOWN(CHR$(0) & CHR$(75)); KEYDOWN(\"a\"); KEYDOWN(\"A\"); KEYDOWN(\"space\")\n",
            () => new HeldKeys("ArrowLeft", "a"));
        Assert.Equal("-1 -1 -1 -1  0 \n", i);
        Assert.Equal(i, v);
    }

    [Fact]
    public void KeydownLeavesPressesForInkey()
    {
        // Just pressed, so it reads as held — and INKEY$ still gets the press, once.
        var (i, v) = RunBoth("LET D = KEYDOWN(\"x\")\nPRINT D; INKEY$; \"|\"; INKEY$; \"|\"\n", () => new Presses("x"));
        Assert.Equal("-1 x||\n", i);
        Assert.Equal(i, v);
    }

    [Fact]
    public void InkeyStillReadsOnePressPerCall()
    {
        var (i, v) = RunBoth("FOR I = 1 TO 4\nPRINT \"[\" & INKEY$ & \"]\";\nNEXT I\nPRINT\n", () => new Presses("", "a", "", "b"));
        Assert.Equal("[][a][][b]\n", i);
        Assert.Equal(i, v);
    }

    [Fact]
    public void AnUnknownKeyNameRaisesACatchableException()
    {
        var (i, v) = RunBoth(
            "WHEN EXCEPTION IN\nLET D = KEYDOWN(\"Lft\")\nUSE\nPRINT EXTYPE; EXTEXT$\nEND WHEN\n",
            () => new Presses());
        Assert.Equal(" 4000 KEYDOWN: \"Lft\" is not a key name\n", i);
        Assert.Equal(i, v);
    }
}
