using System.Runtime.InteropServices.JavaScript;
using ArcadeBasic.Bytecode;

// File → Export (wwwroot/export.js): a program compiled to bytecode, for the player
// (src/ArcadeBasic.Player) to run as game.bin — the page packs the player and it into a zip.
internal static partial class Playground
{
    private static byte[] s_exported = [];

    /// <summary>Compile the program (with its libraries) for exporting: "" on success — then
    /// <see cref="ExportedBytecode"/> has it — otherwise the diagnostics to show.</summary>
    [JSExport]
    internal static string Export(string name, string source,
        [JSMarshalAs<JSType.Array<JSType.String>>] string[] names, [JSMarshalAs<JSType.Array<JSType.String>>] string[] texts)
    {
        s_exported = [];
        var (compiled, _, problems) = Compile(name, source, names, texts);
        if (compiled is null) return problems;
        s_exported = BytecodeSerializer.Serialize(compiled);
        return "";
    }

    /// <summary>The last <see cref="Export"/>'s bytecode.</summary>
    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    internal static byte[] ExportedBytecode() => s_exported;
}
