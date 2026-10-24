using System.Collections.Generic;
using UnityEngine;

namespace Monolith.Juice
{
    public enum SparkStyle { Hot, Plasma, Metal }

    /// <summary>
    /// The building blocks of every blast, hit and flash: a glow, a flare, a fireball, a spray of sparks, a ring, embers, gas,
    /// a scorch mark, a vent. Each places <see cref="Particle"/>s; the effect classes combine them. Colours are display units (see MN_Fx.shader);
    /// counts go through the density setting and sizes through the size setting.
    /// </summary>
    public sealed class Emitters
    {
        sealed class VentState
        {
            public Vector3 Pos, Dir; public Transform Parent; public Vector3 LocalPos, LocalDir;
            public float Speed, Radius, Left, Total, Rate, Carry; public bool Plasma; public Vector3 Vel;
        }

        readonly JuiceContext _c;
        readonly List<VentState> _vents = new List<VentState>();

        public Emitters(JuiceContext c) { _c = c; }

        /// <summary>Fire sprites are drawn dimmer than the flashes over them: bright orange tonemaps to white, and a fireball should stay orange.</summary>
        public const float FireGain = 0.52f;

        public static readonly Vector3 WarmWhite = new Vector3(1f, 0.90f, 0.70f);
        public static readonly Vector3 Orange = new Vector3(1f, 0.50f, 0.14f);
        public static readonly Vector3 BlueWhite = new Vector3(0.62f, 0.80f, 1f);

        static Vector4 V4(Vector3 c, float a = 0f) => new Vector4(c.x, c.y, c.z, a);

        // ------------------------------------------------------------------------------------------------ glows

        /// <summary>A round flash: hot core on a soft halo, gone in <paramref name="life"/> seconds.</summary>
        public void Flash(Vector3 pos, float radius, Vector3 rgb, float life, float hardness = 0.7f, float fadeExp = 2f, float grow = 1.25f, Vector3 vel = default)
        {
            // Nothing may swallow the screen: close to the lens a flash is held to a fraction of the view, so the target and the HUD stay readable.
            radius = Mathf.Min(radius, Mathf.Max(6f, 0.30f * _c.DistanceToCamera(pos)));
            ref var g = ref _c.Particles.Add();
            g.Kind = FxKind.Glow; g.Pos = pos; g.Vel = vel; g.Life = life; g.W0 = radius; g.W1 = radius * grow; g.P1a = g.P1b = hardness;
            g.C0 = g.C1 = g.C2 = V4(rgb); g.FadeExp = fadeExp; g.NearStart = 6f; g.NearRange = Mathf.Max(20f, radius * 0.4f);
        }

        /// <summary>A star-shaped lens flare with a long horizontal streak (The Expanse's anamorphic look).</summary>
        public void Flare(Vector3 pos, float halfWidth, float halfHeight, Vector3 rgb, float life, float fadeExp = 1.6f)
        {
            float cap = Mathf.Max(10f, 0.9f * _c.DistanceToCamera(pos));
            halfWidth = Mathf.Min(halfWidth, cap); halfHeight = Mathf.Min(halfHeight, cap * 0.12f);
            ref var f = ref _c.Particles.Add();
            f.Kind = FxKind.Flare; f.Pos = pos; f.Life = life; f.W0 = halfWidth * 0.7f; f.W1 = halfWidth; f.H0 = halfHeight; f.H1 = halfHeight * 1.2f;
            f.C0 = f.C1 = f.C2 = V4(rgb); f.FadeExp = fadeExp; f.NearStart = 6f; f.NearRange = Mathf.Max(20f, halfWidth * 0.2f);
        }

        // ------------------------------------------------------------------------------------------------ fire

