using System;
using System.Diagnostics;
using System.IO;

namespace Monolith.AudioTools
{
    /// <summary>Where things live in the repo, and the ffmpeg call that makes OGG.</summary>
    public static class Paths
    {
        static string _root;
        public static string Root
        {
            get
            {
                if (_root != null) return _root;
                var env = Environment.GetEnvironmentVariable("MN_ROOT");
                if (!string.IsNullOrEmpty(env)) return _root = env;
                var d = new DirectoryInfo(Directory.GetCurrentDirectory());
                while (d != null && !(Directory.Exists(Path.Combine(d.FullName, "Game")) && Directory.Exists(Path.Combine(d.FullName, "tools")))) d = d.Parent;
                if (d == null) d = new DirectoryInfo(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")));
                return _root = d.FullName;
            }
        }
        public static string AudioSrc => Path.Combine(Root, "audio_src");
        public static string Render => Path.Combine(AudioSrc, "render");
        public static string StreamingAudio => Path.Combine(Root, "Game", "Assets", "StreamingAssets", "Audio");

        public static string Ffmpeg
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("FFMPEG");
                return string.IsNullOrEmpty(env) ? "ffmpeg" : env;
            }
        }

        public static int Run(string exe, string args, out string output)
        {
            var psi = new ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                output = o;
                return p.ExitCode;
            }
        }

        /// <summary>Encode a WAV to OGG Vorbis (quality 6, about 192 kbps for stereo music).</summary>
        public static void EncodeOgg(string wav, string ogg, float quality = 6f)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ogg)));
            int code = Run(Paths.Ffmpeg, $"-y -v error -i \"{wav}\" -fflags +bitexact -flags:a +bitexact -c:a libvorbis -q:a {quality.ToString(System.Globalization.CultureInfo.InvariantCulture)} \"{ogg}\"", out var o);
            if (code != 0) throw new Exception("ffmpeg failed: " + o);
        }
    }
}
