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
    /// Who hurt whom, by what (checks only): every shield and hull hit on the ships it watches, summed by the kind of source: the capital
    /// ships' guns, flak and beams, and the fighters' guns, missiles and torpedoes.
    /// </summary>
    public sealed class DamageLedger : IDisposable
    {
        public const string Turret = "turret guns", Flak = "flak", Beam = "beams", Guns = "fighter guns", Missiles = "missiles", Torpedoes = "torpedoes", Other = "other";

        readonly GameWorld _w;
        readonly List<IDisposable> _subs = new List<IDisposable>();
        readonly Dictionary<int, Dictionary<string, float>> _by = new Dictionary<int, Dictionary<string, float>>();

        public DamageLedger(GameWorld w)
        {
            _w = w;
            _subs.Add(GameEvents.Subscribe(CombatEvents.HitHull, On));
            _subs.Add(GameEvents.Subscribe(CombatEvents.HitShield, On));
        }

        public void Dispose() { foreach (var s in _subs) s.Dispose(); _subs.Clear(); }
        public void Reset() => _by.Clear();

        void On(GameEvent e)
        {
            if (e.Value <= 0f) return;
            var src = _w.Combat.Get(e.Other);
            var def = WeaponCatalog.Get(e.Subject);
            string kind;
            if (def == null) kind = Other;
            else if (def.IsBeam) kind = Beam;
            else if (def.Kind == WeaponKind.Flak) kind = Flak;
            else if (def.Kind == WeaponKind.Torpedo) kind = Torpedoes;
            else if (def.Kind == WeaponKind.HeatMissile || def.Kind == WeaponKind.LockMissile) kind = Missiles;
            else kind = src != null && AiDirector.IsBig(src) ? Turret : Guns;
            if (!_by.TryGetValue(e.Entity, out var d)) _by[e.Entity] = d = new Dictionary<string, float>();
            d[kind] = (d.TryGetValue(kind, out float v) ? v : 0f) + e.Value;
        }

        public float Total(int ship) { float t = 0f; if (_by.TryGetValue(ship, out var d)) foreach (var kv in d) t += kv.Value; return t; }
        public float Of(int ship, string kind) => _by.TryGetValue(ship, out var d) && d.TryGetValue(kind, out float v) ? v : 0f;

        /// <summary>"flak 40%, turret guns 35%, ..." (largest first), or "none".</summary>
        public string Shares(int ship) => Shares(new[] { ship });

        /// <summary>The same over several ships together (a wing).</summary>
        public string Shares(IEnumerable<int> ships)
        {
            var d = new Dictionary<string, float>();
            foreach (int ship in ships)
                if (_by.TryGetValue(ship, out var one)) foreach (var kv in one) d[kv.Key] = (d.TryGetValue(kv.Key, out float v0) ? v0 : 0f) + kv.Value;
            if (d.Count == 0) return "none";
            float total = 0f; foreach (var kv in d) total += kv.Value;
            var list = new List<KeyValuePair<string, float>>(d);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            var sb = new StringBuilder();
            foreach (var kv in list) { if (sb.Length > 0) sb.Append(", "); sb.Append(kv.Key).Append(' ').Append((100f * kv.Value / total).ToString("0", CultureInfo.InvariantCulture)).Append('%'); }
            return sb.ToString();
        }

        public JObject ToJson(int ship)
        {
            var o = new JObject();
            if (_by.TryGetValue(ship, out var d)) foreach (var kv in d) o[kv.Key] = Math.Round(kv.Value, 1);
            return o;
        }
    }

    /// <summary>
    /// The Warden runs (the capital-ship balance check): <c>Monolith.exe -batchmode -nographics -mnDogfightSoak 0 -mnWardenRun 8 -mnOut dir
    /// [-mnDifficulty Medium] [-mnRunSeconds 300] [-mnNoFighters] [-mnWingOrder cover|attack|none]</c>. The AI flies a mortal player's ship
    /// straight at the Warden's turrets (the beam turrets first, then whichever is nearest) in the dogfight, as a player stripping the corvette
    /// would; the wingmen are given one order at the start. Each run ends when the player's ship dies, the loop ends or the time is up, and a
    /// fresh loop starts the next. Logs, per run: how long the ship lived, how long it spent within 1 km of the Warden's hull, how many of the
    /// Warden's turrets were destroyed (by the player and in all), how many turrets fired at it at once (most, and on average while close), how
    /// many turret shots came at it, and what its damage came from. Writes warden_runs.txt and warden_runs.json.
    /// </summary>
    public static class WardenRun
    {
        public const string Flag = "-mnWardenRun";
        public static bool Requested => CommandLine.Has(Flag);

        sealed class Run
        {
            public int Index;
            public float Alive, Inside, FirstInside = -1f, OnPlayerSum, OnPlayerSamples;
            public int TurretsByPlayer, TurretsAll, BeamsDown, MaxOnPlayer, ShotsAtPlayer, ShotsInside;
            public string End;
            public bool Died;
            public string Damage;
            public JObject DamageJson;
            public int Turrets;
            /// <summary>Seconds within 1 km and turret shots at the player meanwhile, by how many of the Warden's guns were down: 0-2, 3-5, 6 or more.</summary>
            public readonly float[] BandSeconds = new float[3];
            public readonly int[] BandShots = new int[3];
        }

        static int Band(int down) => down < 3 ? 0 : down < 6 ? 1 : 2;

        public static int RunFromCommandLine()
        {
            int runs = Mathf.Max(1, Mathf.RoundToInt(CommandLine.GetFloat(Flag, 8f)));
            float limit = CommandLine.GetFloat("-mnRunSeconds", 300f);
            bool noFighters = CommandLine.Has("-mnNoFighters");
            string wing = CommandLine.Get("-mnWingOrder", "cover");
            string outDir = CommandLine.Get("-mnOut", "warden_runs");
            bool trace = CommandLine.Has("-mnSoakTrace");
            Directory.CreateDirectory(outDir);
            int level = AiData.Default.LevelOf(CommandLine.Get("-mnDifficulty"));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var results = new List<Run>();
            using (var lab = new AiLab(level))
            using (var ledger = new DamageLedger(lab.W))
            {
                var battle = new DogfightBattle(lab.W, lab.Flight, lab.Rig, lab.Ai)
                {
                    Autopilot = true, PlayerInvulnerable = false, AutopilotStripsTurrets = true, NoFighters = noFighters,
                };
                lab.W.Sim.Register(battle);
                int playerId = GameWorld.NoShip, wardenId = GameWorld.NoShip;
                Run cur = null;
                var sub = GameEvents.Subscribe(CombatEvents.SubsystemDestroyed, e =>
                {
                    if (cur == null || e.Entity != wardenId) return;
                    var wc = lab.W.Combat.Get(wardenId);
                    var part = wc?.FindPart(e.Subject);
                    if (part == null || !part.IsTurret) return;
                    cur.TurretsAll++;
                    if (e.Other == playerId) cur.TurretsByPlayer++;
                    if (battle.Data.corvette.orders.weapons.ContainsKey(e.Subject)) cur.BeamsDown++;
                });
                var hitDiag = new List<string>();
                var subHit = GameEvents.Subscribe(CombatEvents.HitHull, e =>
                {
                    if (!trace || cur == null || e.Entity != wardenId || e.Other != playerId) return;
                    var wc = lab.W.Combat.Get(wardenId); var pl = lab.W.Ships.Get(playerId);
                    if (wc == null || pl == null) return;
                    var tp = pl.TargetPart >= 0 && pl.TargetPart < wc.Parts.Count ? wc.Parts[pl.TargetPart] : null;
                    Vector3 local = Quaternion.Inverse(wc.Ship.Rotation) * (e.Position - wc.Ship.Position);
                    Vector3 from = Quaternion.Inverse(wc.Ship.Rotation) * (pl.Position - wc.Ship.Position);
                    if (hitDiag.Count < 40) hitDiag.Add($"hit {e.Subject} local ({local.x:0.0},{local.y:0.0},{local.z:0.0}) from ({from.x:0},{from.y:0},{from.z:0}) target part {tp?.Name} at ({tp?.LocalPosition.x:0.0},{tp?.LocalPosition.y:0.0},{tp?.LocalPosition.z:0.0}) dist {(tp != null ? Vector3.Distance(local, tp.LocalPosition) : -1f):0.0}");
                });
                for (int r = 0; r < runs; r++)
                {
                    if (r == 0) battle.Start(); else battle.Restart();
                    ledger.Reset();
                    playerId = battle.Player.Id; wardenId = battle.CorvetteId;
                    cur = new Run { Index = r + 1 };
                    var wc0 = lab.W.Combat.Get(wardenId);
                    foreach (var p in wc0.Parts) if (p.IsTurret) cur.Turrets++;
                    lab.Guns.ShotsAt.Remove(playerId);
                    float t0 = lab.W.Time;
                    bool ordered = false;
                    int steps = Mathf.RoundToInt(limit * Sim.Hz);
                    for (int i = 0; i < steps; i++)
                    {
                        lab.W.Sim.StepOnce();
                        if (!ordered && lab.W.Time - t0 > 1f)
                        {
                            ordered = true;
                            // The player's order to the wing: cover me (they keep fighters off), attack my target (they join the turret runs), or none.
                            if (wing == "attack") { battle.Player.Target = wardenId; lab.Ai.PlayerWing.Give(WingOrder.Attack); }
                            else if (wing == "cover") lab.Ai.PlayerWing.Give(WingOrder.Cover);
                        }
                        var pc = lab.W.Combat.Get(playerId);
                        if (pc == null || !pc.Alive) { cur.Died = true; cur.End = "player ship destroyed"; break; }
                        if (battle.Outcome != DogfightBattle.State.Fighting) { cur.End = battle.OutcomeText; break; }
                        cur.Alive = lab.W.Time - t0;
                        var wc = lab.W.Combat.Get(wardenId);
                        if (wc != null && wc.Alive)
                        {
                            float hull = ShipHits.DistanceToHull(wc.Ship, wc, battle.Player.Position, out _);
                            bool inside = hull < 1000f;
                            if (inside) { cur.Inside += Sim.Dt; cur.BandSeconds[Band(cur.TurretsAll)] += Sim.Dt; if (cur.FirstInside < 0f) cur.FirstInside = lab.W.Time - t0; }
                            if (i % 6 == 0)
                            {
                                int on = lab.Guns.FiringAt(playerId, 2f);
                                cur.MaxOnPlayer = Mathf.Max(cur.MaxOnPlayer, on);
                                if (inside) { cur.OnPlayerSum += on; cur.OnPlayerSamples++; }
                            }
                            int shots = lab.Guns.ShotsAt.TryGetValue(playerId, out int s) ? s : 0;
                            if (inside) { cur.ShotsInside += shots - cur.ShotsAtPlayer; cur.BandShots[Band(cur.TurretsAll)] += shots - cur.ShotsAtPlayer; }
                            cur.ShotsAtPlayer = shots;
                        }
                        if (trace && i % Sim.Hz == 0)
                        {
                            var p = battle.Player; var ap = lab.Ai.PilotOf(p.Id); var tc = lab.W.Combat.Get(p.Target);
                            string part = tc != null && p.TargetPart >= 0 && p.TargetPart < tc.Parts.Count ? tc.Parts[p.TargetPart].Name + " " + tc.Parts[p.TargetPart].Health01.ToString("0.00") : "-";
                            float hd = wc != null && wc.Alive ? ShipHits.DistanceToHull(wc.Ship, wc, p.Position, out _) : -1f;
                            AiStats.Line sl = null;
                            foreach (var kv in lab.Ai.Stats.Lines) if (kv.Key.EndsWith("(player ship)")) sl = kv.Value;
                            MnLog.Info($"TRACE shots {sl?.Shots} hits {sl?.Hits} energy {pc.Energy:0} t={lab.W.Time - t0:0} mode {ap?.ModeName} goal {ap?.Goal.Kind} speed {p.Velocity.magnitude:0} target {tc?.Ship.Callsign ?? "-"} part {part} hull {hd:0} m shield {pc.Shield01:0.00} hp {pc.Hull01:0.00} on it {lab.Guns.FiringAt(playerId, 2f)}");
                        }
                        bool anyTurret = false;
                        if (wc != null && wc.Alive) foreach (var p in wc.Parts) if (p.IsTurret && !p.Destroyed) { anyTurret = true; break; }
                        if (!anyTurret) { cur.End = "every turret down"; break; }
                    }
                    if (cur.End == null) cur.End = $"alive at the {limit:0} s limit";
                    cur.Damage = ledger.Shares(playerId);
                    cur.DamageJson = ledger.ToJson(playerId);
                    results.Add(cur);
                    MnLog.Info($"WARDEN run {cur.Index}: {cur.End} after {cur.Alive:0} s; {cur.Inside:0} s within 1 km (first at {cur.FirstInside:0} s); turrets destroyed {cur.TurretsAll} of {cur.Turrets} ({cur.TurretsByPlayer} by the player, beams {cur.BeamsDown}); turrets on it most {cur.MaxOnPlayer}, avg close {(cur.OnPlayerSamples > 0 ? cur.OnPlayerSum / cur.OnPlayerSamples : 0f):0.0}; turret shots at it {cur.ShotsAtPlayer} ({cur.ShotsInside} while close); damage {cur.Damage}");
                }
                sub.Dispose();
                subHit.Dispose();
                foreach (var h in hitDiag) MnLog.Info("HITDIAG " + h);
                battle.Dispose();

                // The summary.
                float Mean(Func<Run, float> f) { float t = 0f; foreach (var x in results) t += f(x); return results.Count > 0 ? t / results.Count : 0f; }
                float Median(Func<Run, float> f) { var l = results.ConvertAll(x => f(x)); l.Sort(); return l.Count == 0 ? 0f : l.Count % 2 == 1 ? l[l.Count / 2] : 0.5f * (l[l.Count / 2 - 1] + l[l.Count / 2]); }
                int died = results.FindAll(x => x.Died).Count;
                int several = results.FindAll(x => x.TurretsByPlayer >= 3).Count;
                var sb = new StringBuilder();
                sb.AppendLine($"Warden runs at {lab.Ai.LevelName}: {results.Count} runs of up to {limit:0} s, the AI flying a mortal player ship at the Warden's turrets, {(noFighters ? "no Earth fighters" : "the dogfight's waves on")}, wing order '{wing}'; {sw.Elapsed.TotalSeconds:0} s real.");
                sb.AppendLine($"Alive: mean {Mean(x => x.Alive):0} s, median {Median(x => x.Alive):0} s; died in {died} of {results.Count}.");
                sb.AppendLine($"Within 1 km of the Warden's hull: mean {Mean(x => x.Inside):0} s, median {Median(x => x.Inside):0} s.");
                sb.AppendLine($"Turrets destroyed by the player: mean {Mean(x => x.TurretsByPlayer):0.0}, median {Median(x => x.TurretsByPlayer):0}; 3 or more in {several} of {results.Count}; beam turrets down mean {Mean(x => x.BeamsDown):0.0}.");
                sb.AppendLine($"Turrets firing at the player at once: most {Mean(x => x.MaxOnPlayer):0.0} (mean of runs), average while close {Mean(x => x.OnPlayerSamples > 0 ? x.OnPlayerSum / x.OnPlayerSamples : 0f):0.0}; turret shots at it per second close {Mean(x => x.Inside > 1f ? x.ShotsInside / x.Inside : 0f):0.0}.");
                string BandRate(int b)
                {
                    float sec = 0f; int n = 0;
                    foreach (var x in results) { sec += x.BandSeconds[b]; n += x.BandShots[b]; }
                    return sec > 5f ? $"{n / sec:0.00} a second ({sec:0} s)" : "-";
                }
                sb.AppendLine($"Stripping thins the fire: turret shots at the player per second within 1 km with 0-2 of the Warden's turrets down {BandRate(0)}, 3-5 down {BandRate(1)}, 6 or more down {BandRate(2)}.");
                foreach (var x in results)
                    sb.AppendLine($"  run {x.Index}: {x.End} after {x.Alive:0} s, {x.Inside:0} s close, turrets {x.TurretsByPlayer} by the player ({x.TurretsAll} in all), most on it {x.MaxOnPlayer}, damage {x.Damage}");
                sb.AppendLine();
                sb.AppendLine(lab.Ai.Stats.Table());
                File.WriteAllText(Path.Combine(outDir, "warden_runs.txt"), sb.ToString());
                var arr = new JArray();
                foreach (var x in results)
                    arr.Add(new JObject
                    {
                        ["run"] = x.Index, ["end"] = x.End, ["died"] = x.Died, ["alive"] = Math.Round(x.Alive, 1), ["inside1km"] = Math.Round(x.Inside, 1),
                        ["firstInside"] = Math.Round(x.FirstInside, 1), ["turretsByPlayer"] = x.TurretsByPlayer, ["turretsAll"] = x.TurretsAll, ["beamsDown"] = x.BeamsDown,
                        ["maxOnPlayer"] = x.MaxOnPlayer, ["avgOnPlayerClose"] = x.OnPlayerSamples > 0 ? Math.Round(x.OnPlayerSum / x.OnPlayerSamples, 2) : 0,
                        ["shotsAtPlayer"] = x.ShotsAtPlayer, ["shotsClose"] = x.ShotsInside, ["damage"] = x.DamageJson,
                    });
                File.WriteAllText(Path.Combine(outDir, "warden_runs.json"), new JObject
                {
                    ["level"] = lab.Ai.LevelName, ["runs"] = arr, ["noFighters"] = noFighters, ["wing"] = wing, ["limit"] = limit,
                    ["meanAlive"] = Math.Round(Mean(x => x.Alive), 1), ["meanInside"] = Math.Round(Mean(x => x.Inside), 1), ["meanTurretsByPlayer"] = Math.Round(Mean(x => x.TurretsByPlayer), 2),
                }.ToString());
                foreach (var l in sb.ToString().Split('\n')) if (l.Trim().Length > 0) MnLog.Info("WARDEN " + l.TrimEnd());
                return lab.Errors == 0 ? 0 : 1;
            }
        }
    }
}
