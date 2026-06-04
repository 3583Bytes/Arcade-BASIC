using System.Reflection;

namespace ArcadeBasic.Ide;

/// <summary>
/// Surfaces the .bas files embedded into the IDE assembly at build time.
/// Files come from /examples in the repo; see the EmbeddedResource glob in
/// ArcadeBasic.Ide.csproj.
/// </summary>
internal static class ExamplesProvider
{
    private const string Prefix = "ArcadeBasic.Ide.Examples.";

    public static IReadOnlyList<Example> All { get; } = Load();

    public sealed record Example(string Name, string Source, string Category);

    private static IReadOnlyList<Example> Load()
    {
        var asm = typeof(ExamplesProvider).Assembly;
        var results = new List<Example>();
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            if (!name.EndsWith(".bas", StringComparison.OrdinalIgnoreCase)) continue;

            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) continue;
            using var reader = new StreamReader(stream);
            var source = reader.ReadToEnd();

            var display = name.Substring(Prefix.Length);
            display = display.Substring(0, display.Length - ".bas".Length);
            results.Add(new Example(display, source, ParseCategory(source)));
        }
        return results.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Reads the example's group from a <c>@category &lt;Word&gt;</c> tag in
    /// a leading comment (any comment style). Defaults to <c>Basics</c> when absent.</summary>
    private static string ParseCategory(string source)
    {
        const string Tag = "@category";
        int i = source.IndexOf(Tag, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return "Basics";
        i += Tag.Length;
        while (i < source.Length && (source[i] == ' ' || source[i] == '\t')) i++;
        int start = i;
        while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '&')) i++;
        return i > start ? source.Substring(start, i - start) : "Basics";
    }
}
