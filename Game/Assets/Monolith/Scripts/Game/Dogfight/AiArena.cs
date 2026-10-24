using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Monolith.AI;
using Monolith.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// AI against AI, headless: <c>Monolith.exe -batchmode -nographics -mnAiArena spec.json -mnOut dir</c>. Each match puts two sides (a profile at a
    /// difficulty, a ship class, how many) 1.5 km apart, facing each other with a seeded jitter, and fights it out, a number of runs, until one side is
    /// gone or the time runs out. A match may sweep one skill knob of side A over values, the rest of the profile unchanged, so each knob's effect
    /// shows on its own. Writes arena.json and arena.txt (per side: wins, kills, losses, time on the tail, shots, hit rate, missiles), and logs
    /// [MN] ARENA lines.
    /// </summary>
    public static class AiArena
    {
        public const string Flag = "-mnAiArena";
        public static bool Requested => CommandLine.Has(Flag);

        sealed class Side { public string profile = "drone", level = "Medium", ship = "earth_interceptor"; public int count = 1, team = 1; }
        sealed class Match { public string name, knob; public float[] values; public Side a = new Side(), b = new Side { team = 2 }; public int runs = -1; public float seconds = -1f; }
        sealed class Spec { public string format, about; public int runs = 10; public float seconds = 120f; public int seed = 1; public List<Match> matches = new List<Match>(); }

        public sealed class SideResult
        {
            public string key;
            public int wins, kills, losses, shots, hits, missiles, peels, evades;
            public float tailShare, hitRate, fightSeconds, damage, meanKillSeconds, peelHull;
        }

        public static int RunFromCommandLine()
        {
            string specPath = CommandLine.Get(Flag);
            string outDir = CommandLine.Get("-mnOut", "arena_out");
            try
            {
                var spec = JsonConvert.DeserializeObject<Spec>(File.ReadAllText(specPath), new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
                if (spec == null || spec.format != "monolith-ai-arena/1") throw new InvalidDataException($"{specPath}: format must be monolith-ai-arena/1");
                Directory.CreateDirectory(outDir);
                var all = new JArray();
                var txt = new StringBuilder();
                txt.AppendLine($"AI arena: {specPath}, {spec.runs} runs of up to {spec.seconds:0} s per match unless the match says otherwise.");
                txt.AppendLine("Tail% is time on the target's tail over time with a fighter target; hit% is gun hits over gun shots.");
                txt.AppendLine();
                foreach (var m in spec.matches)
                {
                    var values = m.knob != null && m.values != null && m.values.Length > 0 ? m.values : new float[] { float.NaN };
                    foreach (var v in values)
                    {
                        var (a, b, secs, timeouts) = RunMatch(spec, m, v);
                        string name = float.IsNaN(v) ? m.name : $"{m.name} {m.knob}={v.ToString("0.##", CultureInfo.InvariantCulture)}";
                        var line = $"{name,-44} A {a.key,-40} wins {a.wins,2} kills {a.kills,3} lost {a.losses,3} tail {a.tailShare * 100f,5:0.0}% hit {a.hitRate * 100f,5:0.0}% ({a.hits}/{a.shots}) missiles {a.missiles,3} evades {a.evades,3} peels {a.peels,2}{(a.peels > 0 ? $" at {a.peelHull * 100f:0}%" : "")} | "
                                 + $"B {b.key,-28} wins {b.wins,2} kills {b.kills,3} lost {b.losses,3} tail {b.tailShare * 100f,5:0.0}% hit {b.hitRate * 100f,5:0.0}% ({b.hits}/{b.shots}) | timeouts {timeouts}, mean {secs:0} s";
                        MnLog.Info("ARENA " + line);
                        txt.AppendLine(line);
                        all.Add(new JObject { ["match"] = name, ["knob"] = m.knob, ["value"] = float.IsNaN(v) ? null : (JToken)v, ["a"] = JObject.FromObject(a), ["b"] = JObject.FromObject(b), ["timeouts"] = timeouts, ["meanSeconds"] = secs });
                    }
                }
                File.WriteAllText(Path.Combine(outDir, "arena.json"), all.ToString(Formatting.Indented));
                File.WriteAllText(Path.Combine(outDir, "arena.txt"), txt.ToString());
                MnLog.Info($"ARENA DONE -> {Path.Combine(outDir, "arena.txt")}");
                return 0;
            }
            catch (Exception e)
            {
                MnLog.Error("ARENA failed: " + e);
                return 1;
            }
        }

        static (SideResult a, SideResult b, float meanSeconds, int timeouts) RunMatch(Spec spec, Match m, float knobValue)
        {
            int runs = m.runs > 0 ? m.runs : spec.runs;
            float seconds = m.seconds > 0f ? m.seconds : spec.seconds;
            var a = new SideResult(); var b = new SideResult();
            float totalSeconds = 0f; int timeouts = 0; float killSum = 0f; int killN = 0;
            for (int r = 0; r < runs; r++)
            {
                using (var lab = new AiLab(AiData.Default.LevelOf(m.a.level)))
                {
                    lab.Ai.PlayerTeam = 0;
                    uint rng = (uint)(spec.seed * 7919 + r * 104729 + 17);
                    float Rand() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777216f; }
                    var aIds = new List<int>(); var bIds = new List<int>();
                    string aKey = "A:" + m.a.profile + "@" + m.a.level.ToLowerInvariant() + (float.IsNaN(knobValue) ? "" : $"+{m.knob}={knobValue:0.##}");
                    string bKey = "B:" + m.b.profile + "@" + m.b.level.ToLowerInvariant();
                    for (int i = 0; i < m.a.count; i++)
                    {
                        var pos = new Vector3((i - (m.a.count - 1) * 0.5f) * 80f + (Rand() - 0.5f) * 300f, (Rand() - 0.5f) * 200f, (Rand() - 0.5f) * 200f);
                        var rot = Quaternion.Euler((Rand() - 0.5f) * 30f, (Rand() - 0.5f) * 40f, 0f);
                        var st = lab.Fighter(m.a.ship, m.a.team, pos, rot, 0.7f, m.a.profile, AiRole.Fighter, $"A{i + 1}", aKey);
                        var p = lab.Ai.PilotOf(st.Id);
                        int lvA = AiData.Default.LevelOf(m.a.level);
                        p.SkillOverride = s => { var k = AiData.Default.Resolve(m.a.profile, lvA, lab.Ai.IsHostileSide(m.a.team)); return float.IsNaN(knobValue) ? k : AiData.WithKnob(k, m.knob, knobValue); };
                        p.Reskill();
                        aIds.Add(st.Id);
                    }
                    for (int i = 0; i < m.b.count; i++)
                    {
                        var pos = new Vector3((i - (m.b.count - 1) * 0.5f) * 80f + (Rand() - 0.5f) * 300f, (Rand() - 0.5f) * 200f, 1500f + (Rand() - 0.5f) * 200f);
                        var rot = Quaternion.Euler((Rand() - 0.5f) * 30f, 180f + (Rand() - 0.5f) * 40f, 0f);
                        var st = lab.Fighter(m.b.ship, m.b.team, pos, rot, 0.7f, m.b.profile, AiRole.Fighter, $"B{i + 1}", bKey);
                        var p = lab.Ai.PilotOf(st.Id);
                        int lvB = AiData.Default.LevelOf(m.b.level);
                        p.SkillOverride = s => AiData.Default.Resolve(m.b.profile, lvB, lab.Ai.IsHostileSide(m.b.team));
                        p.Reskill();
                        bIds.Add(st.Id);
                    }
                    bool Gone(List<int> ids) { foreach (var id in ids) { var c = lab.W.Combat.Get(id); if (c != null && c.Alive) return false; } return true; }
                    float t0 = lab.W.Time;
                    float firstKill = -1f;
                    string traceDir = CommandLine.Get("-mnArenaTrace");
                    StringBuilder trace = traceDir != null && r < 2 && aIds.Count == 1 && bIds.Count == 1 ? new StringBuilder("t,modeA,modeB,dist,aNose,bNose,aSpeed,bSpeed,aTail,bTail,aHull,bHull,aShield,bShield" + Environment.NewLine) : null;
                    int step = 0;
                    lab.Run(seconds, () =>
                    {
                        if (firstKill < 0f && lab.Rig.Damage.Deaths > 0) firstKill = lab.W.Time - t0;
                        if (trace != null && (step++ % 2) == 0)
                        {
                            var sa = lab.W.Ships.Get(aIds[0]); var sb = lab.W.Ships.Get(bIds[0]);
                            var ca = lab.W.Combat.Get(aIds[0]); var cb = lab.W.Combat.Get(bIds[0]);
                            if (sa != null && sb != null && ca != null && cb != null)
                            {
                                Vector3 ab = sb.Position - sa.Position; float d = ab.magnitude; Vector3 u = ab / Mathf.Max(1f, d);
                                trace.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:0.0},{1},{2},{3:0},{4:0.00},{5:0.00},{6:0},{7:0},{8},{9},{10:0.00},{11:0.00},{12:0.00},{13:0.00}",
                                    lab.W.Time - t0, lab.Ai.PilotOf(sa.Id)?.ModeName, lab.Ai.PilotOf(sb.Id)?.ModeName, d, Vector3.Dot(sa.Forward, u), Vector3.Dot(sb.Forward, -u),
                                    sa.Velocity.magnitude, sb.Velocity.magnitude, AiStats.OnTail(sa, sb, 900f, 0.866f, 0.5f) ? 1 : 0, AiStats.OnTail(sb, sa, 900f, 0.866f, 0.5f) ? 1 : 0,
                                    ca.Hull01, cb.Hull01, ca.Shield01, cb.Shield01));
                            }
                        }
                        return Gone(aIds) || Gone(bIds);
                    });
                    if (trace != null)
                    {
                        Directory.CreateDirectory(traceDir);
                        File.WriteAllText(Path.Combine(traceDir, $"{m.name.Replace(' ', '_').Replace(",", "")}_{(float.IsNaN(knobValue) ? "" : knobValue.ToString("0.##", CultureInfo.InvariantCulture))}_run{r}.csv"), trace.ToString());
                    }
                    float took = lab.W.Time - t0;
                    totalSeconds += took;
                    if (firstKill > 0f) { killSum += firstKill; killN++; }
                    bool aGone = Gone(aIds), bGone = Gone(bIds);
                    if (bGone && !aGone) a.wins++;
                    else if (aGone && !bGone) b.wins++;
                    else timeouts++;
                    Add(a, lab.Ai.Stats.Get(aKey)); Add(b, lab.Ai.Stats.Get(bKey));
                    a.key = aKey; b.key = bKey;
                }
            }
            Finish(a); Finish(b);
            a.meanKillSeconds = b.meanKillSeconds = killN > 0 ? killSum / killN : -1f;
            return (a, b, totalSeconds / Mathf.Max(1, runs), timeouts);
        }

        static void Add(SideResult r, AiStats.Line l)
        {
            r.kills += l.Kills; r.losses += l.Deaths; r.shots += l.Shots; r.hits += l.Hits; r.missiles += l.Missiles; r.peels += l.Peels; r.evades += l.Evades; r.peelHull += l.PeelHullSum;
            r.fightSeconds += l.FightSeconds; r.tailShare += l.TailSeconds; r.damage += l.Damage;
        }

        static void Finish(SideResult r)
        {
            r.tailShare = r.fightSeconds > 0.01f ? r.tailShare / r.fightSeconds : 0f;
            r.hitRate = r.shots > 0 ? (float)r.hits / r.shots : 0f;
            r.peelHull = r.peels > 0 ? r.peelHull / r.peels : 0f;
        }
    }
}