        /// <summary>
        /// A billowing, turbulent ball of fire (a raymarched volume: the shader makes the structure) that swells and cools. <paramref name="heat"/> about 1 is
        /// white-hot at birth; <paramref name="brightness"/> scales its light (5 is a hot, bright ball). It is opaque where it is dense, so it hides what is behind it.
        /// </summary>
        public void Fireball(Vector3 pos, Vector3 vel, float r0, float r1, float life, float heat, float brightness, float palette = 0f, float drag = 1.2f, float erode = 0.25f)
        {
            float lim = Mathf.Max(6f, 0.80f * _c.DistanceToCamera(pos));
            if (r1 > lim) { float k = lim / r1; r0 *= k; r1 = lim; }
            ref var b = ref _c.Particles.Add();
            b.Kind = FxKind.Fire; b.Pos = pos; b.Vel = vel; b.Drag = drag; b.Life = life; b.W0 = r0; b.W1 = r1; b.SizeCurve = 0.5f;
            b.Seed = _c.Rng.F(); b.P1a = b.P1b = heat; b.P2a = b.P2b = erode; b.Palette = palette; b.Angle = 0f; b.Spin = 0f;
            b.C0 = b.C1 = b.C2 = V4(Vector3.one * brightness); b.FadeExp = 0.45f; b.FadeIn = 0.0f;
            b.NearStart = Mathf.Max(4f, r1 * 0.12f); b.NearRange = Mathf.Max(12f, r1 * 0.4f);
        }

        /// <summary>A soft gas puff: dim, fast to thin out, cooler colour (venting atmosphere, escaping coolant).</summary>
        public void Gas(Vector3 pos, Vector3 vel, float r0, float r1, float life, Vector3 rgb, float heat = 0.5f, float palette = 2f)
        {
            ref var b = ref _c.Particles.Add();
            b.Kind = FxKind.Fire; b.Pos = pos; b.Vel = vel; b.Drag = 0.5f; b.Life = life; b.W0 = r0; b.W1 = r1; b.SizeCurve = 0.7f;
            b.Seed = _c.Rng.F(); b.P1a = b.P1b = heat; b.P2a = b.P2b = 0.6f; b.Palette = palette; b.Angle = _c.Rng.F() * 6.28f;
            b.C0 = b.C1 = b.C2 = V4(rgb); b.FadeExp = 1.1f; b.FadeIn = 0.04f; b.Soft = Mathf.Max(2f, r1 * 0.5f); b.NearStart = 6f; b.NearRange = Mathf.Max(20f, r1);
        }

        // ------------------------------------------------------------------------------------------------ sparks and embers

