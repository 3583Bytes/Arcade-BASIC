using System.Buffers.Binary;
using System.Text;
using ArcadeBasic.Bytecode;
using ArcadeBasic.Cli;

namespace ArcadeBasic.Conformance.Tests;

/// <summary>
/// `arcade-basic build --desktop`: the program and the desktop player made into an app —
/// for macOS an app bundle with the program in its Resources, for Windows and Linux the
/// player with the program appended — each with an icon: Arcade BASIC's, or the one given.
/// Stand-in players here: just the first bytes that say which OS an executable is for, or
/// for a Windows player's icon, a Windows program with nothing but an icon.
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
    public void AMacAppHasArcadeBasicsIconOrOneGiven()
    {
        var player = Player(0xCF, 0xFA, 0xED, 0xFE);
        var app = Bundles.WriteDesktopApp(player, At("game.app"), "game", Program);
        var icns = File.ReadAllBytes(Path.Combine(app, "Contents", "Resources", "icon.icns"));
        Assert.Equal("icns", Encoding.ASCII.GetString(icns, 0, 4));
        Assert.Equal(icns.Length, (int)BinaryPrimitives.ReadUInt32BigEndian(icns.AsSpan(4)));
        Assert.Contains("<key>CFBundleIconFile</key><string>icon</string>", File.ReadAllText(Path.Combine(app, "Contents", "Info.plist")));

        var png = Png(256, 100);                                           // a PNG of a size a Mac icon has
        File.WriteAllBytes(At("icon.png"), png);
        Bundles.WriteDesktopApp(player, At("game.app"), "game", Program, At("icon.png"));
        icns = File.ReadAllBytes(At("game.app/Contents/Resources/icon.icns"));
        Assert.Equal("ic08", Encoding.ASCII.GetString(icns, 8, 4));        // 256 × 256
        Assert.Equal(png, icns[16..]);

        File.WriteAllBytes(At("small.png"), Png(100, 100));                // not one
        var error = Assert.Throws<InvalidOperationException>(() => Bundles.WriteDesktopApp(player, At("other.app"), "other", Program, At("small.png")));
        Assert.Contains("100 × 100", error.Message);
        Assert.False(Directory.Exists(At("other.app")));
    }

    [Fact]
    public void AWindowsAppsIconIsWrittenIntoThePlayer()
    {
        File.WriteAllBytes(At("player.exe"), WindowsPlayerWithIcon());
        File.WriteAllBytes(At("icon.png"), Png(32, 390));
        Bundles.WriteDesktopApp(At("player.exe"), At("game.exe"), "game", Program, At("icon.png"));
        var exe = File.ReadAllBytes(At("game.exe"));

        // The group lists the one image, and it's in the old image with room for it.
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(exe.AsSpan(Group + 4)));
        Assert.Equal([32, 32, 0, 0, 1, 0, 32, 0], exe[(Group + 6)..(Group + 14)]);
        Assert.Equal(390u, BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(Group + 14)));
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(exe.AsSpan(Group + 18)));
        Assert.Equal(390u, BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(Entries + 0x10 + 4)));
        Assert.Equal(File.ReadAllBytes(At("icon.png")), exe[Images[1]..(Images[1] + 390)]);
        Assert.Equal(6 + 14u, BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(Entries + 0x20 + 4)));
        Assert.Equal(Program, EmbeddedPayload.TryRead(At("game.exe")));
    }

    [Fact]
    public void AnIconsImagesGoWhereTheyFit()
    {
        var images = new AppIcons.Image[] { new(16, 16, 0, 32, Png(16, 60)), new(32, 32, 0, 32, Png(32, 300)), new(48, 48, 0, 32, Png(48, 500)) };
        var exe = AppIcons.WithWindowsIcon(WindowsPlayerWithIcon(), images);

        // 48 has no room (400 bytes at most), so the group is 32 and 16.
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(exe.AsSpan(Group + 4)));
        Assert.Equal([32, 16], new[] { exe[Group + 6], exe[Group + 20] });
        Assert.Equal(images[1].Data, exe[Images[1]..(Images[1] + 300)]);
        Assert.Equal(images[0].Data, exe[Images[0]..(Images[0] + 60)]);
        Assert.All(exe[(Images[0] + 60)..(Images[0] + 64)], b => Assert.Equal(0, b));   // the rest of the old one cleared

        Assert.Throws<InvalidOperationException>(() => AppIcons.WithWindowsIcon(WindowsPlayerWithIcon(), [images[2]]));
        Assert.Throws<InvalidOperationException>(() => AppIcons.WithWindowsIcon([.. "MZ"u8, .. new byte[100]], images));
    }

    [Fact]
    public void ALinuxAppsIconIsAppendedBeforeTheProgram()
    {
        var player = Player(0x7F, (byte)'E', (byte)'L', (byte)'F');
        var png = Png(256, 80);
        File.WriteAllBytes(At("icon.png"), png);
        Bundles.WriteDesktopApp(player, At("game"), "game", Program, At("icon.png"));
        var app = File.ReadAllBytes(At("game"));
        var tail = app[File.ReadAllBytes(player).Length..];
        Assert.Equal([.. png, 80, 0, 0, 0, .. "FB-ICEND"u8, .. Program, 5, 0, 0, 0, .. "FB-BCEND"u8], tail);

        // Built again from the app: the old program and icon go.
        Bundles.WriteDesktopApp(At("game"), At("again"), "again", [7], At("icon.png"));
        Assert.Equal(app.Length - Program.Length + 1, new FileInfo(At("again")).Length);

        File.WriteAllBytes(At("icon.ico"), [0, 0, 1, 0, 0, 0]);
        Assert.Throws<InvalidOperationException>(() => Bundles.WriteDesktopApp(player, At("x"), "x", Program, At("icon.ico")));
    }

    /// <summary>A PNG's first bytes (its signature, and its header saying it's size × size),
    /// made up to `length`.</summary>
    private static byte[] Png(int size, int length)
    {
        var png = new byte[length];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(png, 0);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), (uint)size);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), (uint)size);
        return png;
    }

    // WindowsPlayerWithIcon's parts, in the file: its resources' data entries (the two images',
    // then the group's), the images (with room for 64 and 400 bytes), and the group.
    private const int Resources = 0x200, Entries = Resources + 0xC0, Group = Resources + 0x300;
    private static readonly int[] Images = [Resources + 0x100, Resources + 0x140];

    /// <summary>A Windows program with nothing but an icon: two images, 16 and 32 pixels, with
    /// room for 64 and 400 bytes, and the group listing them — in a resource section, laid
    /// out as a linker lays them out.</summary>
    private static byte[] WindowsPlayerWithIcon()
    {
        const uint Rva = 0x1000;   // where the resource section is when it's loaded
        var exe = new byte[0x800];
        void U16(int at, int value) => BinaryPrimitives.WriteUInt16LittleEndian(exe.AsSpan(at), (ushort)value);
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(at), value);

        "MZ"u8.CopyTo(exe);
        U32(0x3C, 0x40);
        "PE\0\0"u8.CopyTo(exe.AsSpan(0x40));
        U16(0x44, 0x8664);                       // x64
        U16(0x46, 1);                            // one section
        U16(0x54, 240);                          // the optional header's size
        const int Optional = 0x58;
        U16(Optional, 0x20B);                    // PE32+
        U32(Optional + 112 + 2 * 8, Rva);        // data directory 2, the resources
        U32(Optional + 112 + 2 * 8 + 4, 0x600);
        const int Section = Optional + 240;
        ".rsrc"u8.CopyTo(exe.AsSpan(Section));
        U32(Section + 8, 0x600);
        U32(Section + 12, Rva);
        U32(Section + 16, 0x600);
        U32(Section + 20, Resources);

        // The tree (offsets in the section): types, names, languages, then the data's entries.
        void Directory(int at, params (uint Id, uint To)[] entries)
        {
            U16(Resources + at + 14, entries.Length);
            for (var i = 0; i < entries.Length; i++)
            {
                U32(Resources + at + 16 + i * 8, entries[i].Id);
                U32(Resources + at + 20 + i * 8, entries[i].To);
            }
        }
        const uint Down = 0x80000000;            // to a directory, not data
        Directory(0x00, (3, Down | 0x20), (14, Down | 0x40));          // RT_ICON, RT_GROUP_ICON
        Directory(0x20, (1, Down | 0x60), (2, Down | 0x80));           // the images
        Directory(0x40, (32512, Down | 0xA0));                         // the group
        Directory(0x60, (1033, 0xC0));
        Directory(0x80, (1033, 0xD0));
        Directory(0xA0, (1033, 0xE0));
        void Data(int entry, int at, int size)
        {
            U32(Entries + entry, Rva + (uint)(at - Resources));
            U32(Entries + entry + 4, (uint)size);
        }
        Data(0x00, Images[0], 64);
        Data(0x10, Images[1], 400);
        Data(0x20, Group, 6 + 2 * 14);

        U16(Group + 2, 1);                        // an icon
        U16(Group + 4, 2);
        foreach (var (i, size, room) in new[] { (0, 16, 64), (1, 32, 400) })
        {
            var at = Group + 6 + i * 14;
            exe[at] = exe[at + 1] = (byte)size;
            U16(at + 4, 1);
            U16(at + 6, 32);
            U32(at + 8, (uint)room);
            U16(at + 12, i + 1);
        }
        return exe;
    }

    [Fact]
    public void SomethingElseIsntAPlayer()
    {
        var notAPlayer = Player((byte)'#', (byte)'!', (byte)'/', (byte)'b');
        Assert.Equal(Bundles.Os.Unknown, Bundles.OsOf(notAPlayer));
        Assert.Throws<InvalidOperationException>(() => Bundles.WriteDesktopApp(notAPlayer, At("x.app"), "x", Program));
    }
}
