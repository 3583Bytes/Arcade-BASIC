using System.Text;
using System.Text.RegularExpressions;
using ArcadeBasic.Compiler;
using ArcadeBasic.Core;
using ArcadeBasic.Interpreter;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;

namespace ArcadeBasic.Cli;

/// <summary>
/// Interactive Arcade BASIC REPL.
///
/// Strategy: accumulate every accepted line into a single session source string.
/// On each new fragment (single statement or multi-line block) we re-lex / parse /
/// analyze / execute the whole accumulated source, capturing stdout. Only the
/// portion that wasn't in the previous capture is emitted to the real stdout.
///
/// This keeps variable state correct (the whole program runs against a fresh
/// frame each turn) without needing incremental sema. The drawback: long
/// sessions get linearly slower, and side effects with nondeterministic input
/// (INPUT statements, RANDOMIZE / RND seeded from clock) don't round-trip
/// through the diff. The REPL warns about that and offers .clear / .load for
/// recovery.
/// </summary>
internal sealed class BasicRepl
{
    private readonly StringBuilder _session = new();
    private string _previousOutput = string.Empty;

    public int Run()
    {
        Console.WriteLine("Arcade BASIC REPL — type .help for commands, .exit to quit.");

        var buffer = new StringBuilder();
        var depth = 0;

        while (true)
        {
            Console.Write(depth == 0 ? "> " : "... ");
            var line = Console.ReadLine();
            if (line is null)
            {
                Console.WriteLine();
                break;
            }

            // Top-level dot-commands.
            if (depth == 0 && line.TrimStart().StartsWith('.'))
            {
                if (HandleCommand(line.Trim())) break;
                continue;
            }

            buffer.AppendLine(line);
            depth = Math.Max(0, depth + BlockDelta(line));

            if (depth == 0 && buffer.Length > 0)
            {
                ProcessFragment(buffer.ToString());
                buffer.Clear();
            }
        }

        return 0;
    }

    // -- Commands --------------------------------------------------------

    private bool HandleCommand(string cmd)
    {
        switch (cmd)
        {
            case ".exit":
            case ".quit":
                Console.WriteLine("bye.");
                return true;

            case ".help":
                Console.WriteLine("commands:");
                Console.WriteLine("  .exit, .quit        leave the REPL");
                Console.WriteLine("  .help               this message");
                Console.WriteLine("  .list               print the accumulated session source");
                Console.WriteLine("  .clear              discard the session and start fresh");
                Console.WriteLine();
                Console.WriteLine("notes:");
                Console.WriteLine("  - Multi-line blocks (FOR/DO/IF.../SUB...) are accepted; the prompt");
                Console.WriteLine("    becomes '... ' until the block is closed.");
                Console.WriteLine("  - INPUT and RANDOMIZE don't round-trip cleanly through the REPL's");
                Console.WriteLine("    re-execute-each-turn model. Use a .bas file with `arcade-basic run`");
                Console.WriteLine("    for programs that need them.");
                return false;

            case ".list":
                if (_session.Length == 0) Console.WriteLine("(empty session)");
                else Console.Write(_session.ToString());
                return false;

            case ".clear":
                _session.Clear();
                _previousOutput = string.Empty;
                Console.WriteLine("session cleared.");
                return false;

            default:
                Console.Error.WriteLine($"unknown command: {cmd} (type .help)");
                return false;
        }
    }

    // -- Fragment execution ---------------------------------------------

    private void ProcessFragment(string fragment)
    {
        var candidate = _session.ToString() + fragment;
        var file = new SourceFile("repl", candidate);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();

        if (diags.HasErrors)
        {
            RenderDiagnostics(diags, onlyAfter: _session.Length);
            return; // don't accept the fragment
        }

        var info = Analyzer.Analyze(program, diags);
        if (diags.HasErrors)
        {
            RenderDiagnostics(diags, onlyAfter: _session.Length);
            return;
        }
        // Surface non-error diagnostics from the new fragment only.
        RenderDiagnostics(diags, onlyAfter: _session.Length);

        var capture = new StringWriter();
        try
        {
            var interp = new BasicInterpreter(program, info, capture, Console.In);
            interp.Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"runtime error: {ex.Message}");
            return;
        }

