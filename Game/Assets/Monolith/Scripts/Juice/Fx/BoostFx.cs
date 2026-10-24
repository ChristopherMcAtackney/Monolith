using System.Collections.Generic;
using System.IO;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Juice
{
    /// <summary>
    /// The boost (the afterburner). The flight model gives the player's fighter its speed (data: Resources/Ships/vanguard_fighter.json);
    /// this is what the speed feels like. It watches the player's ship and turns its burner state into:
    /// <list type="bullet">
    /// <item><b>The view widening</b>: the field of view kicks out on ignition (a quick overshoot), holds wider while the burner burns and
    /// eases back on release (<see cref="GameWorld.FovKick"/>, which the cockpit view applies). The wide look is the cabin's standing view
    /// (68 degrees against the cockpit's 55) and the sky warm-up's wide frames, borrowed on purpose.</item>
    /// <item><b>The body</b>: a camera shake and a pad jolt on ignition, then a low steady rumble while it burns.</item>
    /// <item><b>The speed</b>: the motion dust drawn longer and brighter (<see cref="GameWorld.DustStretch"/>), a radial blur and colour
    /// fringe toward the edges of the picture and a faint engine glow at the rim (<see cref="FxFrame"/>'s boost lens, pass 4 of MN_Fx).
    /// The middle of the picture and a disc round the player's target stay sharp: nothing hides the target, and the HUD draws after
    /// post-processing so it is never touched.</item>
    /// <item><b>The flame</b> on every other ship that burns: a plume that grows over the first half second, with a flash on ignition.</item>
    /// <item><b>The sound</b>: an ignition whoomph through <see cref="IJuiceAudio"/> (the roar loop and the on/off swells belong to the audio
    /// system, which watches the same burner flag), and a sputter when the tank runs dry or the key is pressed on an empty tank.</item>
    /// <item><b>The last of the fuel</b>: the view flutters and the pad stutters as the tank runs low, so the cut-off is never a surprise.</item>
    /// </list>
    /// The player's own ignition is the loudest reaction; every effect has its own level and a master intensity in
    /// <see cref="JuiceSettings.BoostSettings"/>, and the switches for shake, rumble and flashes in the main settings still apply.
    /// </summary>
    public sealed class BoostFx
    {
        /// <summary>FS2 afterburner.cpp: the least fuel the burner engages on (flight_rules.json, afterburnerMinFuel).</summary>
        public const float MinFuelToEngage = 10f;

        /// <summary>Seconds into each cough of the empty-tank sputter (the sound's design in tools/audio/Sfx/SfxShip.cs follows the same beats).</summary>
        public static readonly float[] CoughAt = { 0.00f, 0.17f, 0.38f, 0.66f };
        static readonly float[] CoughTrauma = { 0.30f, 0.22f, 0.15f, 0.08f };

        readonly JuiceContext _c;
        StreamWriter _log;

        bool _on, _key, _seen;
        float _k;               // 0..1: how fully the boost shows. Rises in a tenth of a second, falls over about a second.
        float _sinceIgnite = 99f, _sinceDry = 99f, _sinceDenied = 99f;
        bool _dry;              // the last cut-off was the tank running out
        int _coughsDone;
        float _clock;

        // Read by tests and the panel.
        public float Amount => _k;
        public float FovKick { get; private set; }
        public float Lens { get; private set; }
        public bool Burning => _on;
        public int Ignitions { get; private set; }
        public int Sputters { get; private set; }

        readonly Dictionary<int, float> _burn = new Dictionary<int, float>();
        readonly List<int> _gone = new List<int>();

        public BoostFx(JuiceContext c)
        {
            _c = c;
            string path = CommandLine.Get("-mnBoostLog");
            if (!string.IsNullOrEmpty(path))
            {
                _log = new StreamWriter(path, false);
                _log.WriteLine("time_s,burning,amount,fov_kick_deg,dust_stretch,lens,fuel,speed,trauma");
            }
        }

        public void Dispose() { _log?.Dispose(); _log = null; }

        public void Clear()
        {
            _on = false; _k = 0f; _sinceIgnite = _sinceDry = _sinceDenied = 99f; _coughsDone = 99; _dry = false;
            var w = _c.World;
            w.FovKick = 0f; w.DustStretch = 1f; w.DustBright = 1f;
            ClearLens();
            _burn.Clear();
        }

        void ClearLens()
        {
            var f = _c.Frame;
            f.BoostBlur = 0f; f.BoostChroma = 0f; f.BoostGlow = Vector3.zero;
            FovKick = 0f; Lens = 0f;
        }

        /// <summary>The ignition kick's shape over time: a quick rise, then it settles (0..1).</summary>
        public static float PunchEnvelope(float t) => Mathf.SmoothStep(0f, 1f, t / 0.07f) * Mathf.Exp(-Mathf.Max(0f, t - 0.07f) / 0.32f);

        // ------------------------------------------------------------------------------------------------ the frame

        /// <summary>Once per rendered frame, with the effect clock's step.</summary>
        public void Update(float dt)
        {
            var s = _c.Settings.boost;
            var w = _c.World;
            var me = _c.Player;
            _clock += dt;
            _sinceIgnite += dt; _sinceDry += dt; _sinceDenied += dt;

            bool has = me != null && me.FuelMax > 0f;
            bool on = has && me.AfterburnerOn;
            bool master = has && s.enabled && s.intensity > 0.001f;
            bool key = has && w.Pilots.Current(w.LocalPlayerShip).Held(ControlButtons.Afterburner);
            bool keyEdge = key && !_key;
            _key = key;

            if (!master)
            {
                // Off: the afterburner as it was (the old steady hum), and nothing else.
                if (on) _c.Rumble.Sustain(0.12f, 0.07f);
                w.FovKick = 0f; w.DustStretch = 1f; w.DustBright = 1f;
                ClearLens();
                _on = on; _k = 0f;
                UpdateFlames(dt, false);
                return;
            }

            float I = s.intensity;
            float sound = Mathf.Clamp(s.sound * (0.6f + 0.4f * I), 0f, 1.6f);
            Vector3 earPos = _c.CameraPosition;

            // ---- edges
            if (!_seen) { _seen = true; _on = on; }              // the first frame is not an ignition
            if (on && !_on)
            {
                // Ignition. The loudest reaction there is: it is the player's own act, and it worked.
                _sinceIgnite = 0f; _dry = false; Ignitions++;
                _c.Trauma.Add(0.62f * s.shake);
                _c.Rumble.Pulse(0.80f * s.rumble, 0.46f * s.rumble, 0.30f);
                if (sound > 0.01f) _c.Audio.Play(JuiceCue.BoostWhoomph, earPos, sound);
            }
            else if (!on && _on)
            {
                bool empty = me.Fuel <= 0.5f && key;                 // the tank ran out with the key still down
                if (empty) { StartSputter(sound, 1f); }
                else _c.Rumble.Pulse(0.22f * s.rumble, 0.10f, 0.18f);    // a soft let-go
            }
            else if (keyEdge && !on && me.Fuel < MinFuelToEngage && _sinceDenied > 0.35f)
            {
                // The key pressed on an empty tank: it coughs and nothing lights.
                StartSputter(sound, 0.65f);
                _sinceDenied = 0f;
            }
            _on = on;

            // ---- the amount
            float target = on ? 1f : 0f;
            float tau = on ? 0.09f : (_dry && _sinceDry < 1.5f ? 0.14f : 0.32f);
            _k += (target - _k) * (1f - Mathf.Exp(-dt / tau));
            if (_k < 0.0005f) _k = 0f;
            float ease = Mathf.SmoothStep(0f, 1f, _k);
            float punch = BoostEnv(_sinceIgnite);
            float flash = Mathf.Exp(-_sinceIgnite / 0.20f) * (_sinceIgnite < 5f ? 1f : 0f);

            // ---- the last of the fuel: the view flutters, the pad stutters
            float fuel01 = me.FuelMax > 0f ? me.Fuel / me.FuelMax : 0f;
            float low = on ? Mathf.Clamp01((0.18f - fuel01) / 0.18f) : 0f;
            float flutter = low * (0.5f + 0.5f * Mathf.Sin(_clock * Mathf.PI * 2f * 7.5f));

            // ---- the coughs of the sputter
            float cough = 0f;
            if (_sinceDry < 1.2f)
            {
                for (int i = 0; i < CoughAt.Length; i++)
                {
                    float t = _sinceDry - CoughAt[i];
                    if (t < 0f) continue;
                    if (i >= _coughsDone)
                    {
                        _coughsDone = i + 1;
                        _c.Trauma.Add(CoughTrauma[i] * s.shake * _coughScale);
                        _c.Rumble.Pulse((0.55f - 0.1f * i) * s.rumble * _coughScale, 0.22f * s.rumble * _coughScale, 0.11f);
                    }
                    cough += Mathf.Exp(-t / 0.06f) * (1f - 0.18f * i);
                }
            }

            // ---- the view
            float fov = s.fovHold * ease + s.fovPunch * punch - 1.6f * flutter * ease + 1.4f * cough;
            fov = Mathf.Max(0f, fov * I);
            w.FovKick = fov;
            FovKick = fov;

            // ---- the speed on the screen
            w.DustStretch = 1f + 1.0f * s.streaks * I * (ease + 0.4f * punch);
            w.DustBright = 1f + 0.5f * s.streaks * I * ease;
            float lens = s.lens * I * (0.55f * ease + 0.45f * punch) * (1f - 0.4f * flutter);
            Lens = lens;
            var f = _c.Frame;
            f.BoostBlur = 0.075f * lens;
            f.BoostChroma = 0.0125f * lens;
            f.BoostZone = new Vector4(0.34f, 0.98f, 0f, 0f);
            f.BoostSafe = SafeDisc(me);
            // The engine's light on the rim: bright for an instant at ignition, a faint steady glow while it burns. Respects the flash switch.
            float glow = _c.Settings.effects.screenFlash ? s.flare * I * _c.Bright * (0.022f * ease + 0.15f * flash) : 0f;
            f.BoostGlow = new Vector3(0.45f, 0.70f, 1.0f) * glow;

            // ---- the body: a low steady rumble while it burns (the jolt on ignition is above)
            if (on)
            {
                float r = s.rumble * (0.55f + 0.45f * ease);
                float stutter = 1f - 0.7f * flutter;
                _c.Rumble.Sustain(0.14f * r * stutter, 0.06f * r * stutter);
                _c.Trauma.Sustain(0.045f * s.shake * ease);               // the faintest tremble of the hull at speed
            }

            // ---- other ships' flames
            UpdateFlames(dt, s.flare > 0.01f);

            if (_log != null)
            {
                _log.WriteLine($"{_c.Time:0.000},{(on ? 1 : 0)},{_k:0.000},{fov:0.00},{w.DustStretch:0.000},{lens:0.000},{me.Fuel:0.0},{me.ForwardSpeed:0.0},{_c.Trauma.Value:0.000}");
            }
        }

        float _coughScale = 1f;

        void StartSputter(float sound, float scale)
        {
            _sinceDry = 0f; _coughsDone = 0; _dry = true; _coughScale = scale; Sputters++;
            if (sound > 0.01f) _c.Audio.Play(JuiceCue.BoostSputter, _c.CameraPosition, Mathf.Clamp(sound * scale, 0f, 1.6f));
        }

        /// <summary>The ignition's shape: a quick rise, then it settles (0..1).</summary>
        static float BoostEnv(float t) => t > 3f ? 0f : PunchEnvelope(t);

        /// <summary>The target's place on screen, so the lens leaves a disc round it alone: x, y (0..1, y down), z radius in screen heights.</summary>
        Vector3 SafeDisc(ShipState me)
        {
            var w = _c.World;
            var t = me.Target != GameWorld.NoShip ? w.Ships.Get(me.Target) : null;
            var cam = _c.Camera;
            if (t == null || cam == null) return new Vector3(0.5f, 0.5f, 0f);
            Vector3 v = cam.WorldToViewportPoint(t.RenderPosition(w.Alpha));
            if (v.z < 1f) return new Vector3(0.5f, 0.5f, 0f);
            float perHeight = 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);       // screen heights per (metre / metre)
            float r = Mathf.Max(0.07f, 2.0f * Mathf.Max(t.Radius, 3f) / v.z * perHeight);
            return new Vector3(v.x, 1f - v.y, Mathf.Min(r, 0.5f));
        }

        // ------------------------------------------------------------------------------------------------ other ships' flames

        void UpdateFlames(float dt, bool draw)
        {
            var w = _c.World;
            var ships = w.Ships.All;
            for (int i = 0; i < ships.Count; i++)
            {
                var st = ships[i];
                if (st == null) continue;
                int id = st.Id;
                if (id == w.LocalPlayerShip) continue;
                _burn.TryGetValue(id, out float b);
                float nb = st.AfterburnerOn && draw ? Mathf.Min(1f, b + dt / 0.5f) : Mathf.Max(0f, b - dt / 0.30f);
                if (nb <= 0f) { if (b > 0f || _burn.ContainsKey(id)) _burn.Remove(id); }
                else _burn[id] = nb;
            }
            // Ships that are gone.
            _gone.Clear();
            foreach (var kv in _burn) if (w.Ships.Get(kv.Key) == null) _gone.Add(kv.Key);
            foreach (var id in _gone) _burn.Remove(id);
        }

        /// <summary>Queue the flames of the ships that burn (called while the frame's quads are being written).</summary>
        public void Draw()
        {
            if (_burn.Count == 0) return;
            var s = _c.Settings.boost;
            if (!s.enabled) return;
            var w = _c.World;
            var add = _c.Add;
            float bright = _c.Bright;
            float size = s.flare * Mathf.Clamp(s.intensity, 0.25f, 2f);
            Vector3 cam = _c.CameraPosition;
            foreach (var kv in _burn)
            {
                var st = w.Ships.Get(kv.Key);
                if (st == null) continue;
                float b = kv.Value;
                if (b <= 0.001f) continue;
                Vector3 pos = st.RenderPosition(w.Alpha);
                float dist = Vector3.Distance(pos, cam);
                if (dist > 9000f) continue;
                Quaternion rot = st.RenderRotation(w.Alpha);
                Vector3 fwd = rot * Vector3.forward;
                float R = Mathf.Max(st.Radius, 2f);
                var look = _c.LookOf(kv.Key);
                Vector3 rearLocal = look != null && look.Visual != null
                    ? look.LocalCentre + new Vector3(0f, 0f, -look.HalfExtents.z * 0.96f)
                    : new Vector3(0f, 0f, -R * 0.9f);
                Vector3 nozzle = pos + rot * rearLocal;

                float grow = Mathf.SmoothStep(0f, 1f, b);
                float hot = Mathf.Pow(b, 0.8f);
                // A plume: a long blue-white point trailing behind, a hard hot core, a flare at the nozzle.
                float halfW = Mathf.Clamp(R * 0.14f, 0.45f, 8f) * size;
                float len = R * (1.1f + 5.2f * grow) * size;
                Vector3 bc = new Vector3(0.42f, 0.66f, 1.0f) * (9f * hot * bright);
                add.Quad(nozzle - fwd * (len * 0.5f), new Vector4(bc.x, bc.y, bc.z, 1.3f * hot * bright), FxKind.Streak, 0.31f + 0.07f * kv.Key,
                         new Vector4(fwd.x, fwd.y, fwd.z, len * 0.5f), halfW, 0f, 1.35f, 0.15f, 0f, 30f, 120f);
                Vector3 gc = new Vector3(0.55f, 0.78f, 1.0f) * (16f * hot * bright);
                add.Quad(nozzle, new Vector4(gc.x, gc.y, gc.z, 0f), FxKind.Glow, 0.4f, Vector4.zero, halfW * 5.5f, 0f, 0.65f, 0f, 0f, 30f, 120f);
                Vector3 fc = new Vector3(0.50f, 0.74f, 1.0f) * (5.5f * hot * bright);
                add.Quad(nozzle - fwd * (halfW * 2f), new Vector4(fc.x, fc.y, fc.z, 0f), FxKind.Flare, 0.5f,
                         new Vector4(halfW * 1.1f, 0, 0, 0), halfW * (9f + 10f * grow), 0f, 0f, 0f, 0f, 30f, 120f);
            }
        }
    }
}
