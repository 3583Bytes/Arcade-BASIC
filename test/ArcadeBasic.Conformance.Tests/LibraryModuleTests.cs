using ArcadeBasic.Bytecode;
using ArcadeBasic.Compiler;
using ArcadeBasic.Core;
using ArcadeBasic.Parser;
using ArcadeBasic.Runtime;
using ArcadeBasic.Sema;
using ArcadeBasic.Vm;
using AstProgram = ArcadeBasic.Parser.Ast.Program;

namespace ArcadeBasic.Conformance.Tests;

/// <summary>
/// Programs in several files: LIBRARY "file" brings in another file's MODULEs and
/// procedures, and modules have their own variables (SHARE, PUBLIC — and DECLARE PUBLIC
/// to use them elsewhere), their own start-up code, and their own DATA (ISO 10279).
/// </summary>
public class LibraryModuleTests
{
    /// <summary>Load files["main.bas"] with LIBRARY finding the others by name; the problems,
    /// and the analysed program if there were no errors.</summary>
    private static (AstProgram? Program, SemanticInfo? Info, List<Diagnostic> Problems) Load(Dictionary<string, string> files)
    {
        var diags = new DiagnosticBag();
        var program = ProgramLoader.Load(new SourceFile("main.bas", files["main.bas"]),
            (name, _) => files.TryGetValue(name, out var text) ? new SourceFile(name, text) : null, diags);
        var info = diags.HasErrors ? null : Analyzer.Analyze(program, diags);
        return diags.HasErrors ? (null, null, [.. diags.All]) : (program, info, [.. diags.All]);
    }

    /// <summary>Run it to the end in one step; the output.</summary>
    private static string Run(Dictionary<string, string> files)
    {
        var (program, info, problems) = Load(files);
        Assert.True(program is not null, string.Join("\n", problems.Select(p => p.Render(false))));

        var stepped = new StringWriter { NewLine = "\n" };
        var vm = new BasicVm(BasicCompiler.Compile(program!, info!), stepped, TextReader.Null);
        var result = vm.Step(1_000_000);
        Assert.Equal(StepStatus.Halted, result.Status);
        Assert.Null(result.Error);
        return stepped.ToString();
    }

    [Fact]
    public void AModuleStartsUpBeforeTheMainProgramAndSharesItsPublicVariables()
    {
        var output = Run(new()
        {
            ["main.bas"] = """
                LIBRARY "counter.bas"
                DECLARE PUBLIC COUNT
                PRINT "main sees"; COUNT
                CALL BUMP
                CALL BUMP
                PRINT "after"; COUNT
                END
                """,
            ["counter.bas"] = """
                MODULE COUNTER
                  PUBLIC COUNT
                  SHARE INC
                  LET COUNT = 10
                  LET INC = 5
                  PRINT "counter ready"
                  PUBLIC SUB BUMP
                    LET COUNT = COUNT + INC
                  END SUB
                END MODULE
                """,
        });
        Assert.Equal("counter ready\nmain sees 10 \nafter 20 \n", output);
    }

    [Fact]
    public void ModuleCodeDoesntSeeTheMainProgramsVariables()
    {
        var files = new Dictionary<string, string>
        {
            ["main.bas"] = """
                LET X = 5
                CALL SHOWX
                MODULE M
                  PUBLIC SUB SHOWX
                    PRINT X
                  END SUB
                END MODULE
                """,
        };
        Assert.Equal(" 0 \n", Run(files));        // SHOWX's own X, not the main program's
        Assert.Contains(Load(files).Problems, p => p.Code == Analyzer.WarnImplicitVariable);
    }

    [Fact]
    public void PublicArraysAreDimensionedAsTheModuleStartsUp()
    {
        Assert.Equal(" 4  9 \n", Run(new()
        {
            ["main.bas"] = """
                LIBRARY "board.bas"
                DECLARE PUBLIC BOARD()
                CALL FILL
                PRINT BOARD(2); BOARD(3)
                """,
            ["board.bas"] = """
                MODULE BOARDS
                  PUBLIC BOARD(3)
                  PUBLIC SUB FILL
                    FOR I = 1 TO 3
                      LET BOARD(I) = I * I
                    NEXT I
                  END SUB
                END MODULE
                """,
        }));
    }

    [Fact]
    public void TheProgramsOptionBaseHoldsAsItsModulesStartUp()
    {
        Assert.Equal(" 7  7 \n", Run(new()
        {
            ["main.bas"] = "LIBRARY \"grid.bas\"\nOPTION BASE 0\nDECLARE PUBLIC G()\nPRINT G(0); G(2)\n",
            ["grid.bas"] = "MODULE GRID\n  PUBLIC G(2)\n  FOR I = 0 TO 2\n    LET G(I) = 7\n  NEXT I\nEND MODULE\n",
        }));
    }

    [Fact]
    public void EachProgramUnitReadsItsOwnData()
    {
        Assert.Equal(" 1  2 alpha\nbeta\ngamma\n 1 \n", Run(new()
        {
            ["main.bas"] = """
                LIBRARY "words.bas"
                DECLARE PUBLIC FIRST$
                DATA 1, 2
                READ A, B
                PRINT A; B; FIRST$
                CALL NEXTWORD
                CALL NEXTWORD
                RESTORE
                READ C
                PRINT C
                """,
            ["words.bas"] = """
                MODULE WORDS
                  PUBLIC FIRST$
                  READ FIRST$
                  PUBLIC SUB NEXTWORD
                    READ W$
                    PRINT W$
                  END SUB
                  DATA "alpha", "beta", "gamma"
                END MODULE
                """,
        }));
    }

