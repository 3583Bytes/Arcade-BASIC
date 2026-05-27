using FluentAssertions;
using ArcadeBasic.Bytecode;
using ArcadeBasic.Compiler;
using ArcadeBasic.Core;
using ArcadeBasic.Lexer;
using ArcadeBasic.Parser;
using ArcadeBasic.Sema;
using ArcadeBasic.Vm;

namespace ArcadeBasic.Vm.Tests;

/// <summary>Phase-10 bytecode serialization round-trip tests.</summary>
public class SerializationTests
{
    private static ArcadeBasic.Bytecode.Program Compile(string source)
    {
        var file = new SourceFile("test.bas", source);
        var diags = new DiagnosticBag();
        var tokens = new BasicLexer(file, diags).Lex();
        var program = new BasicParser(tokens, file, diags).ParseProgram();
        var info = Analyzer.Analyze(program, diags);
        return BasicCompiler.Compile(program, info);
    }

    private static string RunCompiled(ArcadeBasic.Bytecode.Program p)
    {
        var sw = new StringWriter();
        new BasicVm(p, sw, new StringReader("")).Run();
        return sw.ToString();
    }

    [Fact]
    public void RoundTripPreservesProgramOutput()
    {
        var source = """
            FUNCTION SQUARE(X)
              SQUARE = X * X
            END FUNCTION
            FOR I = 1 TO 5
              PRINT SQUARE(I)
            NEXT I
            """;
        var compiled = Compile(source);
        var serialized = BytecodeSerializer.Serialize(compiled);
        var deserialized = BytecodeSerializer.Deserialize(serialized);

        var direct = RunCompiled(compiled);
        var roundtripped = RunCompiled(deserialized);
        roundtripped.Should().Be(direct);
    }

    [Fact]
    public void RoundTripStringConstantsAndPi()
    {
        var compiled = Compile("PRINT \"hello\"\nPRINT PI");
        var bytes = BytecodeSerializer.Serialize(compiled);
        var back = BytecodeSerializer.Deserialize(bytes);
        RunCompiled(back).Should().Be(RunCompiled(compiled));
    }

    [Fact]
    public void TrailerFramingAppendAndDetect()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "fb-trailer-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(tmp, "this is a fake stub");
        try
        {
            var payload = new byte[] { 1, 2, 3, 4, 5, 0xFE, 0xFF };
            using (var fs = new FileStream(tmp, FileMode.Append))
            {
                EmbeddedPayload.Append(fs, payload);
            }
            var read = EmbeddedPayload.TryRead(tmp);
            read.Should().NotBeNull();
            read.Should().BeEquivalentTo(payload);
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void TryReadReturnsNullForFileWithoutTrailer()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "fb-notrailer-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(tmp, new byte[] { 0x7F, 0x45, 0x4C, 0x46 }); // ELF magic, no trailer
        try
        {
            EmbeddedPayload.TryRead(tmp).Should().BeNull();
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* best effort */ }
        }
    }
}