        /// <summary>A spray of hot streaks along <paramref name="dir"/> within a cone (360 degrees = every direction).</summary>
        public void Sparks(Vector3 pos, Vector3 inherit, Vector3 dir, float coneDeg, int count, float speedMin, float speedMax, float lifeMin, float lifeMax,
                           float width, SparkStyle style = SparkStyle.Hot, float drag = 0.45f, float brightness = 1f, Vector3? tint = null)
        {
            Vector4 c0, c1, c2;
            switch (style)
            {
                case SparkStyle.Plasma:
                    c0 = V4(new Vector3(0.85f, 0.93f, 1f) * 16f, 10f); c1 = V4(new Vector3(0.35f, 0.58f, 1f) * 7f, 2f); c2 = V4(new Vector3(0.12f, 0.12f, 0.5f) * 0.6f); break;
                case SparkStyle.Metal:
                    c0 = V4(new Vector3(1f, 0.97f, 0.85f) * 20f, 12f); c1 = V4(new Vector3(1f, 0.62f, 0.2f) * 8f, 3f); c2 = V4(new Vector3(0.5f, 0.07f, 0.01f) * 0.8f); break;
                default:
                    c0 = V4(new Vector3(1f, 0.92f, 0.68f) * 15f, 9f); c1 = V4(new Vector3(1f, 0.46f, 0.10f) * 7f, 2f); c2 = V4(new Vector3(0.45f, 0.05f, 0.01f) * 0.8f); break;
            }
            c0 *= brightness; c1 *= brightness;
            if (tint.HasValue)
            {
                var t = tint.Value;
                c0 = new Vector4(Mathf.Lerp(c0.x, t.x * 16f, 0.55f), Mathf.Lerp(c0.y, t.y * 16f, 0.55f), Mathf.Lerp(c0.z, t.z * 16f, 0.55f), c0.w) * brightness;
                c1 = new Vector4(t.x * 7f, t.y * 7f, t.z * 7f, c1.w) * brightness;
                c2 = new Vector4(t.x * 0.5f, t.y * 0.5f, t.z * 0.5f, 0f);
            }
            bool every = coneDeg >= 179f;
            Vector3 toCam = _c.CameraPosition - pos;
            float camDist = toCam.magnitude;
            toCam = camDist > 1f ? toCam / camDist : Vector3.back;
            for (int i = 0; i < count; i++)
            {
                ref var s = ref _c.Particles.Add();
                Vector3 d = every ? _c.Rng.OnSphere() : _c.Rng.InCone(dir, coneDeg);
                // Depth: a share of the sparks fly at the camera (they come out big, long and soft), a share fly away (small and sharp).
                float pick = _c.Rng.F();
                if (pick < (every ? 0.16f : 0.05f) && camDist > 8f) d = (d * 0.55f + toCam * 0.85f).normalized;
                float spd = Mathf.Lerp(speedMin, speedMax, Mathf.Pow(_c.Rng.F(), 1.7f));
                float size = width * Mathf.Exp(_c.Rng.Range(-0.9f, 0.6f));           // many fine ones, a few thick
                float shutter = _c.Rng.Range(0.014f, 0.055f);                         // exposure time: longer = a longer streak
                float lum = Mathf.Exp(_c.Rng.Range(-1.1f, 0.35f));                    // brightness varies widely
                s.Kind = FxKind.Streak; s.Pos = pos + d * _c.Rng.F() * width * 4f; s.Vel = inherit + d * spd; s.Dir = d; s.Drag = drag * _c.Rng.Range(0.6f, 1.4f);
                s.Life = _c.Rng.Range(lifeMin, lifeMax);
                s.Dim = 1f; s.W0 = s.W1 = size; s.Stretch = shutter; s.MinLen = Mathf.Max(0.5f, size * _c.Rng.Range(2f, 6f));
                s.P1a = s.P1b = 1.1f; s.P2a = 0.0f; s.P2b = 0.0f; s.Blur = _c.Rng.Range(0.15f, 0.75f);
                s.C0 = c0 * lum; s.C1 = c1 * lum; s.C2 = c2 * lum; s.FadeExp = 0.5f; s.Seed = _c.Rng.F(); s.Flicker = 0.3f; s.NearStart = 4f; s.NearRange = 14f;
            }
        }

        /// <summary>
        /// A burst of sparks that is not an even spray and not a field of copies: a few jets (each its own cone, speed and brightness) and a few lone ones, and
        /// every spark differs from its neighbours in size, length, brightness, blur and colour (some fat and slow, some hair-thin and fast, some dim, some
        /// soft smears, a few white-hot heads). The show's blasts throw about a dozen.
        /// </summary>
        public void SparkBurst(Vector3 pos, Vector3 inherit, int count, float speedMin, float speedMax, float lifeMin, float lifeMax, float width,
                               SparkStyle style = SparkStyle.Hot, float drag = 0.45f, float brightness = 1f, Vector3? tint = null)
        {
            if (count <= 0) return;
            int jets = _c.Rng.Int(2, 5);
            int lone = Mathf.RoundToInt(count * 0.18f);
            var w = new float[jets]; float sum = 0f;
            for (int j = 0; j < jets; j++) { w[j] = Mathf.Exp(_c.Rng.Range(-0.5f, 1.2f)); sum += w[j]; }
            for (int j = 0; j < jets; j++)
            {
                Vector3 axis = _c.Rng.OnSphere();
                float cone = _c.Rng.Range(8f, 34f);
                int n = Mathf.Max(1, Mathf.RoundToInt((count - lone) * w[j] / sum));
                float fast = _c.Rng.Range(0.45f, 1.0f);
                SparkVaried(pos, inherit, axis, cone, n, speedMin * fast, Mathf.Lerp(speedMin, speedMax, fast), lifeMin, lifeMax, width, style, drag, brightness * _c.Rng.Range(0.5f, 1.3f), tint);
            }
            if (lone > 0) SparkVaried(pos, inherit, Vector3.up, 360f, lone, speedMin * 0.6f, speedMax * 0.8f, lifeMin, lifeMax * 0.8f, width, style, drag, brightness * 0.8f, tint);
        }

