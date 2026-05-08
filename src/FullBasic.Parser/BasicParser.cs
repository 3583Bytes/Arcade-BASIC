using FullBasic.Core;
using FullBasic.Lexer;
using FullBasic.Parser.Ast;

namespace FullBasic.Parser;

/// <summary>
/// Recursive-descent parser for ISO/IEC 10279 Full BASIC. Consumes a token
/// stream from <see cref="BasicLexer"/> and produces a <see cref="Program"/>
/// AST plus diagnostics.
///
/// Scope: interpreter-core subset. File I/O, MAT, WHEN/USE/HANDLER, MODULE,
/// and picture/graphics statements are NOT yet handled — they fall into the
/// statement-level error path.
///
/// Error recovery: on a syntactic error inside a statement we emit a
/// diagnostic and skip tokens up to the next Newline (or block terminator)
/// so subsequent lines still parse.
/// </summary>
public sealed partial class BasicParser
{
    // Diagnostic codes (FB02xx range = parser)
    public const string ErrExpectedToken = "FB0201";
    public const string ErrExpectedExpression = "FB0202";
    public const string ErrExpectedStatement = "FB0203";
    public const string ErrUnsupportedSyntax = "FB0204";
    public const string ErrBadAssignmentTarget = "FB0205";

    private readonly IReadOnlyList<Token> _tokens;
    private readonly SourceFile _file;
    private readonly DiagnosticBag _diags;
    private int _pos;

    public BasicParser(IReadOnlyList<Token> tokens, SourceFile file, DiagnosticBag diagnostics)
    {
        _tokens = tokens;
        _file = file;
        _diags = diagnostics;
    }

    public Program ParseProgram()
    {
        var stmts = new List<Stmt>();
        var startSpan = _tokens[0].Span;

        SkipNewlines();
        while (!AtEnd())
        {
            var stmt = ParseLabeledStatement();
            if (stmt is not null)
            {
                stmts.Add(stmt);
            }
            SkipNewlines();
        }

        var lastEnd = stmts.Count > 0 ? stmts[^1].Span.End : startSpan.Start;
        return new Program(new SourceSpan(_file, startSpan.Start, lastEnd - startSpan.Start), stmts);
    }

    /// <summary>
    /// Parse a single statement, optionally prefixed by a line label. Statements
    /// may also be chained on the same line via colons. Returns null only if
    /// recovery left us at EOF without consuming a real statement.
    /// </summary>
    private Stmt? ParseLabeledStatement()
    {
        int? label = null;
        if (Check(TokenKind.LineLabel))
        {
            var labelTok = Advance();
            label = int.Parse(labelTok.Text);
        }

        // Allow a single label on its own line (rare but legal).
        if (Check(TokenKind.Newline) || AtEnd())
        {
            return null;
        }

        var stmt = ParseStatement();
        if (stmt is null)
        {
            SkipToNextStatement();
            return null;
        }

        if (label is not null)
        {
            stmt = stmt with { Label = label };
        }

        // If a colon follows, the next statement on this line is a peer — we
        // parse it as a separate statement on the next iteration of the program
        // loop. Consume the colon here; the program loop continues without
        // expecting a Newline.
        if (Match(TokenKind.Colon))
        {
            return stmt;
        }

        // Otherwise expect Newline or EOF.
        if (!AtEnd() && !Check(TokenKind.Newline))
        {
            ErrorAt(Peek(), ErrExpectedToken, "expected end of line or ':' separator");
            SkipToNextStatement();
        }

        return stmt;
    }

    /// <summary>Dispatch on the first token of a statement.</summary>
    private Stmt? ParseStatement()
    {
        var tok = Peek();
        return tok.Kind switch
        {
            TokenKind.KwLet => ParseLet(explicitLet: true),
            TokenKind.KwPrint => ParsePrint(),
            TokenKind.KwInput => ParseInput(),
            TokenKind.KwLine => ParseLineInputOrError(),
            TokenKind.KwRead => ParseRead(),
            TokenKind.KwData => ParseData(),
            TokenKind.KwRestore => ParseRestore(),
            TokenKind.KwGoto or TokenKind.KwGo => ParseGoto(),
            TokenKind.KwGosub => ParseGosub(),
            TokenKind.KwReturn => ParseReturn(),
            TokenKind.KwStop => ParseStop(),
            TokenKind.KwEnd => ParseEnd(),
            TokenKind.KwRun => ParseRun(),
            TokenKind.KwRandomize => ParseRandomize(),
            TokenKind.KwRem => ParseRem(),
            TokenKind.KwDim => ParseDim(),
            TokenKind.KwOption => ParseOption(),
            TokenKind.KwIf => ParseIf(),
            TokenKind.KwFor => ParseFor(),
            TokenKind.KwNext => ParseNext(),
            TokenKind.KwDo => ParseDo(),
            TokenKind.KwLoop => ParseLoop(),
            TokenKind.KwSelect => ParseSelect(),
            TokenKind.KwExit => ParseExit(),
            TokenKind.KwDef => ParseDef(),
            TokenKind.KwSub => ParseSub(),
            TokenKind.KwFunction => ParseFunction(),
            TokenKind.KwCall => ParseCall(),
            TokenKind.KwMat => ParseMat(),
            TokenKind.KwOpen => ParseOpenStmt(),
            TokenKind.KwClose => ParseCloseStmt(),
            TokenKind.KwWhen => ParseWhen(),
            TokenKind.KwHandler => ParseHandler(),
            TokenKind.KwCause => ParseCause(),
            TokenKind.KwRetry => ParseRetry(),
            TokenKind.KwContinue => ParseContinueResume(),
            TokenKind.KwModule => ParseModule(),
            TokenKind.KwPublic => ParsePublicDecl(),
            TokenKind.KwPrivate => ParsePrivateDecl(),
            TokenKind.Identifier or TokenKind.StringIdentifier => ParseAssignmentOrFunctionCall(),
            _ => UnsupportedStatement(),
        };
    }

