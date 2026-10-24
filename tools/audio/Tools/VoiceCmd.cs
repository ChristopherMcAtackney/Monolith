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
    /// MnAudio voice [--wav-only]: turn the raw text-to-speech lines (audio_src/voice/raw, made by gen_voice.py in WSL) into radio:
    /// the radio filter for each voice's class, a loudness level, OGG into Game/Assets/StreamingAssets/Audio/vo, and the index that maps line text to clip.
    /// </summary>
    public static class VoiceCmd
    {
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

            var clips = new List<(string voice, string text, string file, double seconds, float lufs)>();
            int made = 0, missing = 0;
            foreach (var l in lines.EnumerateArray())
            {
                string voice = l.GetProperty("voice").GetString(), file = l.GetProperty("file").GetString(), text = l.GetProperty("text").GetString();
                if (only != null && voice != only) continue;
                string raw = Path.Combine(rawDir, file);
                if (!File.Exists(raw)) { Console.WriteLine("  no raw clip yet: " + file); missing++; continue; }
                var buf = Wav.Read(raw);
                var style = RadioFx.StyleOf(voices[voice]);
                uint seed = Dsp.StableHash(file) % 100000 + 1;
                float target = style == RadioStyle.Earth ? -21f : style == RadioStyle.Alien ? -22.5f : -23f;
                var y = RadioFx.Process(buf.Mono(), buf.SampleRate, style, seed, target);
                var pcm = PcmBuffer.FromMono(RadioFx.OutRate, y);
                string rel = file.Replace(".wav", "");
                string wav = Path.Combine(wavDir, rel + ".wav");
                Wav.Write(wav, pcm, 16, true);
                var m = LoudnessMeter.Measure(pcm);
                if (!wavOnly) Paths.EncodeOgg(wav, Path.Combine(outDir, rel + ".ogg"), 4f);
                clips.Add((voice, text, "vo/" + rel + ".ogg", y.Length / (double)RadioFx.OutRate, m.Integrated));
                made++;
            }
            // the index: text to clip, plus who speaks as whom
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"format\": \"monolith-voice-index/1\",");
            sb.AppendLine("  \"about\": \"Maps a radio line to its clip. A line is found by the speaker's voice and the text (upper case, spaces collapsed); if that voice has no clip for it, any voice's clip of the same text plays; with none, the line gets a squelch and is logged. A file of the same name in the Audio folder beside the game replaces a clip.\",");
            sb.AppendLine("  \"speakers\": " + cast.GetProperty("speakers").GetRawText() + ",");
            sb.AppendLine("  \"defaults\": " + cast.GetProperty("defaults").GetRawText() + ",");
            sb.AppendLine("  \"voices\": {" + string.Join(", ", voices.Select(v => $"\"{v.Key}\": \"{v.Value}\"")) + "},");
            sb.AppendLine("  \"clips\": [");
            for (int i = 0; i < clips.Count; i++)
            {
                var c = clips[i];
                string norm = System.Text.RegularExpressions.Regex.Replace(c.text.Trim().ToUpperInvariant(), "\\s+", " ");
                sb.Append($"    {{ \"voice\": \"{c.voice}\", \"text\": {JsonSerializer.Serialize(c.text)}, \"norm\": {JsonSerializer.Serialize(norm)}, \"file\": \"{c.file}\", \"seconds\": {c.seconds:0.00}, \"lufs\": {c.lufs:0.0} }}");
                sb.AppendLine(i == clips.Count - 1 ? "" : ",");
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            Directory.CreateDirectory(outDir);
            if (only == null) File.WriteAllText(Path.Combine(outDir, "index.json"), sb.ToString());
            Console.WriteLine($"{made} radio clips made, {missing} raw clips missing, total {clips.Sum(c => c.seconds):0} s");
            return 0;
        }
    }
}
