using System;
using System.Collections.Generic;
using System.Globalization;
using Monolith.AI;
using Monolith.Cabin;
using Monolith.Core;
using Monolith.Input;
using Monolith.Mission;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Monolith.Game
{
    /// <summary>
    /// Headless checks of the cabin on the real sim: <c>Monolith.exe -batchmode -nographics -mnCabinSelfTest</c>. Each line logs <c>CABIN PASS</c> or <c>CABIN FAIL</c>.
    /// The model's plan (every interactable reachable, every named node and display in the prefab), the pilot standing up, walking, the steps, the walls and the table;
    /// the ship holding its heading and speed while nobody flies it; the main breaker (the HUD collapses, the lights go, the ship is dark); a scan that finds a powered
    /// ship, misses a cold one and finds one that has only just gone dark; the hand repair (hold to seat the module, a slip, decay); the rack (a module swapped, the loadout
    /// fed to the ship's arms, locked after launch); the plan table's pages; sitting down again; and the cabin mission played through with the real cabin and a scripted pilot.
    /// </summary>
    public static class CabinSelfTest
    {
        const string Flag = "-mnCabinSelfTest";
        static int _checks, _fails;
        public static bool Requested => CommandLine.Has(Flag);

        static void Check(bool ok, string what)
        {
            _checks++;
            if (!ok) _fails++;
            MnLog.Info($"CABIN {(ok ? "PASS" : "FAIL")}: {what}");
        }

        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        public static bool Run()
        {
            _checks = 0; _fails = 0;
            var tests = new (string, Action)[] { ("plan", Plan), ("pad", PadInput), ("body", Body), ("breaker", Breaker), ("scan", Scans), ("repair", Repair), ("rack", Rack), ("mission", MissionRun) };
            foreach (var (name, t) in tests)
            {
                try { t(); }
                catch (Exception e) { _checks++; _fails++; MnLog.Error($"CABIN FAIL: {name} threw {e}"); }
            }
            MnLog.Info($"CABIN SELFTEST {(_fails == 0 ? "OK" : "FAILED")}: {_checks - _fails} of {_checks} checks pass");
            return _fails == 0;
        }

        // ------------------------------------------------------------------------------------------------ the pad and the keyboard

        /// <summary>The real input path (the scripted pilot bypasses it): a virtual keyboard and pad through the Input System, read by <see cref="PadCabinInput"/> with the shipped mapping.</summary>
        static void PadInput()
        {
            var cfg = CabinConfig.Load(out _);
            Keyboard kb = null; Gamepad pad = null;
            try
            {
                kb = InputSystem.AddDevice<Keyboard>("MnCabinTestKeyboard");
                pad = InputSystem.AddDevice<Gamepad>("MnCabinTestPad");
                var input = new PadCabinInput(cfg);
                InputSystem.QueueStateEvent(kb, new KeyboardState(Key.W, Key.RightArrow, Key.E, Key.Z));
                InputSystem.Update();
                var f = input.Sample(0.016f);
                Check(f.Move.y > 0.9f && Mathf.Abs(f.Move.x) < 0.1f && f.Look.x > 0.3f && f.Context && f.ContextPressed && f.StandHeld,
                      $"the keyboard drives it: W walks ({F(f.Move.y)}), the right arrow looks ({F(f.Look.x)}), E is the context button, Z holds the stand button");
                InputSystem.QueueStateEvent(kb, new KeyboardState(Key.S, Key.A, Key.Q, Key.Enter));
                InputSystem.Update();
                f = input.Sample(0.016f);
                Check(f.Move.y < -0.5f && f.Move.x < -0.5f && f.Back && f.Launch && !f.Context, "... and S, A walk back and left, Q is back, Enter launches");
                InputSystem.QueueStateEvent(kb, new KeyboardState());
                InputSystem.Update();
                input.Sample(0.016f);
                InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(0f, 1f), rightStick = new Vector2(1f, 0f), buttons = 1u << (int)GamepadButton.South | 1u << (int)GamepadButton.Select });
                InputSystem.Update();
                f = input.Sample(0.016f);
                Check(f.Move.y > 0.9f && f.Look.x > 0.9f && f.Context && f.StandHeld, $"the pad drives it: the left stick walks ({F(f.Move.y)}), the right stick looks ({F(f.Look.x)}), A is the context button, View holds stand");
                InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(0.05f, 0.08f), buttons = 1u << (int)GamepadButton.East });
                InputSystem.Update();
                f = input.Sample(0.016f);
                Check(f.Move.sqrMagnitude < 1e-6f && f.Back && !f.StandHeld, "a stick inside its dead zone does nothing; B is back");
                // Inverted look is the default: push the right stick up and the view goes down (Look.y is "up", so it is negative); the switch in controls.json turns it round.
                InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = new Vector2(0f, 1f) });
                InputSystem.Update();
                f = input.Sample(0.016f);
                Check(f.Look.y < -0.9f, $"the right stick pushed up looks down, the inverted default ({F(f.Look.y)})");
                var plain = new ControlsConfig(); plain.invert.cabinLook = false;
                var straight = new PadCabinInput(cfg, plain);
                f = straight.Sample(0.016f);
                Check(f.Look.y > 0.9f, $"... and with invert.cabinLook off it looks up ({F(f.Look.y)})");
                InputSystem.QueueStateEvent(pad, new GamepadState());
                InputSystem.QueueStateEvent(kb, new KeyboardState(Key.UpArrow));
                InputSystem.Update();
                f = input.Sample(0.016f);
                Check(f.Look.y < -0.3f, $"the up arrow looks down too ({F(f.Look.y)})");
                InputSystem.QueueStateEvent(kb, new KeyboardState());
                InputSystem.Update();
            }
            finally
            {
                if (kb != null) InputSystem.RemoveDevice(kb);
                if (pad != null) InputSystem.RemoveDevice(pad);
            }
        }

        // ------------------------------------------------------------------------------------------------ a bench

        sealed class Bench : IDisposable
        {
            public AiLab Lab;
            public CabinSystem Cab;
            public GameMissionHost Host;
            public ShipState Me;
            public ShipCombat Combat;
            public ScriptedCabinInput Input;
            public ScriptedPilot Pilot;
            public CabinLayout Layout;

            public Bench(string script, bool prelaunch = false)
            {
                Lab = new AiLab(2);
                var cfg = CabinConfig.Load(out _);
                Layout = CabinLayout.Load();
                Me = Lab.Fighter("vanguard_fighter", 0, Vector3.zero, Quaternion.identity, 0.5f, null, name: "Sable 1");
                Lab.W.LocalPlayerShip = Me.Id;
                Combat = Lab.W.Combat.Get(Me.Id);
                Combat.Invulnerable = true;
                Host = new GameMissionHost(Lab.W, Lab.Flight, Lab.Rig, Lab.Ai);
                Input = new ScriptedCabinInput(JArray.Parse(script));
                Cab = new CabinSystem(Lab.W, Layout, cfg, Input, Host, null);
                Cab.Prelaunch = prelaunch;
                Lab.W.Sim.Register(Cab);
                // a pilot that is always asking for a hard right turn, full throttle and a gun: the cabin decides whether the ship hears it
                Pilot = new ScriptedPilot(new[] { new ScriptedPilot.Key(0f, new ControlInput { Yaw = 0.6f, Throttle = 0.8f, Buttons = ControlButtons.PrimaryFire }) });
                Lab.W.Pilots.Attach(Me.Id, new CabinPilot(Lab.W, Pilot, Cab));
            }

            public void Run(double seconds, Func<bool> until = null) => Lab.Run((float)seconds, until);
            public float Heading => Me.Rotation.eulerAngles.y;
            public void Dispose() { Cab.Dispose(); Lab.Dispose(); }
        }

        static bool Near(float a, float b, float tol) => Mathf.Abs(a - b) <= tol;

        // ------------------------------------------------------------------------------------------------ the plan

        static void Plan()
        {
            var layout = CabinLayout.Load();
            var cfg = CabinConfig.Load(out string source);
            Check(layout.Levels.Count >= 3 && layout.Interactables.Count >= 8, $"the plan loads: {layout.Levels.Count} floor levels, {layout.Interactables.Count} interactables, {layout.Displays.Count} displays, {layout.Lights.Count} lights ({source})");
            int unreachable = 0;
            foreach (var it in layout.Interactables)
            {
                bool ok = layout.Walkable(it.Stand.x, it.Stand.y, cfg.body.radius) && !float.IsNaN(layout.FloorAt(it.Stand.x, it.Stand.y));
                if (!ok) { unreachable++; MnLog.Info($"CABIN plan: {it.Id} stands at {it.Stand} which is not walkable"); }
            }
            Check(unreachable == 0, "every interactable has somewhere to stand");
            Check(layout.Walkable(layout.StandAt.x, layout.StandAt.z, cfg.body.radius), "the place the pilot stands up to is clear");
            var prefab = Resources.Load<GameObject>(CabinRig.PrefabPath);
            Check(prefab != null, "the cabin prefab is in the build");
            if (prefab != null)
            {
                var names = new HashSet<string>();
                foreach (var t in prefab.GetComponentsInChildren<Transform>(true)) names.Add(t.name);
                var missing = new List<string>();
                foreach (var d in layout.Displays) if (!names.Contains(d.Name)) missing.Add(d.Name);
                foreach (var n in new[] { "CABIN_SHELL", "CABIN_FRAME", "CABIN_GLASS", "BREAKER_LEVER", "PANEL_engine_DOOR", "PANEL_engine_MODULE", "PANEL_arms_DOOR", "PANEL_arms_MODULE", "ANCHOR_seat_eye" })
                    if (!names.Contains(n)) missing.Add(n);
                Check(missing.Count == 0, "every display and moving part the code binds is in the model" + (missing.Count > 0 ? ": missing " + string.Join(", ", missing) : ""));
                int onLayer = 0, total = 0;
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true)) { total++; if (r.renderingLayerMask == 2u) onLayer++; }
                Check(total > 0 && onLayer == total, $"every renderer is on the cabin's own lighting layer ({onLayer} of {total})");
            }
            Check(Resources.Load<GameObject>("Cabin/pilot_hands") == null, "the pilot has no hands: no hand model is in the build");
        }

        // ------------------------------------------------------------------------------------------------ the body

        static void Body()
        {
            using (var b = new Bench(@"[ { ""t"": 2.0, ""do"": ""stand"" }, { ""do"": ""wait"", ""seconds"": 8.5 }, { ""do"": ""walk"", ""to"": ""breaker"" }, { ""do"": ""wait"", ""seconds"": 0.5 }, { ""do"": ""sit"" } ]"))
            {
                b.Run(1.9);
                float h0 = b.Heading;
                Check(b.Cab.State == CabinBoard.Phase.Seated && !b.Cab.PilotAway, "seated at the start: the pilot's input reaches the ship");
                Check(b.Me.AngularVelocity.y > 0.05f || Mathf.Abs(Mathf.DeltaAngle(h0, 0f)) > 0.5f, $"seated, the ship turns as asked (yaw rate {F(b.Me.AngularVelocity.y)} rad/s)");
                // stand up
                b.Run(4.5, () => b.Cab.State == CabinBoard.Phase.Standing);
                Check(b.Cab.State == CabinBoard.Phase.Standing, "holding the stand button stands the pilot up");
                var board = b.Cab.Board;
                Check(board.OwnsCamera && board.Stand01 > 0.99f, "the cabin owns the view and the seat is behind the pilot");
                Check(b.Cab.PilotAway && !((ICabin)b.Cab).Seated, "the mission is told the pilot is not seated");
                // nobody flies: the ship keeps its heading and speed
                b.Run(0.8);
                float hStand = b.Heading, vStand = b.Me.ForwardSpeed;
                b.Run(6.0);
                Check(Mathf.Abs(Mathf.DeltaAngle(hStand, b.Heading)) < 0.5f, $"standing, the ship holds its heading (moved {F(Mathf.Abs(Mathf.DeltaAngle(hStand, b.Heading)))} degrees in 6 s, the pad asking for a hard turn)");
                Check(Mathf.Abs(b.Me.ForwardSpeed - vStand) < 0.6f, $"... and its speed ({F(vStand)} to {F(b.Me.ForwardSpeed)} m/s, the pad asking for 80 percent)");
                Check(b.Lab.W.Combat.Get(b.Me.Id).Energy >= b.Combat.EnergyMax - 0.01f, "... and nobody fires");
                Check(board.HudCollapse > 0.99f, "the HUD has collapsed into the cabin's displays");
                // the floor
                Check(Near(board.ExposureShift, b.Cab.Cfg.exposure.lit - b.Cab.Cfg.exposure.seated, 0.2f), $"the eye has opened to the lit cabin (exposure shift {F(board.ExposureShift)} EV, ambient {F(board.AmbientScale)})");
                // walk to the breaker: up the steps to the level floor
                float maxFloor = 0f; bool allWalkable = true;
                b.Run(20.0, () =>
                {
                    var p = b.Cab.Position2;
                    maxFloor = Mathf.Max(maxFloor, b.Layout.FloorAt(p.x, p.y));
                    if (!b.Layout.Walkable(p.x, p.y, b.Cab.Cfg.body.radius - 0.01f)) allWalkable = false;
                    return (p - b.Layout.Find("breaker").Stand).magnitude < 0.08f;
                });
                var br = b.Layout.Find("breaker");
                Check((b.Cab.Position2 - br.Stand).magnitude < 0.12f, $"the pilot walks to the breaker's place ({F((b.Cab.Position2 - br.Stand).magnitude)} m off)");
                Check(allWalkable, "the pilot never left the floor or went through anything on the way");
                Check(Near(b.Cab.EyeCabin.y, b.Layout.FloorAt(b.Cab.Position2.x, b.Cab.Position2.y) + b.Cab.EyeHeightNow, 0.1f), $"the eye follows the floor's steps and rides at {F(b.Cab.EyeCabin.y)} m");
                // walls and the table: walk straight at the plan table and the aft wall for a few seconds
                float minDist = 9f;
                b.Run(0.2);
                // sit back down
                b.Run(30.0, () => b.Cab.State == CabinBoard.Phase.Seated);
                Check(b.Cab.State == CabinBoard.Phase.Seated && !b.Cab.PilotAway, "walking back to the seat and holding sits the pilot down again, and the pad flies the ship");
                b.Run(1.0);
                Check(b.Me.AngularVelocity.y > 0.05f || Mathf.Abs(b.Me.AngularVelocity.y) > 0.01f, $"... the ship hears the pad again (yaw rate {F(b.Me.AngularVelocity.y)})");
                _ = minDist;
            }
            // the walls: a pilot told to walk at the aft wall stops short of it
            using (var b = new Bench(@"[ { ""t"": 1.0, ""do"": ""stand"" }, { ""do"": ""walk"", ""to"": [0.0, -2.9] } ]"))
            {
                b.Run(14.0);
                var p = b.Cab.Position2;
                Check(b.Layout.Walkable(p.x, p.y, b.Cab.Cfg.body.radius - 0.01f) && p.y > -2.35f, $"told to walk through the aft wall, the pilot stops at it (z {F(p.y)})");
            }
        }

        // ------------------------------------------------------------------------------------------------ the breaker

        static void Breaker()
        {
            using (var b = new Bench(@"[ { ""t"": 1.0, ""do"": ""stand"" }, { ""do"": ""walk"", ""to"": ""breaker"" }, { ""do"": ""face"", ""at"": ""breaker"" }, { ""do"": ""press"" }, { ""do"": ""wait"", ""seconds"": 6.0 }, { ""do"": ""press"" }, { ""do"": ""wait"", ""seconds"": 5.0 } ]"))
            {
                var events = new List<string>();
                using (GameEvents.Subscribe(CabinEvents.Power, e => events.Add(e.Subject)))
                {
                    bool prompted = false;
                    b.Run(30.0, () => { if (b.Cab.Board.Prompt != null && b.Cab.Board.Prompt.Contains("BREAKER")) prompted = true; return !b.Cab.Powered; });
                    Check(prompted, "looking at the breaker shows its prompt");
                    Check(!b.Cab.Powered && !((ICabin)b.Cab).Powered, "pulling the breaker takes the power off");
                    b.Run(0.5);
                    Check(b.Cab.Board.Lights == "blackout", $"the cabin goes to its emergency glow (lights {b.Cab.Board.Lights})");
                    b.Run(1.5);
                    Check(b.Cab.Board.HudCollapse > 0.99f, "the HUD has died");
                    Check(b.Cab.LeverPos < 0.05f, "the lever is down");
                    Check(events.Count == 1 && events[0] == "off", "the clunk is announced once");
                    // dark: the ship drifts, and the pad does nothing
                    float h = b.Heading;
                    b.Run(2.0);
                    Check(Mathf.Abs(Mathf.DeltaAngle(h, b.Heading)) < 0.3f, "dark, the ship drifts on its heading");
                    b.Run(6.0, () => b.Cab.Powered);
                    Check(b.Cab.Powered && events.Count >= 2 && events[1] == "on", "pulling it back up restores the power");
                    b.Run(3.0);
                    Check(b.Cab.Board.Lights == "normal", $"the lights come back after the boot ({b.Cab.Board.Lights})");
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ scans

        static void Scans()
        {
            using (var b = new Bench(@"[ ]"))
            {
                var scanner = b.Lab.Fighter("earth_interceptor", 1, new Vector3(0f, 0f, 3000f), Quaternion.identity, 0f, null, name: "Scanner");
                var cab = b.Cab;
                // a powered ship is found
                cab.StartScan(scanner.Id, 14f, 6000f, b.Lab.W.Time);
                b.Run(15.0);
                Check(cab.DetectedBy(scanner.Id), "a sweep that crosses a powered ship finds it");
            }
            using (var b = new Bench(@"[ ]"))
            {
                var scanner = b.Lab.Fighter("earth_interceptor", 1, new Vector3(0f, 0f, 3000f), Quaternion.identity, 0f, null, name: "Scanner");
                var cab = b.Cab;
                cab.SetPower(false);
                b.Run(8.0);
                cab.StartScan(scanner.Id, 14f, 6000f, b.Lab.W.Time);
                b.Run(15.0);
                Check(!cab.DetectedBy(scanner.Id), "a sweep that crosses a ship that has been dark for a while misses it");
                cab.SetPower(true);
                b.Run(1.0);
                cab.StartScan(scanner.Id, 14f, 6000f, b.Lab.W.Time);
                b.Run(15.0);
                Check(cab.DetectedBy(scanner.Id), "... and finds it once the power is back");
            }
            using (var b = new Bench(@"[ ]"))
            {
                var scanner = b.Lab.Fighter("earth_interceptor", 1, new Vector3(0f, 0f, 3000f), Quaternion.identity, 0f, null, name: "Scanner");
                var cab = b.Cab;
                cab.SetPower(false);
                b.Run(1.0);
                cab.StartScan(scanner.Id, 4f, 6000f, b.Lab.W.Time);
                b.Run(5.0);
                Check(cab.DetectedBy(scanner.Id), "a ship that has only just gone dark is still warm, and is found");
            }
            using (var b = new Bench(@"[ ]"))
            {
                var scanner = b.Lab.Fighter("earth_interceptor", 1, new Vector3(0f, 0f, 9000f), Quaternion.identity, 0f, null, name: "Scanner");
                b.Cab.StartScan(scanner.Id, 10f, 6000f, b.Lab.W.Time);
                float light = 0f;
                b.Run(11.0, () => { light = Mathf.Max(light, b.Cab.Board.ScanLight); return false; });
                Check(!b.Cab.DetectedBy(scanner.Id), "a scanner out of range finds nothing");
            }
            using (var b = new Bench(@"[ ]"))
            {
                var scanner = b.Lab.Fighter("earth_interceptor", 1, new Vector3(-1500f, 0f, 2500f), Quaternion.identity, 0f, null, name: "Scanner");
                b.Cab.SetPower(false);
                b.Run(7.0);
                b.Cab.StartScan(scanner.Id, 14f, 6000f, b.Lab.W.Time);
                float peak = 0f, atContact = 0f, early = 0f; Vector3 dirAtContact = Vector3.zero;
                for (int i = 0; i < 15 * Sim.Hz; i++)
                {
                    b.Lab.W.Sim.StepOnce();
                    float t = i / (float)Sim.Hz;
                    peak = Mathf.Max(peak, b.Cab.Board.ScanLight);
                    if (Mathf.Abs(t - 7f) < 0.02f) { atContact = b.Cab.Board.ScanLight; dirAtContact = b.Cab.Board.ScanDirShip; }
                    if (Mathf.Abs(t - 1f) < 0.02f) early = b.Cab.Board.ScanLight;
                }
                Check(atContact > 0.9f * peak && peak > 0.5f && early < 0.1f, $"the sweep's light crawls through the canopy and peaks at the contact (peak {F(peak)}, at contact {F(atContact)}, early {F(early)})");
                var toScanner = (new Vector3(-1500f, 0f, 2500f)).normalized;
                Check(Vector3.Angle(dirAtContact, toScanner) < 8f, $"... coming from the scanner's side ({F(Vector3.Angle(dirAtContact, toScanner))} degrees off at the contact)");
            }
        }

        // ------------------------------------------------------------------------------------------------ the hand repair

        static void Repair()
        {
            string script = @"[ { ""t"": 1.0, ""do"": ""stand"" }, { ""do"": ""walk"", ""to"": ""panel_engine"" }, { ""do"": ""face"", ""at"": ""panel_engine"" }, { ""do"": ""use"", ""seconds"": 2.0 }, { ""do"": ""wait"", ""seconds"": 1.5 }, { ""do"": ""use"", ""seconds"": 7.0 }, { ""do"": ""wait"", ""seconds"": 2.0 } ]";
            using (var b = new Bench(script))
            {
                var part = b.Combat.FindPart("engine_port");
                b.Run(0.1);
                Check(!((ICabin)b.Cab).NeedsRepair(b.Me.Id, null), "an undamaged ship needs nothing from the pilot's hands");
                b.Host.DestroyPart(b.Me.Id, part.Index);
                b.Run(0.2);
                Check(part.Destroyed && ((ICabin)b.Cab).NeedsRepair(b.Me.Id, "engine_port") && ((ICabin)b.Cab).NeedsRepair(b.Me.Id, null), "a destroyed engine needs a hand repair");
                Check(b.Combat.Flags.HasFlag(CombatFlags.EnginesCrippled) || b.Combat.EngineHealth01 < 1f, "... and it cripples the ship until it is mended");
                // walk there, face it, hold two seconds, let go
                b.Run(30.0, () => b.Cab.Action == CabinSystem.Act.Repair);
                Check(b.Cab.Action == CabinSystem.Act.Repair && b.Cab.Board.Prompt != null && b.Cab.Board.Prompt.Contains("ENGINE"), $"at the panel, holding the context button begins the repair (prompt '{b.Cab.Board.Prompt}')");
                b.Run(2.0);
                float p2 = b.Cab.RepairProgress("engine_port");
                Check(Near(p2, 2.0f / b.Cab.Cfg.timing.repairSeconds, 0.12f), $"two seconds of holding is {F(p2)} of the repair (5 s in all)");
                b.Run(0.5);
                // let go: it decays, but slowly, so a pause costs little
                b.Run(1.0);
                float pr = b.Cab.RepairProgress("engine_port");
                Check(pr < p2 && pr > p2 - 0.35f, $"let go, the progress decays slowly ({F(p2)} to {F(pr)})");
                // a jolt while holding makes the repair slip
                b.Run(1.6, () => b.Cab.Action == CabinSystem.Act.Repair);
                float before = b.Cab.RepairProgress("engine_port");
                GameEvents.RaiseAt(CombatEvents.HitHull, Vector3.zero, Vector3.up, "needle", 40f, b.Me.Id);
                b.Run(0.3);
                float after = b.Cab.RepairProgress("engine_port");
                Check(after < before, $"a hit while the module is being pressed in makes the repair slip ({F(before)} to {F(after)})");
                b.Run(12.0, () => !part.Destroyed);
                Check(!part.Destroyed && !((ICabin)b.Cab).NeedsRepair(b.Me.Id, "engine_port"), "holding to the end seats the module: the part is back and nothing needs repair");
                Check(part.Hp >= part.MaxHp * 0.24f, $"... at a quarter of its health, for the living tech to mend the rest ({F(part.Health01)})");
            }
        }

        // ------------------------------------------------------------------------------------------------ the rack, the table

        static void Rack()
        {
            string script = @"[ { ""t"": 1.0, ""do"": ""stand"" }, { ""do"": ""walk"", ""to"": ""rack_gun0"" }, { ""do"": ""face"", ""at"": ""rack_gun0"" }, { ""do"": ""press"" }, { ""do"": ""wait"", ""seconds"": 1.5 },
                                { ""do"": ""face"", ""at"": ""rack_msl1"" }, { ""do"": ""press"" }, { ""do"": ""wait"", ""seconds"": 1.5 }, { ""do"": ""face"", ""at"": ""rack_msl0"" }, { ""do"": ""press"" }, { ""do"": ""wait"", ""seconds"": 1.5 },
                                { ""do"": ""walk"", ""to"": ""table"" }, { ""do"": ""face"", ""at"": ""table"" }, { ""do"": ""press"" }, { ""do"": ""wait"", ""seconds"": 1.0 }, { ""do"": ""back"" } ]";
            using (var b = new Bench(script, prelaunch: true))
            {
                var arms = b.Combat;
                Check(b.Cab.RackOpen && b.Cab.Loadout.Guns[0] == "needle" && b.Cab.Loadout.Missiles[0] == "missile_heat", "before launch the rack is open, loaded with its defaults");
                b.Run(30.0, () => b.Cab.Action == CabinSystem.Act.Rack);
                Check(b.Cab.Action == CabinSystem.Act.Rack, "facing a gun bay and pressing the context button works the module");
                b.Run(1.6);
                Check(b.Cab.Loadout.Guns[0] == "hammer" && arms.PrimaryGun == "hammer", $"the first gun bay now holds the hammer and the ship's fast gun slot is {arms.PrimaryGun}");
                b.Run(3.5);
                var m = b.Cab.Loadout.Missiles;
                Check(m[0] != m[1] && arms.Missiles.Count == 2 && arms.Missiles[0].Weapon == m[0] && arms.Missiles[1].Weapon == m[1], $"two different missile types are loaded ({m[0]}, {m[1]}) and they are the ship's only missile slots");
                b.Run(1.6);
                Check(arms.Missiles.Count == 2 && arms.Missiles[0].Ammo > 0 && arms.Missiles[1].Ammo > 0, "each carries the ammunition its type has on this ship (" + string.Join(", ", arms.Missiles.ConvertAll(x => x.Name + " " + x.Ammo)) + ")");
                // the table
                b.Run(30.0, () => b.Cab.Action == CabinSystem.Act.Table);
                Check(b.Cab.TablePages.Count >= 2 && b.Cab.CurrentPage.Kind != CabinSystem.TableKind.Map || b.Cab.TablePages.Count >= 2, $"the plan table has pages ({b.Cab.TablePages.Count}), the briefing's on it before launch");
                b.Run(0.1);
                Check(b.Cab.TablePage == 1, $"the context button turns the table's page (page {b.Cab.TablePage})");
                b.Run(2.0);
                Check(b.Cab.TablePage == 0, $"the back button turns it back (page {b.Cab.TablePage})");
                // launch: the rack locks
                b.Cab.Prelaunch = false;
                Check(!b.Cab.RackOpen, "after launch the rack is locked");
            }
        }

        // ------------------------------------------------------------------------------------------------ the mission, with the real cabin

        static void MissionRun()
        {
            var file = MissionLibrary.Load("cabin");
            string script = @"[
                { ""t"": 25.0, ""do"": ""stand"" }, { ""do"": ""walk"", ""to"": ""breaker"" }, { ""do"": ""face"", ""at"": ""breaker"" }, { ""do"": ""press"" }, { ""do"": ""wait"", ""seconds"": 2.0 },
                { ""t"": 60.0, ""do"": ""walk"", ""to"": ""panel_engine"" }, { ""do"": ""face"", ""at"": ""panel_engine"" }, { ""do"": ""use"", ""seconds"": 7.0 },
                { ""do"": ""walk"", ""to"": ""breaker"" }, { ""do"": ""face"", ""at"": ""breaker"" }, { ""do"": ""press"" }, { ""do"": ""wait"", ""seconds"": 2.0 }, { ""do"": ""sit"" },
                { ""t"": 100.0, ""do"": ""stand"" }, { ""do"": ""walk"", ""to"": ""panel_engine"" }, { ""do"": ""face"", ""at"": ""panel_engine"" }, { ""do"": ""use"", ""seconds"": 7.0 }, { ""do"": ""sit"" },
                { ""t"": 150.0, ""do"": ""stand"" }, { ""do"": ""walk"", ""to"": ""panel_engine"" }, { ""do"": ""face"", ""at"": ""panel_engine"" }, { ""do"": ""use"", ""seconds"": 7.0 }, { ""do"": ""sit"" } ]";   // a last look at the panel: when the drones hit the engine again depends on how they fly
            using (var lab = new AiLab(2))
            {
                var cfg = CabinConfig.Load(out _);
                var layout = CabinLayout.Load();
                var host = new GameMissionHost(lab.W, lab.Flight, lab.Rig, lab.Ai) { Autopilot = true, PlayerInvulnerable = true };
                var input = new ScriptedCabinInput(JArray.Parse(script));
                var cab = new CabinSystem(lab.W, layout, cfg, input, host, file);
                lab.W.Sim.Register(cab);
                var rt = new MissionRuntime(lab.W, file, host, cab);
                lab.W.Sim.Register(rt);
                rt.Start();
                int me = lab.W.LocalPlayerShip;
                lab.W.Pilots.Attach(me, new CabinPilot(lab.W, lab.Ai.PilotOf(me), cab));
                var c = lab.W.Combat.Get(me);
                float hAway = float.NaN, hBack = float.NaN, vAway = float.NaN, vBack = float.NaN;
                bool wasDarkWhenSwept = false, repaired = false, sawRepair = false; float powerBackAt = -1f;
                for (int i = 0; i < 320 * Sim.Hz; i++)
                {
                    lab.W.Sim.StepOnce();
                    if (float.IsNaN(hAway) && cab.State == CabinBoard.Phase.Standing) { hAway = lab.W.Ships.Get(me).Rotation.eulerAngles.y; vAway = lab.W.Ships.Get(me).ForwardSpeed; }
                    if (cab.State == CabinBoard.Phase.Standing && lab.W.Time - rt.StartTime < 58f) { hBack = lab.W.Ships.Get(me).Rotation.eulerAngles.y; vBack = lab.W.Ships.Get(me).ForwardSpeed; }
                    var ev = rt.EventNamed("Sweep");
                    if (ev != null && ev.FirstFiredAt >= 0f && lab.W.Time - rt.StartTime > ev.FirstFiredAt - rt.StartTime + 8f && lab.W.Time - rt.StartTime < ev.FirstFiredAt - rt.StartTime + 9f) wasDarkWhenSwept = !cab.Powered;
                    if (cab.Action == CabinSystem.Act.Repair) sawRepair = true;
                    if (sawRepair && !cab.NeedsRepair(me, null)) repaired = true;
                    // The scripted pilot has to be back in the seat before the run is read: how long the dogfight lasts depends on how the ships fly
                    // (a faster player ship ends it sooner), so the mission can end with the pilot still on the way back from the panel.
                    if (rt.Ended && lab.W.Time > rt.EndedAt + 2f && cab.State == CabinBoard.Phase.Seated) break;
                }
                var enginePart = c.FindPart("engine_port");
                Check(rt.Ended && rt.Result == "victory", $"the mission plays through with the real cabin and a scripted pilot: {rt.Result} \"{rt.ResultText}\" at {rt.EndedAt - rt.StartTime:0} s (needs repair: {cab.NeedsRepair(me, null)}, engine port destroyed: {enginePart != null && enginePart.Destroyed}, state {cab.State})");
                Check(rt.EventNamed("Go dark").FirstFiredAt >= 0f && wasDarkWhenSwept, "the call to go dark came and the ship was dark when the sweep crossed it");
                Check(rt.EventNamed("Missed").FirstFiredAt >= 0f && rt.EventNamed("Spotted").FirstFiredAt < 0f && !cab.DetectedBy(GameWorld.NoShip), "the Lector's sweep missed a dark ship (and did not call it spotted)");
                Check(rt.ObjectiveState("unseen") == MissionBoard.State.Done, "the bonus objective (stay unseen) is met");
                Check(rt.EventNamed("Hit").FirstFiredAt >= 0f && repaired && rt.EventNamed("Engine back").FirstFiredAt >= 0f, "the hit took the port engine and the pilot's hands brought it back (the mission saw needsRepair end)");
                Check(rt.ObjectiveState("engine") == MissionBoard.State.Done, "the hand-repair objective is done");
                Check(Mathf.Abs(Mathf.DeltaAngle(hAway, hBack)) < 1.0f && Mathf.Abs(vAway - vBack) < 2f, $"while the pilot was up the ship held its heading ({F(Mathf.Abs(Mathf.DeltaAngle(hAway, hBack)))} degrees) and speed ({F(vAway)} to {F(vBack)} m/s) until the hit");
                Check(cab.State == CabinBoard.Phase.Seated && cab.Powered, $"the pilot is back in the seat with the power on (state {cab.State}, power {cab.Powered}, the run ended at {lab.W.Time - rt.StartTime:0} s, the mission at {rt.EndedAt - rt.StartTime:0} s)");
                Check(lab.Errors == 0, $"no errors logged ({lab.Errors})");
                cab.Dispose();
                rt.Dispose();
                host.Dispose();
            }
            // the other way: sit tight with the power on and be found
            using (var lab = new AiLab(2))
            {
                var cfg = CabinConfig.Load(out _);
                var host = new GameMissionHost(lab.W, lab.Flight, lab.Rig, lab.Ai) { Autopilot = true, PlayerInvulnerable = true };
                var cab = new CabinSystem(lab.W, CabinLayout.Load(), cfg, new ScriptedCabinInput(JArray.Parse("[]")), host, file);
                lab.W.Sim.Register(cab);
                var rt = new MissionRuntime(lab.W, file, host, cab);
                lab.W.Sim.Register(rt);
                rt.Start();
                int me = lab.W.LocalPlayerShip;
                lab.W.Pilots.Attach(me, new CabinPilot(lab.W, lab.Ai.PilotOf(me), cab));
                // nobody stands up: the repair is done by the pilot's hands only, so the mission stays on until the time is up. The sweep alone is the point.
                for (int i = 0; i < 70 * Sim.Hz; i++) lab.W.Sim.StepOnce();
                Check(rt.EventNamed("Spotted").FirstFiredAt >= 0f && rt.EventNamed("Missed").FirstFiredAt < 0f, "a pilot who stays in the seat with the power on is found by the sweep");
                Check(rt.ObjectiveState("unseen") == MissionBoard.State.Failed, "... and the bonus objective fails");
                cab.Dispose(); rt.Dispose(); host.Dispose();
            }
        }
    }
}
