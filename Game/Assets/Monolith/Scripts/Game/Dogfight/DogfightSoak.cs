using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Monolith.AI;
using Monolith.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// The dogfight with nothing drawn, run for a long time: <c>Monolith.exe -batchmode -nographics -mnDogfightSoak 1800 -mnOut dir [-mnDifficulty
    /// Medium] [-mnSoakMortal]</c>. The AI flies the player's ship (invulnerable unless -mnSoakMortal, so a loop runs its whole course); each loop
    /// that ends is followed by a fresh one. Checks that no AI ship gets stuck (slower than 3 m/s for 5 s or more) or flies into a capital hull,
    /// that nothing throws and no ship's numbers go bad; writes the pacing of every loop (pacing_loopN.csv), the AI's deeds (soak.txt) and
    /// soak.json. Exit 0 when it passes.
    /// </summary>
    public static class DogfightSoak
    {
        public const string Flag = "-mnDogfightSoak";
        public static bool Requested => CommandLine.Has(Flag);

        public static int RunFromCommandLine()
        {
            float seconds = CommandLine.GetFloat(Flag, 1200f);
            string outDir = CommandLine.Get("-mnOut", "soak_out");
            Directory.CreateDirectory(outDir);
            var data = AiData.Default;
            int level = data.LevelOf(CommandLine.Get("-mnDifficulty"));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using (var lab = new AiLab(level))
            {
                var battle = new DogfightBattle(lab.W, lab.Flight, lab.Rig, lab.Ai) { Autopilot = true, PlayerInvulnerable = !CommandLine.Has("-mnSoakMortal") };
                lab.W.Sim.Register(battle);
                var loops = new JArray();
                var text = new StringBuilder();
                float restartAt = -1f;
                battle.Ended += s =>
                {
                    battle.Log.Write(Path.Combine(outDir, $"pacing_loop{battle.Loop}.csv"));
                    var l = new JObject
                    {
                        ["loop"] = battle.Loop, ["outcome"] = s.ToString(), ["text"] = battle.OutcomeText,
                        ["seconds"] = Math.Round(battle.EndedAt - battle.StartedAt, 1), ["wavesReached"] = battle.WaveIndex + 1,
                        ["longestQuiet"] = battle.Log.LongestQuiet, ["quietSeconds"] = battle.Log.QuietSeconds, ["maxThreats"] = battle.Log.MaxThreats,
                        ["maxOnPlayer"] = battle.Log.MaxEngaging, ["playerKills"] = battle.Kills,
                    };
                    loops.Add(l);
                    string line = $"loop {battle.Loop}: {s} ({battle.OutcomeText}) after {battle.EndedAt - battle.StartedAt:0} s, wave {battle.WaveIndex + 1}/{battle.WaveCount}; {battle.Log.Summary}";
                    text.AppendLine(line);
                    MnLog.Info("SOAK " + line);
                    restartAt = lab.W.Time + 5f;
                };
                battle.Start();

                int steps = Mathf.RoundToInt(seconds * Sim.Hz);
                int nan = 0; string nanShip = null;
                bool trace = CommandLine.Has("-mnSoakTrace");
                int maxShips = 0;
                for (int i = 0; i < steps; i++)
                {
                    lab.W.Sim.StepOnce();
                    if (restartAt >= 0f && lab.W.Time >= restartAt) { restartAt = -1f; battle.Restart(); }
                    if (trace && i % 60 == 0 && battle.Player != null)
                    {
                        var p = battle.Player; var ap = lab.Ai.PilotOf(p.Id); var tgt = lab.W.Ships.Get(p.Target);
                        var tc = lab.W.Combat.Get(p.Target);
                        string part = tc != null && p.TargetPart >= 0 && p.TargetPart < tc.Parts.Count ? tc.Parts[p.TargetPart].Name + " " + tc.Parts[p.TargetPart].Health01.ToString("0.00") : "-";
                        float hull = tc != null ? ShipHits.DistanceToHull(tgt, tc, p.Position, out _) : 0f;
                        MnLog.Info($"TRACE t={lab.W.Time - battle.StartedAt:0} mode {ap?.ModeName} goal {ap?.Goal.Kind} speed {p.Velocity.magnitude:0} thr {p.Throttle:0.00} target {tgt?.Callsign ?? "-"} part {part} hull {hull:0} m");
                    }
                    if (i % 30 == 0)
                    {
                        maxShips = Mathf.Max(maxShips, lab.W.Ships.Count);
                        foreach (var s in lab.W.Ships.All)
                            if (float.IsNaN(s.Position.x) || float.IsNaN(s.Velocity.x) || float.IsInfinity(s.Position.x)) { nan++; nanShip = s.Callsign; }
                    }
                }
                if (battle.Outcome == DogfightBattle.State.Fighting)
                {
                    battle.Log.Write(Path.Combine(outDir, $"pacing_loop{battle.Loop}_unfinished.csv"));
                    text.AppendLine($"loop {battle.Loop}: still fighting when the soak ended, wave {battle.WaveIndex + 1}/{battle.WaveCount}; {battle.Log.Summary}");
                }

                var st = lab.Ai.Stats;
                bool stuckOk = st.LongestStuck < 5f;
                bool hullOk = st.HullCollisions == 0;
                bool ok = stuckOk && hullOk && nan == 0 && lab.Errors == 0;
                var head = new StringBuilder();
                head.AppendLine($"Dogfight soak: {seconds:0} s of sim at {lab.Ai.LevelName} in {sw.Elapsed.TotalSeconds:0} s real, {loops.Count} loop(s) finished, most ships at once {maxShips}.");
                head.AppendLine($"Stuck: longest {st.LongestStuck:0.0} s ({st.LongestStuckShip ?? "-"}) {(stuckOk ? "PASS" : "FAIL")} (under 5 s).");
                head.AppendLine($"AI fighters into capital hulls: {st.HullCollisions} {(hullOk ? "PASS" : "FAIL")}.");
                head.AppendLine($"Bad numbers (NaN): {nan}{(nanShip != null ? " (" + nanShip + ")" : "")}. Errors logged: {lab.Errors}.");
                head.AppendLine($"Turret shots: guns {lab.Guns.GunShots}, flak {lab.Guns.FlakShots}, at torpedoes {lab.Guns.TorpedoShots}, beams {lab.Guns.BeamShots}. Radio lines {lab.Ai.Radio.Said.Count}.");
                head.AppendLine(ok ? "SOAK PASS" : "SOAK FAIL");
                head.AppendLine();
                string report = head + text.ToString() + "\n" + st.Table();
                File.WriteAllText(Path.Combine(outDir, "soak.txt"), report);
                File.WriteAllText(Path.Combine(outDir, "soak_radio.txt"), string.Join("\n", lab.Ai.Radio.Said) + "\n");
                var json = new JObject
                {
                    ["seconds"] = seconds, ["level"] = lab.Ai.LevelName, ["loops"] = loops, ["longestStuck"] = st.LongestStuck, ["stuckShip"] = st.LongestStuckShip,
                    ["hullCollisions"] = st.HullCollisions, ["nan"] = nan, ["errors"] = lab.Errors, ["pass"] = ok,
                };
                File.WriteAllText(Path.Combine(outDir, "soak.json"), json.ToString());
                foreach (var l in report.Split('\n')) if (l.Trim().Length > 0) MnLog.Info("SOAK " + l.TrimEnd());
                battle.Dispose();
                return ok ? 0 : 1;
            }
        }
    }
}
