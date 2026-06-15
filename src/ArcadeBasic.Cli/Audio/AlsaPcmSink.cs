using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Cli.Audio;

/// <summary>
/// Linux real-time PCM sink via ALSA (<c>libasound</c>). <c>snd_pcm_writei</c> is
/// a blocking interleaved write, which paces foreground playback in real time.
///
/// NOT yet verified on real hardware — written to the documented ALSA API. Any
/// failure (no ALSA, no device) throws from the constructor or a write and the
/// caller falls back to silence. See docs/audio-extension.md.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class AlsaPcmSink : IPcmSink
{
    private const int SND_PCM_STREAM_PLAYBACK = 0;
    private const int SND_PCM_FORMAT_S16_LE = 2;
    private const int SND_PCM_ACCESS_RW_INTERLEAVED = 3;
    private const int EPIPE = 32;   // underrun

    private IntPtr _pcm;

    public AlsaPcmSink()
    {
        if (snd_pcm_open(out _pcm, "default", SND_PCM_STREAM_PLAYBACK, 0) < 0 || _pcm == IntPtr.Zero)
            throw new InvalidOperationException("snd_pcm_open failed");
        // soft_resample = 1; latency ~0.5 s.
        if (snd_pcm_set_params(_pcm, SND_PCM_FORMAT_S16_LE, SND_PCM_ACCESS_RW_INTERLEAVED,
                1, (uint)PcmRenderer.SampleRate, 1, 500_000) < 0)
            throw new InvalidOperationException("snd_pcm_set_params failed");
    }

    public void Write(short[] samples)
    {
        if (_pcm == IntPtr.Zero || samples.Length == 0) return;
        var frames = samples.Length;          // mono: 1 sample == 1 frame
        var written = snd_pcm_writei(_pcm, samples, (ulong)frames);
        if (written < 0)
            snd_pcm_recover(_pcm, (int)written, 1);   // recover from underrun and drop this buffer
    }

    public void Dispose()
    {
        if (_pcm == IntPtr.Zero) return;
        try { snd_pcm_drain(_pcm); snd_pcm_close(_pcm); } catch { }
        _pcm = IntPtr.Zero;
    }

    [DllImport("libasound.so.2")] private static extern int snd_pcm_open(out IntPtr pcm, string name, int stream, int mode);
    [DllImport("libasound.so.2")] private static extern int snd_pcm_set_params(
        IntPtr pcm, int format, int access, uint channels, uint rate, int softResample, uint latency);
    [DllImport("libasound.so.2")] private static extern long snd_pcm_writei(IntPtr pcm, short[] buffer, ulong size);
    [DllImport("libasound.so.2")] private static extern int snd_pcm_recover(IntPtr pcm, int err, int silent);
    [DllImport("libasound.so.2")] private static extern int snd_pcm_drain(IntPtr pcm);
    [DllImport("libasound.so.2")] private static extern int snd_pcm_close(IntPtr pcm);
}
