using System.Runtime.InteropServices.JavaScript;
using ArcadeBasic.Bytecode;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("browser")]

// The player's .NET side: open a compiled program (what `arcade-basic build --desktop`
// puts beside the player); running it is Playground.Run.cs, shared with the IDE.
Playground.UseFilesFolder();   // programs' files (Playground.Files.cs)

internal static partial class Playground
{
    private static string s_title = "";

    /// <summary>Get the compiled program in <paramref name="bytecode"/> ready to step: "" on
    /// success, otherwise why it can't run.</summary>
    [JSExport]
    internal static string Open([JSMarshalAs<JSType.Array<JSType.Number>>] byte[] bytecode)
    {
        s_vm = null;
        ArcadeBasic.Bytecode.Program program;
        try
        {
            program = BytecodeSerializer.Deserialize(bytecode);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
        {
            return $"This program can't be run: {ex.Message}";
        }
        s_title = program.SourceFiles.Count > 0 ? Path.GetFileNameWithoutExtension(program.SourceFiles[0]) : "";
        Begin(program);
        return "";
    }

    /// <summary>The program's name: its main file's, without ".bas".</summary>
    [JSExport]
    internal static string Title() => s_title;
}
