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
            if (e.Entity != GameWorld.NoShip && _c.IsPlayer(e.Entity) && _c.PlayerDestroyed != null && _c.PlayerDestroyed(e)) return;      // the death sequence stages the player's own
            var ship = _c.World.Ships.Get(e.Entity);
            Vector3 pos = e.HasPosition ? e.Position : (ship != null ? ship.Position : _c.CameraPosition + _c.Camera.transform.forward * 300f);
            Vector3 vel = ship != null ? ship.Velocity : Vector3.zero;
            string size = string.IsNullOrEmpty(e.Subject) ? _c.SizeClassOf(e.Entity, e.Value) : e.Subject;
            bool success = _c.IsPlayer(e.Other) && e.Other != GameWorld.NoShip;
            var look = _c.LookOf(e.Entity);
            Transform visual = look != null ? look.Visual : null;
            Material hullMat = look != null && look.HullMat != null ? look.HullMat : Debris.HullMaterial(visual);

            // Whatever was shooting at it should stop: the bolts in flight are not ours to hold, but a shield bubble must go.
            if (look != null && look.Shield != null) look.Shield = null;

            switch (size)
            {
                case CombatEvents.SizeBomber: Bomber(pos, vel, visual, success, e, hullMat); break;
                case CombatEvents.SizeCorvette:
                case CombatEvents.SizeCapital:
                    if (visual != null || size == CombatEvents.SizeCapital) Capital(pos, vel, visual, look, size == CombatEvents.SizeCorvette ? 0.5f : 1f, success, e);
                    else Fighter(pos, vel, visual, success, e, hullMat);
                    break;
                default: Fighter(pos, vel, visual, success, e, hullMat); break;
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
            var look = _c.LookOf(e.Entity);
            bool success = _c.IsPlayer(e.Other) && e.Other != GameWorld.NoShip;
            if (look != null && look.Damage != null && look.Visual != null)
            {
                // A modelled ship: the part the event names (or the nearest weapon to where it landed) really comes apart.
                string name = PickPart(look, e.Subject, e.Position);
                var ship = _c.World.Ships.Get(e.Entity);
                if (name != null && DestroyPart(look, name, ship != null ? ship.Velocity : Vector3.zero, 1f, success))
                {
                    if (success) _feel.MaybeHitStop(0.5f);
                    return;
                }
            }
            float radius = e.Value > 1f ? e.Value : 14f;
            Vector3 pos = e.Position;
            Blast(pos, Vector3.zero, radius * 1.1f, 1.15f, JuiceCue.SubsystemBlast, false);
            Vector3 n = e.Direction.sqrMagnitude > 0.5f ? e.Direction.normalized : Vector3.up;
            _fx.Vent(pos, n, radius * 0.25f, 1.6f, 55f);
            _fx.Scorch(pos, radius * 0.9f, 30f, 1f, Vector3.zero);
            _feel.Impact(pos, 0.38f, 300f, 0.55f, 0.35f, 0.4f, success);
            if (success) _feel.MaybeHitStop(0.5f);
        }

        static bool IsBig(string kind) => kind == "engine" || kind == "reactor" || kind == "bridge";

        /// <summary>The part a destruction event means: the one it names, else the nearest intact one to the point (weapons and sensors first).</summary>
        public static string PickPart(ShipLook look, string subject, Vector3 near)
        {
            if (look?.Damage == null) return null;
            string best = null; float bd = float.MaxValue;
            foreach (var p in look.Damage.Parts)
            {
                if (p.Destroyed || p.Pivot == null) continue;
                if (!string.IsNullOrEmpty(subject) && p.Name == subject) return p.Name;
                float d = Vector3.Distance(p.Pivot.position, near) * (IsBig(p.Kind) ? 2.5f : 1f);
                if (d < bd) { bd = d; best = p.Name; }
            }
            return best;
        }

        /// <summary>
        /// A part of a modelled ship is destroyed: its wreck shows, a blast goes off sized to it, and the wreck's chunks fly off under the
        /// effects' control (faster than the model's own motion data, which is for a slow reveal, and trailing fire).
        /// </summary>
        public bool DestroyPart(ShipLook look, string name, Vector3 shipVel, float power, bool success, bool quiet = false, Transform follow = null)
        {
            if (look == null || look.Damage == null) return false;
            JuicePart part = default; bool found = false;
            foreach (var p in look.Damage.Parts) if (p.Name == name) { part = p; found = true; break; }
            if (!found || part.Destroyed || part.Pivot == null) return false;
            Vector3 at = part.Pivot.position;
            Transform frame = follow != null ? follow : look.Visual;
            Vector3 centre = frame != null ? frame.TransformPoint(look.LocalCentre) : at;
            Vector3 outward = at - centre;
            outward = outward.sqrMagnitude > 1f ? outward.normalized : part.Pivot.forward;
            float R = Mathf.Clamp(part.Radius * 1.7f + 9f, 16f, 80f) * power;
            var chunks = look.Damage.DestroyPart(name);
            Blast(at + outward * R * 0.2f, shipVel, R, 1.2f, quiet ? JuiceCue.ExplosionSmall : JuiceCue.SubsystemBlast, false);
            _fx.Vent(at, outward, R * 0.22f, 2.2f, 60f, frame);
            _debris.Shards(at, shipVel, 7, 0.8f, Mathf.Max(2.5f, part.Radius * 0.5f), 25f, 110f, look.HullMat != null ? look.HullMat : Debris.HullMaterial(frame), 1.0f, 24f, outward);
            foreach (var ch in chunks)
            {
                if (ch.Piece == null) continue;
                Vector3 v = shipVel + ch.Velocity * 9f + outward * _c.Rng.Range(10f, 34f) + _c.Rng.OnSphere() * _c.Rng.Range(4f, 14f);
                var dp = _debris.AdoptChunk(ch.Piece, v, ch.SpinDegPerSec * 1.5f, 24f);
                dp.TrailRate = 7f; dp.TrailLeft = 2.2f;
            }
            if (!quiet) _feel.Impact(at, Mathf.Clamp(R / 200f, 0.1f, 0.4f), 500f, 0.4f, 0.25f, 0.3f, success);
            return true;
        }

        // ------------------------------------------------------------------------------------------------ one blast with sound

        void Blast(Vector3 pos, Vector3 vel, float radius, float intensity, JuiceCue cue, bool plasma)
        {
            _fx.Blast(pos, vel, radius, intensity, plasma);
            float dist = _c.DistanceToCamera(pos);
            _c.Audio.Play(cue, pos, Mathf.Clamp(0.55f + radius / 150f, 0.55f, 1f), dist);
        }

        // ------------------------------------------------------------------------------------------------ fighters and bombers

        void Fighter(Vector3 pos, Vector3 vel, Transform visual, bool success, in GameEvent e, Material hullMat)
        {
            float R = 52f;
            Blast(pos, vel, R, 1f, JuiceCue.ExplosionSmall, false);
            _fx.SparkBurst(pos, vel, _c.N(14), 60f, 200f, 0.5f, 1.5f, 0.12f, SparkStyle.Metal, 0.5f);
            Pieces(visual, pos, vel, 5, 22f, 85f, 1.7f, false, 1.2f);
            _debris.Shards(pos, vel, 22, 1.2f, 5.0f, 12f, 55f, hullMat, 1.2f);
            _feel.Impact(pos, 0.42f, 300f, 0.55f, 0.35f, 0.34f, success);
            if (success) _feel.MaybeHitStop(0.5f);
        }

        void Bomber(Vector3 pos, Vector3 vel, Transform visual, bool success, in GameEvent e, Material hullMat)
        {
            float R = 96f;
            Blast(pos, vel, R * 0.55f, 0.9f, JuiceCue.ExplosionSmall, false);
            Vector3 shipFwd = visual != null ? visual.forward : Vector3.forward;
            Vector3 second = pos + shipFwd * R * 0.6f;
            Later(0.17f, () => { Blast(second, vel, R, 1.1f, JuiceCue.ExplosionMedium, false); _fx.Vent(second, shipFwd, 4f, 1.2f, 50f); });
            Pieces(visual, pos, vel, 7, 20f, 75f, 2.1f, false, 1.2f);
            _debris.Shards(pos, vel, 32, 1.6f, 7.0f, 12f, 60f, hullMat, 1.2f);
            _fx.SparkBurst(pos, vel, _c.N(24), 70f, 260f, 0.5f, 1.7f, 0.14f, SparkStyle.Metal, 0.5f);
            _feel.Impact(pos, 0.62f, 480f, 0.75f, 0.45f, 0.48f, success);
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
        /// A capital ship's death: internal blasts that build (venting gas, flashes along the hull; on a modelled ship its parts go one
        /// after another, each with its own blast and flying wreckage), the hull breaking along jagged lines into lit sections that drift
        /// apart burning, then a final flash that swallows the whole ship, a shockwave and the sections shattering.
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
            readonly ShipLook _look;
            readonly IJuiceShipDamage _dmg;           // the ship's real parts and damage looks, when it has them
            readonly Material _hullMat;               // its plating, for shards
            DebrisPiece _hull;
            Bounds _bounds;
            Quaternion _rot = Quaternion.identity;
            List<DebrisPiece> _sections = new List<DebrisPiece>();
            Vector3 _centre, _shatterCentre;
            readonly List<DebrisPiece> _shatterQueue = new List<DebrisPiece>();
            int _sectionCountForLog;
            float _t, _carry, _secondary, _partCarry;
            readonly float _tBreak, _tFinal;
            bool _baked;                // the sections are the model's own baked hull cuts (shattering them would cost tens of ms, so the blast throws them whole)
            int _phase;                 // 0 internal blasts, 1 sections, 2 aftermath
            float _afterT;

            public bool Done => _phase == 2 && _afterT > 14f && _shatterQueue.Count == 0;

            public CapitalDeath(JuiceContext c, Emitters fx, Feel feel, Debris debris, Shockwaves waves, Explosions owner,
                                Transform visual, ShipLook look, Vector3 pos, Vector3 vel, float scale, bool success)
            {
                _c = c; _fx = fx; _feel = feel; _debris = debris; _waves = waves; _owner = owner; _success = success; _scale = scale; _vel = vel; _centre = pos;
                _hullMat = look != null && look.HullMat != null ? look.HullMat : Debris.HullMaterial(visual);
                _look = look; _dmg = look != null && look.Damage != null && look.Damage.Parts.Count > 0 ? look.Damage : null;
                // The timing has its own random source, so a death takes the same time whatever else is going on (captures rely on it).
                var timing = new FxRng(77 + (int)(scale * 10f));
                _tBreak = timing.Range(3.0f, 3.6f) * Mathf.Lerp(0.7f, 1f, scale);
                _tFinal = _tBreak + timing.Range(3.4f, 4.0f) * Mathf.Lerp(0.7f, 1f, scale);
                if (visual != null)
                {
                    _rot = visual.rotation;
                    _hull = debris.Adopt(visual, vel);
                    _bounds = Debris.LocalBounds(_hull.T);
                    _hull.LocalCentre = _bounds.center;
                }
                else
                {
                    _bounds = new Bounds(Vector3.zero, new Vector3(120f, 90f, 700f) * scale);
                }
                // The hull has been hammered before it goes: scorch marks and open breaches (a destroyed ship's model is gone a frame later, so this is the one chance).
                if (_dmg != null && _dmg.HullDamage < 0.6f) _dmg.SetHullDamage(0.6f);
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
                if (t == null) { normal = _rot * n; return _centre + _rot * local; }
                normal = t.TransformDirection(n);
                return t.TransformPoint(local);
            }

            void InternalBlast(bool first = false)
            {
                Transform t = _hull != null ? _hull.T : null;
                Vector3 p; Vector3 n;
                // On a modelled ship most blasts go off where a part is (an engine, a battery), not just anywhere on the hull's box.
                var part = _dmg != null && !first && _c.Rng.Chance(0.55f) ? RandomIntactPart(false) : null;
                if (part != null) { p = part.Value.Pivot.position; n = (p - Pos).normalized; if (n.sqrMagnitude < 0.5f) n = Vector3.up; }
                else p = SurfacePoint(t, out n);
                bool big = first || _c.Rng.Chance(0.28f);
                float R = (big ? _c.Rng.Range(46f, 80f) : _c.Rng.Range(18f, 36f)) * Mathf.Max(0.55f, _scale);
                _fx.Blast(p + n * R * 0.2f, _vel, R, big ? 1.1f : 0.9f);
                _fx.Vent(p, n, R * 0.28f, _c.Rng.Range(1.0f, 2.0f), 55f + R, t);
                if (big && _dmg == null) _fx.Scorch(p, R * 0.8f, 40f, 1f, _vel);
                if (big) _debris.Shards(p + n * 2f, _vel, 8, 1.5f, 7f, 25f, 110f, _hullMat, 1.0f, 30f, n);
                float dist = _c.DistanceToCamera(p);
                _c.Audio.Play(big ? JuiceCue.ExplosionMedium : JuiceCue.ExplosionSmall, p, big ? 0.9f : 0.65f, dist);
                _feel.Impact(p, big ? 0.10f : 0.055f, 2000f, big ? 0.28f : 0.14f, 0.12f, big ? 0.22f : 0.12f, false);
            }

            JuicePart? RandomIntactPart(bool lateOnly)
            {
                var list = _dmg.Parts;
                int n = list.Count; if (n == 0) return null;
                int start = _c.Rng.Int(0, n);
                for (int i = 0; i < n; i++)
                {
                    var p = list[(start + i) % n];
                    if (p.Destroyed || p.Pivot == null) continue;
                    if (IsBig(p.Kind) != lateOnly) continue;
                    return p;
                }
                return null;
            }

            static bool IsBig(string kind) => kind == "engine" || kind == "reactor" || kind == "bridge";

            /// <summary>One part of the ship goes: weapons and sensors first, the engines, reactor and bridge last.</summary>
            void LosePart(float k)
            {
                var p = RandomIntactPart(false);
                if (p == null && k > 0.4f) p = RandomIntactPart(true);
                if (p == null) return;
                _owner.DestroyPart(_look, p.Value.Name, _vel, Mathf.Lerp(1.0f, 1.5f, k), false, false, _hull != null ? _hull.T : null);
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
                    if (_dmg != null)
                    {
                        _partCarry += Mathf.Lerp(2.0f, 9.0f, k * k) * dt * _c.Settings.levels.density;
                        while (_partCarry >= 1f) { _partCarry -= 1f; LosePart(k); }
                    }
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
                        float R = _c.Rng.Range(26f, 58f) * Mathf.Max(0.55f, _scale);
                        _fx.Blast(at, s.Vel, R, 0.95f);
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
                    for (int n = 0; n < 1 && _shatterQueue.Count > 0; n++)
                    {
                        var s = _shatterQueue[_shatterQueue.Count - 1];
                        _shatterQueue.RemoveAt(_shatterQueue.Count - 1);
                        if (s != null && !s.Dead && s.T != null)
                            _debris.Shatter(s, 3, _shatterCentre, 35f * _scale + 15f, 150f * _scale + 50f, 0.45f);
                    }
                }
            }

            static Bounds WorldBounds(Matrix4x4 m, Bounds b)
            {
                var r = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
                for (int i = 0; i < 8; i++)
                    r.Encapsulate(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                return r;
            }

            /// <summary>
            /// The modelled ship breaks along the cuts baked for it: each section holds its piece of hull (the model's real mesh and materials)
            /// and the real parts and scorch decals that sat on it. Null when there is no bake for this ship.
            /// </summary>
            List<DebrisPiece> BreakModel(out List<Vector3> centres)
            {
                centres = new List<Vector3>();
                var bins = new List<KeyValuePair<Renderer, Mesh>>[HullSections.MaxSections];
                bool any = false;
                foreach (var r in _dmg.HullRenderers)
                {
                    if (r == null) continue;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null) continue;
                    var set = HullSections.Get(mf.sharedMesh);
                    if (set == null) continue;
                    for (int k = 0; k < set.Length; k++)
                    {
                        if (set[k] == null) continue;
                        if (bins[k] == null) bins[k] = new List<KeyValuePair<Renderer, Mesh>>();
                        bins[k].Add(new KeyValuePair<Renderer, Mesh>(r, set[k]));
                        any = true;
                    }
                }
                if (!any) return null;

                var holders = new List<Transform>();
                for (int k = 0; k < bins.Length; k++)
                {
                    if (bins[k] == null) continue;
                    Bounds wb = default; bool first = true;
                    foreach (var kv in bins[k])
                    {
                        var b = WorldBounds(kv.Key.transform.localToWorldMatrix, kv.Value.bounds);
                        if (first) { wb = b; first = false; } else wb.Encapsulate(b);
                    }
                    var go = new GameObject("hull section " + k);
                    go.transform.SetPositionAndRotation(wb.center, _hull.T.rotation);
                    foreach (var kv in bins[k])
                    {
                        var src = kv.Key;
                        var ch = new GameObject(src.name);
                        ch.transform.SetParent(go.transform, false);
                        ch.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
                        ch.transform.localScale = src.transform.lossyScale;
                        ch.AddComponent<MeshFilter>().sharedMesh = HullSections.WithInnerSkin(kv.Value);
                        var mr = ch.AddComponent<MeshRenderer>();
                        mr.sharedMaterials = src.sharedMaterials;
                        mr.shadowCastingMode = src.shadowCastingMode;
                        mr.receiveShadows = src.receiveShadows;
                    }
                    holders.Add(go.transform);
                    centres.Add(wb.center);
                }

                // The parts and the scorch marks go with the section nearest to them.
                foreach (var part in _dmg.Parts)
                    if (part.Pivot != null) part.Pivot.SetParent(holders[Nearest(centres, part.Pivot.position)], true);
                foreach (var d in _hull.T.GetComponentsInChildren<UnityEngine.Rendering.HighDefinition.DecalProjector>(true))
                    d.transform.SetParent(holders[Nearest(centres, d.transform.position)], true);

                // What is left of the intact ship is gone: the sections replace it.
                foreach (var r in _hull.T.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                foreach (var l in _hull.T.GetComponentsInChildren<LODGroup>(true)) l.enabled = false;
                foreach (var col in _hull.T.GetComponentsInChildren<Collider>(true)) col.enabled = false;
                _debris.Remove(_hull);
                _hull = null;

                var pieces = new List<DebrisPiece>();
                foreach (var h in holders) { var p = _debris.AdoptChunk(h, _vel, Vector3.zero, 90f); p.Group = 1; pieces.Add(p); }
                return pieces;
            }

            static int Nearest(List<Vector3> centres, Vector3 p)
            {
                int best = 0; float bd = float.MaxValue;
                for (int i = 0; i < centres.Count; i++) { float d = (centres[i] - p).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
                return best;
            }

            /// <summary>Real parts that are still on the ship are thrown clear of it, each a lit piece of wreckage tumbling away from <paramref name="from"/>.</summary>
            void ScatterParts(Vector3 from, float speedMin, float speedMax, float fraction)
            {
                if (_dmg == null) return;
                foreach (var part in _dmg.Parts)
                {
                    if (part.Destroyed || part.Pivot == null || !_c.Rng.Chance(fraction)) continue;
                    Vector3 dir = part.Pivot.position - from;
                    dir = dir.sqrMagnitude < 1f ? _c.Rng.OnSphere() : (dir.normalized + _c.Rng.OnSphere() * 0.5f).normalized;
                    part.Pivot.SetParent(null, true);
                    var dp = _debris.AdoptChunk(part.Pivot, _vel + dir * _c.Rng.Range(speedMin, speedMax), _c.Rng.OnSphere() * _c.Rng.Range(8f, 60f), 28f);
                    dp.TrailRate = 6f; dp.TrailLeft = 2.5f;
                }
            }

            void BreakUp()
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                _phase = 1;
                if (_hull == null) { return; }
                Vector3 axis = _hull.T.forward;
                List<Vector3> seeds;
                bool baked = false;
                if (_dmg != null)
                {
                    _sections = BreakModel(out seeds);
                    baked = _sections != null; _baked = baked;
                    if (!baked) { _sections = new List<DebrisPiece>(); ScatterParts(Pos, 20f, 70f, 0.8f); }
                }
                else seeds = null;
                if (!baked)
                {
                    int count = _scale < 0.8f ? 4 : 7;
                    // Cells along the length, so the ship parts into long slabs with ragged joins.
                    seeds = _debris.MakeSeeds(_hull.T, _bounds, count, true);
                    _sections = _debris.Slice(_hull.T, seeds, _vel, 1);
                }
                // Sort along the axis to push the ends apart.
                _sections.Sort((a, b) => Vector3.Dot(a.T.position, axis).CompareTo(Vector3.Dot(b.T.position, axis)));
                if (baked) { seeds = new List<Vector3>(); foreach (var s in _sections) seeds.Add(s.T.position); }
                float mid = (_sections.Count - 1) * 0.5f;
                float push = baked ? 1.7f : 1f;
                for (int i = 0; i < _sections.Count; i++)
                {
                    var s = _sections[i];
                    Vector3 along = axis * ((i - mid) * _c.Rng.Range(9f, 17f) * _scale * push);
                    Vector3 lateral = _c.Rng.OnSphere() * _c.Rng.Range(7f, 22f) * _scale * push;
                    _debris.Launch(s, _vel + along + lateral, _c.Rng.Range(0.03f, 0.10f), 0.15f, 5f, 40f);
                    s.LocalCentre = Vector3.zero;
                    s.TrailRate = 7f; s.TrailLeft = 6f;
                }
                // The joins: fireballs, burning edges on both sections, venting jets, and one bright flash over the lot.
                for (int i = 0; i + 1 < seeds.Count; i++)
                {
                    Vector3 cut = (seeds[i] + seeds[i + 1]) * 0.5f;
                    float R = Mathf.Min(_bounds.extents.x, _bounds.extents.y) * _c.Rng.Range(0.5f, 0.75f) * _scale + 26f;
                    _fx.Blast(cut, _vel, R, 0.9f, true, false, false);
                    _fx.Vent(cut, _c.Rng.OnSphere(), R * 0.3f, 3.2f, 90f, _sections.Count > i ? _sections[i].T : null, true);
                    if (i < _sections.Count) _debris.Burn(_sections[i], cut, 5.5f, 5.5f, R * 0.45f);
                    if (i + 1 < _sections.Count) _debris.Burn(_sections[i + 1], cut, 5.5f, 5.5f, R * 0.45f);
                }
                Vector3 centre = _sections.Count > 0 ? AverageSectionPos() : Pos;
                _c.Lights.Flash(centre, new Color(1f, 0.86f, 0.66f), 1.5e10f * _c.Bright * _scale, 2400f, 0.9f, 1.6f);
                _fx.Flash(centre, Mathf.Max(90f, _bounds.extents.z * 0.35f), Emitters.WarmWhite * 40f, 0.18f, 0.6f, 2f, 1.3f, _vel);
                _c.Audio.Play(JuiceCue.BreakUp, centre, 1f, _c.DistanceToCamera(centre));
                _c.Audio.Duck(0.5f, 2f);
                _feel.Impact(centre, 0.62f, 2600f, 0.9f, 0.5f, 0.9f, _success);
                _centre = centre;
                MnLog.Info($"JUICE capital death: hull breaks into {_sections.Count} sections at t={_t:0.0}s{(baked ? " (baked cuts of the model's hull)" : "")} ({sw.Elapsed.TotalMilliseconds:0.0} ms in all, {Debris.LastSliceMs:0.0} ms cutting {Debris.LastSliceTris} triangles)");
            }

            /// <summary>
            /// The final blast made of many simulated explosions: one big hot opening at the hull's middle, then the whole volume the hull filled lighting up in a chain
            /// from one end to the other (a dozen more, each its own variant, turned and flipped, at its own time and place), then smoke that outlasts them; with ejecta
            /// and a shock front thrown outward through all of it.
            /// </summary>
            void FinalFlips(Vector3 centre, float R, Vector3 ext)
            {
                float dur = Mathf.Clamp(3.0f + R * 0.005f, 3.2f, 5.0f);
                var big = FlipLibrary.Find("fire_b") ?? FlipLibrary.Pick("fire", _c.Rng);
                _fx.Flipbook(big, centre, _vel, R * 1.9f, dur, 1.25f, 3.2f, 1.0f, 0f, 0.25f);
                _fx.Flipbook(FlipLibrary.Find("fire_d") ?? FlipLibrary.Pick("fire", _c.Rng), centre + _rot * new Vector3(0f, 0f, ext.z * 0.18f * (_c.Rng.Chance(0.5f) ? 1f : -1f)), _vel, R * 1.5f, dur * 0.9f, 1.2f, 2.9f, 1.0f, 0.10f, 0.25f);
                int n = Mathf.Min(_c.N(15), 18);
                float dir = _c.Rng.Chance(0.5f) ? -1f : 1f;
                for (int i = 0; i < n; i++)
                {
                    float along = dir * Mathf.Lerp(-1f, 1f, (i + 0.5f) / n) * 0.95f;
                    Vector3 local = new Vector3(_c.Rng.Range(-1f, 1f) * ext.x * 1.1f, _c.Rng.Range(-1f, 1f) * ext.y * 1.1f, along * ext.z);
                    Vector3 off = _rot * local;
                    float rr = R * _c.Rng.Range(0.55f, 0.95f);
                    float delay = (float)i / n * 0.75f + _c.Rng.Range(0f, 0.10f);
                    _fx.Flipbook(FlipLibrary.Pick("fire", _c.Rng), centre + off, _vel + off.normalized * R * 0.10f, rr, dur * _c.Rng.Range(0.65f, 1.0f),
                                 _c.Rng.Range(1.0f, 1.25f), _c.Rng.Range(2.6f, 3.3f), _c.Rng.Range(0.8f, 1.3f), delay, 0.3f);
                    if (i % 3 == 0)
                    {
                        float at = delay; Vector3 lp = centre + off; float lr = rr;
                        _owner.Later(at, () => _c.Lights.Flash(lp, new Color(1f, 0.80f, 0.55f), 1.2e6f * lr * lr * _c.Bright, 2400f, 0.9f, 1.4f, new Color(1f, 0.5f, 0.22f), lr * 0.3f));
                    }
                }
                for (int i = 0; i < 3; i++)
                    _fx.Flipbook(FlipLibrary.Pick("smoke", _c.Rng), centre + _c.Rng.OnSphere() * R * 0.35f, _vel, R * _c.Rng.Range(0.7f, 1.0f), dur * 1.6f, 0.7f, 1.3f, 1.3f, _c.Rng.Range(0.7f, 1.4f), 0.2f);
                // Matter torn outward: streaks in jets and a shock front.
                _fx.Ejecta(centre, _vel, _c.N(44), 160f, 100f + R * 3.4f, 1.2f, 2.6f, 1.4f, 0.8f);
                _fx.ShockFront(centre, R, 1.5f, 1f);
            }

            void Final()
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                _phase = 2;
                Vector3 centre = _sections.Count > 0 ? AverageSectionPos() : Pos;
                _centre = centre;
                // The blast covers the whole ship: it is as big as the hull is long, with fireballs spread through the space it filled.
                var ext = _bounds.extents;
                float R = Mathf.Clamp(ext.z * 1.05f, 220f, 800f) * _c.Size * Mathf.Lerp(0.8f, 1f, _scale);
                var white = new Vector3(0.82f, 0.92f, 1f);
                // The opening punch: a blown-out white mass as big as the ship, dense and opaque for its first half second (the fire shader opens a ball dense and
                // opaque, then breaks it into thin, smeared, cooling fire), so the hull is gone into it. A central ball the whole size of the blast, and lobes through
                // the space the hull filled; far fewer, far bigger balls than before, so it reads as one mass and not a pile of bursts.
                _fx.Flash(centre, R * 0.9f, Emitters.WarmWhite * 50f, 0.16f, 0.8f, 2.2f, 1.2f, _vel);
                _fx.Flare(centre, R * 5f, R * 0.30f, Vector3.Lerp(Emitters.WarmWhite, white, 0.5f) * 6f, 0.25f, 2.2f);
                if (FlipLibrary.Available) FinalFlips(centre, R, ext);
                else
                {
                    _fx.Fireball(centre, _vel, R * 0.6f, R * 1.45f, _c.Rng.Range(2.3f, 2.8f), 1.8f, 5.8f, 0f, 0.6f, 0.2f);
                    int balls = Mathf.Min(_c.N(11), 14);
                    for (int i = 0; i < balls; i++)
                    {
                        Vector3 off = _rot * new Vector3(_c.Rng.Range(-1f, 1f) * ext.x * 1.3f, _c.Rng.Range(-1f, 1f) * ext.y * 1.3f, _c.Rng.Range(-1f, 1f) * ext.z * 0.95f);
                        off += _c.Rng.OnSphere() * R * 0.15f;
                        float rr = R * _c.Rng.Range(0.55f, 0.95f);
                        _fx.Fireball(centre + off, _vel + off.normalized * R * _c.Rng.Range(0.15f, 0.55f), rr * 0.75f, rr * _c.Rng.Range(1.3f, 1.8f),
                                     _c.Rng.Range(1.8f, 2.7f), _c.Rng.Range(1.5f, 2.0f), _c.Rng.Range(4.8f, 6.2f), 0f, 0.7f, 0.3f);
                    }
                }
                // Few sparks, each different; the fine dots of FS2's blasts stay (hundreds, not nine hundred).
                _fx.SparkBurst(centre, _vel, _c.N(60), 80f, 60f + R * 3.6f, 1.0f, 3.0f, 0.3f, SparkStyle.Hot, 0.4f, 1.2f);
                _fx.SparkBurst(centre, _vel, _c.N(16), 120f, 80f + R * 4.2f, 0.8f, 2.2f, 0.3f, SparkStyle.Plasma, 0.4f, 0.9f);
                _fx.Specks(centre, _vel, R, _c.N(260), R * 0.2f, R * 2.2f, 1.0f, 3.0f, 0.5f);
                _fx.Embers(centre, _vel, R * 0.9f, _c.N(60), 60f + R * 0.3f, 3f, 8f, 3.2f);
                // Real light on everything near, starting white-hot and going amber, from a source with a size (a hull shows a broad bright highlight, not a pinprick).
                _c.Lights.Flash(centre, new Color(1f, 0.88f, 0.70f), 5.5e5f * R * R * _c.Bright, 5200f, 2.4f, 1.1f, new Color(1f, 0.56f, 0.26f), 60f);
                // And a slow ember glow that outlasts it: the fire keeps lighting the wreckage round it while it cools.
                _c.Lights.Flash(centre, new Color(1f, 0.50f, 0.22f), 1.6e5f * R * R * _c.Bright, 3200f, 5.0f, 0.65f, new Color(0.85f, 0.34f, 0.14f), 90f);
                _c.Audio.Play(JuiceCue.ExplosionLarge, centre, 1f, _c.DistanceToCamera(centre));
                _c.Audio.Duck(0.65f, 3.2f);

                // The ship's remaining parts are flung out through the fireball; everything else shatters and flies: the cuts are spread over the next few frames, behind the flash.
                ScatterParts(centre, 60f + R * 0.3f, 160f + R * 1.1f, 0.6f);
                _debris.Shards(centre, _vel, 100, 3f, 22f, 50f, 190f + R * 0.8f, _hullMat, 1.1f, 40f);
                if (_baked)
                {
                    foreach (var sec in _sections)
                    {
                        if (sec == null || sec.Dead || sec.T == null) continue;
                        Vector3 dir = sec.T.position - centre;
                        dir = dir.sqrMagnitude < 1f ? _c.Rng.OnSphere() : (dir.normalized + _c.Rng.OnSphere() * 0.4f).normalized;
                        _debris.Launch(sec, sec.Vel + dir * _c.Rng.Range(45f, 120f), _c.Rng.Range(0.15f, 0.7f), 0.6f, 4.0f, 45f);
                        sec.TrailRate = 10f; sec.TrailLeft = 3.5f;
                    }
                    _sectionCountForLog = _sections.Count;
                }
                else { _shatterQueue.AddRange(_sections); _sectionCountForLog = _shatterQueue.Count; }
                _shatterCentre = centre;
                _sections.Clear();
                if (_hull != null) { _debris.Remove(_hull); _hull = null; }

                _waves.Start(centre, 1f * Mathf.Lerp(0.55f, 1f, _scale), 3600f, 1500f);
                _feel.Impact(centre, 0.85f, 2600f, 1f, 0.7f, 1.2f, _success);
                _feel.MaybeHitStop(1f);
                _c.Hits.EdgeFlash(Emitters.WarmWhite * (0.35f * JuiceContext.Falloff(_c.DistanceToCamera(centre), 1800f)), 0.3f);
                MnLog.Info($"JUICE capital death: final blast at t={_t:0.0}s, {_sectionCountForLog} sections to shatter, radius {R:0} m, shockwave out to 3600 m ({sw.Elapsed.TotalMilliseconds:0.0} ms this frame)");
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
