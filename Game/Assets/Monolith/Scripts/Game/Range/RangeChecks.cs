using System.Collections;
using Monolith.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Monolith.Game
{
    /// <summary>
    /// Checks the combat range can run on its own, because nobody can hold the pad in a build: <c>-mnScene range -mnPadDrive</c> adds a virtual
    /// Xbox pad, presses its buttons the way a hand would (taps, holds, the Y quick menu) and checks what reached the ship as
    /// <see cref="ControlInput"/> and what the systems did with it. Logs <c>PADDRIVE PASS</c> / <c>FAIL</c> lines and quits (exit 1 on a failure).
    /// </summary>
    public sealed class RangeChecks : MonoBehaviour
    {
        CombatRange _range;
        GameWorld _world;
        int _fails, _checks;
        ControlButtons _seen;

        public static bool Requested => CommandLine.Has("-mnPadDrive");

        public static void Attach(CombatRange range, Transform parent)
        {
            if (!Requested) return;
            var c = new GameObject("RangeChecks").AddComponent<RangeChecks>();
            c.transform.SetParent(parent, false);
            c._range = range;
            c._world = GameWorld.Current;
            c._world.Sim.Register(new Watcher(c));
            GameEvents.Subscribe(CombatEvents.WeaponFired, e =>
            {
                if (e.Entity != c._world.LocalPlayerShip || e.Subject == null) return;
                c._shots.TryGetValue(e.Subject, out int n);
                c._shots[e.Subject] = n + 1;
            });
            c.StartCoroutine(c.Run());
        }

        /// <summary>ORs every button the player's pilot held on any tick, so a one-tick tap cannot be missed between frames.</summary>
        sealed class Watcher : ISimSystem
        {
            readonly RangeChecks _c;
            public Watcher(RangeChecks c) { _c = c; }
            public int Order => SimOrder.Pilots + 5;
            public void Step(in SimFrame frame) => _c._seen |= _c._world.Pilots.Current(_c._world.LocalPlayerShip).Buttons;
        }

        void Check(bool ok, string what)
        {
            _checks++;
            if (!ok) _fails++;
            MnLog.Info($"PADDRIVE {(ok ? "PASS" : "FAIL")}: {what}");
        }

        static IEnumerator Hold(Gamepad pad, float seconds, params GamepadButton[] buttons)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState(buttons));
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds) yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState());
            // Let at least three sim ticks see the release (a headless run draws a frame every millisecond, faster than the 60 Hz sim ticks).
            float t1 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t1 < 0.07f) yield return null;
        }

        static IEnumerator Tap(Gamepad pad, params GamepadButton[] buttons) => Hold(pad, 0.12f, buttons);

        bool Saw(ControlButtons b) => (_seen & b) == b;
        void Clear() => _seen = ControlButtons.None;

        IEnumerator Run()
        {
            var oldBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var pad = InputSystem.AddDevice<Gamepad>("MnVirtualPad");
            yield return new WaitForSeconds(1.5f);
            var me = _world.Ships.Get(_world.LocalPlayerShip);
            var mc = _world.Combat.Get(me.Id);
            mc.Invulnerable = true;

            // Inverted flight is the default (controls.json invert.flight): the aim stick pushed forward points the nose down, pulled back brings it up.
            {
                float y0 = me.Forward.y;
                InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = new Vector2(0f, 1f) });
                yield return new WaitForSeconds(0.4f);
                float y1 = me.Forward.y;
                InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = new Vector2(0f, -1f) });
                yield return new WaitForSeconds(0.9f);
                float y2 = me.Forward.y;
                InputSystem.QueueStateEvent(pad, new GamepadState());
                yield return new WaitForSeconds(0.4f);
                Check(y1 < y0 - 0.03f, $"the aim stick pushed forward points the nose down, the inverted default (nose height {y0:0.00} to {y1:0.00})");
                Check(y2 > y1 + 0.03f, $"... and pulled back brings the nose up ({y1:0.00} to {y2:0.00})");
            }

            yield return Tap(pad, GamepadButton.South);
            Check(Saw(ControlButtons.TargetNext) && me.Target != GameWorld.NoShip && _world.Ships.Get(me.Target).Team != 0, "A targets the next hostile");

            Clear(); int shots0 = Count("needle");
            // Triggers are analog: drive them through the state's trigger fields.
            InputSystem.QueueStateEvent(pad, new GamepadState { rightTrigger = 1f });
            yield return new WaitForSeconds(0.5f);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null; yield return null;
            Check(Saw(ControlButtons.PrimaryFire) && Count("needle") > shots0 + 2, $"RT fires the fast gun ({Count("needle") - shots0} shots)");

            Clear(); int hammers0 = Count("hammer");
            yield return Hold(pad, 0.6f, GamepadButton.LeftShoulder);
            Check(Saw(ControlButtons.SecondaryFire) && Count("hammer") > hammers0, "LB fires the heavy gun");

            Clear(); int sel0 = mc.SelectedMissile;
            yield return Tap(pad, GamepadButton.RightStick);
            Check(Saw(ControlButtons.CycleMissile) && mc.SelectedMissile != sel0, $"R3 steps to the next missile type ({sel0} to {mc.SelectedMissile})");
            yield return Tap(pad, GamepadButton.RightStick);
            yield return Tap(pad, GamepadButton.RightStick);
            Check(mc.SelectedMissile == sel0, "three clicks come full circle");

            Clear(); int ammo0 = mc.Missiles[0].Ammo;
            yield return Tap(pad, GamepadButton.RightShoulder);
            Check(Saw(ControlButtons.MissileFire) && mc.Missiles[0].Ammo == ammo0 - 1, $"RB fires the selected missile ({ammo0} to {mc.Missiles[0].Ammo})");

            Clear(); int cm0 = mc.Decoys;
            yield return Tap(pad, GamepadButton.East);
            Check(Saw(ControlButtons.Countermeasure) && mc.Decoys == cm0 - 1, $"B releases a decoy ({cm0} to {mc.Decoys})");

            Clear();
            InputSystem.QueueStateEvent(pad, new GamepadState { leftTrigger = 1f });
            yield return new WaitForSeconds(0.2f);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null; yield return null;
            Check(Saw(ControlButtons.TargetReticle), "LT asks for the ship in the reticle");

            Clear();
            yield return Tap(pad, GamepadButton.Select);
            Check(Saw(ControlButtons.TargetAttacker), "View asks for the nearest attacker");

            // Y: a tap is the next part; a hold is the quick menu.
            Clear();
            yield return Tap(pad, GamepadButton.North);
            Check(Saw(ControlButtons.SubsystemNext) && !Saw(ControlButtons.Orders), "tapping Y steps the target's parts and does not open the menu");

            Clear();
            InputSystem.QueueStateEvent(pad, new GamepadState(GamepadButton.North));
            yield return new WaitForSeconds(0.5f);
            Check(Saw(ControlButtons.Orders) && !Saw(ControlButtons.SubsystemNext), "holding Y opens the quick menu");
            Clear();
            InputSystem.QueueStateEvent(pad, new GamepadState(GamepadButton.North, GamepadButton.DpadUp));
            yield return new WaitForSeconds(0.2f);
            Check(Saw(ControlButtons.PowerGuns) && mc.Power == PowerMode.Guns, "with the menu open D-pad up favours the guns");
            InputSystem.QueueStateEvent(pad, new GamepadState(GamepadButton.North, GamepadButton.DpadLeft));
            yield return new WaitForSeconds(0.2f);
            Check(mc.Power == PowerMode.Shields, "left favours the shields");
            InputSystem.QueueStateEvent(pad, new GamepadState(GamepadButton.North, GamepadButton.DpadRight));
            yield return new WaitForSeconds(0.2f);
            Check(mc.Power == PowerMode.Engines, "right favours the engines");
            InputSystem.QueueStateEvent(pad, new GamepadState(GamepadButton.North, GamepadButton.DpadDown));
            yield return new WaitForSeconds(0.2f);
            Check(mc.Power == PowerMode.Balanced, "down is balanced");
            Clear();
            InputSystem.QueueStateEvent(pad, new GamepadState(GamepadButton.North, GamepadButton.South));
            yield return new WaitForSeconds(0.2f);
            Check(Saw(ControlButtons.OrderAttack) && !Saw(ControlButtons.TargetNext), "in the menu A is the wingman order, not next target");
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return new WaitForSeconds(0.2f);
            Check(_world.Pilots.Current(me.Id).Buttons == ControlButtons.None || !_world.Pilots.Current(me.Id).Held(ControlButtons.Orders), "letting go of Y closes the menu");

            // The throttle presets still work out of the menu.
            yield return Tap(pad, GamepadButton.DpadUp);
            yield return new WaitForSeconds(0.2f);
            Check(_world.Pilots.Current(me.Id).Throttle > 0.99f, "D-pad up is still 100% throttle outside the menu");

            // The HUD's switches, through the real scene: the battle map on a double tap of View, the pause screen's invert switches and HUD style, F9.
            {
                var hud = _range.Hud;
                var sw = _range.HudSwitches;
                bool map0 = hud.MapSwitch;
                yield return Tap(pad, GamepadButton.Select);
                yield return Tap(pad, GamepadButton.Select);
                yield return new WaitForSeconds(0.15f);
                Check(!map0 && hud.MapSwitch, "a double tap of View opens the battle map");
                yield return Tap(pad, GamepadButton.Select);
                yield return Tap(pad, GamepadButton.Select);
                yield return new WaitForSeconds(0.15f);
                Check(!hud.MapSwitch, "and another closes it");

                yield return Tap(pad, GamepadButton.Start);
                yield return new WaitForSeconds(0.2f);
                Check(hud.Overlay != null && hud.Overlay.Length > 0 && System.Array.Exists(hud.Overlay, l => l.Contains("FLIGHT PITCH")) && System.Array.Exists(hud.Overlay, l => l.Contains("CABIN LOOK")), "Start pauses and the pause screen lists both invert switches");
                bool f0 = sw.Controls.invert.flight, l0 = sw.Controls.invert.cabinLook;
                yield return Tap(pad, GamepadButton.LeftShoulder);
                yield return Tap(pad, GamepadButton.RightShoulder);
                yield return new WaitForSeconds(0.2f);
                Check(sw.Controls.invert.flight == !f0 && sw.Controls.invert.cabinLook == !l0, $"on the pause screen LB flips flight pitch ({f0} to {sw.Controls.invert.flight}) and RB flips cabin look ({l0} to {sw.Controls.invert.cabinLook})");
                Check(System.Array.Exists(hud.Overlay, l => l.Contains("FLIGHT PITCH") && l.Contains(f0 ? "NORMAL" : "INVERTED")), "the screen shows the new setting");
                yield return Tap(pad, GamepadButton.LeftShoulder);
                yield return Tap(pad, GamepadButton.RightShoulder);
                yield return new WaitForSeconds(0.2f);
                Check(sw.Controls.invert.flight == f0 && sw.Controls.invert.cabinLook == l0, "and they flip back");
                string style0 = Monolith.Hud.HudStyles.Current.Name;
                yield return Tap(pad, GamepadButton.DpadUp);
                yield return new WaitForSeconds(0.2f);
                string style1 = Monolith.Hud.HudStyles.Current.Name;
                Check(style1 != style0 && hud.Overlay != null && System.Array.Exists(hud.Overlay, l => l.Contains("HUD STYLE") && l.ToUpperInvariant().Contains(style1.ToUpperInvariant())), $"on the pause screen D-pad up changes the HUD style ({style0} to {style1})");
                yield return Tap(pad, GamepadButton.DpadDown);
                yield return new WaitForSeconds(0.2f);
                Check(Monolith.Hud.HudStyles.Current.Name == style0, "and D-pad down changes it back");
                yield return Tap(pad, GamepadButton.Start);
                yield return new WaitForSeconds(0.2f);
                Check(hud.Overlay == null, "Start resumes");

                var kb = InputSystem.AddDevice<Keyboard>("MnVirtualKeys");
                InputSystem.QueueStateEvent(kb, new KeyboardState(Key.F9));
                yield return new WaitForSeconds(0.1f);
                InputSystem.QueueStateEvent(kb, new KeyboardState());
                yield return new WaitForSeconds(0.2f);
                Check(Monolith.Hud.HudStyles.Current.Name != style0, $"F9 changes the HUD style in play ({style0} to {Monolith.Hud.HudStyles.Current.Name})");
                Monolith.Hud.HudStyles.Set(style0);
            }

            MnLog.Info($"PADDRIVE {(_fails == 0 ? "OK" : "FAILED")}: {_checks - _fails} of {_checks} checks pass");
            InputSystem.settings.backgroundBehavior = oldBackground;
            Application.Quit(_fails == 0 ? 0 : 1);
        }

        readonly System.Collections.Generic.Dictionary<string, int> _shots = new System.Collections.Generic.Dictionary<string, int>();

        int Count(string weapon) => _shots.TryGetValue(weapon, out int n) ? n : 0;
    }
}
