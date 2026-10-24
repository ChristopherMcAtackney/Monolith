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
        static bool _userFixed;

        /// <summary>The built-in sounds: music, effects and radio voices, as made by tools/audio.</summary>
        public static string BuiltIn => Path.Combine(Application.streamingAssetsPath, "Audio");

        /// <summary>Beside the game (Monolith.exe), so a player can drop MP3, WAV or OGG files there. Override with -mnAudioUser dir (tests).</summary>
        public static string User
        {
            get
            {
                if (_user != null) return _user;
                var flag = CommandLine.Get("-mnAudioUser");
                if (!string.IsNullOrEmpty(flag)) { _userFixed = true; return _user = flag; }
                return _user = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Audio");
            }
        }

        /// <summary>Point the player's Audio folder somewhere else (the checks use a scratch folder).</summary>
        public static void OverrideUser(string dir) { _user = dir; _userFixed = true; }

        /// <summary>The player's music.json: beside Monolith.exe, or inside the Audio folder (the first that exists); null when there is none. (With -mnAudioUser, only that folder is looked in.)</summary>
        public static string FindMusicConfig()
        {
            string beside = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "music.json");
            string inUser = Path.Combine(User, "music.json");               // reading User first: it sets whether the folder was fixed by a flag
            if (!_userFixed && File.Exists(beside)) return beside;
            return File.Exists(inUser) ? inUser : null;
        }

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
                        "For the whole score at once (base music, stings, endings and the themes a mission fades in over it, like the Lector's), with fades and volumes: put a music.json next to\r\n" +
                        "Monolith.exe and name your tracks there (MUSIC.txt, in the playtest folder or tools/audio, says how). Slots you leave empty play the built-in music.\r\n" +
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

    /// <summary>Where the time of loading sound goes, by file type (the load probe prints it; only a blocking, one-at-a-time load makes it exact).</summary>
    public sealed class LoadStat
    {
        public int Files; public long Bytes; public double RequestMs, DecodeMs, ReadyMs, CopyMs, WavMs, AllocMs, GetMs, CacheMs; public double Seconds; public int Cached;
        public double TotalMs => RequestMs + DecodeMs + ReadyMs + CopyMs + WavMs + AllocMs + GetMs + CacheMs;
    }

    /// <summary>
    /// Loads WAV by hand and OGG/MP3 through Unity's decoder, into the mixer's own 16-bit clips.
    /// Unity decodes (one file at a time, on its own thread); everything after that is ours and off the main thread: the main thread only copies the decoded sound out of Unity in
    /// short pieces (<see cref="AudioClip.GetData(float[], int)"/>, a few milliseconds for a four-minute track), and worker threads turn the pieces into 16 bits while Unity decodes
    /// the next file. (The conversion was the slowest part of loading: 3.6 s of the main thread for the 349 files, more than the decoder's own 2.4 s. A WAV is read and converted on a worker too.)
    /// Unity's own decoding cannot leave the main thread (it happens inside GetContent), so a file that has been decoded once is kept as plain PCM (<see cref="PcmCache"/>) and read by a worker next time.
    /// </summary>
    public static class AudioFileLoader
    {
        public static readonly System.Collections.Generic.Dictionary<string, LoadStat> Stats = new System.Collections.Generic.Dictionary<string, LoadStat>();
        public static bool Timing;
        static LoadStat StatOf(string path)
        {
            string p = path.Replace('\\', '/');
            string key = (p.Contains("/vo/") ? "vo" : p.Contains("/music/") ? "music" : p.Contains("/sfx/") ? "sfx" : "other") + Path.GetExtension(p).ToLowerInvariant();
            if (!Stats.TryGetValue(key, out var s)) Stats[key] = s = new LoadStat();
            return s;
        }

        /// <summary>While true, decoding waits for the request instead of yielding (captures load everything before the first tick; local files only).</summary>
        public static bool Blocking;

        /// <summary>Frames of a clip copied out of Unity at a time (about 2.7 s at 48 kHz).</summary>
        const int ChunkFrames = 131072;

        sealed class Job
        {
            public int Pending;
            public volatile string Error;
            public System.Lazy<short[]> Pcm;                 // made by the first worker that needs it: a four-minute track is 40 MB, which the main thread should not zero
        }

        // buffers for the pieces, reused (a piece is a megabyte: they must not go to the garbage collector by the hundred)
        static readonly System.Collections.Concurrent.ConcurrentBag<float[]>[] Pool = { new System.Collections.Concurrent.ConcurrentBag<float[]>(), new System.Collections.Concurrent.ConcurrentBag<float[]>(), new System.Collections.Concurrent.ConcurrentBag<float[]>() };
        static float[] Rent(int channels) => Pool[channels].TryTake(out var b) ? b : new float[ChunkFrames * channels];
        static void Return(int channels, float[] b) => Pool[channels].Add(b);

        static void Wait(Func<bool> done) { var t = System.Diagnostics.Stopwatch.StartNew(); while (!done() && t.ElapsedMilliseconds < 60000) System.Threading.Thread.Sleep(1); }

        public static IEnumerator Load(string path, string name, LoadResult result)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".wav")
            {
                var w0 = Timing ? System.Diagnostics.Stopwatch.StartNew() : null;
                var task = System.Threading.Tasks.Task.Run(() => Clip.FromPcm(Wav.Read(path), name));
                if (Blocking) Wait(() => task.IsCompleted); else while (!task.IsCompleted) yield return null;
                if (task.IsFaulted) result.Error = task.Exception.GetBaseException().Message;
                else if (task.IsCompleted) result.Clip = task.Result;
                else result.Error = "timed out";
                if (Timing && result.Clip != null) { var st = StatOf(path); st.Files++; st.Bytes += new FileInfo(path).Length; st.WavMs += w0.Elapsed.TotalMilliseconds; st.Seconds += result.Clip.Seconds; }
                result.Done = true;
                yield break;
            }
            // decoded before, and the file unchanged since: read the PCM back on a worker thread
            long srcLength = 0, srcTicks = 0;
            bool cache = PcmCache.Usable;
            if (cache)
            {
                try { var fi = new FileInfo(path); srcLength = fi.Length; srcTicks = fi.LastWriteTimeUtc.Ticks; }
                catch (Exception) { cache = false; }
            }
            if (cache)
            {
                var c0 = Timing ? System.Diagnostics.Stopwatch.StartNew() : null;
                var read = System.Threading.Tasks.Task.Run(() => PcmCache.TryRead(path, name));
                if (Blocking) Wait(() => read.IsCompleted); else while (!read.IsCompleted) yield return null;
                if (read.IsCompleted && !read.IsFaulted && read.Result != null)
                {
                    result.Clip = read.Result; result.Done = true; PcmCache.Hits++;
                    if (Timing) { var st = StatOf(path); st.Files++; st.Bytes += srcLength; st.CacheMs += c0.Elapsed.TotalMilliseconds; st.Seconds += result.Clip.Seconds; st.Cached++; }
                    yield break;
                }
                PcmCache.Misses++;
            }
            AudioType type = ext == ".ogg" ? AudioType.OGGVORBIS : ext == ".mp3" ? AudioType.MPEG : AudioType.UNKNOWN;
            string url;
            try { url = new Uri(path).AbsoluteUri; }
            catch (Exception e) { result.Error = e.Message; result.Done = true; yield break; }
            var t = Timing ? System.Diagnostics.Stopwatch.StartNew() : null;
            using (var req = UnityWebRequestMultimedia.GetAudioClip(url, type))
            {
                var op = req.SendWebRequest();
                if (Blocking) { var t0 = System.Diagnostics.Stopwatch.StartNew(); while (!op.isDone && t0.ElapsedMilliseconds < 20000) System.Threading.Thread.Sleep(1); }
                else yield return op;
                if (req.result != UnityWebRequest.Result.Success) { result.Error = req.error; result.Done = true; yield break; }
                double sentMs = Timing ? t.Elapsed.TotalMilliseconds : 0; if (Timing) t.Restart();
                AudioClip ac = null;
                try { ac = DownloadHandlerAudioClip.GetContent(req); }
                catch (Exception e) { result.Error = e.Message; result.Done = true; yield break; }
                if (ac == null) { result.Error = "no clip decoded"; result.Done = true; yield break; }
                double requestMs = Timing ? sentMs : 0; double contentMs = Timing ? t.Elapsed.TotalMilliseconds : 0; if (Timing) t.Restart();
                float guard = 0f;
                if (Blocking) { var t1 = System.Diagnostics.Stopwatch.StartNew(); while (ac.loadState == AudioDataLoadState.Loading && t1.ElapsedMilliseconds < 20000) System.Threading.Thread.Sleep(1); }
                else while (ac.loadState == AudioDataLoadState.Loading && guard < 20f) { guard += Time.unscaledDeltaTime; yield return null; }
                if (ac.loadState != AudioDataLoadState.Loaded || ac.samples <= 0) { result.Error = "decode failed (" + ac.loadState + ")"; UnityEngine.Object.Destroy(ac); result.Done = true; yield break; }
                double readyMs = Timing ? t.Elapsed.TotalMilliseconds : 0; if (Timing) t.Restart();

                // copy the sound out of Unity piece by piece (main thread, quick); worker threads quantise each piece into the clip's 16-bit array
                int channels = ac.channels, frames = ac.samples, freq = ac.frequency;
                int ch = Math.Min(channels, 2);
                int pcmLength = frames * ch;
                var job = new Job { Pcm = new System.Lazy<short[]>(() => new short[pcmLength], System.Threading.LazyThreadSafetyMode.ExecutionAndPublication) };
                var slice = System.Diagnostics.Stopwatch.StartNew();
                bool ok = channels >= 1;
                int bufCh = channels <= 2 ? channels : 0;             // more than two channels: a piece is allocated whole (rare: nobody drops in 5.1 music)
                double allocMs = Timing ? t.Elapsed.TotalMilliseconds : 0; if (Timing) t.Restart();
                for (int f0 = 0; f0 < frames && ok; f0 += ChunkFrames)
                {
                    int n = Math.Min(ChunkFrames, frames - f0);
                    var buf = n == ChunkFrames && bufCh > 0 ? Rent(bufCh) : new float[n * channels];
                    if (!ac.GetData(buf, f0)) { job.Error = "GetData failed"; if (n == ChunkFrames && bufCh > 0) Return(bufCh, buf); ok = false; break; }
                    System.Threading.Interlocked.Increment(ref job.Pending);
                    int start = f0, count = n;
                    bool pooled = n == ChunkFrames && bufCh > 0;
                    System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try { Clip.Quantize(buf, channels, job.Pcm.Value, start, count); }
                        catch (Exception e) { job.Error = e.Message; }
                        if (pooled) Return(bufCh, buf);
                        System.Threading.Interlocked.Decrement(ref job.Pending);
                    });
                    if (!Blocking && slice.ElapsedMilliseconds >= 2) { yield return null; slice.Restart(); }       // a long track takes a few frames to copy out, not one long one
                }
                double getMs = Timing ? t.Elapsed.TotalMilliseconds : 0; if (Timing) t.Restart();
                UnityEngine.Object.Destroy(ac);                      // everything it held has been copied
                if (Blocking) Wait(() => System.Threading.Volatile.Read(ref job.Pending) == 0);
                else while (System.Threading.Volatile.Read(ref job.Pending) > 0) yield return null;
                if (job.Error != null) { result.Error = job.Error; result.Done = true; yield break; }
                var pcmFinal = job.Pcm.Value;
                if (cache)
                {
                    // keep it for next time; written before the clip is handed over (its loop seam may be baked into it by whoever gets it)
                    var write = System.Threading.Tasks.Task.Run(() => PcmCache.Write(path, pcmFinal, freq, ch, srcLength, srcTicks));
                    if (Blocking) Wait(() => write.IsCompleted); else while (!write.IsCompleted) yield return null;
                    if (write.IsCompleted && !write.IsFaulted && write.Result) PcmCache.Writes++; else PcmCache.Failures++;
                }
                result.Clip = new Clip { Name = name, SampleRate = freq, Channels = ch, Pcm = pcmFinal };
                if (Timing) { var st = StatOf(path); st.Files++; st.Bytes += new FileInfo(path).Length; st.RequestMs += requestMs; st.DecodeMs += contentMs; st.ReadyMs += readyMs; st.AllocMs += allocMs; st.GetMs += getMs; st.CopyMs += t.Elapsed.TotalMilliseconds; st.Seconds += result.Clip.Seconds; }
                result.Done = true;
            }
        }
    }
}
