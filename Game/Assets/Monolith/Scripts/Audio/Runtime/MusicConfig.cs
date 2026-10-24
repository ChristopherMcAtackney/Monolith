using System;
using System.Collections.Generic;
using System.IO;
using Monolith.Core;
using Newtonsoft.Json.Linq;

namespace Monolith.Audio
{
    /// <summary>One slot of the player's music.json: which file plays there and how it fades. Every field is optional; a slot with no file keeps the built-in synthesised track and takes only the settings.</summary>
    public sealed class MusicSlotConfig
    {
        public string Section, Key;                                    // "themes", "escort/lector"
        public string File, FullPath;                                  // as written, and made absolute (null when the slot names no file)
        public float VolumeDb;
        public float FadeIn = float.NaN, FadeOut = float.NaN;          // seconds; NaN: the slot's default
        public bool? Loop;
        public float Bpm;                                              // 0: unknown (changes are time-based)
        public int BeatsPerBar = 4;
        public float LoopStart, LoopEnd;                               // seconds inside the file
        public float Priority = float.NaN, DuckDb = float.NaN;         // themes
        public float LoopFade, StartAt;                                // seconds: cross-fade the end of the loop into its lead-in; where the first pass starts
        public bool Off;                                               // the fight slot: "off": true is no fight theme at all (the intensity tracks play, as before)
        public string UnderThemes;                                     // the fight slot, under an event theme: "pause" (the default) or "duck"
        public float[] IntensityDb;                                    // the fight slot: dB at battle1, battle2, battle3 (null: the built-in swell)
        public string Mode;                                            // themes: "duck" or "replace"
        public string Describe() => $"{Section}/{Key}" + (File != null ? " <- " + File : " (built-in sound)");
    }

    /// <summary>
    /// The player's music.json (beside Monolith.exe, or in the Audio folder): every slot of the score mapped to their own MP3, WAV or OGG with its fades, level, loop and tempo.
    /// Sections: <c>base</c> (ambient, battle1, battle2, battle3), <c>stings</c>, <c>endings</c> (victory, failure, death) and <c>themes</c> (a mission's moments: "escort/lector",
    /// or just "lector" for every mission). A slot name may be prefixed with a mission id ("escort/battle2") to apply to that mission only. Anything left out plays the built-in
    /// synthesised track. The file is read again when it changes.
    /// </summary>
    public sealed class MusicConfig
    {
        public static readonly string[] BaseSlots = { "ambient", "battle1", "battle2", "battle3", "fight" };
        public static readonly string[] StingSlots = { "enemy_arrival", "enemy_arrival_battle", "friendly_arrival", "friendly_arrival_battle", "goal_complete", "goal_failed" };
        public static readonly string[] EndingSlots = { "victory", "failure", "death" };
        public static readonly string[] Sections = { "base", "stings", "endings", "themes" };

        public string Path;
        public DateTime Time;
        public readonly Dictionary<string, MusicSlotConfig> Slots = new Dictionary<string, MusicSlotConfig>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Problems = new List<string>();

        static string K(string section, string key) => section + "|" + key;

        /// <summary>The slot for <paramref name="key"/> in a section: this mission's own entry first ("escort/lector"), then the general one ("lector"); null when the player named neither.</summary>
        public MusicSlotConfig Find(string section, string mission, string key)
        {
            if (!string.IsNullOrEmpty(mission) && Slots.TryGetValue(K(section, mission + "/" + key), out var m)) return m;
            return Slots.TryGetValue(K(section, key), out var g) ? g : null;
        }

