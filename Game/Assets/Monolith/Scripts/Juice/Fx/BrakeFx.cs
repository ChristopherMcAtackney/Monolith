using Monolith.Core;
using UnityEngine;

namespace Monolith.Juice
{
    /// <summary>
    /// The retro brake (R3), the boost's mirror image. The flight model does the stopping (data: <c>brakeDecel</c> in Resources/Ships/vanguard_fighter.json);
    /// this is what it feels like. It watches the player's ship and, when the brake goes on at speed, turns it into:
    /// <list type="bullet">
    /// <item><b>The lurch</b>: the head thrown forward against the harness (a push along the view and a nod, a spring that overshoots once and settles into a lean
    /// while the retros fire; <see cref="JuiceContext.LurchShift"/>, which <see cref="CameraShake"/> applies after the view has placed the camera).</item>
    /// <item><b>The view narrowing</b>: the boost's field-of-view kick in reverse. A quick overshoot on the click, a few degrees held while the retros fire, easing
    /// back when the ship is stopped (<see cref="GameWorld.FovKick"/>, written after the boost has written it, so this goes below zero).</item>
    /// <item><b>The flare</b>: two retro jets at the sides of the nose, seen from the cockpit, firing forward; a bright flash on the click that dies down as the
    /// speed does.</item>
    /// <item><b>The body</b>: a hard camera shake and a heavy pad thump on the click, then a steady rumble while the retros fire.</item>
    /// <item><b>The sound</b>: a thump as the valves slam open and a hiss of the jets, through <see cref="IJuiceAudio"/>.</item>
    /// </list>
    /// Only a click at speed gets all of it: braking while crawling is a quiet latch (a small pulse). The click that lets go gives a soft pulse, no more.
    /// Every effect has its own level and a master intensity in <see cref="JuiceSettings.BrakeSettings"/>.
    /// </summary>
    public sealed class BrakeFx
    {
        /// <summary>Below this forward speed (m/s) the click is a quiet latch, not a hard stop.</summary>
        public const float HardStopSpeed = 8f;
        /// <summary>The retro jets' places on the ship (x is mirrored): beside the nose, a little below the eye, ahead of it. Ship axes.</summary>
        public static readonly Vector3 Nozzle = new Vector3(1.45f, -0.35f, 5.2f);

        readonly JuiceContext _c;
        bool _seen, _on;
        float _k;                    // 0..1: how fully the braking shows; rises fast, falls with the speed
        float _since = 99f;          // seconds since the click that began a hard stop
        float _power;                // how hard that stop was (speed / cruise speed)
        float _x, _v;                // the lurch spring: metres forward of rest, and how fast it moves
        float _flash;                // the retro ignition's flash, 1 on the click, gone in a quarter of a second
        float _speed01;

        // Read by tests and the panel.
        public float Amount => _k;
        public float FovKick { get; private set; }
        public float Lurch => _x;
        public float Flash => _flash;
        public bool Engaged => _on;
        public int Brakes { get; private set; }

        public BrakeFx(JuiceContext c) { _c = c; }

        public void Clear()
        {
            _on = false; _k = 0f; _since = 99f; _x = _v = 0f; _flash = 0f; FovKick = 0f;
            _c.LurchShift = Vector3.zero; _c.LurchNodDeg = 0f;
        }

        // ------------------------------------------------------------------------------------------------ the frame

