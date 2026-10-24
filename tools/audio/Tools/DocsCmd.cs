using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Monolith.AudioTools
{
    /// <summary>MnAudio docs: writes the list of every sound the player may replace (Audio/AUDIO_LIST.txt) from the catalogs.</summary>
    public static class DocsCmd
    {
        public static int Run(string[] args)
        {
            string root = Paths.StreamingAudio;
            var sb = new StringBuilder();
            sb.AppendLine("MONOLITH - EVERY SOUND YOU CAN REPLACE");
            sb.AppendLine();
            sb.AppendLine("Drop a file with the same name (any of .wav, .ogg, .mp3, in the same folders) into the Audio folder beside Monolith.exe and it plays instead.");
            sb.AppendLine("The name is the path below without the extension. Lengths and levels are those of the built-in sound, to match.");
            sb.AppendLine();
            sb.AppendLine("MUSIC  (music\\<name>)");
            var music = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "music", "music.json"))).RootElement;
            var desc = new System.Collections.Generic.Dictionary<string, string>
            {
                ["ambient"] = "the quiet bed (a loop)", ["battle1"] = "first contact (a loop)", ["battle2"] = "the fight (a loop)", ["battle3"] = "the worst of it (a loop)",
                ["victory"] = "plays once at the win", ["failure"] = "plays once at a lost mission", ["death"] = "plays once when you die",
                ["sting_friendly"] = "friendly ship arrives, quiet", ["sting_friendly_battle"] = "friendly ship arrives, in battle", ["sting_enemy"] = "Earth arrives, quiet",
                ["sting_enemy_battle"] = "Earth arrives, in battle", ["sting_goal"] = "an objective is done", ["sting_fail"] = "an objective is lost",
            };
            foreach (var t in music.GetProperty("tracks").EnumerateObject())
                sb.AppendLine($"  music\\{t.Name,-24} {t.Value.GetProperty("seconds").GetDouble(),6:0.0} s   {(desc.TryGetValue(t.Name, out var d) ? d : "")}");
            sb.AppendLine($"  The loops are {music.GetProperty("loopSeconds").GetDouble():0.0} s ({music.GetProperty("bpm").GetDouble():0} BPM, 4/4, 32 bars): a loop of that exact length is cross-faded in step with the others; any other length starts from its beginning on the next bar.");
            sb.AppendLine();
            sb.AppendLine("EFFECTS  (sfx\\<name>; \"_far\" files are the distant versions; _0 _1 _2 are variants played at random)");
            var sfx = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "sfx", "sfx.json"))).RootElement.GetProperty("cues");
            foreach (var c in sfx.EnumerateObject())
            {
                string note = c.Value.TryGetProperty("note", out var n) ? n.GetString() : "";
                var files = c.Value.GetProperty("files").EnumerateArray().Select(f => Path.GetFileNameWithoutExtension(f.GetString())).ToList();
                var far = c.Value.GetProperty("far").EnumerateArray().Select(f => Path.GetFileNameWithoutExtension(f.GetString())).ToList();
                bool loop = c.Value.GetProperty("loop").GetBoolean();
                sb.AppendLine($"  sfx\\{string.Join(", ", files),-40}{(far.Count > 0 ? "  +far: " + string.Join(", ", far) : "")}{(loop ? "  (loops)" : "")}  {note}");
            }
            sb.AppendLine();
            sb.AppendLine("RADIO  (vo\\<voice>\\<line>; your recording goes through the game's radio filter unless a .json beside it says { \"radio\": false })");
            sb.AppendLine("  Each line is tagged calm, tense or frantic by the moment it is said in (the crew under fire is breathless and shouting; the quiet moments and Earth are calm).");
            sb.AppendLine("  A line made at two intensities (the wingmen's acknowledgements) has a file for each, ending _tense and _frantic, and the game plays the one the music asks for:");
            sb.AppendLine("  tense on the quiet bed and at first contact, frantic in the fight. Replace one and only that one changes; replace both to change the line everywhere.");
            sb.AppendLine("  Your recording of a tense or frantic line gets the matching radio treatment (more distortion and a faster squelch as it climbs).");
            var vo = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "vo", "index.json"))).RootElement;
            foreach (var g in vo.GetProperty("clips").EnumerateArray().GroupBy(c => c.GetProperty("voice").GetString()).OrderBy(g => g.Key))
            {
                sb.AppendLine($"  [{g.Key}]  ({vo.GetProperty("voices").GetProperty(g.Key).GetString()} radio)");
                foreach (var c in g)
                {
                    string inten = c.TryGetProperty("intensity", out var ip) ? ip.GetString() : "calm";
                    sb.AppendLine($"    {Path.ChangeExtension(c.GetProperty("file").GetString(), null).Replace('/', '\\'),-52} {(inten == "calm" ? "" : "[" + inten + "] ")}{c.GetProperty("text").GetString()}");
                }
            }
            File.WriteAllText(Path.Combine(root, "AUDIO_LIST.txt"), sb.ToString());
            Console.WriteLine("wrote AUDIO_LIST.txt");
            return 0;
        }
    }
}
