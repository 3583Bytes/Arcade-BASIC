using System.Text;

namespace FullBasic.Core;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// A compile-time diagnostic. Stable code (e.g. "FB0001") plus a user-visible message,
/// pinned to a source span. Optional hint text may suggest a fix.
/// </summary>
public sealed record class Diagnostic(
    DiagnosticSeverity Severity,
    string Code,
    SourceSpan Span,
    string Message,
    string? Hint = null)
{
    /// <summary>
    /// Renders this diagnostic in a Rust-style snippet, with the offending
    /// source line and a caret underline. ANSI colors are applied if requested.
    /// </summary>
    public string Render(bool useColor)
    {
        var sb = new StringBuilder();
        var (line, col) = Span.StartPosition.LineCol;

        var (sevWord, sevColor) = Severity switch
        {
            DiagnosticSeverity.Error => ("error", Ansi.BrightRed),
            DiagnosticSeverity.Warning => ("warning", Ansi.BrightYellow),
            DiagnosticSeverity.Info => ("note", Ansi.BrightCyan),
            _ => ("diagnostic", ""),
        };

        // Header: "error[FB0001]: message"
        Color(sb, sevColor, useColor);
        sb.Append(sevWord);
        sb.Append('[').Append(Code).Append(']');
        Reset(sb, useColor);
        sb.Append(": ").AppendLine(Message);

        // Location: " --> path:line:col"
        Color(sb, Ansi.BrightBlue, useColor);
        sb.Append(" --> ");
        Reset(sb, useColor);
        sb.Append(Span.File.Path).Append(':').Append(line).Append(':').Append(col).AppendLine();

        // Source line + caret.
        var lineText = Span.File.GetLineText(line);
        var gutterWidth = line.ToString().Length;
        var gutterFill = new string(' ', gutterWidth);

        Color(sb, Ansi.BrightBlue, useColor);
        sb.Append(gutterFill).AppendLine(" |");
        sb.Append(line).Append(" | ");
        Reset(sb, useColor);
        sb.Append(lineText).AppendLine();

        Color(sb, Ansi.BrightBlue, useColor);
        sb.Append(gutterFill).Append(" | ");
        Reset(sb, useColor);
        sb.Append(new string(' ', col - 1));
        Color(sb, sevColor, useColor);
        var caretLen = Math.Max(1, Span.Length);
        sb.Append(new string('^', caretLen));
        Reset(sb, useColor);
        sb.AppendLine();

        if (Hint is not null)
        {
            Color(sb, Ansi.BrightBlue, useColor);
            sb.Append(gutterFill).Append(" = ");
            Reset(sb, useColor);
            Color(sb, Ansi.Bold, useColor);
            sb.Append("hint");
            Reset(sb, useColor);
            sb.Append(": ").AppendLine(Hint);
        }

        return sb.ToString();
    }

    private static void Color(StringBuilder sb, string code, bool useColor)
    {
        if (useColor && code.Length > 0)
        {
            sb.Append(code);
        }
    }

    private static void Reset(StringBuilder sb, bool useColor)
    {
        if (useColor)
        {
            sb.Append(Ansi.Reset);
        }
    }

    private static class Ansi
    {
        public const string Reset = "[0m";
        public const string Bold = "[1m";
        public const string BrightRed = "[1;31m";
        public const string BrightYellow = "[1;33m";
        public const string BrightBlue = "[1;34m";
        public const string BrightCyan = "[1;36m";
    }
}
