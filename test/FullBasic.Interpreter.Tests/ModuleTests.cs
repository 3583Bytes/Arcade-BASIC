using FluentAssertions;
using FullBasic.Core;
using FullBasic.Interpreter;
using FullBasic.Lexer;
using FullBasic.Parser;
using FullBasic.Sema;

namespace FullBasic.Interpreter.Tests;

/// <summary>Phase-7 module tests. Single-file MODULE blocks plus multi-file linkage.</summary>
public class ModuleTests
{
    private static (string Output, int Exit, DiagnosticBag Diagnostics) RunMulti(params string[] sources)
    {
        var diags = new DiagnosticBag();
        var allStmts = new List<FullBasic.Parser.Ast.Stmt>();
        FullBasic.Parser.Ast.Program? mainProg = null;
        // Sources arrive in order: modules then main. We mirror the CLI: parse
        // each, concatenate, and analyze once.
        for (var i = 0; i < sources.Length; i++)
        {
            var file = new SourceFile($"file{i}.bas", sources[i]);
            var tokens = new BasicLexer(file, diags).Lex();
            var prog = new BasicParser(tokens, file, diags).ParseProgram();
            allStmts.AddRange(prog.Statements);
            if (i == sources.Length - 1) mainProg = prog;
        }
        var combined = new FullBasic.Parser.Ast.Program(mainProg!.Span, allStmts);
        var info = Analyzer.Analyze(combined, diags);
        if (diags.HasErrors)
        {
            return (string.Join("\n", diags.All.Select(d => d.Render(false))), 1, diags);
        }
        var sw = new StringWriter();
        var sr = new StringReader("");
        var exit = new BasicInterpreter(combined, info, sw, sr).Run();
        return (sw.ToString(), exit, diags);
    }

    private static (string Output, int Exit) RunCapture(string source, string stdin = "")
    {
        var file = new SourceFile("test.bas", source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        if (diags.HasErrors)
        {
            return (string.Join("\n", diags.All.Select(d => d.Render(false))), 1);
        }
        var sw = new StringWriter();
        var sr = new StringReader(stdin);
        var exit = new BasicInterpreter(program, info, sw, sr).Run();
        return (sw.ToString(), exit);
    }

    // -- Single-file MODULE block ---------------------------------------

    [Fact]
    public void EmptyModuleParsesAndRuns()
    {
        const string src = """
            MODULE EMPTYMOD
            END MODULE
            PRINT "hello"
            """;
        var (output, exit) = RunCapture(src);
        exit.Should().Be(0);
        output.Trim().Should().Be("hello");
    }

    [Fact]
    public void PublicSubInModuleCallableFromMain()
    {
        const string src = """
            MODULE GREETER
              PUBLIC SUB GREET(N$)
                PRINT "hi " & N$
              END SUB
            END MODULE
            CALL GREET("Adam")
            """;
        var (output, exit) = RunCapture(src);
        exit.Should().Be(0);
        output.Trim().Should().Be("hi Adam");
    }

    [Fact]
    public void PrivateSubNotCallableFromMain()
    {
        const string src = """
            MODULE PRIVATELIB
              SUB SECRET
                PRINT "should not be visible"
              END SUB
            END MODULE
            CALL SECRET
            """;
        var (_, exit) = RunCapture(src);
        // Sema fails because SECRET is private to the module — not visible
        // from program scope.
        exit.Should().Be(1);
    }

    [Fact]
    public void PublicFunctionInModuleCallableFromMain()
    {
        const string src = """
            MODULE MATHLIB
              PUBLIC FUNCTION SQUARE(X)
                SQUARE = X * X
              END FUNCTION
            END MODULE
            PRINT SQUARE(7)
            """;
        var (output, exit) = RunCapture(src);
        exit.Should().Be(0);
        output.Trim().Should().Be("49");
    }

    [Fact]
    public void TwoModulesWithPrivateNamesCanCoexist()
    {
        // Two modules both define SUB HELPER privately — should not collide.
        const string src = """
            MODULE A
              SUB HELPER
                PRINT "A's helper"
              END SUB
              PUBLIC SUB DOITA
                CALL HELPER
              END SUB
            END MODULE
            MODULE B
              SUB HELPER
                PRINT "B's helper"
              END SUB
              PUBLIC SUB DOITB
                CALL HELPER
              END SUB
            END MODULE
            CALL DOITA
            CALL DOITB
            """;
        var (output, exit) = RunCapture(src);
        exit.Should().Be(0);
        output.Should().Contain("A's helper");
        output.Should().Contain("B's helper");
    }

    [Fact]
    public void ModuleHelpersCallEachOther()
    {
        const string src = """
            MODULE LIB
              SUB INNER
                PRINT "inner"
              END SUB
              PUBLIC SUB OUTER
                CALL INNER
                PRINT "outer"
              END SUB
            END MODULE
            CALL OUTER
            """;
        var (output, exit) = RunCapture(src);
        exit.Should().Be(0);
        output.Should().Contain("inner");
        output.Should().Contain("outer");
    }

    // -- Multi-file linkage --------------------------------------------

    [Fact]
    public void MultiFileBasicLinkage()
    {
        const string mod = """
            MODULE GREETER
              PUBLIC SUB SAY(N$)
                PRINT "hello " & N$
              END SUB
            END MODULE
            """;
        const string main = """
            CALL SAY("world")
            """;
        var (output, exit, _) = RunMulti(mod, main);
        exit.Should().Be(0);
        output.Trim().Should().Be("hello world");
    }

    [Fact]
    public void MultiFileTwoModules()
    {
        const string modA = """
            MODULE LIBA
              PUBLIC FUNCTION DOUBLE(X)
                DOUBLE = X * 2
              END FUNCTION
            END MODULE
            """;
        const string modB = """
            MODULE LIBB
              PUBLIC FUNCTION TRIPLE(X)
                TRIPLE = X * 3
              END FUNCTION
            END MODULE
            """;
        const string main = """
            PRINT DOUBLE(TRIPLE(5))
            """;
        var (output, exit, _) = RunMulti(modA, modB, main);
        exit.Should().Be(0);
        output.Trim().Should().Be("30");
    }
}