        void SparkVaried(Vector3 pos, Vector3 inherit, Vector3 dir, float coneDeg, int count, float speedMin, float speedMax, float lifeMin, float lifeMax, float width,
                         SparkStyle style, float drag, float brightness, Vector3? tint)
        {
            Vector4 hot0, hot1, hot2, white0, white1, dim0, dim1;
            switch (style)
            {
                case SparkStyle.Plasma:
                    hot0 = V4(new Vector3(0.85f, 0.93f, 1f) * 16f, 10f); hot1 = V4(new Vector3(0.35f, 0.58f, 1f) * 7f, 2f); hot2 = V4(new Vector3(0.12f, 0.12f, 0.5f) * 0.6f); break;
                case SparkStyle.Metal:
                    hot0 = V4(new Vector3(1f, 0.97f, 0.85f) * 20f, 12f); hot1 = V4(new Vector3(1f, 0.62f, 0.2f) * 8f, 3f); hot2 = V4(new Vector3(0.5f, 0.07f, 0.01f) * 0.8f); break;
                default:
                    hot0 = V4(new Vector3(1f, 0.92f, 0.68f) * 15f, 9f); hot1 = V4(new Vector3(1f, 0.46f, 0.10f) * 7f, 2f); hot2 = V4(new Vector3(0.45f, 0.05f, 0.01f) * 0.8f); break;
            }
            if (tint.HasValue)
            {
                var t = tint.Value;
                hot0 = new Vector4(Mathf.Lerp(hot0.x, t.x * 16f, 0.55f), Mathf.Lerp(hot0.y, t.y * 16f, 0.55f), Mathf.Lerp(hot0.z, t.z * 16f, 0.55f), hot0.w);
                hot1 = new Vector4(t.x * 7f, t.y * 7f, t.z * 7f, hot1.w); hot2 = new Vector4(t.x * 0.5f, t.y * 0.5f, t.z * 0.5f, 0f);
            }
            white0 = V4(new Vector3(1f, 0.97f, 0.92f) * 18f, 12f); white1 = V4(new Vector3(0.95f, 0.72f, 0.55f) * 5f, 2f);        // a white-hot head cooling to a pale pink
            dim0 = V4(new Vector3(0.9f, 0.45f, 0.18f) * 6f, 3f); dim1 = V4(new Vector3(0.7f, 0.2f, 0.06f) * 2.5f, 1f);            // a cooler, duller ember
            bool every = coneDeg >= 179f;
            Vector3 toCam = _c.CameraPosition - pos;
            float camDist = toCam.magnitude;
            toCam = camDist > 1f ? toCam / camDist : Vector3.back;
            for (int i = 0; i < count; i++)
            {
                ref var s = ref _c.Particles.Add();
                Vector3 d = every ? _c.Rng.OnSphere() : _c.Rng.InCone(dir, coneDeg);
                if (_c.Rng.F() < (every ? 0.14f : 0.05f) && camDist > 8f) d = (d * 0.55f + toCam * 0.85f).normalized;
                float spd = speedMin * Mathf.Pow(speedMax / Mathf.Max(1f, speedMin), Mathf.Pow(_c.Rng.F(), 1.4f));                   // log-spread: a few very fast, most not
                float size = width * Mathf.Exp(_c.Rng.Range(-1.3f, 0.95f));                                                          // hair-thin to fat
                float shutter = Mathf.Exp(_c.Rng.Range(Mathf.Log(0.004f), Mathf.Log(0.15f)));                                         // a dot to a long streak
                float lum = Mathf.Exp(_c.Rng.Range(-1.7f, 0.65f));                                                                   // from a barely-there ember to a brilliant one
                float pick = _c.Rng.F();
                Vector4 c0 = pick < 0.14f ? white0 : pick < 0.34f ? dim0 : hot0, c1 = pick < 0.14f ? white1 : pick < 0.34f ? dim1 : hot1, c2 = hot2;
                s.Kind = FxKind.Streak; s.Pos = pos + d * _c.Rng.F() * width * 3f; s.Vel = inherit + d * spd; s.Dir = d; s.Drag = drag * _c.Rng.Range(0.5f, 1.5f);
                s.Life = _c.Rng.Range(lifeMin, lifeMax) * Mathf.Lerp(0.5f, 1f, _c.Rng.F());
                s.W0 = s.W1 = size; s.Stretch = shutter; s.MinLen = Mathf.Max(0.4f, size * _c.Rng.Range(1.5f, 7f));
                s.Dim = 1f; s.P1a = s.P1b = _c.Rng.Range(0.6f, 1.4f); s.P2a = 0f; s.P2b = 0f; s.Blur = _c.Rng.F() < 0.45f ? _c.Rng.Range(0.55f, 1f) : _c.Rng.Range(0f, 0.35f);
                float k = brightness * lum;
                s.C0 = c0 * k; s.C1 = c1 * k; s.C2 = c2 * k; s.FadeExp = _c.Rng.Range(0.3f, 0.9f); s.Seed = _c.Rng.F(); s.Flicker = _c.Rng.Range(0.1f, 0.5f); s.NearStart = 4f; s.NearRange = 14f;
            }
        }

