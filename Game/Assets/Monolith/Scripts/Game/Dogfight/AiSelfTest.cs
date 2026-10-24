using System;
using System.Collections.Generic;
using System.Globalization;
using Monolith.AI;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// Headless checks of the AI on the real sim, flight model, weapons and damage: <c>Monolith.exe -batchmode -nographics -mnAiSelfTest</c>.
    /// Each line logs <c>AI PASS</c> or <c>AI FAIL</c> with the numbers: the data, steering, a dogfight to the kill, keeping off a capital hull,
    /// missiles, evading, peeling off (and drones not), a bomber's torpedo run, turrets (and silenced turrets), heavy beams, the wingman orders.
    /// </summary>
    public static class AiSelfTest
    {
        const string Flag = "-mnAiSelfTest";
        static int _checks, _fails;
        public static bool Requested => CommandLine.Has(Flag);

        static void Check(bool ok, string what)
        {
            _checks++;
            if (!ok) _fails++;
            MnLog.Info($"AI {(ok ? "PASS" : "FAIL")}: {what}");
        }

        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        public static bool Run()
        {
            _checks = 0; _fails = 0;
            var tests = new (string, Action)[]
            {
                ("data", Data), ("steering", Steering), ("dogfight", Dogfight), ("hull", HullAvoidance), ("missiles", Missiles),
                ("evade", Evade), ("peel", Peel), ("bomber", Bomber), ("turrets", Turrets), ("beams", Beams), ("orders", Orders),
            };
            foreach (var (name, t) in tests)
            {
                try { t(); }
                catch (Exception e) { _checks++; _fails++; MnLog.Error($"AI FAIL: {name} threw {e}"); }
            }
            MnLog.Info($"AI SELFTEST {(_fails == 0 ? "OK" : "FAILED")}: {_checks - _fails} of {_checks} checks pass");
            return _fails == 0;
        }

        // ------------------------------------------------------------------------------------------------ the data

        static void Data()
        {
            var d = AiData.Default;
            Check(d.Difficulty.Count == 5 && d.Difficulty.Levels[2] == "Medium", $"five difficulty levels, Medium the default ({string.Join(", ", d.Difficulty.Levels)})");
            var drone = d.Resolve("drone", 2, true);
            Check(drone.Fearless && drone.Courage == 0f, "Earth's drones are fearless with courage zero (design section 7)");
            var ace = d.Resolve("ascended_ace", 2, true);
            Check(ace.Accuracy > drone.Accuracy && ace.Evasion > drone.Evasion && ace.Patience > drone.Patience && ace.TurnScale == 1f && drone.TurnScale < 1f,
                  $"the ascended ace beats a drone on every knob and turns at full rate (ace {F(ace.Accuracy)}/{F(ace.Evasion)}/{F(ace.Patience)} turn {F(ace.TurnScale)}, drone {F(drone.Accuracy)}/{F(drone.Evasion)}/{F(drone.Patience)} turn {F(drone.TurnScale)})");
            var easy = d.Resolve("drone", 0, true); var insane = d.Resolve("drone", 4, true);
            Check(Mathf.Approximately(easy.TurnScale, 1f / 3f) && insane.TurnScale == 1f && easy.FireDelay == 4f && insane.FireDelay == 1f && easy.MaxPlayerAttackers == 2,
                  "FS2's level tables: Earth turns at a third and fires at a quarter rate on Very easy, full on Insane, two on the player at most on Very easy");
            var wing = d.Resolve("wingman", 2, false);
            Check(!wing.Fearless && wing.TurnScale == 1f && Mathf.Approximately(wing.FireDelay, 1.25f), "wingmen can be afraid, turn at full rate and use FS2's friendly fire delay");
        }

        // ------------------------------------------------------------------------------------------------ steering

        sealed class Steer : IPilotSource
        {
            public Vector3 Goal; public ShipState Me;
            public ControlInput Sample(in SimFrame f) { var i = new ControlInput { Throttle = 0.5f }; AiSteer.TurnTo(Me, Goal, 1f, ref i); return i; }
        }

        static void Steering()
        {
            using (var lab = new AiLab(2))
            {
                var st = lab.Fighter("earth_interceptor", 1, Vector3.zero, Quaternion.identity, 0.5f, null);
                var p = new Steer { Me = st, Goal = Quaternion.Euler(-30f, 120f, 0f) * Vector3.forward };
                lab.W.Pilots.Attach(st.Id, p);
                float settledAt = -1f, worst = 0f;
                for (int i = 0; i < 6 * Sim.Hz; i++)
                {
                    lab.W.Sim.StepOnce();
                    float a = Vector3.Angle(st.Forward, p.Goal);
                    if (settledAt < 0f && a < 2f) settledAt = lab.W.Time;
                    if (settledAt >= 0f) worst = Mathf.Max(worst, a);
                }
                Check(settledAt > 0f && settledAt < 3.5f && worst < 4f, $"a fighter turns 120 degrees onto a heading in {F(settledAt)} s and holds it within {F(worst)} deg (no overshoot)");
            }
        }

        // ------------------------------------------------------------------------------------------------ a dogfight

        static void Dogfight()
        {
            using (var lab = new AiLab(2))
            {
                var hunter = lab.Fighter("vanguard_fighter", 0, Vector3.zero, Quaternion.identity, 0.6f, "wingman", AiRole.Fighter, "Hunter");
                var prey = lab.Fighter("earth_interceptor", 1, new Vector3(150f, 50f, 900f), Quaternion.Euler(0f, 60f, 0f), 0.6f, "drone", AiRole.Fighter, "Prey");
                var pc = lab.W.Combat.Get(prey.Id);
                lab.Run(90f, () => !pc.Alive);
                var l = lab.Ai.Stats.Get(lab.Ai.PilotOf(hunter.Id).Skill.Key);
                Check(!pc.Alive, $"a wingman hunts down a drone and kills it ({F(lab.W.Time)} s, shots {l.Shots}, hits {l.Hits}, tail {F(l.TailShare * 100f)}%)");
                Check(l.Shots > 0 && l.Hits > 0 && l.TailSeconds > 0f, "it gets on the tail, fires its guns with lead and lands hits");
                Check(lab.Errors == 0, $"no errors in the fight ({lab.Errors})");
            }
        }

        // ------------------------------------------------------------------------------------------------ capital hulls

        static void HullAvoidance()
        {
            using (var lab = new AiLab(2))
            {
                var capital = SpawnCorvette(lab, 1, new Vector3(0f, 0f, 1200f), Quaternion.Euler(0f, 90f, 0f), true);
                // A fighter of ours ordered to attack the corvette: attack runs at the hull, breaking off before it, again and again.
                var f = lab.Fighter("vanguard_fighter", 0, Vector3.zero, Quaternion.identity, 0.9f, "wingman", AiRole.Fighter, "Runner");
                lab.Ai.PilotOf(f.Id).SetGoal(new AiGoal { Kind = GoalKind.Attack, Target = capital.Id, Part = -1 });
                lab.W.Combat.Get(f.Id).Invulnerable = true;
                // And one that just wants to fly through it to the far side.
                var g = lab.Fighter("vanguard_fighter", 0, new Vector3(0f, 0f, 200f), Quaternion.identity, 1f, "wingman", AiRole.Fighter, "Through");
                lab.Ai.PilotOf(g.Id).SetGoal(new AiGoal { Kind = GoalKind.Form, Target = capital.Id, Part = -1 });   // its slot is inside the hull
                lab.W.Combat.Get(g.Id).Invulnerable = true;
                int hits = 0;
                var sub = GameEvents.Subscribe(CombatEvents.Collision, e => { if (e.Entity == capital.Id || e.Other == capital.Id) hits++; });
                lab.Run(60f);
                sub.Dispose();
                var l = lab.Ai.Stats.Get(lab.Ai.PilotOf(f.Id).Skill.Key);
                Check(hits == 0, $"fighters on attack runs at a capital ship, and one whose formation slot is inside it, never touch its hull in 60 s ({hits} contacts, {l.Shots} shots at it)");
                Check(l.Shots > 20, "the attack runs fire on the capital ship");
            }
        }

        static ShipState SpawnCorvette(AiLab lab, int team, Vector3 pos, Quaternion rot, bool stationary, string turretProfile = "earth_turret", Dictionary<string, string> weapons = null)
        {
            var cls = Monolith.Flight.ShipCatalog.Load("earth_corvette");
            var st = lab.Flight.Spawn(cls, pos, rot, 0f, 0f, team, "Warden");
            var go = Monolith.ShipArt.ShipArtCatalog.Instantiate("earth_corvette", null);
            var parts = ShipArtVisual.Parts(go.GetComponent<Monolith.ShipArt.ShipDamage>());
            lab.Rig.Spawn(st, "earth_corvette", null, null, null, parts, 0.5f);
            UnityEngine.Object.Destroy(go);
            lab.Ai.AddCapital(st.Id, new CapitalOrders { Stationary = stationary, TurretProfile = turretProfile, Weapons = weapons ?? new Dictionary<string, string>() });
            return st;
        }

        // ------------------------------------------------------------------------------------------------ missiles

        static void Missiles()
        {
            using (var lab = new AiLab(4))
            {
                var shooter = lab.Fighter("earth_fighter", 1, Vector3.zero, Quaternion.identity, 0.6f, "drone", AiRole.Fighter, "Shooter");
                var prey = lab.Fighter("vanguard_fighter", 0, new Vector3(0f, 0f, 700f), Quaternion.identity, 0.6f, null, AiRole.Fighter, "Prey");
                lab.W.Pilots.Attach(prey.Id, new ScriptedPilot(new[] { new ScriptedPilot.Key(0f, new ControlInput { Throttle = 0.5f, Yaw = 0.15f }) }));
                lab.W.Combat.Get(prey.Id).Invulnerable = true;
                int locks = 0;
                var sub = GameEvents.Subscribe(CombatEvents.LockAcquired, e => { if (e.Entity == shooter.Id) locks++; });
                lab.Run(40f, () => lab.Ai.Stats.Get(lab.Ai.PilotOf(shooter.Id).Skill.Key).Missiles > 0);
                sub.Dispose();
                var l = lab.Ai.Stats.Get(lab.Ai.PilotOf(shooter.Id).Skill.Key);
                Check(l.Missiles > 0, $"a drone with missiles fires one at a target in front of it ({l.Missiles} fired, {locks} locks, after {F(lab.W.Time)} s)");
            }
        }

        static void Evade()
        {
            using (var lab = new AiLab(4))
            {
                // A high-evasion pilot with a heat-seeker coming: it turns hard across the missile's path and throws decoys.
                var me = lab.Fighter("vanguard_fighter", 0, Vector3.zero, Quaternion.identity, 0.7f, "ascended_ace", AiRole.Fighter, "Dodger");
                var launcher = lab.Fighter("earth_interceptor", 1, new Vector3(0f, 0f, -500f), Quaternion.identity, 0.7f, null, AiRole.Fighter, "Launcher");
                var lc = lab.W.Combat.Get(launcher.Id);
                int decoys = 0;
                var sub = GameEvents.Subscribe(CombatEvents.DecoyLaunched, e => { if (e.Entity == me.Id) decoys++; });
                int fired = 0;
                for (int k = 0; k < 4; k++)
                {
                    lab.W.Pilots.Attach(launcher.Id, new ScriptedPilot(new[] { new ScriptedPilot.Key(0f, new ControlInput { Throttle = 0.7f, TargetShip = me.Id, SelectMissilePlusOne = 1, Buttons = ControlButtons.MissileFire }) }));
                    lab.Run(0.6f);
                    lab.W.Pilots.Attach(launcher.Id, new ScriptedPilot(new[] { new ScriptedPilot.Key(0f, new ControlInput { Throttle = 0.7f }) }));
                    lab.Run(5f);
                    fired++;
                }
                sub.Dispose();
                var l = lab.Ai.Stats.Get(lab.Ai.PilotOf(me.Id).Skill.Key);
                Check(l.Evades > 0, $"an evasive pilot reacts to heat-seekers on its tail ({l.Evades} evasions, {decoys} decoys over {fired} missiles)");
                Check(decoys > 0, "and throws decoys at them (FS2's countermeasure chance per level, scaled by evasion)");
            }
        }

        static void Peel()
        {
            using (var lab = new AiLab(2))
            {
                var home = SpawnCorvette(lab, 0, new Vector3(0f, 0f, -1500f), Quaternion.identity, true, "diaspora_turret");
                var wing = lab.Fighter("vanguard_fighter", 0, Vector3.zero, Quaternion.identity, 0.7f, "wingman", AiRole.Fighter, "Hurt");
                var drone = lab.Fighter("earth_interceptor", 1, new Vector3(0f, 0f, 600f), Quaternion.Euler(0f, 180f, 0f), 0.7f, "drone", AiRole.Fighter, "Drone");
                lab.W.Combat.Get(wing.Id).Hull = lab.W.Combat.Get(wing.Id).HullMax * 0.2f;
                lab.W.Combat.Get(drone.Id).Hull = lab.W.Combat.Get(drone.Id).HullMax * 0.05f;
                lab.W.Combat.Get(drone.Id).Invulnerable = true;
                lab.Ai.PlayerWing.Add(lab.Ai.PilotOf(wing.Id));
                lab.Run(3f);
                var wp = lab.Ai.PilotOf(wing.Id); var dp = lab.Ai.PilotOf(drone.Id);
                Check(wp.Peeling, $"a wingman at 20% hull pulls out of the fight (mode {wp.ModeName}; courage {F(wp.Skill.Courage)} peels below {F(Mathf.Lerp(0.5f, 0.15f, wp.Skill.Courage) * 100f)}%)");
                Check(!dp.Peeling, $"a drone at 5% hull keeps fighting: no fear (mode {dp.ModeName})");
                Check(lab.Ai.Radio.Said.Exists(s => s.StartsWith("Hurt:")), $"a wingman pulling out says so on the radio ('{Last(lab)}')");
            }
        }

        // ------------------------------------------------------------------------------------------------ bombers, turrets, beams

        static void Bomber()
        {
            using (var lab = new AiLab(2))
            {
                var cap = SpawnCorvette(lab, 0, new Vector3(0f, 0f, 2500f), Quaternion.Euler(0f, 90f, 0f), true, "diaspora_turret");
                var b = lab.Fighter("earth_bomber", 1, Vector3.zero, Quaternion.identity, 0.8f, "drone_bomber", AiRole.Bomber, "Censer");
                var p = lab.Ai.PilotOf(b.Id);
                p.DefaultGoal = new AiGoal { Kind = GoalKind.Bomb, Target = cap.Id, Part = -1 };
                p.SetGoal(p.DefaultGoal);
                lab.W.Combat.Get(b.Id).Invulnerable = true;
                int torps = 0; float firstAt = -1f;
                var l0 = lab.Ai.Stats.Get(p.Skill.Key);
                lab.Run(90f, () => { if (l0.Torpedoes > 0 && firstAt < 0f) firstAt = lab.W.Time; return l0.Torpedoes >= 2; });
                torps = l0.Torpedoes;
                Check(torps >= 1, $"a bomber lines up on a capital ship, locks and launches its torpedo (first at {F(firstAt)} s, {torps} launched)");
                Check(torps >= 2, "then peels away and comes round for another run");
                Check(lab.Guns.TorpedoShots > 0 || lab.Guns.GunShots > 0, $"the capital's guns answer: at torpedoes {lab.Guns.TorpedoShots}, at the bomber {lab.Guns.GunShots}, flak {lab.Guns.FlakShots}");
            }
        }

        static void Turrets()
        {
            using (var lab = new AiLab(2))
            {
                var cap = SpawnCorvette(lab, 1, new Vector3(0f, 0f, 900f), Quaternion.Euler(0f, 90f, 0f), true);
                var f = lab.Fighter("vanguard_fighter", 0, new Vector3(-300f, 150f, 300f), Quaternion.identity, 0.6f, null);
                lab.W.Pilots.Attach(f.Id, new ScriptedPilot(new[] { new ScriptedPilot.Key(0f, new ControlInput { Throttle = 0.6f, Yaw = 0.25f }) }));
                lab.W.Combat.Get(f.Id).Invulnerable = true;
                var shots = new Dictionary<int, int>();
                var sub = GameEvents.Subscribe(CombatEvents.WeaponFired, e => { if (e.Entity == cap.Id) shots[0] = (shots.TryGetValue(0, out int n) ? n : 0) + 1; });
                lab.Run(15f);
                int before = shots.TryGetValue(0, out int b0) ? b0 : 0;
                Check(before > 0 && lab.Guns.FlakShots > 0 && lab.Guns.GunShots > 0, $"a capital's turrets track and fire at a fighter in range (guns {lab.Guns.GunShots}, flak {lab.Guns.FlakShots})");
                // Silence every turret: no more shots.
                var cc = lab.W.Combat.Get(cap.Id);
                foreach (var part in cc.Parts)
                    if (part.IsTurret) lab.W.Damage.Add(new DamageRequest { Ship = cap.Id, Amount = part.MaxHp * 10f, SubsystemMult = 1f, Part = part.Index, BypassShield = true, Kind = DamageKind.Debug, Point = part.WorldPosition(cap) });
                lab.Run(1f);
                int mid = shots.TryGetValue(0, out int m0) ? m0 : 0;
                lab.Run(15f);
                int after = shots.TryGetValue(0, out int a0) ? a0 : 0;
                sub.Dispose();
                Check((cc.Flags & CombatFlags.TurretsSilenced) != 0 && after == mid, $"destroyed turrets go silent ({after - mid} shots after every turret was destroyed)");
            }
        }

        static void Beams()
        {
            using (var lab = new AiLab(4))
            {
                var beams = new Dictionary<string, string> { ["SUB_aaf_F01"] = "beam_slash", ["SUB_aaf_F03"] = "beam_slash" };
                var warden = SpawnCorvette(lab, 1, new Vector3(0f, 0f, 2500f), Quaternion.Euler(0f, 90f, 0f), true, "earth_turret", beams);
                var cls = Monolith.Flight.ShipCatalog.Load("diaspora_cruiser");
                var mercy = lab.Flight.Spawn(cls, Vector3.zero, Quaternion.Euler(0f, 90f, 0f), 0f, 0f, 0, "Mercy");
                lab.Rig.Spawn(mercy, "diaspora_cruiser", null, null, null, null, 1f, false);
                var mc = lab.W.Combat.Get(mercy.Id);
                float hull0 = mc.Hull;
                int charge = 0, fire = 0, end = 0;
                var subs = new List<IDisposable>
                {
                    GameEvents.Subscribe(CombatEvents.BeamCharge, e => { if (e.Entity == warden.Id) charge++; }),
                    GameEvents.Subscribe(CombatEvents.BeamFire, e => { if (e.Entity == warden.Id) fire++; }),
                    GameEvents.Subscribe(CombatEvents.BeamEnd, e => { if (e.Entity == warden.Id) end++; }),
                };
                lab.Run(12f);
                Check(charge > 0 && fire > 0 && end > 0, $"the Warden's beam turrets charge, fire and stop at an enemy capital in range (charge {charge}, fire {fire}, end {end})");
                float dealt = hull0 - mc.Hull;
                var def = WeaponCatalog.Require("beam_slash");
                Check(dealt > def.Damage * 5f, $"a beam burn hurts the hull: {F(dealt)} hp ({F(def.Damage)} per {F(def.BeamDamageInterval)} s tick for {F(def.Life)} s)");
                // Destroy both beam turrets: the beams go quiet.
                var wc = lab.W.Combat.Get(warden.Id);
                foreach (var name in beams.Keys)
                {
                    var part = wc.FindPart(name);
                    lab.W.Damage.Add(new DamageRequest { Ship = warden.Id, Amount = part.MaxHp * 10f, SubsystemMult = 1f, Part = part.Index, BypassShield = true, Kind = DamageKind.Debug, Point = part.WorldPosition(warden) });
                }
                lab.Run(1f);
                int c0 = charge;
                lab.Run(60f);
                foreach (var s in subs) s.Dispose();
                Check(charge == c0, $"with its beam turrets destroyed the Warden fires no more beams ({charge - c0} charges after)");
            }
        }

        // ------------------------------------------------------------------------------------------------ wingman orders

        static void Orders()
        {
            using (var lab = new AiLab(2))
            {
                var player = lab.Fighter("vanguard_fighter", 0, Vector3.zero, Quaternion.identity, 0.5f, null, AiRole.Fighter, "Sable 1");
                lab.W.LocalPlayerShip = player.Id;
                var hold = new HoldPilot();
                lab.W.Pilots.Attach(player.Id, hold);
                var wingmen = new List<AiPilot>();
                for (int i = 0; i < 2; i++)
                {
                    var w = lab.Fighter("vanguard_fighter", 0, new Vector3(i == 0 ? -40f : 40f, 0f, -40f), Quaternion.identity, 0.5f, "wingman", AiRole.Fighter, $"Sable {i + 2}");
                    var p = lab.Ai.PilotOf(w.Id);
                    lab.Ai.PlayerWing.Add(p);
                    p.DefaultGoal = new AiGoal { Kind = GoalKind.Guard, Target = player.Id, Part = -1 };
                    wingmen.Add(p);
                }
                var foe = lab.Fighter("earth_interceptor", 1, new Vector3(0f, 0f, 1500f), Quaternion.Euler(0f, 180f, 0f), 0.5f, "drone", AiRole.Fighter, "Foe");
                var other = lab.Fighter("earth_interceptor", 1, new Vector3(600f, 0f, 1200f), Quaternion.Euler(0f, 180f, 0f), 0.5f, "drone", AiRole.Fighter, "Other");
                lab.W.Combat.Get(foe.Id).Invulnerable = true; lab.W.Combat.Get(other.Id).Invulnerable = true;

                // No target: they say so.
                Press(lab, hold, ControlButtons.OrderAttack);
                Check(lab.Ai.Radio.Said.Count > 0 && lab.Ai.Radio.Said[lab.Ai.Radio.Said.Count - 1].Contains("Need") || lab.Ai.Radio.Said.Exists(s => s.ToLowerInvariant().Contains("target")),
                      $"'attack my target' with nothing targeted gets a dry refusal ('{Last(lab)}')");
                // Attack my target.
                hold.In.TargetShip = other.Id;
                lab.Run(0.2f);
                hold.In.TargetShip = 0;
                Press(lab, hold, ControlButtons.OrderAttack);
                lab.Run(1f);
                Check(wingmen.TrueForAll(w => w.Target == other.Id) && lab.Ai.PlayerWing.Current == WingOrder.Attack, $"'attack my target': both wingmen take the player's target ('{Last(lab)}')");
                // Disable my target: its engines.
                Press(lab, hold, ControlButtons.OrderDisable);
                lab.Run(0.5f);
                var oc = lab.W.Combat.Get(other.Id);
                bool engines = wingmen.TrueForAll(w => { var s = lab.W.Ships.Get(w.Ship); return s.Target == other.Id && s.TargetPart >= 0 && oc.Parts[s.TargetPart].Kind == "engine"; });
                Check(engines, $"'disable my target': they go for its engines ('{Last(lab)}')");
                // Cover me: they fight what attacks the player.
                lab.W.Pilots.Attach(foe.Id, new ScriptedPilot(new[] { new ScriptedPilot.Key(0f, new ControlInput { Throttle = 0.5f, TargetShip = player.Id }) }));
                lab.Run(0.5f);
                Press(lab, hold, ControlButtons.OrderCover);
                lab.Run(2.5f);
                bool Threat(int id) { var s = lab.W.Ships.Get(id); return s != null && (s.Target == player.Id || Vector3.Distance(s.Position, player.Position) < 600f); }
                Check(wingmen.TrueForAll(w => w.Goal.Kind == GoalKind.Guard && Threat(w.Target)), $"'cover me': they turn on the ships that have the player targeted ('{Last(lab)}')");
                // Form up (the foes gone, so nothing distracts them).
                foreach (var id in new[] { foe.Id, other.Id })
                {
                    var c = lab.W.Combat.Get(id); c.Invulnerable = false;
                    lab.W.Damage.Add(new DamageRequest { Ship = id, Amount = 5000f, HullMult = 1f, BypassShield = true, Kind = DamageKind.Debug, Part = -2, Point = c.Ship.Position });
                }
                Press(lab, hold, ControlButtons.OrderForm);
                lab.Run(30f);
                float worst = 0f;
                foreach (var w in wingmen) worst = Mathf.Max(worst, Vector3.Distance(lab.W.Ships.Get(w.Ship).Position, player.Position));
                Check(wingmen.TrueForAll(w => w.Goal.Kind == GoalKind.Form) && worst < 150f, $"'form up': they fly in formation on the player (furthest {F(worst)} m after 30 s; '{Last(lab)}')");
                Check(lab.Ai.Radio.Said.Count >= 5, $"every order is acknowledged on the radio ({lab.Ai.Radio.Said.Count} lines)");
            }
        }

        static string Last(AiLab lab) => lab.Ai.Radio.Said.Count > 0 ? lab.Ai.Radio.Said[lab.Ai.Radio.Said.Count - 1] : "";

        sealed class HoldPilot : IPilotSource
        {
            public ControlInput In = new ControlInput { Throttle = 0.5f };
            public ControlInput Sample(in SimFrame f) => In;
        }

        static void Press(AiLab lab, HoldPilot hold, ControlButtons b)
        {
            hold.In.Buttons |= b;
            lab.Run(0.1f);
            hold.In.Buttons &= ~b;
            lab.Run(0.1f);
        }
    }
}
