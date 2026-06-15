using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Cli.Audio;

/// <summary>
/// Windows real-time PCM sink via the winmm <c>waveOut</c> API. Plays one buffer
/// at a time and blocks until it finishes (polling <c>WHDR_DONE</c>), which paces
/// foreground playback in real time. P/Invoke with blittable structs, so it is
/// NativeAOT-safe.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WinmmPcmSink : IPcmSink
{
    private const int MMSYSERR_NOERROR = 0;
    private const uint WAVE_MAPPER = 0xFFFFFFFF;
    private const uint WHDR_DONE = 0x00000001;

    private IntPtr _h;

    public WinmmPcmSink()
    {
        var fmt = new WAVEFORMATEX
        {
            wFormatTag = 1,                                   // WAVE_FORMAT_PCM
            nChannels = 1,
            nSamplesPerSec = (uint)PcmRenderer.SampleRate,
            wBitsPerSample = 16,
            nBlockAlign = 2,
            nAvgBytesPerSec = (uint)(PcmRenderer.SampleRate * 2),
            cbSize = 0,
        };
        if (waveOutOpen(out _h, WAVE_MAPPER, ref fmt, IntPtr.Zero, IntPtr.Zero, 0) != MMSYSERR_NOERROR)
            throw new InvalidOperationException("waveOutOpen failed");
    }

    public void Write(short[] samples)
    {
        if (_h == IntPtr.Zero || samples.Length == 0) return;
        var handle = GCHandle.Alloc(samples, GCHandleType.Pinned);
        var hdr = new WAVEHDR
        {
            lpData = handle.AddrOfPinnedObject(),
            dwBufferLength = (uint)(samples.Length * 2),
        };
        var size = (uint)Marshal.SizeOf<WAVEHDR>();
        try
        {
            if (waveOutPrepareHeader(_h, ref hdr, size) != MMSYSERR_NOERROR) return;
            if (waveOutWrite(_h, ref hdr, size) != MMSYSERR_NOERROR) return;
            while ((hdr.dwFlags & WHDR_DONE) == 0) Thread.Sleep(1);   // block until played
            waveOutUnprepareHeader(_h, ref hdr, size);
        }
        finally
        {
            handle.Free();
        }
    }

    public void Dispose()
    {
        if (_h == IntPtr.Zero) return;
        try { waveOutReset(_h); waveOutClose(_h); } catch { }
        _h = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    [DllImport("winmm.dll")] private static extern int waveOutOpen(
        out IntPtr h, uint deviceId, ref WAVEFORMATEX fmt, IntPtr cb, IntPtr inst, uint flags);
    [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr h, ref WAVEHDR hdr, uint size);
    [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr h, ref WAVEHDR hdr, uint size);
    [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr h, ref WAVEHDR hdr, uint size);
    [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr h);
    [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr h);
}
