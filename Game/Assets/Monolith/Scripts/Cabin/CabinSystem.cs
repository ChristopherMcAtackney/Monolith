using System;
using System.Collections.Generic;
using Monolith.Core;
using Monolith.Mission;
using UnityEngine;

namespace Monolith.Cabin
{
    /// <summary>
    /// The cabin: the pilot as a person in a very small ship (design section 6). It is the mission system's <see cref="ICabin"/> (seated, powered, detected,
    /// needs repair; scan, call out, knock out, lights) and a sim system (order Mission + 50) that steps the pilot's body, the transitions in and out of the seat, the four
    /// interactions and the lights. It owns the <see cref="CabinBoard"/> the view, the HUD, the sky and the audio read. While the pilot is not seated, or the ship
    /// is dark, nobody flies: <see cref="CabinPilot"/> holds the ship's last heading and speed.
    ///
    /// Everything here is plain data and maths (no scene objects), so it runs headless: the cabin self-test drives it with a <see cref="ScriptedCabinInput"/>.
    /// Before launch the sim is held and the rig steps it on its own clock with <see cref="Advance"/>; in play the sim does, at 60 Hz.
    /// </summary>
    public sealed partial class CabinSystem : ISimSystem, ICabin, IDisposable
    {
        public readonly GameWorld World;
        public readonly CabinLayout Layout;
        public readonly CabinConfig Cfg;
        public readonly CabinBoard Board = new CabinBoard();
        public readonly MissionFile File;
        public Loadout Loadout { get; private set; }

        ICabinInput _input;
        readonly IMissionHost _host;
        readonly List<IDisposable> _subs = new List<IDisposable>();
        float _now;
        readonly System.Random _rng = new System.Random(2207);

        public int Order => SimOrder.Mission + 50;

        /// <summary>The cabin's pilot is not flying: standing up, walking, sitting back down, or the ship is dark. The ship holds its heading and speed.</summary>
        public bool PilotAway => Board.State != CabinBoard.Phase.Seated || !Powered;
        public CabinBoard.Phase State => Board.State;
        /// <summary>The pause menu is up: the rig does not step the cabin on its own clock.</summary>
        public bool Paused;
        /// <summary>The seat asked to launch (before launch, seated, the confirm button): the scene lets the sim go.</summary>
        public event Action Launched;

        public CabinSystem(GameWorld world, CabinLayout layout, CabinConfig cfg, ICabinInput input, IMissionHost host = null, MissionFile file = null)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            Cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _host = host; File = file;
            Loadout = new Loadout(file?.Cabin, cfg.rack);
            World.Cabin = Board;
            if (_input is ScriptedCabinInput sc) sc.Bind(this);
            ResetBody();
            Publish(true);
            _subs.Add(GameEvents.Subscribe(CombatEvents.HitHull, e => OnShove(e, 0.25f, 0.012f)));
            _subs.Add(GameEvents.Subscribe(CombatEvents.HitShield, e => OnShove(e, 0.10f, 0.004f)));
            _subs.Add(GameEvents.Subscribe(CombatEvents.Collision, e => { if (e.Entity == World.LocalPlayerShip || e.Other == World.LocalPlayerShip) Jolt(0.8f, "collision"); }));
            _subs.Add(GameEvents.Subscribe(CombatEvents.Explosion, OnExplosion));
        }

