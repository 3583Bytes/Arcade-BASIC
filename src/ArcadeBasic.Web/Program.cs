using System.Runtime.InteropServices.JavaScript;
using ArcadeBasic.Compiler;
using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Runtime;
using ArcadeBasic.Sema;
using ArcadeBasic.Vm;
using ArcadeBasic.Web;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("browser")]

// The IDE's .NET side: compiling, checking and debugging programs (here and in the
// Playground.*.cs files); running them is Playground.Run.cs, which the player
// (src/ArcadeBasic.Player) shares.
Console.WriteLine("Arcade BASIC playground loaded");
Playground.UseFilesFolder();   // programs' files (Playground.Files.cs)

internal static partial class Playground
{
    private const string ExamplePrefix = "Examples.";


    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.String>>]
    internal static string[] Examples() =>
        [.. typeof(Playground).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ExamplePrefix, StringComparison.Ordinal))
            .Select(n => n[ExamplePrefix.Length..])
            .Order(StringComparer.Ordinal)];

    [JSExport]
    internal static string ExampleSource(string name)
    {
        using var stream = typeof(Playground).Assembly.GetManifestResourceStream(ExamplePrefix + name);
        if (stream is null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The program in <paramref name="source"/> (the file <paramref name="name"/>) with
    /// the libraries its LIBRARY statements name: the other open programs
    /// (<paramref name="names"/>, <paramref name="texts"/>), else the bundled examples.</summary>
    private static ArcadeBasic.Parser.Ast.Program LoadProgram(string name, string source, string[] names, string[] texts, DiagnosticBag diags) =>
        ProgramLoader.Load(new SourceFile(name, source), (library, _) =>
        {
            var i = Array.FindIndex(names, n => string.Equals(n, library, StringComparison.OrdinalIgnoreCase));
            var text = i >= 0 ? texts[i] : ExampleSource(library);
            return string.IsNullOrEmpty(text) && i < 0 ? null : new SourceFile(library, text);
        }, diags);

    /// <summary>Compile the program (with its libraries; see <see cref="LoadProgram"/>) and get
    /// it ready to step: "" on success, otherwise the diagnostics to show.</summary>
    [JSExport]
    internal static string Load(string name, string source,
        [JSMarshalAs<JSType.Array<JSType.String>>] string[] names, [JSMarshalAs<JSType.Array<JSType.String>>] string[] texts)
    {
        s_vm = null;
        var diags = new DiagnosticBag();
        var program = LoadProgram(name, source, names, texts, diags);
        var info = diags.HasErrors ? null : Analyzer.Analyze(program, diags);
        if (info is null || diags.HasErrors)
            return string.Join("\n", diags.All.Select(d => d.Render(useColor: false)));

        ArcadeBasic.Bytecode.Program compiled;
        try
        {
            compiled = BasicCompiler.Compile(program, info);
        }
        catch (BasicCompiler.UnsupportedFeatureException ex)
        {
            return ex.Message;
        }

        Begin(compiled);
        RememberScopes(compiled, info);
        return "";
    }

    private static string[] s_checkMessages = [];

    /// <summary>Check the program (with its libraries) without running it, for the editor's
    /// error marks: (from, to, severity) per problem, as character offsets, severity 0 a
    /// note, 1 a warning, 2 an error. <see cref="CheckMessages"/> has their texts. An error
    /// in a library is marked on the LIBRARY statement that names it.</summary>
    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    internal static double[] Check(string name, string source,
        [JSMarshalAs<JSType.Array<JSType.String>>] string[] names, [JSMarshalAs<JSType.Array<JSType.String>>] string[] texts)
    {
        var diags = new DiagnosticBag();
        var program = LoadProgram(name, source, names, texts, diags);
        var info = diags.HasErrors ? null : Analyzer.Analyze(program, diags);
        var problems = new List<double>();
        var messages = new List<string>();
        var main = program.Span.File;
        foreach (var d in diags.All)
        {
            var text = d.Hint is null ? $"{d.Message} [{d.Code}]" : $"{d.Message} [{d.Code}]\n{d.Hint}";
            if (d.Span.File == main)
            {
                problems.AddRange([d.Span.Start, d.Span.End, (int)d.Severity]);
                messages.Add(text);
            }
            else if (d.Severity == DiagnosticSeverity.Error)
            {
                // In a library: on the LIBRARY statement's name for it (its own tab shows the rest).
                var library = $"\"{d.Span.File.Path}\"";
                var at = source.IndexOf(library, StringComparison.OrdinalIgnoreCase);
                problems.AddRange([Math.Max(0, at), at < 0 ? 0 : at + library.Length, (int)d.Severity]);
                messages.Add($"In {d.Span.File.Path}, line {d.Span.StartPosition.LineCol.Line}: {text}");
            }
        }
        if (info is not null && !diags.HasErrors)
        {
            try
            {
                RememberStatementLines(BasicCompiler.Compile(program, info));
            }
            catch (BasicCompiler.UnsupportedFeatureException ex)
            {
                problems.AddRange([0, 0, (int)DiagnosticSeverity.Error]);
                messages.Add(ex.Message);
            }
        }
        s_checkMessages = [.. messages];
        return [.. problems];
    }

    /// <summary>The texts of the problems the last <see cref="Check"/> found.</summary>
    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.String>>]
    internal static string[] CheckMessages() => s_checkMessages;

    /// <summary>The words the editor colours and completes: the reserved words, then
    /// (after an empty string) the supplied functions and constants.</summary>
    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.String>>]
    internal static string[] Words() => [.. BasicLexer.ReservedWords, "", .. Analyzer.BuiltinNames];

}
