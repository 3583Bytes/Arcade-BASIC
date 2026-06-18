using System;
using System.Collections.Concurrent;
using System.Threading;

namespace ArcadeBasic.Runtime;

/// <summary>
/// Real-time <see cref="IAudioDevice"/>: renders each <see cref="ToneEvent"/> to
/// PCM and plays it through a platform <see cref="IPcmSink"/> on a background
/// worker thread.
///
/// Foreground/background (PLAY MF/MB, mirrored by SOUND) maps onto the device
/// seam: <see cref="Flush"/> drains — it blocks until every queued tone has
/// played — so an <c>MF</c> statement (which calls Flush via
/// <see cref="AudioState"/>) waits for completion, while an <c>MB</c> statement
/// returns immediately and the worker keeps playing. The queue is bounded to 32
/// items, matching GW-BASIC's background-music buffer: a program that races ahead
/// in <c>MB</c> blocks on <see cref="Emit"/> once 32 tones are outstanding.
///
/// The sink is created lazily on the first tone, so a program that never makes a
/// sound never opens the audio hardware. If the sink factory throws, or a write
/// fails mid-run, the device degrades to silence (it never throws from
/// <see cref="Emit"/>/<see cref="Flush"/>) and reports the failure once through the
/// optional <see cref="AudioDiagnostic"/> handler so the silence is explainable.
/// </summary>
public sealed class RealtimeAudioDevice : IAudioDevice, IDisposable
{
    private readonly BlockingCollection<WorkItem> _queue = new(boundedCapacity: 32);
    private readonly Thread _worker;
    private readonly Func<IPcmSink> _sinkFactory;
    private readonly AudioDiagnostic? _onDiagnostic;
    private volatile bool _disposed;

    /// <param name="sinkFactory">Opens the platform audio sink on the first tone.</param>
    /// <param name="onDiagnostic">Optional sink for non-fatal init/playback failures
    /// (the device degrades to silence regardless). Invoked on the worker thread.</param>
    public RealtimeAudioDevice(Func<IPcmSink> sinkFactory, AudioDiagnostic? onDiagnostic = null)
    {
        _sinkFactory = sinkFactory;
        _onDiagnostic = onDiagnostic;
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "arcade-basic-audio" };
        _worker.Start();
    }

    public void Emit(ToneEvent tone)
    {
        if (_disposed) return;
        // ObjectDisposedException derives from InvalidOperationException, so this
        // also covers a concurrent Dispose racing with Emit.
        try { _queue.Add(new WorkItem(PcmRenderer.Render(tone), null)); }
        catch (InvalidOperationException) { /* adding completed / disposed during shutdown */ }
    }

    public void Flush()
    {
        if (_disposed) return;
        using var done = new ManualResetEventSlim(false);
        try { _queue.Add(new WorkItem(null, done)); }
        catch (InvalidOperationException) { return; }
        done.Wait();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.CompleteAdding();
        _worker.Join();
        _queue.Dispose();
    }

    private void WorkerLoop()
    {
        IPcmSink? sink = null;
        var sinkTried = false;
        foreach (var item in _queue.GetConsumingEnumerable())
        {
            if (item.FlushDone is not null) { item.FlushDone.Set(); continue; }
            if (!sinkTried)
            {
                sinkTried = true;
                try { sink = _sinkFactory(); }
                catch (Exception ex)   // degrade to silence
                {
                    sink = null;
                    Report("audio output could not be initialized; continuing silently", ex);
                }
            }
            if (sink is null || item.Pcm is null) continue;
            try { sink.Write(item.Pcm); }
            catch (Exception ex)       // a mid-run failure → silence
            {
                Report("audio playback failed; continuing silently", ex);
                try { sink.Dispose(); } catch { }
                sink = null;
            }
        }
        try { sink?.Dispose(); } catch { }
    }

    /// <summary>Hand a non-fatal failure to the diagnostic sink, if any. Never lets
    /// a misbehaving handler take down the worker thread.</summary>
    private void Report(string message, Exception? error)
    {
        var handler = _onDiagnostic;
        if (handler is null) return;
        try { handler(message, error); } catch { /* a diagnostic sink must never break playback */ }
    }

    private readonly struct WorkItem(short[]? pcm, ManualResetEventSlim? flushDone)
    {
        public readonly short[]? Pcm = pcm;
        public readonly ManualResetEventSlim? FlushDone = flushDone;
    }
}
