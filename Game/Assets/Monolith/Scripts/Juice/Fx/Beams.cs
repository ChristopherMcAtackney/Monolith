using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Juice
{
    /// <summary>Box maths on a ship's hull (the juice has no colliders; a capital ship is a long chamfered box to a beam).</summary>
    public static class HullGeometry
    {
        /// <summary>The ship's box: its visual's transform and local bounds when registered, else a box round its published pose.</summary>
        public static bool Box(JuiceContext c, int ship, out Matrix4x4 localToWorld, out Bounds local)
        {
            var look = c.LookOf(ship);
            if (look != null && look.Visual != null)
            {
                localToWorld = look.Visual.localToWorldMatrix;
                local = new Bounds(look.LocalCentre, look.HalfExtents * 2f);
                return true;
            }
            var s = c.World.Ships.Get(ship);
            if (s != null)
            {
                localToWorld = Matrix4x4.TRS(s.RenderPosition(c.World.Alpha), s.RenderRotation(c.World.Alpha), Vector3.one);
                local = new Bounds(Vector3.zero, new Vector3(0.9f, 0.6f, 1.4f) * s.Radius);
                return true;
            }
            localToWorld = Matrix4x4.identity; local = default;
            return false;
        }

        /// <summary>Where a ray first meets the box (world space), with the face normal. <paramref name="inset"/> shrinks the box (0.9 = just inside).</summary>
        public static bool RayBox(Vector3 origin, Vector3 dir, Matrix4x4 l2w, Bounds local, out Vector3 hit, out Vector3 normal, float inset = 0.92f)
        {
            var w2l = l2w.inverse;
            Vector3 o = w2l.MultiplyPoint3x4(origin), d = w2l.MultiplyVector(dir);
            Vector3 mn = local.center - local.extents * inset, mx = local.center + local.extents * inset;
            float tmin = 0f, tmax = float.MaxValue;
            int axis = -1; float sign = 1f;
            for (int i = 0; i < 3; i++)
            {
                if (Mathf.Abs(d[i]) < 1e-8f)
                {
                    if (o[i] < mn[i] || o[i] > mx[i]) { hit = default; normal = default; return false; }
                    continue;
                }
                float t1 = (mn[i] - o[i]) / d[i], t2 = (mx[i] - o[i]) / d[i];
                float s = -1f;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; s = 1f; }
                if (t1 > tmin) { tmin = t1; axis = i; sign = s; }
                if (t2 < tmax) tmax = t2;
                if (tmin > tmax) { hit = default; normal = default; return false; }
            }
            if (axis < 0) { hit = origin; normal = -dir; return true; }
            Vector3 hl = o + d * tmin;
            var nl = Vector3.zero; nl[axis] = sign;
            hit = l2w.MultiplyPoint3x4(hl);
            normal = l2w.MultiplyVector(nl).normalized;
            return true;
        }

        public static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f) return a;
            return a + ab * Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
        }
    }

    /// <summary>
    /// Capital-ship heavy beams: a glow that gathers at the muzzle while the beam charges (sparks pulled in, light building, a
    /// rumble rising), then a thick, roaring, noise-animated ribbon that sweeps along the target's hull with impact sparks,
    /// molten scorch lines and small blasts where it touches, and a roar you feel through the pad when it passes near.
    /// </summary>
    public sealed class Beams
    {
        enum Phase { Charging, Firing, Fading }

        sealed class Beam
        {
            public int Source, Target;
            public string Id;
            public Phase Phase;
            public float T, ChargeDur, FireDur, FadeT;
            public Vector3 MuzzleWorld, MuzzleLocal, AimDir;
            public bool MuzzleOnShip;
            public bool AimExternal;
            public Vector3 SweepA, SweepB;     // in the target's local space, on its hull
            public Vector3 SweepNormal;
            public Vector3 End, EndNormal; public bool HasEnd;
            public Vector3 Colour;
            public float HalfWidth;
            public float SparkCarry, ScorchCarry, BlastCarry;
            public IJuiceLoop Loop, ImpactLoop;
            public int Key;
            public bool Passed;
        }

        readonly JuiceContext _c;
        readonly Emitters _fx;
        readonly Feel _feel;
        readonly List<Beam> _beams = new List<Beam>();
        int _keySeq = 9000;

        public int Count => _beams.Count;

        public Beams(JuiceContext c, Emitters fx, Feel feel) { _c = c; _fx = fx; _feel = feel; }

        Beam Find(int source)
        {
            for (int i = _beams.Count - 1; i >= 0; i--) if (_beams[i].Source == source && _beams[i].Phase != Phase.Fading) return _beams[i];
            return null;
        }

        Beam Make(in GameEvent e)
        {
            var src = _c.World.Ships.Get(e.Entity);
            int team = src != null ? src.Team : 1;
            var b = new Beam
            {
                Source = e.Entity, Target = e.Other, Id = e.Subject, MuzzleWorld = e.Position,
                AimDir = e.Direction.sqrMagnitude > 0.5f ? e.Direction.normalized : Vector3.forward,
                Colour = team == 0 ? new Vector3(0.20f, 1.0f, 0.34f) : new Vector3(1.0f, 0.13f, 0.08f),
                HalfWidth = 19f, Key = _keySeq += 3,
            };
            if (!string.IsNullOrEmpty(e.Subject))
            {
                if (e.Subject.Contains("blue")) b.Colour = new Vector3(0.25f, 0.55f, 1f);
                else if (e.Subject.Contains("green")) b.Colour = new Vector3(0.2f, 1f, 0.34f);
                else if (e.Subject.Contains("red")) b.Colour = new Vector3(1f, 0.13f, 0.08f);
            }
            var look = _c.LookOf(e.Entity);
            if (look != null && look.Visual != null) { b.MuzzleOnShip = true; b.MuzzleLocal = look.Visual.InverseTransformPoint(e.Position); }
            return b;
        }

        public void OnCharge(in GameEvent e)
        {
            if (!_c.Settings.effects.beams) return;
            var b = Make(e);
            b.Phase = Phase.Charging;
            b.ChargeDur = e.Value > 0.1f ? e.Value : 2.2f;
            _beams.Add(b);
            _c.Audio.Play(JuiceCue.BeamCharge, b.MuzzleWorld, 1f, _c.DistanceToCamera(b.MuzzleWorld));
            MnLog.Info($"JUICE beam charging from ship #{e.Entity}: {b.ChargeDur:0.0} s");
        }

        public void OnFire(in GameEvent e)
        {
            if (!_c.Settings.effects.beams) return;
            var b = Find(e.Entity);
            if (b == null) { b = Make(e); _beams.Add(b); }
            b.Phase = Phase.Firing;
            b.T = 0f;
            b.FireDur = e.Value > 0.2f ? e.Value : 4.5f;
            if (e.Other != GameWorld.NoShip) b.Target = e.Other;
            PlanSweep(b);
            b.Loop = _c.Audio.StartLoop(JuiceCue.BeamLoop, b.MuzzleWorld, 1f);
            b.ImpactLoop = _c.Audio.StartLoop(JuiceCue.BeamImpactLoop, b.MuzzleWorld, 0.8f);
            // The moment it fires: a flash and a jolt, scaled by distance.
            _fx.Flash(b.MuzzleWorld, b.HalfWidth * 6f, b.Colour * 18f, 0.18f, 0.6f, 1.6f, 1.4f);
            _fx.Flare(b.MuzzleWorld, b.HalfWidth * 22f, b.HalfWidth * 1.2f, Vector3.Lerp(b.Colour, Vector3.one, 0.45f) * 20f, 0.35f);
            _feel.Impact(b.MuzzleWorld, 0.30f, 1800f, 0.55f, 0.3f, 0.35f);
            MnLog.Info($"JUICE beam fires for {b.FireDur:0.0} s at ship #{b.Target}");
        }

        public void OnAim(in GameEvent e)
        {
            var b = Find(e.Entity);
            if (b == null) return;
            b.AimExternal = true;
            b.MuzzleWorld = e.Position;
            b.AimDir = e.Direction.sqrMagnitude > 0.5f ? e.Direction.normalized : b.AimDir;
        }

        public void OnEnd(in GameEvent e)
        {
            var b = Find(e.Entity);
            if (b == null) return;
            if (b.Phase == Phase.Charging) { Kill(b); return; }
            b.Phase = Phase.Fading; b.FadeT = 0f;
        }

        /// <summary>Choose where on the target's hull the beam starts and ends: a line down its long side that faces the source.</summary>
        void PlanSweep(Beam b)
        {
            if (b.Target == GameWorld.NoShip || !HullGeometry.Box(_c, b.Target, out var l2w, out var box)) { b.SweepNormal = Vector3.up; return; }
            Vector3 mz = l2w.inverse.MultiplyPoint3x4(CurrentMuzzle(b));
            Vector3 rel = mz - box.center;
            var e = box.extents;
            // The face the muzzle looks at most directly.
            float sx = Mathf.Abs(rel.x) / e.x, sy = Mathf.Abs(rel.y) / e.y;
            Vector3 n; float yOff = _c.Rng.Range(-0.35f, 0.35f);
            if (sx >= sy) { n = new Vector3(Mathf.Sign(rel.x), 0, 0); }
            else { n = new Vector3(0, Mathf.Sign(rel.y), 0); }
            bool forward = _c.Rng.Chance(0.5f);
            float z0 = (forward ? -0.82f : 0.82f) * e.z, z1 = (forward ? 0.82f : -0.82f) * e.z;
            Vector3 along = n.x != 0 ? new Vector3(n.x * e.x, yOff * e.y, 0) : new Vector3(yOff * e.x, n.y * e.y, 0);
            b.SweepA = box.center + along + new Vector3(0, 0, z0);
            b.SweepB = box.center + along + new Vector3(0, 0, z1);
            b.SweepNormal = n;
        }

        Vector3 CurrentMuzzle(Beam b)
        {
            if (b.MuzzleOnShip)
            {
                var look = _c.LookOf(b.Source);
                if (look != null && look.Visual != null) return look.Visual.TransformPoint(b.MuzzleLocal);
            }
            return b.MuzzleWorld;
        }

        void Kill(Beam b)
        {
            b.Loop?.Stop(0.25f); b.ImpactLoop?.Stop(0.25f);
            _beams.Remove(b);
        }

        public void Update(float dt)
        {
            for (int i = _beams.Count - 1; i >= 0; i--)
            {
                var b = _beams[i];
                b.T += dt;
                switch (b.Phase)
                {
                    case Phase.Charging: UpdateCharge(b, dt); break;
                    case Phase.Firing:
                        UpdateFire(b, dt);
                        if (b.T >= b.FireDur) { b.Phase = Phase.Fading; b.FadeT = 0f; }
                        break;
                    case Phase.Fading:
                        b.FadeT += dt;
                        UpdateFire(b, dt);
                        if (b.FadeT > 0.5f) { Kill(b); }
                        break;
                }
            }
        }

        void UpdateCharge(Beam b, float dt)
        {
            Vector3 mz = CurrentMuzzle(b);
            float k = Mathf.Clamp01(b.T / b.ChargeDur);
            // Sparks pulled in from a sphere round the muzzle, faster the nearer it is to firing.
            b.SparkCarry += Mathf.Lerp(20f, 95f, k) * dt * _c.Settings.levels.density;
            while (b.SparkCarry >= 1f)
            {
                b.SparkCarry -= 1f;
                Vector3 from = mz + _c.Rng.OnSphere() * b.HalfWidth * Mathf.Lerp(4f, 7f, _c.Rng.F());
                Vector3 vel = (mz - from) / 0.34f;
                ref var s = ref _c.Particles.Add();
                s.Kind = FxKind.Streak; s.Pos = from; s.Vel = vel; s.Dir = vel.normalized; s.Life = 0.34f; s.W0 = s.W1 = 0.5f;
                s.Stretch = 0.05f; s.MinLen = 5f; s.P1a = s.P1b = 0.3f; s.P2a = 0.3f; s.P2b = 0.8f;
                var c = b.Colour * 5f;
                s.C0 = new Vector4(c.x, c.y, c.z, 3f); s.C1 = new Vector4(c.x, c.y, c.z, 4f) * 1.1f; s.C2 = new Vector4(1f, 1f, 1f, 6f); s.FadeExp = 0f; s.Seed = _c.Rng.F();
            }
            Vector3 cg = b.Colour * (4f + 26f * k * k) * _c.Bright;
            float r = b.HalfWidth * (0.9f + 4.2f * k * k) * _c.Settings.levels.beamWidth;
            var add = _c.Add;
            add.Quad(mz, new Vector4(cg.x, cg.y, cg.z, 0f), FxKind.Glow, 0.2f, Vector4.zero, r * 1.2f, 0f, 0.7f, 0f);
            add.Quad(mz, new Vector4(cg.x, cg.y, cg.z, 0f) * 0.25f, FxKind.Glow, 0.3f, Vector4.zero, r * 4.5f, 0f, 0.1f, 0f);
            var fl = Vector3.Lerp(b.Colour, Vector3.one, 0.5f) * (3f + 22f * k * k) * _c.Bright;
            add.Quad(mz, new Vector4(fl.x, fl.y, fl.z, 0f), FxKind.Flare, 0.4f, new Vector4(r * 0.5f, 0, 0, 0), r * (4f + 6f * k), 0f, 0f, 0f);
            _c.Lights.Hold(b.Key, mz + b.AimDir * 25f, new Color(b.Colour.x, b.Colour.y * 0.9f + 0.1f, b.Colour.z * 0.9f + 0.1f), 2e9f * (0.05f + k * k) * _c.Bright, 900f);

            // Felt through the pad as it builds, if you are near.
            float near = JuiceContext.Falloff(_c.DistanceToCamera(mz), 1400f);
            _c.Trauma.Sustain(0.16f * k * near * 1.5f);
            _c.Rumble.Sustain(0.12f * k * near, 0.25f * k * k * near);
        }

        void UpdateFire(Beam b, float dt)
        {
            Vector3 mz = CurrentMuzzle(b);
            float fade = b.Phase == Phase.Fading ? Mathf.Pow(1f - Mathf.Clamp01(b.FadeT / 0.5f), 2f) : 1f;
            float onRamp = Mathf.Clamp01(b.T / 0.12f);
            float width = b.HalfWidth * _c.Settings.levels.beamWidth * fade * Mathf.Lerp(0.45f, 1f, onRamp) * (1f + 0.04f * Mathf.Sin(_c.Time * 41f));
            ComputeEnd(b, mz);
            Vector3 end = b.HasEnd ? b.End : mz + b.AimDir * 4000f;
            Vector3 dirv = end - mz;
            float len = dirv.magnitude;
            if (len < 1f) return;
            Vector3 u = dirv / len;
            Vector3 extEnd = end + u * width * 1.2f;
            float bright = _c.Bright * fade;
            var add = _c.Add;
            var col = b.Colour * (4.0f * bright);
            // Wide soft sheath, then the saturated body, then the hot core.
            Ribbon(mz, extEnd, width * 2.3f, new Vector4(col.x, col.y, col.z, 0.5f * bright), 0.11f, 55f, 1.0f);
            Ribbon(mz, extEnd, width * 1.0f, new Vector4(col.x, col.y, col.z, 4.5f * bright), 0.42f, 90f, 1.6f);
            var hot = Vector3.Lerp(b.Colour, Vector3.one, 0.7f) * (26f * bright);
            Ribbon(mz, extEnd, width * 0.40f, new Vector4(hot.x, hot.y, hot.z, 14f * bright), 0.73f, 140f, 2.4f);

            // Muzzle: a hard flare and glow.
            var mg = b.Colour * (16f * bright);
            add.Quad(mz, new Vector4(mg.x, mg.y, mg.z, 0f), FxKind.Glow, 0.2f, Vector4.zero, width * 4.2f, 0f, 0.7f, 0f);
            var mf = Vector3.Lerp(b.Colour, Vector3.one, 0.5f) * (18f * bright);
            add.Quad(mz, new Vector4(mf.x, mf.y, mf.z, 0f), FxKind.Flare, 0.4f, new Vector4(width * 0.9f, 0, 0, 0), width * 14f, 0f, 0f, 0f);

            if (b.HasEnd)
            {
                // Impact: a blinding spot, a long flare, sparks flung off the hull, molten scorch trail, small blasts.
                float flick = 0.85f + 0.3f * Mathf.Sin(_c.Time * 53f + b.Key);
                var ig = Vector3.Lerp(b.Colour, Vector3.one, 0.55f) * (30f * bright * flick);
                add.Quad(end + b.EndNormal * 2f, new Vector4(ig.x, ig.y, ig.z, 0f), FxKind.Glow, 0.3f, Vector4.zero, width * 3.4f, 0f, 0.75f, 0f);
                var og = Emitters.Orange * (6f * bright);
                add.Quad(end + b.EndNormal * 3f, new Vector4(og.x, og.y, og.z, 0f), FxKind.Glow, 0.6f, Vector4.zero, width * 7f, 0f, 0.1f, 0f);
                var ifl = Vector3.Lerp(b.Colour, Vector3.one, 0.6f) * (26f * bright);
                add.Quad(end + b.EndNormal * 2f, new Vector4(ifl.x, ifl.y, ifl.z, 0f), FxKind.Flare, 0.5f, new Vector4(width * 0.7f, 0, 0, 0), width * 13f, 0f, 0f, 0f);

                if (b.Phase == Phase.Firing)
                {
                    var tgt = _c.World.Ships.Get(b.Target);
                    Vector3 tv = tgt != null ? tgt.Velocity : Vector3.zero;
                    b.SparkCarry += 300f * dt * _c.Settings.levels.density;
                    int n = (int)b.SparkCarry; b.SparkCarry -= n;
                    if (n > 0) _fx.Sparks(end + b.EndNormal, tv, b.EndNormal, 72f, n, 60f, 520f, 0.4f, 1.4f, 0.5f, SparkStyle.Hot, 0.35f, 1.3f);
                    int m = Mathf.Max(1, n / 5);
                    _fx.Sparks(end + b.EndNormal, tv, b.EndNormal, 40f, m, 160f, 720f, 0.2f, 0.5f, 0.4f, SparkStyle.Metal, 0.3f, 1.3f);

                    b.ScorchCarry += dt;
                    if (b.ScorchCarry > 0.07f)
                    {
                        b.ScorchCarry = 0f;
                        _fx.Scorch(end + b.EndNormal * 0.25f, width * _c.Rng.Range(0.7f, 1.0f), 34f, 1f);
                    }
                    b.BlastCarry += dt * _c.Rng.Range(1.8f, 3.2f);
                    if (b.BlastCarry > 1f)
                    {
                        b.BlastCarry = 0f;
                        Vector3 at = end + b.EndNormal * 5f + _c.Rng.OnSphere() * width;
                        _fx.Blast(at, tv, _c.Rng.Range(18f, 40f), 0.8f);
                        _fx.Vent(end, b.EndNormal, 4f, 1.2f, 55f);
                        _c.Audio.Play(JuiceCue.BeamImpactLoop, at, 0.6f, _c.DistanceToCamera(at));
                    }
                }
                _c.Lights.Hold(b.Key + 1, end + b.EndNormal * 60f, new Color(1f, 0.8f, 0.6f), 9e9f * bright * Mathf.Max(0.4f, width / 8f), 1100f);
            }
            _c.Lights.Hold(b.Key, mz + u * 30f, new Color(b.Colour.x, b.Colour.y * 0.85f + 0.15f, b.Colour.z * 0.85f + 0.15f), 6e9f * bright, 1200f);

            // Sound follows the closest part of the beam to the listener; the pad feels it when it is near.
            Vector3 cam = _c.CameraPosition;
            Vector3 nearest = HullGeometry.ClosestOnSegment(mz, end, cam);
            float dist = Vector3.Distance(nearest, cam);
            float prox = JuiceContext.Falloff(dist, 700f);
            b.Loop?.Set(nearest, Mathf.Clamp01(0.25f + 0.9f * prox) * fade);
            if (b.HasEnd) b.ImpactLoop?.Set(end, Mathf.Clamp01(0.3f + 0.7f * JuiceContext.Falloff(_c.DistanceToCamera(end), 900f)) * fade);
            _c.Trauma.Sustain(0.30f * prox * fade * 1.5f);
            _c.Rumble.Sustain(0.65f * prox * fade, 0.34f * prox * fade);
            if (dist < 90f && !b.Passed && b.Phase == Phase.Firing)
            {
                b.Passed = true;
                _c.Trauma.Add(0.45f);
                _c.Rumble.Pulse(1f, 0.8f, 0.5f);
                _c.Hits.EdgeFlash(Vector3.Lerp(b.Colour, Vector3.one, 0.4f) * 3.2f, 0.35f);
            }
        }

        /// <summary>
        /// A beam layer from <paramref name="a"/> to <paramref name="b"/> as a chain of ribbon segments, short near the camera and long far
        /// away, so every part of it faces the viewer (a single long quad would turn edge-on when you are beside the beam).
        /// </summary>
        void Ribbon(Vector3 a, Vector3 b, float halfWidth, Vector4 colour, float seed, float p1, float p2)
        {
            Vector3 d = b - a;
            float total = d.magnitude;
            if (total < 1f) return;
            Vector3 u = d / total;
            Vector3 cam = _c.CameraPosition;
            float at = 0f;
            int guard = 0;
            while (at < total - 0.01f && guard++ < 64)
            {
                Vector3 p = a + u * at;
                float dist = Vector3.Distance(HullGeometry.ClosestOnSegment(p, b, cam), cam);
                float step = Mathf.Clamp(dist * 0.55f, 30f, 700f);
                float end = Mathf.Min(total, at + step);
                if (total - end < step * 0.3f) end = total;
                float len = end - at;
                Vector3 centre = a + u * (at + len * 0.5f);
                _c.Add.Quad(centre, colour, FxKind.Beam, seed, new Vector4(u.x, u.y, u.z, len * 0.5f), halfWidth, 0f, p1, p2,
                            0f, 0f, 0f, 0f, new Vector4(total, at / total, end / total, 0f));
                at = end;
            }
        }

        /// <summary>Where the beam ends this frame: on the target's hull, sweeping along it; or on the aim ray.</summary>
        void ComputeEnd(Beam b, Vector3 muzzle)
        {
            b.HasEnd = false;
            if (b.Target != GameWorld.NoShip && HullGeometry.Box(_c, b.Target, out var l2w, out var box))
            {
                Vector3 aim;
                if (b.AimExternal) aim = b.AimDir;
                else
                {
                    float k = b.FireDur > 0.01f ? Mathf.Clamp01(b.T / b.FireDur) : 1f;
                    k = k * k * (3f - 2f * k);                      // eases in and out along the hull
                    Vector3 p = l2w.MultiplyPoint3x4(Vector3.Lerp(b.SweepA, b.SweepB, k));
                    // A slight wobble, as a real beam would not hold perfectly still.
                    p += _c.Camera.transform.up * (Mathf.Sin(_c.Time * 9f + b.Key) * 6f);
                    aim = (p - muzzle).normalized;
                }
                if (HullGeometry.RayBox(muzzle, aim, l2w, box, out var hit, out var normal, 0.97f))
                {
                    b.End = hit; b.EndNormal = normal; b.HasEnd = true; b.AimDir = aim;
                    return;
                }
                b.AimDir = aim;
            }
            else if (b.AimExternal && _c.Looks.Count > 0)
            {
                b.End = muzzle + b.AimDir * 4000f;
            }
        }

        public void Clear() { foreach (var b in _beams) { b.Loop?.Stop(0.1f); b.ImpactLoop?.Stop(0.1f); } _beams.Clear(); }
    }
}