    [Fact]
    public void AStandaloneBuildKeepsEachUnitsDataAndTheFileNames()
    {
        var (program, info, _) = Load(new()
        {
            ["main.bas"] = "LIBRARY \"lib.bas\"\nDATA 7\nREAD A\nPRINT A; WORD$()\n",
            ["lib.bas"] = "MODULE L\n  PUBLIC FUNCTION WORD$()\n    READ W$\n    WORD$ = W$\n  END FUNCTION\n  DATA \"seven\"\nEND MODULE\n",
        });
        var bytes = BytecodeSerializer.Serialize(BasicCompiler.Compile(program!, info!));
        var loaded = BytecodeSerializer.Deserialize(bytes);
        Assert.Equal(["main.bas", "lib.bas"], loaded.SourceFiles);
        var output = new StringWriter { NewLine = "\n" };
        Assert.Equal(StepStatus.Halted, new BasicVm(loaded, output, TextReader.Null).Step(100_000).Status);
        Assert.Equal(" 7 seven\n", output.ToString());
    }

    [Fact]
    public void ALibrarysOwnProceduresAreExternalOnes()
    {
        var files = new Dictionary<string, string>
        {
            ["main.bas"] = """
                LIBRARY "util.bas"
                LET N = 3
                PRINT TWICE(N); HALF(N)
                """,
            ["util.bas"] = """
                ! Helpers: callable from the program, seeing only their own variables.
                FUNCTION TWICE(X)
                  TWICE = 2 * X
                END FUNCTION
                FUNCTION HALF(X)
                  HALF = X / 2 + N
                END FUNCTION
                """,
        };
        Assert.Equal(" 6  1.5 \n", Run(files));    // HALF's N is its own (0), not the program's 3
    }

    [Fact]
    public void LibrariesLoadOnceWhatTheyUseFirst()
    {
        Assert.Equal("b ready\na ready\nmain\n", Run(new()
        {
            ["main.bas"] = "LIBRARY \"a.bas\", \"b.bas\"\nPRINT \"main\"\n",
            ["a.bas"] = "LIBRARY \"b.bas\"\nMODULE A\n  PRINT \"a ready\"\nEND MODULE\n",
            ["b.bas"] = "MODULE B\n  PRINT \"b ready\"\nEND MODULE\n",
        }));
    }

    [Theory]
    [InlineData("LIBRARY \"missing.bas\"\nPRINT 1\n", "", ProgramLoader.ErrLibraryNotFound)]
    [InlineData("LIBRARY \"lib.bas\"\nPRINT 1\n", "PRINT \"loose\"\n", ProgramLoader.ErrNotInLibrary)]
    [InlineData("DECLARE PUBLIC NOPE\nPRINT NOPE\n", "", Analyzer.ErrUndefinedName)]
    [InlineData("LIBRARY \"lib.bas\"\nDECLARE PUBLIC T\n", "MODULE L\n  PUBLIC T(2)\nEND MODULE\n", Analyzer.ErrTypeMismatch)]
    [InlineData("SHARE X\nPRINT X\n", "", Analyzer.ErrModule)]
    [InlineData("LIBRARY \"lib.bas\"\n", "MODULE L1\n  PUBLIC V\nEND MODULE\nMODULE L2\n  PUBLIC V\nEND MODULE\n", Analyzer.ErrDuplicateDeclaration)]
    public void MistakesAreProblemsBeforeItRuns(string main, string library, string code)
    {
        var (_, _, problems) = Load(new() { ["main.bas"] = main, ["lib.bas"] = library });
        Assert.Contains(problems, p => p.Code == code && p.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void TheOneCallEngineFindsLibrariesAndSaysWhereTheProgramStopped()
    {
        var output = new StringWriter { NewLine = "\n" };
        var result = BasicEngine.Run("LIBRARY \"lib.bas\"\nPRINT \"start\"\nCALL DIVIDE(0)\n", output, filename: "main.bas",
            libraries: name => name == "lib.bas" ? "MODULE L\n  PUBLIC SUB DIVIDE(D)\n    PRINT 1 / D\n  END SUB\nEND MODULE\n" : null);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("start\n", output.ToString());
        Assert.Equal(["unhandled exception type 1001 at line 3 in lib.bas: division by zero"], result.Diagnostics);
    }

    [Fact]
    public void ErrorsAndBreakpointsNameTheLibrarysFile()
    {
        var files = new Dictionary<string, string>
        {
            ["main.bas"] = "LIBRARY \"lib.bas\"\nCALL DIVIDE(0)\n",
            ["lib.bas"] = "MODULE L\n  PUBLIC SUB DIVIDE(D)\n    PRINT 1 / D\n  END SUB\nEND MODULE\n",
        };
        var (program, info, problems) = Load(files);
        Assert.True(program is not null, string.Join("\n", problems.Select(p => p.Render(false))));
        var compiled = BasicCompiler.Compile(program!, info!);
        Assert.Equal(["main.bas", "lib.bas"], compiled.SourceFiles);

        var vm = new BasicVm(compiled, new StringWriter(), TextReader.Null);
        vm.Breakpoints.Add(new SourceLine(1, 3));
        Assert.Equal(StepStatus.Paused, vm.Step(10_000).Status);
        Assert.Equal((1, 3), (vm.CurrentFile, vm.CurrentLine));
        Assert.Equal(1, vm.CallStack()[0].File);

        var result = vm.Step(10_000);
        Assert.Equal("unhandled exception type 1001 at line 3 in lib.bas: division by zero", result.Error);

        Assert.Equal("line 3 in lib.bas", new BasicException(1001, 3, "", "lib.bas").Where);
    }
}
