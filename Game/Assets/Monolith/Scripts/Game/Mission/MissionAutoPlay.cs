using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Monolith.AI;
using Monolith.Core;
using Monolith.Mission;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// The headless auto-play check: <c>Monolith.exe -batchmode -nographics -mnMissionAutoPlay &lt;mission&gt; -mnOut dir [-mnSeconds n]
    /// [-mnDifficulty x] [-mnMortal] [-mnGoDark]</c> (-mnGoDark: the player's cabin goes dark for every scan sweep, so it is never seen). Runs a mission on a bare world with the AI flying the player's ship (invulnerable unless the mission's
    /// autoplay block or -mnMortal says otherwise) until it ends or the time is up, then asserts its event log against the mission's
    /// <c>autoplay.expect</c>: the result, which events fired and which did not, the order they fired in, and the pacing numbers. Also: no
    /// errors, no bad numbers, no AI ship stuck. Writes events.csv, pacing.csv, autoplay.txt and autoplay.json; exit 0 when it passes.
    /// </summary>
    public static class MissionAutoPlay
    {
        public const string Flag = "-mnMissionAutoPlay";
        public static bool Requested => CommandLine.Has(Flag);

        public sealed class Result
        {
            public bool Pass;
            public string Report;
            public JObject Json;
            public MissionRuntime Runtime;
        }

        public static int RunFromCommandLine()
        {
            string name = CommandLine.Get(Flag);
            string outDir = CommandLine.Get("-mnOut", "autoplay_" + MissionLibrary.NameOf(name));
            MissionFile file;
            try { file = MissionLibrary.Load(name); }
            catch (Exception e) { MnLog.Error("AUTOPLAY could not load the mission: " + e.Message); return 2; }
            var ap = file.Autoplay;
            int level = AiData.Default.LevelOf(CommandLine.Get("-mnDifficulty", ap.Difficulty ?? file.Difficulty));
            var r = Run(file, CommandLine.GetFloat("-mnSeconds", ap.Seconds), level, ap.Invulnerable && !CommandLine.Has("-mnMortal"), outDir);
            return r.Pass ? 0 : 1;
        }

        public static Result Run(MissionFile file, float seconds, int level, bool invulnerable, string outDir)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var checks = new List<(bool ok, string what)>();
            void Check(bool ok, string what) { checks.Add((ok, what)); MnLog.Info($"AUTOPLAY {(ok ? "PASS" : "FAIL")}: {what}"); }
            using (var lab = new AiLab(level))
            {
                var host = new GameMissionHost(lab.W, lab.Flight, lab.Rig, lab.Ai) { Autopilot = true, PlayerInvulnerable = invulnerable };
                var rt = new MissionRuntime(lab.W, file, host, new CabinStub { AutoRepairSeconds = 8f, GoesDark = CommandLine.Has("-mnGoDark") });
                lab.W.Sim.Register(rt);
                lab.W.Sim.Register(new PlayerKeeper(lab.W, lab.Rig, rt, invulnerable));
                rt.Start();
                int steps = Mathf.RoundToInt(seconds * Sim.Hz);
                int nan = 0, maxShips = 0; string nanShip = null;
                float endAt = -1f;
                for (int i = 0; i < steps; i++)
                {
                    lab.W.Sim.StepOnce();
                    if (rt.Ended && endAt < 0f) endAt = lab.W.Time;
                    if (endAt >= 0f && lab.W.Time >= endAt + 5f) break;
                    if (i % 30 == 0)
                    {
                        maxShips = Mathf.Max(maxShips, lab.W.Ships.Count);
                        foreach (var s in lab.W.Ships.All)
                            if (float.IsNaN(s.Position.x) || float.IsNaN(s.Velocity.x) || float.IsInfinity(s.Position.x)) { nan++; nanShip = s.Callsign; }
                    }
                }
                float ran = lab.W.Time;
                var ex = file.Autoplay;
                string result = rt.Ended ? rt.Result : "none";

                // The assertions.
                if (ex.Result.Count > 0) Check(ex.Result.Contains(result), $"result {result} is one of [{string.Join(", ", ex.Result)}]" + (rt.Ended ? $" (\"{rt.ResultText}\" at {rt.EndedAt - rt.StartTime:0.0} s)" : $" (still running after {ran:0} s)"));
                foreach (var n in ex.Fired) { var e = rt.EventNamed(n); Check(e != null && e.FirstFiredAt >= 0f, $"'{n}' fired" + (e != null && e.FirstFiredAt >= 0f ? $" at {e.FirstFiredAt - rt.StartTime:0.0} s" : "")); }
                foreach (var n in ex.NotFired) { var e = rt.EventNamed(n); Check(e != null && e.FirstFiredAt < 0f, $"'{n}' did not fire" + (e != null && e.FirstFiredAt >= 0f ? $" (it fired at {e.FirstFiredAt - rt.StartTime:0.0} s)" : "")); }
                if (ex.Order.Count > 1)
                {
                    float last = -1f; string lastName = null; bool ok = true; var times = new List<string>();
                    foreach (var n in ex.Order)
                    {
                        var e = rt.EventNamed(n);
                        if (e == null || e.FirstFiredAt < 0f) { times.Add($"{n} -"); continue; }       // not fired: only the ones that fired must keep the order
                        times.Add($"{n} {e.FirstFiredAt - rt.StartTime:0.0}");
                        if (e.FirstFiredAt < last) { ok = false; MnLog.Info($"AUTOPLAY order: '{n}' at {e.FirstFiredAt - rt.StartTime:0.0} s came before '{lastName}' at {last - rt.StartTime:0.0} s"); }
                        last = e.FirstFiredAt; lastName = n;
                    }
                    Check(ok, $"events fired in order: {string.Join(", ", times)}");
                }
                var log = rt.Log;
                if (ex.LongestQuietMax >= 0f) Check(log.LongestQuiet <= ex.LongestQuietMax, $"longest stretch without a threat in the fight {log.LongestQuiet:0} s <= {ex.LongestQuietMax:0} s");
                if (ex.MaxEngagingMax >= 0) Check(log.MaxEngaging <= ex.MaxEngagingMax, $"most hostiles on the player at once {log.MaxEngaging} <= {ex.MaxEngagingMax}");
                if (ex.MaxThreatsMax >= 0) Check(log.MaxThreats <= ex.MaxThreatsMax, $"most threats alive at once {log.MaxThreats} <= {ex.MaxThreatsMax}");
                if (ex.ThreatsAvgMin >= 0f) Check(log.AvgThreats >= ex.ThreatsAvgMin, $"threats alive on average {log.AvgThreats:0.0} >= {ex.ThreatsAvgMin:0.0}");
                var st = lab.Ai.Stats;
                Check(nan == 0, $"no bad numbers (NaN {nan}{(nanShip != null ? " " + nanShip : "")})");
                Check(lab.Errors == 0, $"no errors logged ({lab.Errors})");
                Check(st.LongestStuck < 5f, $"no AI ship stuck (longest {st.LongestStuck:0.0} s{(st.LongestStuckShip != null ? " " + st.LongestStuckShip : "")})");

                bool pass = checks.TrueForAll(c => c.ok);
                var text = new StringBuilder();
                text.AppendLine($"Auto-play of '{file.Name}' ({file.Id}) at {lab.Ai.LevelName}, the AI flying {(invulnerable ? "an invulnerable" : "a mortal")} player ship: {ran:0} s of sim in {sw.Elapsed.TotalSeconds:0.0} s real.");
                text.AppendLine($"Result: {result}{(rt.Ended ? $" \"{rt.ResultText}\" at {rt.EndedAt - rt.StartTime:0.0} s" : "")}. Kills by the player {rt.Kills}; losses {(rt.Losses.Count == 0 ? "none" : string.Join(", ", rt.Losses))}. Most ships at once {maxShips}.");
                text.AppendLine($"Pacing: {log.Summary}.");
                text.AppendLine($"Radio lines {rt.Radio.Said.Count} (dropped {rt.Radio.Dropped}); AI hull contacts {st.HullCollisions}.");
                text.AppendLine();
                foreach (var c in checks) text.AppendLine($"{(c.ok ? "PASS" : "FAIL")}  {c.what}");
                text.AppendLine(pass ? "AUTOPLAY PASS" : "AUTOPLAY FAIL");
                text.AppendLine();
                text.AppendLine("Events fired (first time), in order:");
                var fired = new List<MissionRuntime.EventRec>();
                foreach (var e in rt.Events) if (e.FirstFiredAt >= 0f) fired.Add(e);
                fired.Sort((x, y) => x.FirstFiredAt.CompareTo(y.FirstFiredAt));
                foreach (var e in fired) text.AppendLine($"  {e.FirstFiredAt - rt.StartTime,7:0.0} s  {e.Def.Name}{(e.Fired > 1 ? $" (x{e.Fired})" : "")}");
                text.AppendLine("Objectives:");
                foreach (var o in rt.Objectives) text.AppendLine($"  {o.Def.Kind,-9} {o.View.State,-6} {o.Def.Text}");

                var json = new JObject
                {
                    ["mission"] = file.Id, ["level"] = lab.Ai.LevelName, ["seconds"] = Math.Round(ran, 1), ["result"] = result, ["resultText"] = rt.ResultText,
                    ["endedAt"] = rt.Ended ? Math.Round(rt.EndedAt - rt.StartTime, 2) : (double?)null, ["kills"] = rt.Kills, ["losses"] = new JArray(rt.Losses.ToArray()),
                    ["pacing"] = log.ToJson(), ["pass"] = pass,
                    ["events"] = new JArray(EventTimes(rt)),
                    ["checks"] = new JArray(checks.ConvertAll(c => (JToken)new JObject { ["pass"] = c.ok, ["what"] = c.what }).ToArray()),
                };
                if (outDir != null)
                {
                    rt.Log.Write(outDir);
                    File.WriteAllText(Path.Combine(outDir, "autoplay.txt"), text.ToString());
                    File.WriteAllText(Path.Combine(outDir, "autoplay.json"), json.ToString());
                    File.WriteAllText(Path.Combine(outDir, "radio.txt"), string.Join("\n", rt.Radio.Said) + "\n");
                }
                foreach (var l in text.ToString().Split('\n')) if (l.Trim().Length > 0) MnLog.Info("AUTOPLAY " + l.TrimEnd());
                rt.Dispose();
                host.Dispose();
                return new Result { Pass = pass, Report = text.ToString(), Json = json, Runtime = rt };
            }
        }

        static IEnumerable<JToken> EventTimes(MissionRuntime rt)
        {
            foreach (var e in rt.Events)
                if (e.FirstFiredAt >= 0f) yield return new JObject { ["name"] = e.Def.Name, ["t"] = Math.Round(e.FirstFiredAt - rt.StartTime, 2), ["fired"] = e.Fired };
        }
    }

    /// <summary>
    /// Proof that the dogfight's port to data plays the same: <c>-mnDogfightPortCheck seconds -mnOut dir</c> runs the hand-written dogfight
    /// (<see cref="DogfightBattle"/>) and Resources/Missions/dogfight.mission.json through the mission system, each on a fresh bare world with the
    /// AI flying an invulnerable player (as the soak does), and compares a fingerprint of every ship (id, name, alive, hull, shield, position,
    /// velocity, rotation, to the bit) once a second. Identical fingerprints mean the two are the same fight, tick for tick.
    /// </summary>
    public static class DogfightPortCheck
    {
        public const string Flag = "-mnDogfightPortCheck";
        public static bool Requested => CommandLine.Has(Flag);

        public sealed class Result
        {
            public bool Same;
            public int Seconds, FirstDiff = -1;
            public string Report;
            public List<string> Old = new List<string>(), New = new List<string>();
        }

        public static int RunFromCommandLine()
        {
            var r = Run(CommandLine.GetFloat(Flag, 600f), AiData.Default.LevelOf(CommandLine.Get("-mnDifficulty")), CommandLine.Get("-mnOut", "dogfight_port"));
            return r.Same ? 0 : 1;
        }

        public static Result Run(float seconds, int level, string outDir)
        {
            var res = new Result();
            var oldTimeline = new List<string>();
            var newTimeline = new List<string>();
            string oldEnd, newEnd;
            int steps = Mathf.RoundToInt(seconds * Sim.Hz);

            using (var lab = new AiLab(level))
            {
                var battle = new DogfightBattle(lab.W, lab.Flight, lab.Rig, lab.Ai) { Autopilot = true, PlayerInvulnerable = true };
                lab.W.Sim.Register(battle);
                var tl = Timeline(lab.W, oldTimeline);
                battle.Start();
                for (int i = 0; i < steps; i++)
                {
                    lab.W.Sim.StepOnce();
                    if (i % Sim.Hz == 0) res.Old.Add(Fingerprint(lab.W));
                }
                oldEnd = battle.Outcome == DogfightBattle.State.Fighting ? $"still fighting at wave {battle.WaveIndex + 1}" : $"{battle.Outcome} \"{battle.OutcomeText}\" at {battle.EndedAt - battle.StartedAt:0.0} s";
                tl.Dispose();
                battle.Dispose();
            }
            using (var lab = new AiLab(level))
            {
                var file = MissionLibrary.Load("dogfight");
                var host = new GameMissionHost(lab.W, lab.Flight, lab.Rig, lab.Ai) { Autopilot = true, PlayerInvulnerable = true };
                var rt = new MissionRuntime(lab.W, file, host);
                lab.W.Sim.Register(rt);
                lab.W.Sim.Register(new PlayerKeeper(lab.W, lab.Rig, rt, true));
                var tl = Timeline(lab.W, newTimeline);
                rt.Start();
                for (int i = 0; i < steps; i++)
                {
                    lab.W.Sim.StepOnce();
                    if (i % Sim.Hz == 0) res.New.Add(Fingerprint(lab.W));
                }
                newEnd = rt.Ended ? $"{rt.Result} \"{rt.ResultText}\" at {rt.EndedAt - rt.StartTime:0.0} s" : "still running";
                tl.Dispose();
                rt.Dispose();
                host.Dispose();
            }

            res.Seconds = res.Old.Count;
            for (int i = 0; i < Mathf.Min(res.Old.Count, res.New.Count); i++)
                if (res.Old[i] != res.New[i]) { res.FirstDiff = i; break; }
            res.Same = res.FirstDiff < 0 && res.Old.Count == res.New.Count;
            bool timelineSame = string.Join("\n", oldTimeline) == string.Join("\n", newTimeline);

            var sb = new StringBuilder();
            sb.AppendLine($"Dogfight port check: {seconds:0} s of sim at {AiData.Default.Difficulty.Levels[AiData.Default.ClampLevel(level < 0 ? AiData.Default.Difficulty.Default : level)]}, the AI flying an invulnerable player in both.");
            sb.AppendLine($"Hand-written dogfight (DogfightBattle): {oldEnd}.");
            sb.AppendLine($"dogfight.mission.json (mission system): {newEnd}.");
            sb.AppendLine(res.Same
                ? $"SAME: every ship's state matches to the bit at all {res.Seconds} one-second samples ({res.Old[res.Old.Count - 1].Split('|')[0]} ships at the end)."
                : $"DIFFERENT: first difference at t={res.FirstDiff} s.");
            sb.AppendLine($"Spawn and death timeline (ship, tick): {(timelineSame ? "identical" : "different")}, {oldTimeline.Count} entries.");
            if (!res.Same && res.FirstDiff >= 0)
            {
                sb.AppendLine("Old at the first difference: " + res.Old[res.FirstDiff]);
                sb.AppendLine("New at the first difference: " + res.New[res.FirstDiff]);
            }
            res.Report = sb.ToString();
            if (outDir != null)
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "port_check.txt"), res.Report);
                File.WriteAllText(Path.Combine(outDir, "old_fingerprints.txt"), string.Join("\n", res.Old) + "\n");
                File.WriteAllText(Path.Combine(outDir, "new_fingerprints.txt"), string.Join("\n", res.New) + "\n");
                File.WriteAllText(Path.Combine(outDir, "old_timeline.txt"), string.Join("\n", oldTimeline) + "\n");
                File.WriteAllText(Path.Combine(outDir, "new_timeline.txt"), string.Join("\n", newTimeline) + "\n");
            }
            foreach (var l in res.Report.Split('\n')) if (l.Trim().Length > 0) MnLog.Info("PORT " + l.TrimEnd());
            res.Same &= timelineSame;
            return res;
        }

        /// <summary>Every ship's state as one line: count, then per ship id, name, alive, and the bits of hull, shield, position, velocity, rotation.</summary>
        static string Fingerprint(GameWorld w)
        {
            var sb = new StringBuilder();
            sb.Append(w.Ships.Count).Append('|').Append(w.Tick).Append('|');
            uint h = 2166136261u;
            void Mix(float f) { uint b = (uint)BitConverter.ToInt32(BitConverter.GetBytes(f), 0); for (int k = 0; k < 4; k++) { h = (h ^ (b & 0xFF)) * 16777619u; b >>= 8; } }
            foreach (var s in w.Ships.All)
            {
                var c = w.Combat.Get(s.Id);
                Mix(s.Id); Mix(s.Position.x); Mix(s.Position.y); Mix(s.Position.z); Mix(s.Velocity.x); Mix(s.Velocity.y); Mix(s.Velocity.z);
                Mix(s.Rotation.x); Mix(s.Rotation.y); Mix(s.Rotation.z); Mix(s.Rotation.w);
                if (c != null) { Mix(c.Hull); Mix(c.Shield); Mix(c.Alive ? 1f : 0f); }
                foreach (char ch in s.Callsign ?? "") h = (h ^ ch) * 16777619u;
            }
            sb.Append(h.ToString("x8"));
            foreach (var s in w.Ships.All)
                sb.Append(string.Format(CultureInfo.InvariantCulture, "|{0}#{1}:{2:0.0},{3:0.0},{4:0.0}", s.Callsign, s.Id, s.Position.x, s.Position.y, s.Position.z));
            return sb.ToString();
        }

        static IDisposable Timeline(GameWorld w, List<string> into)
        {
            var subs = new List<IDisposable>
            {
                GameEvents.Subscribe(CombatEvents.ShipDestroyed, e => into.Add($"{e.Tick} destroyed {w.Ships.Get(e.Entity)?.Callsign}#{e.Entity}")),
            };
            int seen = 0;
            var spawnWatch = new SpawnWatch(w, into);
            w.Sim.Register(spawnWatch);
            return new Disposer(() => { foreach (var s in subs) s.Dispose(); w.Sim.Unregister(spawnWatch); seen++; });
        }

        sealed class SpawnWatch : ISimSystem
        {
            readonly GameWorld _w; readonly List<string> _into; int _maxId;
            public SpawnWatch(GameWorld w, List<string> into) { _w = w; _into = into; }
            public int Order => SimOrder.Late + 10;
            public void Step(in SimFrame frame)
            {
                foreach (var s in _w.Ships.All) if (s.Id > _maxId) { _maxId = s.Id; _into.Add($"{frame.Tick} spawned {s.Callsign}#{s.Id}"); }
            }
        }

        sealed class Disposer : IDisposable { readonly Action _a; public Disposer(Action a) { _a = a; } public void Dispose() => _a(); }
    }
}