    // -- Simple statements ------------------------------------------------

    private AssignStmt? ParseLet(bool explicitLet)
    {
        var start = Peek();
        if (explicitLet)
        {
            Advance(); // LET
        }

        var target = ParseAssignmentTarget();
        if (target is null)
        {
            return null;
        }

        if (!ExpectKind(TokenKind.Equal, "'='"))
        {
            return null;
        }

        var value = ParseExpression();
        if (value is null)
        {
            return null;
        }

        return new AssignStmt(SpanFrom(start, value.Span), target, value, explicitLet);
    }

    private Stmt? ParseAssignmentOrFunctionCall()
    {
        // Without an explicit LET, this is an assignment whose target is an
        // identifier (possibly subscripted). We don't have implicit calls in
        // statement position outside CALL, so an Identifier here means assign.
        return ParseLet(explicitLet: false);
    }

    /// <summary>The LHS of an assignment is a name or subscripted name only.</summary>
    private Expr? ParseAssignmentTarget()
    {
        var tok = Peek();
        if (tok.Kind != TokenKind.Identifier && tok.Kind != TokenKind.StringIdentifier)
        {
            ErrorAt(tok, ErrBadAssignmentTarget, "assignment target must be a variable name");
            return null;
        }

        Advance();
        var name = NameWithoutDollar(tok.Text);
        var isString = tok.Kind == TokenKind.StringIdentifier;

        if (Match(TokenKind.LParen))
        {
            var args = ParseExpressionList(TokenKind.RParen);
            if (!ExpectKind(TokenKind.RParen, "')'"))
            {
                return null;
            }
            return new CallOrIndexExpr(SpanBetween(tok.Span, Previous().Span), name, isString, args);
        }

        return new NameRefExpr(tok.Span, name, isString);
    }

    private Stmt? ParsePrint()
    {
        var start = Advance(); // PRINT
        if (Check(TokenKind.Hash))
        {
            return ParsePrintFile(start);
        }
        var items = new List<PrintItem>();

        while (!AtStatementEnd())
        {
            if (Match(TokenKind.Comma))
            {
                items.Add(new PrintComma(Previous().Span));
                continue;
            }
            if (Match(TokenKind.Semicolon))
            {
                items.Add(new PrintSemicolon(Previous().Span));
                continue;
            }

            // Recognize TAB(expr) as a print-tab item.
            if (Check(TokenKind.Identifier) && string.Equals(Peek().Text, "TAB", StringComparison.OrdinalIgnoreCase)
                && PeekKind(1) == TokenKind.LParen)
            {
                var tabTok = Advance(); // TAB
                Advance();              // (
                var col = ParseExpression();
                if (col is null)
                {
                    return new PrintStmt(SpanFrom(start, Previous().Span), items);
                }
                if (!ExpectKind(TokenKind.RParen, "')'"))
                {
                    return new PrintStmt(SpanFrom(start, Previous().Span), items);
                }
                items.Add(new PrintTab(SpanBetween(tabTok.Span, Previous().Span), col));
                continue;
            }

            var expr = ParseExpression();
            if (expr is null)
            {
                break;
            }
            items.Add(new PrintExprItem(expr.Span, expr));
        }

        return new PrintStmt(SpanFrom(start, Previous().Span), items);
    }

    private Stmt? ParseInput()
    {
        var start = Advance(); // INPUT
        if (Check(TokenKind.Hash))
        {
            return ParseInputFile(start);
        }
        Expr? prompt = null;
        var promptIsSemi = false;

        // Optional prompt: a string literal followed by ; or ,
        if (Check(TokenKind.StringLiteral)
            && (PeekKind(1) == TokenKind.Semicolon || PeekKind(1) == TokenKind.Comma))
        {
            prompt = ParseExpression();
            promptIsSemi = Check(TokenKind.Semicolon);
            Advance(); // ; or ,
        }

        var targets = new List<Expr>();
        do
        {
            var t = ParseAssignmentTarget();
            if (t is null) return null;
            targets.Add(t);
        }
        while (Match(TokenKind.Comma));

        return new InputStmt(SpanFrom(start, Previous().Span), prompt, promptIsSemi, targets);
    }

