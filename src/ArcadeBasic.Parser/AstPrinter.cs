using System.Text;
using ArcadeBasic.Parser.Ast;

namespace ArcadeBasic.Parser;

/// <summary>
/// Pretty-prints an AST as an indented tree. Used by the CLI's `parse` command
/// for smoke-testing and for AST snapshot tests.
/// </summary>
public static class AstPrinter
{
    public static string Print(Program program)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Program");
        foreach (var stmt in program.Statements)
        {
            PrintStmt(sb, stmt, 1);
        }
        return sb.ToString();
    }

    private static void PrintStmt(StringBuilder sb, Stmt stmt, int depth)
    {
        Indent(sb, depth);
        var label = stmt.Label is { } l ? $"[{l}] " : "";

        switch (stmt)
        {
            case AssignStmt s:
                sb.AppendLine($"{label}Assign{(s.ExplicitLet ? " (LET)" : "")}");
                PrintExpr(sb, s.Target, depth + 1, "target");
                PrintExpr(sb, s.Value, depth + 1, "value");
                break;
            case PrintStmt s:
                sb.AppendLine($"{label}Print  ({s.Items.Count} items)");
                foreach (var it in s.Items)
                {
                    PrintItem(sb, it, depth + 1);
                }
                break;
            case InputStmt s:
                sb.AppendLine($"{label}Input  ({s.Targets.Count} targets)");
                if (s.Prompt is not null) PrintExpr(sb, s.Prompt, depth + 1, "prompt");
                foreach (var t in s.Targets) PrintExpr(sb, t, depth + 1, "target");
                break;
            case LineInputStmt s:
                sb.AppendLine($"{label}LineInput");
                if (s.Prompt is not null) PrintExpr(sb, s.Prompt, depth + 1, "prompt");
                PrintExpr(sb, s.Target, depth + 1, "target");
                break;
            case ReadStmt s:
                sb.AppendLine($"{label}Read");
                foreach (var t in s.Targets) PrintExpr(sb, t, depth + 1, "target");
                break;
            case DataStmt s:
                sb.AppendLine($"{label}Data");
                foreach (var d in s.Items)
                {
                    Indent(sb, depth + 1);
                    sb.AppendLine($"{(d.IsString ? "string" : "number")}: {d.Text}");
                }
                break;
            case RestoreStmt s:
                sb.AppendLine($"{label}Restore");
                if (s.LabelTarget is not null) PrintExpr(sb, s.LabelTarget, depth + 1, "label");
                break;
            case GotoStmt s:
                sb.AppendLine($"{label}Goto");
                PrintExpr(sb, s.LabelTarget, depth + 1, "to");
                break;
            case GosubStmt s:
                sb.AppendLine($"{label}Gosub");
                PrintExpr(sb, s.LabelTarget, depth + 1, "to");
                break;
            case ReturnStmt: sb.AppendLine($"{label}Return"); break;
            case StopStmt: sb.AppendLine($"{label}Stop"); break;
            case EndStmt: sb.AppendLine($"{label}End"); break;
            case EndBlockStmt s: sb.AppendLine($"{label}EndBlock {s.Kind}"); break;
            case RunStmt: sb.AppendLine($"{label}Run"); break;
            case RandomizeStmt s:
                sb.AppendLine($"{label}Randomize");
                if (s.Seed is not null) PrintExpr(sb, s.Seed, depth + 1, "seed");
                break;
            case SleepStmt s:
                sb.AppendLine($"{label}Sleep");
                PrintExpr(sb, s.Seconds, depth + 1, "seconds");
                break;
            case SoundStmt s:
                sb.AppendLine($"{label}Sound");
                PrintExpr(sb, s.Frequency, depth + 1, "frequency");
                PrintExpr(sb, s.Duration, depth + 1, "duration");
                break;
            case BeepStmt: sb.AppendLine($"{label}Beep"); break;
            case PlayStmt s:
                sb.AppendLine($"{label}Play");
                PrintExpr(sb, s.Notes, depth + 1, "notes");
                break;
            case RemStmt s: sb.AppendLine($"{label}Rem  \"{s.Comment}\""); break;
            case DimStmt s:
                sb.AppendLine($"{label}Dim");
                foreach (var spec in s.Specs)
                {
                    Indent(sb, depth + 1);
                    sb.AppendLine($"{spec.Name}{(spec.IsString ? "$" : "")}  ({spec.Bounds.Count} dim)");
                    foreach (var b in spec.Bounds)
                    {
                        if (b.Lower is not null) PrintExpr(sb, b.Lower, depth + 2, "lower");
                        PrintExpr(sb, b.Upper, depth + 2, "upper");
                    }
                }
                break;
            case OptionBaseStmt s: sb.AppendLine($"{label}OptionBase {s.Base}"); break;
            case OptionArithmeticStmt s: sb.AppendLine($"{label}OptionArithmetic {s.Mode}"); break;
            case IfStmt s:
                sb.AppendLine($"{label}If");
                PrintExpr(sb, s.Condition, depth + 1, "cond");
                Indent(sb, depth + 1); sb.AppendLine("then:");
                foreach (var t in s.ThenBlock) PrintStmt(sb, t, depth + 2);
                foreach (var ei in s.ElseIfs)
                {
                    Indent(sb, depth + 1); sb.AppendLine("elseif:");
                    PrintExpr(sb, ei.Condition, depth + 2, "cond");
                    foreach (var t in ei.Body) PrintStmt(sb, t, depth + 2);
                }
                if (s.ElseBlock is not null)
                {
                    Indent(sb, depth + 1); sb.AppendLine("else:");
                    foreach (var t in s.ElseBlock) PrintStmt(sb, t, depth + 2);
                }
                break;
            case ForStmt s:
                sb.AppendLine($"{label}For {s.Variable.Name}");
                PrintExpr(sb, s.From, depth + 1, "from");
                PrintExpr(sb, s.To, depth + 1, "to");
                if (s.Step is not null) PrintExpr(sb, s.Step, depth + 1, "step");
                foreach (var t in s.Body) PrintStmt(sb, t, depth + 1);
                break;
            case NextStmt s: sb.AppendLine($"{label}Next {s.Variable?.Name ?? "(implicit)"}"); break;
            case DoStmt s:
                sb.AppendLine($"{label}Do");
                if (s.Pre is not null)
                {
                    Indent(sb, depth + 1); sb.AppendLine($"pre {(s.Pre.IsUntil ? "UNTIL" : "WHILE")}:");
                    PrintExpr(sb, s.Pre.Condition, depth + 2, "cond");
                }
                foreach (var t in s.Body) PrintStmt(sb, t, depth + 1);
                if (s.Post is not null)
                {
                    Indent(sb, depth + 1); sb.AppendLine($"post {(s.Post.IsUntil ? "UNTIL" : "WHILE")}:");
                    PrintExpr(sb, s.Post.Condition, depth + 2, "cond");
                }
                break;
            case LoopStmt: sb.AppendLine($"{label}Loop"); break;
            case SelectStmt s:
                sb.AppendLine($"{label}Select");
                PrintExpr(sb, s.Subject, depth + 1, "subject");
                foreach (var c in s.Cases)
                {
                    Indent(sb, depth + 1); sb.AppendLine("case:");
                    foreach (var v in c.Values) PrintCaseSpec(sb, v, depth + 2);
                    foreach (var t in c.Body) PrintStmt(sb, t, depth + 2);
                }
                if (s.CaseElse is not null)
                {
                    Indent(sb, depth + 1); sb.AppendLine("case else:");
                    foreach (var t in s.CaseElse) PrintStmt(sb, t, depth + 2);
                }
                break;
            case ExitStmt s: sb.AppendLine($"{label}Exit {s.Target}"); break;
            case DefStmt s:
                sb.AppendLine($"{label}Def {s.Name}{(s.IsString ? "$" : "")}  ({s.Params.Count} params)");
                if (s.SingleLineBody is not null) PrintExpr(sb, s.SingleLineBody, depth + 1, "body");
                if (s.MultiLineBody is not null) foreach (var t in s.MultiLineBody) PrintStmt(sb, t, depth + 1);
                break;
            case SubStmt s:
                sb.AppendLine($"{label}Sub {s.Name}  ({s.Params.Count} params)");
                foreach (var t in s.Body) PrintStmt(sb, t, depth + 1);
                break;
            case FunctionStmt s:
                sb.AppendLine($"{label}Function {s.Name}{(s.IsString ? "$" : "")}  ({s.Params.Count} params)");
                foreach (var t in s.Body) PrintStmt(sb, t, depth + 1);
                break;
            case CallStmt s:
                sb.AppendLine($"{label}Call {s.Name}");
                foreach (var a in s.Args) PrintExpr(sb, a, depth + 1, "arg");
                break;
            default:
                sb.AppendLine($"{label}{stmt.GetType().Name}");
                break;
        }
    }

    private static void PrintCaseSpec(StringBuilder sb, CaseSpec spec, int depth)
    {
        switch (spec)
        {
            case CaseValue v:
                PrintExpr(sb, v.Value, depth, "value");
                break;
            case CaseRange r:
                Indent(sb, depth); sb.AppendLine("range:");
                PrintExpr(sb, r.Lo, depth + 1, "lo");
                PrintExpr(sb, r.Hi, depth + 1, "hi");
                break;
            case CaseIs i:
                Indent(sb, depth); sb.AppendLine($"is {i.Op}:");
                PrintExpr(sb, i.Value, depth + 1, "value");
                break;
        }
    }

    private static void PrintItem(StringBuilder sb, PrintItem item, int depth)
    {
        switch (item)
        {
            case PrintExprItem e: PrintExpr(sb, e.Value, depth, "expr"); break;
            case PrintTab t: PrintExpr(sb, t.Column, depth, "tab"); break;
            case PrintComma: Indent(sb, depth); sb.AppendLine(","); break;
            case PrintSemicolon: Indent(sb, depth); sb.AppendLine(";"); break;
        }
    }

    private static void PrintExpr(StringBuilder sb, Expr expr, int depth, string label)
    {
        Indent(sb, depth);
        sb.Append(label).Append(": ");
        switch (expr)
        {
            case NumberExpr n: sb.AppendLine($"Number {n.Text}"); break;
            case StringExpr s: sb.AppendLine($"String \"{s.Value}\""); break;
            case NameRefExpr n: sb.AppendLine($"Name {n.Name}{(n.IsString ? "$" : "")}"); break;
            case CallOrIndexExpr c:
                sb.AppendLine($"CallOrIndex {c.Name}{(c.IsString ? "$" : "")}  ({c.Args.Count} args)");
                foreach (var a in c.Args) PrintExpr(sb, a, depth + 1, "arg");
                break;
            case ParenExpr p:
                sb.AppendLine("Paren");
                PrintExpr(sb, p.Inner, depth + 1, "inner");
                break;
            case UnaryExpr u:
                sb.AppendLine($"Unary {u.Op}");
                PrintExpr(sb, u.Operand, depth + 1, "operand");
                break;
            case BinaryExpr b:
                sb.AppendLine($"Binary {b.Op}");
                PrintExpr(sb, b.Left, depth + 1, "left");
                PrintExpr(sb, b.Right, depth + 1, "right");
                break;
            default:
                sb.AppendLine(expr.GetType().Name);
                break;
        }
    }

    private static void Indent(StringBuilder sb, int depth) =>
        sb.Append(' ', depth * 2);
}
