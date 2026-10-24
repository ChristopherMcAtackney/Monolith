using UnityEngine;

namespace Monolith.Audio
{
    /// <summary>
    /// The bridge to the sound card: Unity calls <see cref="OnAudioFilterRead"/> on its audio thread for a silent looping source, and the
    /// mixer fills the buffer. Nothing else in the game uses Unity's AudioSources, so everything that is heard went through the mixer.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class AudioOut : MonoBehaviour
    {
        public Mixer Mixer;
        volatile bool _mixerReady;

        public void Begin(Mixer mixer)
        {
            Mixer = mixer;
            _mixerReady = true;
            var src = GetComponent<AudioSource>();
            src.clip = AudioClip.Create("mn_out", 1024, 1, AudioSettings.outputSampleRate, false);
            src.loop = true;
            src.spatialBlend = 0f;
            src.volume = 1f;
            src.playOnAwake = false;
            src.ignoreListenerPause = true;
            src.Play();
        }

        /// <summary>The audio thread's load: how long a block took to mix against the time it plays for (a share, 1 = the whole of it).</summary>
        public volatile float LoadMax, LoadAvg;
        public volatile int Callbacks;
        readonly System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();

        void OnAudioFilterRead(float[] data, int channels)
        {
            var m = Mixer;
            if (!_mixerReady || m == null) { System.Array.Clear(data, 0, data.Length); return; }
            int frames = data.Length / channels;
            _sw.Restart();
            m.Render(data, frames, channels);
            _sw.Stop();
            float load = (float)(_sw.Elapsed.TotalSeconds / (frames / (double)m.SampleRate));
            if (load > LoadMax) LoadMax = load;
            LoadAvg += (load - LoadAvg) * 0.02f;
            Callbacks++;
        }

        public void ResetLoad() { LoadMax = 0f; }
    }
}