    /// <summary>LINE INPUT — currently only this form; LINE in other contexts is unsupported.</summary>
    private Stmt? ParseLineInputOrError()
    {
        var start = Advance(); // LINE
        if (!Check(TokenKind.KwInput))
        {
            ErrorAt(start, ErrUnsupportedSyntax, "'LINE' must be followed by 'INPUT' in this context");
            return null;
        }
        Advance(); // INPUT

        if (Check(TokenKind.Hash))
        {
            return ParseLineInputFile(start);
        }

        Expr? prompt = null;
        var promptIsSemi = false;
        if (Check(TokenKind.StringLiteral)
            && (PeekKind(1) == TokenKind.Semicolon || PeekKind(1) == TokenKind.Comma))
        {
            prompt = ParseExpression();
            promptIsSemi = Check(TokenKind.Semicolon);
            Advance();
        }

        var target = ParseAssignmentTarget();
        if (target is null) return null;

        return new LineInputStmt(SpanFrom(start, target.Span), prompt, promptIsSemi, target);
    }

    private ReadStmt? ParseRead()
    {
        var start = Advance(); // READ
        var targets = new List<Expr>();
        do
        {
            var t = ParseAssignmentTarget();
            if (t is null) return null;
            targets.Add(t);
        }
        while (Match(TokenKind.Comma));
        return new ReadStmt(SpanFrom(start, Previous().Span), targets);
    }

    private DataStmt? ParseData()
    {
        var start = Advance(); // DATA
        var items = new List<DataItem>();

        do
        {
            // DATA items are either string literals, signed numeric literals,
            // or unquoted strings (the parser/sema disambiguates by content).
            var tok = Peek();
            if (tok.Kind == TokenKind.StringLiteral)
            {
                Advance();
                items.Add(new DataItem(tok.Span, IsString: true, UnescapeString(tok.Text)));
            }
            else if (tok.Kind == TokenKind.NumericLiteral || tok.Kind == TokenKind.Plus || tok.Kind == TokenKind.Minus)
            {
                var sign = "";
                if (tok.Kind == TokenKind.Plus || tok.Kind == TokenKind.Minus)
                {
                    Advance();
                    sign = tok.Text;
                }
                if (!Check(TokenKind.NumericLiteral))
                {
                    ErrorAt(Peek(), ErrExpectedToken, "expected numeric value in DATA item");
                    return null;
                }
                var num = Advance();
                items.Add(new DataItem(SpanBetween(tok.Span, num.Span), IsString: false, sign + num.Text));
            }
            else
            {
                ErrorAt(tok, ErrExpectedToken, "expected DATA item (string or number)");
                return null;
            }
        }
        while (Match(TokenKind.Comma));

        return new DataStmt(SpanFrom(start, Previous().Span), items);
    }

    private RestoreStmt ParseRestore()
    {
        var start = Advance(); // RESTORE
        Expr? labelExpr = null;
        if (!AtStatementEnd())
        {
            labelExpr = ParseExpression();
        }
        return new RestoreStmt(SpanFrom(start, Previous().Span), labelExpr);
    }

    private GotoStmt? ParseGoto()
    {
        var start = Peek();
        // Accept GOTO or GO TO.
        if (Match(TokenKind.KwGo))
        {
            if (!ExpectKind(TokenKind.KwTo, "'TO' (after 'GO')"))
            {
                return null;
            }
        }
        else
        {
            Advance(); // GOTO
        }
        var target = ParseExpression();
        if (target is null) return null;
        return new GotoStmt(SpanFrom(start, target.Span), target);
    }

    private GosubStmt? ParseGosub()
    {
        var start = Advance(); // GOSUB
        var target = ParseExpression();
        if (target is null) return null;
        return new GosubStmt(SpanFrom(start, target.Span), target);
    }

    private ReturnStmt ParseReturn()
    {
        var t = Advance();
        return new ReturnStmt(t.Span);
    }

    private StopStmt ParseStop()
    {
        var t = Advance();
        return new StopStmt(t.Span);
    }

    private Stmt ParseEnd()
    {
        var t = Advance(); // END
        // Block terminators: END IF / END SELECT / END SUB / END FUNCTION /
        // END DEF / END WHEN / END MODULE.
        if (Check(TokenKind.KwIf)) { Advance(); return new EndBlockStmt(SpanFrom(t, Previous().Span), EndBlockKind.EndIf); }
        if (Check(TokenKind.KwSelect)) { Advance(); return new EndBlockStmt(SpanFrom(t, Previous().Span), EndBlockKind.EndSelect); }
        if (Check(TokenKind.KwSub)) { Advance(); return new EndBlockStmt(SpanFrom(t, Previous().Span), EndBlockKind.EndSub); }
        if (Check(TokenKind.KwFunction)) { Advance(); return new EndBlockStmt(SpanFrom(t, Previous().Span), EndBlockKind.EndFunction); }
        if (Check(TokenKind.KwDef)) { Advance(); return new EndBlockStmt(SpanFrom(t, Previous().Span), EndBlockKind.EndDef); }
        if (Check(TokenKind.KwWhen)) { Advance(); return new EndBlockStmt(SpanFrom(t, Previous().Span), EndBlockKind.EndWhen); }
        if (Check(TokenKind.KwModule)) { Advance(); return new EndBlockStmt(SpanFrom(t, Previous().Span), EndBlockKind.EndModule); }
        return new EndStmt(t.Span);
    }

