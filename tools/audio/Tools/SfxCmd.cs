using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    /// <summary>MnAudio sfx [id ...|all] [--wav-only]: synthesise the sound effects, measure them, encode them and write sfx.json.</summary>
    public static class SfxCmd
    {
        static string J(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        public static int Run(string[] args)
        {
            bool wavOnly = args.Contains("--wav-only");
            var want = args.Skip(1).Where(a => !a.StartsWith("--")).ToList();
            var defs = SfxLib.All();
            string wavDir = Path.Combine(Paths.Render, "sfx");
            string outDir = Path.Combine(Paths.StreamingAudio, "sfx");
            Directory.CreateDirectory(wavDir);
            var sb = new StringBuilder();
            sb.AppendLine("{\n  \"format\": \"monolith-sfx/1\",\n  \"rate\": 48000,\n  \"cues\": {");
            var report = new StringBuilder("id,variant,seconds,peakDb,momentaryLufs,integratedLufs,rms_db\n");
            int n = 0;
            foreach (var d in defs)
            {
                bool selected = want.Count == 0 || want.Contains("all") || want.Contains(d.Id);
                var files = new List<string>(); var farFiles = new List<string>();
                float seconds = 0f;
                for (int v = 0; v < d.Variants; v++)
                {
                    string ext = d.Loop ? "wav" : "ogg";
                    string name = d.Variants > 1 ? $"{d.Id}_{v}" : d.Id;
                    files.Add($"sfx/{name}.{ext}");
                    if (!selected) continue;
                    var sd = d.Build(v);
                    sd.Normalize(-1f);
                    if (!d.Loop) sd.TrimSilence();
                    var buf = PcmBuffer.FromMono(Sd.Rate, sd.X);
                    string wav = Path.Combine(wavDir, name + ".wav");
                    Wav.Write(wav, buf, 16, true);
                    var m = LoudnessMeter.Measure(buf);
                    seconds = Math.Max(seconds, sd.Seconds);
                    report.AppendLine($"{d.Id},{v},{sd.Seconds:0.00},{m.PeakDb:0.0},{m.MaxMomentary:0.0},{m.Integrated:0.0},{Db.FromLin((float)Math.Sqrt(buf.Ch[0].Sum(x => (double)x * x) / buf.Frames)):0.0}");
                    if (!wavOnly) { if (d.Loop) File.Copy(wav, Path.Combine(outDir, name + ".wav"), true); else Paths.EncodeOgg(wav, Path.Combine(outDir, name + ".ogg"), 5f); }
                    if (d.FarAmount > 0f)
                    {
                        var far = d.Build(v).Normalize(-1f).Far(d.FarAmount).Normalize(-1f).TrimSilence(-60f, 0.08f, 0.1f);
                        var fb = PcmBuffer.FromMono(Sd.Rate, far.X);
                        string fwav = Path.Combine(wavDir, name + "_far.wav");
                        Wav.Write(fwav, fb, 16, true);
                        if (!wavOnly) Paths.EncodeOgg(fwav, Path.Combine(outDir, name + "_far.ogg"), 4f);
                    }
                    n++;
                }
                if (d.FarAmount > 0f) for (int v = 0; v < d.Variants; v++) farFiles.Add($"sfx/{(d.Variants > 1 ? $"{d.Id}_{v}" : d.Id)}_far.ogg");
                sb.Append($"    \"{d.Id}\": {{ \"files\": [{string.Join(", ", files.Select(f => "\"" + f + "\""))}], \"far\": [{string.Join(", ", farFiles.Select(f => "\"" + f + "\""))}], " +
                          $"\"bus\": \"{d.Bus}\", \"levelDb\": {J(d.LevelDb)}, \"loop\": {(d.Loop ? "true" : "false")}, \"spatial\": {(d.Spatial ? "true" : "false")}, " +
                          $"\"minDist\": {J(d.MinDist)}, \"maxDist\": {J(d.MaxDist)}, \"nearFade\": [{J(d.NearFadeFrom)}, {J(d.NearFadeTo)}], \"pitchVar\": {J(d.PitchVar)}, " +
                          $"\"duck\": [{J(d.DuckAmount)}, {J(d.DuckSeconds)}], \"priority\": {J(d.Priority)}, \"arrivalDelay\": {J(d.ArrivalDelay)}, \"note\": \"{d.Note}\" }}");
                sb.AppendLine(d == defs.Last() ? "" : ",");
            }
            sb.AppendLine("  }\n}");
            if (want.Count == 0 || want.Contains("all"))
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "sfx.json"), sb.ToString());
                File.WriteAllText(Path.Combine(Paths.Render, "sfx_report.csv"), report.ToString());
            }
            Console.WriteLine($"{n} sound files made");
            return 0;
        }
    }
}
