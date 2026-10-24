using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Monolith.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Audio
{
    /// <summary>A built-in theme track: what the game ships for a theme name (music.json "themes": made by tools/audio from audio_src/compose/themes.py).</summary>
    public sealed class ThemeDef
    {
        public string Name, Track, Mode = "duck";
        public float FadeIn = 4f, FadeOut = 6f, Priority = 10f, DuckDb = -10f, VolumeDb;
    }

    /// <summary>The sound chosen for a slot: the clip, and the settings (the player's music.json over the built-in defaults) that say how it fades.</summary>
    public sealed class MusicChoice
    {
        public MusicTrackInfo Info;
        public MusicSlotConfig Cfg;            // the player's slot (null when they named none)
        public ThemeDef Def;                   // a built-in theme's defaults (themes only)
        public bool User => Cfg != null && Cfg.FullPath != null && Info != null && Info.Source != "built in";
        public string Name;
    }

    /// <summary>The player's music.json and the sounds it names, and the built-in themes.</summary>
    public sealed partial class AudioLibrary
    {
        public MusicConfig UserMusic;
        public readonly Dictionary<string, ThemeDef> ThemeDefs = new Dictionary<string, ThemeDef>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> MusicLog = new List<string>();
        float _nextMusicPoll;

        void ReadThemeCatalog(JObject j)
        {
            if (!(j["themes"] is JObject th)) return;
            foreach (var p in th.Properties())
            {
                var o = (JObject)p.Value;
                ThemeDefs[p.Name] = new ThemeDef
                {
                    Name = p.Name, Track = (string)o["track"] ?? ("theme_" + p.Name), FadeIn = (float?)o["fadeIn"] ?? 4f, FadeOut = (float?)o["fadeOut"] ?? 6f, Priority = (float?)o["priority"] ?? 10f,
                    Mode = (string)o["mode"] ?? "duck", DuckDb = (float?)o["duckDb"] ?? -10f, VolumeDb = (float?)o["volumeDb"] ?? 0f,
                };
            }
        }

        void LoadUserMusicConfig()
        {
            string path = AudioPaths.FindMusicConfig();
            if (path == null) { UserMusic = null; return; }
            UserMusic = MusicConfig.Load(path);
            var files = 0;
            foreach (var s in UserMusic.Slots.Values) if (s.FullPath != null && File.Exists(s.FullPath)) files++;
            string line = $"music.json {path}: {UserMusic.Slots.Count} slots, {files} of your files";
            MusicLog.Add(line); MnLog.Info("AUDIO " + line);
            foreach (var pr in UserMusic.Problems) { MusicLog.Add("WARN " + pr); MnLog.Warn("AUDIO " + pr); }
        }

        /// <summary>Called with the folder watch: a music.json that appeared, changed or went away is read again (it applies from the next music change).</summary>
        void PollMusicConfig(MonoBehaviour host)
        {
            string path = AudioPaths.FindMusicConfig();
            if (path == null) { if (UserMusic != null) { UserMusic = null; MusicLog.Add("music.json removed: the built-in score is back"); MnLog.Info("AUDIO music.json removed: the built-in score is back"); } return; }
            if (UserMusic != null && UserMusic.Path == path && UserMusic.Time == File.GetLastWriteTimeUtc(path)) return;
            LoadUserMusicConfig();
            PreloadUserMusic(host);
        }

        /// <summary>Start loading every file the music.json names, so a sting or a theme is ready when its moment comes. <paramref name="host"/> null: wait for them (the checks).</summary>
        void PreloadUserMusic(MonoBehaviour host)
        {
            if (UserMusic == null) return;
            foreach (var s in UserMusic.Slots.Values)
            {
                if (s.FullPath == null || !File.Exists(s.FullPath)) continue;
                if (host != null) { UserClipFor(s, host, out _); continue; }
                var t = File.GetLastWriteTimeUtc(s.FullPath);
                if (_userClips.TryGetValue(s.FullPath, out var have) && have.Time == t && !have.Loading) continue;
                var uc = _userClips[s.FullPath] = new UserClip { Loading = true, Time = t };
                RunNow(LoadUserClip(s, s.FullPath, uc));
            }
        }

        // ------------------------------------------------------------------------------------------------ the player's music files

        sealed class UserClip { public Clip Clip; public DateTime Time; public bool Loading, Failed; }
        readonly Dictionary<string, UserClip> _userClips = new Dictionary<string, UserClip>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The player's file for a slot: loaded on the first ask (null until it has), and again when the file changes. <paramref name="pending"/> while it loads.</summary>
        Clip UserClipFor(MusicSlotConfig cfg, MonoBehaviour host, out bool pending)
        {
            pending = false;
            string path = cfg.FullPath;
            if (path == null || !File.Exists(path)) return null;
            var t = File.GetLastWriteTimeUtc(path);
            if (_userClips.TryGetValue(path, out var uc))
            {
                if (uc.Loading) { pending = true; return null; }
                if (uc.Time == t) return uc.Clip;                                  // loaded (or failed: null)
            }
            else _userClips[path] = uc = new UserClip();
            uc.Loading = true; uc.Time = t; uc.Clip = null; uc.Failed = false;
            pending = true;
            host.StartCoroutine(LoadUserClip(cfg, path, uc));
            return null;
        }

        IEnumerator LoadUserClip(MusicSlotConfig cfg, string path, UserClip uc)
        {
            var r = new LoadResult();
            yield return AudioFileLoader.Load(path, Path.GetFileNameWithoutExtension(path), r);
            uc.Loading = false;
            if (r.Clip == null)
            {
                uc.Failed = true;
                string msg = $"music.json {cfg.Section}/{cfg.Key}: could not load {path}: {r.Error}; the built-in sound plays";
                MusicLog.Add("WARN " + msg); MnLog.Warn("AUDIO " + msg);
                yield break;
            }
            var clip = r.Clip;
            Shape(null, clip, path);                                                 // a sidecar .json beside the file may give loop points
            if (cfg.LoopEnd > 0f || cfg.LoopStart > 0f)
            {
                clip.LoopStart = Mathf.Clamp(Mathf.RoundToInt(cfg.LoopStart * clip.SampleRate), 0, clip.Frames - 1);
                clip.LoopEnd = cfg.LoopEnd > 0f ? Mathf.Clamp(Mathf.RoundToInt(cfg.LoopEnd * clip.SampleRate), clip.LoopStart + 1, clip.Frames) : 0;
            }
            uc.Clip = clip;
            string line = $"your music {cfg.Section}/{cfg.Key} <- {path} ({clip.Seconds:0.0} s at {clip.SampleRate} Hz)";
            MusicLog.Add(line); MnLog.Info("AUDIO " + line);
        }

        // ------------------------------------------------------------------------------------------------ choosing the sound for a slot

        /// <summary>
        /// The sound for a slot of the score: the player's music.json first (this mission's entry, then the general one), else the built-in track named <paramref name="builtInTrack"/>
        /// (which the Audio folder's own music\\name.mp3 may replace). <paramref name="pending"/> is true while a file of theirs is still loading (ask again in a moment).
        /// </summary>
        public MusicChoice ResolveMusic(string section, string mission, string key, string builtInTrack, MonoBehaviour host, out bool pending)
        {
            pending = false;
            var cfg = UserMusic?.Find(section, mission, key);
            if (cfg != null && cfg.FullPath != null)
            {
                var clip = UserClipFor(cfg, host, out pending);
                if (clip != null)
                {
                    var info = new MusicTrackInfo { Name = key, File = cfg.File, Source = cfg.FullPath, Clip = clip, Seconds = clip.Seconds, Bpm = cfg.Bpm, Loop = cfg.Loop ?? (section == "base" || section == "themes") };
                    return new MusicChoice { Info = info, Cfg = cfg, Name = key };
                }
                if (pending) return null;
                // the file is missing or would not load (said once in the log): the built-in sound plays with the player's fades and level
            }
            if (string.IsNullOrEmpty(builtInTrack)) return null;
            var built = FindMusic(builtInTrack, host, out bool bp);
            if (built == null) { pending = bp; return null; }
            return new MusicChoice { Info = built, Cfg = cfg, Name = builtInTrack };
        }

        /// <summary>The sound for a mission's theme: the player's entry ("escort/lector", then "lector"), else the built-in theme of that name; null when neither exists.</summary>
        public MusicChoice FindTheme(string mission, string name, MonoBehaviour host, out bool pending)
        {
            ThemeDefs.TryGetValue(name, out var def);
            var c = ResolveMusic("themes", mission, name, def != null ? def.Track : null, host, out pending);
            if (c != null) c.Def = def;
            return c;
        }
    }
}
