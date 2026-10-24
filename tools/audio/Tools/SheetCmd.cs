using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    /// <summary>
    /// MnAudio sheet [--out dir]: the audio sheet. Every music track, sting, effect and radio clip as it ships (the OGG and WAV files, decoded), with its
    /// length, loudness and peak, and a spectrogram of each, in one HTML page and one CSV: a way to look at the sound without ears.
    /// </summary>
    public static class SheetCmd
    {
        static string F(double v, string fmt = "0.0") => v.ToString(fmt, CultureInfo.InvariantCulture);

        static PcmBuffer Decode(string path)
        {
            if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return Wav.Read(path);
            string tmp = Path.Combine(Path.GetTempPath(), "mn_sheet_" + Guid.NewGuid().ToString("N") + ".wav");
            int code = Paths.Run(Paths.Ffmpeg, $"-y -v error -i \"{path}\" -c:a pcm_s16le \"{tmp}\"", out var o);
            if (code != 0) throw new Exception("ffmpeg decode failed: " + o);
            try { return Wav.Read(tmp); } finally { File.Delete(tmp); }
        }

        public static int Run(string[] args)
        {
            string outDir = args.SkipWhile(a => a != "--out").Skip(1).FirstOrDefault() ?? Path.Combine(Paths.Root, "gauntlet", "captures", "audio", "r1", "sheet");
            Directory.CreateDirectory(Path.Combine(outDir, "img"));
            string root = Paths.StreamingAudio;
            var rows = new List<(string group, string name, string file, double secs, float lufs, float mom, float peak, float tp, string img, string note)>();

            void Do(string group, string name, string file, string note = "")
            {
                try
                {
                    var b = Decode(file);
                    var m = LoudnessMeter.Measure(b);
                    float tp = LoudnessMeter.TruePeakDb(b);
                    string img = $"img/{group}_{name}.png".Replace('/', '/');
                    string imgPath = Path.Combine(outDir, "img", $"{group}_{name}.png");
                    int w = (int)Math.Clamp(b.Frames / (double)b.SampleRate * 60, 360, 1400);
                    Spectro.Write(imgPath, b, w, 200, 60);
                    rows.Add((group, name, Path.GetRelativePath(root, file).Replace('\\', '/'), b.Frames / (double)b.SampleRate, m.Integrated, m.MaxMomentary, m.PeakDb, tp, img, note));
                    Console.WriteLine($"  {group,-6} {name,-26} {b.Frames / (double)b.SampleRate,6:0.0} s {m.Integrated,6:0.0} LUFS {m.PeakDb,6:0.0} dBFS");
                }
                catch (Exception e) { Console.WriteLine($"  {group} {name}: {e.Message}"); }
            }

            foreach (var f in Directory.GetFiles(Path.Combine(root, "music"), "*.ogg").OrderBy(x => x)) Do("music", Path.GetFileNameWithoutExtension(f), f);
            foreach (var f in Directory.GetFiles(Path.Combine(root, "sfx")).Where(x => x.EndsWith(".ogg") || x.EndsWith(".wav")).OrderBy(x => x)) Do("sfx", Path.GetFileNameWithoutExtension(f), f);
            foreach (var f in Directory.GetFiles(Path.Combine(root, "vo"), "*.ogg", SearchOption.AllDirectories).OrderBy(x => x))
            {
                string voice = Path.GetFileName(Path.GetDirectoryName(f));
                Do("vo", voice + "_" + Path.GetFileNameWithoutExtension(f), f);
            }

            var csv = new StringBuilder("group,name,file,seconds,lufs_integrated,lufs_momentary_max,peak_dbfs,true_peak_dbtp\n");
            foreach (var r in rows) csv.AppendLine($"{r.group},{r.name},{r.file},{F(r.secs, "0.00")},{F(r.lufs)},{F(r.mom)},{F(r.peak)},{F(r.tp)}");
            File.WriteAllText(Path.Combine(outDir, "audio_levels.csv"), csv.ToString());

            var html = new StringBuilder();
            html.AppendLine("<!doctype html><meta charset=\"utf-8\"><title>Monolith audio sheet</title>");
            html.AppendLine("<style>body{background:#0c0d11;color:#cfd3dc;font:14px/1.4 system-ui,sans-serif;margin:24px}h1{font-weight:600}h2{margin-top:36px;border-bottom:1px solid #2a2d36}table{border-collapse:collapse;margin:8px 0}td,th{padding:4px 10px;text-align:right;border-bottom:1px solid #1d2028}td:first-child,th:first-child{text-align:left}.c{display:inline-block;margin:6px 10px 14px 0;vertical-align:top}.c img{display:block;border:1px solid #2a2d36}.c b{display:block}small{color:#8a90a0}</style>");
            html.AppendLine("<h1>Monolith audio sheet</h1><p><small>Generated from the shipped files by tools/audio (MnAudio sheet). Spectrogram: 30 Hz to 20 kHz on a log axis, waveform above (clipping in red). Loudness is BS.1770 (LUFS); level is the file at full scale, before any mix gain.</small></p>");
            foreach (var g in rows.GroupBy(r => r.group))
            {
                html.AppendLine($"<h2>{g.Key} ({g.Count()})</h2>");
                if (g.Key == "music")
                {
                    html.AppendLine("<table><tr><th>track</th><th>seconds</th><th>LUFS</th><th>momentary max</th><th>peak dBFS</th><th>true peak</th></tr>");
                    foreach (var r in g) html.AppendLine($"<tr><td>{r.name}</td><td>{F(r.secs)}</td><td>{F(r.lufs)}</td><td>{F(r.mom)}</td><td>{F(r.peak)}</td><td>{F(r.tp)}</td></tr>");
                    html.AppendLine("</table>");
                }
                foreach (var r in g.Take(g.Key == "vo" ? 24 : 200))
                    html.AppendLine($"<div class=\"c\"><b>{r.name}</b><small>{F(r.secs)} s &middot; {F(r.lufs)} LUFS &middot; peak {F(r.peak)}</small><img src=\"{r.img}\" loading=\"lazy\"></div>");
                if (g.Key == "vo") html.AppendLine($"<p><small>... and {g.Count() - 24} more radio clips; every one is in audio_levels.csv.</small></p>");
            }
            File.WriteAllText(Path.Combine(outDir, "index.html"), html.ToString());
            Console.WriteLine($"audio sheet: {rows.Count} files -> {outDir}");
            return 0;
        }
    }
}
