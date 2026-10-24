using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Monolith.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Audio
{
    /// <summary>One sound effect as the game plays it (from sfx.json, made by tools/audio).</summary>
    public sealed class SfxCue
    {
        public string Id, Bus = "sfx", Note;
        public Clip[] Near = new Clip[0], Far = new Clip[0];
        public float LevelDb = -12f, MinDist = 60f, MaxDist = 9000f, NearFadeFrom = 250f, NearFadeTo = 1500f, PitchVar = 0.03f, DuckAmount, DuckSeconds, Priority = 1f, ArrivalDelay;
        public bool Loop, Spatial = true;
        public bool Ready => Near.Length > 0 && Near[0] != null;
    }

    public sealed class MusicTrackInfo
    {
        public string Name, File, Source = "built in";
        public Clip Clip;
        public bool Loop = true;
        public float Seconds, Lufs, Bpm = 112f;
        public int Bars;
        /// <summary>For a long track that plays through and loops (the fight theme): its level in dB, where the first pass starts, the loop body and the cross-fade across the seam (seconds), and how much louder or quieter it is at each battle intensity.</summary>
        public float VolumeDb, StartAt, LoopStart, LoopEnd, LoopFade;
        public float[] IntensityDb = { 0f, 0f, 0f };
    }

    public sealed class VoiceClip
    {
        public string Voice, Text, Norm, File, Source = "built in";
        public Clip Clip;
        public float Seconds;
        /// <summary>How hard the speaker is working: 0 calm, 1 tense, 2 frantic (the index tags it; collect_lines.py decides from the mission context).</summary>
        public int Intensity;
        public float Lufs;
        public static int ParseIntensity(string s) => s == "frantic" ? 2 : s == "tense" ? 1 : 0;
    }

    /// <summary>Who speaks as whom (cast.json, copied into the index).</summary>
    public sealed class SpeakerMap
    {
        public readonly Dictionary<string, string> Speakers = new Dictionary<string, string>();
        public readonly Dictionary<string, string> VoiceFx = new Dictionary<string, string>();
        public string[] WingmanVoices = { "sable2", "sable3" }, EarthVoices = { "earth_warden", "earth_lector" };
        public string Unknown = "sable2";

        /// <summary>The voice for a callsign or "#Name" as it appears before the colon of a radio line; null when the speaker is not known.</summary>
        public string VoiceOf(string speaker)
        {
            if (string.IsNullOrEmpty(speaker)) return null;
            string key = speaker.Trim().ToLowerInvariant();
            if (Speakers.TryGetValue(key, out var v)) return v;
            var m = Regex.Match(key, @"^(.*?)\s*\d+$");
            if (m.Success && Speakers.TryGetValue(m.Groups[1].Value, out v)) return v;
            return null;
        }
    }

    /// <summary>
    /// Every sound the game has, loaded into memory: the built-in files named by the catalogs (music.json, sfx.json, vo/index.json) and, where the
    /// player has put a file of the same name in the Audio folder beside the game, theirs instead. Loading is a coroutine (Unity decodes OGG and
    /// MP3 for us); the Audio folder is watched, so a saved file is picked up within a couple of seconds and a deleted one gives the built-in
    /// sound back.
    /// </summary>
    public sealed partial class AudioLibrary
    {
        public readonly Dictionary<string, SfxCue> Sfx = new Dictionary<string, SfxCue>();
        public readonly Dictionary<string, MusicTrackInfo> Music = new Dictionary<string, MusicTrackInfo>();
        public readonly List<VoiceClip> Voices = new List<VoiceClip>();
        public readonly Dictionary<string, List<VoiceClip>> VoicesByText = new Dictionary<string, List<VoiceClip>>();
        public readonly SpeakerMap Cast = new SpeakerMap();
        public float Bpm = 112f, LoopSeconds = 68.571f;
        public int BeatsPerBar = 4;

        public bool Ready { get; private set; }
        /// <summary>Loading has begun (it may not be done): a library started early, by the loading screen, is not started again by the audio system that takes it over.</summary>
        public bool Started { get; private set; }
        public int FilesLoaded, FilesFailed, Overrides, FilesTotal;
        public readonly List<string> Problems = new List<string>();
        public readonly List<string> OverrideLog = new List<string>();

        enum Kind { Music, Sfx, SfxFar, Voice, Extra }

        sealed class Asset
        {
            public string Rel;                 // "music/battle2.ogg": the built-in file, relative to Audio/
            public Kind Kind;
            public Action<Clip, string> Apply;
            public string Voice;               // for radio styling of a player's recording
            public int Intensity;              // likewise: the radio filter works harder on a frantic line
            public string UserPath;            // the override in use, or null
            public DateTime UserTime;
            public bool IsLoop;
        }

        readonly List<Asset> _assets = new List<Asset>();
        readonly Dictionary<string, Asset> _byKey = new Dictionary<string, Asset>();
        float _nextPoll;

        static string Key(string rel) => Path.ChangeExtension(rel, null).Replace('\\', '/').ToLowerInvariant();

        // ------------------------------------------------------------------------------------------------ catalogs

        void ReadCatalogs()
        {
            string root = AudioPaths.BuiltIn;
            // music
            string mj = Path.Combine(root, "music", "music.json");
            if (File.Exists(mj))
            {
                var j = JObject.Parse(File.ReadAllText(mj));
                Bpm = (float?)j["bpm"] ?? 112f; BeatsPerBar = (int?)j["beatsPerBar"] ?? 4; LoopSeconds = (float?)j["loopSeconds"] ?? LoopSeconds;
                ReadThemeCatalog(j);
                foreach (var p in ((JObject)j["tracks"]).Properties())
                {
                    var t = new MusicTrackInfo
                    {
                        Name = p.Name, File = (string)p.Value["file"], Loop = (bool?)p.Value["loop"] ?? true, Seconds = (float?)p.Value["seconds"] ?? 0f,
                        Lufs = (float?)p.Value["lufs"] ?? 0f, Bpm = (float?)p.Value["bpm"] ?? Bpm, Bars = (int?)p.Value["bars"] ?? 0,
                        VolumeDb = (float?)p.Value["volumeDb"] ?? 0f, StartAt = (float?)p.Value["startAt"] ?? 0f, LoopStart = (float?)p.Value["loopStart"] ?? 0f, LoopEnd = (float?)p.Value["loopEnd"] ?? 0f, LoopFade = (float?)p.Value["loopFade"] ?? 0f,
                    };
                    if (p.Value["intensityDb"] is JObject idb) t.IntensityDb = new[] { (float?)idb["battle1"] ?? 0f, (float?)idb["battle2"] ?? 0f, (float?)idb["battle3"] ?? 0f };
                    Music[p.Name] = t;
                    var tt = t;
                    AddAsset(t.File, Kind.Music, (c, src) =>
                    {
                        tt.Clip = c; tt.Source = src;
                        if (c == null) return;
                        tt.Seconds = c.Seconds;
                        if (src == "built in" && (tt.LoopEnd > 0f || tt.StartAt > 0f))
                        {
                            // the shipped long track: where it starts, where its loop body is, and the seam cross-faded (the player's own file of the same name has its own sidecar)
                            c.StartFrame = Mathf.RoundToInt(tt.StartAt * c.SampleRate);
                            c.LoopStart = Mathf.RoundToInt(tt.LoopStart * c.SampleRate); c.LoopEnd = Mathf.RoundToInt(tt.LoopEnd * c.SampleRate);
                            if (tt.LoopFade > 0f && !c.BakeLoopCrossfade(tt.LoopFade)) MnLog.Warn($"AUDIO {tt.Name}: not enough audio before its loop start for a {tt.LoopFade:0.0} s cross-fade; it loops with a plain jump");
                        }
                    }, loop: t.Loop);
                }
            }
            else Problems.Add("music/music.json is missing");
            LoadUserMusicConfig();

            // effects
            string sj = Path.Combine(root, "sfx", "sfx.json");
            if (File.Exists(sj))
            {
                var j = JObject.Parse(File.ReadAllText(sj));
                foreach (var p in ((JObject)j["cues"]).Properties())
                {
                    var o = (JObject)p.Value;
                    var c = new SfxCue
                    {
                        Id = p.Name, Bus = (string)o["bus"] ?? "sfx", Note = (string)o["note"], LevelDb = (float?)o["levelDb"] ?? -12f,
                        MinDist = (float?)o["minDist"] ?? 60f, MaxDist = (float?)o["maxDist"] ?? 9000f, PitchVar = (float?)o["pitchVar"] ?? 0.03f,
                        Priority = (float?)o["priority"] ?? 1f, ArrivalDelay = (float?)o["arrivalDelay"] ?? 0f, Loop = (bool?)o["loop"] ?? false, Spatial = (bool?)o["spatial"] ?? true,
                    };
                    var nf = (JArray)o["nearFade"]; if (nf != null && nf.Count == 2) { c.NearFadeFrom = (float)nf[0]; c.NearFadeTo = (float)nf[1]; }
                    var dk = (JArray)o["duck"]; if (dk != null && dk.Count == 2) { c.DuckAmount = (float)dk[0]; c.DuckSeconds = (float)dk[1]; }
                    var files = o["files"].ToObject<string[]>(); var far = o["far"]?.ToObject<string[]>() ?? new string[0];
                    c.Near = new Clip[files.Length]; c.Far = new Clip[far.Length];
                    for (int i = 0; i < files.Length; i++) { int ii = i; AddAsset(files[i], Kind.Sfx, (cl, src) => c.Near[ii] = cl, loop: c.Loop); }
                    for (int i = 0; i < far.Length; i++) { int ii = i; AddAsset(far[i], Kind.SfxFar, (cl, src) => c.Far[ii] = cl); }
                    Sfx[c.Id] = c;
                }
            }
            else Problems.Add("sfx/sfx.json is missing");

            // radio voices
            string vj = Path.Combine(root, "vo", "index.json");
            if (File.Exists(vj))
            {
                var j = JObject.Parse(File.ReadAllText(vj));
                foreach (var p in ((JObject)j["speakers"]).Properties()) Cast.Speakers[p.Name.ToLowerInvariant()] = (string)p.Value;
                var d = (JObject)j["defaults"];
                if (d != null)
                {
                    Cast.WingmanVoices = d["wingmanVoices"]?.ToObject<string[]>() ?? Cast.WingmanVoices;
                    Cast.EarthVoices = d["earthVoices"]?.ToObject<string[]>() ?? Cast.EarthVoices;
                    Cast.Unknown = (string)d["unknown"] ?? Cast.Unknown;
                }
                foreach (var p in ((JObject)j["voices"]).Properties()) Cast.VoiceFx[p.Name] = (string)p.Value;
                foreach (JObject c in (JArray)j["clips"])
                {
                    var v = new VoiceClip { Voice = (string)c["voice"], Text = (string)c["text"], Norm = (string)c["norm"], File = (string)c["file"], Seconds = (float?)c["seconds"] ?? 0f, Intensity = VoiceClip.ParseIntensity((string)c["intensity"]), Lufs = (float?)c["lufs"] ?? -23f };
                    Voices.Add(v);
                    if (!VoicesByText.TryGetValue(v.Norm, out var list)) VoicesByText[v.Norm] = list = new List<VoiceClip>();
                    list.Add(v);
                    var vv = v;
                    AddAsset(v.File, Kind.Voice, (cl, src) => { vv.Clip = cl; vv.Source = src; if (cl != null) vv.Seconds = cl.Seconds; }, voice: v.Voice, intensity: v.Intensity);
                }
            }
            else Problems.Add("vo/index.json is missing");
        }

        void AddAsset(string rel, Kind kind, Action<Clip, string> apply, string voice = null, bool loop = false, int intensity = 0)
        {
            if (string.IsNullOrEmpty(rel)) return;
            // loops are stored as WAV; the others as OGG: either way the catalog says which
            var a = new Asset { Rel = rel, Kind = kind, Apply = apply, Voice = voice, IsLoop = loop, Intensity = intensity };
            _assets.Add(a);
            _byKey[Key(rel)] = a;
        }

        // ------------------------------------------------------------------------------------------------ loading

        /// <summary>Load everything now, on this thread (captures and checks that must not start before the sound is there). Local files only.</summary>
        public void LoadBlocking()
        {
            AudioFileLoader.Blocking = true;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try { ReadCatalogs(); }
                catch (Exception e) { Problems.Add("catalogs: " + e.Message); MnLog.Error("AUDIO catalogs: " + e); }
                FilesTotal = _assets.Count;
                foreach (var a in _assets.ToArray()) RunNow(LoadAsset(a, () => { }));
                PreloadUserMusic(null);
                Ready = true;
                MnLog.Info($"AUDIO library (blocking): {Music.Count} music tracks, {Sfx.Count} effects, {Voices.Count} radio clips; {FilesLoaded - FilesFailed}/{FilesTotal} files in {sw.ElapsedMilliseconds} ms, {Overrides} overrides, {FilesFailed} failed");
                foreach (var p in Problems) MnLog.Warn("AUDIO " + p);
            }
            finally { AudioFileLoader.Blocking = false; }
        }

        static void RunNow(IEnumerator it)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(it);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }
                if (top.Current is IEnumerator inner) stack.Push(inner);
            }
        }

        public IEnumerator Load(MonoBehaviour host)
        {
            Ready = false;
            Started = true;
            try { ReadCatalogs(); }
            catch (Exception e) { Problems.Add("catalogs: " + e.Message); MnLog.Error("AUDIO catalogs: " + e); }
            FilesTotal = _assets.Count;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int next = 0, running = 0;
            const int Concurrency = 32;
            while (next < _assets.Count || running > 0)
            {
                while (running < Concurrency && next < _assets.Count)
                {
                    var a = _assets[next++];
                    running++;
                    host.StartCoroutine(LoadAsset(a, () => running--));
                }
                yield return null;
            }
            Ready = true;
            PreloadUserMusic(host);
            MnLog.Info($"AUDIO library: {Music.Count} music tracks, {Sfx.Count} effects, {Voices.Count} radio clips; {FilesLoaded - FilesFailed}/{FilesTotal} files in {sw.ElapsedMilliseconds} ms, {Overrides} overrides, {FilesFailed} failed");
            foreach (var p in Problems) MnLog.Warn("AUDIO " + p);
        }

        IEnumerator LoadAsset(Asset a, Action done)
        {
            string builtIn = FindBuiltIn(a.Rel);
            string user = AudioPaths.FindUser(a.Rel);
            string path = user ?? builtIn;
            yield return LoadFrom(a, path, user != null);
            done();
        }

        static string FindBuiltIn(string rel)
        {
            string p = Path.Combine(AudioPaths.BuiltIn, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(p)) return p;
            foreach (var e in AudioPaths.Extensions) { string q = Path.ChangeExtension(p, e); if (File.Exists(q)) return q; }
            return p;
        }

        IEnumerator LoadFrom(Asset a, string path, bool isUser)
        {
            var r = new LoadResult();
            if (path == null || !File.Exists(path)) { r.Error = "file not found: " + path; r.Done = true; }
            else yield return AudioFileLoader.Load(path, Path.GetFileNameWithoutExtension(path), r);
            FilesLoaded++;
            if (r.Clip == null)
            {
                FilesFailed++;
                string msg = $"could not load {(isUser ? "your" : "built-in")} {a.Rel}: {r.Error}";
                Problems.Add(msg);
                if (isUser) MnLog.Warn("AUDIO " + msg);
                if (isUser) { /* fall back to the built-in */ var b = FindBuiltIn(a.Rel); var r2 = new LoadResult(); yield return AudioFileLoader.Load(b, Path.GetFileNameWithoutExtension(b), r2); if (r2.Clip != null) { a.Apply(r2.Clip, "built in"); a.UserPath = null; } }
                yield break;
            }
            var clip = r.Clip;
            string source = "built in";
            if (isUser)
            {
                source = path;
                a.UserPath = path; a.UserTime = File.GetLastWriteTimeUtc(path);
                Overrides++;
                Shape(a, clip, path);
                string line = $"override {a.Rel} <- {path} ({clip.Seconds:0.00} s at {clip.SampleRate} Hz{(clip.LoopEnd > 0 ? $", loops {clip.LoopStart / (float)clip.SampleRate:0.000}-{clip.LoopEnd / (float)clip.SampleRate:0.000} s" : "")})";
                OverrideLog.Add(line);
                MnLog.Info("AUDIO " + line);
                if (a.Kind == Kind.Voice) clip = StyleVoice(clip, a, path);
            }
            else a.UserPath = null;
            a.Apply(clip, source);
        }

        /// <summary>The sidecar json of a player's file: loop points and a trim for music.</summary>
        static void Shape(Asset a, Clip clip, string path)
        {
            string side = Path.ChangeExtension(path, ".json");
            if (!File.Exists(side)) return;
            try
            {
                var j = JObject.Parse(File.ReadAllText(side));
                float ls = (float?)j["loopStart"] ?? 0f, le = (float?)j["loopEnd"] ?? 0f;
                clip.LoopStart = Mathf.Clamp(Mathf.RoundToInt(ls * clip.SampleRate), 0, clip.Frames - 1);
                clip.LoopEnd = le > 0f ? Mathf.Clamp(Mathf.RoundToInt(le * clip.SampleRate), clip.LoopStart + 1, clip.Frames) : 0;
                clip.TrimDb = (float?)j["volume"] ?? 0f;
            }
            catch (Exception e) { MnLog.Warn($"AUDIO sidecar {side}: {e.Message}"); }
        }

        Clip StyleVoice(Clip clip, Asset a, string path)
        {
            try
            {
                string side = Path.ChangeExtension(path, ".json");
                if (File.Exists(side) && (bool?)JObject.Parse(File.ReadAllText(side))["radio"] == false) return clip;
                var mono = new float[clip.Frames];
                for (int i = 0; i < mono.Length; i++)
                {
                    float s = clip.Pcm[i * clip.Channels] / 32768f;
                    if (clip.Channels == 2) s = 0.5f * (s + clip.Pcm[i * 2 + 1] / 32768f);
                    mono[i] = s;
                }
                Cast.VoiceFx.TryGetValue(a.Voice ?? "", out var fx);
                var y = RadioFx.Process(mono, clip.SampleRate, RadioFx.StyleOf(fx), (uint)(path.GetHashCode() & 0xFFFF) + 1u, -23f + (a.Intensity == 2 ? 3.5f : a.Intensity == 1 ? 1.5f : 0f), (RadioIntensity)a.Intensity);
                return Clip.FromPcm(PcmBuffer.FromMono(RadioFx.OutRate, y), clip.Name);
            }
            catch (Exception e) { MnLog.Warn("AUDIO radio filter failed on " + path + ": " + e.Message); return clip; }
        }

        // ------------------------------------------------------------------------------------------------ the Audio folder is watched

        /// <summary>Call now and then (every frame is fine): looks for new, changed and deleted files in the player's Audio folder every couple of seconds.</summary>
        public void Poll(MonoBehaviour host, float realTime)
        {
            if (!Ready || realTime < _nextPoll) return;
            _nextPoll = realTime + 2f;
            try { PollMusicConfig(host); } catch (Exception e) { MnLog.Warn("AUDIO music.json watch: " + e.Message); }
            try
            {
                string user = AudioPaths.User;
                if (!Directory.Exists(user)) return;
                var seen = new HashSet<string>();
                foreach (var f in Directory.GetFiles(user, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (Array.IndexOf(AudioPaths.Extensions, ext) < 0) continue;
                    string rel = f.Substring(user.Length).TrimStart('\\', '/').Replace('\\', '/');
                    string key = Key(rel);
                    if (!_byKey.TryGetValue(key, out var a)) { UnknownFile(rel); continue; }
                    seen.Add(key);
                    var t = File.GetLastWriteTimeUtc(f);
                    if (a.UserPath != f || a.UserTime != t) host.StartCoroutine(LoadFrom(a, f, true));
                }
                foreach (var a in _assets)
                    if (a.UserPath != null && !seen.Contains(Key(a.Rel)) && !File.Exists(a.UserPath))
                    {
                        MnLog.Info($"AUDIO override of {a.Rel} removed: the built-in sound is back");
                        var b = a;
                        host.StartCoroutine(LoadFrom(b, FindBuiltIn(b.Rel), false));
                    }
            }
            catch (Exception e) { MnLog.Warn("AUDIO folder watch: " + e.Message); }
        }

        readonly HashSet<string> _unknownLogged = new HashSet<string>();

        void UnknownFile(string rel)
        {
            // a track a mission names, or a file that matches nothing: loaded on demand, or noted once
            if (rel.StartsWith("music/", StringComparison.OrdinalIgnoreCase)) return;
            if (_unknownLogged.Add(rel)) MnLog.Info($"AUDIO {rel} in your Audio folder matches no built-in sound (names are listed in Audio\\README.txt); ignored");
        }

        // ------------------------------------------------------------------------------------------------ music tracks named by missions

        readonly Dictionary<string, MusicTrackInfo> _extra = new Dictionary<string, MusicTrackInfo>();

        /// <summary>Find a music track by name: a built-in one, one loaded already, or a file of that name in the player's music folder (loaded on the first ask; null until it is there).</summary>
        public MusicTrackInfo FindMusic(string name, MonoBehaviour host) => FindMusic(name, host, out _);

        /// <param name="pending">true when there is such a file and it is still loading (ask again in a moment); false when there is no such track at all</param>
        public MusicTrackInfo FindMusic(string name, MonoBehaviour host, out bool pending)
        {
            pending = false;
            if (string.IsNullOrEmpty(name)) return null;
            if (Music.TryGetValue(name, out var t)) { pending = t.Clip == null; return t.Clip != null ? t : null; }
            if (_extra.TryGetValue(name, out t))
            {
                // a file that appeared since we last looked (the player dropped it in)
                if (t.File == null && AudioPaths.FindUser("music/" + name + ".ogg") != null) _extra.Remove(name);
                else { pending = t.File != null && t.Clip == null; return t.Clip != null ? t : null; }
            }
            string rel = "music/" + name + ".ogg";
            string user = AudioPaths.FindUser(rel), built = FindBuiltIn(rel);
            string path = user ?? (File.Exists(built) ? built : null);
            if (path == null) { _extra[name] = new MusicTrackInfo { Name = name }; return null; }
            pending = true;
            var info = new MusicTrackInfo { Name = name, File = rel, Source = path };
            _extra[name] = info;
            var a = new Asset { Rel = rel, Kind = Kind.Music, Apply = (c, src) => { info.Clip = c; info.Source = src; info.Seconds = c.Seconds; } };
            _assets.Add(a); _byKey[Key(rel)] = a;
            host.StartCoroutine(LoadFrom(a, path, user != null));
            return null;
        }

        readonly Dictionary<string, Clip> _slots = new Dictionary<string, Clip>();
        readonly HashSet<string> _slotsLoading = new HashSet<string>();

        /// <summary>A clip named by a mission message's "voice" slot (a path under Audio/): the player's file, or a built-in one. Null until it has loaded (the first ask starts the load) and when there is none.</summary>
        public Clip FindVoiceSlot(string slot, MonoBehaviour host, string voice)
        {
            if (string.IsNullOrEmpty(slot)) return null;
            if (_slots.TryGetValue(slot, out var c)) return c;
            if (_slotsLoading.Contains(slot)) return null;
            string user = AudioPaths.FindUser(slot);
            string path = user ?? (File.Exists(FindBuiltIn(slot)) ? FindBuiltIn(slot) : null);
            if (path == null) { _slots[slot] = null; return null; }
            _slotsLoading.Add(slot);
            var a = new Asset { Rel = slot, Kind = Kind.Voice, Voice = voice, Apply = (cl, src) => { _slots[slot] = cl; } };
            host.StartCoroutine(LoadFrom(a, path, user != null));
            return null;
        }

        public bool Has(string sfxId) => Sfx.TryGetValue(sfxId, out var c) && c.Ready;
    }
}
