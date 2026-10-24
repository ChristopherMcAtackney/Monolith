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
        /// <summary>The Warden runs (<see cref="WardenRun"/>) start through the soak's flag too: <c>-mnDogfightSoak 0 -mnWardenRun n</c>.</summary>
        public static bool Requested => CommandLine.Has(Flag);

        static int EarthFighters(AiLab lab)
        {
            int n = 0;
            foreach (var c in lab.W.Combat.All) if (c.Alive && c.Ship != null && c.Ship.Team == DogfightBattle.Earth && !AiDirector.IsBig(c)) n++;
            return n;
        }

        static string EarthList(AiLab lab, ShipState player)
        {
            var sb = new StringBuilder();
            foreach (var c in lab.W.Combat.All)
            {
                if (!c.Alive || c.Ship == null || c.Ship.Team != DogfightBattle.Earth || AiDirector.IsBig(c)) continue;
                var p = lab.Ai.PilotOf(c.Id);
                sb.Append($"{c.Ship.Callsign} {Vector3.Distance(c.Ship.Position, player.Position):0}m {p?.ModeName}/{p?.Goal.Kind}->{lab.W.Ships.Get(c.Ship.Target)?.Callsign ?? "-"} {c.Ship.Velocity.magnitude:0}m/s; ");
            }
            return sb.ToString();
        }

        public static int RunFromCommandLine()
        {
            if (WardenRun.Requested) return WardenRun.RunFromCommandLine();
            if (ChaseTest.Requested) return ChaseTest.RunFromCommandLine();
            float seconds = CommandLine.GetFloat(Flag, 1200f);
            string outDir = CommandLine.Get("-mnOut", "soak_out");
            Directory.CreateDirectory(outDir);
            var data = AiData.Default;
            int level = data.LevelOf(CommandLine.Get("-mnDifficulty"));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using (var lab = new AiLab(level))
            using (var ledger = new DamageLedger(lab.W))
            {
                var battle = new DogfightBattle(lab.W, lab.Flight, lab.Rig, lab.Ai) { Autopilot = true, PlayerInvulnerable = !CommandLine.Has("-mnSoakMortal") };
                lab.W.Sim.Register(battle);
                var loops = new JArray();
                var text = new StringBuilder();
                float restartAt = -1f;
                float firstWingLoss = -1f;
                // Hostile fighters with each living wingman, and the player, as their target: sums over one-second samples.
                float onWingSum = 0f, onPlayerSum = 0f, onMercySum = 0f; int wingSamples = 0, playerSamples = 0, mercyKills = 0;
                battle.Ended += s =>
                {
                    battle.Log.Write(Path.Combine(outDir, $"pacing_loop{battle.Loop}.csv"));
                    int wingAlive = battle.Wingmen.FindAll(w => lab.W.Combat.Get(w.Ship)?.Alive == true).Count;
                    var mercyHull = lab.W.Combat.Get(battle.FriendlyId);
                    var l = new JObject
                    {
                        ["loop"] = battle.Loop, ["outcome"] = s.ToString(), ["text"] = battle.OutcomeText,
                        ["seconds"] = Math.Round(battle.EndedAt - battle.StartedAt, 1), ["wavesReached"] = battle.WaveIndex + 1,
                        ["longestQuiet"] = battle.Log.LongestQuiet, ["quietSeconds"] = battle.Log.QuietSeconds, ["maxThreats"] = battle.Log.MaxThreats,
                        ["maxOnPlayer"] = battle.Log.MaxEngaging, ["playerKills"] = battle.Kills,
                        ["wingmenAlive"] = wingAlive, ["firstWingmanLost"] = firstWingLoss >= 0f ? Math.Round(firstWingLoss, 1) : (double?)null,
                        ["mercyHull"] = mercyHull != null && mercyHull.Alive ? Math.Round(mercyHull.Hull01, 2) : 0, ["mercyDamage"] = ledger.ToJson(battle.FriendlyId),
                        ["playerDamage"] = ledger.ToJson(battle.Player.Id),
                        ["avgOnWingman"] = wingSamples > 0 ? Math.Round(onWingSum / wingSamples, 2) : 0, ["avgOnPlayer"] = playerSamples > 0 ? Math.Round(onPlayerSum / playerSamples, 2) : 0,
                    };
                    loops.Add(l);
                    string line = $"loop {battle.Loop}: {s} ({battle.OutcomeText}) after {battle.EndedAt - battle.StartedAt:0} s, wave {battle.WaveIndex + 1}/{battle.WaveCount}; {battle.Log.Summary}; wingmen alive {wingAlive}/{battle.Wingmen.Count} (first lost {(firstWingLoss >= 0f ? firstWingLoss.ToString("0") + " s" : "never")}); Mercy hull {(mercyHull != null && mercyHull.Alive ? mercyHull.Hull01 : 0f):0.00}, its damage from {ledger.Shares(battle.FriendlyId)}; the wingmen's from {ledger.Shares(battle.Wingmen.ConvertAll(w => w.Ship))}; hostiles on a wingman avg {(wingSamples > 0 ? onWingSum / wingSamples : 0f):0.0}, on the player {(playerSamples > 0 ? onPlayerSum / playerSamples : 0f):0.0}, on the Mercy {(playerSamples > 0 ? onMercySum / playerSamples : 0f):0.0}; Mercy's kills {mercyKills}";
                    text.AppendLine(line);
                    MnLog.Info("SOAK " + line);
                    restartAt = lab.W.Time + 5f;
                };
                var beamSub = !CommandLine.Has("-mnBeamTrace") ? null : GameEvents.Subscribe(CombatEvents.BeamFire, e =>
                {
                    if (battle.Loop < CommandLine.GetFloat("-mnBeamTrace", 1f)) return;
                    var src = lab.W.Ships.Get(e.Entity);
                    MnLog.Info($"BEAMTRACE L{battle.Loop} t={lab.W.Time - battle.StartedAt:0} {src?.Callsign} fires at {lab.W.Ships.Get(e.Other)?.Callsign}");
                });
                var partSub = !CommandLine.Has("-mnBeamTrace") ? null : GameEvents.Subscribe(CombatEvents.SubsystemDestroyed, e =>
                {
                    if (battle.Loop < CommandLine.GetFloat("-mnBeamTrace", 1f) || (e.Entity != battle.FriendlyId && e.Entity != battle.CorvetteId)) return;
                    MnLog.Info($"BEAMTRACE L{battle.Loop} t={lab.W.Time - battle.StartedAt:0} {lab.W.Ships.Get(e.Entity)?.Callsign} lost {e.Subject} to {lab.W.Ships.Get(e.Other)?.Callsign ?? "-"}");
                });
                var wingSub = GameEvents.Subscribe(CombatEvents.ShipDestroyed, e =>
                {
                    if (firstWingLoss < 0f && battle.Wingmen.Exists(w => w.Ship == e.Entity)) firstWingLoss = lab.W.Time - battle.StartedAt;
                    if (e.Other == battle.FriendlyId) mercyKills++;
                });
                battle.Start();

                int steps = Mathf.RoundToInt(seconds * Sim.Hz);
                int nan = 0; string nanShip = null;
                bool trace = CommandLine.Has("-mnSoakTrace");
                bool wingTrace = CommandLine.Has("-mnWingTrace");
                float beamTrace = CommandLine.Has("-mnBeamTrace") ? CommandLine.GetFloat("-mnBeamTrace", 1f) : 0f;
                int maxShips = 0;
                for (int i = 0; i < steps; i++)
                {
                    lab.W.Sim.StepOnce();
                    if (restartAt >= 0f && lab.W.Time >= restartAt) { restartAt = -1f; battle.Restart(); ledger.Reset(); firstWingLoss = -1f; onWingSum = onPlayerSum = onMercySum = 0f; wingSamples = playerSamples = mercyKills = 0; }
                    if (beamTrace > 0f && battle.Loop >= beamTrace && i % (10 * Sim.Hz) == 0 && battle.Outcome == DogfightBattle.State.Fighting)
                    {
                        var mc = lab.W.Combat.Get(battle.FriendlyId); var wc = lab.W.Combat.Get(battle.CorvetteId);
                        if (mc != null && wc != null && mc.Alive && wc.Alive)
                        {
                            string lances = "";
                            foreach (var p in mc.Parts) if (p.Kind == "turret") lances += $"{p.Name}:{p.Health01:0.00} ";
                            var cap = lab.Ai.CapitalPilots; bool hold = false; foreach (var cp in cap) if (cp.Ship == battle.FriendlyId) hold = cp.Orders.HoldBeams;
                            var ap = lab.Ai.PilotOf(battle.Player.Id);
                            MnLog.Info($"BEAMTRACE L{battle.Loop} t={lab.W.Time - battle.StartedAt:0} status: mercy-warden {Vector3.Distance(mc.Ship.Position, wc.Ship.Position):0} m, hold {hold}, lances {lances}warden {wc.Hull01:0.00}, player {ap?.ModeName}/{ap?.Goal.Kind} at {Vector3.Distance(battle.Player.Position, wc.Ship.Position):0} m from the Warden, earth fighters {EarthFighters(lab)}: {EarthList(lab, battle.Player)}");
                        }
                    }
                    if (wingTrace && i % (2 * Sim.Hz) == 0 && battle.Wingmen.Count > 0 && battle.Loop == 1)
                    {
                        var wp = battle.Wingmen[0]; var wc = lab.W.Combat.Get(wp.Ship);
                        if (wc != null && wc.Alive)
                        {
                            int on = 0; float near = 1e9f;
                            foreach (var c in lab.W.Combat.All)
                            {
                                if (!c.Alive || c.Ship == null || c.Ship.Team != DogfightBattle.Earth || AiDirector.IsBig(c)) continue;
                                if (c.Ship.Target == wp.Ship) on++;
                                near = Mathf.Min(near, Vector3.Distance(c.Ship.Position, wc.Ship.Position));
                            }
                            var warden = lab.W.Combat.Get(battle.CorvetteId);
                            float wd = warden != null && warden.Alive ? ShipHits.DistanceToHull(warden.Ship, warden, wc.Ship.Position, out _) : -1f;
                            MnLog.Info($"WINGTRACE t={lab.W.Time - battle.StartedAt:0} mode {wp.ModeName} goal {wp.Goal.Kind} target {lab.W.Ships.Get(wc.Ship.Target)?.Callsign ?? "-"} shield {wc.Shield01:0.00} hull {wc.Hull01:0.00} on it {on} nearest {near:0} warden {wd:0} dmg {ledger.Shares(wp.Ship)}");
                        }
                    }
                    if (i % Sim.Hz == 0 && battle.Outcome == DogfightBattle.State.Fighting && battle.Player != null)
                    {
                        foreach (var c in lab.W.Combat.All)
                        {
                            if (!c.Alive || c.Ship == null || c.Ship.Team != DogfightBattle.Earth || AiDirector.IsBig(c)) continue;
                            if (c.Ship.Target == battle.Player.Id) onPlayerSum++;
                            if (battle.Wingmen.Exists(w => w.Ship == c.Ship.Target)) onWingSum++;
                            if (c.Ship.Target == battle.FriendlyId) onMercySum++;
                        }
                        playerSamples++;
                        wingSamples += battle.Wingmen.FindAll(w => lab.W.Combat.Get(w.Ship)?.Alive == true).Count;
                    }
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
                wingSub.Dispose(); beamSub?.Dispose(); partSub?.Dispose();
                battle.Dispose();
                return ok ? 0 : 1;
            }
        }
    }
}