        var fullOutput = capture.ToString();
        var newPart = fullOutput.Length > _previousOutput.Length
            ? fullOutput[_previousOutput.Length..]
            : string.Empty;

        if (!string.IsNullOrEmpty(newPart))
        {
            Console.Write(newPart);
            if (!newPart.EndsWith('\n')) Console.WriteLine();
        }

        _previousOutput = fullOutput;
        _session.Append(fragment);
    }

    private static void RenderDiagnostics(DiagnosticBag diags, int onlyAfter)
    {
        var useColor = !Console.IsErrorRedirected;
        foreach (var d in diags.All)
        {
            if (d.Span.Start < onlyAfter) continue;
            Console.Error.Write(d.Render(useColor));
        }
    }

    // -- Block-depth heuristic ------------------------------------------

    private static readonly Regex WordToken = new(@"\b[A-Za-z_]\w*\b", RegexOptions.Compiled);

    /// <summary>
    /// Estimate how much a single typed line shifts our block-nesting depth.
    /// Returns +1 for each block-opener and -1 for each block-closer it
    /// contains. Conservative on edge cases — when in doubt, assume a typed
    /// line is self-contained (delta 0) so the user can hit Enter to run it.
    /// </summary>
    private static int BlockDelta(string line)
    {
        // Strip string literals so words inside "..." don't count.
        var stripped = StripStrings(line);
        var matches = WordToken.Matches(stripped.ToUpperInvariant());
        var tokens = matches.Select(m => m.Value).ToList();
        var lastIndex = tokens.Count - 1;
        int delta = 0;

        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            switch (t)
            {
                case "FOR":
                    delta++;
                    break;

                case "DO":
                    delta++;
                    break;

                case "SELECT":
                    if (i + 1 < tokens.Count && tokens[i + 1] == "CASE") delta++;
                    break;

                case "MODULE":
                    if (i == 0 || tokens[i - 1] != "END") delta++;
                    break;

                case "WHEN":
                    if (i == 0 || tokens[i - 1] != "END") delta++;
                    break;

                case "HANDLER":
                    if (i == 0 || tokens[i - 1] != "END") delta++;
                    break;

                case "SUB":
                case "FUNCTION":
                    {
                        var prev = i > 0 ? tokens[i - 1] : "";
                        if (prev != "END" && prev != "EXIT" && prev != "CALL") delta++;
                        break;
                    }

                case "IF":
                    // Block IF only when THEN is the last significant token on the line.
                    if (lastIndex > i && tokens[lastIndex] == "THEN") delta++;
                    break;

                case "DEF":
                    // Block DEF when the line has no '=' on it (single-line uses '=').
                    if (!stripped.Contains('=')) delta++;
                    break;

                case "NEXT":
                case "LOOP":
                    delta--;
                    break;

                case "END":
                    // END followed by a block keyword closes that block.
                    // Bare END (= program terminator) doesn't.
                    if (i + 1 < tokens.Count)
                    {
                        var nxt = tokens[i + 1];
                        if (nxt is "IF" or "SELECT" or "SUB" or "FUNCTION"
                                or "DEF" or "MODULE" or "WHEN" or "HANDLER")
                        {
                            delta--;
                        }
                    }
                    break;
            }
        }

        return delta;
    }

    private static string StripStrings(string s)
    {
        var sb = new StringBuilder(s.Length);
        var inString = false;
        foreach (var c in s)
        {
            if (c == '"') { inString = !inString; continue; }
            sb.Append(inString ? ' ' : c);
        }
        return sb.ToString();
    }
}
