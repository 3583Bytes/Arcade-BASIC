using ArcadeBasic.Ide;

namespace ArcadeBasic.Ide.Tests;

/// <summary>Compile (F6) and Build (F7) check the whole program, its LIBRARY files included —
/// found next to the open file, or among the bundled examples.</summary>
public class CompileServiceTests
{
    [Fact]
    public void ABundledExampleFindsTheBundledLibraries()
    {
        var result = CompileService.Compile(ExamplesProvider.Source("maze3d.bas")!);
        Assert.True(result.Ok, string.Join("\n", result.Diagnostics));
        Assert.Equal(["<editor>", "maze3d-level.bas", "maze3d-textures.bas"], result.Program!.SourceFiles.Order());
    }

    [Fact]
    public void ASavedProgramFindsTheLibrariesBesideIt()
    {
        var folder = Directory.CreateTempSubdirectory("arcade-basic-ide-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "greet.bas"), "SUB HELLO\n  PRINT \"hello\"\nEND SUB\n");
            const string Source = "LIBRARY \"greet.bas\"\nCALL HELLO\n";
            Assert.True(CompileService.Compile(Source, folder).Ok);
            Assert.Contains(CompileService.Validate(Source).Diagnostics, d => d.Contains("can't find the library \"greet.bas\""));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
