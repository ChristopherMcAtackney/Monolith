using System;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    /// <summary>Hits, explosions, the death of a capital ship, and the groans of a hull under load.</summary>
    public static class SfxImpacts
    {
        static float Hit(float t, float k, float a = 0.001f) => Sd.Hit(t, k, a);
        static float Ex(float t, float a, float b, float tau) => Sd.Expo(t, a, b, tau);
        static Sd Noise(float len, uint seed, Func<float, float> amp, float colour = 1f) { var n = new Sd(len); n.Noise(amp, seed, colour); return n; }

        /// <summary>Energy meeting a shield: a bubble of light pressure, a sizzle, a ring.</summary>
        public static Sd HitShield(int v, bool onPlayer)
        {
            float k = v == 0 ? 1f : v == 1 ? 1.12f : 0.89f;
            float len = onPlayer ? 1.0f : 0.8f;
            var s = new Sd(len);
            s.Sine(t => Ex(t, 2300f * k, 430f * k, 0.09f), t => 0.55f * Hit(t, onPlayer ? 5f : 7f, 0.001f) * (1f + 0.5f * MathF.Sin(t * Dsp.TwoPi * 55f)));
            s.Fm(t => Ex(t, 3300f * k, 700f * k, 0.1f), 1.5f, t => 2.2f * MathF.Exp(-t * 9f), t => 0.3f * Hit(t, 8f));
            s.Mix(Noise(len, 211u + (uint)v, t => Hit(t, 24f, 0.0005f)).HighPass(3000f), -6f);
            s.Mix(Noise(len, 215u + (uint)v, t => Hit(t, 5f, 0.01f)).BandPass(2400f, 0.5f), -12f);
            s.Sine(t => Ex(t, 150f, 58f, 0.04f), t => (onPlayer ? 1.1f : 0.65f) * Hit(t, onPlayer ? 8f : 14f, 0.002f));
            var ring = new Sd(len); ring.Clang(1450f * k, 0.5f, 0.3f, 9u); s.Mix(ring, -9f);
            s.Drive(1.1f);
            if (onPlayer) s.LowPass(t => 7500f, 0.1f);
            s.Reverb(onPlayer ? 0.7f : 0.9f, 0.12f, 0.6f, 0.55f, 3f, 0.3f);
            return s.Edges(0.4f, 40f).Normalize(-1f);
        }

        /// <summary>Metal struck: a bar's inharmonic ring, a crack, a thud, shards pattering. On the player the whole cabin takes it: more thud, a rattle.</summary>
        public static Sd HitHull(int v, bool onPlayer, bool heavy = false)
        {
            float k = v == 0 ? 1f : v == 1 ? 1.19f : 0.84f;
            float len = onPlayer ? (heavy ? 2.0f : 1.3f) : 0.9f;
            var s = new Sd(len);
            s.Mix(Noise(len, 221u + (uint)v, t => Hit(t, 260f, 0.0002f)), -2f);
            var clang = new Sd(len); clang.Clang(430f * k, onPlayer ? 0.55f : 0.35f, 0.7f, 17u + (uint)v, 0f, 0.8f); s.Mix(clang, -4f);
            var clang2 = new Sd(len); clang2.Clang(1180f * k, 0.18f, 0.5f, 29u + (uint)v); s.Mix(clang2, -9f);
            s.Sine(t => Ex(t, 130f, 44f, 0.03f), t => (onPlayer ? (heavy ? 1.7f : 1.3f) : 0.9f) * Hit(t, onPlayer ? 8f : 15f, 0.002f));
            s.Crackle(t => 110f * MathF.Exp(-t * 7f), t => 0.55f, 231u + (uint)v, 1200f, 7000f, 7f);
            if (onPlayer)
            {
                s.Mix(Noise(len, 233u, t => Hit(t, 5f, 0.01f)).LowPass(220f), -3f, 0.01f);        // the cabin's boom
                s.Crackle(t => 70f * MathF.Exp(-t * 3f), t => 0.45f, 235u, 300f, 1600f, 18f, 0.05f);   // things rattling loose
                s.LowPass(t => Ex(t, 9000f, 4500f, 0.2f), 0.1f);
            }
            s.Drive(1.5f);
            s.Reverb(onPlayer ? 0.5f : 0.7f, 0.1f, 0.5f, 0.6f, 2f, 0.2f);
            return s.Edges(0.3f, 40f).Normalize(-1f);
        }

        public static Sd ShieldDown()
        {
            var s = new Sd(1.6f);
            s.Sine(t => Ex(t, 640f, 46f, 0.2f), t => 1.0f * Hit(t, 2.6f, 0.002f));
            s.Saw(t => Ex(t, 1400f, 80f, 0.16f), t => 0.4f * Hit(t, 3.5f, 0.002f));
            s.Mix(Noise(1.6f, 241u, t => Hit(t, 4f, 0.005f)).HighPass(2000f), -8f);
            s.Crackle(t => 120f * MathF.Exp(-t * 3f), t => 0.5f, 242u, 2000f, 8000f, 5f);
            s.Ladder(t => Ex(t, 6000f, 300f, 0.3f), 0.25f, 1.4f);
            s.Drive(1.4f);
            s.Reverb(0.9f, 0.2f, 0.8f, 0.5f, 5f, 0.6f);
            return s.Edges(0.5f, 60f).Normalize(-1f);
        }

        public static Sd SubsystemBlast(bool onPlayer)
        {
            var s = new Sd(1.6f);
            s.Mix(Noise(1.6f, 251u, t => Hit(t, 14f, 0.0003f)).LowPass(t => Ex(t, 7000f, 500f, 0.12f)), 0f);
            s.Sine(t => Ex(t, 105f, 36f, 0.07f), t => 1.2f * Hit(t, 4.5f, 0.002f));
            s.Clang(240f, 0.35f, 0.4f, 33u);
            s.Crackle(t => 160f * MathF.Exp(-t * 3f), t => 0.55f, 252u, 1500f, 8000f, 6f);
            s.Drive(1.4f);
            s.Reverb(1.0f, 0.18f, 0.8f, 0.5f, 4f, 0.5f);
            return s.Edges(0.4f, 50f).Normalize(-1f);
        }

        /// <summary>Explosions by size. In vacuum there is no air to carry them, so what you hear is the structure: a crack, a deep sub thump, a body of roar that dims quickly, a crackle of falling debris.</summary>
        public static Sd Explosion(int size, int v)
        {
            // size 0 small, 1 medium, 2 large
            float[] len = { 2.2f, 3.4f, 7.5f };
            float L = len[size];
            float w = size == 0 ? 1f : size == 1 ? 1.6f : 3f;
            var s = new Sd(L);
            uint sd = (uint)(260 + size * 10 + v);
            s.Mix(Noise(L, sd, t => Hit(t, 60f / w, 0.0002f)).HighPass(900f), 0f);                                                      // crack
            s.Mix(Noise(L, sd + 1, t => Dsp.Smooth(t / 0.004f) * MathF.Exp(-t * 3.2f / w)).LowPass(t => Ex(t, 6500f, 160f, 0.35f * w)), 2f);  // the body
            s.Sine(t => Ex(t, 86f, 24f * (1f + 0.2f * (1 - size)), 0.18f * w), t => 1.5f * Hit(t, 2.6f / w, 0.003f));                          // sub thump
            s.Sine(t => Ex(t, 50f, 30f, 0.5f * w), t => 0.8f * Hit(t, 1.5f / w, 0.015f));
            s.Crackle(t => (260f + 90f * w) * MathF.Exp(-t * 1.6f / MathF.Sqrt(w)), t => 0.5f * MathF.Exp(-t * 1.2f / w), sd + 2, 1100f, 7000f, 8f);   // debris
            if (size >= 1)
            {
                // a second burst
                s.Mix(Noise(L, sd + 3, t => Hit(t, 24f, 0.0004f)).LowPass(t => Ex(t, 4000f, 200f, 0.2f)), -3f, 0.09f * w);
                s.Sine(t => Ex(t, 70f, 26f, 0.3f), t => 0.8f * Hit(t, 2.2f / w, 0.01f), 0f, 0.09f * w);
            }
            if (size == 2)
            {
                // the ship groaning as it goes
                s.Saw(t => Ex(t, 75f, 24f, 1.6f), t => 0.42f * Dsp.Smooth(t / 0.4f) * MathF.Exp(-t * 0.55f), 0.2f);
                s.Ladder(t => Ex(t, 1800f, 120f, 1.4f), 0.35f, 1.5f);
                s.Mix(Noise(L, sd + 5, t => Hit(t, 18f, 0.0003f)).LowPass(900f), -4f, 0.55f);
                s.Sine(t => Ex(t, 62f, 22f, 0.6f), t => 0.9f * Hit(t, 1.0f, 0.02f), 0f, 0.55f);
            }
            s.Drive(1.5f + 0.3f * size);
            s.Reverb(size == 2 ? 3.6f : size == 1 ? 1.8f : 1.1f, 0.22f, 0.8f + 0.4f * size, 0.5f, 5f, size == 2 ? 2.0f : 0.8f);
            return s.Edges(0.4f, 120f).Normalize(-1f);
        }

        /// <summary>The hull of a capital ship coming apart: tearing, groaning, a dozen clangs as the sections let go. 5.5 s.</summary>
        public static Sd BreakUp()
        {
            const float L = 6.0f;
            var s = new Sd(L);
            s.Mix(Noise(L, 301u, t => Dsp.Smooth(t / 0.03f) * MathF.Exp(-t * 0.65f) * (0.55f + 0.45f * MathF.Sin(t * 13f))).BandPass(t => Ex(t, 1400f, 170f, 1.4f), 0.55f, 2), 5f);
            s.Mix(Noise(L, 302u, t => Hit(t, 40f, 0.0003f)).HighPass(500f), -2f);
            s.Sine(t => Ex(t, 66f, 20f, 0.8f), t => 1.7f * Hit(t, 0.8f, 0.008f));
            var rng = new Rng(303);
            for (int i = 0; i < 16; i++)
            {
                float at = 0.05f + i * 0.2f + rng.Float() * 0.2f;
                s.Clang(rng.Range(140f, 700f), rng.Range(0.3f, 1.1f), rng.Range(0.3f, 0.8f), (uint)(310 + i), at, 0.8f);
            }
            for (int i = 0; i < 5; i++)
            {
                float f0 = 120f + 45f * i, st = 0.15f * i;
                s.Saw(t => t < st ? 0f : Ex(t - st, f0, 28f, 0.9f), t => t < st ? 0f : 0.2f * Hit(t - st, 1.0f, 0.06f), i * 0.2f);
            }
            s.Crackle(t => 330f * MathF.Exp(-t * 0.7f), t => 0.5f * MathF.Exp(-t * 0.5f), 330u, 800f, 6000f, 9f);
            s.Ladder(t => Ex(t, 6000f, 500f, 1.5f), 0.3f, 1.4f);
            s.Drive(1.8f);
            s.Reverb(3.0f, 0.25f, 1.4f, 0.5f, 8f, 1.5f);
            return s.Edges(0.4f, 120f).Normalize(-1f);
        }

        /// <summary>The pressure wave after a capital ship dies: mostly felt. A sub swell and a dull push.</summary>
        public static Sd Shockwave()
        {
            const float L = 4.2f;
            var s = new Sd(L);
            s.Sine(t => Ex(t, 62f, 17f, 1.4f), t => 2.0f * Dsp.Smooth(t / 0.18f) * MathF.Exp(-t * 0.85f));
            s.Sine(t => Ex(t, 36f, 22f, 1f), t => 1.0f * Dsp.Smooth(t / 0.4f) * MathF.Exp(-t * 0.6f));
            s.Mix(Noise(L, 341u, t => MathF.Sin(Dsp.Clamp01(t / 2.8f) * Dsp.Pi)).LowPass(t => 240f - 90f * t, 0.1f, 2), 1f);
            s.Mix(Noise(L, 342u, t => Hit(t, 5f, 0.05f)).LowPass(900f).HighPass(150f), -8f);
            s.Drive(1.6f);
            s.Reverb(2.2f, 0.15f, 1.5f, 0.7f, 20f, 1.0f, 40f);
            return s.Edges(1f, 150f).Normalize(-1f);
        }

        /// <summary>Two ships touching: a deep dull thud and a long scrape of metal on metal.</summary>
        public static Sd Collision()
        {
            var s = new Sd(1.8f);
            s.Sine(t => Ex(t, 96f, 32f, 0.1f), t => 1.5f * Hit(t, 5f, 0.002f));
            s.Mix(Noise(1.8f, 351u, t => Hit(t, 9f, 0.002f)).LowPass(600f), -2f);
            s.Clang(180f, 0.9f, 0.6f, 35u);
            s.Mix(Noise(1.8f, 352u, t => Dsp.Smooth(t / 0.1f) * MathF.Exp(-t * 1.6f) * (0.6f + 0.4f * MathF.Sin(t * 37f))).BandPass(t => 700f + 500f * MathF.Sin(t * 5f), 0.6f, 2), -4f, 0.05f);
            s.Drive(1.6f);
            s.Reverb(0.8f, 0.14f, 0.7f, 0.6f, 3f, 0.4f);
            return s.Edges(0.4f, 60f).Normalize(-1f);
        }

        /// <summary>A hull groan: metal under load, a slow pitched moan with a wavering resonance. Four sizes.</summary>
        public static Sd Creak(int kind)
        {
            float[] len = { 1.1f, 1.7f, 2.4f, 3.4f };
            float[] f0 = { 210f, 150f, 95f, 70f };
            float L = len[kind], f = f0[kind];
            var s = new Sd(L);
            var r = new Rng((uint)(361 + kind));
            float wob = r.Range(3f, 7f), drift = r.Range(-0.12f, 0.1f);
            s.Saw(t => f * (1f + drift * t / L + 0.015f * MathF.Sin(t * wob * 3.1f)), t => 0.6f * Dsp.Smooth(t / (0.25f * L)) * Dsp.Smooth((L - t) / (0.45f * L)), 0.1f);
            s.Pulse(t => f * 1.502f * (1f + drift * t / L), t => 0.35f * Dsp.Smooth(t / (0.3f * L)) * Dsp.Smooth((L - t) / (0.4f * L)), t => 0.3f + 0.2f * MathF.Sin(t * 2f));
            s.BandPass(t => f * (2.6f + 1.6f * MathF.Sin(t * wob * 0.7f)), 0.82f, 2);
            s.Mix(Noise(L, 371u + (uint)kind, t => 0.5f * Dsp.Smooth(t / 0.3f) * Dsp.Smooth((L - t) / 0.5f)).BandPass(t => 900f + 700f * MathF.Sin(t * 3.3f), 0.6f, 2), -9f);
            s.Crackle(t => 18f * (0.5f + 0.5f * MathF.Sin(t * 9f)), t => 0.4f, 372u + (uint)kind, 400f, 2200f, 14f);
            s.Drive(1.5f);
            s.Reverb(0.9f, 0.15f, 0.5f, 0.6f, 3f, 0.4f);
            return s.Edges(20f, 80f).Normalize(-1f);
        }
    }
}
