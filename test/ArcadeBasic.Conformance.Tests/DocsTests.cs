using System.IO.Compression;
using System.Text;
using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;
using ArcadeBasic.Compiler;

namespace ArcadeBasic.Conformance.Tests;

/// <summary>
/// The BASIC examples in the docs (README.md and docs/*.md), so they can't drift from the
/// language: each whole program among them compiles, and a "▶ Try it" link under one
/// opens exactly that program in the IDE (tools/docs/try-links.mjs writes them). An
/// example marked &lt;!-- fragment --&gt; on the line before it isn't a whole program —
/// it jumps to lines it doesn't have, say — and is left alone.
/// </summary>
public class DocsTests
{
    /// <summary>(file, line of its first line of code, the code, the #code= of the link under it or "").</summary>
    public static IEnumerable<object[]> Examples()
    {
        var root = RepoDir();
        var files = Directory.GetFiles(Path.Combine(root, "docs"), "*.md").Append(Path.Combine(root, "README.md")).OrderBy(f => f);
        foreach (var path in files)
        {
            var name = Path.GetRelativePath(root, path).Replace('\\', '/');
            var lines = File.ReadAllText(path).Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() != "```basic") continue;
                var end = i + 1;
                while (lines[end].Trim() != "```") end++;
                if (i == 0 || lines[i - 1].Trim() != "<!-- fragment -->")
                {
                    var code = string.Join("\n", lines[(i + 1)..end]) + "\n";
                    var after = end + 1 < lines.Length ? lines[end + 1] : "";
                    var link = after.Contains("#code=") ? after[(after.IndexOf("#code=") + 6)..].TrimEnd(')', '*', ' ') : "";
                    yield return [name, i + 2, code, link];
                }
                i = end;
            }
        }
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void EachExampleCompiles(string file, int line, string code, string link)
    {
        _ = link;
        var source = new SourceFile($"{file}:{line}", code);
        var diags = new DiagnosticBag();
        var program = new BasicParser(new BasicLexer(source, diags).Lex(), source, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        Assert.False(diags.HasErrors,
            $"The example at {file}:{line} doesn't compile (mark it <!-- fragment --> if it isn't meant to be a whole program):\n" +
            string.Join("\n", diags.All.Select(d => d.Render(false))));
        BasicCompiler.Compile(program, info);
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void EachTryItLinkOpensItsExample(string file, int line, string code, string link)
    {
        if (link == "") return;
        Assert.True(Unpack(link) == code,
            $"The link under the example at {file}:{line} opens something else: run `node tools/docs/try-links.mjs`");
    }

    /// <summary>Every whole program in the language reference has a link to try it.</summary>
    [Fact]
    public void EveryExampleInTheReferenceHasATryItLink()
    {
        var missing = Examples().Where(e => (string)e[0] == "docs/keywords.md" && (string)e[3] == "").Select(e => e[1]).ToList();
        Assert.True(missing.Count == 0,
            $"Examples in docs/keywords.md without a ▶ Try it link (lines {string.Join(", ", missing)}): run `node tools/docs/try-links.mjs`");
    }

    /// <summary>The program a #code= link carries: base64url, then raw deflate (the IDE's unpack).</summary>
    private static string Unpack(string code)
    {
        var base64 = code.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        using var inflate = new DeflateStream(new MemoryStream(Convert.FromBase64String(base64)), CompressionMode.Decompress);
        using var reader = new StreamReader(inflate, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string RepoDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "docs"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new DirectoryNotFoundException("docs");
    }
}
