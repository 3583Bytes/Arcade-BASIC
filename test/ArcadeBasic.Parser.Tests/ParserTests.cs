using FluentAssertions;
using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Parser.Ast;

namespace ArcadeBasic.Parser.Tests;

public class ParserTests
{
    private static (Program Program, DiagnosticBag Diagnostics) Parse(string source)
    {
        var file = new SourceFile("test.bas", source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var prog = new BasicParser(tokens, file, diags).ParseProgram();
        return (prog, diags);
    }

    private static T SingleStmt<T>(string source) where T : Stmt
    {
        var (prog, diags) = Parse(source);
        diags.HasErrors.Should().BeFalse();
        prog.Statements.Should().HaveCount(1);
        return prog.Statements[0].Should().BeOfType<T>().Subject;
    }

    // -- Simple statements -----------------------------------------------

    [Fact]
    public void EmptyProgram()
    {
        var (prog, diags) = Parse("");
        diags.HasErrors.Should().BeFalse();
        prog.Statements.Should().BeEmpty();
    }

    [Fact]
    public void LetWithNumber()
    {
        var stmt = SingleStmt<AssignStmt>("LET X = 42");
        stmt.ExplicitLet.Should().BeTrue();
        stmt.Target.Should().BeOfType<NameRefExpr>().Which.Name.Should().Be("X");
        stmt.Value.Should().BeOfType<NumberExpr>().Which.Text.Should().Be("42");
    }

    [Fact]
    public void ImplicitLet()
    {
        var stmt = SingleStmt<AssignStmt>("X = 42");
        stmt.ExplicitLet.Should().BeFalse();
    }

    [Fact]
    public void StringAssignment()
    {
        var stmt = SingleStmt<AssignStmt>("LET MSG$ = \"hi\"");
        var target = stmt.Target.Should().BeOfType<NameRefExpr>().Subject;
        target.Name.Should().Be("MSG");
        target.IsString.Should().BeTrue();
        stmt.Value.Should().BeOfType<StringExpr>().Which.Value.Should().Be("hi");
    }

    [Fact]
    public void DoubledQuoteInString()
    {
        var stmt = SingleStmt<AssignStmt>("LET S$ = \"say \"\"hi\"\"\"");
        stmt.Value.Should().BeOfType<StringExpr>().Which.Value.Should().Be("say \"hi\"");
    }

    [Fact]
    public void ArrayAssignment()
    {
        var stmt = SingleStmt<AssignStmt>("LET A(I, J) = 0");
        var target = stmt.Target.Should().BeOfType<CallOrIndexExpr>().Subject;
        target.Name.Should().Be("A");
        target.Args.Should().HaveCount(2);
    }

    [Fact]
    public void Print()
    {
        var stmt = SingleStmt<PrintStmt>("PRINT \"hi\", 42; X");
        stmt.Items.Should().HaveCount(5); // expr, comma, expr, semicolon, expr
        stmt.Items[0].Should().BeOfType<PrintExprItem>();
        stmt.Items[1].Should().BeOfType<PrintComma>();
        stmt.Items[2].Should().BeOfType<PrintExprItem>();
        stmt.Items[3].Should().BeOfType<PrintSemicolon>();
        stmt.Items[4].Should().BeOfType<PrintExprItem>();
    }

    [Fact]
    public void PrintTab()
    {
        var stmt = SingleStmt<PrintStmt>("PRINT TAB(20); \"hi\"");
        stmt.Items[0].Should().BeOfType<PrintTab>();
    }

    [Fact]
    public void Input()
    {
        var stmt = SingleStmt<InputStmt>("INPUT A, B$, C");
        stmt.Targets.Should().HaveCount(3);
        stmt.Prompt.Should().BeNull();
    }

    [Fact]
    public void InputWithPrompt()
    {
        var stmt = SingleStmt<InputStmt>("INPUT \"name? \"; N$");
        stmt.Prompt.Should().BeOfType<StringExpr>();
        stmt.PromptIsSemicolon.Should().BeTrue();
        stmt.Targets.Should().HaveCount(1);
    }

    [Fact]
    public void ReadAndData()
    {
        var (prog, diags) = Parse("READ A, B$\nDATA 42, \"hi\"");
        diags.HasErrors.Should().BeFalse();
        prog.Statements.Should().HaveCount(2);
        prog.Statements[0].Should().BeOfType<ReadStmt>();
        prog.Statements[1].Should().BeOfType<DataStmt>().Which.Items.Should().HaveCount(2);
    }

    [Fact]
    public void DataWithSignedNumbers()
    {
        var stmt = SingleStmt<DataStmt>("DATA -1, +2, 3");
        stmt.Items[0].Text.Should().Be("-1");
        stmt.Items[1].Text.Should().Be("+2");
        stmt.Items[2].Text.Should().Be("3");
    }

    [Fact]
    public void Restore()
    {
        var (prog, _) = Parse("RESTORE\nRESTORE 100");
        prog.Statements[0].Should().BeOfType<RestoreStmt>().Which.LabelTarget.Should().BeNull();
        prog.Statements[1].Should().BeOfType<RestoreStmt>().Which.LabelTarget.Should().NotBeNull();
    }

    [Fact]
    public void GotoAndGosub()
    {
        var (prog, _) = Parse("GOTO 100\nGO TO 200\nGOSUB 300\nRETURN");
        prog.Statements.Should().HaveCount(4);
        prog.Statements[0].Should().BeOfType<GotoStmt>();
        prog.Statements[1].Should().BeOfType<GotoStmt>();
        prog.Statements[2].Should().BeOfType<GosubStmt>();
        prog.Statements[3].Should().BeOfType<ReturnStmt>();
    }

    [Fact]
    public void OnGotoParsesTargetsAndKind()
    {
        var stmt = SingleStmt<OnJumpStmt>("ON X GOTO 100, 200, 300");
        stmt.IsGosub.Should().BeFalse();
        stmt.Targets.Should().Equal(100, 200, 300);
        stmt.ElseStmt.Should().BeNull();
    }

    [Fact]
    public void OnGosubWithElseParses()
    {
        var stmt = SingleStmt<OnJumpStmt>("ON A + 1 GOSUB 1000, 2000 ELSE PRINT \"oops\"");
        stmt.IsGosub.Should().BeTrue();
        stmt.Targets.Should().Equal(1000, 2000);
        stmt.ElseStmt.Should().BeOfType<PrintStmt>();
    }

    [Fact]
    public void OnGoToTwoWordFormParses()
    {
        var stmt = SingleStmt<OnJumpStmt>("ON I GO TO 10, 20");
        stmt.IsGosub.Should().BeFalse();
        stmt.Targets.Should().Equal(10, 20);
    }

    // -- Graphics (§13) -------------------------------------------------

    [Fact]
    public void SetDeviceViewportParsesTwoWordObject()
    {
        var stmt = SingleStmt<SetBoundsStmt>("SET DEVICE VIEWPORT 0, 0.5, 0, 0.5");
        stmt.Object.Should().Be(GfxRectKind.DeviceViewport);
    }

    [Fact]
    public void SetLineColorParses()
    {
        var stmt = SingleStmt<SetColorStmt>("SET LINE COLOR 3");
        stmt.Target.Should().Be(GfxColorKind.Line);
    }

    [Fact]
    public void GraphLinesPointListParses()
    {
        var stmt = SingleStmt<GraphStmt>("GRAPH LINES: 1,2; 3,4; 5,6");
        stmt.Kind.Should().Be(GfxGeometry.Lines);
        stmt.Points.Should().HaveCount(3);
    }

    [Fact]
    public void GraphTextWithAtParses()
    {
        var stmt = SingleStmt<GraphTextStmt>("GRAPH TEXT, AT 2, 3: \"hi\"");
        stmt.Image.Should().BeNull();
        stmt.Items.Should().HaveCount(1);
    }

    [Fact]
    public void AskMaxColorParsesDespiteMaxBeingABuiltin()
    {
        // MAX stays a builtin function name; ASK MAX COLOR is still recognised.
        var stmt = SingleStmt<AskGfxStmt>("ASK MAX COLOR M");
        stmt.Object.Should().Be(GfxAskObject.MaxColor);
        stmt.Targets.Should().HaveCount(1);
    }

    [Fact]
    public void Dim()
    {
        var stmt = SingleStmt<DimStmt>("DIM A(10), B(2 TO 5, 1 TO 10), S$(20)");
        stmt.Specs.Should().HaveCount(3);
        stmt.Specs[0].Name.Should().Be("A");
        stmt.Specs[0].Bounds.Should().HaveCount(1);
        stmt.Specs[0].Bounds[0].Lower.Should().BeNull(); // implicit lower bound
        stmt.Specs[1].Bounds.Should().HaveCount(2);
        stmt.Specs[1].Bounds[0].Lower.Should().NotBeNull();
        stmt.Specs[2].IsString.Should().BeTrue();
    }

    [Fact]
    public void OptionBase()
    {
        var stmt = SingleStmt<OptionBaseStmt>("OPTION BASE 1");
        stmt.Base.Should().Be(1);
    }

    [Fact]
    public void Rem()
    {
        var stmt = SingleStmt<RemStmt>("REM hello world");
        stmt.Comment.Should().Be("hello world");
    }

    [Fact]
    public void Randomize()
    {
        var (prog, _) = Parse("RANDOMIZE\nRANDOMIZE 42");
        prog.Statements[0].Should().BeOfType<RandomizeStmt>().Which.Seed.Should().BeNull();
        prog.Statements[1].Should().BeOfType<RandomizeStmt>().Which.Seed.Should().NotBeNull();
    }

    [Fact]
    public void EndAndStop()
    {
        var (prog, _) = Parse("STOP\nEND");
        prog.Statements[0].Should().BeOfType<StopStmt>();
        prog.Statements[1].Should().BeOfType<EndStmt>();
    }

    [Fact]
    public void LineLabelAttachedToStatement()
    {
        var stmt = SingleStmt<AssignStmt>("100 LET X = 1");
        stmt.Label.Should().Be(100);
    }

    [Fact]
    public void ColonStatementSeparator()
    {
        var (prog, _) = Parse("LET X = 1 : LET Y = 2");
        prog.Statements.Should().HaveCount(2);
        prog.Statements[0].Should().BeOfType<AssignStmt>();
        prog.Statements[1].Should().BeOfType<AssignStmt>();
    }

    // -- IF ---------------------------------------------------------------

    [Fact]
    public void SingleLineIf()
    {
        var stmt = SingleStmt<IfStmt>("IF X > 0 THEN PRINT \"pos\"");
        stmt.ThenBlock.Should().HaveCount(1);
        stmt.ThenBlock[0].Should().BeOfType<PrintStmt>();
        stmt.ElseBlock.Should().BeNull();
    }

    [Fact]
    public void SingleLineIfElse()
    {
        var stmt = SingleStmt<IfStmt>("IF X > 0 THEN PRINT \"pos\" ELSE PRINT \"non-pos\"");
        stmt.ThenBlock.Should().HaveCount(1);
        stmt.ElseBlock.Should().NotBeNull();
        stmt.ElseBlock!.Should().HaveCount(1);
    }

    [Fact]
    public void BareIfThenLineNumberIsImplicitGoto()
    {
        // ISO 10279 shorthand: "IF c THEN 1990" == "IF c THEN GOTO 1990".
        var stmt = SingleStmt<IfStmt>("IF X > 0 THEN 1990");
        stmt.ThenBlock.Should().HaveCount(1);
        stmt.ThenBlock[0].Should().BeOfType<GotoStmt>();
        stmt.ElseBlock.Should().BeNull();
    }

    [Fact]
    public void BareIfThenElseLineNumbersAreImplicitGotos()
    {
        var stmt = SingleStmt<IfStmt>("IF X > 0 THEN 100 ELSE 200");
        stmt.ThenBlock.Should().ContainSingle().Which.Should().BeOfType<GotoStmt>();
        stmt.ElseBlock.Should().NotBeNull();
        stmt.ElseBlock!.Should().ContainSingle().Which.Should().BeOfType<GotoStmt>();
    }

    [Fact]
    public void BlockIf()
    {
        const string src = """
            IF X > 0 THEN
              PRINT "pos"
              LET Y = 1
            ELSEIF X = 0 THEN
              PRINT "zero"
            ELSE
              PRINT "neg"
            END IF
            """;
        var stmt = SingleStmt<IfStmt>(src);
        stmt.ThenBlock.Should().HaveCount(2);
        stmt.ElseIfs.Should().HaveCount(1);
        stmt.ElseIfs[0].Body.Should().HaveCount(1);
        stmt.ElseBlock.Should().NotBeNull();
        stmt.ElseBlock!.Should().HaveCount(1);
    }

    // -- FOR --------------------------------------------------------------

    [Fact]
    public void ForLoop()
    {
        const string src = """
            FOR I = 1 TO 10 STEP 2
              PRINT I
            NEXT I
            """;
        var stmt = SingleStmt<ForStmt>(src);
        stmt.Variable.Name.Should().Be("I");
        stmt.From.Should().BeOfType<NumberExpr>();
        stmt.To.Should().BeOfType<NumberExpr>();
        stmt.Step.Should().NotBeNull();
        stmt.Body.Should().HaveCount(1);
    }

    [Fact]
    public void ForLoopWithoutStep()
    {
        var stmt = SingleStmt<ForStmt>("FOR I = 1 TO 10\nPRINT I\nNEXT I");
        stmt.Step.Should().BeNull();
    }

    // -- DO ---------------------------------------------------------------

    [Fact]
    public void DoWhile()
    {
        const string src = """
            DO WHILE X > 0
              LET X = X - 1
            LOOP
            """;
        var stmt = SingleStmt<DoStmt>(src);
        stmt.Pre.Should().NotBeNull();
        stmt.Pre!.IsUntil.Should().BeFalse();
        stmt.Post.Should().BeNull();
    }

    [Fact]
    public void DoLoopUntil()
    {
        const string src = """
            DO
              LET X = X + 1
            LOOP UNTIL X > 10
            """;
        var stmt = SingleStmt<DoStmt>(src);
        stmt.Pre.Should().BeNull();
        stmt.Post.Should().NotBeNull();
        stmt.Post!.IsUntil.Should().BeTrue();
    }

    // -- SELECT CASE ------------------------------------------------------

    [Fact]
    public void SelectCase()
    {
        const string src = """
            SELECT CASE X
              CASE 1, 2, 3
                PRINT "low"
              CASE 4 TO 10
                PRINT "mid"
              CASE IS > 10
                PRINT "high"
              CASE ELSE
                PRINT "other"
            END SELECT
            """;
        var stmt = SingleStmt<SelectStmt>(src);
        stmt.Cases.Should().HaveCount(3);
        stmt.Cases[0].Values.Should().HaveCount(3);
        stmt.Cases[0].Values[0].Should().BeOfType<CaseValue>();
        stmt.Cases[1].Values[0].Should().BeOfType<CaseRange>();
        stmt.Cases[2].Values[0].Should().BeOfType<CaseIs>();
        stmt.CaseElse.Should().NotBeNull();
    }

    // -- SUB / FUNCTION / DEF / CALL --------------------------------------

    [Fact]
    public void Sub()
    {
        var stmt = SingleStmt<SubStmt>("SUB GREET(NAME$)\nPRINT NAME$\nEND SUB");
        stmt.Name.Should().Be("GREET");
        stmt.Params.Should().HaveCount(1);
        stmt.Params[0].IsString.Should().BeTrue();
        stmt.Body.Should().HaveCount(1);
    }

    [Fact]
    public void Function()
    {
        var stmt = SingleStmt<FunctionStmt>("FUNCTION DOUBLE(X)\nDOUBLE = X * 2\nEND FUNCTION");
        stmt.Name.Should().Be("DOUBLE");
        stmt.IsString.Should().BeFalse();
        stmt.Params.Should().HaveCount(1);
    }

    [Fact]
    public void Call()
    {
        var stmt = SingleStmt<CallStmt>("CALL GREET(\"Adam\")");
        stmt.Name.Should().Be("GREET");
        stmt.Args.Should().HaveCount(1);
    }

    [Fact]
    public void DefSingleLine()
    {
        var stmt = SingleStmt<DefStmt>("DEF FN DOUBLE(X) = X * 2");
        stmt.SingleLineBody.Should().NotBeNull();
        stmt.MultiLineBody.Should().BeNull();
    }

    [Fact]
    public void Exit()
    {
        var (prog, _) = Parse("EXIT FOR\nEXIT DO\nEXIT SUB");
        prog.Statements.Should().HaveCount(3);
        ((ExitStmt)prog.Statements[0]).Target.Should().Be(ExitTarget.For);
        ((ExitStmt)prog.Statements[1]).Target.Should().Be(ExitTarget.Do);
        ((ExitStmt)prog.Statements[2]).Target.Should().Be(ExitTarget.Sub);
    }

    // -- Expression precedence -------------------------------------------

    [Fact]
    public void AdditiveLeftAssociative()
    {
        var stmt = SingleStmt<AssignStmt>("X = 1 - 2 - 3");
        // Should parse as ((1 - 2) - 3).
        var bin = stmt.Value.Should().BeOfType<BinaryExpr>().Subject;
        bin.Op.Should().Be(BinaryOp.Subtract);
        bin.Right.Should().BeOfType<NumberExpr>().Which.Text.Should().Be("3");
        var inner = bin.Left.Should().BeOfType<BinaryExpr>().Subject;
        inner.Op.Should().Be(BinaryOp.Subtract);
    }

    [Fact]
    public void PowerRightAssociative()
    {
        var stmt = SingleStmt<AssignStmt>("X = 2 ^ 3 ^ 2");
        // Should parse as 2 ^ (3 ^ 2).
        var bin = stmt.Value.Should().BeOfType<BinaryExpr>().Subject;
        bin.Op.Should().Be(BinaryOp.Power);
        bin.Left.Should().BeOfType<NumberExpr>().Which.Text.Should().Be("2");
        bin.Right.Should().BeOfType<BinaryExpr>().Which.Op.Should().Be(BinaryOp.Power);
    }

    [Fact]
    public void MultiplicativeBindsBeforeAdditive()
    {
        var stmt = SingleStmt<AssignStmt>("X = 1 + 2 * 3");
        var bin = stmt.Value.Should().BeOfType<BinaryExpr>().Subject;
        bin.Op.Should().Be(BinaryOp.Add);
        bin.Right.Should().BeOfType<BinaryExpr>().Which.Op.Should().Be(BinaryOp.Multiply);
    }

    [Fact]
    public void RelationalBindsBeforeLogical()
    {
        var stmt = SingleStmt<AssignStmt>("X = A < B AND C > D");
        var bin = stmt.Value.Should().BeOfType<BinaryExpr>().Subject;
        bin.Op.Should().Be(BinaryOp.And);
        bin.Left.Should().BeOfType<BinaryExpr>().Which.Op.Should().Be(BinaryOp.Less);
        bin.Right.Should().BeOfType<BinaryExpr>().Which.Op.Should().Be(BinaryOp.Greater);
    }

    [Fact]
    public void NotIsLowerPrecedenceThanRelational()
    {
        var stmt = SingleStmt<AssignStmt>("X = NOT A < B");
        // Should parse as NOT (A < B).
        var un = stmt.Value.Should().BeOfType<UnaryExpr>().Subject;
        un.Op.Should().Be(UnaryOp.Not);
        un.Operand.Should().BeOfType<BinaryExpr>().Which.Op.Should().Be(BinaryOp.Less);
    }

    [Fact]
    public void StringConcatenation()
    {
        var stmt = SingleStmt<AssignStmt>("S$ = A$ & B$ & C$");
        var bin = stmt.Value.Should().BeOfType<BinaryExpr>().Subject;
        bin.Op.Should().Be(BinaryOp.Concat);
    }

    [Fact]
    public void UnaryMinus()
    {
        var stmt = SingleStmt<AssignStmt>("X = -5");
        var un = stmt.Value.Should().BeOfType<UnaryExpr>().Subject;
        un.Op.Should().Be(UnaryOp.Negate);
    }

    [Fact]
    public void ParensOverridePrecedence()
    {
        var stmt = SingleStmt<AssignStmt>("X = (1 + 2) * 3");
        var bin = stmt.Value.Should().BeOfType<BinaryExpr>().Subject;
        bin.Op.Should().Be(BinaryOp.Multiply);
        bin.Left.Should().BeOfType<ParenExpr>().Which.Inner.Should().BeOfType<BinaryExpr>().Which.Op.Should().Be(BinaryOp.Add);
    }

    [Fact]
    public void FunctionCallExpression()
    {
        var stmt = SingleStmt<AssignStmt>("X = SIN(0.5) + COS(A)");
        var bin = stmt.Value.Should().BeOfType<BinaryExpr>().Subject;
        bin.Op.Should().Be(BinaryOp.Add);
        bin.Left.Should().BeOfType<CallOrIndexExpr>().Which.Name.Should().Be("SIN");
        bin.Right.Should().BeOfType<CallOrIndexExpr>().Which.Name.Should().Be("COS");
    }

    // -- Error recovery --------------------------------------------------

    [Fact]
    public void BadStatementDoesNotKillTheRest()
    {
        var (prog, diags) = Parse("LET X = \nLET Y = 2\nLET Z = 3");
        diags.HasErrors.Should().BeTrue();
        // Statements after the broken first one should still parse.
        prog.Statements.Should().HaveCount(2);
        ((AssignStmt)prog.Statements[0]).Target.Should().BeOfType<NameRefExpr>().Which.Name.Should().Be("Y");
        ((AssignStmt)prog.Statements[1]).Target.Should().BeOfType<NameRefExpr>().Which.Name.Should().Be("Z");
    }

    [Fact]
    public void RealisticProgram()
    {
        const string src = """
            100 REM compute factorial
            110 INPUT "n? "; N
            120 LET F = 1
            130 FOR I = 1 TO N
            140   LET F = F * I
            150 NEXT I
            160 PRINT "n!="; F
            170 END
            """;
        var (prog, diags) = Parse(src);
        diags.HasErrors.Should().BeFalse();
        // The 8 source lines collapse to 6 program-level statements: the FOR
        // block consumes its body and trailing NEXT.
        prog.Statements.Should().HaveCount(6);
        prog.Statements[0].Should().BeOfType<RemStmt>();
        prog.Statements[1].Should().BeOfType<InputStmt>();
        prog.Statements[2].Should().BeOfType<AssignStmt>();
        // The body holds "LET F = F * I" plus a labeled no-op that preserves the
        // line label (150) sitting on the terminating NEXT, so it stays a valid
        // jump target.
        var forBody = prog.Statements[3].Should().BeOfType<ForStmt>().Which.Body;
        forBody.Should().HaveCount(2);
        forBody[0].Should().BeOfType<AssignStmt>();
        forBody[1].Should().BeOfType<RemStmt>().Which.Label.Should().Be(150);
        prog.Statements[4].Should().BeOfType<PrintStmt>();
        prog.Statements[5].Should().BeOfType<EndStmt>();
    }
}
