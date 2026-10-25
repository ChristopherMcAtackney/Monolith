using System;
using System.IO;
using Monolith.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Start
{
    /// <summary>
    /// What the start screen remembers beside the game, the way hud.json remembers the HUD style: the last scenario and the last difficulty, in <c>start.json</c>
    /// (<c>{"scenario": "oort", "difficulty": "Hard"}</c>). Written when a scenario is launched; never in a capture or a headless run.
    /// </summary>
    public static class StartSettings
    {
        public static string FilePath => FileOverride ?? Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "start.json");

        /// <summary>Another file instead of start.json (the checks).</summary>
        public static string FileOverride;

        /// <summary>Written even in a headless run (the checks, with <see cref="FileOverride"/>).</summary>
        public static bool AllowBatch;

        public static void Load(out string scenario, out string difficulty)
        {
            scenario = null; difficulty = null;
            try
            {
                if (!File.Exists(FilePath)) return;
                var o = JObject.Parse(File.ReadAllText(FilePath));
                scenario = o["scenario"]?.Type == JTokenType.String ? (string)o["scenario"] : null;
                difficulty = o["difficulty"]?.Type == JTokenType.String ? (string)o["difficulty"] : null;
            }
            catch (Exception e) { MnLog.Warn("START could not read start.json: " + e.Message); }
        }

        public static void Save(string scenario, string difficulty)
        {
            if (CommandLine.Has("-mnCapture") || (Application.isBatchMode && !AllowBatch)) return;
            try
            {
                var o = new JObject { ["scenario"] = scenario, ["difficulty"] = difficulty };
                File.WriteAllText(FilePath, o.ToString());
            }
            catch (Exception e) { MnLog.Warn("START could not save start.json: " + e.Message); }
        }
    }
}
