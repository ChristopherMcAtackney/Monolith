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

            void Need(string speaker, string text, string where)
            {
                foreach (var v in VoicesOf(speaker, where, gaps))
                {
                    count++;
                    if (!have.Contains(v + "|" + Norm(text))) gaps.Add($"{where}: {speaker} ({v}) '{text}'");
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
                if (root["messages"] is JArray msgs)
                    foreach (JObject m in msgs)
                    {
                        string text = (string)m["text"], from = (string)m["from"], fb = (string)m["fallbackFrom"], where = $"{name}:{(string)m["id"]}";
                        if (string.IsNullOrEmpty(text)) continue;
                        Need(from, text, where);
                        if (!string.IsNullOrEmpty(fb)) Need(fb, text, where + " (fallback)");
                    }
                Walk(root["events"], name, Need);
            }
            lines = count;
            return gaps;
        }

        static void Walk(JToken o, string mission, Action<string, string, string> need)
        {
            if (o is JObject d)
            {
                string act = d["do"] is JValue dv ? (string)dv : null;          // an event's "do" is a list of actions; an action's is its name
                if ((act == "radio" || act == "callOut") && d["text"] is JValue && (d["from"] is JValue || act == "callOut"))
                    need(d["from"] is JValue fv ? (string)fv : MissionFile.AnyWingman, (string)d["text"], $"{mission}:{act}");
                foreach (var p in d.Properties()) Walk(p.Value, mission, need);
            }
            else if (o is JArray a) foreach (var x in a) Walk(x, mission, need);
        }
    }
}