        /// <summary>
        /// A fine spray of tiny hot dots with barely a trail (FS2's explosions throw hundreds): they hang in the air a moment, cool from white
        /// to orange to red and wink out.
        /// </summary>
        public void Specks(Vector3 pos, Vector3 inherit, float radius, int count, float speedMin, float speedMax, float lifeMin, float lifeMax, float size = 0.07f)
        {
            for (int i = 0; i < count; i++)
            {
                ref var s = ref _c.Particles.Add();
                Vector3 d = _c.Rng.OnSphere();
                float sp = Mathf.Lerp(speedMin, speedMax, Mathf.Pow(_c.Rng.F(), 2.2f));          // most stay near the blast, a few go far
                s.Kind = FxKind.Streak; s.Pos = pos + d * radius * 0.2f * _c.Rng.F(); s.Vel = inherit + d * sp; s.Dir = d; s.Drag = 0.55f;
                s.Dim = 1f; s.Life = _c.Rng.Range(lifeMin, lifeMax); s.W0 = s.W1 = size * _c.Rng.Range(0.7f, 1.5f); s.Stretch = _c.Rng.Range(0.004f, 0.012f); s.MinLen = size * 5f;
                s.P1a = s.P1b = 0.3f; s.P2a = 0.8f; s.P2b = 0.5f;
                s.C0 = V4(new Vector3(1f, 0.93f, 0.72f) * 13f, 7f); s.C1 = V4(new Vector3(1f, 0.5f, 0.12f) * 5f, 1f); s.C2 = V4(new Vector3(0.5f, 0.07f, 0.01f) * 0.6f);
                s.FadeExp = 0.6f; s.Seed = _c.Rng.F(); s.Flicker = 0.55f; s.NearStart = 4f; s.NearRange = 14f;
            }
        }

        /// <summary>Slow glowing specks of hot debris that drift and cool.</summary>
        public void Embers(Vector3 pos, Vector3 inherit, float radius, int count, float speedMax, float lifeMin, float lifeMax, float size)
        {
            for (int i = 0; i < count; i++)
            {
                ref var e = ref _c.Particles.Add();
                e.Kind = FxKind.Glow; e.Pos = pos + _c.Rng.OnSphere() * radius * _c.Rng.F(); e.Vel = inherit + _c.Rng.OnSphere() * speedMax * _c.Rng.F();
                e.Life = _c.Rng.Range(lifeMin, lifeMax); e.W0 = size * _c.Rng.Range(0.6f, 1.4f); e.W1 = e.W0 * 0.5f; e.P1a = e.P1b = 0.9f; e.Drag = 0.2f;
                e.C0 = V4(new Vector3(1f, 0.7f, 0.35f) * 5f); e.C1 = V4(new Vector3(1f, 0.35f, 0.08f) * 2.4f); e.C2 = V4(new Vector3(0.4f, 0.04f, 0f) * 0.3f);
                e.FadeExp = 0.6f; e.Seed = _c.Rng.F(); e.Flicker = 0.5f; e.NearStart = 4f; e.NearRange = 12f;
            }
        }

        // ------------------------------------------------------------------------------------------------ shock rings

