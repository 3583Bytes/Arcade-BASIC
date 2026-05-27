using System.Text;
using Singulink.Numerics;

namespace FullBasic.Bytecode;

/// <summary>
/// Binary serializer for compiled <see cref="Program"/> instances. Used by
/// Phase-10's `build` command to append bytecode to a self-extracting
/// binary, and at startup to read it back.
///
/// Format (all multi-byte ints little-endian):
///   u32 magic = 0x46424358 ('FBCX')
///   u32 version = 1
///   string[] builtin_names
///   u32 sub_count   then SubMetadata + Chunk pairs
///   u32 fn_count    then FunctionMetadata + Chunk pairs
///   u32 def_count   then DefMetadata + Chunk pairs
///   Chunk main      (last so the format mirrors the runtime layout)
///
/// Strings are length-prefixed UTF-8. BigDecimals are length-prefixed
/// invariant-culture decimal text.
/// </summary>
public static class BytecodeSerializer
{
    private const uint Magic = 0x46424358u; // 'FBCX'
    private const uint Version = 1;

    public static byte[] Serialize(Program program)
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(Version);

        WriteStringList(w, program.BuiltinNames);

        w.Write((uint)program.Subs.Count);
        foreach (var s in program.Subs)
        {
            WriteString(w, s.Name);
            w.Write((uint)s.ParamCount);
            WriteChunk(w, s.Body);
        }

        w.Write((uint)program.Functions.Count);
        foreach (var f in program.Functions)
        {
            WriteString(w, f.Name);
            w.Write(f.IsString);
            w.Write((uint)f.ParamCount);
            w.Write((uint)f.ReturnSlot);
            WriteChunk(w, f.Body);
        }

        w.Write((uint)program.Defs.Count);
        foreach (var d in program.Defs)
        {
            WriteString(w, d.Name);
            w.Write(d.IsString);
            w.Write((uint)d.ParamCount);
            WriteChunk(w, d.Body);
        }

        WriteChunk(w, program.Main);
        w.Flush();
        return ms.ToArray();
    }

    public static Program Deserialize(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
        var magic = r.ReadUInt32();
        if (magic != Magic) throw new InvalidDataException("bytecode: bad magic");
        var version = r.ReadUInt32();
        if (version != Version) throw new InvalidDataException($"bytecode: unsupported version {version}");

        var builtinNames = ReadStringList(r);

        var subCount = r.ReadUInt32();
        var subs = new List<CompiledSub>((int)subCount);
        for (var i = 0; i < subCount; i++)
        {
            var name = ReadString(r);
            var paramCount = (int)r.ReadUInt32();
            var body = ReadChunk(r);
            subs.Add(new CompiledSub(name, paramCount, body));
        }

        var fnCount = r.ReadUInt32();
        var functions = new List<CompiledFunction>((int)fnCount);
        for (var i = 0; i < fnCount; i++)
        {
            var name = ReadString(r);
            var isString = r.ReadBoolean();
            var paramCount = (int)r.ReadUInt32();
            var returnSlot = (int)r.ReadUInt32();
            var body = ReadChunk(r);
            functions.Add(new CompiledFunction(name, isString, paramCount, returnSlot, body));
        }

        var defCount = r.ReadUInt32();
        var defs = new List<CompiledDef>((int)defCount);
        for (var i = 0; i < defCount; i++)
        {
            var name = ReadString(r);
            var isString = r.ReadBoolean();
            var paramCount = (int)r.ReadUInt32();
            var body = ReadChunk(r);
            defs.Add(new CompiledDef(name, isString, paramCount, body));
        }

        var main = ReadChunk(r);

        return new Program
        {
            Main = main,
            Subs = subs,
            Functions = functions,
            Defs = defs,
            BuiltinNames = builtinNames,
        };
    }

    private static void WriteChunk(BinaryWriter w, Chunk c)
    {
        w.Write((uint)c.FrameSize);
        var code = c.Code;
        w.Write((uint)code.Count);
        for (var i = 0; i < code.Count; i++) w.Write(code[i]);
        w.Write((uint)c.Numbers.Count);
        foreach (var n in c.Numbers) WriteString(w, n.ToString());
        w.Write((uint)c.Strings.Count);
        foreach (var s in c.Strings) WriteString(w, s);
    }

    private static Chunk ReadChunk(BinaryReader r)
    {
        var c = new Chunk { FrameSize = (int)r.ReadUInt32() };
        var codeLen = (int)r.ReadUInt32();
        for (var i = 0; i < codeLen; i++)
        {
            c.Emit((Opcode)r.ReadByte());
        }
        var numbers = (int)r.ReadUInt32();
        for (var i = 0; i < numbers; i++)
        {
            var s = ReadString(r);
            c.AddNumberConstant(BigDecimal.Parse(s));
        }
        var strings = (int)r.ReadUInt32();
        for (var i = 0; i < strings; i++)
        {
            c.AddStringConstant(ReadString(r));
        }
        return c;
    }

    private static void WriteString(BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        w.Write((uint)bytes.Length);
        w.Write(bytes);
    }

    private static string ReadString(BinaryReader r)
    {
        var len = (int)r.ReadUInt32();
        var bytes = r.ReadBytes(len);
        return Encoding.UTF8.GetString(bytes);
    }

    private static void WriteStringList(BinaryWriter w, IReadOnlyList<string> list)
    {
        w.Write((uint)list.Count);
        foreach (var s in list) WriteString(w, s);
    }

    private static List<string> ReadStringList(BinaryReader r)
    {
        var count = (int)r.ReadUInt32();
        var list = new List<string>(count);
        for (var i = 0; i < count; i++) list.Add(ReadString(r));
        return list;
    }
}

