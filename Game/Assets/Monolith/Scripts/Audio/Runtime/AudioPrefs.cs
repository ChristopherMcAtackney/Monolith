using System;
using System.IO;
using Monolith.Core;
using Newtonsoft.Json;

namespace Monolith.Audio
{
    /// <summary>The player's volume settings (master, music, effects, voice, mute), saved as audio.json beside the game when they change.</summary>
    [Serializable]
    public sealed class AudioPrefs
    {
        public float master = 1.0f, music = 0.75f, effects = 1.0f, voice = 1.0f;
        public bool mute;

        [JsonIgnore] public bool Dirty;
        [JsonIgnore] public string Source = "defaults";

        /// <summary>Slider position (0..1) to a gain: squared, so the middle of the slider is about 12 dB down, like a fader.</summary>
        public static float Curve(float v) => v * v;

        public static AudioPrefs Load(string path = null)
        {
            path ??= AudioPaths.SettingsFile;
            try
            {
                if (File.Exists(path))
                {
                    var p = JsonConvert.DeserializeObject<AudioPrefs>(File.ReadAllText(path)) ?? new AudioPrefs();
                    p.Clamp();
                    p.Source = path;
                    return p;
                }
            }
            catch (Exception e) { MnLog.Warn("AUDIO settings unreadable (" + path + "): " + e.Message); }
            return new AudioPrefs();
        }

        public void Clamp()
        {
            master = Clamp01(master); music = Clamp01(music); effects = Clamp01(effects); voice = Clamp01(voice);
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        public void Save(string path = null)
        {
            path ??= AudioPaths.SettingsFile;
            try { File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented)); Dirty = false; Source = path; }
            catch (Exception e) { MnLog.Warn("AUDIO settings not saved: " + e.Message); }
        }
    }
}
