using System;
using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Flight
{
    /// <summary>
    /// Checks the parts of the flight model the FS2 scenarios do not reach: knocks loosening the damping, match speed,
    /// afterburner refusals, and that input signs follow Core's convention. Run by the scenario runner (its result is in
    /// the same log as the traces). Uses the GTF Ulysses' numbers so the expectations are stable.
    /// </summary>
    public static class FlightSelfTest
    {
        public static bool Run()
        {
            var failures = new List<string>();
            int checks = 0;
            void Check(bool ok, string what) { checks++; if (!ok) failures.Add(what); }

            var cls = ShipCatalog.Load("gtf_ulysses");
            var rules = FlightRules.Default;
            const float dt = Sim.Dt;

            // Signs: Core's Pitch +1 is nose up, Yaw +1 nose right, Roll +1 roll right.
            var m = Fresh(cls, rules, 0f);
            Hold(m, new ControlInput { Pitch = 1f }, 30);
            Check((m.Rotation * Vector3.forward).y > 0.1f, "pitch +1 raises the nose");
            m = Fresh(cls, rules, 0f);
            Hold(m, new ControlInput { Yaw = 1f }, 30);
            Check((m.Rotation * Vector3.forward).x > 0.1f, "yaw +1 turns the nose right");
            Check((m.Rotation * Vector3.right).y < -0.02f, "yaw +1 banks into the turn (right wing down)");
            m = Fresh(cls, rules, 0f);
            Hold(m, new ControlInput { Roll = 1f }, 30);
            Check((m.Rotation * Vector3.right).y < -0.1f, "roll +1 rolls right (right wing down)");

            // Knock: a 20 m/s sideways shove decays far more slowly than normal slip, then damping returns to normal.
            m = Fresh(cls, rules, cls.TopSpeed);
            m.ApplyKnock(new ShipKnock { Impulse = Vector3.right * (cls.Mass * 20f), Point = Vector3.zero });
            Check(Mathf.Abs(m.LocalVelocity.x - 20f) < 0.01f, $"knock adds impulse / mass (vx {m.LocalVelocity.x:0.00})");
            Check(m.KnockFraction > 0.6f && m.KnockFraction < 0.7f, $"a 20 m/s knock loosens damping for 2/3 of the knock time (got {m.KnockFraction:0.00})");
            float normalAfter = 20f * Mathf.Exp(-0.2f / cls.SideDamp);
            Hold(m, new ControlInput { Throttle = 1f }, 12);
            Check(m.LocalVelocity.x > normalAfter * 3f, $"sideways shove lingers after a knock (vx {m.LocalVelocity.x:0.00} after 0.2 s vs {normalAfter:0.00} unknocked)");
            Hold(m, new ControlInput { Throttle = 1f }, 180);
            Check(m.KnockFraction == 0f && Mathf.Abs(m.LocalVelocity.x) < 0.05f, $"knock wears off and slip is damped again (vx {m.LocalVelocity.x:0.000})");
            Check(Mathf.Abs(m.LocalVelocity.z - cls.TopSpeed) < 0.5f, $"engines pull back to set speed after a knock (vz {m.LocalVelocity.z:0.0})");
            m = Fresh(cls, rules, cls.TopSpeed);
            m.ApplyKnock(new ShipKnock { Impulse = Vector3.right * (cls.Mass * 10f), Point = new Vector3(0f, 0f, 5f) });
            Check(m.Spin.y > 0.05f, $"an off-centre knock at the nose pushed right yaws the nose right (spin y {m.Spin.y:0.000})");

            // Match speed: pressing it with a target sets the throttle to the target's speed; moving the throttle cancels it.
            m = Fresh(cls, rules, 0f);
            var idle = new ControlInput { Throttle = 0.2f };
            m.Step(idle, idle, 35f, dt);
            var press = new ControlInput { Throttle = 0.2f, Buttons = ControlButtons.MatchSpeed };
            m.Step(press, idle, 35f, dt);
            Check(m.Matching && Mathf.Abs(m.Throttle - 0.5f) < 1e-4f, $"match speed sets throttle to target speed / top speed (got {m.Throttle:0.000})");
            var follow = new ControlInput { Throttle = m.Throttle };
            for (int i = 0; i < 600; i++) { var prev = follow; follow.Throttle = m.Throttle; m.Step(follow, prev, 35f, dt); }
            Check(m.Matching && Mathf.Abs(m.LocalVelocity.z - 35f) < 0.5f, $"matched speed is reached and held (vz {m.LocalVelocity.z:0.0})");
            var moved = new ControlInput { Throttle = m.Throttle + 0.05f };
            m.Step(moved, follow, 35f, dt);
            Check(!m.Matching, "moving the throttle cancels match speed");
            m.Step(press, moved, float.NaN, dt);
            Check(!m.Matching, "match speed needs a target");

            // Afterburner: refused under the minimum fuel, and again within the restart lockout.
            m = Fresh(cls, rules, cls.TopSpeed);
            m.Fuel = rules.AfterburnerMinFuel - 1f;
            var ab = new ControlInput { Throttle = 1f, Buttons = ControlButtons.Afterburner };
            var off = new ControlInput { Throttle = 1f };
            m.Step(ab, off, float.NaN, dt);
            Check(!m.AfterburnerOn, "afterburner refuses to light below the minimum fuel");
            m = Fresh(cls, rules, cls.TopSpeed);
            m.Step(ab, off, float.NaN, dt);
            Check(m.AfterburnerOn, "afterburner lights on the key press");
            m.Step(off, ab, float.NaN, dt);
            m.Step(ab, off, float.NaN, dt);
            Check(!m.AfterburnerOn, "afterburner cannot restart within the 1.3 s lockout");

            // The brake (the player's fighter has retro thrusters, the FS2 classes do not): much quicker than the ordinary slow-down, to a full stop, with the burner shut off.
            var van = ShipCatalog.Load("vanguard_fighter");
            var brake = new ControlInput { Throttle = 1f, Buttons = ControlButtons.Brake };
            var cruise = new ControlInput { Throttle = 1f };
            m = Fresh(van, rules, van.TopSpeed);
            Hold(m, brake, 78);                                                          // 1.3 s
            Check(m.LocalVelocity.z < 3.5f, $"the brake takes the fighter from {van.TopSpeed:0} m/s to under 3.5 m/s in 1.3 s (vz {m.LocalVelocity.z:0.0})");
            Hold(m, brake, 42);                                                          // 2.0 s in all
            Check(m.LocalVelocity.z == 0f && !m.Braking && m.BrakeHeld && m.Throttle == 0f, $"... and stops it dead, with the throttle setting at 0 (vz {m.LocalVelocity.z:0.00})");
            m = Fresh(van, rules, van.TopSpeed);
            Hold(m, new ControlInput { Throttle = 0f }, 78);
            Check(m.LocalVelocity.z > 20f, $"without the brake a throttle cut is far slower (vz {m.LocalVelocity.z:0.0} after 1.3 s)");
            m = Fresh(van, rules, van.TopSpeed);
            m.Step(brake, cruise, float.NaN, dt);
            Check(m.Braking, "the retros are firing while it brakes at speed");
            m = Fresh(van, rules, van.TopSpeed);
            m.Step(ab, off, float.NaN, dt);
            Hold(m, ab, 120);
            Check(m.AfterburnerOn && m.LocalVelocity.z > van.AfterburnerSpeed * 0.9f, $"the burner takes the fighter to {m.LocalVelocity.z:0} m/s");
            Hold(m, brake, 150);                                                         // 2.5 s
            Check(!m.AfterburnerOn && m.LocalVelocity.z < 1f, $"the brake shuts the burner off and stops it from burner speed in 2.5 s (vz {m.LocalVelocity.z:0.0})");
            Hold(m, cruise, 300);
            Check(m.LocalVelocity.z > van.TopSpeed * 0.9f && !m.BrakeHeld, $"released, the ship accelerates again (vz {m.LocalVelocity.z:0.0})");
            m = Fresh(van, rules, van.TopSpeed);
            Hold(m, new ControlInput { Throttle = 1f, Buttons = ControlButtons.Brake | ControlButtons.Afterburner }, 150);
            Check(!m.AfterburnerOn && m.LocalVelocity.z < 1f, $"the brake overrules a held afterburner key (vz {m.LocalVelocity.z:0.0}, burner {m.AfterburnerOn})");
            m = Fresh(cls, rules, cls.TopSpeed);
            Hold(m, brake, 78);
            Check(m.LocalVelocity.z > 20f, $"a class without retro thrusters brakes on its ordinary slow-down (vz {m.LocalVelocity.z:0.0} after 1.3 s)");
            m = Fresh(van, rules, 0f);
            m.Step(new ControlInput { Throttle = 0.2f }, idle, 35f, dt);
            m.Step(new ControlInput { Throttle = 0.2f, Buttons = ControlButtons.MatchSpeed }, idle, 35f, dt);
            Check(m.Matching, "match speed holds before the brake");
            m.Step(brake, idle, 35f, dt);
            Check(!m.Matching, "the brake lets go of match speed");

            if (failures.Count == 0)
            {
                MnLog.Info($"SELFTEST flight OK ({checks} checks)");
                return true;
            }
            foreach (var f in failures) MnLog.Error($"SELFTEST flight FAILED: {f}");
            return false;
        }

        static ShipFlight Fresh(ShipClass cls, FlightRules rules, float speed)
        {
            var m = new ShipFlight(cls, rules, 1);
            m.Place(Vector3.zero, Quaternion.identity, speed, speed / cls.TopSpeed);
            return m;
        }

        static void Hold(ShipFlight m, ControlInput input, int ticks)
        {
            for (int i = 0; i < ticks; i++) m.Step(input, input, float.NaN, Sim.Dt);
        }
    }
}
