using System;
using System.Collections.Generic;
using Monolith.Core;
using Monolith.HudUi;
using Monolith.Hud;
using Monolith.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Monolith.Game
{
    /// <summary>
    /// The HUD's switches every playable scene shares, so each scene only calls this: the HUD style (F9 at any time, D-pad up and down or H on the pause screen), the battle map for
    /// scenes with no cabin (B, or a double tap of View; the cabin's own double tap does it where there is one), whether the quick menu shows the pad's buttons or the keyboard's,
    /// and the pause screen's invert switches (LB flight pitch, RB cabin look; I and O on the keyboard), which are written back to the controls file the game was started with.
    /// </summary>
    public sealed class HudControls
    {
        readonly PilotHud _hud;
        readonly ControlsConfig _controls;
        readonly Func<bool> _hasCabin;
        float _lastTap = -10f;

        /// <summary>The controls whose invert switches the pause screen flips (null when there are none).</summary>
        public ControlsConfig Controls => _controls;

        public HudControls(PilotHud hud, ControlsConfig controls, Func<bool> hasCabin = null)
        {
            _hud = hud; _controls = controls; _hasCabin = hasCabin ?? (() => false);
        }

        /// <summary>Every frame (paused or not).</summary>
        public void Update(bool paused)
        {
            var kb = Keyboard.current; var pad = Gamepad.current;
            _hud.UsePadGlyphs = pad != null;
            if (kb != null && kb.f9Key.wasPressedThisFrame) Cycle(1);
            if (paused) return;
            if (_hasCabin()) { _hud.MapSwitch = false; return; }
            bool toggle = kb != null && kb.bKey.wasPressedThisFrame;
            if (pad != null && pad.selectButton.wasPressedThisFrame)
            {
                if (Time.unscaledTime - _lastTap < 0.35f) { toggle = true; _lastTap = -10f; } else _lastTap = Time.unscaledTime;
            }
            if (toggle) _hud.MapSwitch = !_hud.MapSwitch;
        }

        /// <summary>On the pause screen: the switches it offers.</summary>
        public void UpdatePaused()
        {
            var kb = Keyboard.current; var pad = Gamepad.current;
            if (_controls != null)
            {
                bool flipFlight = pad != null && pad.leftShoulder.wasPressedThisFrame || kb != null && kb.iKey.wasPressedThisFrame;
                bool flipLook = pad != null && pad.rightShoulder.wasPressedThisFrame || kb != null && kb.oKey.wasPressedThisFrame;
                if (flipFlight) _controls.invert.flight = !_controls.invert.flight;
                if (flipLook) _controls.invert.cabinLook = !_controls.invert.cabinLook;
                if (flipFlight || flipLook)
                {
                    bool saved = _controls.SaveInvert();
                    MnLog.Info($"CONTROLS invert: flight {_controls.invert.flight}, cabin look {_controls.invert.cabinLook}" + (saved ? " (saved to " + _controls.SourcePath + ")" : " (this run only)"));
                }
            }
            if (pad != null && pad.dpad.up.wasPressedThisFrame || kb != null && kb.hKey.wasPressedThisFrame) Cycle(1);
            if (pad != null && pad.dpad.down.wasPressedThisFrame) Cycle(-1);
        }

        void Cycle(int dir)
        {
            var s = HudStyles.Cycle(dir, save: true);
            MnLog.Info($"HUD style {s.Name}");
        }

        /// <summary>The pause screen's settings rows (the tab splits a key from what it does).</summary>
        public List<string> SettingsLines()
        {
            var lines = new List<string> { "#SETTINGS" };
            if (_controls != null)
            {
                lines.Add($"LB   I\tFLIGHT PITCH   {(_controls.invert.flight ? "INVERTED" : "NORMAL")}");
                lines.Add($"RB   O\tCABIN LOOK   {(_controls.invert.cabinLook ? "INVERTED" : "NORMAL")}");
            }
            var st = HudStyles.Current;
            lines.Add($"D-PAD UP / DOWN   H\tHUD STYLE   {st.Title}   ({st.About.Split('.')[0].ToUpperInvariant()})");
            lines.Add("F9\tNEXT HUD STYLE, AT ANY TIME");
            return lines;
        }
    }
}
