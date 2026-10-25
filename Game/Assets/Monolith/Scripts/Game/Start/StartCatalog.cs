using System;
using System.Collections.Generic;
using System.IO;
using Monolith.AI;
using Monolith.Core;
using Monolith.Mission;
using Monolith.Start;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// What the start screen lists: every mission file (the ones the game carries and any in the Missions folder beside the game, which are read first, by the name written in the file),
    /// the three sandboxes, and the quit row; and the difficulty levels from the AI's own table. Also what a choice launches: a mission is <c>-mnMission id -mnDifficulty level</c>, a sandbox is
    /// <c>-mnScene name</c>, added to the command line for the run (<see cref="CommandLine.SetOverlay"/>), so every scene starts the way it does from a command line.
    /// </summary>
    public static class StartCatalog
    {
        /// <summary>The order the game's own missions are listed in (the others follow, by name).</summary>
        public static readonly string[] Order = { "dogfight", "oort", "escort", "cabin" };

        public static readonly (string id, string title, string detail)[] Sandboxes =
        {
            (FlightPlaytest.SceneName, "Free flight", "The flight model on its own: nothing to fight."),
            (CombatRange.SceneName, "Combat range", "Targets and turrets to shoot at."),
            (JuiceSandbox.SceneName, "Explosions sandbox", "Fire the explosions and effects yourself. Start opens the tuning panel."),
        };

        public sealed class MissionInfo { public string Id, Name, Detail, Source; public bool Beside; }

        /// <summary>The folder of missions beside the game.</summary>
        public static string BesideFolder => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", MissionLibrary.ResourceFolder);

        /// <summary>Every mission there is to play, in the order they are listed.</summary>
        public static List<MissionInfo> Missions(string besideFolder = null)
        {
            besideFolder ??= BesideFolder;
            var byId = new Dictionary<string, MissionInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in Resources.LoadAll<TextAsset>(MissionLibrary.ResourceFolder))
            {
                string id = MissionLibrary.NameOf(t.name);
                byId[id] = Describe(id, t.text, "built in", false);
            }
            try
            {
                if (Directory.Exists(besideFolder))
                    foreach (var f in Directory.GetFiles(besideFolder, "*" + MissionLibrary.Extension))
                    {
                        string id = MissionLibrary.NameOf(f);
                        byId[id] = Describe(id, File.ReadAllText(f), f, true);       // a file beside the game replaces the built-in one of the same name, as the game reads it
                    }
            }
            catch (Exception e) { MnLog.Warn("START could not read the Missions folder: " + e.Message); }
            var list = new List<MissionInfo>(byId.Values);
            list.Sort((a, b) =>
            {
                int ia = Array.IndexOf(Order, a.Id.ToLowerInvariant()), ib = Array.IndexOf(Order, b.Id.ToLowerInvariant());
                if (ia < 0) ia = int.MaxValue; if (ib < 0) ib = int.MaxValue;
                return ia != ib ? ia.CompareTo(ib) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        static MissionInfo Describe(string id, string text, string source, bool beside)
        {
            var info = new MissionInfo { Id = id, Name = id, Source = source, Beside = beside };
            try
            {
                var o = JObject.Parse(text);
                if (o["name"]?.Type == JTokenType.String) info.Name = (string)o["name"];
                info.Detail = FirstSentences(o["briefing"]?["stages"]?.First?.Type == JTokenType.String ? (string)o["briefing"]["stages"].First : null, 2, 260);
            }
            catch (Exception e) { info.Detail = "This file could not be read: " + e.Message; }
            return info;
        }

        /// <summary>The first <paramref name="sentences"/> sentences of a text, cut at a sentence end within <paramref name="max"/> characters.</summary>
        public static string FirstSentences(string text, int sentences, int max)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim();
            int end = 0, count = 0;
            for (int i = 0; i < text.Length && i < max; i++)
                if ((text[i] == '.' || text[i] == '!' || text[i] == '?') && (i + 1 >= text.Length || text[i + 1] == ' ')) { end = i + 1; if (++count >= sentences) break; }
            if (end == 0) return text.Length <= max ? text : text.Substring(0, max).TrimEnd() + "...";
            return text.Substring(0, end);
        }

        /// <summary>The start screen's menu: the scenarios, the levels, and the cursor on what was chosen last time (start.json) or <c>-mnDifficulty</c>.</summary>
        public static StartMenu Build(string besideFolder = null, bool useSaved = true)
        {
            var menu = new StartMenu();
            foreach (var m in Missions(besideFolder)) menu.Entries.Add(new StartEntry { Id = m.Id, Title = m.Name, Detail = m.Detail, Kind = EntryKind.Mission });
            foreach (var s in Sandboxes) menu.Entries.Add(new StartEntry { Id = s.id, Title = s.title, Detail = s.detail, Kind = EntryKind.Sandbox });
            menu.Entries.Add(new StartEntry { Id = "quit", Title = "Quit", Detail = "Close the game.", Kind = EntryKind.Quit });
            var ai = AiData.Default.Difficulty;
            menu.Levels.AddRange(ai.Levels);
            foreach (var v in ai.PlayerDamageScale) menu.Stat.Add(v);
            while (menu.Stat.Count < menu.Levels.Count) menu.Stat.Add(1f);
            menu.Level = Mathf.Clamp(ai.Default, 0, menu.Levels.Count - 1);
            if (useSaved)
            {
                StartSettings.Load(out string scenario, out string difficulty);
                menu.Select(scenario);
                menu.SelectLevel(difficulty);
            }
            string flag = CommandLine.Get("-mnDifficulty");
            if (flag != null) menu.SelectLevel(flag);
            return menu;
        }

        /// <summary>The flags a choice adds to the command line for its run.</summary>
        public static string[] FlagsFor(StartEntry e, string level) =>
            e.Kind == EntryKind.Mission ? new[] { "-mnMission", e.Id, "-mnDifficulty", level }
            : e.Kind == EntryKind.Sandbox ? new[] { "-mnScene", e.Id }
            : Array.Empty<string>();
    }
}
