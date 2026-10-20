using System;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Flight
{
    /// <summary>
    /// One ship's flight, FS2's way: every axis chases a goal with a first-order exponential approach instead of forces
    /// and inertia (design section 4; rules in tools/fs2ref/README.md; behaviour read from FS2's physics.cpp, code our own).
    /// <list type="number">
    /// <item>Turn rate: goal = stick x max rate (2 pi / rotation time) per axis, chased with <c>RotDamp</c>. Yaw also asks
    ///   for roll at half the yaw rate the other way (auto-bank).</item>
    /// <item>Speed: goal = throttle (or 1 with the afterburner key) x top speed, or the afterburner speed while it burns;
    ///   ramped with the speed-up or slow-down time constant, and the last sliver closes quickly (FS2's ramp shortcut).</item>
    /// <item>Velocity follows the nose: forward velocity is the ramp speed; sideways and vertical velocity decay with
    ///   <c>SideDamp</c>. The velocity is worked in the frame from before this tick's turn, so turning ships slip a little.</item>
    /// <item>Afterburner key edges act after the move (the physics reacts one tick later, as in FS2); fuel burns while on,
    ///   cuts out at empty, and recharges whenever it is off.</item>
    /// <item>Knocks (<see cref="ShipKnock"/>) change velocity and spin, then loosen the damping for up to ~2 s, fading.</item>
    /// </list>
    /// Pure C#: no Unity objects, so the scenario runner and every pilot (player, AI, script, remote) drive the same code.
    /// </summary>
    public sealed class ShipFlight
    {
        /// <summary>A time constant this small is treated as "no lag" (numerical guard, as FS2's apply_physics does).</summary>
        const float InstantTimeConstant = 1e-4f;

        public readonly ShipClass Class;
        readonly FlightRules _rules;
        readonly Vector3 _maxTurn;
        readonly Vector3 _inertia;
        readonly float _shakePhase;

        public Vector3 Position;
        public Quaternion Rotation = Quaternion.identity;
        /// <summary>World velocity, m/s.</summary>
        public Vector3 Velocity;
        /// <summary>Ship-local turn rates, rad/s: +x nose down, +y nose right, +z roll left (Unity's rotation sense).</summary>
        public Vector3 Spin;
        /// <summary>The forward speed being ramped toward its goal (FS2's prev_ramp_vel.z).</summary>
        public float RampSpeed;
        /// <summary>The throttle setting the last step used (the pilot's, or the matched speed).</summary>
        public float Throttle;
        public bool Matching;
        public float Fuel;
        public bool AfterburnerOn;
        /// <summary>Seconds this ship has been simulated.</summary>
        public float Time;

        float _lastAfterburnerStart = -1e9f;
        float _knockLeft, _shockLeft, _shockStrength;

        public ShipFlight(ShipClass cls, FlightRules rules, int seed = 0)
        {
            Class = cls ?? throw new ArgumentNullException(nameof(cls));
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _maxTurn = cls.MaxTurnRate;
            // Solid box of the ship's size: only used to turn off-centre knocks into spin.
            float w = cls.Size.x, h = cls.Size.y, l = cls.Size.z, m = cls.Mass / 12f;
            _inertia = new Vector3(m * (h * h + l * l), m * (w * w + l * l), m * (w * w + h * h));
            _shakePhase = (seed * 0.61803398f) % 1f * 100f;
            Fuel = cls.HasAfterburner ? cls.AfterburnerFuel : 0f;
        }

        /// <summary>0..1: how much of the knock time is left (1 right after a big hit).</summary>
        public float KnockFraction => Mathf.Clamp01(_knockLeft / _rules.KnockTime);

        /// <summary>Velocity in ship axes (x right, y up, z forward).</summary>
        public Vector3 LocalVelocity => Quaternion.Inverse(Rotation) * Velocity;

        /// <summary>Put the ship somewhere, flying straight along its nose at <paramref name="speed"/> (spawn, respawn, tests).</summary>
        public void Place(Vector3 position, Quaternion rotation, float speed, float throttle)
        {
            Position = position;
            Rotation = rotation;
            Velocity = rotation * new Vector3(0f, 0f, speed);
            RampSpeed = speed;
            Spin = Vector3.zero;
            Throttle = Mathf.Clamp01(throttle);
            Matching = false;
            AfterburnerOn = false;
            _knockLeft = _shockLeft = _shockStrength = 0f;
        }

        /// <summary>
        /// One tick. <paramref name="input"/> is this tick's control, <paramref name="previous"/> the last tick's (for
        /// button edges). <paramref name="targetSpeed"/> is the targeted ship's forward speed, or NaN with no target.
        /// </summary>
        public void Step(in ControlInput input, in ControlInput previous, float targetSpeed, float dt)
        {
            // 1. Controls. Afterburner key edges become start/stop requests that are acted on after the move.
            bool key = Class.HasAfterburner && input.Held(ControlButtons.Afterburner);
            bool keyBefore = Class.HasAfterburner && previous.Held(ControlButtons.Afterburner);
            bool startRequest = key && !keyBefore;
            bool stopRequest = !key && keyBefore;

            bool hasTarget = !float.IsNaN(targetSpeed);
            if (input.Pressed(previous, ControlButtons.MatchSpeed)) Matching = !Matching && hasTarget;
            if (Matching)
            {
                // The pilot moving the throttle cancels it. A pilot that follows the published setting is not moving it.
                float c = _rules.MatchSpeedCancel;
                bool moved = Math.Abs(input.Throttle - previous.Throttle) > c && Math.Abs(input.Throttle - Throttle) > c;
                if (!hasTarget || moved) Matching = false;
            }
            float throttle = Matching && Class.TopSpeed > 0f ? Mathf.Clamp01(targetSpeed / Class.TopSpeed) : Mathf.Clamp01(input.Throttle);
            Throttle = throttle;

            // 2. Goals.
            float forward = Math.Min(1f, throttle + (key ? 1f : 0f));      // the key asks for full speed even when it cannot burn
            if (AfterburnerOn) forward = 1f;
            float pitchDown = -input.Pitch, yawRight = input.Yaw, rollLeft = -input.Roll;
            var goalSpin = new Vector3(
                pitchDown * _maxTurn.x,
                yawRight * _maxTurn.y,
                rollLeft * _maxTurn.z - yawRight * _maxTurn.y * _rules.AutoBankFraction);

            float goalSpeed, speedUp;
            if (AfterburnerOn) { goalSpeed = forward * Class.AfterburnerSpeed; speedUp = Class.AfterburnerAccel; }
            else { goalSpeed = forward * Class.TopSpeed; speedUp = Class.ForwardAccel; }
            goalSpeed = Math.Max(goalSpeed, -Class.ReverseSpeed);
            RampSpeed = Ramp(RampSpeed, goalSpeed, goalSpeed >= RampSpeed ? speedUp : Class.ForwardDecel, dt);

            // 3a. Velocity, in the frame the ship had at the start of the tick.
            float knock = KnockFraction;
            float sideLag = Class.SideDamp * (1f + (_rules.KnockDampFactor - 1f) * knock);
            float forwardLag = Class.SideDamp * _rules.KnockDampFactor * knock;   // 0 normally: forward speed IS the ramp speed
            var local = Quaternion.Inverse(Rotation) * Velocity;
            Vector3 moved3;
            local.x = Chase(local.x, 0f, sideLag, dt, out moved3.x);
            local.y = Chase(local.y, 0f, sideLag, dt, out moved3.y);
            local.z = Chase(local.z, RampSpeed, forwardLag, dt, out moved3.z);
            Position += Rotation * moved3;
            Velocity = Rotation * local;

            // 3b. Rotation: chase the goal rates, then turn by the new rates over the tick.
            float shock = _rules.KnockTime > 0f ? Mathf.Clamp01(_shockLeft / _rules.KnockTime) : 0f;
            float turnLag = Class.RotDamp * (1f + (_rules.ShockTurnDampFactor - 1f) * shock);
            Spin.x = Chase(Spin.x, goalSpin.x, turnLag, dt, out _);
            Spin.y = Chase(Spin.y, goalSpin.y, turnLag, dt, out _);
            Spin.z = Chase(Spin.z, goalSpin.z, turnLag, dt, out _);
            if (shock > 0f) Spin += Shake(Time) * (_rules.ShockShake * _shockStrength * shock);
            CapSpin();
            Rotation = Normalized(Rotation * Quaternion.Euler(Spin * (dt * Mathf.Rad2Deg)));
            Time += dt;

            // 4. Afterburner edges, after the move.
            if (startRequest && !AfterburnerOn && Fuel >= _rules.AfterburnerMinFuel &&
                Time - _lastAfterburnerStart >= _rules.AfterburnerRestartLockout - 1e-4f)
            {
                AfterburnerOn = true;
                _lastAfterburnerStart = Time;
            }
            if (stopRequest) AfterburnerOn = false;

            // 5. Fuel: burns while on; at empty the burner drops even with the key held; refills whenever it is off.
            if (Class.HasAfterburner)
            {
                if (!AfterburnerOn)
                    Fuel = Math.Min(Class.AfterburnerFuel, Fuel + Class.AfterburnerRechargeRate * _rules.AfterburnerRechargeScale * dt);
                else if (Fuel <= 0f) { Fuel = 0f; AfterburnerOn = false; }
                else Fuel = Math.Max(0f, Fuel - Class.AfterburnerBurnRate * dt);
            }

            _knockLeft = Math.Max(0f, _knockLeft - dt);
            _shockLeft = Math.Max(0f, _shockLeft - dt);
            if (_shockLeft <= 0f) _shockStrength = 0f;
        }

        /// <summary>
        /// A shove (hit, collision, shockwave): velocity changes by impulse / mass, an off-centre impulse adds spin, and the
        /// damping loosens for up to <c>KnockTime</c> in proportion to the speed change, fading out. The speed ramp restarts
        /// from the shoved forward speed, so the engines pull the ship back to its set speed on the normal constants.
        /// </summary>
        public void ApplyKnock(in ShipKnock k)
        {
            var dv = k.Impulse / Class.Mass;
            Velocity += dv;
            var localImpulse = Quaternion.Inverse(Rotation) * k.Impulse;
            var torque = Vector3.Cross(k.Point, localImpulse);
            Spin += new Vector3(torque.x / _inertia.x, torque.y / _inertia.y, torque.z / _inertia.z) * _rules.KnockSpin;
            CapSpin();
            float t = _rules.KnockTime * Math.Min(1f, dv.magnitude / _rules.KnockFullSpeedChange);
            _knockLeft = Math.Min(_rules.KnockTime, _knockLeft + t);
            RampSpeed = LocalVelocity.z;
            if (k.Shock > 0f)
            {
                float fading = _rules.KnockTime > 0f ? _shockStrength * Mathf.Clamp01(_shockLeft / _rules.KnockTime) : 0f;
                _shockStrength = Math.Max(Mathf.Clamp01(k.Shock), fading);
                _shockLeft = _rules.KnockTime;
            }
        }

        /// <summary>Speed ramp with FS2's late shortcut: in the last sliver the time constant shrinks with the gap.</summary>
        float Ramp(float v, float goal, float timeConstant, float dt)
        {
            float gap = Math.Abs(goal - v);
            if (gap < timeConstant / _rules.RampShortcutDivisor) timeConstant = gap / _rules.RampShortcutDivisor;
            if (timeConstant < _rules.RampSnapTimeConstant) return goal;
            return v + (goal - v) * (float)(1.0 - Math.Exp(-dt / timeConstant));
        }

        /// <summary>Closed-form exponential approach over dt; also returns the distance covered if v is a speed.</summary>
        static float Chase(float v, float goal, float timeConstant, float dt, out float travelled)
        {
            if (timeConstant < InstantTimeConstant)
            {
                travelled = goal * dt;
                return goal;
            }
            double decay = Math.Exp(-dt / timeConstant);
            double gap = v - goal;
            travelled = (float)((1.0 - decay) * gap * timeConstant + goal * dt);
            return (float)(goal + gap * decay);
        }

        void CapSpin()
        {
            float m = Spin.magnitude;
            if (m > _rules.MaxTurnRate) Spin *= _rules.MaxTurnRate / m;
        }

        /// <summary>Smooth, repeatable shake for pitch and yaw (no random numbers, so runs replay exactly).</summary>
        Vector3 Shake(float t)
        {
            float p = _shakePhase;
            return new Vector3(
                0.6f * Mathf.Sin(t * 23.1f + p) + 0.4f * Mathf.Sin(t * 41.7f + 1.3f * p),
                0.6f * Mathf.Sin(t * 19.3f + 2.1f * p) + 0.4f * Mathf.Sin(t * 37.9f + 0.7f * p),
                0f);
        }

        static Quaternion Normalized(Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return m > 0f ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : Quaternion.identity;
        }
    }
}
