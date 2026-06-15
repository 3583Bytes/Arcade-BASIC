using ArcadeBasic.Runtime;    // IAudioDevice, BufferedAudioDevice, PcmRenderer (from the shipped plugin DLLs)
using UnityEngine;

namespace ArcadeBasic.Unity
{
    /// <summary>
    /// Plays an Arcade BASIC program's audio (SOUND / BEEP / PLAY) through Unity.
    /// The engine-agnostic <see cref="BufferedAudioDevice"/> (unit-tested, no
    /// UnityEngine dependency) renders tones to PCM on the BASIC thread; this
    /// component streams them out through a looping <see cref="AudioClip"/> whose
    /// PCM-reader callback pulls from that buffer on Unity's audio thread. It is
    /// the audio analogue of <see cref="BasicScreen"/>.
    ///
    /// Usage: add the component, call <see cref="BeginRun"/> to get an
    /// <c>IAudioDevice</c> to pass to <c>BasicEngine.Run(..., audio: device)</c>
    /// (typically on a background thread), and <see cref="EndRun"/> when the
    /// program stops. A fresh device per run means leftover audio never bleeds
    /// across runs.
    /// </summary>
    [AddComponentMenu("Arcade BASIC/Basic Audio")]
    [RequireComponent(typeof(AudioSource))]
    public sealed class BasicAudioOutput : MonoBehaviour
    {
        private AudioSource _source;
        private volatile BufferedAudioDevice _device;   // current run's device, or null between runs

        private void Awake()
        {
            int rate = PcmRenderer.SampleRate;
            // A 1-second looping streaming clip: Unity calls OnAudioRead to fill it.
            var clip = AudioClip.Create("ArcadeBasicAudio", rate, 1, rate, stream: true, OnAudioRead);
            _source = GetComponent<AudioSource>();
            _source.clip = clip;
            _source.loop = true;
            _source.spatialBlend = 0f;     // 2D
            _source.playOnAwake = false;
            _source.Play();                // stream continuously (silence when idle)
        }

        /// <summary>Start a fresh run; returns the device to inject into the engine.</summary>
        public IAudioDevice BeginRun()
        {
            var dev = new BufferedAudioDevice();
            _device = dev;
            return dev;
        }

        /// <summary>End the current run: go silent and release any program thread
        /// blocked waiting for foreground audio to finish.</summary>
        public void EndRun()
        {
            var dev = _device;
            _device = null;
            dev?.Close();
        }

        // Unity audio thread: pull the next block from the current run's buffer.
        private void OnAudioRead(float[] data)
        {
            var dev = _device;
            if (dev != null) dev.Read(data);
            else System.Array.Clear(data, 0, data.Length);
        }

        private void OnDestroy()
        {
            if (_source != null) _source.Stop();
            EndRun();
        }
    }
}
