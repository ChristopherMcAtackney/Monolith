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
        // The brake (R3): a click latches it and remembers the throttle; the next click, a throttle push, match speed or a boost lets go.
        bool _brakeLatched, _prevBrakeButton, _presetPushed;
        float _brakeResume = 2f / 3f, _lastCruise = 2f / 3f;
        // A boost started at a standstill leaves the throttle at cruise; if the burner turns out to be refused the brake and throttle come back.
        bool _prevAbRequest, _boostFromRest, _brakeBeforeBoost;
        float _throttleBeforeBoost;
        // Clicking the aim stick nudges it: the aim is held at what it read just before the click while the button is down and for a moment after.
        const int AimHistory = 8;
        const float AimHoldMaxPress = 0.5f;
        readonly Vector2[] _aimHist = new Vector2[AimHistory];
        int _aimHistAt;
        bool _prevBrakePad, _aimHolding;
        Vector2 _aimHeld;
        float _aimHoldLeft, _aimHoldPress;

        /// <summary>The retro brake is latched (the throttle setting is 0 and the previous one is remembered).</summary>
        public bool BrakeLatched => _brakeLatched;
        /// <summary>The throttle setting the next click of the brake gives back.</summary>
        public float BrakeResume => _brakeResume;

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
            _brakeLatched = false;
            _boostFromRest = false;
            _aimHolding = false;
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
            bool abButton = false, abHeld = false, brakeButton = false;
            _presetPushed = false;

            if (pad != null)
            {
                var b = p.buttons;
                var aim = StickShaping.Radial(Stick(pad, p.aim.stick), p.aim.deadZone, p.aim.outerDeadZone, p.aim.exponent);
                bool brakePad = Pressed(pad, b.brake);
                aim = HoldAimThroughClick(aim, brakePad, frame.Dt);
                brakeButton |= brakePad;
                pitchUp += _cfg.invert.flight ? -aim.y : aim.y;      // inverted (the default): stick forward is nose down
                yaw += aim.x;
                float r = StickShaping.Axial(Axis(pad, p.roll.control), p.roll.deadZone, p.roll.outerDeadZone, p.roll.exponent);
                roll += p.roll.rightIsRollRight ? r : -r;
                throttleRate += StickShaping.Axial(Axis(pad, p.throttle.control), p.throttle.deadZone, p.throttle.outerDeadZone,
                    p.throttle.exponent) * p.throttle.ratePerSecond;

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
                if (!menu && Pressed(pad, b.cycleMissile)) buttons |= ControlButtons.CycleMissile;      // D-pad right is "favour engines" while the quick menu is open
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
                { float kp = Key(kb, k.pitchUp) - Key(kb, k.pitchDown); pitchUp += _cfg.invert.flight ? kp : -kp; }      // W is "stick forward": nose down when inverted
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
                brakeButton |= Key(kb, k.brake) > 0f;
                Preset(Key(kb, k.throttleZero) > 0f, ref _prevKbZero, 0f);
                Preset(Key(kb, k.throttleThird) > 0f, ref _prevKbThird, 1f / 3f);
                Preset(Key(kb, k.throttleTwoThirds) > 0f, ref _prevKbTwoThirds, 2f / 3f);
                Preset(Key(kb, k.throttleFull) > 0f, ref _prevKbFull, 1f);
            }

            _throttle = Mathf.Clamp01(_throttle + throttleRate * frame.Dt);
            bool throttlePushed = throttleRate > 0f || _presetPushed;

            // Brake click: stop hard and remember the throttle (a second click gives it back). Pushing the throttle up, matching
            // speed or boosting lets go of it, and the throttle carries on from where the brake left it (0).
            bool brakeClick = brakeButton && !_prevBrakeButton;
            _prevBrakeButton = brakeButton;
            if (!_brakeLatched && _throttle >= 0.1f) _lastCruise = _throttle;
            if (brakeClick)
            {
                if (_brakeLatched) { _throttle = _brakeResume; _brakeLatched = false; }
                else
                {
                    _brakeResume = _throttle >= 0.1f ? _throttle : _lastCruise;      // braked from a stop: the last real setting
                    _throttle = 0f;
                    _brakeLatched = true;
                    _abLatched = false;                                               // the brake shuts the burner off
                    _boostFromRest = false;
                }
            }
            else if (_brakeLatched && (throttlePushed || (buttons & ControlButtons.MatchSpeed) != 0)) _brakeLatched = false;

            // Afterburner click: latch on, click again to drop it; let go of the latch when the burner goes out
            // (tank empty) or never lights (not enough fuel, or the 1.3 s restart lockout).
            if (p.afterburnerToggle && abButton && !_prevAbButton && !brakeClick)
            {
                _abLatched = !_abLatched;
                _abSeenOn = false;
                _abLatchTicks = 0;
            }
            _prevAbButton = abButton;
            bool abRequest = _abLatched || abHeld;
            if (abRequest && !_prevAbRequest)
            {
                // A boost from a standstill (throttle 0, braked or not) must not end in a stop: it leaves the throttle at cruise.
                _boostFromRest = false;
                if (_brakeLatched || _throttle < 0.05f)
                {
                    _throttleBeforeBoost = _throttle; _brakeBeforeBoost = _brakeLatched;
                    _throttle = Mathf.Clamp01(_cfg.brake.boostFromRestThrottle);
                    _boostFromRest = _abLatched;      // only a click can be refused and taken back (a held key just stops when it is let go)
                }
                _brakeLatched = false;
            }
            _prevAbRequest = abRequest;
            if (_abLatched)
            {
                _abLatchTicks++;
                bool on = ship != null && ship.AfterburnerOn;
                if (on) { _abSeenOn = true; _boostFromRest = false; }
                if ((_abSeenOn && !on) || (!_abSeenOn && _abLatchTicks > 3))
                {
                    _abLatched = false;
                    if (_boostFromRest && !_abSeenOn) { _throttle = _throttleBeforeBoost; _brakeLatched = _brakeBeforeBoost; }      // refused: nothing changed
                    _boostFromRest = false;
                }
            }
            if (_abLatched || abHeld) buttons |= ControlButtons.Afterburner;
            if (_brakeLatched) buttons |= ControlButtons.Brake;

            return new ControlInput
            {
                Pitch = Mathf.Clamp(pitchUp, -1f, 1f),
                Yaw = Mathf.Clamp(yaw, -1f, 1f),
                Roll = Mathf.Clamp(roll, -1f, 1f),
                Throttle = _brakeLatched ? _brakeResume : _throttle,      // while braked the ship ignores it and the HUD shows it as the throttle a second click gives back
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
            if (down && !before) { _throttle = value; if (value > 0f) _presetPushed = true; }
            before = down;
        }

        /// <summary>
        /// R3 is the aim stick, and clicking a thumbstick in pushes it a little off centre. From the click until
        /// <c>aimHoldSeconds</c> after the button comes up (never more than 0.5 s of holding) the aim reads what it read
        /// <c>aimLookBackSeconds</c> before the click, so the nose carries straight on through it.
        /// </summary>
        Vector2 HoldAimThroughClick(Vector2 aim, bool clickDown, float dt)
        {
            var cfg = _cfg.brake;
            Vector2 result = aim;
            if (cfg.aimHoldSeconds > 0f)
            {
                if (clickDown && !_prevBrakePad)
                {
                    int back = Mathf.Clamp(Mathf.RoundToInt(cfg.aimLookBackSeconds / Mathf.Max(1e-4f, dt)), 0, AimHistory - 1);
                    _aimHeld = _aimHist[((_aimHistAt - 1 - back) % AimHistory + AimHistory) % AimHistory];
                    _aimHolding = true; _aimHoldLeft = cfg.aimHoldSeconds; _aimHoldPress = 0f;
                }
                if (_aimHolding)
                {
                    if (clickDown)
                    {
                        _aimHoldPress += dt;
                        _aimHoldLeft = cfg.aimHoldSeconds;
                        if (_aimHoldPress > AimHoldMaxPress) _aimHolding = false;      // held in on purpose: the stick is the player's again
                    }
                    else _aimHoldLeft -= dt;
                    if (_aimHoldLeft <= 0f) _aimHolding = false;
                    if (_aimHolding) result = _aimHeld;
                }
            }
            _prevBrakePad = clickDown;
            _aimHist[_aimHistAt] = aim;          // the raw reading, so the look-back never sees a held value
            _aimHistAt = (_aimHistAt + 1) % AimHistory;
            return result;
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
