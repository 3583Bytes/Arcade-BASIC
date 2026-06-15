using System.Collections.Generic;
using System.IO;
using System.Text;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Cli;

/// <summary>
/// A headless <see cref="IAudioDevice"/> that renders the tone-event stream to a
/// PCM WAV file: 44.1 kHz, 16-bit, mono, square wave (most faithful to the PC
/// speaker). It is the audio analogue of <see cref="SvgGraphicsDevice"/> — the
/// deterministic, dependency-free Phase-1 backend, identical across the
/// interpreter and the VM. Real-time audible playback is a later phase.
/// </summary>
public sealed class WavAudioDevice : IAudioDevice
{
    private const int SampleRate = PcmRenderer.SampleRate;

    private readonly List<short> _samples = new();

    public void Emit(ToneEvent tone) => _samples.AddRange(PcmRenderer.Render(tone));

    public void Flush() { }

    /// <summary>Serialize the accumulated samples to a complete WAV file.</summary>
    public byte[] ToBytes()
    {
        var dataLen = _samples.Count * 2;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        // RIFF / WAVE header
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataLen);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        // fmt chunk (PCM)
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);                  // chunk size
        w.Write((short)1);            // audio format: PCM
        w.Write((short)1);            // channels: mono
        w.Write(SampleRate);
        w.Write(SampleRate * 2);      // byte rate (mono * 16-bit)
        w.Write((short)2);            // block align
        w.Write((short)16);           // bits per sample
        // data chunk
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(dataLen);
        foreach (var s in _samples) w.Write(s);
        w.Flush();
        return ms.ToArray();
    }
}