        public static MusicConfig Load(string path)
        {
            var c = new MusicConfig { Path = path, Time = File.GetLastWriteTimeUtc(path) };
            JObject root;
            try { root = JObject.Parse(File.ReadAllText(path)); }
            catch (Exception e) { c.Problems.Add($"music.json could not be read: {e.Message}"); return c; }
            string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? ".";
            foreach (var secProp in root.Properties())
            {
                if (secProp.Name.StartsWith("_") || secProp.Name == "about" || secProp.Name == "format") continue;
                string section = secProp.Name.ToLowerInvariant();
                if (Array.IndexOf(Sections, section) < 0) { c.Problems.Add($"music.json: '{secProp.Name}' is not a section (base, stings, endings, themes)"); continue; }
                if (!(secProp.Value is JObject entries)) { c.Problems.Add($"music.json: '{secProp.Name}' should be an object of slots"); continue; }
                foreach (var e in entries.Properties())
                {
                    if (e.Name.StartsWith("_")) continue;
                    if (!(e.Value is JObject o)) { c.Problems.Add($"music.json: {section}/{e.Name} should be an object like {{ \"file\": \"my_track.mp3\" }}"); continue; }
                    var slot = new MusicSlotConfig { Section = section, Key = e.Name };
                    string file = (string)o["file"];
                    if (!string.IsNullOrWhiteSpace(file))
                    {
                        slot.File = file;
                        slot.FullPath = System.IO.Path.IsPathRooted(file) ? file : System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, file));
                    }
                    slot.VolumeDb = (float?)o["volume"] ?? (float?)o["volumeDb"] ?? 0f;
                    if (o["fadeIn"] != null) slot.FadeIn = (float)o["fadeIn"];
                    if (o["fadeOut"] != null) slot.FadeOut = (float)o["fadeOut"];
                    if (o["loop"] != null && o["loop"].Type == JTokenType.Boolean) slot.Loop = (bool)o["loop"];
                    slot.Bpm = (float?)o["bpm"] ?? 0f;
                    slot.BeatsPerBar = (int?)o["beatsPerBar"] ?? 4;
                    slot.LoopStart = (float?)o["loopStart"] ?? 0f; slot.LoopEnd = (float?)o["loopEnd"] ?? 0f;
                    slot.LoopFade = (float?)o["loopFade"] ?? 0f; slot.StartAt = (float?)o["startAt"] ?? 0f;
                    slot.Off = o["off"] != null && o["off"].Type == JTokenType.Boolean && (bool)o["off"];
                    slot.UnderThemes = (string)o["underThemes"];
                    if (slot.UnderThemes != null && slot.UnderThemes != "pause" && slot.UnderThemes != "duck") { c.Problems.Add($"music.json: {section}/{e.Name}: underThemes '{slot.UnderThemes}' should be pause or duck"); slot.UnderThemes = null; }
                    if (o["intensity"] is JObject idb) slot.IntensityDb = new[] { (float?)idb["battle1"] ?? 0f, (float?)idb["battle2"] ?? 0f, (float?)idb["battle3"] ?? 0f };
                    if (o["priority"] != null) slot.Priority = (float)o["priority"];
                    if (o["duck"] != null) slot.DuckDb = (float)o["duck"];
                    slot.Mode = (string)o["mode"];
                    if (slot.Mode != null && slot.Mode != "duck" && slot.Mode != "replace") { c.Problems.Add($"music.json: {section}/{e.Name}: mode '{slot.Mode}' should be duck or replace"); slot.Mode = null; }
                    // typos in a slot name are the likeliest mistake: say so
                    string bare = e.Name.Contains("/") ? e.Name.Substring(e.Name.LastIndexOf('/') + 1) : e.Name;
                    string[] known = section == "base" ? BaseSlots : section == "stings" ? StingSlots : section == "endings" ? EndingSlots : null;
                    if (known != null && Array.IndexOf(known, bare) < 0) c.Problems.Add($"music.json: {section}/{e.Name} is not a slot of that section ({string.Join(", ", known)}; a mission may prefix its id: escort/{known[0]})");
                    if (slot.FullPath != null && !File.Exists(slot.FullPath)) c.Problems.Add($"music.json: {section}/{e.Name}: the file '{file}' is not there ({slot.FullPath}); the built-in sound plays");
                    c.Slots[K(section, e.Name)] = slot;
                }
            }
            return c;
        }
    }
}