/// <summary>Helper for the embedded-payload framing used by Phase-10 self-extracting binaries.</summary>
public static class EmbeddedPayload
{
    public const string TrailerMagic = "FB-BCEND";
    public static readonly byte[] TrailerMagicBytes = Encoding.ASCII.GetBytes(TrailerMagic);

    /// <summary>Append [payload bytes][u32 length LE][magic bytes] to <paramref name="dest"/>.</summary>
    public static void Append(Stream dest, byte[] payload)
    {
        dest.Write(payload, 0, payload.Length);
        Span<byte> lenBuf = stackalloc byte[4];
        var len = (uint)payload.Length;
        lenBuf[0] = (byte)(len & 0xFF);
        lenBuf[1] = (byte)((len >> 8) & 0xFF);
        lenBuf[2] = (byte)((len >> 16) & 0xFF);
        lenBuf[3] = (byte)((len >> 24) & 0xFF);
        dest.Write(lenBuf);
        dest.Write(TrailerMagicBytes, 0, TrailerMagicBytes.Length);
    }

    /// <summary>Try to read an appended payload from a binary file. Returns null if no trailer is found.</summary>
    public static byte[]? TryRead(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var totalLen = fs.Length;
            if (totalLen < 12) return null;

            // Read trailer (last 12 bytes).
            var trailer = new byte[12];
            fs.Seek(totalLen - 12, SeekOrigin.Begin);
            ReadExact(fs, trailer);

            // Magic is the last 8 bytes.
            for (var i = 0; i < 8; i++)
            {
                if (trailer[4 + i] != TrailerMagicBytes[i]) return null;
            }

            // Length is the 4 bytes before magic.
            var len = (uint)trailer[0]
                | ((uint)trailer[1] << 8)
                | ((uint)trailer[2] << 16)
                | ((uint)trailer[3] << 24);
            if (len == 0 || len > totalLen - 12) return null;

            var payload = new byte[(int)len];
            fs.Seek(totalLen - 12 - len, SeekOrigin.Begin);
            ReadExact(fs, payload);
            return payload;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Read exactly <c>buffer.Length</c> bytes from <paramref name="stream"/> or throw.
    /// Stream.ReadExactly is .NET 7+; this helper works on both net9 and netstandard2.1.
    /// </summary>
    private static void ReadExact(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }
}
