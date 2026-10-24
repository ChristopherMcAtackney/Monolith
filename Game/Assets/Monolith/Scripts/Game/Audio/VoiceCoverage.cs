using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Monolith.Core;
using Monolith.Mission;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// Is every radio line of every built-in mission voiced? Reads the missions' own JSON (the messages, and the inline radio and callOut actions) and the voice index
    /// (StreamingAssets/Audio/vo/index.json), without loading any sound, and names every line whose speaker has no recording of it. A line nobody has voiced plays
    /// as a squelch, which a playtester hears as clicks and no voice: so the audio self-test and the mission self-test fail on it. Fix: <c>tools\audio\voice.ps1</c>.
    /// </summary>
    public static class VoiceCoverage
    {
        public const string Fix = "run tools\\audio\\voice.ps1 (it gathers the lines, voices the new ones, rebuilds the index), rebuild the player";

        /// <summary>
        /// Lines the last <see cref="Unvoiced"/> call skipped because they are drafts pending the user's approval (<c>"pending": true</c> on the line, or
        /// <c>"linesPending": true</c> on the mission): no voice is made for a line before the user approves it (design: the dialogue process). They play as
        /// on-screen text with the radio's squelch. A new speaker of a draft (a ship only this mission has) goes in audio_src/voice/cast.json; the voice
        /// index learns it at the voice run that voices the approved line, so until then it is counted here too, not failed.
        /// </summary>
        public static int Pending { get; private set; }
        public static readonly List<string> PendingLines = new List<string>();

        static string Norm(string s) => Regex.Replace((s ?? "").Trim().ToUpperInvariant(), @"\s+", " ");

        /// <summary>The lines with no recording, as readable text; <paramref name="lines"/> is how many (speaker, text) pairs were looked at.</summary>
        public static List<string> Unvoiced(out int lines, params (string name, string json)[] extra)
        {
            int count = 0;
            var gaps = new List<string>();
            lines = 0;
            string path = Path.Combine(Application.streamingAssetsPath, "Audio", "vo", "index.json");
            if (!File.Exists(path)) { gaps.Add("Audio/vo/index.json is missing: no radio line is voiced"); lines = 0; return gaps; }
            var idx = JObject.Parse(File.ReadAllText(path));
            var speakers = new Dictionary<string, string>();
            foreach (var p in ((JObject)idx["speakers"]).Properties()) speakers[p.Name.ToLowerInvariant()] = (string)p.Value;
            var defaults = (JObject)idx["defaults"];
            var wingmen = defaults["wingmanVoices"].ToObject<string[]>();
            string player = (string)defaults["playerVoice"] ?? "sable1";
            var have = new HashSet<string>();
            foreach (JObject c in (JArray)idx["clips"]) have.Add((string)c["voice"] + "|" + (string)c["norm"]);

            List<string> VoicesOf(string name, string where, List<string> gapsOut)
            {
                if (string.IsNullOrEmpty(name)) { gapsOut.Add($"{where}: the line names no speaker"); return new List<string>(); }
                if (name == MissionFile.AnyWingman) { var l = new List<string>(wingmen) { player }; return l; }
                string key = name.Trim().ToLowerInvariant();
                if (speakers.TryGetValue(key, out var v)) return new List<string> { v };
                var m = Regex.Match(key, @"^(.*?)\s*\d+$");
                if (m.Success && speakers.TryGetValue(m.Groups[1].Value, out v)) return new List<string> { v };
                gapsOut.Add($"{where}: nobody is cast as '{name}' (add it to \"speakers\" in audio_src/voice/cast.json)");
                return new List<string>();
            }

            Pending = 0; PendingLines.Clear();
            void Need(string speaker, string text, string where, bool pending)
            {
                // a draft's speaker may be new: cast.json names it, and the voice index learns it at the next voice run (the one that voices the approved line)
                var uncast = pending ? new List<string>() : gaps;
                var voices = VoicesOf(speaker, where, uncast);
                if (pending && uncast.Count > 0) { count++; Pending++; PendingLines.Add($"{where}: {speaker} (not in the voice index yet) '{text}'"); }
                foreach (var v in voices)
                {
                    count++;
                    if (have.Contains(v + "|" + Norm(text))) continue;
                    if (pending) { Pending++; PendingLines.Add($"{where}: {speaker} ({v}) '{text}'"); }
                    else gaps.Add($"{where}: {speaker} ({v}) '{text}'");
                }
            }

            var sources = new List<(string name, string json)>();
            foreach (var name in MissionLibrary.BuiltIn())
                foreach (var t in Resources.LoadAll<TextAsset>(MissionLibrary.ResourceFolder))
                    if (MissionLibrary.NameOf(t.name) == name) sources.Add((name, t.text));
            sources.AddRange(extra);                       // a mission handed in (the self-test's own negative control)
            foreach (var (name, json) in sources)
            {
                var root = JObject.Parse(Regex.Replace(json, @"(?m)^\s*//.*$", ""));
                bool filePending = root["linesPending"] is JValue lp && (bool)lp;
                bool PendingOf(JObject line) => line["pending"] is JValue pv ? (bool)pv : filePending;
                if (root["messages"] is JArray msgs)
                    foreach (JObject m in msgs)
                    {
                        string text = (string)m["text"], from = (string)m["from"], fb = (string)m["fallbackFrom"], where = $"{name}:{(string)m["id"]}";
                        if (string.IsNullOrEmpty(text)) continue;
                        bool pending = PendingOf(m);
                        Need(from, text, where, pending);
                        if (!string.IsNullOrEmpty(fb)) Need(fb, text, where + " (fallback)", pending);
                    }
                Walk(root["events"], name, (sp, tx, wh, line) => Need(sp, tx, wh, PendingOf(line)));
            }
            lines = count;
            return gaps;
        }

        static void Walk(JToken o, string mission, Action<string, string, string, JObject> need)
        {
            if (o is JObject d)
            {
                string act = d["do"] is JValue dv ? (string)dv : null;          // an event's "do" is a list of actions; an action's is its name
                if ((act == "radio" || act == "callOut") && d["text"] is JValue && (d["from"] is JValue || act == "callOut"))
                    need(d["from"] is JValue fv ? (string)fv : MissionFile.AnyWingman, (string)d["text"], $"{mission}:{act}", d);
                foreach (var p in d.Properties()) Walk(p.Value, mission, need);
            }
            else if (o is JArray a) foreach (var x in a) Walk(x, mission, need);
        }
    }
}
