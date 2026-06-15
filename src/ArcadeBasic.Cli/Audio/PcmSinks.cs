using System;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Cli.Audio;

/// <summary>
/// Picks the real-time PCM sink for the current OS. Any initialization failure
/// (no audio device, missing system library) falls back to
/// <see cref="SilentPcmSink"/> so audio never crashes a run — it just goes quiet.
///
/// Verification status: the Windows (winmm) backend is exercised on Windows; the
/// Linux (ALSA) and macOS (CoreAudio/AudioQueue) backends are written to the
/// platform APIs but have NOT been verified on real hardware — see
/// docs/audio-extension.md. `--wav` is the deterministic, universal path.
/// </summary>
public static class PcmSinks
{
    public static IPcmSink CreateDefault()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return new WinmmPcmSink();
            if (OperatingSystem.IsLinux()) return new AlsaPcmSink();
            if (OperatingSystem.IsMacOS()) return new CoreAudioPcmSink();
        }
        catch { /* fall through to silence */ }
        return new SilentPcmSink();
    }
}
