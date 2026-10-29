using System.Runtime.InteropServices.JavaScript;

// The files programs read and write (OPEN #1: NAME "todo.board", …). They live in the
// page's memory, which a reload wipes, so the page keeps a copy in the browser: it puts
// them back here when it starts (PutFiles), and collects what programs change as they
// run (ChangedFiles). Programs see them in their own folder, the working directory.
internal static partial class Playground
{
    /// <summary>The programs' folder (and working directory).</summary>
    internal const string FilesFolder = "/files";

    /// <summary>When each file was last seen, and its length — to tell what's changed.</summary>
    private static readonly Dictionary<string, (DateTime Written, long Length)> s_seen = new(StringComparer.Ordinal);

    /// <summary>Make the programs' folder the working directory (once, at start-up).</summary>
    internal static void UseFilesFolder()
    {
        Directory.CreateDirectory(FilesFolder);
        Directory.SetCurrentDirectory(FilesFolder);
    }

    /// <summary>Put files back into the programs' folder: <paramref name="names"/> (relative
    /// paths, "/" between folders) and their <paramref name="texts"/>.</summary>
    [JSExport]
    internal static void PutFiles(string[] names, string[] texts)
    {
        UseFilesFolder();
        for (var i = 0; i < Math.Min(names.Length, texts.Length); i++)
        {
            if (FullPath(names[i]) is not { } path) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, texts[i]);
            s_seen[names[i]] = Stamp(path);
        }
    }

    /// <summary>The files written since the last call, as name, text, name, text, …</summary>
    [JSExport]
    internal static string[] ChangedFiles()
    {
        var changed = new List<string>();
        if (!Directory.Exists(FilesFolder)) return [];
        foreach (var path in Directory.EnumerateFiles(FilesFolder, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetRelativePath(FilesFolder, path).Replace('\\', '/');
            var stamp = Stamp(path);
            if (s_seen.TryGetValue(name, out var seen) && seen == stamp) continue;
            try
            {
                changed.Add(name);
                changed.Add(File.ReadAllText(path));
                s_seen[name] = stamp;
            }
            catch (IOException)
            {
                changed.RemoveAt(changed.Count - 1);   // still open for writing: next time
            }
        }
        return [.. changed];
    }

    /// <summary>Remove a file from the programs' folder.</summary>
    [JSExport]
    internal static void DeleteFile(string name)
    {
        if (FullPath(name) is { } path && File.Exists(path)) File.Delete(path);
        s_seen.Remove(name);
    }

    private static (DateTime, long) Stamp(string path)
    {
        var info = new FileInfo(path);
        return (info.LastWriteTimeUtc, info.Length);
    }

    /// <summary>Where <paramref name="name"/> is, if it's inside the programs' folder.</summary>
    private static string? FullPath(string name)
    {
        var path = Path.GetFullPath(Path.Combine(FilesFolder, name));
        return path.StartsWith(FilesFolder + "/", StringComparison.Ordinal) ? path : null;
    }
}
