using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using ArcadeBasic.Runtime;

namespace ArcadeBasic.Cli.Audio;

/// <summary>
/// macOS real-time PCM sink via AudioToolbox <c>AudioQueue</c>. Each write
/// allocates a queue buffer, enqueues it, and blocks for the buffer's duration to
/// pace foreground playback (the output callback is a no-op; buffers are freed
/// after they have played).
///
/// NOT yet verified on real hardware — written to the documented AudioQueue API.
/// Any failure throws from the constructor or a write and the caller falls back to
/// silence. See docs/audio-extension.md.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class CoreAudioPcmSink : IPcmSink
{
    private const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    private const uint kAudioFormatLinearPCM = 0x6C70636D;   // 'lpcm'
    private const uint kLinearPCMFormatFlagIsSignedInteger = 0x4;
    private const uint kLinearPCMFormatFlagIsPacked = 0x8;

    // Keep the marshalled callback alive for the lifetime of the queue.
    private static readonly AudioQueueOutputCallback s_callback = OnBufferDone;
    private static readonly IntPtr s_callbackPtr = Marshal.GetFunctionPointerForDelegate(s_callback);

    private IntPtr _queue;
    private bool _started;

    public CoreAudioPcmSink()
    {
        var fmt = new AudioStreamBasicDescription
        {
            mSampleRate = PcmRenderer.SampleRate,
            mFormatID = kAudioFormatLinearPCM,
            mFormatFlags = kLinearPCMFormatFlagIsSignedInteger | kLinearPCMFormatFlagIsPacked,
            mBytesPerPacket = 2,
            mFramesPerPacket = 1,
            mBytesPerFrame = 2,
            mChannelsPerFrame = 1,
            mBitsPerChannel = 16,
            mReserved = 0,
        };
        if (AudioQueueNewOutput(ref fmt, s_callbackPtr, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out _queue) != 0
            || _queue == IntPtr.Zero)
            throw new InvalidOperationException("AudioQueueNewOutput failed");
    }

    public void Write(short[] samples)
    {
        if (_queue == IntPtr.Zero || samples.Length == 0) return;
        var byteSize = samples.Length * 2;
        if (AudioQueueAllocateBuffer(_queue, (uint)byteSize, out var buf) != 0 || buf == IntPtr.Zero) return;

        // AudioQueueBuffer layout (64-bit): [0]=mAudioDataBytesCapacity(uint),
        // [8]=mAudioData(ptr), [16]=mAudioDataByteSize(uint).
        var dataPtr = Marshal.ReadIntPtr(buf, 8);
        Marshal.Copy(samples, 0, dataPtr, samples.Length);
        Marshal.WriteInt32(buf, 16, byteSize);

        if (AudioQueueEnqueueBuffer(_queue, buf, 0, IntPtr.Zero) != 0) { AudioQueueFreeBuffer(_queue, buf); return; }
        if (!_started) { AudioQueueStart(_queue, IntPtr.Zero); _started = true; }

        // Pace: block for the buffer's real-time duration, then reclaim it.
        var ms = (int)(samples.Length * 1000.0 / PcmRenderer.SampleRate);
        Thread.Sleep(ms);
        AudioQueueFreeBuffer(_queue, buf);
    }

    public void Dispose()
    {
        if (_queue == IntPtr.Zero) return;
        try { AudioQueueStop(_queue, true); AudioQueueDispose(_queue, true); } catch { }
        _queue = IntPtr.Zero;
    }

    private static void OnBufferDone(IntPtr userData, IntPtr aq, IntPtr buffer) { }

    private delegate void AudioQueueOutputCallback(IntPtr userData, IntPtr aq, IntPtr buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioStreamBasicDescription
    {
        public double mSampleRate;
        public uint mFormatID;
        public uint mFormatFlags;
        public uint mBytesPerPacket;
        public uint mFramesPerPacket;
        public uint mBytesPerFrame;
        public uint mChannelsPerFrame;
        public uint mBitsPerChannel;
        public uint mReserved;
    }

    [DllImport(AudioToolbox)] private static extern int AudioQueueNewOutput(
        ref AudioStreamBasicDescription fmt, IntPtr callback, IntPtr userData,
        IntPtr runLoop, IntPtr runLoopMode, uint flags, out IntPtr queue);
    [DllImport(AudioToolbox)] private static extern int AudioQueueAllocateBuffer(IntPtr queue, uint size, out IntPtr buffer);
    [DllImport(AudioToolbox)] private static extern int AudioQueueEnqueueBuffer(IntPtr queue, IntPtr buffer, uint nPackets, IntPtr packetDescs);
    [DllImport(AudioToolbox)] private static extern int AudioQueueStart(IntPtr queue, IntPtr startTime);
    [DllImport(AudioToolbox)] private static extern int AudioQueueStop(IntPtr queue, bool immediate);
    [DllImport(AudioToolbox)] private static extern int AudioQueueFreeBuffer(IntPtr queue, IntPtr buffer);
    [DllImport(AudioToolbox)] private static extern int AudioQueueDispose(IntPtr queue, bool immediate);
}
