using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using ArcadeBasic.Bytecode;
using ArcadeBasic.Runtime;
using ArcadeBasic.Sema;
using ArcadeBasic.Vm;

// The debugger's side of the page: breakpoints, pausing and stepping, and the
// variables by name while the program is stopped (or, refreshed, while it runs).
internal static partial class Playground
{
    /// <summary>Which analyzer scope names each chunk's slots: the main program's, a SUB's, …</summary>
    private static Dictionary<Chunk, Scope> s_scopes = new(ReferenceEqualityComparer.Instance);

    /// <summary>The lines with statements, from the last <see cref="Check"/> that compiled.</summary>
    private static int[] s_statementLines = [];

    private const int MaxItems = 4096;        // array elements in a snapshot
    private const int MaxText = 200;          // characters of a string

    private static void RememberScopes(ArcadeBasic.Bytecode.Program compiled, SemanticInfo info)
    {
        s_scopes = new(ReferenceEqualityComparer.Instance) { [compiled.Main] = info.ProgramScope };
        var declared = info.ProgramScope.Symbols.Values.Concat(info.ModuleScopes.Values.SelectMany(s => s.Symbols.Values)).ToList();
        Scope? BodyOf<T>(string name, bool isString) where T : Symbol =>
            declared.OfType<T>().FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase) && s.IsString == isString) switch
            {
                SubSymbol s => s.BodyScope,
                FunctionSymbol f => f.BodyScope,
                DefSymbol d => d.BodyScope,
                _ => null,
            };
        foreach (var s in compiled.Subs) if (BodyOf<SubSymbol>(s.Name, false) is { } scope) s_scopes[s.Body] = scope;
        foreach (var f in compiled.Functions) if (BodyOf<FunctionSymbol>(f.Name, f.IsString) is { } scope) s_scopes[f.Body] = scope;
        foreach (var d in compiled.Defs) if (BodyOf<DefSymbol>(d.Name, d.IsString) is { } scope) s_scopes[d.Body] = scope;
    }

    private static void RememberStatementLines(ArcadeBasic.Bytecode.Program compiled) =>
        s_statementLines = [.. new[] { compiled.Main }
            .Concat(compiled.Subs.Select(s => s.Body)).Concat(compiled.Functions.Select(f => f.Body)).Concat(compiled.Defs.Select(d => d.Body))
            .SelectMany(c => c.StatementLines).Where(l => l.File == 0).Select(l => l.Line).Distinct().Order()];

    /// <summary>The running program's source files, the main program's first (file numbers).</summary>
    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.String>>]
    internal static string[] SourceFiles() => [.. s_vm?.SourceFiles ?? []];

    /// <summary>Which of <see cref="SourceFiles"/> the program is in (with <see cref="CurrentLine"/>).</summary>
    [JSExport]
    internal static int CurrentFile() => s_vm?.CurrentFile ?? 0;

    /// <summary>The lines a breakpoint can stop at (the last checked program's statements).</summary>
    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    internal static int[] StatementLines() => s_statementLines;

    /// <summary>The breakpoints: (file number, line) pairs.</summary>
    [JSExport]
    internal static void SetBreakpoints([JSMarshalAs<JSType.Array<JSType.Number>>] int[] files, [JSMarshalAs<JSType.Array<JSType.Number>>] int[] lines)
    {
        if (s_vm is null) return;
        s_vm.Breakpoints.Clear();
        for (var i = 0; i < Math.Min(files.Length, lines.Length); i++) s_vm.Breakpoints.Add(new SourceLine(files[i], lines[i]));
    }

    /// <summary>Stop again soon: a <see cref="DebugStep"/> (0 pause, 1 into, 2 over, 3 out).</summary>
    [JSExport]
    internal static void PauseAt(int step) => s_vm?.PauseAt((DebugStep)step);

    /// <summary>The calls in progress and their variables, as JSON:
    /// <c>{"frames":[{"name","line","vars":[…]}]}</c>, innermost first. A scalar is
    /// <c>{"name","value"}</c> (a string's value quoted), plus <c>"unset":true</c> before
    /// it's assigned; an array is <c>{"name","dims":[[lo,hi],…],"items":[…],"total"}</c>,
    /// elements in row-major order, or <c>"dims":null</c> before its DIM runs.</summary>
    [JSExport]
    internal static string Variables()
    {
        var json = new StringBuilder("{\"frames\":[");
        if (s_vm is not null)
        {
            var first = true;
            foreach (var frame in s_vm.CallStack())
            {
                if (!first) json.Append(',');
                first = false;
                json.Append("{\"name\":");
                Quote(json, frame.Name);
                json.Append(",\"file\":").Append(frame.File).Append(",\"line\":").Append(frame.Line).Append(",\"vars\":[");
                if (s_scopes.TryGetValue(frame.Chunk, out var scope)) Variables(json, scope, frame.Variables);
                json.Append("]}");
            }
        }
        return json.Append("]}").ToString();
    }

    private static void Variables(StringBuilder json, Scope scope, ActivationRecord slots)
    {
        var first = true;
        foreach (var symbol in scope.Symbols.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            var (slot, isArray) = symbol switch
            {
                VariableSymbol v => (v.Slot, false),
                ArraySymbol a => (a.Slot, true),
                ParamSymbol p => (p.Slot, p.IsArray),
                _ => (-1, false),
            };
            if (slot < 0 || slot >= slots.Slots.Length) continue;
            if (!first) json.Append(',');
            first = false;
            json.Append("{\"name\":");
            Quote(json, symbol.IsString ? symbol.Name + "$" : symbol.Name);
            var value = slots.IsSet(slot) ? slots.Get(slot) : null;
            if (isArray) ArrayJson(json, value);
            else
            {
                json.Append(",\"value\":");
                Quote(json, value is null ? (symbol.IsString ? "\"\"" : "0") : Show(value));
                if (value is null) json.Append(",\"unset\":true");
            }
            json.Append('}');
        }
    }

    private static void ArrayJson(StringBuilder json, Value? value)
    {
        var bounds = value switch { NumericArrayValue n => n.Bounds, StringArrayValue s => s.Bounds, _ => null };
        if (bounds is null)
        {
            json.Append(",\"dims\":null");
            return;
        }
        json.Append(",\"dims\":[");
        for (var d = 0; d < bounds.Rank; d++)
            json.Append(d == 0 ? "" : ",").Append('[').Append(bounds.Lower[d]).Append(',').Append(bounds.Upper[d]).Append(']');
        json.Append("],\"total\":").Append(bounds.Length).Append(",\"items\":[");
        var count = Math.Min(bounds.Length, MaxItems);
        for (var i = 0; i < count; i++)
        {
            if (i > 0) json.Append(',');
            Quote(json, value switch
            {
                NumericArrayValue n => Number(n[i]),
                StringArrayValue s => Text(s.Data[i]),
                _ => "",
            });
        }
        json.Append(']');
    }

    /// <summary>A value as BASIC would show it: a number as PRINT does (no padding), a string quoted.</summary>
    private static string Show(Value value) => value switch
    {
        NumericValue n => Number(n),
        StringValue s => Text(s.V),
        _ => value.ToString() ?? "",
    };

    private static string Number(NumericValue n) => DisplayFormat.FormatNumeric(n.V).Trim();

    private static string Text(string s) =>
        "\"" + (s.Length > MaxText ? s[..MaxText] + "…" : s) + "\"";

    private static void Quote(StringBuilder json, string s)
    {
        json.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': json.Append("\\\""); break;
                case '\\': json.Append("\\\\"); break;
                case '\n': json.Append("\\n"); break;
                case '\r': json.Append("\\r"); break;
                case '\t': json.Append("\\t"); break;
                default:
                    if (c < ' ') json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else json.Append(c);
                    break;
            }
        }
        json.Append('"');
    }
}
