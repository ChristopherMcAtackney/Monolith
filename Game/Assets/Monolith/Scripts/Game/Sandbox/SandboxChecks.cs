using System;
using System.Collections;
using System.IO;
using Monolith.Core;
using Monolith.Juice;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Monolith.Game
{
    /// <summary>
    /// Checks the juice sandbox can run on its own, from the player's command line (the builder cannot hold the pad):
    ///   -mnJuiceSelfTest        settings save and reload, preset maths, trauma maths, the sounds' levels; logs "JUICE selftest ...", then quits.
    ///   -mnPanelShot file.png [-mnPanelPage 0|1|2]   runs the sandbox, opens the panel, saves a screenshot with the panel on it, quits.
    ///   -mnRumbleTest           pulses both pad motors for a third of a second and logs the device and what it supports, then quits.
    /// </summary>
    public sealed class SandboxChecks : MonoBehaviour
    {
        JuiceSandbox _box;
        JuicePanel _panel;
        /// <summary>The exit code the run ends with (a failed check in the middle must not be lost when the last line quits with 0).</summary>
        int _exit;
        /// <summary>The longest frame since it was last zeroed (the death check reports it: a hitch is what the old waits could not stand), and the longest in the first second after the hit (<see cref="_hitAt"/>).</summary>
        float _worstFrame, _worstFirstSecond, _hitAt = -1f;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _worstFrame = Mathf.Max(_worstFrame, dt);
            if (_hitAt >= 0f && Time.realtimeSinceStartup - _hitAt < 1f) _worstFirstSecond = Mathf.Max(_worstFirstSecond, dt);
        }

        public static void Attach(JuiceSandbox box, JuicePanel panel, Transform parent)
        {
            if (!CommandLine.Has("-mnJuiceSelfTest") && !CommandLine.Has("-mnPanelShot") && !CommandLine.Has("-mnRumbleTest") && !CommandLine.Has("-mnPanelDrive")) return;
            var c = new GameObject("SandboxChecks").AddComponent<SandboxChecks>();
            c.transform.SetParent(parent, false);
            c._box = box; c._panel = panel;
            c.StartCoroutine(c.Run());
        }

        IEnumerator Run()
        {
            yield return new WaitForSeconds(1.0f);
            if (CommandLine.Has("-mnJuiceSelfTest")) SelfTest();
            if (CommandLine.Has("-mnJuiceSelfTest")) yield return BoostCheck();
            if (CommandLine.Has("-mnJuiceSelfTest")) yield return BrakeCheck();
            if (CommandLine.Has("-mnJuiceSelfTest")) yield return DeathCheck();
            if (CommandLine.Has("-mnRumbleTest")) yield return RumbleTest();
            if (CommandLine.Has("-mnPanelDrive")) yield return PanelDrive();
            if (CommandLine.Has("-mnPanelShot"))
            {
                string path = Path.GetFullPath(CommandLine.Get("-mnPanelShot"));
                _box.Combat.Battle = true;
                yield return new WaitForSeconds(1.5f);
                _box.Trigger("flak");
                _box.Trigger("hull_hit");
                if (_panel != null)
                {
                    _panel.SetVisible(true);
                    _panel.GoToPage(CommandLine.GetInt("-mnPanelPage", 0));
                }
                yield return new WaitForSeconds(0.45f);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(path);
                yield return new WaitForSeconds(1.0f);
                MnLog.Info($"PANELSHOT {path}");
            }
            Application.Quit(_exit);
        }

        void SelfTest()
        {
            int fails = 0;
            void Check(bool ok, string what) { if (!ok) { fails++; MnLog.Error("JUICE selftest FAILED: " + what); } }

            // Settings: save, reload, compare.
            string tmp = Path.Combine(Path.GetTempPath(), "mn_juice_selftest.json");
            var s = new JuiceSettings();
            s.ApplyPreset("High");
            s.effects.bolts = false; s.levels.shake = 1.7f; s.levels.beamWidth = 2.3f; s.hitStop.enabled = true; s.slowMo = 0.4f;
            s.boost.enabled = false; s.boost.fovHold = 20f; s.boost.lens = 0.4f;
            File.WriteAllText(tmp, s.ToJson());
            var r = JuiceSettings.Parse(File.ReadAllText(tmp), tmp);
            Check(!r.effects.bolts && r.effects.beams, "switches round-trip");
            Check(Mathf.Approximately(r.levels.shake, 1.7f) && Mathf.Approximately(r.levels.beamWidth, 2.3f), "levels round-trip");
            Check(r.hitStop.enabled && Mathf.Approximately(r.slowMo, 0.4f), "hit-stop and slow-mo round-trip");
            Check(!r.boost.enabled && Mathf.Approximately(r.boost.fovHold, 20f) && Mathf.Approximately(r.boost.lens, 0.4f), "boost settings round-trip");
            Check(JuiceSettings.Parse("{ \"format\": \"monolith-juice/1\" }", "old file").boost.fovHold == 12f, "a juice.json without a boost section gets the boost defaults");
            Check(r.DetectPreset() == "Custom", "a hand-tuned set is Custom");
            r.ApplyPreset("Medium");
            Check(r.DetectPreset() == "Medium" && r.levels.shake == 1f, "Medium preset");
            var d = new JuiceSettings();
            Check(d.preset == "Medium" && d.levels.shake == 1f && !d.hitStop.enabled, "defaults are Medium with hit-stop off");
            Check(JuiceSettings.Parse(Resources.Load<TextAsset>(JuiceSettings.ResourcePath).text, "defaults").DetectPreset() == "Medium", "shipped defaults are Medium");
            Check(d.boost.enabled && d.boost.intensity == 1f && d.boost.fovHold == 12f && d.boost.fovPunch == 8f, "boost defaults: on, 12 degrees held, 8 thrown");
            Check(JuiceSettings.Parse(Resources.Load<TextAsset>(JuiceSettings.ResourcePath).text, "defaults").boost.fovHold == 12f, "shipped defaults carry the boost section");
            File.Delete(tmp);

            // Trauma: shake grows with the square, then fades.
            var t = new Trauma();
            t.Add(0.5f);
            Check(Mathf.Abs(t.Shake - 0.25f) < 1e-5f, "shake is trauma squared");
            t.Add(0.9f);
            Check(t.Value == 1f, "trauma caps at 1");
            for (int i = 0; i < 200; i++) t.Tick(1f / 60f);
            Check(t.Value == 0f, "trauma fades to nothing");
            s.effects.cameraShake = false;
            Check(s.ShakeScale == 0f, "the shake off switch zeroes the shake");

            // Hit-stop: a short freeze that lets go by itself.
            var hs = new HitStop();
            hs.Trigger(70f, 0.05f);
            Check(Mathf.Approximately(hs.Factor, 0.05f), "hit-stop slows the game");
            hs.Update(0.1f);
            Check(Mathf.Approximately(hs.Factor, 1f), "hit-stop lets go");

            MnLog.Info($"JUICE selftest {(fails == 0 ? "OK" : "FAILED (" + fails + ")")}: settings, presets, trauma, hit-stop");
            if (fails > 0) Application.Quit(1);
        }

        /// <summary>
        /// The boost, on the running sandbox: the button lights the burner, the view widens and the dust stretches, the speed rises, it all eases back
        /// on release, the off switch really turns the treatment off, and running the tank dry makes the sputter. About 15 s.
        /// </summary>
        IEnumerator BoostCheck()
        {
            int fails = 0;
            void Check(bool ok, string what) { if (!ok) { fails++; MnLog.Error("JUICE boostcheck FAILED: " + what); } }
            var world = GameWorld.Current;
            var juice = _box.Juice;
            var fx = juice.Boost;
            var me = world.Ships.Get(world.LocalPlayerShip);
            var s = juice.Settings.boost;
            Check(Mathf.Abs(BoostFx.PunchEnvelope(0f)) < 1e-5f && BoostFx.PunchEnvelope(0.07f) > 0.99f && BoostFx.PunchEnvelope(2.5f) < 0.01f, "the ignition kick rises in a few frames, then settles");
            Check(me != null && me.FuelMax > 0f, "the player's ship has an afterburner");
            yield return Both(0.6f);
            Check(world.FovKick == 0f && world.DustStretch == 1f && world.DustBright == 1f, "no boost, no kick");
            float cruise = me.ForwardSpeed;

            // The button lights the burner; everything shows.
            int ign0 = fx.Ignitions;
            float simT0 = world.Time;
            _box.Trigger("boost");
            yield return Until(() => fx.Ignitions == ign0 + 1, 5f);          // the effects see the ignition when they next draw; 0.8 s of the effects' clock is counted from there
            yield return FxTo(juice.Context.Time + 0.8f); yield return SimTo(simT0 + 0.8f);
            Check(me.AfterburnerOn && fx.Burning && fx.Ignitions == ign0 + 1, "the boost button lights the afterburner (one ignition)");
            Check(world.FovKick > s.fovHold * 0.8f, $"the view widens while it burns (+{world.FovKick:0.0} degrees)");
            Check(world.DustStretch > 1.3f && world.DustBright > 1.1f, $"the dust is drawn longer and brighter ({world.DustStretch:0.00}x, {world.DustBright:0.00}x)");
            Check(juice.Context.Frame.BoostBlur > 0.004f && juice.Context.Frame.BoostActive, "the lens is on");
            Check(me.ForwardSpeed > cruise + 0.5f * (me.AfterburnerSpeed - me.TopSpeed), $"the ship speeds up ({cruise:0} to {me.ForwardSpeed:0} m/s)");
            Check(me.AfterburnerSpeed >= 3f * me.TopSpeed, $"the player's burner speed is at least 3x cruise ({me.TopSpeed:0} to {me.AfterburnerSpeed:0} m/s)");

            // Released after 4 s: the burner is out and the view eases back, not snaps.
            // The burner's 4 s are the sim's; the view eases back on the effects' clock from the moment the effects draw the burner out (under load the effects' clock lags the sim's, and "0.12 s after the
            // release" counted from the sim's 4 s fell a second after the effects' own release: the view had eased back already, +0.2 degrees).
            yield return SimTo(simT0 + 4.2f);
            Check(!me.AfterburnerOn, "the burner is out after the 4 s");
            yield return Until(() => !fx.Burning, 5f);
            float outFx = juice.Context.Time;
            yield return FxTo(outFx + 0.12f);
            Check(world.FovKick > 0.3f, $"the view is still easing back a moment after release (+{world.FovKick:0.0} degrees)");
            yield return FxTo(outFx + 1.92f);
            Check(world.FovKick < 0.2f && world.DustStretch < 1.05f && !juice.Context.Frame.BoostActive, $"it has eased all the way back (+{world.FovKick:0.00} degrees, dust {world.DustStretch:0.00}x)");

            // The off switch: the speed stays, the treatment goes.
            s.enabled = false;
            yield return Both(0.4f);
            _box.Trigger("boost");
            yield return Both(0.6f);
            Check(me.AfterburnerOn && world.FovKick == 0f && world.DustStretch == 1f && !juice.Context.Frame.BoostActive, "with the boost juice off the afterburner still burns, with nothing added");
            s.enabled = true;
            yield return Both(3.6f);

            // Run the tank dry: the burner drops with the key down and the sputter plays. Through frames of two ticks each (30 fps): below 60 fps the frame that shows the burner going
            // out has run a tick of refilling since (the old test of "the tank ran out" read the fuel level in that frame and missed it, so the sputter never played; headless frames are
            // shorter than a tick, which is why this passed when nobody throttled the frame rate).
            int fpsWas = Application.targetFrameRate, vsyncWas = QualitySettings.vSyncCount;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 30;
            int sp0 = fx.Sputters;
            _box.Trigger("boost_dry");
            float waited = 0f;
            while (fx.Sputters == sp0 && waited < 12f) { waited += 0.25f; yield return Both(0.25f); }
            Check(fx.Sputters > sp0, $"running the tank dry makes the sputter, through frames of two ticks (after {waited:0.0} s)");
            Application.targetFrameRate = fpsWas; QualitySettings.vSyncCount = vsyncWas;
            yield return Both(0.3f);
            Check(!me.AfterburnerOn && me.Fuel < 40f, $"the burner is out on an empty tank (fuel {me.Fuel:0})");

            MnLog.Info($"JUICE boostcheck {(fails == 0 ? "OK" : "FAILED (" + fails + ")")}: ignition, view, dust, lens, release, off switch, dry tank");
            if (fails > 0) Application.Quit(1);
        }

        /// <summary>
        /// The retro brake, on the running sandbox: the button brakes the ship to a dead stop in about a second and a half, the view narrows and lurches,
        /// it all eases back, letting go sends the ship off again, and the off switch leaves the stop with nothing added. About 13 s.
        /// </summary>
        IEnumerator BrakeCheck()
        {
            int fails = 0;
            void Check(bool ok, string what) { if (!ok) { fails++; MnLog.Error("JUICE brakecheck FAILED: " + what); } }
            var world = GameWorld.Current;
            var juice = _box.Juice;
            var fx = juice.Brake;
            var me = world.Ships.Get(world.LocalPlayerShip);
            var s = juice.Settings.brake;
            yield return Both(2.5f);                      // the last check's burner is out and the view has eased back
            float cruise = me.ForwardSpeed;
            Check(cruise > 20f && !me.BrakeOn, $"the ship is flying (cruise {cruise:0} m/s) before the brake");
            Check(Mathf.Abs(world.FovKick) < 0.05f && fx.Lurch == 0f, "no brake, no kick and no lurch");

            // The brake holds for 3 s of the sim's clock and its view eases on the effects' clock; under load the two drift apart (a long frame costs the effects more than the sim), so each moment is
            // waited for on the clock its condition is about: the stop and the hold on the sim's, the lurch and the view on the effects'.
            int b0 = fx.Brakes;
            float sT = world.Time;
            _box.Trigger("brake");
            yield return Until(() => fx.Brakes == b0 + 1, 5f);                // the click is felt when the effects next draw it: its age is counted from there (a long frame between the
            float fT = juice.Context.Time;                                    // press and the draw used to put "a tenth of a second in" before the click: 0 cm)
            // The lurch is a spring that peaks within a tenth of a second: a single sample "a tenth of a second in" lands on a different part of it at every frame rate (at 15 fps the frames are
            // 66 ms apart and the second one is at 133 ms, past the peak), so the check takes the peak of every frame in the first 0.3 s, against the same two thresholds.
            float lurch = 0f, nod = 0f, peakAt = -1f, r0 = Time.realtimeSinceStartup;
            while (juice.Context.Time < fT + 0.3f && Time.realtimeSinceStartup - r0 < 10f)
            {
                if (juice.Context.LurchShift.z > lurch) { lurch = juice.Context.LurchShift.z; peakAt = juice.Context.Time - fT; }
                nod = Mathf.Max(nod, juice.Context.LurchNodDeg);
                yield return null;
            }
            Check(lurch > 0.05f && nod > 0.4f && peakAt < 0.25f, $"the view lurches forward and nods (peak {lurch * 100f:0} cm at {peakAt:0.00} s, {nod:0.0} degrees, in the first 0.3 s)");
            yield return SimTo(sT + 0.3f); yield return FxTo(fT + 0.3f);
            Check(me.BrakeOn && fx.Brakes == b0 + 1, "the brake button brakes (one hard stop)");
            Check(world.FovKick < -s.fovHold * 0.6f, $"the view narrows ({world.FovKick:0.0} degrees)");
            Check(me.ForwardSpeed < cruise * 0.5f, $"the ship is slowing hard ({cruise:0} to {me.ForwardSpeed:0} m/s in 0.3 s)");
            yield return SimTo(sT + 2.0f);
            Check(me.BrakeOn && !me.Braking && me.ForwardSpeed < 0.5f, $"two seconds in it is stopped and the brake is still on (speed {me.ForwardSpeed:0.0})");
            yield return FxTo(fT + 2.0f);
            Check(Mathf.Abs(world.FovKick) < 0.8f && Mathf.Abs(juice.Context.LurchShift.z) < 0.02f, $"the view has eased back ({world.FovKick:0.0} degrees, {juice.Context.LurchShift.z * 100f:0} cm)");
            yield return SimTo(sT + 3.8f);
            Check(!me.BrakeOn, "the brake lets go after its 3 s");
            yield return SimTo(sT + 5.3f);
            Check(me.ForwardSpeed > me.TopSpeed * 0.5f, $"letting go sends the ship off again ({me.ForwardSpeed:0} m/s)");
            yield return Both(2.0f);

            // The off switch: the ship still stops, the treatment goes.
            s.enabled = false;
            int b1 = fx.Brakes;
            _box.Trigger("brake");
            yield return Both(0.4f);
            Check(me.BrakeOn && fx.Brakes == b1 && world.FovKick == 0f && juice.Context.LurchShift == Vector3.zero, "with the brake juice off the ship still brakes, with nothing added");
            Check(me.ForwardSpeed < cruise * 0.5f, "... and it is still stopping hard");
            s.enabled = true;
            yield return Both(3.4f);

            MnLog.Info($"JUICE brakecheck {(fails == 0 ? "OK" : "FAILED (" + fails + ")")}: stop, view, lurch, release, off switch");
            if (fails > 0) Application.Quit(1);
        }

        /// <summary>
        /// Wait until BOTH clocks the checks are about have moved on by this many seconds: the sim's (the burner's and the brake's timers, the fuel, the speed) and the effects' (the view's easing, the lurch, the
        /// sputter), which runs on frame time cut to 0.1 s a frame. They are the same as the wall clock on a quiet machine and not on a loaded one: a stall of half a second costs the sim 0.25 s (its catch-up is
        /// cut there) and the effects 0.4 s, and a wait in wall seconds then looks at a state that has not happened yet, or one a second too late (the brake check failed once in five under 20 busy processes:
        /// "two seconds in it is stopped and the brake is still on"). Gives up after four times as long in real time, so a clock that stands still cannot hang a run (the check then reports what it sees).
        /// </summary>
        IEnumerator Both(float seconds)
        {
            var world = GameWorld.Current;
            var ctx = _box.Juice.Context;
            float sim0 = world.Time, fx0 = ctx.Time, real0 = Time.realtimeSinceStartup;
            while ((world.Time - sim0 < seconds || ctx.Time - fx0 < seconds) && Time.realtimeSinceStartup - real0 < 4f * seconds + 2f) yield return null;
        }

        /// <summary>Wait until the sim's clock reaches this time (real-time limit 30 s, so a clock that stands still cannot hang a run).</summary>
        IEnumerator SimTo(float t)
        {
            var world = GameWorld.Current; float r0 = Time.realtimeSinceStartup;
            while (world.Time < t && Time.realtimeSinceStartup - r0 < 30f) yield return null;
        }

        /// <summary>Wait until the juice's effect clock reaches this time.</summary>
        IEnumerator FxTo(float t)
        {
            var ctx = _box.Juice.Context; float r0 = Time.realtimeSinceStartup;
            while (ctx.Time < t && Time.realtimeSinceStartup - r0 < 30f) yield return null;
        }

        /// <summary>Wait, frame by frame, until a condition holds; gives up after <paramref name="limit"/> seconds of real time (the check then reports what it sees).</summary>
        static IEnumerator Until(Func<bool> condition, float limit = 8f)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!condition() && Time.realtimeSinceStartup - t0 < limit) yield return null;
        }

        /// <summary>
        /// The player's death on the running sandbox: it takes the eye (first person: the pose is the player's own eye thrown out, never a camera apart), the HUD
        /// stands down, the events come in order, it cannot be skipped in its first moments and can after, the view ends black, the ordinary destroyed-ship event
        /// starts it too and the off switch stops that. About 8 s.
        /// <para>
        /// The sequence runs on a clock of its own: the frame time, cut to 0.1 s a frame (<see cref="DeathFx.Update"/>), so a slow frame (the first time a
        /// breach's fire and debris are drawn took 0.7 s in a windowed run) leaves it behind the wall clock, and a sub-millisecond headless frame runs it ahead.
        /// So every moment here is waited for on the sequence's own time, not on a number of seconds (the old waits sampled it 0.3 s early after a hitch, or
        /// 0.25 s late in a fast headless run, and the camera was read in the very frame the hitch ended in). Distances are measured from where the wreck is: the
        /// ship's pose at the hit carried on at its velocity at the hit, as the sequence's wreck drifts, because the sandbox's ship is not destroyed and goes on
        /// accelerating under its pilot (the brake check just before leaves it speeding up).
        /// </para>
        /// </summary>
        IEnumerator DeathCheck()
        {
            int fails = 0;
            void Check(bool ok, string what) { if (!ok) { fails++; MnLog.Error("JUICE deathcheck FAILED: " + what); } }
            var world = GameWorld.Current;
            var juice = _box.Juice;
            var death = juice.Death;
            var board = world.Death;
            var ids = new System.Collections.Generic.List<string>();
            var sub = GameEvents.SubscribeAll(e => { if (e.Id.StartsWith("death_", StringComparison.Ordinal)) ids.Add(e.Id); });
            yield return new WaitForSeconds(0.5f);
            Check(!board.Active && !board.CameraActive, "no death, the view is the flight view");
            int tickPlays = board.Plays;
            var me = world.Ships.Get(world.LocalPlayerShip);
            var cam = world.MainCamera.transform;
            Quaternion q0 = cam.rotation;
            Vector3 shipAtHit = me.Position, shipVelocityAtHit = me.Velocity;
            _worstFrame = 0f; _worstFirstSecond = 0f; _hitAt = Time.realtimeSinceStartup;
            _box.Trigger("death_gunfire");
            yield return Until(() => board.Time >= 0.25f);
            Check(board.Active && board.CameraActive && !board.Finished && board.Plays == tickPlays + 1, "the sequence has the eye from the hit");
            Check(death.Parts >= 20, $"the cabin model is in pieces to tear ({death.Parts})");
            Check(!death.TrySkip(), "it cannot be skipped in its first moments");
            yield return Until(() => board.Time >= 1.15f);
            float seqT = board.Time;
            float away = Vector3.Distance(board.Position, shipAtHit + shipVelocityAtHit * seqT);
            Check(away > 6f, $"flung out of the ship: the eye is {away:0} m from the wreck {seqT:0.00} s into the sequence");
            Check(Quaternion.Angle(q0, board.Rotation) > 45f, $"tumbling: the view has turned {Quaternion.Angle(q0, board.Rotation):0} degrees");
            // The camera takes the sequence's pose in the frame's late update, after this step: so the pose the board has now is the one the camera has next frame.
            Vector3 eyeNow = board.Position;
            yield return null;
            float camOff = Vector3.Distance(cam.position, eyeNow);
            Check(Mathf.Abs(camOff) < 3f, $"the camera is the sequence's eye (first person, no external view): {camOff:0.00} m off");
            Check(!_box.HudOn, "the HUD stands down");
            yield return Until(() => board.Time >= DeathFx.SkippableAt + 0.1f);
            Check(death.TrySkip(), "it can be skipped after a moment");
            yield return Until(() => board.Finished);
            yield return null;
            Check(!board.Active && board.Finished && board.CameraActive && juice.Context.Frame.DeathA.z > 0.99f, "skipped: over, and the screen is black");
            string order = string.Join(" ", ids);
            Check(ids.Count >= 6 && ids[0] == DeathEvents.Start && ids.IndexOf(DeathEvents.Decompression) > ids.IndexOf(DeathEvents.CanopyCrack) && order.Contains(DeathEvents.Skip) && ids[ids.Count - 1] == DeathEvents.End,
                  $"the events came in order: {order}");
            death.Reset();
            yield return new WaitForSeconds(0.3f);
            Check(!board.Active && !board.CameraActive && !board.Finished && !juice.Context.Frame.DeathActive, "reset: the flight view is back");

            // The ordinary destroyed-ship event for the player starts it too; the off switch stops that.
            juice.Settings.death.enabled = false;
            GameEvents.RaiseAt(CombatEvents.ShipDestroyed, me.Position, Vector3.zero, "fighter", me.Radius, me.Id, GameWorld.NoShip);
            yield return new WaitForSeconds(0.2f);
            Check(!board.Active, "with the death sequence off, the destroyed ship does not start it");
            juice.Settings.death.enabled = true;
            GameEvents.RaiseAt(CombatEvents.ShipDestroyed, me.Position, Vector3.zero, "fighter", me.Radius, me.Id, GameWorld.NoShip);
            yield return new WaitForSeconds(0.2f);
            Check(board.Active, "the destroyed player ship starts it");
            death.Reset();
            sub.Dispose();
            float worst = _worstFrame;
            _hitAt = -1f;
            MnLog.Info($"JUICE deathcheck measured: the eye {away:0.0} m from the wreck at {seqT:0.00} s, the camera {camOff:0.00} m off the eye, the worst frame in the first second after the hit {_worstFirstSecond * 1000f:0} ms (since the hit, {worst * 1000f:0} ms)");
            MnLog.Info($"JUICE deathcheck {(fails == 0 ? "OK" : "FAILED (" + fails + ")")}: the eye, the pieces, the fling, the tumble, the skip, the events, the black, the off switch");
            if (fails > 0) { _exit = 1; Application.Quit(1); }
        }

        static IEnumerator Tap(Gamepad pad, params GamepadButton[] buttons)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState(buttons));
            yield return null; yield return null; yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null; yield return null;
        }

        static bool Saw(string id, int sinceTick) { foreach (var e in GameEvents.Recent) if (e.Id == id && e.Tick >= sinceTick) return true; return false; }

        /// <summary>Drives the panel and the pad shortcuts with a virtual gamepad, the way a hand on the pad would, and checks what happened.</summary>
        IEnumerator PanelDrive()
        {
            int fails = 0;
            void Check(bool ok, string what) { if (!ok) { fails++; MnLog.Error("JUICE paneldrive FAILED: " + what); } }
            // A test run may not have the window's focus (another program in front): let the virtual pad work anyway.
            var oldBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var pad = InputSystem.AddDevice<Gamepad>("MnVirtualPad");
            var world = GameWorld.Current;
            yield return null; yield return null;
            int b0 = world.Tick;
            yield return Tap(pad, GamepadButton.LeftShoulder, GamepadButton.North);   // LB + Y: beam strike (the capital ships are still alive)
            Check(Saw(CombatEvents.BeamCharge, b0), "LB+Y fires the beam strike");
            yield return Tap(pad, GamepadButton.Start);
            Check(_panel != null && _panel.Visible, "Start opens the panel");
            Check(_box.Combat.Blocked, "the guns go quiet while the panel is open");

            int t0 = world.Tick;
            yield return Tap(pad, GamepadButton.South);                    // A on the first button: Fighter kill
            Check(Saw(CombatEvents.ShipDestroyed, t0), "A on 'Fighter kill' raises ship_destroyed");

            yield return Tap(pad, GamepadButton.DpadDown);                 // Capital death
            t0 = world.Tick;
            yield return Tap(pad, GamepadButton.South);
            Check(Saw(CombatEvents.ShipDestroyed, t0), "D-pad down then A fires 'Capital death'");

            yield return Tap(pad, GamepadButton.RightShoulder);            // EFFECTS page
            yield return Tap(pad, GamepadButton.South);                    // Laser bolts toggle
            Check(!_box.Juice.Settings.effects.bolts, "A on a switch turns it off");
            yield return Tap(pad, GamepadButton.South);
            Check(_box.Juice.Settings.effects.bolts, "and on again");

            yield return Tap(pad, GamepadButton.RightShoulder);            // TUNING page: presets
            yield return Tap(pad, GamepadButton.DpadDown);                 // High (row 2, col 0)
            yield return Tap(pad, GamepadButton.South);
            Check(_box.Juice.Settings.preset == "High", "a preset button sets the preset (got " + _box.Juice.Settings.preset + ")");
            yield return Tap(pad, GamepadButton.DpadDown);                 // Shake slider
            float before = _box.Juice.Settings.levels.shake;
            yield return Tap(pad, GamepadButton.DpadRight);
            Check(_box.Juice.Settings.levels.shake > before, "right on a slider raises it");
            Check(_box.Juice.Settings.preset == "Custom", "a moved slider makes the preset Custom (got " + _box.Juice.Settings.preset + ")");

            yield return Tap(pad, GamepadButton.East);                     // B closes
            Check(!_panel.Visible, "B closes the panel");
            Check(!_box.Combat.Blocked, "the guns come back");

            t0 = world.Tick;
            yield return Tap(pad, GamepadButton.RightShoulder, GamepadButton.North);  // RB + Y: flak
            yield return new WaitForSeconds(0.2f);
            Check(Saw(CombatEvents.WeaponFired, t0), "RB+Y starts the flak shells");
            // Slow motion and hit-stop act on the sim's clock.
            var sim = world.Sim;
            _panel.SetVisible(false);
            yield return new WaitForSeconds(0.2f);
            _box.SetSlowMoForTest(true);
            yield return new WaitForSeconds(0.1f);
            Check(Mathf.Abs(sim.TimeScale - _box.Juice.Settings.slowMo) < 1e-3f, $"slow motion sets the sim's time scale to {_box.Juice.Settings.slowMo} (got {sim.TimeScale})");
            _box.SetSlowMoForTest(false);
            yield return new WaitForSeconds(0.1f);
            Check(Mathf.Abs(sim.TimeScale - 1f) < 1e-3f, "and back to 1");
            _box.Juice.Settings.hitStop.enabled = true; _box.Juice.Settings.hitStop.ms = 200f; _box.Juice.Settings.hitStop.scale = 0.05f;
            yield return new WaitForSeconds(0.3f);
            _box.Trigger("capital_death");                                   // a capital death only freezes at its end; use a bomber kill instead
            yield return new WaitForSeconds(0.05f);
            _box.Trigger("bomber_kill");
            yield return null;
            int ticksBefore = world.Tick; float realBefore = Time.unscaledTime;
            yield return new WaitForSeconds(0.15f);
            int ticks = world.Tick - ticksBefore; float real = Time.unscaledTime - realBefore;
            Check(ticks < real * 60f * 0.5f, $"hit-stop slows the game: {ticks} ticks in {real:0.00} s of real time (60/s normally)");
            _box.Juice.Settings.hitStop.enabled = false;
            yield return new WaitForSeconds(0.3f);

            // The mouse: open the panel, click "Hull hit" and drag the Shake slider.
            yield return Tap(pad, GamepadButton.Start);
            var mouse = InputSystem.AddDevice<Mouse>("MnVirtualMouse");
            yield return null; yield return null;
            _panel.GoToPage(0);
            yield return new WaitForSeconds(0.3f);
            if (_panel.TryGetRect("Hull hit", out var r))
            {
                t0 = world.Tick;
                var pos = new Vector2(r.center.x, Screen.height - r.center.y);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = pos }.WithButton(MouseButton.Left, false));
                yield return null; yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = pos }.WithButton(MouseButton.Left, true));
                yield return null; yield return null; yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = pos }.WithButton(MouseButton.Left, false));
                yield return null; yield return null;
                Check(Saw(CombatEvents.HitHull, t0), "a mouse click on 'Hull hit' fires it");
            }
            else Check(false, "the 'Hull hit' button is on screen to click");
            _panel.GoToPage(2);
            yield return new WaitForSeconds(0.3f);
            if (_panel.TryGetRect("Shake", out var sr))
            {
                float was = _box.Juice.Settings.levels.shake;
                var to = new Vector2(sr.xMax - 70f * Mathf.Max(0.6f, Screen.height / 1080f), Screen.height - sr.center.y);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = to }.WithButton(MouseButton.Left, true));
                yield return null; yield return null; yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = to }.WithButton(MouseButton.Left, false));
                yield return null; yield return null;
                Check(_box.Juice.Settings.levels.shake > was + 0.5f, $"dragging the Shake slider to the right end raises it (was {was:0.00}, now {_box.Juice.Settings.levels.shake:0.00})");
            }
            else Check(false, "the Shake slider is on screen to drag");
            InputSystem.RemoveDevice(mouse);
            InputSystem.RemoveDevice(pad);
            InputSystem.settings.backgroundBehavior = oldBackground;
            MnLog.Info($"JUICE paneldrive {(fails == 0 ? "OK" : "FAILED (" + fails + ")")}: panel, pad shortcuts, switches, presets, sliders, slow motion, hit-stop, mouse click and drag");
        }

        IEnumerator RumbleTest()
        {
            var pad = Gamepad.current;
            MnLog.Info($"RUMBLETEST pad: {(pad != null ? pad.GetType().Name + " '" + pad.displayName + "'" : "none")}; {RumbleOut.Describe()}");
            if (pad == null) yield break;
            pad.SetMotorSpeeds(0.35f, 0f);
            yield return new WaitForSeconds(0.18f);
            pad.SetMotorSpeeds(0f, 0.35f);
            yield return new WaitForSeconds(0.18f);
            pad.ResetHaptics();
            MnLog.Info("RUMBLETEST both motors commanded without error");
        }
    }
}
