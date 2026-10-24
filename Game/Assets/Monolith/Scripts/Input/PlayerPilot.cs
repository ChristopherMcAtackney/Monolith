using Monolith.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Monolith.Input
{
    /// <summary>Menu-level actions that are not flight controls (handled by whoever runs the session).</summary>
    public enum SystemAction { Pause, TestKnock, Resume, Respawn, Quit }

    /// <summary>
    /// The local player's pad (first) and keyboard (fallback) as an <see cref="IPilotSource"/>. Sampled once per sim
    /// tick, so the stick is read at 60 Hz whatever the frame rate. Shaping (dead zones, curves) comes from
    /// <see cref="ControlsConfig"/>. The throttle is a setting moved by the left stick and held where it is left; while
    /// flight is matching a target's speed the setting follows the matched value, so moving the stick carries on from
    /// there (and cancels the match). The afterburner click is latched (toggle) and released when the ship reports the
    /// burner went out.
    /// </summary>
    public sealed class PlayerPilot : IPilotSource
    {
        readonly GameWorld _world;
        readonly ControlsConfig _cfg;
        int _ship;
        float _throttle;
        bool _synced;
        bool _abLatched, _abSeenOn;
        int _abLatchTicks;
        bool _prevAbButton, _prevFull, _prevZero, _prevThird, _prevTwoThirds;
        bool _prevKbZero, _prevKbThird, _prevKbTwoThirds, _prevKbFull;
        // The quick menu: hold the orders button to open it, tap it for the next subsystem.
        bool _menuHeld, _menuOpen, _tapPulse;
        int _menuTicks;

        /// <summary>The quick menu (power picks, wingman orders) is open: the HUD draws it.</summary>
        public bool QuickMenuOpen => _menuOpen;

        public PlayerPilot(GameWorld world, int shipId, ControlsConfig config)
        {
            _world = world;
            _ship = shipId;
            _cfg = config;
        }

        public ControlsConfig Config => _cfg;

        /// <summary>Fly a different ship, or pick the throttle up again from the ship after a respawn.</summary>
        public void Attach(int shipId)
        {
            _ship = shipId;
            _synced = false;
            _abLatched = false;
        }

        public ControlInput Sample(in SimFrame frame)
        {
            var ship = _world.Ships.Get(_ship);
            if (ship != null && (!_synced || ship.MatchingSpeed))
            {
                _throttle = ship.Throttle;
                _synced = true;
            }

            var pad = Gamepad.current;
            var kb = Keyboard.current;
            var p = _cfg.pad;
            var k = _cfg.keyboard;
            float pitchUp = 0f, yaw = 0f, roll = 0f, throttleRate = 0f;
            ControlButtons buttons = ControlButtons.None;
            bool abButton = false, abHeld = false;

            if (pad != null)
            {
                var aim = StickShaping.Radial(Stick(pad, p.aim.stick), p.aim.deadZone, p.aim.outerDeadZone, p.aim.exponent);
                pitchUp += p.aim.pushForwardIsNoseDown ? -aim.y : aim.y;
                yaw += aim.x;
                float r = StickShaping.Axial(Axis(pad, p.roll.control), p.roll.deadZone, p.roll.outerDeadZone, p.roll.exponent);
                roll += p.roll.rightIsRollRight ? r : -r;
                throttleRate += StickShaping.Axial(Axis(pad, p.throttle.control), p.throttle.deadZone, p.throttle.outerDeadZone,
                    p.throttle.exponent) * p.throttle.ratePerSecond;

                var b = p.buttons;
                // Y: a tap is "next subsystem"; held, it opens the quick menu and the buttons it names change meaning.
                bool menuButton = Pressed(pad, b.orders);
                if (menuButton)
                {
                    _menuTicks++;
                    if (!_menuOpen && _menuTicks * frame.Dt >= p.quickMenuHoldSeconds) _menuOpen = true;
                }
                else
                {
                    if (_menuHeld && !_menuOpen) _tapPulse = true;
                    _menuTicks = 0; _menuOpen = false;
                }
                _menuHeld = menuButton;
                bool menu = _menuOpen;
                if (menu)
                {
                    buttons |= ControlButtons.Orders;
                    if (Pressed(pad, b.menuPowerGuns)) buttons |= ControlButtons.PowerGuns;
                    if (Pressed(pad, b.menuPowerShields)) buttons |= ControlButtons.PowerShields;
                    if (Pressed(pad, b.menuPowerEngines)) buttons |= ControlButtons.PowerEngines;
                    if (Pressed(pad, b.menuPowerBalanced)) buttons |= ControlButtons.PowerBalanced;
                    if (Pressed(pad, b.menuOrderAttack)) buttons |= ControlButtons.OrderAttack;
                    if (Pressed(pad, b.menuOrderDisable)) buttons |= ControlButtons.OrderDisable;
                    if (Pressed(pad, b.menuOrderCover)) buttons |= ControlButtons.OrderCover;
                    if (Pressed(pad, b.menuOrderForm)) buttons |= ControlButtons.OrderForm;
                }
                if (_tapPulse) { buttons |= ControlButtons.SubsystemNext; _tapPulse = false; }
                if (Pressed(pad, b.primaryFire)) buttons |= ControlButtons.PrimaryFire;
                if (!menu)
                {
                    if (Pressed(pad, b.secondaryFire)) buttons |= ControlButtons.SecondaryFire;
                    if (Pressed(pad, b.countermeasure)) buttons |= ControlButtons.Countermeasure;
                    if (Pressed(pad, b.matchSpeed)) buttons |= ControlButtons.MatchSpeed;
                    if (Pressed(pad, b.targetNext)) buttons |= ControlButtons.TargetNext;
                }
                if (Pressed(pad, b.missileFire)) buttons |= ControlButtons.MissileFire;
                if (Pressed(pad, b.targetAhead)) buttons |= ControlButtons.TargetReticle;
                if (Pressed(pad, b.cycleMissile)) buttons |= ControlButtons.CycleMissile;
                if (Pressed(pad, b.targetAttacker)) buttons |= ControlButtons.TargetAttacker;
                abButton = Pressed(pad, b.afterburner);
                if (!p.afterburnerToggle) abHeld |= abButton;

                Preset(!menu && Pressed(pad, b.throttleFull), ref _prevFull, 1f);
                Preset(!menu && Pressed(pad, b.throttleZero), ref _prevZero, 0f);
                Preset(!menu && Pressed(pad, b.throttleThird), ref _prevThird, 1f / 3f);
                Preset(!menu && Pressed(pad, b.throttleTwoThirds), ref _prevTwoThirds, 2f / 3f);
            }

            if (kb != null)
            {
                pitchUp += Key(kb, k.pitchUp) - Key(kb, k.pitchDown);
                yaw += Key(kb, k.yawRight) - Key(kb, k.yawLeft);
                roll += Key(kb, k.rollRight) - Key(kb, k.rollLeft);
                throttleRate += (Key(kb, k.throttleUp) - Key(kb, k.throttleDown)) * k.throttleRatePerSecond;
                if (Key(kb, k.primaryFire) > 0f) buttons |= ControlButtons.PrimaryFire;
                if (Key(kb, k.secondaryFire) > 0f) buttons |= ControlButtons.SecondaryFire;
                if (Key(kb, k.missileFire) > 0f) buttons |= ControlButtons.MissileFire;
                if (Key(kb, k.countermeasure) > 0f) buttons |= ControlButtons.Countermeasure;
                if (Key(kb, k.cycleMissile) > 0f) buttons |= ControlButtons.CycleMissile;
                if (Key(kb, k.subsystemNext) > 0f) buttons |= ControlButtons.SubsystemNext;
                if (Key(kb, k.targetAttacker) > 0f) buttons |= ControlButtons.TargetAttacker;
                if (Key(kb, k.powerGuns) > 0f) buttons |= ControlButtons.PowerGuns;
                if (Key(kb, k.powerShields) > 0f) buttons |= ControlButtons.PowerShields;
                if (Key(kb, k.powerEngines) > 0f) buttons |= ControlButtons.PowerEngines;
                if (Key(kb, k.powerBalanced) > 0f) buttons |= ControlButtons.PowerBalanced;
                if (Key(kb, k.orderAttack) > 0f) buttons |= ControlButtons.OrderAttack;
                if (Key(kb, k.orderDisable) > 0f) buttons |= ControlButtons.OrderDisable;
                if (Key(kb, k.orderCover) > 0f) buttons |= ControlButtons.OrderCover;
                if (Key(kb, k.orderForm) > 0f) buttons |= ControlButtons.OrderForm;
                if (Key(kb, k.matchSpeed) > 0f) buttons |= ControlButtons.MatchSpeed;
                if (Key(kb, k.targetNext) > 0f) buttons |= ControlButtons.TargetNext;
                if (Key(kb, k.targetAhead) > 0f) buttons |= ControlButtons.TargetReticle;
                abHeld |= Key(kb, k.afterburner) > 0f;
                Preset(Key(kb, k.throttleZero) > 0f, ref _prevKbZero, 0f);
                Preset(Key(kb, k.throttleThird) > 0f, ref _prevKbThird, 1f / 3f);
                Preset(Key(kb, k.throttleTwoThirds) > 0f, ref _prevKbTwoThirds, 2f / 3f);
                Preset(Key(kb, k.throttleFull) > 0f, ref _prevKbFull, 1f);
            }

            _throttle = Mathf.Clamp01(_throttle + throttleRate * frame.Dt);

            // Afterburner click: latch on, click again to drop it; let go of the latch when the burner goes out
            // (tank empty) or never lights (not enough fuel, or the 1.3 s restart lockout).
            if (p.afterburnerToggle && abButton && !_prevAbButton)
            {
                _abLatched = !_abLatched;
                _abSeenOn = false;
                _abLatchTicks = 0;
            }
            _prevAbButton = abButton;
            if (_abLatched)
            {
                _abLatchTicks++;
                bool on = ship != null && ship.AfterburnerOn;
                if (on) _abSeenOn = true;
                if ((_abSeenOn && !on) || (!_abSeenOn && _abLatchTicks > 3)) _abLatched = false;
            }
            if (_abLatched || abHeld) buttons |= ControlButtons.Afterburner;

            return new ControlInput
            {
                Pitch = Mathf.Clamp(pitchUp, -1f, 1f),
                Yaw = Mathf.Clamp(yaw, -1f, 1f),
                Roll = Mathf.Clamp(roll, -1f, 1f),
                Throttle = _throttle,
                Buttons = buttons,
            };
        }

        /// <summary>True on the frame a menu-level button went down (call from Update, not from the sim).</summary>
        public bool SystemPressed(SystemAction action)
        {
            var pad = Gamepad.current;
            var kb = Keyboard.current;
            return (pad != null && WasPressed(pad, PadSystem(action))) || (kb != null && WasPressed(kb, KeySystem(action)));
        }

        /// <summary>True when a gamepad is connected.</summary>
        public static bool HasPad => Gamepad.current != null;

        /// <summary>A short description of the device in use, for the log.</summary>
        public static string Devices()
        {
            var pad = Gamepad.current;
            return pad != null ? $"pad '{pad.displayName}' + keyboard" : "keyboard only (no pad found)";
        }

        string PadSystem(SystemAction a) => Pick(_cfg.pad.system, a);
        string KeySystem(SystemAction a) => Pick(_cfg.keyboard.system, a);

        static string Pick(ControlsConfig.SystemButtons s, SystemAction a)
        {
            switch (a)
            {
                case SystemAction.Pause: return s.pause;
                case SystemAction.TestKnock: return s.testKnock;
                case SystemAction.Resume: return s.resume;
                case SystemAction.Respawn: return s.respawn;
                default: return s.quit;
            }
        }

        void Preset(bool down, ref bool before, float value)
        {
            if (down && !before) _throttle = value;
            before = down;
        }

        static Vector2 Stick(InputDevice d, string path) =>
            !string.IsNullOrEmpty(path) && d.TryGetChildControl(path) is StickControl s ? s.ReadValue() : Vector2.zero;

        static float Axis(InputDevice d, string path) =>
            !string.IsNullOrEmpty(path) && d.TryGetChildControl(path) is AxisControl a ? a.ReadValue() : 0f;

        static bool Pressed(InputDevice d, string path) =>
            !string.IsNullOrEmpty(path) && d.TryGetChildControl(path) is ButtonControl b && b.isPressed;

        static float Key(InputDevice d, string path) => Pressed(d, path) ? 1f : 0f;

        static bool WasPressed(InputDevice d, string path) =>
            !string.IsNullOrEmpty(path) && d.TryGetChildControl(path) is ButtonControl b && b.wasPressedThisFrame;
    }
}
