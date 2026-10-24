using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Audio
{
    /// <summary>
    /// A decoded sound kept on disk as plain 16-bit PCM, so the next start reads it back instead of decoding it again.
    /// <para>
    /// Why: Unity decodes an OGG or MP3 inside <c>DownloadHandlerAudioClip.GetContent</c>, synchronously, on the main thread: for the 349 built-in files that is 2.3 s of the start in which nothing else can happen
    /// (the world cannot be built, the screen cannot move). A cache file is read by a worker thread at disk speed (about 290 MB; a few tenths of a second, in parallel with everything else).
    /// </para>
    /// <para>
    /// Where: <c>.pcm\&lt;path&gt;.mnpcm</c> inside the folder the sound came from: the built-in Audio folder (inside the build, never in the Unity project: the Editor does not use the cache) or the player's own
    /// Audio folder; a file from anywhere else (a path in music.json) goes to <c>.pcm\_external</c> in the player's folder. The sound itself is untouched: the cache is only a faster copy. It names the
    /// length and modification time of the file it was made from, and is ignored (and made again) when either has changed, when it is truncated, or when anything about it is not as expected.
    /// Delete any <c>.pcm</c> folder at any time: the next start makes it again. <c>-mnNoAudioCache</c> turns it off.
    /// </para>
    /// <para>
    /// Made by the first start that decodes the file (the loader writes it before handing the clip over), and ahead of time for the built-in files by <c>tools\audio\make_playtest.ps1</c> (<c>-mnAudioCacheBuild</c>).
    /// </para>
    /// </summary>
    public static class PcmCache
    {
        const string Magic = "MNPCM1\0\0";
        const int Version = 1;
        const int HeaderBytes = 64;

        public static bool Enabled = true;
        public static int Hits, Misses, Writes, Failures;

        public static bool Usable => Enabled && !Application.isEditor && !CommandLine.Has("-mnNoAudioCache");

        static string RelUnder(string full, string root)
        {
            if (string.IsNullOrEmpty(root)) return null;
            string r = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return full.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? full.Substring(r.Length) : null;
        }

        /// <summary>The cache file for a sound file, or null when there is nowhere to keep one.</summary>
        public static string PathFor(string source)
        {
            try
            {
                string full = Path.GetFullPath(source);
                string rel = RelUnder(full, AudioPaths.BuiltIn);
                if (rel != null) return Path.Combine(AudioPaths.BuiltIn, ".pcm", rel + ".mnpcm");
                rel = RelUnder(full, AudioPaths.User);
                if (rel != null) return Path.Combine(AudioPaths.User, ".pcm", rel + ".mnpcm");
                uint h = 2166136261;
                foreach (char c in full.ToLowerInvariant()) h = (h ^ c) * 16777619;
                return Path.Combine(AudioPaths.User, ".pcm", "_external", h.ToString("x8") + "_" + Path.GetFileName(full) + ".mnpcm");
            }
            catch (Exception) { return null; }
        }

        /// <summary>Read the cached sound for a file; null when there is none that is good (none yet, out of date, damaged). Safe on any thread.</summary>
        public static Clip TryRead(string source, string name)
        {
            try
            {
                string cp = PathFor(source);
                if (cp == null || !File.Exists(cp)) return null;
                var fi = new FileInfo(source);
                using (var fs = new FileStream(cp, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
                {
                    var h = new byte[HeaderBytes];
                    if (fs.Read(h, 0, HeaderBytes) != HeaderBytes || Encoding.ASCII.GetString(h, 0, 8) != Magic || BitConverter.ToInt32(h, 8) != Version) return null;
                    int sr = BitConverter.ToInt32(h, 12), ch = BitConverter.ToInt32(h, 16), frames = BitConverter.ToInt32(h, 20);
                    long srcLen = BitConverter.ToInt64(h, 24), srcTicks = BitConverter.ToInt64(h, 32);
                    if (srcLen != fi.Length || srcTicks != fi.LastWriteTimeUtc.Ticks) return null;                         // the sound has changed since
                    if (sr < 8000 || sr > 192000 || ch < 1 || ch > 2 || frames <= 0 || fs.Length != HeaderBytes + (long)frames * ch * 2) return null;
                    var pcm = new short[frames * ch];
                    var bytes = MemoryMarshal.AsBytes(pcm.AsSpan());
                    int got = 0;
                    while (got < bytes.Length)
                    {
                        int n = fs.Read(bytes.Slice(got));
                        if (n <= 0) return null;
                        got += n;
                    }
                    return new Clip { Name = name, SampleRate = sr, Channels = ch, Pcm = pcm };
                }
            }
            catch (Exception) { return null; }
        }

        /// <summary>Keep a decoded sound for next time. <paramref name="srcLength"/> and <paramref name="srcTicks"/> are the file's length and modification time as they were before it was decoded. Safe on any thread; never throws.</summary>
        public static bool Write(string source, short[] pcm, int sampleRate, int channels, long srcLength, long srcTicks)
        {
            string tmp = null;
            try
            {
                string cp = PathFor(source);
                if (cp == null) return false;
                Directory.CreateDirectory(Path.GetDirectoryName(cp));
                tmp = cp + "." + System.Threading.Thread.CurrentThread.ManagedThreadId + ".tmp";
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                {
                    var h = new byte[HeaderBytes];
                    Encoding.ASCII.GetBytes(Magic).CopyTo(h, 0);
                    BitConverter.GetBytes(Version).CopyTo(h, 8);
                    BitConverter.GetBytes(sampleRate).CopyTo(h, 12);
                    BitConverter.GetBytes(channels).CopyTo(h, 16);
                    BitConverter.GetBytes(pcm.Length / channels).CopyTo(h, 20);
                    BitConverter.GetBytes(srcLength).CopyTo(h, 24);
                    BitConverter.GetBytes(srcTicks).CopyTo(h, 32);
                    fs.Write(h, 0, HeaderBytes);
                    fs.Write(MemoryMarshal.AsBytes(pcm.AsSpan()));
                }
                if (File.Exists(cp)) File.Delete(cp);
                File.Move(tmp, cp);
                return true;
            }
            catch (Exception)
            {
                try { if (tmp != null && File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                return false;                                   // a folder that cannot be written: the cache is only a speed-up
            }
        }
    }
}
