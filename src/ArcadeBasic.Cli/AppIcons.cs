using System.Buffers.Binary;
using System.Text;

namespace ArcadeBasic.Cli;

/// <summary>
/// The icon <c>arcade-basic build --desktop --icon &lt;file&gt;</c> gives an app — a PNG, or
/// the OS's own kind of icon file — as the IDE's File → Export does
/// (src/ArcadeBasic.Web/wwwroot/icons.js). A Mac app's is Contents/Resources/icon.icns; a
/// Windows app's is written into the player, over the placeholder icon it's built with
/// (desktop/player/icons/make-icons.mjs), whose images each leave room for any PNG of
/// their size; a Linux app's, a PNG, is appended before the program, for its window.
/// </summary>
public static class AppIcons
{
    /// <summary>One of a Windows icon's images: its size, colours, and data (a PNG, or an
    /// .ico's bitmap).</summary>
    public sealed record Image(int Width, int Height, byte Colours, ushort Bits, byte[] Data);

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>A PNG's width and height, or null if <paramref name="data"/> isn't one.</summary>
    public static (int Width, int Height)? PngSize(byte[] data) =>
        data.Length >= 24 && data.AsSpan(0, 8).SequenceEqual(PngSignature) && Encoding.ASCII.GetString(data, 12, 4) == "IHDR"
            ? ((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16)), (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20)))
            : null;

    /// <summary>The Mac icon (.icns) in <paramref name="path"/>: an .icns as it is, or a PNG
    /// of 128, 256, 512 or 1024 pixels square made one.</summary>
    public static byte[] ForMac(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length >= 8 && Encoding.ASCII.GetString(data, 0, 4) == "icns") return data;
        var type = PngSize(data) switch
        {
            (128, 128) => "ic07",
            (256, 256) => "ic08",
            (512, 512) => "ic09",
            (1024, 1024) => "ic10",
            null => throw new InvalidOperationException($"{path} isn't a PNG or a Mac icon (.icns)"),
            var (w, h) => throw new InvalidOperationException(
                $"{path} is {w} × {h}: a Mac app's icon is a PNG of 128, 256, 512 or 1024 pixels square, or an .icns (iconutil makes one)"),
        };
        var icns = new byte[16 + data.Length];
        Encoding.ASCII.GetBytes("icns").CopyTo(icns, 0);
        BinaryPrimitives.WriteUInt32BigEndian(icns.AsSpan(4), (uint)icns.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(icns, 8);
        BinaryPrimitives.WriteUInt32BigEndian(icns.AsSpan(12), (uint)(8 + data.Length));
        data.CopyTo(icns, 16);
        return icns;
    }

    /// <summary>The Windows icon's images in <paramref name="path"/>: an .ico's, or a PNG of up
    /// to 256 pixels square.</summary>
    public static IReadOnlyList<Image> ForWindows(string path)
    {
        var data = File.ReadAllBytes(path);
        if (PngSize(data) is var (w, h))
            return w == h && w <= 256 ? [new Image(w, h, 0, 32, data)]
                : throw new InvalidOperationException($"{path} is {w} × {h}: a Windows app's icon is a PNG of up to 256 pixels square, or an .ico");
        // An .ico: [0 u16][1 u16][count u16], then for each image [width u8][height u8]
        // [colours u8][0 u8][planes u16][bits u16][size u32][offset u32] (a width of 0 is 256).
        var bad = new InvalidOperationException($"{path} isn't a PNG or a Windows icon (.ico)");
        if (data.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(data) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2)) != 1)
            throw bad;
        var images = new List<Image>();
        for (var i = 0; i < BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4)); i++)
        {
            var at = 6 + i * 16;
            if (at + 16 > data.Length) throw bad;
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 8));
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 12));
            if ((ulong)offset + size > (ulong)data.Length) throw bad;
            images.Add(new Image(data[at] == 0 ? 256 : data[at], data[at + 1] == 0 ? 256 : data[at + 1], data[at + 2],
                BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 6)), data.AsSpan((int)offset, (int)size).ToArray()));
        }
        return images.Count > 0 ? images : throw bad;
    }

    /// <summary>The Linux icon in <paramref name="path"/>: a PNG.</summary>
    public static byte[] ForLinux(string path)
    {
        var data = File.ReadAllBytes(path);
        return PngSize(data) is not null ? data : throw new InvalidOperationException($"{path} isn't a PNG, which a Linux app's icon is");
    }

    /// <summary>A copy of the Windows executable <paramref name="exe"/> with its icon replaced
    /// by <paramref name="images"/>: each written over one of the old images with room for it,
    /// the biggest first, and the icon's group listing them. One with no room left is left
    /// out.</summary>
    public static byte[] WithWindowsIcon(byte[] exe, IReadOnlyList<Image> images)
    {
        var output = (byte[])exe.Clone();
        Resource group;
        List<Resource> slots;
        try
        {
            (group, slots) = IconGroup(output);
        }
        catch (ArgumentOutOfRangeException)   // (an empty directory in its resources)
        {
            throw new InvalidOperationException("the Windows player isn't a Windows program with an icon");
        }
        var free = slots.OrderByDescending(s => s.Size).ToList();
        var placed = new List<(Image Image, ushort Id)>();
        foreach (var image in images.OrderByDescending(i => i.Data.Length))
        {
            if (free.Count == 0 || image.Data.Length > free[0].Size) continue;
            var slot = free[0];
            free.RemoveAt(0);
            Array.Clear(output, slot.Offset, slot.Size);
            image.Data.CopyTo(output, slot.Offset);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(slot.Entry + 4), (uint)image.Data.Length);
            placed.Add((image, slot.Id));
        }
        if (placed.Count == 0)
            throw new InvalidOperationException("the icon doesn't fit in the Windows player (one from desktop/player/build.sh leaves room for it)");
        placed.Sort((a, b) => b.Image.Width.CompareTo(a.Image.Width));
        for (var i = 0; i < placed.Count; i++)
        {
            var (image, id) = placed[i];
            var at = group.Offset + 6 + i * 14;   // [width u8][height u8][colours u8][0 u8][planes u16][bits u16][size u32][id u16]
            output[at] = (byte)image.Width;
            output[at + 1] = (byte)image.Height;
            output[at + 2] = image.Colours;
            output[at + 3] = 0;
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(at + 4), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(at + 6), image.Bits);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(at + 8), (uint)image.Data.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(at + 12), id);
        }
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(group.Offset + 4), (ushort)placed.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(group.Entry + 4), (uint)(6 + placed.Count * 14));
        return output;
    }

    /// <summary>Resource data: its id, the entry that says where it is, where it is, and its size.</summary>
    private sealed record Resource(ushort Id, int Entry, int Offset, int Size);

    /// <summary>A Windows executable's icon (its first icon group): the group's data, and the
    /// images it lists.</summary>
    private static (Resource Group, List<Resource> Images) IconGroup(byte[] exe)
    {
        var bad = new InvalidOperationException("the Windows player isn't a Windows program with an icon");
        uint U32(int at) => at >= 0 && at + 4 <= exe.Length ? BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(at)) : throw bad;
        ushort U16(int at) => at >= 0 && at + 2 <= exe.Length ? BinaryPrimitives.ReadUInt16LittleEndian(exe.AsSpan(at)) : throw bad;

        if (exe.Length < 0x40 || U16(0) != 0x5A4D) throw bad;
        var pe = (int)U32(0x3C);
        if (U32(pe) != 0x4550) throw bad;
        var sectionCount = U16(pe + 6);
        var optional = pe + 24;
        var sections = optional + U16(pe + 20);
        var directories = optional + (U16(optional) == 0x20B ? 112 : 96);   // PE32+ or PE32
        int OffsetOf(uint rva)
        {
            for (var s = 0; s < sectionCount; s++)
            {
                var at = sections + s * 40;
                var start = U32(at + 12);
                if (rva >= start && rva < start + U32(at + 16)) return (int)(U32(at + 20) + rva - start);
            }
            throw bad;
        }
        var resources = U32(directories + 2 * 8);   // data directory 2: the resources
        if (resources == 0) throw bad;
        var root = OffsetOf(resources);
        // The resources are a tree: types, then names (or numbers), then languages, then the data.
        List<(uint Id, int To)> Entries(int at) =>
            Enumerable.Range(0, U16(at + 12) + U16(at + 14))
                .Select(i => (U32(at + 16 + i * 8), (int)(U32(at + 20 + i * 8) & 0x7FFFFFFF))).ToList();
        Resource DataOf((uint Id, int To) named)
        {
            var entry = root + Entries(root + named.To)[0].To;   // its first language's
            return new Resource((ushort)named.Id, entry, OffsetOf(U32(entry)), (int)U32(entry + 4));
        }
        var types = Entries(root);
        var groups = types.FirstOrDefault(t => t.Id == 14);   // RT_GROUP_ICON
        var icons = types.FirstOrDefault(t => t.Id == 3);     // RT_ICON
        if (groups == default || icons == default) throw bad;
        var group = DataOf(Entries(root + groups.To)[0]);
        var ids = Enumerable.Range(0, U16(group.Offset + 4)).Select(i => U16(group.Offset + 6 + i * 14 + 12)).ToHashSet();
        var images = Entries(root + icons.To).Where(e => ids.Contains((ushort)e.Id)).Select(DataOf).ToList();
        return (group, images);
    }
}
