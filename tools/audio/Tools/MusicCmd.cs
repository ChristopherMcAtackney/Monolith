using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    /// <summary>MnAudio music [cue ...|all] [--wav-only]: render the score from its MIDI.</summary>
    public static class MusicCmd
    {
        /// <summary>MnAudio stems cue: every track of a cue on its own, with its loudness and spectrum, to set the mix by numbers.</summary>
        public static int Stems(string[] args)
        {
            var specs = MusicRender.LoadSpecs(Path.Combine(Paths.AudioSrc, "music.json"));
            string midiDir = Path.Combine(Paths.AudioSrc, "midi");
            foreach (var n in args.Skip(1).Where(a => !a.StartsWith("--")))
            {
                var spec = specs[n];
                var song = MidiSong.Load(Path.Combine(midiDir, spec.Midi));
                Console.WriteLine($"== {n}");
                Console.WriteLine("track                  dB    LUFS-I  mom.max  peak   | bands 31 63 125 250 500 1k 2k 4k 8k 16k (dB re loudest)");
                foreach (var tr in song.Tracks)
                {
                    if (tr.Notes.Count == 0) continue;
                    spec.SoloTracks = new[] { tr.Name };
                    spec.Lufs = -200;
                    var buf = MusicRender.Render(spec, midiDir, out var rep, false);
                    var m = LoudnessMeter.Measure(buf);
                    spec.Tracks.TryGetValue(tr.Name, out var ts);
                    var bands = Spectro.Bands(buf);
                    float mx = bands.Max();
                    Console.WriteLine($"{tr.Name,-22} {(ts?.Db ?? 0):0.0,5} {m.Integrated,7:0.0} {m.MaxMomentary,8:0.0} {m.PeakDb,6:0.0}   | " + string.Join(" ", bands.Select(b => (b - mx).ToString("0").PadLeft(3))));
                }
            }
            return 0;
        }

        public static int Run(string[] args)
        {
            bool wavOnly = args.Contains("--wav-only");
            string solo = args.SkipWhile(a => a != "--solo").Skip(1).FirstOrDefault();
            string mute = args.SkipWhile(a => a != "--mute").Skip(1).FirstOrDefault();
            string only = args.SkipWhile(a => a != "--out").Skip(1).FirstOrDefault();
            var names = new List<string>();
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--solo" || args[i] == "--mute" || args[i] == "--out") { i++; continue; }
                if (!args[i].StartsWith("--")) names.Add(args[i]);
            }
            string specPath = Path.Combine(Paths.AudioSrc, "music.json");
            var specs = MusicRender.LoadSpecs(specPath);
            bool shipOnly = args.Contains("--ship-only");
            if (names.Count == 0 || names.Contains("all")) names = specs.Keys.ToList();
            if (shipOnly) names = new List<string>();                       // MnAudio ship: only copy the shipped tracks and rewrite the catalog
            string midiDir = Path.Combine(Paths.AudioSrc, "midi");
            Directory.CreateDirectory(Paths.Render);
            string outDir = Path.Combine(Paths.StreamingAudio, "music");
            var reports = new List<CueReport>();
            foreach (var n in names)
            {
                if (!specs.TryGetValue(n, out var spec)) { Console.Error.WriteLine("no cue " + n); return 64; }
                var sw = System.Diagnostics.Stopwatch.StartNew();
                if (solo != null || mute != null)
                {
                    spec.SoloTracks = solo?.Split(',');
                    spec.MuteTracks = mute?.Split(',');
                    spec.Lufs = -200;          // no loudness normalising of a stem: keep the mix level
                }
                var buf = MusicRender.Render(spec, midiDir, out var rep);
                string wav = Path.Combine(Paths.Render, n + (solo != null ? "_solo_" + solo.Replace(',', '+').Replace(':', '_') : "") + ".wav");
                if (only != null) wav = only;
                Wav.Write(wav, buf, 16, true);
                reports.Add(rep);
                Console.WriteLine($"  -> {n}: {rep.Seconds:0.00} s, {rep.Lufs:0.0} LUFS, peak {rep.SamplePeakDb:0.0} dBFS, true peak {rep.TruePeakDb:0.0} dBTP"
                    + (rep.Loop ? $", seam step {rep.LoopSeamStep:0.0000} vs {rep.LoopSeamBaseline:0.0000} elsewhere" : "") + $" ({sw.Elapsed.TotalSeconds:0.0} s)");
                if (!wavOnly && solo == null && mute == null) Paths.EncodeOgg(wav, Path.Combine(outDir, (spec.Track ?? n) + ".ogg"));
            }
            if ((!wavOnly || shipOnly) && solo == null && mute == null)
            {
                // the runtime catalog: merge with what is already there so rendering one cue does not drop the others
                string catPath = Path.Combine(outDir, "music.json");
                var entries = new SortedDictionary<string, string>();
                if (File.Exists(catPath))
                {
                    var old = JsonDocument.Parse(File.ReadAllText(catPath)).RootElement.GetProperty("tracks");
                    foreach (var p in old.EnumerateObject()) entries[p.Name] = p.Value.GetRawText();
                }
                foreach (var rep in reports)
                {
                    var spec = specs[rep.Name];
                    string track = spec.Track ?? rep.Name;
                    entries[track] = "{ \"file\": \"music/" + track + ".ogg\", \"loop\": " + (rep.Loop ? "true" : "false") + ", \"seconds\": " + rep.Seconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
                        + ", \"bpm\": " + rep.Bpm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + ", \"bars\": " + rep.Bars + ", \"lufs\": " + rep.Lufs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                        + ", \"truePeakDb\": " + rep.TruePeakDb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " }";
                }
                // the themes: a theme track's own section (the cue's "theme" object in audio_src/music.json), merged with what is there, written next to the tracks
                var themes = new SortedDictionary<string, string>();
                if (File.Exists(catPath))
                {
                    var old = JsonDocument.Parse(File.ReadAllText(catPath)).RootElement;
                    if (old.TryGetProperty("themes", out var ot)) foreach (var p in ot.EnumerateObject()) themes[p.Name] = p.Value.GetRawText();
                }
                var specDoc = JsonDocument.Parse(File.ReadAllText(specPath), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }).RootElement.GetProperty("cues");
                foreach (var rep in reports)
                {
                    if (!specDoc.TryGetProperty(rep.Name, out var cue) || !cue.TryGetProperty("theme", out var th)) continue;
                    string track = specs[rep.Name].Track ?? rep.Name;
                    var ic = System.Globalization.CultureInfo.InvariantCulture;
                    string F(string k, double d) => (th.TryGetProperty(k, out var v) ? v.GetDouble() : d).ToString("0.##", ic);
                    themes[th.GetProperty("name").GetString()] = "{ \"track\": \"" + track + "\", \"fadeIn\": " + F("fadeIn", 4) + ", \"fadeOut\": " + F("fadeOut", 6) + ", \"priority\": " + F("priority", 10)
                        + ", \"mode\": \"" + (th.TryGetProperty("mode", out var md) ? md.GetString() : "duck") + "\", \"duckDb\": " + F("duckDb", -10) + ", \"volumeDb\": " + F("volumeDb", 0) + " }";
                }
                // the shipped tracks: music that is not rendered here (the user's own fight theme), copied into the game and listed in the catalog with how it plays ("shipped" in audio_src/music.json)
                var rootSpec = JsonDocument.Parse(File.ReadAllText(specPath), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }).RootElement;
                if (rootSpec.TryGetProperty("shipped", out var shipped))
                    foreach (var sh in shipped.EnumerateObject())
                    {
                        if (sh.Name.StartsWith("_")) continue;
                        var o = sh.Value;
                        string src = Path.Combine(Paths.AudioSrc, o.GetProperty("source").GetString().Replace('/', Path.DirectorySeparatorChar));
                        string file = o.GetProperty("file").GetString();
                        string dst = Path.Combine(Paths.StreamingAudio, file.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(dst));
                        if (!File.Exists(dst) || new FileInfo(dst).Length != new FileInfo(src).Length) File.Copy(src, dst, true);
                        var node = System.Text.Json.Nodes.JsonNode.Parse(o.GetRawText());
                        node.AsObject().Remove("source");
                        entries[sh.Name] = node.ToJsonString();
                        Console.WriteLine($"  shipped {sh.Name}: {src} -> {dst}");
                    }
                string loopSec = entries.ContainsKey("battle1") ? JsonDocument.Parse(entries["battle1"]).RootElement.GetProperty("seconds").GetDouble().ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) : "68.571";
                var sb = new StringBuilder();
                sb.Append("{" + Environment.NewLine);
                sb.Append("  \"format\": \"monolith-music/1\"," + Environment.NewLine);
                sb.Append("  \"about\": \"The score. Loops share one length and one harmony so the music changes on a beat. A file with the same name in the Audio folder beside the game (music/<track>.mp3, .wav or .ogg) replaces a track; a sidecar <track>.json may give loopStart, loopEnd (seconds) and volume (dB).\"," + Environment.NewLine);
                sb.Append("  \"bpm\": 112, \"beatsPerBar\": 4, \"loopSeconds\": " + loopSec + "," + Environment.NewLine);
                sb.Append("  \"tracks\": {" + Environment.NewLine);
                sb.Append(string.Join("," + Environment.NewLine, entries.Select(e => "    \"" + e.Key + "\": " + e.Value)));
                sb.Append(Environment.NewLine + "  }," + Environment.NewLine);
                sb.Append("  \"themes\": {" + Environment.NewLine);
                sb.Append(string.Join("," + Environment.NewLine, themes.Select(e => "    \"" + e.Key + "\": " + e.Value)));
                sb.Append(Environment.NewLine + "  }" + Environment.NewLine + "}" + Environment.NewLine);
                File.WriteAllText(catPath, sb.ToString());
            }
            return 0;
        }
    }
}
