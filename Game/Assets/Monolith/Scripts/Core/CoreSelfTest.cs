using System;
using System.Collections.Generic;

namespace Monolith.Core
{
    /// <summary>
    /// Checks Core's contracts on throwaway objects (clock rate and ordering, events, pilots). The capture harness runs
    /// it at the start of every capture, so a broken foundation shows up in the same log as the pictures.
    /// </summary>
    public static class CoreSelfTest
    {
        public static bool Run()
        {
            var failures = new List<string>();
            int checks = 0;
            void Check(bool ok, string what) { checks++; if (!ok) failures.Add(what); }

            ClockOrderAndRate(Check);
            Events(Check);
            PilotSources(Check);

            if (failures.Count == 0)
            {
                MnLog.Info($"SELFTEST core OK ({checks} checks)");
                return true;
            }
            foreach (var f in failures) MnLog.Error($"SELFTEST core FAILED: {f}");
            return false;
        }

        sealed class Probe : ISimSystem
        {
            readonly string _name;
            readonly List<string> _log;
            public Probe(string name, int order, List<string> log) { _name = name; Order = order; _log = log; }
            public int Order { get; }
            public void Step(in SimFrame frame) => _log.Add(_name);
        }

        static void ClockOrderAndRate(Action<bool, string> check)
        {
            var log = new List<string>();
            var sim = new Sim();
            sim.Register(new Probe("flight", SimOrder.Flight, log));
            sim.Register(new Probe("pilots", SimOrder.Pilots, log));
            sim.Register(new Probe("hudA", SimOrder.Presentation, log));
            sim.Register(new Probe("hudB", SimOrder.Presentation, log));
            sim.StepOnce();
            check(string.Join(",", log) == "pilots,flight,hudA,hudB", $"systems run in order, then registration order (got {string.Join(",", log)})");

            // One simulated second delivered in 144 Hz frames must give 60 ticks.
            sim = new Sim();
            for (int i = 0; i < 144; i++) sim.Advance(1.0 / 144.0);
            check(Math.Abs(sim.Tick - Sim.Hz) <= 1, $"1 s of 144 Hz frames gives ~60 ticks (got {sim.Tick})");
            check(sim.Alpha >= 0f && sim.Alpha < 1f, $"alpha in [0,1) in real-time mode (got {sim.Alpha})");

            // A 30 Hz frame rate still ticks at 60.
            sim = new Sim();
            for (int i = 0; i < 30; i++) sim.Advance(1.0 / 30.0);
            check(Math.Abs(sim.Tick - Sim.Hz) <= 1, $"1 s of 30 Hz frames gives ~60 ticks (got {sim.Tick})");

            // A hitch is clamped rather than replayed.
            sim = new Sim();
            sim.Advance(10.0);
            check(sim.Tick <= (int)(Sim.MaxCatchUpSeconds * Sim.Hz) + 1, $"a 10 s hitch is clamped (got {sim.Tick} ticks)");

            // Capture mode: exactly one tick per frame whatever the frame time.
            sim = new Sim { OneTickPerFrame = true };
            sim.Advance(0.5); sim.Advance(0.001); sim.Advance(7.0);
            check(sim.Tick == 3 && sim.Alpha == 1f, $"OneTickPerFrame steps once per frame (got {sim.Tick})");

            sim = new Sim { TimeScale = 0f };
            sim.Advance(1.0);
            check(sim.Tick == 0, "TimeScale 0 freezes the clock");
        }

        static void Events(Action<bool, string> check)
        {
            int one = 0, all = 0;
            string lastSubject = null;
            var subOne = GameEvents.Subscribe("selftest_event", e => { one++; lastSubject = e.Subject; });
            var subAll = GameEvents.SubscribeAll(e => { if (e.Id == "selftest_event") all++; });
            GameEvents.Raise("selftest_event", "alpha");
            GameEvents.Raise("selftest_other");
            check(one == 1 && all == 1 && lastSubject == "alpha", $"named and catch-all subscribers each see the event once (one={one}, all={all})");
            subOne.Dispose();
            GameEvents.Raise("selftest_event");
            check(one == 1 && all == 2, "disposing a subscription stops delivery, others carry on");
            subAll.Dispose();
            GameEvents.Raise("selftest_event");
            check(all == 2, "disposing the catch-all stops delivery");
        }

        static void PilotSources(Action<bool, string> check)
        {
            var script = new ScriptedPilot(new[]
            {
                new ScriptedPilot.Key(0.5f, new ControlInput { Pitch = 1f, Throttle = 2f, Buttons = ControlButtons.PrimaryFire }),
                new ScriptedPilot.Key(1.0f, new ControlInput { Pitch = float.NaN }),
            });
            var sim = new Sim();
            var pilots = new Pilots();
            sim.Register(pilots);
            pilots.Attach(7, script);

            for (int i = 0; i < 30; i++) sim.StepOnce();
            check(pilots.Current(7).Pitch == 0f && pilots.Current(7).Buttons == ControlButtons.None, "scripted pilot is neutral before its first key");
            sim.StepOnce();   // tick 30 = 0.5 s
            var now = pilots.Current(7);
            check(now.Pitch == 1f && now.Throttle == 1f, $"scripted input arrives on its tick and is clamped (pitch {now.Pitch}, throttle {now.Throttle})");
            check(now.Pressed(pilots.Previous(7), ControlButtons.PrimaryFire), "a button is 'pressed' on the tick it first appears");
            sim.StepOnce();
            check(!pilots.Current(7).Pressed(pilots.Previous(7), ControlButtons.PrimaryFire) && pilots.Current(7).Held(ControlButtons.PrimaryFire), "a held button is not pressed again");
            for (int i = 0; i < 30; i++) sim.StepOnce();
            check(pilots.Current(7).Pitch == 0f, "NaN from a pilot source is sanitised to 0");
            check(pilots.Current(99).Pitch == 0f, "a ship with no pilot reads neutral");
        }
    }
}
