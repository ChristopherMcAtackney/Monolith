using System;
using System.Collections.Generic;
using System.Globalization;
using Monolith.Collision;
using Monolith.Core;
using Monolith.Damage;
using Monolith.Flight;
using Monolith.Targeting;
using Monolith.Weapons;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// Headless checks of the combat systems on the real sim, flight model and data: <c>Monolith.exe -batchmode -nographics -mnCombatSelfTest</c>.
    /// Each line logs <c>COMBAT PASS</c> or <c>COMBAT FAIL</c> with the numbers; the weapon numbers are also checked against FS2's table by
    /// tools/combat/check_weapons.py. Covers fire rates and energy, bolt speed and inherited velocity, shield / hull / part damage, the lead
    /// solver and swept hits (no tunnelling), shield recharge, the power choice, damaged engines and sensors and lost mounts, self-healing,
    /// lock-on timing, heat-seekers, decoys, torpedoes shot down, blast falloff, collisions and targeting.
    /// </summary>
    public static class CombatSelfTest
    {
        const string Flag = "-mnCombatSelfTest";
        static int _checks, _fails;

        public static bool Requested => CommandLine.Has(Flag);

        static void Check(bool ok, string what)
        {
            _checks++;
            if (!ok) _fails++;
            MnLog.Info($"COMBAT {(ok ? "PASS" : "FAIL")}: {what}");
        }

        static bool Near(float a, float b, float tol) => Mathf.Abs(a - b) <= tol;
        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>A pilot the test holds in its hand.</summary>
        sealed class Hold : IPilotSource
        {
            public ControlInput In;
            public ControlInput Sample(in SimFrame frame) => In;
        }

        /// <summary>A little world with the real systems and ships.</summary>
        sealed class Lab
        {
            public readonly GameWorld W;
            public readonly FlightSystem Flight;
            public readonly WeaponSystem Weapons;
            public readonly DamageSystem Damage;
            public readonly CollisionSystem Collision;
            public readonly List<GameEvent> Events = new List<GameEvent>();
            readonly IDisposable _sub;

            public Lab(uint seed = 0x2545F491u)
            {
                GameEvents.Reset();
                W = new GameWorld(new Sim());
                GameWorld.SetCurrent(W);
                W.Sim.Register(W.Pilots);
                Flight = new FlightSystem(W);
                W.Sim.Register(new TargetingSystem(W));
                W.Sim.Register(Flight);
                W.Sim.Register(new AimSystem(W));
                Weapons = new WeaponSystem(W, null, seed);
                Damage = new DamageSystem(W);
                Collision = new CollisionSystem(W);
                W.Sim.Register(Weapons); W.Sim.Register(Collision); W.Sim.Register(Damage);
                _sub = GameEvents.SubscribeAll(e => Events.Add(e));
            }

            public (ShipState s, ShipCombat c, Hold p) Ship(string cls, Vector3 pos, Vector3 euler, int team, float speed = 0f, string def = null, float hullScale = 1f, string name = null)
            {
                var klass = ShipCatalog.Load(cls);
                var st = Flight.Spawn(klass, pos, Quaternion.Euler(euler), speed, klass.TopSpeed > 0f ? speed / klass.TopSpeed : 0f, team, name ?? cls + "#" + W.Ships.Count);
                var p = new Hold();
                p.In.Throttle = klass.TopSpeed > 0f ? speed / klass.TopSpeed : 0f;
                W.Pilots.Attach(st.Id, p);
                var d = (def != null ? CombatCatalog.Get(def) : CombatCatalog.Get(cls)).Clone();
                d.HullScale = hullScale;
                var c = Damage.Register(st, d, null, team == 0);
                return (st, c, p);
            }

            public void Run(float seconds) { int n = Mathf.RoundToInt(seconds * Sim.Hz); for (int i = 0; i < n; i++) W.Sim.StepOnce(); }
            public int Count(string id, string subject = null) { int n = 0; foreach (var e in Events) if (e.Id == id && (subject == null || e.Subject == subject)) n++; return n; }
            public void Dispose() => _sub.Dispose();
        }

        public static bool Run()
        {
            _checks = 0; _fails = 0;
            try
            {
                Data();
                FireRate();
                Damage();
                Hammer();
                Tunnelling();
                Lead();
                Shields();
                PowerAndParts();
                Healing();
                Missiles();
                Locks();
                Decoys();
                Torpedo();
                Blast();
                Collisions();
                Targeting();
                Deaths();
            }
            catch (Exception e)
            {
                _fails++;
                MnLog.Error("COMBAT selftest threw: " + e);
            }
            MnLog.Info($"COMBAT SELFTEST {(_fails == 0 ? "OK" : "FAILED")}: {_checks - _fails} of {_checks} checks pass");
            return _fails == 0;
        }

        // ------------------------------------------------------------------------------------------------ the data

        static void Data()
        {
            var w = WeaponCatalog.All;
            Check(w.Count >= 8, $"the weapon table loads ({w.Count} weapons)");
            var n = WeaponCatalog.Require("needle");
            Check(n.Speed == 485f && n.FireWait == 0.15f && n.Damage == 12f && n.ShieldMult == 0.8f && n.HullMult == 0.9f && n.SubsystemMult == 0.3f && n.Life == 2f && n.Energy == 0.2f,
                  "Needle carries Mekhu HL-7's 485 m/s, 0.15 s, 12 damage, 0.8 shield, 0.9 hull, 0.3 subsystem, 2 s, 0.2 energy");
            var t = WeaponCatalog.Require("torpedo_heavy");
            Check(t.Damage == 6800f && t.ShieldMult == 0.02f && t.BlastInner == 100f && t.BlastOuter == 270f && t.Speed == 65f, "the torpedo carries Helios' 6,800 damage, 2% against shields, 100/270 m blast, 65 m/s");
            var d = CombatCatalog.Get("vanguard_fighter");
            Check(d != null && d.Hull == 180f && d.ShieldTotal == 380f && d.WeaponEnergy == 80f && d.Decoys == 25, "the player's fighter carries the Ulysses' 180 hull, 380 shield, 80 energy and 25 decoys");
            Check(CombatRules.Default.ShieldBubbleScale > 0f, "the combat rules load");
        }

        // ------------------------------------------------------------------------------------------------ guns

        static void FireRate()
        {
            var lab = new Lab();
            var (s, c, p) = lab.Ship("vanguard_fighter", Vector3.zero, Vector3.zero, 0);
            p.In.Buttons = ControlButtons.PrimaryFire;
            lab.Run(3f);
            int shots = lab.Count(CombatEvents.WeaponFired, "needle");
            Check(shots == 20, $"holding the fast gun for 3 s fires 20 shots at 0.15 s ({shots})");
            float expect = 80f - 20f * 0.2f + 3f * 2f * 0.3333f;
            Check(Near(c.Energy, expect, 0.4f), $"energy: 80 less 20 x 0.2 plus 3 s of recharge at power 2 x 1/3 is {F(expect)} ({F(c.Energy)})");

            // Speed: one shot, then 30 ticks of flight.
            lab = new Lab();
            (s, c, p) = lab.Ship("vanguard_fighter", Vector3.zero, Vector3.zero, 0);
            p.In.Buttons = ControlButtons.PrimaryFire; lab.Run(1f / 60f); p.In.Buttons = ControlButtons.None;
            var shot = lab.Weapons.Projectiles[0];
            Vector3 start = shot.Pos;
            lab.Run(0.5f);
            float travelled = Vector3.Distance(lab.Weapons.Projectiles[0].Pos, start);
            Check(Near(travelled, 485f * 0.5f, 3f), $"a bolt flies at 485 m/s: {F(travelled)} m in half a second (expect 242.5)");

            // The shooter's velocity is added, as in FS2.
            lab = new Lab();
            (s, c, p) = lab.Ship("vanguard_fighter", Vector3.zero, Vector3.zero, 0, 70f);
            p.In.Buttons = ControlButtons.PrimaryFire; lab.Run(1f / 60f); p.In.Buttons = ControlButtons.None;
            Check(Near(lab.Weapons.Projectiles[0].Vel.magnitude, 485f + 70f, 2f), $"a bolt fired at 70 m/s travels at 485 + 70 ({F(lab.Weapons.Projectiles[0].Vel.magnitude)})");

            // A dry gun does not fire.
            lab = new Lab();
            (s, c, p) = lab.Ship("vanguard_fighter", Vector3.zero, Vector3.zero, 0);
            c.Energy = 0f; c.EnergyRechargePerSecond = 0f;
            p.In.Buttons = ControlButtons.PrimaryFire; lab.Run(1f);
            Check(lab.Count(CombatEvents.WeaponFired) == 0, "with no energy the gun stays silent");
        }

        static void Damage()
        {
            var lab = new Lab();
            var (a, ac, ap) = lab.Ship("vanguard_fighter", Vector3.zero, Vector3.zero, 0);
            var (b, bc, _) = lab.Ship("gtf_ulysses", new Vector3(0f, 0f, 120f), new Vector3(0f, 180f, 0f), 1);
            float shield0 = bc.Shield, hull0 = bc.Hull;
            Check(Near(shield0, 190f, 0.01f), $"the bubble is half the FS2 380 ({F(shield0)})");
            ap.In.Buttons = ControlButtons.PrimaryFire; lab.Run(1f / 60f); ap.In.Buttons = ControlButtons.None;
            lab.Run(0.6f);
            Check(Near(shield0 - bc.Shield, 12f * 0.8f, 0.01f) && Near(bc.Hull, hull0, 0.001f), $"one Needle hit takes 12 x 0.8 = 9.6 off the shield and nothing off the hull ({F(shield0 - bc.Shield)}, hull {F(bc.Hull)})");

            // With the shield down it goes to the hull (x0.9) and to the nearest part (x0.3).
            lab = new Lab();
            (a, ac, ap) = lab.Ship("vanguard_fighter", Vector3.zero, Vector3.zero, 0);
            (b, bc, _) = lab.Ship("gtf_ulysses", new Vector3(0f, 0f, 120f), new Vector3(0f, 180f, 0f), 1);
            bc.ShieldMax = 0f; bc.Shield = 0f;
            a.Target = b.Id;
            float partsBefore = PartHp(bc);
            ap.In.Buttons = ControlButtons.PrimaryFire; lab.Run(1f / 60f); ap.In.Buttons = ControlButtons.None;
            lab.Run(0.6f);
            Check(Near(180f - bc.Hull, 12f * 0.9f, 0.01f), $"a Needle hit with the shield down takes 10.8 off the hull ({F(180f - bc.Hull)})");
            Check(Near(partsBefore - PartHp(bc), 12f * 0.3f, 0.01f), $"and 3.6 off the part it struck ({F(partsBefore - PartHp(bc))})");
            Check(lab.Count(CombatEvents.HitHull) >= 1, "a hull hit raises hit_hull for the juice");
            lab.Dispose();
        }

        static float PartHp(ShipCombat c) { float t = 0; foreach (var p in c.Parts) t += p.Hp; return t; }

        static void Hammer()
        {
            var lab = new Lab();
            var (a, ac, ap) = lab.Ship("vanguard_fighter", Vector3.zero, Vector3.zero, 0);
            var (b, bc, _) = lab.Ship("gtf_ulysses", new Vector3(0f, 0f, 120f), new Vector3(0f, 180f, 0f), 1);
            a.Target = b.Id;
            ap.In.Buttons = ControlButtons.SecondaryFire; lab.Run(1f / 60f); ap.In.Buttons = ControlButtons.None;
            lab.Run(0.6f);
            Check(Near(190f - bc.Shield, 40f * 0.3f, 0.01f), $"a Hammer hit takes 40 x 0.3 = 12 off a shield ({F(190f - bc.Shield)})");
            bc.ShieldMax = 0f; bc.Shield = 0f;
            float parts = PartHp(bc);
            ap.In.Buttons = ControlButtons.SecondaryFire; lab.Run(1f / 60f); ap.In.Buttons = ControlButtons.None;
            lab.Run(0.6f);
            Check(Near(180f - bc.Hull, 40f * 1.3f, 0.01f), $"and 40 x 1.3 = 52 off the hull ({F(180f - bc.Hull)})");
            Check(Near(parts - PartHp(bc), 36f, 0.01f) && lab.Count(CombatEvents.SubsystemDestroyed) == 1, $"and the full 40 into a 36 hp gun mount, which it destroys ({F(parts - PartHp(bc))} taken)");
            lab.Dispose();
        }

        // ------------------------------------------------------------------------------------------------ swept hits and lead

        static void Tunnelling()
        {
            var lab = new Lab();
            var (b, bc, _) = lab.Ship("gtf_ulysses", Vector3.zero, new Vector3(10f, 40f, 20f), 1);
            bc.Shield = 0f;
            var rng = new System.Random(5);
            int hits = 0, n = 2000;
            var box = bc.Shape.Boxes[0];
            for (int i = 0; i < n; i++)
            {
                // A line through a random point inside the hull, from 200 m before it to 200 m after: the path of a bolt at 1,800 m/s over several ticks.
                Vector3 local = new Vector3(((float)rng.NextDouble() * 2f - 1f) * box.Half.x * 0.95f, ((float)rng.NextDouble() * 2f - 1f) * box.Half.y * 0.95f, ((float)rng.NextDouble() * 2f - 1f) * box.Half.z * 0.95f);
                Vector3 p = b.Position + b.Rotation * (box.Centre + local);
                Vector3 dir = UnityEngine.Random.onUnitSphere;
                if (ShipHits.Segment(b, bc, p - dir * 200f, p + dir * 200f, out _)) hits++;
            }
            Check(hits == n, $"a swept path through the hull always hits, however long the step ({hits} of {n})");
            // And one tick of a very fast bolt at a thin target edge.
            int edge = 0;
            for (int i = 0; i < 500; i++)
            {
                float y = ((float)rng.NextDouble() * 2f - 1f) * box.Half.y * 0.9f;
                Vector3 a = b.Position + b.Rotation * new Vector3(-60f, box.Centre.y + y, 0f), z = b.Position + b.Rotation * new Vector3(60f, box.Centre.y + y, 0f);
                if (ShipHits.Segment(b, bc, a, z, out _)) edge++;
            }
            Check(edge == 500, $"a 120 m step across a 4 m tall fighter never skips it ({edge} of 500)");
            lab.Dispose();
        }

        static void Lead()
        {
            // The shooter turns to the solved aim point and fires one bolt; the target flies on at constant velocity. Every case must hit.
            var cases = new (Vector3 pos, Vector3 vel)[]
            {
                (new Vector3(200f, 30f, 600f), new Vector3(-60f, 0f, 0f)),
                (new Vector3(-250f, -30f, 550f), new Vector3(50f, 5f, 20f)),
                (new Vector3(0f, 100f, 800f), new Vector3(30f, -25f, -45f)),
                (new Vector3(120f, -80f, 350f), new Vector3(-55f, 10f, -30f)),
                (new Vector3(-300f, 50f, 700f), new Vector3(65f, 0f, 0f)),
            };
            int ok = 0;
            foreach (var cs in cases)
            {
                var lab = new Lab();
                var (a, ac, ap) = lab.Ship("vanguard_fighter", Vector3.zero, Vector3.zero, 0);
                float speed = cs.vel.magnitude;
                var (b, bc, bp) = lab.Ship("gtf_ulysses", cs.pos, Quaternion.LookRotation(cs.vel).eulerAngles, 1, speed);
                bc.ShieldMax = 0f; bc.Shield = 0f;
                a.Target = b.Id;
                if (!Geo.SolveIntercept(a.Position, a.Velocity, b.Position, b.Velocity, 485f, out Vector3 dir, out float t, out _)) continue;
                lab.Flight.Place(a.Id, Vector3.zero, Quaternion.LookRotation(dir, Vector3.up), 0f, 0f);
                ap.In.Buttons = ControlButtons.PrimaryFire; lab.Run(1f / 60f); ap.In.Buttons = ControlButtons.None;
                lab.Run(t + 0.8f);
                if (bc.Hull < bc.HullMax - 0.1f) ok++;
                lab.Dispose();
            }
            Check(ok == cases.Length, $"firing along the solved lead hits a crossing target every time ({ok} of {cases.Length})");

            // The HUD's lead point agrees with the solver.
            var l2 = new Lab();
            var (a2, ac2, ap2) = l2.Ship("vanguard_fighter", Vector3.zero, Vector3.zero, 0);
            var (b2, bc2, _) = l2.Ship("gtf_ulysses", new Vector3(100f, 0f, 500f), new Vector3(0f, 90f, 0f), 1, 50f);
            a2.Target = b2.Id;
            l2.Run(0.1f);
            Check(ac2.HasLead && ac2.LeadTime > 0.5f && ac2.LeadTime < 2f, $"the lead indicator is published for a target ahead (lead time {F(ac2.LeadTime)} s)");
            l2.Dispose();
        }

        // ------------------------------------------------------------------------------------------------ shield and power

        static void Shields()
        {
            var lab = new Lab();
            var (a, ac, _) = lab.Ship("gtf_ulysses", Vector3.zero, Vector3.zero, 1);
            ac.Shield = 0f; ac.TimeSinceShieldHit = 999f;
            lab.Run(12.5f);
            Check(Near(ac.Shield, 95f, 3f), $"balanced power refills half the bubble in 12.5 s ({F(ac.Shield)} of 190)");
            lab.Dispose();

            lab = new Lab();
            var shipP = lab.Ship("gtf_ulysses", Vector3.zero, Vector3.zero, 1);
            a = shipP.s; ac = shipP.c; var p = shipP.p;
            ac.Shield = 0f; ac.TimeSinceShieldHit = 999f;
            p.In.Buttons = ControlButtons.PowerShields; lab.Run(1f / 60f); p.In.Buttons = ControlButtons.None;
            Check(ac.Power == PowerMode.Shields && Near(ac.ShieldPower, 1.8f, 0.001f) && Near(ac.GunPower, 0.6f, 0.001f), "the quick menu's shields pick sets 1.8x shields, 0.6x guns");
            lab.Run(12.5f);
            Check(Near(ac.Shield, 171f, 5f), $"favouring shields refills 1.8x as fast ({F(ac.Shield)}, expect 171)");
            lab.Dispose();

            // A shield under fire does not refill.
            lab = new Lab();
            (a, ac, _) = lab.Ship("gtf_ulysses", Vector3.zero, Vector3.zero, 1);
            ac.Shield = 100f;
            lab.W.Damage.Add(new DamageRequest { Ship = a.Id, Amount = 10f, ShieldMult = 1f, HullMult = 1f, SubsystemMult = 1f, Point = a.Position + Vector3.forward * 6f, Direction = Vector3.back, Normal = Vector3.forward, Part = -1 });
            lab.Run(0.5f);
            Check(Near(ac.Shield, 90f, 0.5f), $"a hit and half a second later the shield has not started refilling ({F(ac.Shield)})");
            lab.Dispose();
        }

        static void PowerAndParts()
        {
            var lab = new Lab();
            var (a, ac, p) = lab.Ship("vanguard_fighter", Vector3.zero, Vector3.zero, 0, 70f);
            p.In.Throttle = 1f;
            lab.Run(12f);
            Check(Near(a.ForwardSpeed, 70f, 0.5f), $"flat out the fighter makes its 70 m/s ({F(a.ForwardSpeed)})");

            DestroyPart(lab, ac, "engine_port");
            Check(Near(a.SpeedScale, 0.65f, 0.01f) && (ac.Flags & CombatFlags.EnginesCrippled) != 0, $"one of two engines gone caps speed at 65% ({F(a.SpeedScale)})");
            lab.Run(12f);
            Check(Near(a.ForwardSpeed, 70f * 0.65f, 1f), $"and the fighter really slows to {F(70f * 0.65f)} m/s ({F(a.ForwardSpeed)})");
            DestroyPart(lab, ac, "engine_starboard");
            Check(Near(a.SpeedScale, 0.3f, 0.01f) && (ac.Flags & CombatFlags.EnginesDown) != 0, $"both engines gone: it limps at 30% ({F(a.SpeedScale)})");
            lab.Run(14f);
            Check(Near(a.ForwardSpeed, 21f, 1f), $"and crawls at 21 m/s ({F(a.ForwardSpeed)})");

            DestroyPart(lab, ac, "sensors");
            Check(Near(ac.SensorStatic, 1f, 0.001f) && (ac.Flags & CombatFlags.SensorsDown) != 0, "destroyed sensors put full static on the radar and target box");
            Check(lab.Count(CombatEvents.SubsystemDestroyed) == 3, $"each destroyed part raises subsystem_destroyed ({lab.Count(CombatEvents.SubsystemDestroyed)})");

            DestroyPart(lab, ac, "mount_needle");
            Check((ac.Flags & CombatFlags.PrimaryOffline) != 0, "a destroyed gun mount takes the gun offline");
            int before = lab.Count(CombatEvents.WeaponFired);
            p.In.Buttons = ControlButtons.PrimaryFire; lab.Run(1f);
            Check(lab.Count(CombatEvents.WeaponFired) == before, "and holding fire does nothing");
            p.In.Buttons = ControlButtons.SecondaryFire; lab.Run(1f);
            Check(lab.Count(CombatEvents.WeaponFired) > before, "while the heavy gun still works");
            lab.Dispose();

            // Engines favoured adds a little speed.
            lab = new Lab();
            (a, ac, p) = lab.Ship("vanguard_fighter", Vector3.zero, Vector3.zero, 0, 70f);
            p.In.Throttle = 1f;
            p.In.Buttons = ControlButtons.PowerEngines; lab.Run(1f / 60f); p.In.Buttons = ControlButtons.None;
            lab.Run(12f);
            Check(Near(a.SpeedScale, 1.08f, 0.001f) && Near(a.ForwardSpeed, 70f * 1.08f, 1f), $"favouring engines adds 8% speed ({F(a.ForwardSpeed)})");
            lab.Dispose();
        }

        static void DestroyPart(Lab lab, ShipCombat c, string name)
        {
            var part = c.FindPart(name) ?? throw new InvalidOperationException("no part " + name);
            lab.W.Damage.Add(new DamageRequest
            {
                Ship = c.Id, Attacker = GameWorld.NoShip, Weapon = "test", Amount = part.MaxHp * 3f, ShieldMult = 0f, HullMult = 0f, SubsystemMult = 1f,
                Point = part.WorldPosition(c.Ship), Direction = Vector3.up, Normal = Vector3.up, Part = part.Index, BypassShield = true,
            });
            lab.Run(1f / 60f);
        }

        static void Healing()
        {
            var lab = new Lab();
            var (a, ac, _) = lab.Ship("vanguard_fighter", Vector3.zero, Vector3.zero, 0);
            var (b, bc, _) = lab.Ship("gtf_ulysses", new Vector3(500f, 0f, 0f), Vector3.zero, 1);
            foreach (var c in new[] { ac, bc })
            {
                c.Hull = c.HullMax * 0.5f; c.Shield = 0f;
                c.FindPart("sensors").Hp = c.FindPart("sensors").MaxHp * 0.5f;
                lab.W.Damage.Add(new DamageRequest { Ship = c.Id, Amount = 1f, ShieldMult = 0f, HullMult = 0f, SubsystemMult = 0f, Point = c.Ship.Position, Part = -2, BypassShield = true });
            }
            DestroyPart(lab, ac, "engine_port");
            lab.Run(5f);
            Check(Near(ac.Hull01, 0.5f, 0.001f), $"no healing in the first seconds after damage ({F(ac.Hull01)})");
            lab.Run(1.1f + 10f);
            Check(Near(ac.Hull01, 0.5f + 0.008f * 10f, 0.01f), $"after the quiet period the player's hull mends 0.8% a second ({F(ac.Hull01)}, expect 0.58)");
            Check(Near(ac.FindPart("sensors").Health01, 0.5f + 0.012f * 10f, 0.02f), $"damaged parts mend a little faster ({F(ac.FindPart("sensors").Health01)})");
            Check(ac.FindPart("engine_port").Destroyed, "a destroyed part stays destroyed");
            Check(Near(bc.Hull01, 0.5f, 0.001f), "the enemy side does not heal");
            lab.Dispose();
        }

        // ------------------------------------------------------------------------------------------------ missiles

        static void Missiles()
        {
            // A heat seeker fired at a target 700 m ahead, flying away.
            var lab = new Lab();
            var (a, ac, ap) = lab.Ship("vanguard_fighter", Vector3.zero, Vector3.zero, 0, 30f);
            var (b, bc, _) = lab.Ship("gtf_ulysses", new Vector3(0f, 0f, 700f), Vector3.zero, 1, 40f);
            a.Target = b.Id;
            lab.Run(0.1f);
            Check(ac.Lock == LockPhase.Locked, "a heat-seeker's tone sounds with the target in front of it");
            ap.In.Buttons = ControlButtons.MissileFire; lab.Run(1f / 60f); ap.In.Buttons = ControlButtons.None;
            Check(ac.Missiles[0].Ammo == 11, $"firing a missile costs one ({ac.Missiles[0].Ammo} left of 12)");
            float s0 = bc.Shield, low = s0;
            for (int i = 0; i < 9 * 60; i++) { lab.W.Sim.StepOnce(); low = Mathf.Min(low, bc.Shield); }
            Check(Near(s0 - low, 45f * 0.8f, 0.5f), $"the heat seeker finds its target and takes 45 x 0.8 = 36 off its shield (lowest {F(low)} from {F(s0)})");
            Check(lab.Count(CombatEvents.MissileTrack) > 60, "the missile reports its position every tick for the juice");
            lab.Dispose();

            // A heat seeker cannot turn onto something behind it.
            lab = new Lab();
            (a, ac, ap) = lab.Ship("vanguard_fighter", Vector3.zero, Vector3.zero, 0, 30f);
            (b, bc, _) = lab.Ship("gtf_ulysses", new Vector3(0f, 0f, 300f), Vector3.zero, 1, 0f);
            a.Target = b.Id;
            ap.In.Buttons = ControlButtons.MissileFire; lab.Run(1f / 60f); ap.In.Buttons = ControlButtons.None;
            lab.Flight.Place(b.Id, new Vector3(0f, 0f, -400f), Quaternion.identity, 0f, 0f);
            lab.Run(9f);
            Check(Near(bc.Shield, bc.ShieldMax, 0.001f) && Near(bc.Hull, bc.HullMax, 0.001f), "a target that slips out of the seeker's view cone is not followed");
            lab.Dispose();
        }

        static void Locks()
        {
            var lab = new Lab();
            var (a, ac, ap) = lab.Ship("vanguard_fighter", Vector3.zero, Vector3.zero, 0);
            var (b, bc, _) = lab.Ship("gtf_ulysses", new Vector3(30f, 10f, 800f), Vector3.zero, 1);
            a.Target = b.Id;
            lab.Run(0.2f);
            Check(ac.Lock == LockPhase.Locked && ac.SelectedSlot.Weapon == "missile_heat", "the heat seeker's seek tone is up with the target ahead");
            ap.In.SelectMissilePlusOne = 2;                     // the lock-on missile
            lab.Run(1f / 60f);
            Check(ac.Lock == LockPhase.Acquiring && ac.LockProgress01 < 0.05f, $"switching to the lock-on missile does not inherit the heat seeker's tone: the lock starts from nothing ({F(ac.LockProgress01)})");
            ap.In.SelectMissilePlusOne = 0;
            Check(ac.SelectedMissile == 1 && ac.SelectedSlot.Weapon == "missile_lock", "the AI's missile select picks the lock-on weapon");
            ap.In.Buttons = ControlButtons.MissileFire; lab.Run(0.3f);
            Check(ac.Missiles[1].Ammo == 6 && ac.Lock == LockPhase.Acquiring, $"firing without a lock launches nothing, and the lock is building ({F(ac.LockProgress01)})");
            // How long to lock: count ticks from the start.
            lab.Run(1.0f);
            Check(ac.Lock == LockPhase.Acquiring && ac.LockProgress01 > 0.6f && ac.LockProgress01 < 0.8f, $"1.3 s into a 2.0 s lock ({F(ac.LockProgress01)})");
            lab.Run(0.8f);
            Check(ac.Lock == LockPhase.Locked || ac.Missiles[1].Ammo == 5, "2.1 s on target and the lock is made (or the held fire button has already launched)");
            Check(lab.Count(CombatEvents.LockAcquired) == 1, "lock_acquired is raised once");
            Check(ac.Missiles[1].Ammo == 5, "with the lock made the held fire button launches the missile");
            lab.Run(8f);
            Check(bc.Shield < bc.ShieldMax - 50f || bc.Hull < bc.HullMax, $"and it hits (shield {F(bc.Shield)}, hull {F(bc.Hull)})");
            lab.Dispose();

            // Leaving the cone drains the lock.
            lab = new Lab();
            (a, ac, ap) = lab.Ship("vanguard_fighter", Vector3.zero, Vector3.zero, 0);
            (b, bc, _) = lab.Ship("gtf_ulysses", new Vector3(0f, 0f, 800f), Vector3.zero, 1);
            a.Target = b.Id;
            ap.In.SelectMissilePlusOne = 2; lab.Run(1f / 60f); ap.In.SelectMissilePlusOne = 0;
            lab.Run(2.2f);
            Check(ac.Lock == LockPhase.Locked, "a lock is made on a target dead ahead");
            lab.Flight.Place(a.Id, Vector3.zero, Quaternion.Euler(0f, 40f, 0f), 0f, 0f);
            lab.Run(1.0f);
            Check(ac.Lock == LockPhase.None && lab.Count(CombatEvents.LockLost) == 1, "a target 40 degrees off the nose loses the lock");
            lab.Dispose();
        }

        static void Decoys()
        {
            foreach (var (weapon, chance) in new[] { ("missile_heat", 0.85f), ("missile_lock", 0.45f), ("torpedo_heavy", 0.10f) })
            {
                const int N = 400;
                var lab = new Lab(0x1234567u);
                var (v, vc, vp) = lab.Ship("gtf_ulysses", Vector3.zero, Vector3.zero, 0, 0f);
                vc.Decoys = 25;
                for (int i = 0; i < N; i++)
                {
                    // Shooters spread over a sphere 300 m round the victim, so they neither collide nor crowd.
                    float y = 1f - 2f * (i + 0.5f) / N, r = Mathf.Sqrt(1f - y * y), th = i * 2.399963f;
                    Vector3 where = new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r) * 300f;
                    var (s, sc, _) = lab.Ship("gtb_artemis", where, Vector3.zero, 1);
                    sc.Missiles.Clear();
                    sc.Missiles.Add(new MissileSlot { Weapon = weapon, Name = weapon, Ammo = 1, AmmoMax = 1 });
                    lab.W.Fire.Add(new FireRequest { Ship = s.Id, Part = -1, Weapon = weapon, Origin = Vector3.zero, AimPoint = v.Position, Target = v.Id });
                }
                lab.Run(1f / 60f);
                lab.Run(0.5f);
                vp.In.Buttons = ControlButtons.Countermeasure; lab.Run(1f / 60f); vp.In.Buttons = ControlButtons.None;
                int total = 0, fooled = 0;
                foreach (var pr in lab.Weapons.Projectiles) { if (pr.Kind != ProjectileKind.Missile) continue; total++; if (pr.Diverted) fooled++; }
                float rate = total > 0 ? fooled / (float)total : 0f;
                Check(total >= N - 1 && Near(rate, chance, 0.08f), $"{weapon}: one decoy fools {F(rate * 100f)}% of {total} missiles homing on the ship (design {F(chance * 100f)}%)");
                Check(vc.Decoys == 24, "a decoy costs one of the 25");
                lab.Dispose();
            }
        }

        static void Torpedo()
        {
            var lab = new Lab();
            var (v, vc, _) = lab.Ship("gtf_ulysses", new Vector3(0f, 0f, 1500f), new Vector3(0f, 180f, 0f), 1);
            var (s, sc, sp) = lab.Ship("gtb_artemis", new Vector3(0f, 0f, 0f), Vector3.zero, 1, 0f);
            // A friendly fighter shoots the torpedo as it approaches the (enemy) ship; here the bomber is on the enemy side, the shooter on ours.
            var (a, ac, ap) = lab.Ship("vanguard_fighter", new Vector3(150f, 0f, 380f), new Vector3(0f, 180f, 0f), 0);
            var (me, mc, _) = lab.Ship("gtf_ulysses", new Vector3(0f, 0f, 700f), new Vector3(0f, 180f, 0f), 0);
            lab.W.Fire.Add(new FireRequest { Ship = s.Id, Part = -1, Weapon = "torpedo_heavy", Origin = Vector3.zero, AimPoint = me.Position, Target = me.Id });
            lab.Run(1f / 60f);
            // Let it fly towards the ship at 700 m, then shoot it from 380 m.
            lab.Run(3f);
            Check(lab.Weapons.Projectiles.Count > 0, "the torpedo is in flight");
            ac.Energy = 80f;
            ap.In.Buttons = ControlButtons.PrimaryFire;
            var tp = FirstMissile(lab);
            if (tp != null)
            {
                // Point at the torpedo and keep pointing, firing as it closes.
                for (int i = 0; i < 400 && tp.Alive; i++)
                {
                    Vector3 dir = (tp.Pos - a.Position).normalized;
                    if (Geo.SolveIntercept(a.Position, a.Velocity, tp.Pos, tp.Vel, 485f, out Vector3 d, out _, out _)) dir = d;
                    lab.Flight.Place(a.Id, a.Position, Quaternion.LookRotation(dir, Vector3.up), 0f, 0f);
                    lab.W.Sim.StepOnce();
                }
            }
            ap.In.Buttons = ControlButtons.None;
            Check(lab.Count(CombatEvents.ShotDown) == 1, "a torpedo can be shot down by gunfire");
            Check(mc.Hull01 > 0.99f && mc.Shield01 > 0.99f, "and a torpedo that is shot down hurts nobody");
            lab.Dispose();
        }

        static Projectile FirstMissile(Lab lab)
        {
            foreach (var p in lab.Weapons.Projectiles) if (p.Kind == ProjectileKind.Missile) return p;
            return null;
        }

        static void Blast()
        {
            var lab = new Lab();
            var (a, ac, ap) = lab.Ship("vanguard_fighter", Vector3.zero, Vector3.zero, 0);
            var (t, tc, _) = lab.Ship("gtf_ulysses", new Vector3(0f, 0f, 200f), new Vector3(0f, 180f, 0f), 1, 0f, "range_turret_laser");
            var (bystander, bc, _) = lab.Ship("gtf_ulysses", new Vector3(34f, 0f, 205f), new Vector3(0f, 180f, 0f), 1);
            bc.Shield = bc.ShieldMax;
            a.Target = t.Id;
            ap.In.Buttons = ControlButtons.MissileFire; lab.Run(1f / 60f); ap.In.Buttons = ControlButtons.None;
            GameEvent hit = default; bool found = false;
            for (int i = 0; i < 60 * 6 && !found; i++)
            {
                lab.W.Sim.StepOnce();
                foreach (var e in lab.Events) if (e.Id == CombatEvents.HitHull && e.Entity == t.Id) { hit = e; found = true; break; }
            }
            Check(found, "the missile hit the emplacement's hull");
            if (found)
            {
                float d = ShipHits.DistanceToHull(bystander, bc, hit.Position, out _);
                float f = d <= 20f ? 1f : d >= 40f ? 0f : 1f - (d - 20f) / 20f;
                float expected = 45f * f * 0.8f;
                float taken = bc.ShieldMax - bc.Shield;
                Check(Near(taken, expected, 0.6f) && f > 0f && f < 1f, $"a ship {F(d)} m from the blast takes the falloff: 45 x {F(f)} x 0.8 = {F(expected)} off its shield ({F(taken)})");
            }
            lab.Dispose();
        }

        // ------------------------------------------------------------------------------------------------ collisions

        static void Collisions()
        {
            foreach (var (speed, lo, hi, dies) in new[] { (15f, 8f, 28f, false), (40f, 70f, 160f, false), (70f, 180f, 1000f, true) })
            {
                var lab = new Lab();
                var (cap, cc, _) = lab.Ship("earth_corvette", Vector3.zero, Vector3.zero, 1, 0f, null, 1f, "Warden");
                var (f, fc, fp) = lab.Ship("vanguard_fighter", new Vector3(0f, 0f, -330f), Vector3.zero, 0, speed);
                fc.Shield = fc.ShieldMax;
                fp.In.Throttle = speed / 70f;
                float hull0 = fc.Hull;
                for (int i = 0; i < 60 * 12 && fc.Alive && lab.Collision.Contacts == 0; i++) lab.W.Sim.StepOnce();
                lab.Run(0.3f);
                float lost = hull0 - fc.Hull;
                bool good = dies ? !fc.Alive : (fc.Alive && lost >= lo && lost <= hi);
                Check(good, $"a fighter hitting a capital hull at {F(speed)} m/s {(dies ? "is destroyed" : "loses " + F(lost) + " of its 180 hull (expect " + F(lo) + " to " + F(hi) + ")")}");
                Check(cc.Alive && cc.Hull01 > 0.999f, "the capital ship is not hurt");
                if (!dies)
                {
                    Check(Near(fc.Shield, fc.ShieldMax, 0.001f), "a collision goes round the shield");
                    Vector3 vAfter = fc.Ship.Velocity;
                    Check(vAfter.z < 0f || Mathf.Abs(vAfter.z) < speed, $"and it bounces off (z velocity {F(vAfter.z)})");
                }
                lab.Dispose();
            }
            // A graze: flying along the hull barely hurts.
            var gl = new Lab();
            var (g, gc, _) = gl.Ship("earth_corvette", Vector3.zero, Vector3.zero, 1, 0f, null, 1f, "Warden");
            var (gf, gfc, gfp) = gl.Ship("vanguard_fighter", new Vector3(60f, 0f, -120f), new Vector3(0f, -20f, 0f), 0, 40f);
            gfp.In.Throttle = 40f / 70f;
            float h0 = gfc.Hull;
            for (int i = 0; i < 60 * 8 && gl.Collision.Contacts == 0; i++) gl.W.Sim.StepOnce();
            gl.Run(0.4f);
            float lostGraze = h0 - gfc.Hull;
            Check(gfc.Alive && lostGraze > 3f && lostGraze < 40f, $"a glancing blow along the hull hurts but does not kill (the first contact took {F(lostGraze)} of 180)");
            gl.Dispose();
        }

        // ------------------------------------------------------------------------------------------------ targeting

        static void Targeting()
        {
            var lab = new Lab();
            var (me, mc, p) = lab.Ship("vanguard_fighter", Vector3.zero, Vector3.zero, 0);
            var (n, nc, np) = lab.Ship("gtf_ulysses", new Vector3(0f, 0f, 300f), Vector3.zero, 1, 0f, null, 1f, "Near");
            var (f, fc, fpl) = lab.Ship("gtf_myrmidon", new Vector3(0f, 0f, 900f), Vector3.zero, 1, 0f, null, 1f, "Far");
            var (w, wc, wp) = lab.Ship("gtf_ulysses", new Vector3(50f, 0f, 100f), Vector3.zero, 0, 0f, null, 1f, "Friend");
            void Tap(ControlButtons b) { p.In.Buttons = b; lab.Run(1f / 60f); p.In.Buttons = ControlButtons.None; lab.Run(1f / 60f); }
            Tap(ControlButtons.TargetNext);
            Check(me.Target == n.Id, "next target picks the nearest hostile, not the wingman");
            Tap(ControlButtons.TargetNext);
            Check(me.Target == f.Id, "pressing again moves to the next one out");
            Tap(ControlButtons.TargetNext);
            Check(me.Target == n.Id, "and wraps round");
            fpl.In.TargetShip = me.Id; lab.Run(1f / 60f); fpl.In.TargetShip = 0;
            Tap(ControlButtons.TargetAttacker);
            Check(me.Target == f.Id, "target nearest attacker picks the hostile that has you targeted");
            p.In.TargetShip = w.Id; lab.Run(1f / 60f); p.In.TargetShip = 0;
            Check(me.Target == w.Id, "a pilot can set a target directly (the AI's way)");
            Tap(ControlButtons.TargetReticle);
            Check(me.Target == n.Id, "target ahead picks the ship nearest the nose");

            // Parts: the capital's turrets, cycled nearest the nose first, then back to the whole ship.
            var (cap, cc, _) = lab.Ship("gtf_hercules", new Vector3(0f, 0f, 150f), new Vector3(0f, 180f, 0f), 1, 0f, null, 1f, "Hull");
            me.Target = cap.Id; me.TargetPart = -1;
            int presses = 0;
            var seen = new List<int>();
            do { Tap(ControlButtons.SubsystemNext); presses++; seen.Add(me.TargetPart); } while (me.TargetPart != -1 && presses < 12);
            Check(seen.Count == cc.Parts.Count + 1 && seen[seen.Count - 1] == -1, $"tapping subsystem walks all {cc.Parts.Count} parts and returns to the whole ship ({seen.Count} taps)");
            lab.Dispose();
        }

        // ------------------------------------------------------------------------------------------------ deaths

        static void Deaths()
        {
            var lab = new Lab();
            var (a, ac, ap) = lab.Ship("vanguard_fighter", Vector3.zero, Vector3.zero, 0);
            var (b, bc, _) = lab.Ship("gtf_ulysses", new Vector3(0f, 0f, 100f), new Vector3(0f, 180f, 0f), 1);
            bc.Shield = 0f; bc.Hull = 20f;
            lab.W.Damage.Add(new DamageRequest { Ship = b.Id, Attacker = a.Id, Weapon = "test", Amount = 25f, ShieldMult = 1f, HullMult = 1f, SubsystemMult = 0f, Point = b.Position, Direction = Vector3.forward, Normal = Vector3.back, Part = -1 });
            lab.Run(1f / 60f);
            Check(!bc.Alive && bc.Killer == a.Id, "zero hull kills the ship and remembers the killer");
            GameEvent ev = default; bool found = false;
            foreach (var e in lab.Events) if (e.Id == CombatEvents.ShipDestroyed && e.Entity == b.Id) { ev = e; found = true; }
            Check(found && ev.Subject == CombatEvents.SizeFighter && ev.Other == a.Id, "ship_destroyed names the size class and the killer for the juice");

            var (cap, cc, _) = lab.Ship("earth_corvette", new Vector3(0f, 0f, 2000f), Vector3.zero, 1, 0f, null, 0.001f, "Warden");
            lab.W.Damage.Add(new DamageRequest { Ship = cap.Id, Attacker = a.Id, Weapon = "test", Amount = 500f, ShieldMult = 1f, HullMult = 1f, SubsystemMult = 0f, Point = cap.Position, Direction = Vector3.forward, Normal = Vector3.back, Part = -1 });
            lab.Run(1f / 60f);
            found = false;
            foreach (var e in lab.Events) if (e.Id == CombatEvents.ShipDestroyed && e.Entity == cap.Id) { ev = e; found = true; }
            Check(found && ev.Subject == CombatEvents.SizeCorvette, "a capital hull dying raises ship_destroyed with its class, for the staged sequence");
            lab.Dispose();
        }
    }
}
