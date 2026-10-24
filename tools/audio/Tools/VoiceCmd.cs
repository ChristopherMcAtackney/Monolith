using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    /// <summary>
    /// MnAudio voice [--wav-only] [--only voice]: turn the raw text-to-speech lines (audio_src/voice/raw, made by gen_voice.py in WSL) into radio:
    /// each line is directed to its intensity (calm, tense or frantic: sped up, squeezed of pauses, breathed; VoiceActing.cs), run through the radio
    /// filter for its voice's class at that intensity (more drive, clipping and dropouts as it climbs; Radio.cs), levelled (shouting is louder),
    /// encoded to OGG into Game/Assets/StreamingAssets/Audio/vo, and indexed so a line of text finds its clip.
    /// </summary>
    public static class VoiceCmd
    {
        public static float TargetLufs(RadioStyle style, RadioIntensity i)
        {
            if (style == RadioStyle.Earth) return -21f;
            float calm = style == RadioStyle.Alien ? -22.5f : -23f;
            return calm + (i == RadioIntensity.Tense ? 1.5f : i == RadioIntensity.Frantic ? 3.5f : 0f);
        }

        public static RadioIntensity ParseIntensity(string s) => s == "frantic" ? RadioIntensity.Frantic : s == "tense" ? RadioIntensity.Tense : RadioIntensity.Calm;

        public static int Run(string[] args)
        {
            bool wavOnly = args.Contains("--wav-only");
            string only = args.SkipWhile(a => a != "--only").Skip(1).FirstOrDefault();
            string voiceDir = Path.Combine(Paths.AudioSrc, "voice");
            var cast = JsonDocument.Parse(File.ReadAllText(Path.Combine(voiceDir, "cast.json"))).RootElement;
            var lines = JsonDocument.Parse(File.ReadAllText(Path.Combine(voiceDir, "voice_lines.json"))).RootElement.GetProperty("lines");
            string rawDir = Path.Combine(voiceDir, "raw");
            string wavDir = Path.Combine(Paths.Render, "vo");
            string outDir = Path.Combine(Paths.StreamingAudio, "vo");
            var voices = new Dictionary<string, string>();
            foreach (var v in cast.GetProperty("voices").EnumerateObject()) voices[v.Name] = v.Value.GetProperty("fx").GetString();

            var clips = new List<(string voice, string text, string file, double seconds, float lufs, string inten)>();
            int made = 0, missing = 0;
            var notes = new StringBuilder();
            foreach (var l in lines.EnumerateArray())
            {
                string voice = l.GetProperty("voice").GetString(), file = l.GetProperty("file").GetString(), text = l.GetProperty("text").GetString();
                string intenName = l.TryGetProperty("intensity", out var ip) ? ip.GetString() : "calm";
                if (only != null && voice != only) continue;
                string raw = Path.Combine(rawDir, file);
                if (!File.Exists(raw)) { Console.WriteLine("  no raw clip yet: " + file); missing++; continue; }
                var buf = Wav.Read(raw);
                var style = RadioFx.StyleOf(voices[voice]);
                var inten = style == RadioStyle.Earth ? RadioIntensity.Calm : ParseIntensity(intenName);
                uint seed = Dsp.StableHash(file) % 100000 + 1;
                var dry = VoiceActing.Direct(buf.Mono(), buf.SampleRate, inten, text, seed, out string note);
                var y = RadioFx.Process(dry, buf.SampleRate, style, seed, TargetLufs(style, inten), inten);
                var pcm = PcmBuffer.FromMono(RadioFx.OutRate, y);
                string rel = file.Replace(".wav", "");
                string wav = Path.Combine(wavDir, rel + ".wav");
                Wav.Write(wav, pcm, 16, true);
                var m = LoudnessMeter.Measure(pcm);
                if (!wavOnly) Paths.EncodeOgg(wav, Path.Combine(outDir, rel + ".ogg"), 4f);
                clips.Add((voice, text, "vo/" + rel + ".ogg", y.Length / (double)RadioFx.OutRate, m.Integrated, intenName));
                notes.AppendLine($"{rel,-48} {intenName,-8} {y.Length / (double)RadioFx.OutRate,5:0.0} s {m.Integrated,6:0.0} LUFS peak {m.PeakDb,5:0.0}  {note}");
                made++;
            }
            // the index: text to clip, plus who speaks as whom
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"format\": \"monolith-voice-index/2\",");
            sb.AppendLine("  \"about\": \"Maps a radio line to its clip. A line is found by the speaker's voice and the text (upper case, spaces collapsed); a line made at more than one intensity (calm, tense, frantic) has a clip for each, and the one nearest the moment's intensity (the music state) plays; if that voice has no clip for it, any voice's clip of the same text plays; with none, the line gets a squelch and is logged. A file of the same name in the Audio folder beside the game replaces a clip.\",");
            sb.AppendLine("  \"speakers\": " + cast.GetProperty("speakers").GetRawText() + ",");
            sb.AppendLine("  \"defaults\": " + cast.GetProperty("defaults").GetRawText() + ",");
            sb.AppendLine("  \"voices\": {" + string.Join(", ", voices.Select(v => $"\"{v.Key}\": \"{v.Value}\"")) + "},");
            sb.AppendLine("  \"clips\": [");
            for (int i = 0; i < clips.Count; i++)
            {
                var c = clips[i];
                string norm = System.Text.RegularExpressions.Regex.Replace(c.text.Trim().ToUpperInvariant(), "\\s+", " ");
                sb.Append($"    {{ \"voice\": \"{c.voice}\", \"text\": {JsonSerializer.Serialize(c.text)}, \"norm\": {JsonSerializer.Serialize(norm)}, \"intensity\": \"{c.inten}\", \"file\": \"{c.file}\", \"seconds\": {c.seconds:0.00}, \"lufs\": {c.lufs:0.0} }}");
                sb.AppendLine(i == clips.Count - 1 ? "" : ",");
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            Directory.CreateDirectory(outDir);
            if (only == null) File.WriteAllText(Path.Combine(outDir, "index.json"), sb.ToString());
            if (only == null && !wavOnly)
            {
                // a clip from an earlier cast or an earlier intensity that no line asks for any more must not ship
                var keep = new HashSet<string>(clips.Select(c => Path.GetFullPath(Path.Combine(Paths.StreamingAudio, c.file))), StringComparer.OrdinalIgnoreCase);
                foreach (var f in Directory.GetFiles(outDir, "*.ogg", SearchOption.AllDirectories))
                    if (!keep.Contains(Path.GetFullPath(f))) { File.Delete(f); Console.WriteLine("  removed stale " + Path.GetRelativePath(outDir, f)); }
                foreach (var d in Directory.GetDirectories(outDir)) if (Directory.GetFileSystemEntries(d).Length == 0) Directory.Delete(d);
            }
            Directory.CreateDirectory(Paths.Render);
            File.WriteAllText(Path.Combine(Paths.Render, "voice_directing.txt"), notes.ToString());
            Console.WriteLine($"{made} radio clips made, {missing} raw clips missing, total {clips.Sum(c => c.seconds):0} s");
            foreach (var g in clips.GroupBy(c => c.inten).OrderBy(g => g.Key))
                Console.WriteLine($"  {g.Key,-8} {g.Count(),3} clips, mean {g.Average(c => c.seconds):0.0} s, mean {g.Average(c => c.lufs):0.0} LUFS");
            return 0;
        }
    }
}
