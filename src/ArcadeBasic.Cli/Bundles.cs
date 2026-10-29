using ArcadeBasic.Bytecode;

namespace ArcadeBasic.Cli;

/// <summary>
/// What <c>arcade-basic build</c> writes: a compiled program attached to something that runs
/// it — the CLI itself (a terminal program, with the program appended), or with
/// <c>--desktop</c> the desktop player (desktop/player: the program in a window). For macOS
/// the desktop app is an app bundle with the program in its Resources; for Windows and Linux
/// it's the player with the program appended, as the CLI's are.
/// </summary>
public static class Bundles
{
    public enum Os { Unknown, MacOS, Windows, Linux }

    /// <summary>Which OS an executable is for, by its first bytes: Mach-O, PE or ELF.</summary>
    public static Os OsOf(string executable)
    {
        var head = new byte[4];
        using (var fs = File.OpenRead(executable))
        {
            if (fs.Read(head, 0, 4) < 4) return Os.Unknown;
        }
        var magic = BitConverter.ToUInt32(head, 0);
        if (magic is 0xFEEDFACF or 0xFEEDFACE or 0xBEBAFECA) return Os.MacOS;   // 64- and 32-bit, universal
        if (head[0] == 'M' && head[1] == 'Z') return Os.Windows;
        if (head[0] == 0x7F && head[1] == 'E' && head[2] == 'L' && head[3] == 'F') return Os.Linux;
        return Os.Unknown;
    }

    /// <summary>The executable <paramref name="stub"/> copied to <paramref name="output"/> —
    /// without any program it already had — then <paramref name="program"/> appended
    /// ([program][u32 length]["FB-BCEND"]), made executable.</summary>
    public static void WriteWithProgram(string stub, string output, byte[] program)
    {
        var stubBytes = File.ReadAllBytes(stub);

        // If our stub already has an embedded payload (e.g. user re-bundling),
        // strip it so we don't grow the binary on each rebuild.
        var existing = EmbeddedPayload.TryRead(stub);
        if (existing is not null)
        {
            var strip = existing.Length + 12;
            Array.Resize(ref stubBytes, stubBytes.Length - strip);
        }

        using (var fs = File.Create(output))
        {
            fs.Write(stubBytes, 0, stubBytes.Length);
            EmbeddedPayload.Append(fs, program);
        }
        MakeExecutable(output);
    }

    /// <summary>The desktop app for <paramref name="program"/> (named <paramref name="name"/>),
    /// from the desktop player <paramref name="player"/>: written to <paramref name="output"/>,
    /// else name.app, name.exe or name by the player's OS. Returns where it went. Throws
    /// <see cref="InvalidOperationException"/> if the player isn't one, or the output is in
    /// the way.</summary>
    public static string WriteDesktopApp(string player, string? output, string name, byte[] program)
    {
        switch (OsOf(player))
        {
            case Os.MacOS:
                output ??= name + ".app";
                if (Directory.Exists(output))
                {
                    if (!File.Exists(Path.Combine(output, "Contents", "Info.plist")))
                        throw new InvalidOperationException($"{output} is already there, and isn't an app to replace");
                    Directory.Delete(output, recursive: true);
                }
                var appName = Path.GetFileNameWithoutExtension(output);
                var contents = Path.Combine(output, "Contents");
                Directory.CreateDirectory(Path.Combine(contents, "MacOS"));
                Directory.CreateDirectory(Path.Combine(contents, "Resources"));
                var executable = Path.Combine(contents, "MacOS", appName);
                File.Copy(player, executable);
                MakeExecutable(executable);
                File.WriteAllBytes(Path.Combine(contents, "Resources", "game.bin"), program);
                File.WriteAllText(Path.Combine(contents, "Info.plist"), InfoPlist(appName));
                return output;
            case Os.Windows:
                output ??= name + ".exe";
                WriteWithProgram(player, output, program);
                return output;
            case Os.Linux:
                output ??= name;
                WriteWithProgram(player, output, program);
                return output;
            default:
                throw new InvalidOperationException($"{player} isn't a desktop player for macOS, Windows or Linux");
        }
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch { /* best effort — chmod failure is non-fatal */ }
    }

    /// <summary>The Info.plist of a game's app bundle: desktop/player/app-Info.plist (built
    /// in), which the IDE's File → Export fills in too.</summary>
    private static string InfoPlist(string name)
    {
        using var stream = typeof(Bundles).Assembly.GetManifestResourceStream("app-Info.plist")
            ?? throw new InvalidOperationException("app-Info.plist isn't built in");
        var template = new StreamReader(stream).ReadToEnd();
        var id = new string(name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());
        return template.Replace("__NAME__", System.Security.SecurityElement.Escape(name)).Replace("__ID__", id);
    }
}
