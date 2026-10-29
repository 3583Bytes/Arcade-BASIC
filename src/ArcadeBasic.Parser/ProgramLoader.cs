using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser.Ast;

namespace ArcadeBasic.Parser;

/// <summary>
/// A program in several files: the main file, and the files its LIBRARY statements
/// name — and the ones theirs name — each parsed once and joined into one
/// <see cref="Program"/>, the libraries first (so their modules are set up before the
/// main program runs, and before the libraries that use them).
///
/// A library holds MODULEs, and SUBs, FUNCTIONs and DEFs of its own: those are external
/// procedures (ISO 10279): callable from the whole program, and seeing only their own
/// variables — they're joined as a module named after the file, every procedure PUBLIC.
/// Anything else at a library's top level is an error: it belongs in a module (where it
/// runs first) or the main program.
///
/// How a name in LIBRARY becomes a file is the host's: <c>open(name, from)</c> returns
/// its source, or null, where <c>from</c> is the file the LIBRARY statement is in.
/// </summary>
public static class ProgramLoader
{
    public const string ErrLibraryNotFound = "FB0210";
    public const string ErrNotInLibrary = "FB0211";

    public static Program Load(SourceFile main, Func<string, SourceFile, SourceFile?>? open, DiagnosticBag diagnostics)
    {
        Program Parse(SourceFile file) => new BasicParser(new BasicLexer(file, diagnostics).Lex(), file, diagnostics).ParseProgram();

        var mainProgram = Parse(main);
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { main.Path };
        var libraries = new List<Stmt>();

        void Visit(Program program, SourceFile file)
        {
            foreach (var library in program.Statements.OfType<LibraryStmt>())
            {
                foreach (var name in library.Files)
                {
                    var source = open?.Invoke(name, file);
                    if (source is null)
                    {
                        diagnostics.Error(ErrLibraryNotFound, library.Span, $"can't find the library \"{name}\"");
                        continue;
                    }
                    if (!loaded.Add(source.Path)) continue;
                    var parsed = Parse(source);
                    Visit(parsed, source);                 // what it uses comes before it
                    libraries.AddRange(LibraryUnits(parsed, source, diagnostics));
                }
            }
        }

        Visit(mainProgram, main);
        return libraries.Count == 0 ? mainProgram : new Program(mainProgram.Span, [.. libraries, .. mainProgram.Statements]);
    }

    /// <summary>A library's statements as they join the program: its modules as they are,
    /// its own procedures gathered into a module named after the file.</summary>
    private static IEnumerable<Stmt> LibraryUnits(Program library, SourceFile file, DiagnosticBag diagnostics)
    {
        var procedures = new List<Stmt>();
        foreach (var stmt in library.Statements)
        {
            switch (stmt)
            {
                case ModuleStmt or LibraryStmt or RemStmt:
                    yield return stmt;
                    break;
                case SubStmt s: procedures.Add(s with { IsPublic = true }); break;
                case FunctionStmt f: procedures.Add(f with { IsPublic = true }); break;
                case DefStmt d: procedures.Add(d with { IsPublic = true }); break;
                case EndStmt:
                    break;
                default:
                    diagnostics.Error(ErrNotInLibrary, stmt.Span,
                        "a library holds MODULEs, SUBs, FUNCTIONs and DEFs: put this statement in a MODULE (it runs before the main program) or in the main program");
                    break;
            }
        }
        if (procedures.Count > 0)
            yield return new ModuleStmt(library.Span, Path.GetFileNameWithoutExtension(file.Path), procedures);
    }
}