        public void SetInput(ICabinInput input)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            if (_input is ScriptedCabinInput sc) sc.Bind(this);
        }

        public void Dispose()
        {
            foreach (var s in _subs) s.Dispose();
            _subs.Clear();
            if (World.Cabin == Board) World.Cabin = null;
        }

        // ------------------------------------------------------------------------------------------------ time

        /// <summary>The sim's tick.</summary>
        public void Step(in SimFrame frame) => Advance(frame.Dt, frame.Time);

        /// <summary>One step of <paramref name="dt"/> seconds at cabin time <paramref name="now"/> (the sim's clock, or the rig's own before launch).</summary>
        public void Advance(float dt, float now)
        {
            _now = now;
            var inp = _input.Sample(dt);
            Board.EyePrev = Board.EyeNow; Board.RotPrev = Board.RotNow;
            StepBody(dt, in inp);
            StepActions(dt, in inp);
            StepScans(dt);
            StepLights(dt);
            StepJolt(dt);
            Publish(false);
        }

        // ------------------------------------------------------------------------------------------------ the pilot's body

        // Where the pilot is, in cabin space (x right, z forward), the way they face (degrees; 0 is forward, 90 starboard) and the eye.
        Vector2 _pos, _vel;
        float _yaw, _pitch, _floorY, _floorVel, _stride, _phaseT, _standHold, _bobT;
        Vector3 _eye;
        // The pose the pilot had when they began to sit down (the path is blended from it).
        Vector3 _lowerFromEye; float _lowerFromYaw, _lowerFromPitch;
        Vector2 _lowerFromPos;

        public Vector2 Position2 => _pos;
        /// <summary>How fast the pilot is walking, 0..1 of the walking pace.</summary>
        public float WalkSpeed01 => Mathf.Clamp01(_vel.magnitude / Mathf.Max(0.01f, Cfg.body.walkSpeed));
        /// <summary>Seconds into the action under way.</summary>
        public float ActionSeconds => _actT;
        public float Yaw => _yaw;
        public float Pitch => _pitch;
        public Vector3 EyeCabin => _eye;
        public Vector2 SeatStandPoint() => new Vector2(Layout.StandAt.x, Layout.StandAt.z);
        public bool AtSeat => (_pos - SeatStandPoint()).magnitude < 0.55f;
        public Vector3 CabinOrigin => Cfg.ShipEye - Layout.Eye;
        public Vector3 LookDir => Quaternion.Euler(-_pitch, _yaw, 0f) * Vector3.forward;
        /// <summary>The eye's height above the floor right now: standing, or lower at a panel.</summary>
        public float EyeHeightNow => Cfg.body.eyeStand;

        void ResetBody()
        {
            Board.State = CabinBoard.Phase.Seated;
            Board.Stand01 = 0f;
            _pos = SeatStandPoint(); _yaw = 0f; _pitch = 0f; _vel = Vector2.zero;
            _floorY = Layout.StandAt.y; _phaseT = 0f; _standHold = 0f;
            _eye = Layout.Eye;
            Board.EyeNow = Board.EyePrev = Cfg.ShipEye;
            Board.RotNow = Board.RotPrev = Quaternion.identity;
        }

        void StepBody(float dt, in CabinInputFrame inp)
        {
            var b = Cfg.body; var t = Cfg.timing;
            switch (Board.State)
            {
                case CabinBoard.Phase.Seated:
                    _eye = Layout.Eye; _yaw = 0f; _pitch = 0f;
                    if (Frozen) { _standHold = 0f; break; }
                    if (inp.StandHeld && CanStand()) _standHold += dt; else _standHold = 0f;
                    if (_standHold >= t.standHoldSeconds) BeginRise();
                    if (inp.MapToggle) { Board.MapOpen = !Board.MapOpen; Board.Version++; }
                    if (Board.Prelaunch && inp.Launch) { Launched?.Invoke(); Prelaunch = false; }
                    break;
                case CabinBoard.Phase.Rising:
                    _phaseT += dt;
                    float s = Mathf.Clamp01(_phaseT / t.riseSeconds);
                    PathPose(s, out _eye, out _yaw, out _pitch);
                    Board.Stand01 = Ease(s);
                    if (s >= 1f)
                    {
                        Board.State = CabinBoard.Phase.Standing;
                        _pos = SeatStandPoint(); _yaw = Layout.StandYaw; _pitch = 0f; _floorY = Layout.StandAt.y; _vel = Vector2.zero;
                        GameEvents.Raise(CabinEvents.Stand, "stand", 1f);
                    }
                    break;
                case CabinBoard.Phase.Standing:
                    Board.Stand01 = 1f;
                    StepStanding(dt, in inp);
                    break;
                case CabinBoard.Phase.Lowering:
                    _phaseT += dt;
                    float k = 1f - Mathf.Clamp01(_phaseT / t.lowerSeconds);
                    PathPose(k, out var pe, out float py, out float pp);
                    float blend = k * k * k;
                    _eye = pe + (_lowerFromEye - PathEnd) * blend;
                    _yaw = py + Mathf.DeltaAngle(Layout.StandYaw, _lowerFromYaw) * blend;
                    _pitch = pp + _lowerFromPitch * blend;
                    Board.Stand01 = Ease(k);
                    if (k <= 0f)
                    {
                        Board.State = CabinBoard.Phase.Seated;
                        _eye = Layout.Eye; _yaw = 0f; _pitch = 0f; Board.Stand01 = 0f; _standHold = 0f;
                        GameEvents.Raise(CabinEvents.Stand, "sit", 1f);
                        OnSeated();
                    }
                    break;
            }
        }

        bool CanStand() => File == null || File.Cabin.StandAnytime || _callOutOpen || _prelaunch;

        public bool Frozen;      // the scene holds the pilot (a debrief, a banner)

        void BeginRise()
        {
            Board.State = CabinBoard.Phase.Rising;
            _phaseT = 0f; _standHold = 0f;
            Board.MapOpen = false;
            GameEvents.Raise(CabinEvents.Stand, "stand", 0f);
            ClearCallOut();
        }

        void BeginLower()
        {
            Board.State = CabinBoard.Phase.Lowering;
            _phaseT = 0f; _standHold = 0f;
            _lowerFromYaw = _yaw; _lowerFromPitch = _pitch; _lowerFromPos = _pos;
            _lowerFromEye = _eye;
            _act = Act.None;
            GameEvents.Raise(CabinEvents.Stand, "sit", 0f);
        }

        Vector3 PathEnd => new Vector3(Layout.StandAt.x, Layout.StandAt.y + Cfg.body.eyeStand, Layout.StandAt.z);

        /// <summary>The way out of the seat (s = 1 standing behind it, s = 0 seated): up first, clear of the head rest, then back and round to face the cabin.</summary>
        void PathPose(float s, out Vector3 eye, out float yaw, out float pitch)
        {
            Vector3 e0 = Layout.Eye, e1 = PathEnd;
            float up = Smoother(Mathf.Clamp01(s / 0.6f));
            float back = Smoother(Mathf.Clamp01((s - 0.28f) / 0.72f));
            eye = new Vector3(Mathf.Lerp(e0.x, e1.x, back), Mathf.Lerp(e0.y, e1.y, up), Mathf.Lerp(e0.z, e1.z, back));
            yaw = Mathf.Lerp(0f, Layout.StandYaw, Smoother(Mathf.Clamp01((s - 0.2f) / 0.75f)));
            pitch = 7f * Mathf.Sin(Mathf.PI * s) * (1f - s);
        }

        static float Smoother(float x) => x * x * x * (x * (x * 6f - 15f) + 10f);
        static float Ease(float x) => x * x * (3f - 2f * x);

        void StepStanding(float dt, in CabinInputFrame inp)
        {
            var b = Cfg.body;
            // Look.
            bool locked = _act == Act.Repair || _act == Act.Breaker;
            _yaw = Mathf.Repeat(_yaw + inp.Look.x * b.lookYawRate * dt + 180f, 360f) - 180f;
            _pitch = Mathf.Clamp(_pitch + inp.Look.y * b.lookPitchRate * dt, -b.pitchMax, b.pitchMax);
            // Walk (not while an action is under way).
            Vector2 want = Vector2.zero;
            if (!locked && _act != Act.Rack && !Frozen)
            {
                float yr = _yaw * Mathf.Deg2Rad;
                Vector2 fwd = new Vector2(Mathf.Sin(yr), Mathf.Cos(yr)), right = new Vector2(Mathf.Cos(yr), -Mathf.Sin(yr));
                float fz = inp.Move.y >= 0f ? b.walkSpeed : b.backSpeed;
                want = fwd * inp.Move.y * fz + right * inp.Move.x * b.strafeSpeed;
            }
            _vel = Vector2.Lerp(_vel, want, 1f - Mathf.Exp(-b.accel * dt));
            Vector2 step = _vel * dt;
            Vector2 from = _pos;
            if (step.sqrMagnitude > 1e-10f)
            {
                var np = _pos + step;
                if (Layout.Walkable(np.x, np.y, b.radius)) _pos = np;
                else if (Layout.Walkable(np.x, _pos.y, b.radius)) { _pos.x = np.x; _vel.y *= 0.2f; }
                else if (Layout.Walkable(_pos.x, np.y, b.radius)) { _pos.y = np.y; _vel.x *= 0.2f; }
                else _vel = Vector2.zero;
            }
            // The floor under the feet (steps ease), the walk's bob.
            float fy = Layout.FloorAt(_pos.x, _pos.y);
            if (float.IsNaN(fy)) fy = _floorY;
            float before = _floorY;
            _floorY = Mathf.SmoothDamp(_floorY, fy, ref _floorVel, b.stepSmooth, 10f, dt);
            float moved = (_pos - from).magnitude;
            _stride += moved;
            if (_stride >= b.strideMeters) { _stride -= b.strideMeters; GameEvents.Raise(CabinEvents.Step, "tread", Mathf.Clamp01(_vel.magnitude / b.walkSpeed)); }
            _bobT += moved / b.strideMeters * Mathf.PI;
            float speed01 = Mathf.Clamp01(_vel.magnitude / b.walkSpeed);
            float bob = Mathf.Abs(Mathf.Sin(_bobT)) * b.bobHeight * speed01;
            float side = Mathf.Sin(_bobT) * b.bobSide * speed01;
            float eh = b.eyeStand;
            Vector3 right3 = new Vector3(Mathf.Cos(_yaw * Mathf.Deg2Rad), 0f, -Mathf.Sin(_yaw * Mathf.Deg2Rad));
            _eye = new Vector3(_pos.x, _floorY + eh - bob, _pos.y) + right3 * side;
            // Back in the seat: hold the stand button within reach of it.
            if (inp.StandHeld && AtSeat && _act == Act.None && !Frozen) { _standHold += dt; if (_standHold >= Cfg.timing.standHoldSeconds) BeginLower(); }
            else _standHold = 0f;
        }

        /// <summary>For scripts: turn toward a yaw and pitch (degrees); true once within a degree and a half.</summary>
        public bool AimToward(float yaw, float pitch, float dt, ref CabinInputFrame f)
        {
            float ey = Mathf.DeltaAngle(_yaw, yaw), ep = pitch - _pitch;
            f.Look.x = Mathf.Clamp(ey / 25f, -1f, 1f);
            f.Look.y = Mathf.Clamp(ep / 25f, -1f, 1f);
            return Mathf.Abs(ey) < 1.5f && Mathf.Abs(ep) < 1.5f;
        }

        void Publish(bool force)
        {
            var b = Board;
            Vector3 ship = Cfg.ShipEye + (_eye - Layout.Eye);
            b.EyeNow = ship;
            b.RotNow = Quaternion.Euler(-_pitch, _yaw, 0f);
            if (force) { b.EyePrev = b.EyeNow; b.RotPrev = b.RotNow; }
            b.OwnsCamera = b.State != CabinBoard.Phase.Seated;
            b.Fov = b.Stand01 > 0f ? Cfg.body.fovStanding : 0f;
            b.Prelaunch = _prelaunch;
        }

        bool _prelaunch;
        public bool Prelaunch
        {
            get => _prelaunch;
            set { _prelaunch = value; Board.Prelaunch = value; Board.Version++; }
        }

        /// <summary>Start the mission with the pilot already up, standing at the plan table (a mission that starts in the cabin) looking down at it.</summary>
        public void StartAtTable()
        {
            var t = Layout.Find("table");
            Board.State = CabinBoard.Phase.Standing;
            Board.Stand01 = 1f;
            _pos = t != null ? t.Stand : SeatStandPoint();
            _yaw = t != null ? t.FaceYaw : 180f;
            _pitch = 28f;
            _vel = Vector2.zero;
            float fy = Layout.FloorAt(_pos.x, _pos.y);
            _floorY = float.IsNaN(fy) ? Layout.StandAt.y : fy;
            _eye = new Vector3(_pos.x, _floorY + Cfg.body.eyeStand, _pos.y);
            _ev = Cfg.exposure.lit;
            Board.HudCollapse = 1f;
            Board.ExposureShift = _ev - Cfg.exposure.seated;
            Board.AmbientScale = Cfg.light.ambientScale;
            Publish(true);
            Board.Version++;
        }

        /// <summary>The pilot's weapons-ready: the seat's confirm button (so a scene can read it as launched).</summary>
        void OnSeated()
        {
            Board.MapOpen = false;
        }
    }
}