    private RunStmt ParseRun()
    {
        var t = Advance();
        return new RunStmt(t.Span);
    }

    private RandomizeStmt ParseRandomize()
    {
        var start = Advance(); // RANDOMIZE
        Expr? seed = null;
        if (!AtStatementEnd())
        {
            seed = ParseExpression();
        }
        return new RandomizeStmt(SpanFrom(start, Previous().Span), seed);
    }

    private RemStmt ParseRem()
    {
        var t = Advance();
        // Token text starts with "REM" (case may vary). Strip the keyword and
        // any following space to produce just the comment.
        var comment = t.Text.Length > 3 ? t.Text[3..].TrimStart() : "";
        return new RemStmt(t.Span, comment);
    }

    private DimStmt? ParseDim()
    {
        var start = Advance(); // DIM
        var specs = new List<DimSpec>();
        do
        {
            var nameTok = Peek();
            if (nameTok.Kind != TokenKind.Identifier && nameTok.Kind != TokenKind.StringIdentifier)
            {
                ErrorAt(nameTok, ErrExpectedToken, "expected variable name in DIM");
                return null;
            }
            Advance();
            var isString = nameTok.Kind == TokenKind.StringIdentifier;
            var name = NameWithoutDollar(nameTok.Text);

            if (!ExpectKind(TokenKind.LParen, "'(' (DIM requires bounds)"))
            {
                return null;
            }
            var bounds = new List<DimBound>();
            do
            {
                var lower = ParseExpression();
                if (lower is null) return null;
                Expr? upper;
                if (Match(TokenKind.KwTo))
                {
                    upper = ParseExpression();
                    if (upper is null) return null;
                    bounds.Add(new DimBound(SpanBetween(lower.Span, upper.Span), lower, upper));
                }
                else
                {
                    upper = lower;
                    bounds.Add(new DimBound(lower.Span, null, upper));
                }
            }
            while (Match(TokenKind.Comma));

            if (!ExpectKind(TokenKind.RParen, "')'"))
            {
                return null;
            }
            specs.Add(new DimSpec(SpanBetween(nameTok.Span, Previous().Span), name, isString, bounds));
        }
        while (Match(TokenKind.Comma));

        return new DimStmt(SpanFrom(start, Previous().Span), specs);
    }