        /// <summary>Once per rendered frame, with the effect clock's step, after the boost has written the field-of-view kick.</summary>
        public void Update(float dt)
        {
            var s = _c.Settings.brake;
            var w = _c.World;
            var me = _c.Player;
            bool has = me != null;
            bool on = has && me.BrakeOn;
            float speed = has ? Mathf.Max(0f, me.ForwardSpeed) : 0f;
            float cruise = has ? Mathf.Max(1f, me.TopSpeed) : 70f;
            _speed01 = Mathf.Clamp01(speed / cruise);
            bool master = has && s.enabled && s.intensity > 0.001f;
            _since += dt;

            if (!_seen) { _seen = true; _on = on; }          // the first frame is not a click
            if (!master)
            {
                _on = on; _k = 0f; _x = _v = 0f; _flash = 0f; FovKick = 0f;
                _c.LurchShift = Vector3.zero; _c.LurchNodDeg = 0f;
                return;
            }

            float I = s.intensity;
            float sound = Mathf.Clamp(s.sound * (0.6f + 0.4f * I), 0f, 1.6f);
            Vector3 earPos = _c.CameraPosition;
            bool braking = on && me.Braking;

            // ---- edges
            if (on && !_on)
            {
                if (speed > HardStopSpeed)
                {
                    _since = 0f; _power = Mathf.Clamp(speed / cruise, 0.25f, 1.6f); _flash = 1f; Brakes++;
                    _c.Trauma.Add(0.5f * Mathf.Min(_power, 1.2f) * s.shake);
                    _c.Rumble.Pulse(0.85f * s.rumble, 0.50f * s.rumble, 0.32f);
                    _v += 5.4f * _power * s.lurch * I;
                    if (sound > 0.01f)
                    {
                        float g = sound * Mathf.Clamp(0.45f + 0.55f * _power, 0f, 1.3f);
                        _c.Audio.Play(JuiceCue.BrakeThump, earPos, g);
                        _c.Audio.Play(JuiceCue.BrakeHiss, earPos, g * 0.9f);
                    }
                }
                else _c.Rumble.Pulse(0.14f * s.rumble, 0.06f * s.rumble, 0.10f);              // a latch at a crawl: felt, not heard
            }
            else if (!on && _on)
            {
                _c.Rumble.Pulse(0.26f * s.rumble, 0.10f * s.rumble, 0.20f);                 // the brake lets go and the engines take the ship back
                _c.Trauma.Add(0.06f * s.shake);
            }
            _on = on;

            // ---- the amount
            float target = braking ? Mathf.Clamp01(0.3f + 0.7f * _speed01) : 0f;
            float tau = braking ? 0.06f : 0.35f;
            _k += (target - _k) * (1f - Mathf.Exp(-dt / tau));
            if (_k < 0.0005f) _k = 0f;
            float ease = Mathf.SmoothStep(0f, 1f, _k);
            float punch = _since < 3f ? BoostFx.PunchEnvelope(_since) : 0f;
            _flash = Mathf.Max(0f, _flash - dt / 0.25f);

            // ---- the view: the boost's kick in reverse
            float fov = -(s.fovHold * ease + s.fovPunch * punch * Mathf.Min(_power, 1f)) * I;
            w.FovKick += fov;
            FovKick = fov;

            // ---- the lurch: a spring thrown forward by the click, settling into a lean while the retros fire
            float lean = 0.05f * s.lurch * I * (braking ? _speed01 : 0f);
            const float omega = 2f * Mathf.PI * 2.8f, zeta = 0.5f;
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.012f));
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                _v += (-omega * omega * (_x - lean) - 2f * zeta * omega * _v) * h;
                _x += _v * h;
            }
            if (Mathf.Abs(_x) < 1e-4f && Mathf.Abs(_v) < 1e-3f && lean <= 0f) { _x = 0f; _v = 0f; }
            _c.LurchShift = new Vector3(0f, -0.25f * _x, _x);
            _c.LurchNodDeg = _x * 9f;

            // ---- the body: a steady rumble and a faint tremble while the retros fire
            if (braking)
            {
                float r = s.rumble * I * (0.35f + 0.65f * _speed01);
                _c.Rumble.Sustain(0.22f * r, 0.09f * r);
                _c.Trauma.Sustain(0.07f * s.shake * I * _speed01);
            }
        }

        /// <summary>Queue the retro jets (called while the frame's quads are being written): two flames beside the nose, firing forward, seen from the cockpit.</summary>
        public void Draw()
        {
            var s = _c.Settings.brake;
            if (!s.enabled || s.flare <= 0.01f || (_k < 0.01f && _flash < 0.01f)) return;
            var me = _c.Player;
            if (me == null || !_c.Pose(me.Id, out Vector3 pos, out Quaternion rot)) return;
            var add = _c.Add;
            float bright = _c.Bright;
            float size = s.flare * Mathf.Clamp(s.intensity, 0.25f, 2f);
            float hot = Mathf.Clamp01(_k * (0.35f + 0.65f * _speed01) + 0.9f * _flash);
            if (hot < 0.01f) return;
            float flash = _flash * _flash;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 nozzle = pos + rot * new Vector3(Nozzle.x * side, Nozzle.y, Nozzle.z);
                Vector3 fwd = rot * new Vector3(0.22f * side, -0.03f, 1f).normalized;
                float halfW = 0.06f * size * (0.6f + 0.4f * hot);
                float len = (2.2f + 4.5f * hot) * size;
                // The jet: bright at the nozzle, thinning out ahead of the ship.
                Vector3 bc = new Vector3(0.55f, 0.78f, 1.0f) * (3.2f * hot * bright);
                add.Quad(nozzle + fwd * (len * 0.5f), new Vector4(bc.x, bc.y, bc.z, 0.8f * hot * bright), FxKind.Streak, 0.37f + 0.11f * side,
                         new Vector4(-fwd.x, -fwd.y, -fwd.z, len * 0.5f), halfW, 0f, 1.35f, 0.15f, 0f, 0.6f, 2.4f);
                // The glow at the nozzle, and a hard flash for the first quarter second.
                Vector3 gc = new Vector3(0.62f, 0.82f, 1.0f) * (2.4f * hot * bright);
                add.Quad(nozzle, new Vector4(gc.x, gc.y, gc.z, 0f), FxKind.Glow, 0.43f, Vector4.zero, halfW * (3.2f + 3.5f * flash), 0f, 0.65f, 0f, 0f, 0.6f, 2.4f);
                Vector3 fc = new Vector3(0.80f, 0.90f, 1.0f) * (2.0f * flash * bright);
                if (flash > 0.01f)
                    add.Quad(nozzle + fwd * (halfW * 2f), new Vector4(fc.x, fc.y, fc.z, 0f), FxKind.Flare, 0.55f, new Vector4(halfW * 0.9f, 0, 0, 0),
                             halfW * (7f + 9f * flash), 0f, 0f, 0f, 0f, 0.6f, 2.4f);
            }
        }
    }
}
