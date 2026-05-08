using FluentAssertions;
using FullBasic.Core;
using FullBasic.Lexer;
using FullBasic.Parser;
using FullBasic.Parser.Ast;
using FullBasic.Sema;

namespace FullBasic.Sema.Tests;

public class AnalyzerTests
{
    private static (SemanticInfo Info, DiagnosticBag Diagnostics) Analyze(string source)
    {
        var file = new SourceFile("test.bas", source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        return (info, diags);
    }

    private static IReadOnlyList<Diagnostic> ErrorsFor(DiagnosticBag bag, string code) =>
        bag.All.Where(d => d.Code == code).ToList();

    // -- Variable introduction ------------------------------------------

    [Fact]
    public void LetIntroducesVariable()
    {
        var (info, diags) = Analyze("LET X = 42");
        diags.HasErrors.Should().BeFalse();
        info.ProgramScope.LocalLookup("X").Should().BeOfType<VariableSymbol>();
        info.ProgramScope.FrameSize.Should().Be(1);
    }

    [Fact]
    public void NumericAndStringNamesCoexist()
    {
        var (info, _) = Analyze("LET A = 1\nLET A$ = \"hi\"");
        info.ProgramScope.LocalLookup("A").Should().BeOfType<VariableSymbol>();
        info.ProgramScope.LocalLookup("A$").Should().BeOfType<VariableSymbol>();
        info.ProgramScope.FrameSize.Should().Be(2);
    }

    [Fact]
    public void ImplicitVariableEmitsWarning()
    {
        var (_, diags) = Analyze("LET Y = X + 1");
        // X is read before being written.
        diags.WarningCount.Should().Be(1);
        diags.All[0].Code.Should().Be(Analyzer.WarnImplicitVariable);
    }

    // -- Type checking --------------------------------------------------

    [Fact]
    public void NumericToStringAssignmentIsError()
    {
        var (_, diags) = Analyze("LET S$ = 42");
        diags.HasErrors.Should().BeTrue();
        diags.All[0].Code.Should().Be(Analyzer.ErrTypeMismatch);
    }

    [Fact]
    public void StringToNumericAssignmentIsError()
    {
        var (_, diags) = Analyze("LET X = \"hi\"");
        diags.HasErrors.Should().BeTrue();
        diags.All[0].Code.Should().Be(Analyzer.ErrTypeMismatch);
    }

    [Fact]
    public void ConcatenationRequiresStrings()
    {
        var (_, diags) = Analyze("LET S$ = \"a\" & \"b\"");
        diags.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void ConcatenationOnNumericIsError()
    {
        var (_, diags) = Analyze("LET S$ = 1 & 2");
        diags.HasErrors.Should().BeTrue();
    }

    [Fact]
    public void ComparingMixedTypesIsError()
    {
        var (_, diags) = Analyze("LET X = (\"hi\" = 1)");
        diags.HasErrors.Should().BeTrue();
    }

    [Fact]
    public void IfConditionMustBeNumeric()
    {
        var (_, diags) = Analyze("IF \"yes\" THEN PRINT \"x\"");
        diags.HasErrors.Should().BeTrue();
    }

    // -- Builtin resolution ---------------------------------------------

    [Fact]
    public void BuiltinSinResolves()
    {
        var (info, diags) = Analyze("100 LET X = SIN(0.5)");
        diags.HasErrors.Should().BeFalse();
        var assign = (AssignStmt)info.LineLabels[100];
        var call = (CallOrIndexExpr)assign.Value;
        info.Resolve(call).Should().BeOfType<ResolvedBuiltinCall>();
    }

    [Fact]
    public void BuiltinArityErrorReported()
    {
        var (_, diags) = Analyze("LET X = SIN(1, 2)");
        diags.HasErrors.Should().BeTrue();
        diags.All.Should().Contain(d => d.Code == Analyzer.ErrArityMismatch);
    }

    [Fact]
    public void BuiltinTypeMismatchReported()
    {
        var (_, diags) = Analyze("LET X = SIN(\"hi\")");
        diags.HasErrors.Should().BeTrue();
    }

    [Fact]
    public void StringBuiltinReturnsString()
    {
        var (_, diags) = Analyze("LET S$ = MID$(\"hello\", 2, 3)");
        diags.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void ZeroArgBuiltinUsableWithoutParens()
    {
        var (_, diags) = Analyze("LET X = RND");
        diags.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void PiConstantUsable()
    {
        var (_, diags) = Analyze("LET X = PI");
        diags.HasErrors.Should().BeFalse();
    }

    // -- Arrays ---------------------------------------------------------

    [Fact]
    public void DimRegistersArray()
    {
        var (info, diags) = Analyze("DIM A(10)");
        diags.HasErrors.Should().BeFalse();
        info.ProgramScope.LocalLookup("A").Should().BeOfType<ArraySymbol>();
    }

    [Fact]
    public void ArrayAccessResolves()
    {
        var (info, diags) = Analyze("DIM A(10)\nLET X = A(5)");
        diags.HasErrors.Should().BeFalse();
        var assign = info.ProgramScope.Symbols.Values.OfType<VariableSymbol>().Single();
        assign.Name.Should().Be("X");
    }

    [Fact]
    public void UndeclaredArrayIsError()
    {
        var (_, diags) = Analyze("LET A(5) = 0");
        diags.HasErrors.Should().BeTrue();
        diags.All.Should().Contain(d => d.Code == Analyzer.ErrUndefinedName);
    }

    [Fact]
    public void ArrayIndexMustBeNumeric()
    {
        var (_, diags) = Analyze("DIM A(10)\nLET X = A(\"hi\")");
        diags.HasErrors.Should().BeTrue();
    }

    // -- Sub / Function / Def -------------------------------------------

    [Fact]
    public void SubDeclarationAndCall()
    {
        const string src = """
            SUB GREET(N$)
              PRINT N$
            END SUB
            CALL GREET("Adam")
            """;
        var (_, diags) = Analyze(src);
        diags.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void SubArityMismatchIsError()
    {
        var (_, diags) = Analyze("SUB FOO(A)\nEND SUB\nCALL FOO(1, 2)");
        diags.HasErrors.Should().BeTrue();
        diags.All.Should().Contain(d => d.Code == Analyzer.ErrArityMismatch);
    }

    [Fact]
    public void ForwardCallToSub()
    {
        // CALL appears before the SUB declaration — should still resolve.
        var (_, diags) = Analyze("CALL FOO\nSUB FOO\nEND SUB");
        diags.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void FunctionDeclarationAndCall()
    {
        const string src = """
            FUNCTION DOUBLE(X)
              DOUBLE = X * 2
            END FUNCTION
            LET Y = DOUBLE(5)
            """;
        var (_, diags) = Analyze(src);
        diags.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void DefSingleLine()
    {
        var (_, diags) = Analyze("DEF FN DOUBLE(X) = X * 2\nLET Y = FN DOUBLE(5)");
        diags.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void DefBodyTypeChecked()
    {
        var (_, diags) = Analyze("DEF FN F(X) = \"hi\"");
        // F is numeric (no $), but body returns a string.
        diags.HasErrors.Should().BeTrue();
    }

    // -- Line labels ---------------------------------------------------

    [Fact]
    public void GotoToExistingLabelOk()
    {
        var (_, diags) = Analyze("100 LET X = 1\nGOTO 100");
        diags.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void GotoToMissingLabelIsError()
    {
        var (_, diags) = Analyze("GOTO 999");
        diags.HasErrors.Should().BeTrue();
        diags.All.Should().Contain(d => d.Code == Analyzer.ErrUndefinedLineLabel);
    }

    [Fact]
    public void DuplicateLineLabelIsError()
    {
        var (_, diags) = Analyze("100 LET X = 1\n100 LET Y = 2");
        diags.HasErrors.Should().BeTrue();
    }

    // -- DATA pool ------------------------------------------------------

    [Fact]
    public void DataPoolCollectsItemsInSourceOrder()
    {
        var (info, diags) = Analyze("DATA 1, 2, \"hi\"\nDATA 3");
        diags.HasErrors.Should().BeFalse();
        info.DataPool.Should().HaveCount(4);
        info.DataPool[0].Text.Should().Be("1");
        info.DataPool[2].IsString.Should().BeTrue();
        info.DataPool[3].Text.Should().Be("3");
    }

    // -- Scope chain ----------------------------------------------------

    [Fact]
    public void ProgramVariableVisibleInsideSub()
    {
        const string src = """
            LET X = 1
            SUB FOO
              LET Y = X
            END SUB
            """;
        var (_, diags) = Analyze(src);
        diags.HasErrors.Should().BeFalse();
        diags.WarningCount.Should().Be(0);
    }

    [Fact]
    public void ParameterShadowsProgramName()
    {
        const string src = """
            LET X = 1
            SUB FOO(X)
              LET Y = X
            END SUB
            """;
        var (info, diags) = Analyze(src);
        diags.HasErrors.Should().BeFalse();
        var sub = (SubSymbol)info.ProgramScope.LocalLookup("FOO")!;
        sub.BodyScope.LocalLookup("X").Should().BeOfType<ParamSymbol>();
    }

    [Fact]
    public void RealisticProgramAnalyzesCleanly()
    {
        const string src = """
            100 REM compute factorial
            110 LET F = 1
            120 FOR I = 1 TO 5
            130   LET F = F * I
            140 NEXT I
            150 PRINT "5! ="; F
            160 END
            """;
        var (_, diags) = Analyze(src);
        diags.HasErrors.Should().BeFalse();
        diags.WarningCount.Should().Be(0);
    }

    // -- Helpers ---------------------------------------------------------

    private static Stmt? FindFirst(SemanticInfo info, Type kind)
    {
        // Walk the labels map in source order — adequate for these tests.
        foreach (var (_, stmt) in info.LineLabels.OrderBy(kv => kv.Key))
        {
            if (kind.IsInstanceOfType(stmt)) return stmt;
        }
        return null;
    }
}
