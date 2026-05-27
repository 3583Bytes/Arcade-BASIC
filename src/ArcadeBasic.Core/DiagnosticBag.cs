namespace FullBasic.Core;

/// <summary>Accumulator for diagnostics during compile-time passes.</summary>
public sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _diagnostics = [];

    public IReadOnlyList<Diagnostic> All => _diagnostics;

    public bool HasErrors => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public int ErrorCount => _diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);

    public int WarningCount => _diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

    public void Add(Diagnostic diagnostic) => _diagnostics.Add(diagnostic);

    public void Error(string code, SourceSpan span, string message, string? hint = null) =>
        Add(new Diagnostic(DiagnosticSeverity.Error, code, span, message, hint));

    public void Warning(string code, SourceSpan span, string message, string? hint = null) =>
        Add(new Diagnostic(DiagnosticSeverity.Warning, code, span, message, hint));

    public void Info(string code, SourceSpan span, string message, string? hint = null) =>
        Add(new Diagnostic(DiagnosticSeverity.Info, code, span, message, hint));
}
