using System;
using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Juice
{
    /// <summary>
    /// Ship deaths and blasts, layered and scaled by size, with the physics of vacuum: a fast flash, a fireball that swells and dims
    /// quickly, glowing debris, venting gas, no lingering smoke. A fighter is one blast and a handful of lit pieces; a bomber
    /// goes off twice; a capital ship dies in stages (internal blasts that build, the hull breaking into sections, then a
    /// final flash, a shockwave and the sections shattering). Each is also felt: shake, rumble, sound, and a shove for ships nearby.
    /// </summary>
    public sealed class Explosions
    {
        readonly JuiceContext _c;
        readonly Emitters _fx;
        readonly Feel _feel;
        readonly Debris _debris;
        readonly Shockwaves _waves;
        readonly List<CapitalDeath> _deaths = new List<CapitalDeath>();
        readonly List<KeyValuePair<float, Action>> _later = new List<KeyValuePair<float, Action>>();

        public int ActiveDeaths => _deaths.Count;

        public Explosions(JuiceContext c, Emitters fx, Feel feel, Debris debris, Shockwaves waves)
        {
            _c = c; _fx = fx; _feel = feel; _debris = debris; _waves = waves;
        }

        void Later(float seconds, Action a) => _later.Add(new KeyValuePair<float, Action>(_c.Time + seconds, a));

        // ------------------------------------------------------------------------------------------------ events

        public void OnShipDestroyed(in GameEvent e)
        {
            var ship = _c.World.Ships.Get(e.Entity);
            Vector3 pos = e.HasPosition ? e.Position : (ship != null ? ship.Position : _c.CameraPosition + _c.Camera.transform.forward * 300f);
            Vector3 vel = ship != null ? ship.Velocity : Vector3.zero;
            string size = string.IsNullOrEmpty(e.Subject) ? _c.SizeClassOf(e.Entity, e.Value) : e.Subject;
            bool success = _c.IsPlayer(e.Other) && e.Other != GameWorld.NoShip;
            var look = _c.LookOf(e.Entity);
            Transform visual = look != null ? look.Visual : null;

            // Whatever was shooting at it should stop: the bolts in flight are not ours to hold, but a shield bubble must go.
            if (look != null && look.Shield != null) look.Shield = null;

            switch (size)
            {
                case CombatEvents.SizeBomber: Bomber(pos, vel, visual, success, e); break;
                case CombatEvents.SizeCorvette:
                case CombatEvents.SizeCapital:
                    if (visual != null || size == CombatEvents.SizeCapital) Capital(pos, vel, visual, look, size == CombatEvents.SizeCorvette ? 0.5f : 1f, success, e);
                    else Fighter(pos, vel, visual, success, e);
                    break;
                default: Fighter(pos, vel, visual, success, e); break;
            }
            if (look != null) { look.Visual = null; }
            if (ship != null && look == null) _c.Looks.Remove(e.Entity);
        }

        public void OnExplosion(in GameEvent e)
        {
            float radius = e.Value > 1f ? e.Value : (e.Subject == "large" ? 90f : e.Subject == "medium" ? 40f : 16f);
            Vector3 pos = e.HasPosition ? e.Position : _c.CameraPosition + _c.Camera.transform.forward * 250f;
            Blast(pos, Vector3.zero, radius, 1f, JuiceCue.ExplosionMedium, radius > 60f);
            _feel.Impact(pos, Mathf.Clamp(radius / 120f, 0.15f, 0.8f), 200f + radius * 8f, 0.4f, 0.25f, 0.3f + radius * 0.004f);
        }

        public void OnSubsystemDestroyed(in GameEvent e)
        {
            float radius = e.Value > 1f ? e.Value : 14f;
            Vector3 pos = e.Position;
            bool success = _c.IsPlayer(e.Other) && e.Other != GameWorld.NoShip;
            Blast(pos, Vector3.zero, radius * 1.1f, 1.15f, JuiceCue.SubsystemBlast, false);
            Vector3 n = e.Direction.sqrMagnitude > 0.5f ? e.Direction.normalized : Vector3.up;
            _fx.Vent(pos, n, radius * 0.25f, 1.6f, 55f);
            _fx.Scorch(pos, radius * 0.9f, 30f);
            _feel.Impact(pos, 0.38f, 300f, 0.55f, 0.35f, 0.4f, success);
            if (success) _feel.MaybeHitStop(0.5f);
        }

        // ------------------------------------------------------------------------------------------------ one blast with sound

        void Blast(Vector3 pos, Vector3 vel, float radius, float intensity, JuiceCue cue, bool plasma)
        {
            _fx.Blast(pos, vel, radius, intensity, plasma);
            float dist = _c.DistanceToCamera(pos);
            _c.Audio.Play(cue, pos, Mathf.Clamp(0.55f + radius / 150f, 0.55f, 1f), dist);
        }

        // ------------------------------------------------------------------------------------------------ fighters and bombers

        void Fighter(Vector3 pos, Vector3 vel, Transform visual, bool success, in GameEvent e)
        {
            float R = 30f;
            Blast(pos, vel, R, 1f, JuiceCue.ExplosionSmall, false);
            _fx.Sparks(pos, vel, Vector3.up, 360f, _c.N(26), 80f, 300f, 0.5f, 1.5f, 0.12f, SparkStyle.Metal, 0.4f);
            Pieces(visual, pos, vel, 5, 22f, 85f, 1.7f, false, 1.2f);
            _feel.Impact(pos, 0.30f, 260f, 0.5f, 0.32f, 0.32f, success);
            if (success) _feel.MaybeHitStop(0.5f);
        }

        void Bomber(Vector3 pos, Vector3 vel, Transform visual, bool success, in GameEvent e)
        {
            float R = 56f;
            Blast(pos, vel, R * 0.55f, 0.9f, JuiceCue.ExplosionSmall, false);
            Vector3 shipFwd = visual != null ? visual.forward : Vector3.forward;
            Vector3 second = pos + shipFwd * R * 0.6f;
            Later(0.17f, () => { Blast(second, vel, R, 1.1f, JuiceCue.ExplosionMedium, false); _fx.Vent(second, shipFwd, 4f, 1.2f, 50f); });
            Pieces(visual, pos, vel, 7, 20f, 75f, 2.1f, false, 1.2f);
            _fx.Sparks(pos, vel, Vector3.up, 360f, _c.N(44), 90f, 340f, 0.5f, 1.7f, 0.14f, SparkStyle.Metal, 0.4f);
            _feel.Impact(pos, 0.48f, 420f, 0.7f, 0.42f, 0.45f, success);
            if (success) _feel.MaybeHitStop(0.7f);
        }

        /// <summary>Cut a ship's visual into lit pieces that fly apart.</summary>
        void Pieces(Transform visual, Vector3 centre, Vector3 vel, int count, float speedMin, float speedMax, float heatLife, bool alongLength, float heat)
        {
            if (visual == null || !_c.Settings.effects.debris) { if (visual != null) visual.gameObject.SetActive(false); return; }
            var hold = _debris.Adopt(visual, vel);
            var bounds = Debris.LocalBounds(hold.T);
            var seeds = _debris.MakeSeeds(hold.T, bounds, Mathf.Max(2, Mathf.RoundToInt(count * Mathf.Lerp(0.7f, 1f, _c.Settings.levels.density))), alongLength);
            var parts = _debris.Slice(hold.T, seeds, vel, 0);
            foreach (var p in parts)
            {
                Vector3 dir = p.T.position - centre;
                dir = dir.sqrMagnitude < 0.25f ? _c.Rng.OnSphere() : dir.normalized;
                dir = (dir + _c.Rng.OnSphere() * 0.55f).normalized;
                _debris.Launch(p, vel + dir * _c.Rng.Range(speedMin, speedMax) * _c.Size, _c.Rng.Range(1.2f, 6.5f), heat, heatLife, 28f);
                p.TrailRate = 22f; p.TrailLeft = 1.5f;
                p.LocalCentre = Vector3.zero;
            }
            _debris.Remove(hold);
        }

        // ------------------------------------------------------------------------------------------------ capital ships

        void Capital(Vector3 pos, Vector3 vel, Transform visual, ShipLook look, float scale, bool success, in GameEvent e)
        {
            var d = new CapitalDeath(_c, _fx, _feel, _debris, _waves, this, visual, look, pos, vel, scale, success);
            _deaths.Add(d);
            MnLog.Info($"JUICE capital death begins: scale {scale:0.00}, {(visual != null ? "with a visual to break" : "no visual")}, player {(success ? "got the kill" : "did not")}");
        }

        // ------------------------------------------------------------------------------------------------ per frame

        public void Update(float dt)
        {
            for (int i = _later.Count - 1; i >= 0; i--)
            {
                if (_c.Time >= _later[i].Key)
                {
                    var a = _later[i].Value;
                    _later.RemoveAt(i);
                    try { a(); } catch (Exception ex) { MnLog.Error("JUICE scheduled effect threw: " + ex); }
                }
            }
            for (int i = _deaths.Count - 1; i >= 0; i--)
            {
                _deaths[i].Update(dt);
                if (_deaths[i].Done) _deaths.RemoveAt(i);
            }
        }

        public void Clear() { _later.Clear(); _deaths.Clear(); }

        // ================================================================================================ the staged death

        /// <summary>
        /// A capital ship's death: internal blasts that build (venting gas, flashes along the hull), the hull breaking along jagged
        /// lines into lit sections that drift apart burning, then a final flash, a shockwave and the sections shattering.
        /// </summary>
        sealed class CapitalDeath
        {
            readonly JuiceContext _c;
            readonly Emitters _fx;
            readonly Feel _feel;
            readonly Debris _debris;
            readonly Shockwaves _waves;
            readonly Explosions _owner;
            readonly bool _success;
            readonly float _scale;
            readonly Vector3 _vel;
            DebrisPiece _hull;
            Bounds _bounds;
            List<DebrisPiece> _sections = new List<DebrisPiece>();
            Vector3 _centre, _shatterCentre;
            readonly List<DebrisPiece> _shatterQueue = new List<DebrisPiece>();
            int _sectionCountForLog;
            float _t, _carry, _secondary;
            readonly float _tBreak, _tFinal;
            int _phase;                 // 0 internal blasts, 1 sections, 2 aftermath
            float _afterT;

            public bool Done => _phase == 2 && _afterT > 14f && _shatterQueue.Count == 0;

            public CapitalDeath(JuiceContext c, Emitters fx, Feel feel, Debris debris, Shockwaves waves, Explosions owner,
                                Transform visual, ShipLook look, Vector3 pos, Vector3 vel, float scale, bool success)
            {
                _c = c; _fx = fx; _feel = feel; _debris = debris; _waves = waves; _owner = owner; _success = success; _scale = scale; _vel = vel; _centre = pos;
                _tBreak = c.Rng.Range(3.0f, 3.6f) * Mathf.Lerp(0.7f, 1f, scale);
                _tFinal = _tBreak + c.Rng.Range(3.4f, 4.0f) * Mathf.Lerp(0.7f, 1f, scale);
                if (visual != null)
                {
                    _hull = debris.Adopt(visual, vel);
                    _bounds = Debris.LocalBounds(_hull.T);
                    _hull.LocalCentre = _bounds.center;
                }
                else
                {
                    _bounds = new Bounds(Vector3.zero, new Vector3(120f, 90f, 700f) * scale);
                }
                // The first blast goes off at once: the player must feel the death begin.
                InternalBlast(true);
                c.Audio.Duck(0.3f, 1.2f);
                feel.Impact(pos, 0.22f, 2400f, 0.5f, 0.3f, 0.4f, success);
            }

            Vector3 Pos => _hull != null && _hull.T != null ? _hull.T.TransformPoint(_bounds.center) : _centre;

            Vector3 SurfacePoint(Transform t, out Vector3 normal)
            {
                var e = _bounds.extents;
                float ax = e.y * e.z, ay = e.x * e.z, az = e.x * e.y, sum = ax + ay + az;
                float r = _c.Rng.F() * sum;
                Vector3 local, n;
                float sgn = _c.Rng.Chance(0.5f) ? -1f : 1f;
                float u = _c.Rng.Range(-0.92f, 0.92f), v = _c.Rng.Range(-0.92f, 0.92f);
                if (r < ax) { n = new Vector3(sgn, 0, 0); local = new Vector3(sgn * e.x, u * e.y, v * e.z); }
                else if (r < ax + ay) { n = new Vector3(0, sgn, 0); local = new Vector3(u * e.x, sgn * e.y, v * e.z); }
                else { n = new Vector3(0, 0, sgn); local = new Vector3(u * e.x, v * e.y, sgn * e.z); }
                local = _bounds.center + local * 0.88f;
                if (t == null) { normal = n; return _centre + local; }
                normal = t.TransformDirection(n);
                return t.TransformPoint(local);
            }

            void InternalBlast(bool first = false)
            {
                Transform t = _hull != null ? _hull.T : null;
                Vector3 p = SurfacePoint(t, out var n);
                bool big = first || _c.Rng.Chance(0.28f);
                float R = (big ? _c.Rng.Range(36f, 62f) : _c.Rng.Range(13f, 28f)) * Mathf.Max(0.5f, _scale);
                _fx.Blast(p + n * R * 0.2f, _vel, R, big ? 1.05f : 0.85f);
                _fx.Vent(p, n, R * 0.28f, _c.Rng.Range(1.0f, 2.0f), 55f + R, t);
                if (big) _fx.Scorch(p, R * 0.8f, 40f);
                float dist = _c.DistanceToCamera(p);
                _c.Audio.Play(big ? JuiceCue.ExplosionMedium : JuiceCue.ExplosionSmall, p, big ? 0.9f : 0.65f, dist);
                _feel.Impact(p, big ? 0.10f : 0.055f, 2000f, big ? 0.28f : 0.14f, 0.12f, big ? 0.22f : 0.12f, false);
            }

            public void Update(float dt)
            {
                _t += dt;
                if (_phase == 0)
                {
                    float k = Mathf.Clamp01(_t / _tBreak);
                    float rate = Mathf.Lerp(1.7f, 7.0f, k * k);
                    _carry += rate * dt * _c.Settings.levels.density;
                    while (_carry >= 1f) { _carry -= 1f; InternalBlast(); }
                    // The ship groans: a steady low shake that builds toward the break.
                    _c.Trauma.Sustain(Mathf.Lerp(0.05f, 0.34f, k) * JuiceContext.Falloff(_c.DistanceToCamera(Pos), 2600f) * 1.6f);
                    if (_t >= _tBreak) BreakUp();
                }
                else if (_phase == 1)
                {
                    float k = Mathf.Clamp01((_t - _tBreak) / (_tFinal - _tBreak));
                    _secondary += Mathf.Lerp(2.6f, 7.5f, k) * dt * _c.Settings.levels.density;
                    while (_secondary >= 1f && _sections.Count > 0)
                    {
                        _secondary -= 1f;
                        var s = _sections[_c.Rng.Int(0, _sections.Count)];
                        if (s == null || s.Dead || s.T == null) continue;
                        Vector3 at = s.T.position + _c.Rng.OnSphere() * s.Radius * 0.55f;
                        float R = _c.Rng.Range(18f, 44f) * Mathf.Max(0.5f, _scale);
                        _fx.Blast(at, s.Vel, R, 0.9f);
                        if (_c.Rng.Chance(0.5f)) _fx.Vent(at, _c.Rng.OnSphere(), R * 0.3f, 1.4f, 70f, s.T);
                        _c.Audio.Play(JuiceCue.ExplosionSmall, at, 0.7f, _c.DistanceToCamera(at));
                        _feel.Impact(at, 0.06f, 2000f, 0.15f, 0.1f, 0.14f, false);
                    }
                    _c.Trauma.Sustain(Mathf.Lerp(0.30f, 0.55f, k) * JuiceContext.Falloff(_c.DistanceToCamera(Pos), 2600f) * 1.5f);
                    if (_t >= _tFinal) Final();
                }
                else
                {
                    _afterT += dt;
                    for (int n = 0; n < 2 && _shatterQueue.Count > 0; n++)
                    {
                        var s = _shatterQueue[_shatterQueue.Count - 1];
                        _shatterQueue.RemoveAt(_shatterQueue.Count - 1);
                        if (s != null && !s.Dead && s.T != null)
                            _debris.Shatter(s, 3, _shatterCentre, 35f * _scale + 15f, 150f * _scale + 50f, 0.45f);
                    }
                }
            }

            void BreakUp()
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                _phase = 1;
                int count = _scale < 0.8f ? 4 : 7;
                if (_hull == null) { return; }
                // Cells along the length, so the ship parts into long slabs with ragged joins.
                var seeds = _debris.MakeSeeds(_hull.T, _bounds, count, true);
                _sections = _debris.Slice(_hull.T, seeds, _vel, 1);
                Vector3 axis = _hull.T.forward;
                // Sort along the axis to push the ends apart.
                _sections.Sort((a, b) => Vector3.Dot(a.T.position, axis).CompareTo(Vector3.Dot(b.T.position, axis)));
                float mid = (_sections.Count - 1) * 0.5f;
                for (int i = 0; i < _sections.Count; i++)
                {
                    var s = _sections[i];
                    Vector3 along = axis * ((i - mid) * _c.Rng.Range(4.5f, 8.5f) * _scale);
                    Vector3 lateral = _c.Rng.OnSphere() * _c.Rng.Range(4f, 13f) * _scale;
                    _debris.Launch(s, _vel + along + lateral, _c.Rng.Range(0.012f, 0.06f), 0.15f, 5f, 40f);
                    s.LocalCentre = Vector3.zero;
                    s.TrailRate = 7f; s.TrailLeft = 6f;
                }
                // The joins: fireballs, burning edges on both sections, venting jets, and one bright flash over the lot.
                for (int i = 0; i + 1 < seeds.Count; i++)
                {
                    Vector3 cut = (seeds[i] + seeds[i + 1]) * 0.5f;
                    float R = Mathf.Min(_bounds.extents.x, _bounds.extents.y) * _c.Rng.Range(0.35f, 0.55f) * _scale + 18f;
                    _fx.Blast(cut, _vel, R, 0.8f, true, false, false);
                    _fx.Vent(cut, _c.Rng.OnSphere(), R * 0.3f, 3.2f, 90f, _sections.Count > i ? _sections[i].T : null, true);
                    if (i < _sections.Count) _debris.Burn(_sections[i], cut, 5.5f, 5.5f, R * 0.45f);
                    if (i + 1 < _sections.Count) _debris.Burn(_sections[i + 1], cut, 5.5f, 5.5f, R * 0.45f);
                }
                Vector3 centre = Pos;
                _c.Lights.Flash(centre, new Color(1f, 0.86f, 0.66f), 5e9f * _c.Bright * _scale, 2400f, 0.9f, 1.6f);
                _fx.Flash(centre, Mathf.Max(60f, _bounds.extents.z * 0.20f), Emitters.WarmWhite * 30f, 0.16f, 0.6f, 2f, 1.3f, _vel);
                _c.Audio.Play(JuiceCue.BreakUp, centre, 1f, _c.DistanceToCamera(centre));
                _c.Audio.Duck(0.5f, 2f);
                _feel.Impact(centre, 0.62f, 2600f, 0.9f, 0.5f, 0.9f, _success);
                _centre = centre;
                MnLog.Info($"JUICE capital death: hull breaks into {_sections.Count} sections at t={_t:0.0}s ({sw.Elapsed.TotalMilliseconds:0.0} ms in all, {Debris.LastSliceMs:0.0} ms cutting {Debris.LastSliceTris} triangles)");
            }

            void Final()
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                _phase = 2;
                Vector3 centre = _sections.Count > 0 ? AverageSectionPos() : Pos;
                _centre = centre;
                float R = Mathf.Clamp(_bounds.extents.z * 0.34f, 120f, 460f) * _c.Size * Mathf.Lerp(0.7f, 1f, _scale);
                var white = new Vector3(0.82f, 0.92f, 1f);
                _fx.Flash(centre, R * 1.35f, white * 90f, 0.14f, 0.8f, 2.4f, 1.25f, _vel);
                _fx.Flash(centre, R * 3.6f, Emitters.Orange * 3.2f, 0.55f, 0.04f, 1.8f, 1.5f, _vel);
                _fx.Flare(centre, R * 9f, R * 0.55f, Vector3.Lerp(Emitters.WarmWhite, white, 0.5f) * 26f, 0.45f, 1.5f);
                int balls = Mathf.Min(_c.N(40), 56);
                for (int i = 0; i < balls; i++)
                {
                    Vector3 off = _c.Rng.OnSphere() * R * 0.7f * Mathf.Pow(_c.Rng.F(), 0.6f);
                    float rr = R * _c.Rng.Range(0.35f, 0.75f);
                    _fx.Fireball(centre + off, _vel + off.normalized * R * _c.Rng.Range(0.3f, 0.9f), rr * 0.8f, rr * _c.Rng.Range(1.4f, 2.0f),
                                 _c.Rng.Range(1.6f, 2.8f), _c.Rng.Range(1.0f, 1.2f), _c.Rng.Range(8f, 11f), i < balls / 5 ? 0.25f : 0f, 0.7f);
                }
                _fx.Sparks(centre, _vel, Vector3.up, 360f, _c.N(480), 120f, 1100f, 1.0f, 3.2f, 0.35f, SparkStyle.Hot, 0.35f, 1.2f);
                _fx.Sparks(centre, _vel, Vector3.up, 360f, _c.N(90), 200f, 1500f, 0.8f, 2.2f, 0.3f, SparkStyle.Plasma, 0.35f, 0.9f);
                _fx.Embers(centre, _vel, R * 0.9f, _c.N(90), 60f + R * 0.3f, 3f, 8f, 3.2f);
                _fx.Ring(centre, R * 0.3f, R * 8f, 1.5f, white * 0.3f, 0.02f, 1.8f);
                _c.Lights.Flash(centre, new Color(1f, 0.9f, 0.76f), 4.5e4f * R * R * _c.Bright, 5200f, 1.3f, 1.5f);
                _c.Audio.Play(JuiceCue.ExplosionLarge, centre, 1f, _c.DistanceToCamera(centre));
                _c.Audio.Duck(0.65f, 3.2f);

                // Everything left shatters and flies: the cuts are spread over the next few frames, behind the flash.
                _shatterQueue.AddRange(_sections);
                _sectionCountForLog = _shatterQueue.Count;
                _shatterCentre = centre;
                _sections.Clear();
                if (_hull != null) { _debris.Remove(_hull); _hull = null; }

                _waves.Start(centre, 1f * Mathf.Lerp(0.55f, 1f, _scale), 3600f, 1500f);
                _feel.Impact(centre, 0.85f, 2600f, 1f, 0.7f, 1.2f, _success);
                _feel.MaybeHitStop(1f);
                _c.Hits.EdgeFlash(white * (1.2f * JuiceContext.Falloff(_c.DistanceToCamera(centre), 1800f)), 0.35f);
                MnLog.Info($"JUICE capital death: final blast at t={_t:0.0}s, {_sectionCountForLog} sections to shatter, shockwave out to 3600 m ({sw.Elapsed.TotalMilliseconds:0.0} ms this frame)");
            }

            Vector3 AverageSectionPos()
            {
                Vector3 sum = Vector3.zero; int n = 0;
                foreach (var s in _sections) if (s != null && !s.Dead && s.T != null) { sum += s.T.position; n++; }
                return n > 0 ? sum / n : _centre;
            }
        }
    }
}
