using System.Collections.Generic;
using System.IO;
using System.Text;
using Monolith.AI;
using Monolith.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// The chase check (can Earth's drones keep up with a boosting player?): <c>Monolith.exe -batchmode -nographics -mnDogfightSoak 0
    /// -mnChaseTest 10 -mnOut dir [-mnDifficulty Medium] [-mnChaseClass earth_interceptor] [-mnChaseSeconds 60]</c>. The player's ship (invulnerable,
    /// so the damage it would have taken is counted) runs from three drones that start 500 to 700 m behind it, flat out, the burner lit whenever
    /// the tank allows, weaving a little (a different weave each run). Logs, per run and on average: the nearest drone's range over the run and at
    /// its end, the share of the time a drone was within gun range (800 m), the damage it would have taken, and the drones' time on the burner.
    /// Writes chase.txt and chase.json.
    /// </summary>
    public static class ChaseTest
    {
        public const string Flag = "-mnChaseTest";
        public static bool Requested => CommandLine.Has(Flag);

        /// <summary>Flat out, burner whenever it will light, a slow weave.</summary>
        sealed class FleePilot : IPilotSource
        {
            readonly GameWorld _w; readonly int _ship; readonly float _phase, _weave;
            bool _held;
            public FleePilot(GameWorld w, int ship, float phase, float weave) { _w = w; _ship = ship; _phase = phase; _weave = weave; }
            public ControlInput Sample(in SimFrame frame)
            {
                var me = _w.Ships.Get(_ship);
                var input = new ControlInput { Throttle = 1f, Yaw = _weave * Mathf.Sin(frame.Time * 0.6f + _phase), Pitch = 0.5f * _weave * Mathf.Sin(frame.Time * 0.37f + 2f * _phase) };
                bool press = me != null && me.FuelMax > 0f && (me.AfterburnerOn ? me.Fuel > me.FuelMax * 0.04f : me.Fuel > me.FuelMax * 0.3f && !_held);
                if (press) input.Buttons |= ControlButtons.Afterburner;
                _held = press;
                return input;
            }
        }

        public static int RunFromCommandLine()
        {
            int runs = Mathf.Max(1, Mathf.RoundToInt(CommandLine.GetFloat(Flag, 10f)));
            float seconds = CommandLine.GetFloat("-mnChaseSeconds", 60f);
            string cls = CommandLine.Get("-mnChaseClass", "earth_interceptor");
            string outDir = CommandLine.Get("-mnOut", "chase_out");
            Directory.CreateDirectory(outDir);
            int level = AiData.Default.LevelOf(CommandLine.Get("-mnDifficulty"));
            var arr = new JArray();
            var sb = new StringBuilder();
            float sumEnd = 0f, sumMean = 0f, sumGun = 0f, sumDmg = 0f, sumBurn = 0f; int lost = 0;
            string levelName = null;
            for (int r = 0; r < runs; r++)
            {
                using (var lab = new AiLab(level))
                using (var ledger = new DamageLedger(lab.W))
                {
                    levelName = lab.Ai.LevelName;
                    var player = lab.Fighter("vanguard_fighter", 0, Vector3.zero, Quaternion.identity, 0.9f, null, AiRole.Fighter, "Sable 1");
                    lab.W.LocalPlayerShip = player.Id;
                    lab.W.Combat.Get(player.Id).Invulnerable = true;
                    lab.W.Pilots.Attach(player.Id, new FleePilot(lab.W, player.Id, r * 1.7f, 0.15f + 0.05f * (r % 3)));
                    var drones = new List<ShipState>();
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 pos = new Vector3((k - 1) * 120f, (k % 2) * 40f - 20f, -500f - 100f * k - 13f * r);
                        var d = lab.Fighter(cls, 1, pos, Quaternion.identity, 0.9f, "drone", AiRole.Fighter, $"Choir {k + 1}");
                        lab.Ai.PilotOf(d.Id).SetGoal(new AiGoal { Kind = GoalKind.Attack, Target = player.Id, Part = -1 });
                        drones.Add(d);
                    }
                    int steps = Mathf.RoundToInt(seconds * Sim.Hz);
                    float nearSum = 0f, gunTime = 0f, burn = 0f; int samples = 0; float nearest = 0f;
                    for (int i = 0; i < steps; i++)
                    {
                        lab.W.Sim.StepOnce();
                        nearest = float.MaxValue;
                        foreach (var d in drones)
                        {
                            var dc = lab.W.Combat.Get(d.Id);
                            if (dc == null || !dc.Alive) continue;
                            nearest = Mathf.Min(nearest, Vector3.Distance(d.Position, player.Position));
                            if (d.AfterburnerOn) burn += Sim.Dt;
                        }
                        if (nearest < 800f) gunTime += Sim.Dt;
                        if (i % 6 == 0) { nearSum += nearest; samples++; }
                    }
                    float mean = samples > 0 ? nearSum / samples : 0f;
                    float dmg = ledger.Total(player.Id);
                    bool gone = nearest > 2500f;
                    if (gone) lost++;
                    sumEnd += nearest; sumMean += mean; sumGun += gunTime; sumDmg += dmg; sumBurn += burn / drones.Count;
                    var sl = lab.Ai.Stats.Get(lab.Ai.PilotOf(drones[0].Id)?.Skill.Key ?? "-");
                    string line = $"run {r + 1}: nearest drone at the end {nearest:0} m (mean {mean:0} m), within 800 m for {gunTime:0} of {seconds:0} s, damage it would have taken {dmg:0} ({ledger.Shares(player.Id)}), drones on the burner {burn / drones.Count:0} s each, drone shots {sl.Shots} hits {sl.Hits} missiles {sl.Missiles}";
                    sb.AppendLine("  " + line);
                    MnLog.Info("CHASE " + line);
                    arr.Add(new JObject { ["run"] = r + 1, ["endNearest"] = Mathf.Round(nearest), ["meanNearest"] = Mathf.Round(mean), ["gunRangeSeconds"] = System.Math.Round(gunTime, 1), ["damage"] = Mathf.Round(dmg), ["burnSeconds"] = System.Math.Round(burn / drones.Count, 1) });
                }
            }
            var head = $"Chase at {levelName}: the player's ship flat out on the burner for {seconds:0} s from three {cls} drones 500-700 m behind, {runs} runs.\n" +
                       $"Nearest drone at the end: mean {sumEnd / runs:0} m (contact lost, over 2.5 km, in {lost} of {runs}); over the run: mean {sumMean / runs:0} m.\n" +
                       $"A drone within gun range (800 m): {sumGun / runs:0} s of {seconds:0}. Damage the player would have taken: mean {sumDmg / runs:0}. Drones on the burner: {sumBurn / runs:0} s each.\n";
            File.WriteAllText(Path.Combine(outDir, "chase.txt"), head + sb);
            File.WriteAllText(Path.Combine(outDir, "chase.json"), new JObject { ["level"] = levelName, ["class"] = cls, ["seconds"] = seconds, ["runs"] = arr,
                ["meanEnd"] = Mathf.Round(sumEnd / runs), ["meanNearest"] = Mathf.Round(sumMean / runs), ["gunSeconds"] = System.Math.Round(sumGun / runs, 1), ["damage"] = Mathf.Round(sumDmg / runs), ["lost"] = lost }.ToString());
            foreach (var l in head.Split('\n')) if (l.Trim().Length > 0) MnLog.Info("CHASE " + l);
            return 0;
        }
    }
}
