using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Monolith.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Monolith.Audio
{
    /// <summary>Where the sound lives: the built-in files (StreamingAssets/Audio, shipped with the game) and the player's own (an Audio folder beside the game).</summary>
    public static class AudioPaths
    {
        static string _user;

        /// <summary>The built-in sounds: music, effects and radio voices, as made by tools/audio.</summary>
        public static string BuiltIn => Path.Combine(Application.streamingAssetsPath, "Audio");

        /// <summary>Beside the game (Monolith.exe), so a player can drop MP3, WAV or OGG files there. Override with -mnAudioUser dir (tests).</summary>
        public static string User
        {
            get
            {
                if (_user != null) return _user;
                var flag = CommandLine.Get("-mnAudioUser");
                if (!string.IsNullOrEmpty(flag)) return _user = flag;
                return _user = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Audio");
            }
        }

        /// <summary>Point the player's Audio folder somewhere else (the checks use a scratch folder).</summary>
        public static void OverrideUser(string dir) { _user = dir; }

        public static string SettingsFile => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "audio.json");

        public static readonly string[] Extensions = { ".wav", ".ogg", ".mp3" };

        /// <summary>Make the Audio folder beside the game, with a note on what goes in it (once; never in captures).</summary>
        public static void EnsureUserFolder()
        {
            try
            {
                Directory.CreateDirectory(User);
                string readme = Path.Combine(User, "README.txt");
                if (!File.Exists(readme))
                    File.WriteAllText(readme,
                        "MONOLITH - YOUR OWN SOUND\r\n\r\n" +
                        "Drop MP3, WAV or OGG files in this folder (or the folders inside it) and they play instead of the built-in sound.\r\n" +
                        "No rebuild, no restart needed: save a file and it is picked up within a couple of seconds; delete it and the built-in sound comes back.\r\n\r\n" +
                        "Name a file like the one it replaces, without caring about the extension:\r\n" +
                        "  music\\ambient.mp3  battle1  battle2  battle3  victory  failure  death  (and sting_friendly, sting_friendly_battle, sting_enemy,\r\n" +
                        "                      sting_enemy_battle, sting_goal, sting_fail)\r\n" +
                        "  sfx\\laser_light_0.wav ... (the list is in AUDIO.txt next to the game)\r\n" +
                        "  vo\\<voice>\\<line>.wav  (every radio line; vo\\VOICE_LINES.txt lists them with their text)\r\n" +
                        "A mission can name its own tracks (\"music\": { \"tracks\": { \"battle2\": \"oort_battle_2\" } }): put music\\oort_battle_2.mp3 here.\r\n\r\n" +
                        "Music: keep a loop to 44.1 or 48 kHz, stereo. To loop inside a file, put a file of the same name and .json beside it:\r\n" +
                        "  { \"loopStart\": 3.2, \"loopEnd\": 48.0, \"volume\": -2.5 }     (seconds, and a trim in dB)\r\n" +
                        "MP3 files carry encoder padding at the start; WAV or OGG loop more cleanly. Your loops cross-fade on the bar of the built-in tempo (112 BPM).\r\n" +
                        "Voice: your recordings are put through the game's radio filter (band-limiting, squelch, the reverb of Earth's voices). If a recording is\r\n" +
                        "already radio-ready, add a .json beside it with { \"radio\": false }.\r\n" +
                        "The log (Monolith_Data\\output_log.txt or the player log) lists every override as [MN] AUDIO override ...\r\n");
            }
            catch (Exception e) { MnLog.Warn("AUDIO could not make the Audio folder: " + e.Message); }
        }

        /// <summary>The player's replacement for a built-in file (same relative path, any of the three extensions), or null.</summary>
        public static string FindUser(string relative)
        {
            string noExt = Path.ChangeExtension(relative, null);
            foreach (var e in Extensions)
            {
                string p = Path.Combine(User, noExt.Replace('/', Path.DirectorySeparatorChar) + e);
                if (File.Exists(p)) return p;
            }
            return null;
        }
    }

    /// <summary>The result of loading one sound file.</summary>
    public sealed class LoadResult
    {
        public Clip Clip;
        public string Error;
        public bool Done;
    }

    /// <summary>Loads WAV by hand and OGG/MP3 through Unity's decoder, into the mixer's own 16-bit clips.</summary>
    public static class AudioFileLoader
    {
        /// <summary>While true, decoding waits for the request instead of yielding (captures load everything before the first tick; local files only).</summary>
        public static bool Blocking;

        public static IEnumerator Load(string path, string name, LoadResult result)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".wav")
            {
                try { result.Clip = Clip.FromPcm(Wav.Read(path), name); }
                catch (Exception e) { result.Error = e.Message; }
                result.Done = true;
                yield break;
            }
            AudioType type = ext == ".ogg" ? AudioType.OGGVORBIS : ext == ".mp3" ? AudioType.MPEG : AudioType.UNKNOWN;
            string url;
            try { url = new Uri(path).AbsoluteUri; }
            catch (Exception e) { result.Error = e.Message; result.Done = true; yield break; }
            using (var req = UnityWebRequestMultimedia.GetAudioClip(url, type))
            {
                var op = req.SendWebRequest();
                if (Blocking) { var t0 = System.Diagnostics.Stopwatch.StartNew(); while (!op.isDone && t0.ElapsedMilliseconds < 20000) System.Threading.Thread.Sleep(1); }
                else yield return op;
                if (req.result != UnityWebRequest.Result.Success) { result.Error = req.error; result.Done = true; yield break; }
                AudioClip ac = null;
                try { ac = DownloadHandlerAudioClip.GetContent(req); }
                catch (Exception e) { result.Error = e.Message; result.Done = true; yield break; }
                if (ac == null) { result.Error = "no clip decoded"; result.Done = true; yield break; }
                float guard = 0f;
                if (Blocking) { var t1 = System.Diagnostics.Stopwatch.StartNew(); while (ac.loadState == AudioDataLoadState.Loading && t1.ElapsedMilliseconds < 20000) System.Threading.Thread.Sleep(1); }
                else while (ac.loadState == AudioDataLoadState.Loading && guard < 20f) { guard += Time.unscaledDeltaTime; yield return null; }
                if (ac.loadState != AudioDataLoadState.Loaded || ac.samples <= 0) { result.Error = "decode failed (" + ac.loadState + ")"; UnityEngine.Object.Destroy(ac); result.Done = true; yield break; }
                var data = new float[ac.samples * ac.channels];
                if (!ac.GetData(data, 0)) { result.Error = "GetData failed"; UnityEngine.Object.Destroy(ac); result.Done = true; yield break; }
                result.Clip = Clip.FromInterleaved(data, ac.channels, ac.frequency, name);
                UnityEngine.Object.Destroy(ac);
                result.Done = true;
            }
        }
    }
}
