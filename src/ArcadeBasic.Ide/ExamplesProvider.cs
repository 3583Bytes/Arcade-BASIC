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

    public sealed record Example(string Name, string Source);

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
            results.Add(new Example(display, source));
        }
        return results.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
