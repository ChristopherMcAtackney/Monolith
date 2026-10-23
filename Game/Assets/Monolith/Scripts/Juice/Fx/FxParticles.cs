using UnityEngine;

namespace Monolith.Juice
{
    /// <summary>A small seeded random source, so a replayed event timeline makes the same picture.</summary>
    public sealed class FxRng
    {
        System.Random _r;
        public FxRng(int seed) { _r = new System.Random(seed); }
        public void Reseed(int seed) { _r = new System.Random(seed); }
        public float F() => (float)_r.NextDouble();
        public float Range(float a, float b) => a + (b - a) * F();
        public int Int(int a, int b) => _r.Next(a, b);
        public bool Chance(float p) => F() < p;
        public Vector3 OnSphere()
        {
            while (true)
            {
                var p = new Vector3(F() * 2f - 1f, F() * 2f - 1f, F() * 2f - 1f);
                float m = p.sqrMagnitude;
                if (m > 0.0025f && m <= 1f) return p / Mathf.Sqrt(m);
            }
        }
        /// <summary>A random direction within <paramref name="halfAngleDeg"/> of <paramref name="axis"/>.</summary>
        public Vector3 InCone(Vector3 axis, float halfAngleDeg)
        {
            axis = axis.sqrMagnitude < 1e-6f ? Vector3.up : axis.normalized;
            float cosMax = Mathf.Cos(halfAngleDeg * Mathf.Deg2Rad);
            float cz = Range(cosMax, 1f), phi = F() * Mathf.PI * 2f, sz = Mathf.Sqrt(1f - cz * cz);
            var local = new Vector3(sz * Mathf.Cos(phi), sz * Mathf.Sin(phi), cz);
            return Quaternion.FromToRotation(Vector3.forward, axis) * local;
        }
    }

    /// <summary>One short-lived glowing quad: a spark, a fireball, a flash, a ring, a scorch mark.</summary>
    public struct Particle
    {
        public Vector3 Pos, Vel, Accel, Dir;
        public float Age, Life, Drag;
        public FxKind Kind;
        public bool Alpha;
        public float W0, W1, H0, H1;
        public Vector4 C0, C1, C2;
        public float P1a, P1b, P2a, P2b;
        /// <summary>When above zero, p2 is P2a * exp(-age * P2Decay) instead of the straight line (a scorch cools in seconds, however long it stays).</summary>
        public float P2Decay;
        public float Seed, Palette, Angle, Spin;
        public float Stretch, MinLen;
        public float FadeExp, FadeIn, SizeCurve, Flicker;
        public float Soft, NearStart, NearRange;
        /// <summary>1 = affected by the brightness (flash) setting; 0 = drawn as is (dark scorch).</summary>
        public float Bright;
    }

    /// <summary>
    /// The pool of <see cref="Particle"/>s: <see cref="Add"/> hands out a slot to fill in, <see cref="Update"/> moves and ages
    /// them, <see cref="Write"/> turns the live ones into quads. No allocation after start-up.
    /// </summary>
    public sealed class FxParticles
    {
        Particle[] _p;
        int _n;
        public int Count => _n;
        public int Capacity => _p.Length;
        public int Overflow { get; private set; }

        public FxParticles(int capacity) { _p = new Particle[capacity]; }

        public void Clear() { _n = 0; }

        /// <summary>A fresh particle to fill in (defaults: additive, fades out over its life). Returns a dummy when full.</summary>
        public ref Particle Add()
        {
            if (_n >= _p.Length) { Overflow++; _p[_p.Length - 1] = default; return ref _p[_p.Length - 1]; }
            ref var p = ref _p[_n++];
            p = default;
            p.Life = 1f;
            p.FadeExp = 1f;
            p.SizeCurve = 1f;
            p.Bright = 1f;
            p.W0 = p.W1 = 1f;
            p.C0 = p.C1 = p.C2 = new Vector4(1f, 1f, 1f, 0f);
            return ref p;
        }

        public void Update(float dt)
        {
            if (dt <= 0f) return;
            for (int i = 0; i < _n;)
            {
                ref var p = ref _p[i];
                p.Age += dt;
                if (p.Age >= p.Life)
                {
                    _p[i] = _p[_n - 1];
                    _n--;
                    continue;
                }
                p.Vel += p.Accel * dt;
                if (p.Drag > 0f) p.Vel *= Mathf.Exp(-p.Drag * dt);
                p.Pos += p.Vel * dt;
                i++;
            }
        }

        /// <summary>Write every live particle into the two batches. <paramref name="brightness"/> scales glowing colours.</summary>
        public void Write(FxBatch add, FxBatch alpha, float time, float brightness)
        {
            for (int i = 0; i < _n; i++)
            {
                ref var p = ref _p[i];
                float n = p.Age / p.Life;
                var c = n < 0.35f ? Vector4.Lerp(p.C0, p.C1, n / 0.35f) : Vector4.Lerp(p.C1, p.C2, (n - 0.35f) / 0.65f);
                float env = Mathf.Pow(1f - n, p.FadeExp);
                if (p.FadeIn > 0f) env *= Mathf.Clamp01(p.Age / p.FadeIn);
                if (p.Flicker > 0f)
                {
                    float f = Mathf.Sin(time * 61f + p.Seed * 91f) * Mathf.Sin(time * 37f + p.Seed * 17f);
                    env *= 1f + p.Flicker * f;
                }
                float k = env * (p.Bright > 0f ? Mathf.Lerp(1f, brightness, p.Bright) : 1f);
                if (!p.Alpha) { c.x *= k; c.y *= k; c.z *= k; c.w *= k; }
                else { c.x *= k; c.y *= k; c.z *= k; }
                float sn = p.SizeCurve == 1f ? n : Mathf.Pow(n, p.SizeCurve);
                float w = Mathf.Lerp(p.W0, p.W1, sn);
                float h = p.H0 > 0f || p.H1 > 0f ? Mathf.Lerp(p.H0, p.H1, sn) : 0f;
                float p1 = Mathf.Lerp(p.P1a, p.P1b, n), p2 = p.P2Decay > 0f ? p.P2a * Mathf.Exp(-p.Age * p.P2Decay) : Mathf.Lerp(p.P2a, p.P2b, n);
                var batch = p.Alpha ? alpha : add;

                if (p.Kind == FxKind.Streak || p.Kind == FxKind.Beam)
                {
                    float speed = p.Vel.magnitude;
                    Vector3 dir = speed > 0.05f ? p.Vel / speed : p.Dir;
                    if (dir.sqrMagnitude < 0.5f) dir = Vector3.forward;
                    float len = Mathf.Max(p.MinLen, speed * p.Stretch);
                    float half = len * 0.5f;
                    // The head sits on the particle; the tail trails behind it.
                    batch.Quad(p.Pos - dir * half, c, p.Kind, p.Seed, new Vector4(dir.x, dir.y, dir.z, half), w, n, p1, p2, p.Soft, p.NearStart, p.NearRange, p.Palette);
                }
                else
                {
                    batch.Quad(p.Pos, c, p.Kind, p.Seed, new Vector4(h, 0f, 0f, p.Angle + p.Spin * p.Age), w, n, p1, p2, p.Soft, p.NearStart, p.NearRange, p.Palette);
                }
            }
        }
    }
}