        /// <summary>An expanding shock ring facing the camera: <paramref name="r1"/> metres across by the end.</summary>
        public void Ring(Vector3 pos, float r0, float r1, float life, Vector3 rgb, float thickness = 0.1f, float fadeExp = 1.5f)
        {
            ref var r = ref _c.Particles.Add();
            r.Kind = FxKind.Ring; r.Pos = pos; r.Life = life; r.W0 = r0 / 0.86f; r.W1 = r1 / 0.86f; r.SizeCurve = 0.5f; r.P1a = r.P1b = 0.86f; r.P2a = thickness; r.P2b = thickness * 0.6f;
            r.C0 = r.C1 = r.C2 = V4(rgb); r.FadeExp = fadeExp; r.Seed = _c.Rng.F(); r.NearStart = 10f; r.NearRange = Mathf.Max(40f, r1 * 0.3f);
        }

        // ------------------------------------------------------------------------------------------------ scorch

        /// <summary>A dark burn mark with a hot rim that cools: it stays for <paramref name="life"/> seconds.</summary>
        public void Scorch(Vector3 pos, float radius, float life, float heat = 1f, Vector3 vel = default)
        {
            if (!_c.Settings.effects.hullHits) return;
            ref var s = ref _c.Particles.Add();
            s.Kind = FxKind.Scorch; s.Alpha = true; s.Pos = pos; s.Vel = vel; s.Life = life; s.W0 = s.W1 = radius; s.Seed = _c.Rng.F(); s.Angle = _c.Rng.F() * 6.28f;
            s.P1a = 0.5f; s.P1b = 0.32f; s.P2a = heat; s.P2b = 0f; s.P2Decay = 0.75f; s.FadeExp = 0.25f; s.C0 = s.C1 = s.C2 = V4(new Vector3(1f, 0.55f, 0.2f));
            s.Soft = Mathf.Max(0.6f, radius * 0.5f); s.NearStart = 10f; s.NearRange = 30f; s.Bright = 1f;
        }

        // ------------------------------------------------------------------------------------------------ vents

        /// <summary>A jet of gas from a breach: <paramref name="duration"/> seconds of puffs and a few sparks along <paramref name="dir"/>.</summary>
        public void Vent(Vector3 pos, Vector3 dir, float radius, float duration, float speed, Transform parent = null, bool plasma = false)
        {
            if (_vents.Count > 90) return;
            var v = new VentState { Pos = pos, Dir = dir.normalized, Radius = radius, Left = duration, Total = duration, Speed = speed, Rate = 18f, Plasma = plasma };
            if (parent != null) { v.Parent = parent; v.LocalPos = parent.InverseTransformPoint(pos); v.LocalDir = parent.InverseTransformDirection(dir.normalized); }
            _vents.Add(v);
        }

        public void Update(float dt)
        {
            for (int i = _vents.Count - 1; i >= 0; i--)
            {
                var v = _vents[i];
                v.Left -= dt;
                if (v.Left <= 0f || (v.Parent == null && false)) { _vents.RemoveAt(i); continue; }
                if (v.Parent != null)
                {
                    v.Pos = v.Parent.TransformPoint(v.LocalPos);
                    v.Dir = v.Parent.TransformDirection(v.LocalDir);
                }
                float k = v.Left / v.Total;
                v.Carry += v.Rate * dt * _c.Settings.levels.density;
                while (v.Carry >= 1f)
                {
                    v.Carry -= 1f;
                    Vector3 d = _c.Rng.InCone(v.Dir, 14f);
                    float sp = v.Speed * _c.Rng.Range(0.6f, 1.2f);
                    Gas(v.Pos + d * v.Radius * 0.3f, d * sp, v.Radius * 0.7f, v.Radius * Mathf.Lerp(2.4f, 3.6f, 1f - k), _c.Rng.Range(0.9f, 1.6f),
                        (v.Plasma ? new Vector3(0.7f, 0.85f, 1f) : new Vector3(0.9f, 0.94f, 1f)) * (1.0f + 1.1f * k), 0.55f, v.Plasma ? 1.6f : 2f);
                    if (_c.Rng.Chance(0.35f))
                        Sparks(v.Pos, Vector3.zero, v.Dir, 22f, 1, v.Speed * 0.7f, v.Speed * 1.5f, 0.4f, 0.9f, 0.14f, SparkStyle.Metal, 0.3f);
                }
            }
        }

        public void Clear() { _vents.Clear(); }

        // ------------------------------------------------------------------------------------------------ a whole blast

