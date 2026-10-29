using ArcadeBasic.Bytecode;
using ArcadeBasic.Cli;

namespace ArcadeBasic.Conformance.Tests;

/// <summary>
/// `arcade-basic build --desktop`: the program and the desktop player made into an app —
/// for macOS an app bundle with the program in its Resources, for Windows and Linux the
/// player with the program appended. Stand-in players here: just the first bytes that say
/// which OS an executable is for.
/// </summary>
public sealed class DesktopAppTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("arcade-desktop-").FullName;
    private static readonly byte[] Program = [1, 2, 3, 4, 5];

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Player(params byte[] head)
    {
        var path = Path.Combine(_dir, "arcade-player");
        File.WriteAllBytes(path, [.. head, .. "the player's code"u8.ToArray()]);
        return path;
    }

    private string At(string name) => Path.Combine(_dir, name);

    [Fact]
    public void ForMacOSItIsAnAppWithTheProgramInItsResources()
    {
        var player = Player(0xCF, 0xFA, 0xED, 0xFE);                      // 64-bit Mach-O
        var app = Bundles.WriteDesktopApp(player, At("Maze & More.app"), "maze3d", Program);

        Assert.Equal(At("Maze & More.app"), app);
        Assert.Equal(File.ReadAllBytes(player), File.ReadAllBytes(Path.Combine(app, "Contents", "MacOS", "Maze & More")));
        Assert.Equal(Program, File.ReadAllBytes(Path.Combine(app, "Contents", "Resources", "game.bin")));
        var plist = File.ReadAllText(Path.Combine(app, "Contents", "Info.plist"));
        Assert.Contains("<key>CFBundleExecutable</key><string>Maze &amp; More</string>", plist);
        Assert.Contains("<string>io.github.arcadebasic.game.maze---more</string>", plist);
        if (!OperatingSystem.IsWindows())
            Assert.True(File.GetUnixFileMode(Path.Combine(app, "Contents", "MacOS", "Maze & More")).HasFlag(UnixFileMode.UserExecute));
    }

    [Fact]
    public void BuildingAgainReplacesTheAppButNothingElse()
    {
        var player = Player(0xCF, 0xFA, 0xED, 0xFE);
        Bundles.WriteDesktopApp(player, At("game.app"), "game", Program);
        Bundles.WriteDesktopApp(player, At("game.app"), "game", [9, 9]);
        Assert.Equal([9, 9], File.ReadAllBytes(At("game.app/Contents/Resources/game.bin")));

        Directory.CreateDirectory(At("photos.app"));                       // not an app bundle
        Assert.Throws<InvalidOperationException>(() => Bundles.WriteDesktopApp(player, At("photos.app"), "game", Program));
        Assert.True(Directory.Exists(At("photos.app")));
    }

    [Theory]
    [InlineData(new byte[] { (byte)'M', (byte)'Z', 0x90, 0 }, "game.exe")]   // Windows
    [InlineData(new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F' }, "game")]  // Linux
    public void ForWindowsAndLinuxItIsThePlayerWithTheProgramAppended(byte[] head, string expected)
    {
        var player = Player(head);
        var output = Path.Combine(_dir, "out");
        Directory.CreateDirectory(output);
        var cwd = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = output;                         // (the default name, beside us)
            Assert.Equal(expected, Bundles.WriteDesktopApp(player, null, "game", Program));
        }
        finally
        {
            Environment.CurrentDirectory = cwd;
        }
        var written = Path.Combine(output, expected);
        Assert.Equal(Program, EmbeddedPayload.TryRead(written));
        Assert.Equal(File.ReadAllBytes(player), File.ReadAllBytes(written)[..(int)new FileInfo(player).Length]);
    }

    [Fact]
    public void SomethingElseIsntAPlayer()
    {
        var notAPlayer = Player((byte)'#', (byte)'!', (byte)'/', (byte)'b');
        Assert.Equal(Bundles.Os.Unknown, Bundles.OsOf(notAPlayer));
        Assert.Throws<InvalidOperationException>(() => Bundles.WriteDesktopApp(notAPlayer, At("x.app"), "x", Program));
    }
}
