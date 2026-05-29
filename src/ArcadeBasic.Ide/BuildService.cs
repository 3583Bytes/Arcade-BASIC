using ArcadeBasic.Bytecode;

namespace ArcadeBasic.Ide;

/// <summary>
/// Wraps the standalone-binary build flow used by the CLI's `build` subcommand,
/// in-process. Compile result + a located <c>arcade-basic</c> AOT stub →
/// one self-contained executable with the bytecode payload appended via the
/// same <see cref="EmbeddedPayload"/> framing the CLI uses at startup.
///
/// The IDE itself is a self-contained single-file binary (not AOT'd —
/// Terminal.Gui v1 isn't AOT-clean), so it can't use Environment.ProcessPath
/// as the stub the way the CLI does. We locate an external <c>arcade-basic</c>
/// AOT binary instead: next to the IDE binary first, then on PATH.
/// </summary>
internal static class BuildService
{
    public sealed record Result(bool Ok, string Message, long? PayloadBytes = null, long? OutputBytes = null);

    /// <summary>Write <paramref name="program"/> to <paramref name="outputPath"/>
    /// as a self-contained executable. Returns a status describing what happened.</summary>
    public static Result Build(ArcadeBasic.Bytecode.Program program, string outputPath, string? stubPathOverride = null)
    {
        var stub = stubPathOverride ?? LocateStub();
        if (stub is null)
        {
            return new Result(false,
                "Could not find an `arcade-basic` AOT binary to use as the build stub. " +
                "Place one next to arcade-basic-ide or add it to PATH.");
        }
        if (!File.Exists(stub))
        {
            return new Result(false, $"Stub binary does not exist: {stub}");
        }

        byte[] payload;
        try
        {
            payload = BytecodeSerializer.Serialize(program);
        }
        catch (Exception ex)
        {
            return new Result(false, "serialize failed: " + ex.Message);
        }

        try
        {
            var stubBytes = File.ReadAllBytes(stub);

            // If the located stub itself has an embedded payload (e.g. someone
            // pointed at an already-built binary), strip it so we don't grow
            // the binary on each rebuild — same idea as the CLI's build.
            var existing = EmbeddedPayload.TryRead(stub);
            if (existing is not null)
            {
                var strip = existing.Length + 12;
                Array.Resize(ref stubBytes, stubBytes.Length - strip);
            }

            using (var fs = File.Create(outputPath))
            {
                fs.Write(stubBytes, 0, stubBytes.Length);
                EmbeddedPayload.Append(fs, payload);
            }

            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(outputPath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
                catch { /* best effort */ }
            }

            var outBytes = new FileInfo(outputPath).Length;
            return new Result(true, $"wrote {outputPath} ({outBytes:N0} bytes, payload {payload.Length:N0})", payload.Length, outBytes);
        }
        catch (Exception ex)
        {
            return new Result(false, "build failed: " + ex.Message);
        }
    }

    /// <summary>Best-effort: find an <c>arcade-basic</c> binary to use as the
    /// AOT stub. Looks next to the IDE binary first, then on PATH. Returns
    /// <c>null</c> if nothing plausible was found.</summary>
    public static string? LocateStub()
    {
        var exe = OperatingSystem.IsWindows() ? "arcade-basic.exe" : "arcade-basic";

        // 1. Same directory as the running IDE binary — works when the IDE
        // and CLI ship together (e.g. unzipped from the release tarball).
        var ideDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrEmpty(ideDir))
        {
            var candidate = Path.Combine(ideDir, exe);
            if (File.Exists(candidate)) return candidate;
        }

        // 2. Walk PATH.
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var candidate = Path.Combine(dir, exe);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }
}
