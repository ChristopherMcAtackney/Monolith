using System;
using System.Collections;
using System.IO;
using Monolith.Core;
using Monolith.Juice;
using UnityEngine;
using UnityEngine.InputSystem;

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
            if (!CommandLine.Has("-mnJuiceSelfTest") && !CommandLine.Has("-mnPanelShot") && !CommandLine.Has("-mnRumbleTest")) return;
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