        /// <summary>
        /// A layered blast of <paramref name="radius"/> metres: flash, lens flare, fireballs that swell and cool, sparks, embers, a
        /// shock ring and a flash of light on whatever is near. Everything dims fast, as it does in vacuum.
        /// </summary>
        public void Blast(Vector3 pos, Vector3 inherit, float radius, float intensity = 1f, bool plasmaFlash = false, bool ring = true, bool light = true)
        {
            float R = radius * _c.Size;
            if (!_c.Settings.effects.explosions) return;
            float life = 0.55f + R * 0.0085f;
            Vector3 core = plasmaFlash ? new Vector3(0.78f, 0.9f, 1f) : WarmWhite;
            // A blown-out core: far brighter than the bloom threshold, so it burns through to white and spills.
            Flash(pos, R * 0.85f, core * (42f * intensity), 0.07f + R * 0.0004f, 0.85f, 2.4f, 1.2f, inherit);
            // The haze: a blast scatters light into everything round it (the show's explosions are mostly a hot core and a huge soft warm veil).
            Flash(pos, R * 6.5f, new Vector3(1f, 0.62f, 0.30f) * (0.30f * intensity), life * 1.25f, 0f, 1.6f, 1.15f, inherit);
            // A short, faint anamorphic streak: the lens is the one thing that makes one, and it should not outlast the flash it comes from.
            Flare(pos, R * 4.0f, R * 0.14f, Vector3.Lerp(WarmWhite, core, 0.5f) * (11f * intensity), 0.12f + R * 0.0006f, 2.2f);

            // The fire: a few big turbulent volumes that overlap and swallow what is behind them, and smaller ones that fly out.
            int balls = Mathf.Min(_c.N(Mathf.RoundToInt(3 + R * 0.12f)), 24);
            for (int i = 0; i < balls; i++)
            {
                bool main = i < 2;
                Vector3 off = _c.Rng.OnSphere() * R * (main ? 0.15f : 0.7f) * _c.Rng.F();
                float rr = R * (main ? _c.Rng.Range(1.1f, 1.35f) : _c.Rng.Range(0.45f, 0.85f));
                Fireball(pos + off, inherit + off.normalized * R * (main ? 0.25f : 1.0f), rr * 0.55f, rr * _c.Rng.Range(1.3f, 1.6f), life * (main ? 1.2f : _c.Rng.Range(0.7f, 1.1f)),
                         _c.Rng.Range(2.0f, 2.5f), _c.Rng.Range(3.6f, 4.6f) * intensity, 0f);
            }
            int sparks = Mathf.Min(_c.N(Mathf.RoundToInt(18 + R * 0.7f)), 110);                 // a dozen to a few dozen: the show throws about a dozen
            SparkBurst(pos, inherit, sparks, 30f + R * 0.9f, 70f + R * 3.6f, 0.5f, 1.3f + R * 0.012f, 0.08f + R * 0.0045f, SparkStyle.Hot, 0.45f, intensity);
            Specks(pos, inherit, R, Mathf.Min(_c.N(Mathf.RoundToInt(16 + R * 1.0f)), 220), 10f + R * 0.5f, 40f + R * 2.4f, 0.6f, 1.4f + R * 0.012f, 0.05f + R * 0.0012f);
            Embers(pos, inherit, R * 0.6f, _c.N(Mathf.RoundToInt(6 + R * 0.25f)), 10f + R * 0.6f, 1.2f, 2.8f + R * 0.015f, 0.55f + R * 0.03f);

            // Light on what is near: a big hot flash, and a longer warm glow (the hulls round the blast light up and the ship in it goes into silhouette).
            if (light && _c.Settings.effects.lights)
            {
                float b = _c.Bright * intensity;
                // Starts white-hot (a white light draws a hard bright glint on a hull) and goes amber; it has a size, so the highlight on a plate is broad.
                _c.Lights.Flash(pos, new Color(1f, 0.90f, 0.72f), 3.0e6f * R * R * b, Mathf.Max(500f, R * 34f), 0.8f + R * 0.010f, 1.1f, new Color(1f, 0.56f, 0.26f), Mathf.Min(R * 0.4f, 40f));
            }
        }
    }
}