    private Stmt? ParseOption()
    {
        var start = Advance(); // OPTION
        if (Match(TokenKind.KwBase))
        {
            if (!Check(TokenKind.NumericLiteral))
            {
                ErrorAt(Peek(), ErrExpectedToken, "expected 0 or 1 after OPTION BASE");
                return null;
            }
            var n = Advance();
            if (!int.TryParse(n.Text, out var b) || (b != 0 && b != 1))
            {
                ErrorAt(n, ErrUnsupportedSyntax, "OPTION BASE must be 0 or 1");
                return null;
            }
            return new OptionBaseStmt(SpanFrom(start, n.Span), b);
        }

        // OPTION ARITHMETIC <DECIMAL|NATIVE|FIXED>
        if (Check(TokenKind.Identifier) && string.Equals(Peek().Text, "ARITHMETIC", StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            var modeTok = Peek();
            ArithmeticMode mode;
            if (string.Equals(modeTok.Text, "DECIMAL", StringComparison.OrdinalIgnoreCase)) mode = ArithmeticMode.Decimal;
            else if (string.Equals(modeTok.Text, "NATIVE", StringComparison.OrdinalIgnoreCase)) mode = ArithmeticMode.Native;
            else if (modeTok.Kind == TokenKind.KwFixed) mode = ArithmeticMode.Fixed;
            else
            {
                ErrorAt(modeTok, ErrExpectedToken, "expected DECIMAL, NATIVE, or FIXED");
                return null;
            }
            Advance();
            return new OptionArithmeticStmt(SpanFrom(start, Previous().Span), mode);
        }

        ErrorAt(Peek(), ErrUnsupportedSyntax, "unsupported OPTION clause");
        return null;
    }

    // -- Block statements -------------------------------------------------

    private Stmt? ParseIf()
    {
        var start = Advance(); // IF
        var cond = ParseExpression();
        if (cond is null) return null;
        if (!ExpectKind(TokenKind.KwThen, "'THEN'")) return null;

        // Single-line vs block form: if THEN is followed by a Newline, this is
        // a block IF; otherwise it's single-line.
        if (Match(TokenKind.Newline))
        {
            return ParseBlockIf(start, cond);
        }

        return ParseSingleLineIf(start, cond);
    }

    private Stmt? ParseSingleLineIf(Token start, Expr cond)
    {
        var thenStmts = new List<Stmt>();
        // One statement (or chain via colon) until ELSE or end-of-line.
        while (!AtStatementEnd() && !Check(TokenKind.KwElse))
        {
            var s = ParseStatement();
            if (s is null) return null;
            thenStmts.Add(s);
            if (!Match(TokenKind.Colon)) break;
        }

        IReadOnlyList<Stmt>? elseStmts = null;
        if (Match(TokenKind.KwElse))
        {
            var els = new List<Stmt>();
            while (!AtStatementEnd())
            {
                var s = ParseStatement();
                if (s is null) return null;
                els.Add(s);
                if (!Match(TokenKind.Colon)) break;
            }
            elseStmts = els;
        }

        return new IfStmt(SpanFrom(start, Previous().Span), cond, thenStmts, [], elseStmts);
    }

    private Stmt? ParseBlockIf(Token start, Expr cond)
    {
        var thenBlock = ParseStatementBlock(stoppers:
            [TokenKind.KwElseif, TokenKind.KwElse, TokenKind.KwEnd]);

        var elseIfs = new List<ElseIfClause>();
        while (Check(TokenKind.KwElseif))
        {
            var ei = Advance();
            var econd = ParseExpression();
            if (econd is null) return null;
            if (!ExpectKind(TokenKind.KwThen, "'THEN'")) return null;
            if (!Match(TokenKind.Newline))
            {
                ErrorAt(Peek(), ErrExpectedToken, "expected end of line after ELSEIF ... THEN");
                return null;
            }
            var body = ParseStatementBlock(stoppers:
                [TokenKind.KwElseif, TokenKind.KwElse, TokenKind.KwEnd]);
            elseIfs.Add(new ElseIfClause(SpanFrom(ei, Previous().Span), econd, body));
        }

        IReadOnlyList<Stmt>? elseBlock = null;
        if (Match(TokenKind.KwElse))
        {
            if (!Match(TokenKind.Newline))
            {
                ErrorAt(Peek(), ErrExpectedToken, "expected end of line after ELSE");
                return null;
            }
            elseBlock = ParseStatementBlock(stoppers: [TokenKind.KwEnd]);
        }

        if (!ExpectKind(TokenKind.KwEnd, "'END IF'")) return null;
        if (!ExpectKind(TokenKind.KwIf, "'IF' (END IF)")) return null;

        return new IfStmt(SpanFrom(start, Previous().Span), cond, thenBlock, elseIfs, elseBlock);
    }

    private Stmt? ParseFor()
    {
        var start = Advance(); // FOR
        if (Peek().Kind != TokenKind.Identifier)
        {
            ErrorAt(Peek(), ErrExpectedToken, "expected numeric variable in FOR");
            return null;
        }
        var varTok = Advance();
        var variable = new NameRefExpr(varTok.Span, varTok.Text, IsString: false);

        if (!ExpectKind(TokenKind.Equal, "'='")) return null;
        var from = ParseExpression();
        if (from is null) return null;
        if (!ExpectKind(TokenKind.KwTo, "'TO'")) return null;
        var to = ParseExpression();
        if (to is null) return null;

        Expr? step = null;
        if (Match(TokenKind.KwStep))
        {
            step = ParseExpression();
            if (step is null) return null;
        }

        if (!Match(TokenKind.Newline))
        {
            ErrorAt(Peek(), ErrExpectedToken, "expected end of line after FOR header");
            return null;
        }

        var body = ParseStatementBlock(stoppers: [TokenKind.KwNext]);

        if (!ExpectKind(TokenKind.KwNext, "'NEXT'")) return null;
        // NEXT optionally names the loop variable.
        if (Check(TokenKind.Identifier))
        {
            Advance();
        }

        return new ForStmt(SpanFrom(start, Previous().Span), variable, from, to, step, body);
    }

    private Stmt? ParseNext()
    {
        var start = Advance(); // NEXT
        NameRefExpr? variable = null;
        if (Check(TokenKind.Identifier))
        {
            var tok = Advance();
            variable = new NameRefExpr(tok.Span, tok.Text, IsString: false);
        }
        return new NextStmt(SpanFrom(start, Previous().Span), variable);
    }

    private Stmt? ParseDo()
    {
        var start = Advance(); // DO
        DoCondition? pre = null;
        if (Match(TokenKind.KwWhile))
        {
            var c = ParseExpression();
            if (c is null) return null;
            pre = new DoCondition(IsUntil: false, c);
        }
        else if (Match(TokenKind.KwUntil))
        {
            var c = ParseExpression();
            if (c is null) return null;
            pre = new DoCondition(IsUntil: true, c);
        }

        if (!Match(TokenKind.Newline))
        {
            ErrorAt(Peek(), ErrExpectedToken, "expected end of line after DO");
            return null;
        }

        var body = ParseStatementBlock(stoppers: [TokenKind.KwLoop]);

        if (!ExpectKind(TokenKind.KwLoop, "'LOOP'")) return null;
        DoCondition? post = null;
        if (Match(TokenKind.KwWhile))
        {
            var c = ParseExpression();
            if (c is null) return null;
            post = new DoCondition(IsUntil: false, c);
        }
        else if (Match(TokenKind.KwUntil))
        {
            var c = ParseExpression();
            if (c is null) return null;
            post = new DoCondition(IsUntil: true, c);
        }

        return new DoStmt(SpanFrom(start, Previous().Span), pre, body, post);
    }

    private Stmt? ParseLoop()
    {
        // Bare LOOP outside a DO block — should never appear; signal error.
        var t = Advance();
        ErrorAt(t, ErrUnsupportedSyntax, "'LOOP' must terminate a 'DO' block");
        return null;
    }

    private Stmt? ParseSelect()
    {
        var start = Advance(); // SELECT
        if (!ExpectKind(TokenKind.KwCase, "'CASE'")) return null;
        var subject = ParseExpression();
        if (subject is null) return null;
        if (!Match(TokenKind.Newline))
        {
            ErrorAt(Peek(), ErrExpectedToken, "expected end of line after SELECT CASE");
            return null;
        }

        var cases = new List<CaseClause>();
        IReadOnlyList<Stmt>? caseElse = null;

        while (true)
        {
            SkipNewlines();
            if (!Check(TokenKind.KwCase)) break;
            var caseTok = Advance();

            if (Check(TokenKind.KwElse))
            {
                Advance();
                if (!Match(TokenKind.Newline))
                {
                    ErrorAt(Peek(), ErrExpectedToken, "expected end of line after CASE ELSE");
                    return null;
                }
                caseElse = ParseStatementBlock(stoppers: [TokenKind.KwEnd]);
                break;
            }

            var values = new List<CaseSpec>();
            do
            {
                var spec = ParseCaseSpec();
                if (spec is null) return null;
                values.Add(spec);
            }
            while (Match(TokenKind.Comma));

            if (!Match(TokenKind.Newline))
            {
                ErrorAt(Peek(), ErrExpectedToken, "expected end of line after CASE values");
                return null;
            }
            var body = ParseStatementBlock(stoppers: [TokenKind.KwCase, TokenKind.KwEnd]);
            cases.Add(new CaseClause(SpanFrom(caseTok, Previous().Span), values, body));
        }

        if (!ExpectKind(TokenKind.KwEnd, "'END SELECT'")) return null;
        if (!ExpectKind(TokenKind.KwSelect, "'SELECT' (END SELECT)")) return null;

        return new SelectStmt(SpanFrom(start, Previous().Span), subject, cases, caseElse);
    }

    private CaseSpec? ParseCaseSpec()
    {
        // CASE IS rel-op expr
        if (Match(TokenKind.KwIs))
        {
            var op = TryConvertToBinaryOp(Peek());
            if (op is null)
            {
                ErrorAt(Peek(), ErrExpectedToken, "expected relational operator after CASE IS");
                return null;
            }
            Advance();
            var v = ParseExpression();
            if (v is null) return null;
            return new CaseIs(SpanBetween(Previous().Span, v.Span), op.Value, v);
        }

        // CASE expr [TO expr]
        var first = ParseExpression();
        if (first is null) return null;
        if (Match(TokenKind.KwTo))
        {
            var second = ParseExpression();
            if (second is null) return null;
            return new CaseRange(SpanBetween(first.Span, second.Span), first, second);
        }
        return new CaseValue(first.Span, first);
    }

    private Stmt? ParseExit()
    {
        var start = Advance(); // EXIT
        var t = Peek();
        ExitTarget target = t.Kind switch
        {
            TokenKind.KwFor => ExitTarget.For,
            TokenKind.KwDo => ExitTarget.Do,
            TokenKind.KwSub => ExitTarget.Sub,
            TokenKind.KwFunction => ExitTarget.Function,
            TokenKind.KwDef => ExitTarget.Def,
            TokenKind.KwHandler => ExitTarget.Handler,
            TokenKind.KwSelect => ExitTarget.Select,
            TokenKind.KwWhen => ExitTarget.When,
            _ => (ExitTarget)(-1),
        };
        if ((int)target == -1)
        {
            ErrorAt(t, ErrExpectedToken, "expected FOR, DO, SUB, FUNCTION, DEF, SELECT, WHEN, or HANDLER after EXIT");
            return null;
        }
        Advance();
        return new ExitStmt(SpanFrom(start, Previous().Span), target);
    }

    // -- Definitions ------------------------------------------------------

    private Stmt? ParseDef()
    {
        var start = Advance(); // DEF

        // DEF FNxxx[(params)] = expr
        // DEF FNxxx[(params)] / ... / END DEF
        // Optional FN keyword (some implementations require it; we accept either).
        bool sawFnKeyword = Match(TokenKind.KwFn);

        if (Peek().Kind != TokenKind.Identifier && Peek().Kind != TokenKind.StringIdentifier)
        {
            ErrorAt(Peek(), ErrExpectedToken, "expected function name after DEF");
            return null;
        }

        var nameTok = Advance();
        var isString = nameTok.Kind == TokenKind.StringIdentifier;
        var rawName = NameWithoutDollar(nameTok.Text);
        // Per spec, DEF function names start with FN. If the user wrote `DEF FN x`
        // we keep the joined form `FNx` here for sema; if they wrote `DEF FNx` we
        // already have it. Support both.
        var name = sawFnKeyword && !rawName.StartsWith("FN", StringComparison.OrdinalIgnoreCase)
            ? "FN" + rawName
            : rawName;

        var paramList = ParseOptionalParamList();
        if (paramList is null) return null;

        if (Match(TokenKind.Equal))
        {
            // Single-line form.
            var body = ParseExpression();
            if (body is null) return null;
            return new DefStmt(SpanFrom(start, body.Span), name, isString, paramList, body, null);
        }

        // Multi-line form.
        if (!Match(TokenKind.Newline))
        {
            ErrorAt(Peek(), ErrExpectedToken, "expected '=' (single-line DEF) or newline (multi-line DEF)");
            return null;
        }
        var multiBody = ParseStatementBlock(stoppers: [TokenKind.KwEnd]);
        if (!ExpectKind(TokenKind.KwEnd, "'END DEF'")) return null;
        if (!ExpectKind(TokenKind.KwDef, "'DEF' (END DEF)")) return null;
        return new DefStmt(SpanFrom(start, Previous().Span), name, isString, paramList, null, multiBody);
    }

    private Stmt? ParseSub()
    {
        var start = Advance(); // SUB
        if (Peek().Kind != TokenKind.Identifier)
        {
            ErrorAt(Peek(), ErrExpectedToken, "expected SUB name");
            return null;
        }
        var nameTok = Advance();
        var paramList = ParseOptionalParamList();
        if (paramList is null) return null;
        if (!Match(TokenKind.Newline))
        {
            ErrorAt(Peek(), ErrExpectedToken, "expected end of line after SUB header");
            return null;
        }
        var body = ParseStatementBlock(stoppers: [TokenKind.KwEnd]);
        if (!ExpectKind(TokenKind.KwEnd, "'END SUB'")) return null;
        if (!ExpectKind(TokenKind.KwSub, "'SUB' (END SUB)")) return null;
        return new SubStmt(SpanFrom(start, Previous().Span), nameTok.Text, paramList, body);
    }

    private Stmt? ParseFunction()
    {
        var start = Advance(); // FUNCTION
        if (Peek().Kind != TokenKind.Identifier && Peek().Kind != TokenKind.StringIdentifier)
        {
            ErrorAt(Peek(), ErrExpectedToken, "expected FUNCTION name");
            return null;
        }
        var nameTok = Advance();
        var isString = nameTok.Kind == TokenKind.StringIdentifier;
        var name = NameWithoutDollar(nameTok.Text);
        var paramList = ParseOptionalParamList();
        if (paramList is null) return null;
        if (!Match(TokenKind.Newline))
        {
            ErrorAt(Peek(), ErrExpectedToken, "expected end of line after FUNCTION header");
            return null;
        }
        var body = ParseStatementBlock(stoppers: [TokenKind.KwEnd]);
        if (!ExpectKind(TokenKind.KwEnd, "'END FUNCTION'")) return null;
        if (!ExpectKind(TokenKind.KwFunction, "'FUNCTION' (END FUNCTION)")) return null;
        return new FunctionStmt(SpanFrom(start, Previous().Span), name, isString, paramList, body);
    }

    private Stmt? ParseCall()
    {
        var start = Advance(); // CALL
        if (Peek().Kind != TokenKind.Identifier)
        {
            ErrorAt(Peek(), ErrExpectedToken, "expected SUB name after CALL");
            return null;
        }
        var nameTok = Advance();
        var args = new List<Expr>();
        if (Match(TokenKind.LParen))
        {
            if (!Check(TokenKind.RParen))
            {
                do
                {
                    var e = ParseExpression();
                    if (e is null) return null;
                    args.Add(e);
                }
                while (Match(TokenKind.Comma));
            }
            if (!ExpectKind(TokenKind.RParen, "')'")) return null;
        }
        return new CallStmt(SpanFrom(start, Previous().Span), nameTok.Text, args);
    }

    private List<Param>? ParseOptionalParamList()
    {
        var ps = new List<Param>();
        if (!Match(TokenKind.LParen))
        {
            return ps;
        }
        if (Check(TokenKind.RParen))
        {
            Advance();
            return ps;
        }
        do
        {
            var t = Peek();
            if (t.Kind != TokenKind.Identifier && t.Kind != TokenKind.StringIdentifier)
            {
                ErrorAt(t, ErrExpectedToken, "expected parameter name");
                return null;
            }
            Advance();
            var isString = t.Kind == TokenKind.StringIdentifier;
            var name = NameWithoutDollar(t.Text);
            var isArray = false;
            if (Match(TokenKind.LParen))
            {
                isArray = true;
                if (!ExpectKind(TokenKind.RParen, "')' (array parameter)")) return null;
            }
            ps.Add(new Param(t.Span, name, isString, isArray));
        }
        while (Match(TokenKind.Comma));

        if (!ExpectKind(TokenKind.RParen, "')'")) return null;
        return ps;
    }

    // -- Block helpers ----------------------------------------------------

    private List<Stmt> ParseStatementBlock(IReadOnlyList<TokenKind> stoppers)
    {
        var stmts = new List<Stmt>();
        while (!AtEnd())
        {
            SkipNewlines();
            if (AtEnd()) break;

            // A block stopper may sit behind an optional line label
            // (e.g. "150 NEXT I" inside a FOR body). Peek past one label.
            var labelOffset = Peek().Kind == TokenKind.LineLabel ? 1 : 0;
            var headKind = PeekKind(labelOffset);
            if (stoppers.Contains(headKind))
            {
                // Consume the leading label, if any, so the caller's
                // ExpectKind sees the stopper directly. The label value is
                // discarded — block terminators don't need it on the AST.
                if (labelOffset == 1) Advance();
                break;
            }

            var s = ParseLabeledStatement();
            if (s is not null) stmts.Add(s);
        }
        return stmts;
    }

    private Stmt? UnsupportedStatement()
    {
        var t = Peek();
        ErrorAt(t, ErrExpectedStatement,
            $"unsupported or unrecognized statement (starts with {t.Kind})",
            "MAT, OPEN/CLOSE, WHEN/USE/HANDLER, MODULE, and graphics statements are not yet implemented");
        return null;
    }

    // -- Cursor primitives ------------------------------------------------

    private bool AtEnd() => _pos >= _tokens.Count || _tokens[_pos].Kind == TokenKind.EndOfFile;

    private Token Peek(int offset = 0) =>
        _pos + offset < _tokens.Count ? _tokens[_pos + offset] : _tokens[^1];

    private TokenKind PeekKind(int offset = 0) => Peek(offset).Kind;

    private Token Advance() => _tokens[_pos++];

    private Token Previous() => _tokens[_pos - 1];

    private bool Check(TokenKind kind) => !AtEnd() && Peek().Kind == kind;

    private bool Match(TokenKind kind)
    {
        if (!Check(kind)) return false;
        Advance();
        return true;
    }

    private bool ExpectKind(TokenKind kind, string what)
    {
        if (Match(kind)) return true;
        ErrorAt(Peek(), ErrExpectedToken, $"expected {what}");
        return false;
    }

    private bool AtStatementEnd() =>
        AtEnd() || Check(TokenKind.Newline) || Check(TokenKind.Colon) || Check(TokenKind.KwElse);

    private void SkipNewlines()
    {
        while (Check(TokenKind.Newline)) Advance();
    }

    private void SkipToNextStatement()
    {
        while (!AtEnd() && !Check(TokenKind.Newline) && !Check(TokenKind.Colon))
        {
            Advance();
        }
        // Consume the terminator so the next iteration starts fresh.
        if (Check(TokenKind.Newline) || Check(TokenKind.Colon))
        {
            Advance();
        }
    }

    private void ErrorAt(Token tok, string code, string message, string? hint = null) =>
        _diags.Error(code, tok.Span, message, hint);

    private SourceSpan SpanFrom(Token start, SourceSpan endInclusive) =>
        new(_file, start.Span.Start, Math.Max(endInclusive.End - start.Span.Start, start.Span.Length));

    private SourceSpan SpanFrom(Token start, Token endInclusive) =>
        new(_file, start.Span.Start, Math.Max(endInclusive.Span.End - start.Span.Start, start.Span.Length));

    private SourceSpan SpanBetween(SourceSpan a, SourceSpan b) =>
        new(_file, a.Start, b.End - a.Start);

    // -- Lexical helpers --------------------------------------------------

    private static string NameWithoutDollar(string raw) =>
        raw.EndsWith('$') ? raw[..^1] : raw;

    private static string UnescapeString(string raw)
    {
        // raw includes the surrounding quotes; strip them and resolve "" escapes.
        if (raw.Length < 2) return string.Empty;
        var inner = raw.AsSpan(1, raw.Length - 2);
        if (!inner.Contains('"')) return inner.ToString();
        return inner.ToString().Replace("\"\"", "\"", StringComparison.Ordinal);
    }

    private static BinaryOp? TryConvertToBinaryOp(Token tok) =>
        tok.Kind switch
        {
            TokenKind.Equal => BinaryOp.Equal,
            TokenKind.NotEqual => BinaryOp.NotEqual,
            TokenKind.Less => BinaryOp.Less,
            TokenKind.LessEqual => BinaryOp.LessEqual,
            TokenKind.Greater => BinaryOp.Greater,
            TokenKind.GreaterEqual => BinaryOp.GreaterEqual,
            _ => null,
        };
}
