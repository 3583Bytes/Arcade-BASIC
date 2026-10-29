using ArcadeBasic.Bytecode;

namespace ArcadeBasic.Cli;

/// <summary>
/// What <c>arcade-basic build</c> writes: a compiled program attached to something that runs
/// it — the CLI itself (a terminal program, with the program appended), or with
/// <c>--desktop</c> the desktop player (desktop/player: the program in a window). For macOS
/// the desktop app is an app bundle with the program in its Resources; for Windows and Linux
/// it's the player with the program appended, as the CLI's are. A desktop app's icon is
/// Arcade BASIC's, or the one given (<see cref="AppIcons"/>).
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

    private const string IconMagic = "FB-ICEND";

    /// <summary>The executable <paramref name="stub"/> copied to <paramref name="output"/> —
    /// without any program it already had — then <paramref name="program"/> appended
    /// ([program][u32 length]["FB-BCEND"]), made executable.</summary>
    public static void WriteWithProgram(string stub, string output, byte[] program) =>
        WriteWithProgram(File.ReadAllBytes(stub), output, program);

    /// <summary>The executable <paramref name="stubBytes"/> written to <paramref name="output"/>
    /// — without any program (and icon) it already had — then <paramref name="program"/>
    /// appended ([program][u32 length]["FB-BCEND"]), and before it <paramref name="icon"/> if
    /// there's one ([PNG][u32 length]["FB-ICEND"], a Linux app's), made executable.</summary>
    public static void WriteWithProgram(byte[] stubBytes, string output, byte[] program, byte[]? icon = null)
    {
        // If our stub already has an embedded payload (e.g. user re-bundling),
        // strip it so we don't grow the binary on each rebuild.
        var end = stubBytes.Length;
        if (AppendedLength(stubBytes, end, EmbeddedPayload.TrailerMagic) is int programLength)
        {
            end -= programLength + 12;
            if (AppendedLength(stubBytes, end, IconMagic) is int iconLength) end -= iconLength + 12;
        }

        // Written beside the output and then moved over it, so a rebuild is a new file:
        // macOS remembers an executable's signature by its file, and kills one whose
        // code changed in place (rebuilt by a newer arcade-basic) when it starts.
        var temp = output + ".building";
        try
        {
            using (var fs = File.Create(temp))
            {
                fs.Write(stubBytes, 0, end);
                if (icon is not null)
                {
                    fs.Write(icon);
                    Span<byte> length = stackalloc byte[4];
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)icon.Length);
                    fs.Write(length);
                    fs.Write(System.Text.Encoding.ASCII.GetBytes(IconMagic));
                }
                EmbeddedPayload.Append(fs, program);
            }
            MakeExecutable(temp);
            File.Move(temp, output, overwrite: true);
        }
        finally
        {
            File.Delete(temp);      // (only there if something went wrong)
        }
    }

    /// <summary>The length of what's appended to <paramref name="bytes"/> before
    /// <paramref name="end"/> with <paramref name="magic"/> after its length, or null.</summary>
    private static int? AppendedLength(byte[] bytes, int end, string magic)
    {
        if (end < 12 || System.Text.Encoding.ASCII.GetString(bytes, end - 8, 8) != magic) return null;
        var length = BitConverter.ToUInt32(bytes, end - 12);
        return length > 0 && length <= end - 12 ? (int)length : null;
    }

    /// <summary>The desktop app for <paramref name="program"/> (named <paramref name="name"/>),
    /// from the desktop player <paramref name="player"/>: written to <paramref name="output"/>,
    /// else name.app, name.exe or name by the player's OS, with the icon in the file
    /// <paramref name="icon"/> if one's given. Returns where it went. Throws
    /// <see cref="InvalidOperationException"/> if the player isn't one, the icon isn't one
    /// for its OS, or the output is in the way.</summary>
    public static string WriteDesktopApp(string player, string? output, string name, byte[] program, string? icon = null)
    {
        switch (OsOf(player))
        {
            case Os.MacOS:
                var icns = icon is null ? Resource("icon.icns") : AppIcons.ForMac(icon);
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
                File.WriteAllBytes(Path.Combine(contents, "Resources", "icon.icns"), icns);
                File.WriteAllText(Path.Combine(contents, "Info.plist"), InfoPlist(appName));
                return output;
            case Os.Windows:
                var exe = File.ReadAllBytes(player);
                if (icon is not null) exe = AppIcons.WithWindowsIcon(exe, AppIcons.ForWindows(icon));
                output ??= name + ".exe";
                WriteWithProgram(exe, output, program);
                return output;
            case Os.Linux:
                var png = icon is null ? null : AppIcons.ForLinux(icon);
                output ??= name;
                WriteWithProgram(File.ReadAllBytes(player), output, program, png);
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

    /// <summary>A file built into the CLI (see its .csproj).</summary>
    private static byte[] Resource(string name)
    {
        using var stream = typeof(Bundles).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"{name} isn't built in");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    /// <summary>The Info.plist of a game's app bundle: desktop/player/app-Info.plist (built
    /// in), which the IDE's File → Export fills in too.</summary>
    private static string InfoPlist(string name)
    {
        var template = System.Text.Encoding.UTF8.GetString(Resource("app-Info.plist"));
        var id = new string(name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());
        return template.Replace("__NAME__", System.Security.SecurityElement.Escape(name)).Replace("__ID__", id);
    }
}
