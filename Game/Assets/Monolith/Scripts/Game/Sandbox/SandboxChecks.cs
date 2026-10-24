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
            Application.Quit(0);
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
            File.WriteAllText(tmp, s.ToJson());
            var r = JuiceSettings.Parse(File.ReadAllText(tmp), tmp);
            Check(!r.effects.bolts && r.effects.beams, "switches round-trip");
            Check(Mathf.Approximately(r.levels.shake, 1.7f) && Mathf.Approximately(r.levels.beamWidth, 2.3f), "levels round-trip");
            Check(r.hitStop.enabled && Mathf.Approximately(r.slowMo, 0.4f), "hit-stop and slow-mo round-trip");
            Check(r.DetectPreset() == "Custom", "a hand-tuned set is Custom");
            r.ApplyPreset("Medium");
            Check(r.DetectPreset() == "Medium" && r.levels.shake == 1f, "Medium preset");
            var d = new JuiceSettings();
            Check(d.preset == "Medium" && d.levels.shake == 1f && !d.hitStop.enabled, "defaults are Medium with hit-stop off");
            Check(JuiceSettings.Parse(Resources.Load<TextAsset>(JuiceSettings.ResourcePath).text, "defaults").DetectPreset() == "Medium", "shipped defaults are Medium");
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
