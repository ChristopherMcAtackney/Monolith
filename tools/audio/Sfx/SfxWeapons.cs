using System;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    /// <summary>Guns, missiles, decoys and beams. Weighty: every sound has a crack on top, a body in the middle and a thump under it.</summary>
    public static class SfxWeapons
    {
        static float Hit(float t, float k, float a = 0.001f) => Sd.Hit(t, k, a);
        static float Ex(float t, float a, float b, float tau) => Sd.Expo(t, a, b, tau);

        static Sd Noise(float len, uint seed, Func<float, float> amp, float colour = 1f) { var n = new Sd(len); n.Noise(amp, seed, colour); return n; }

        /// <summary>The player's fast gun: a hard snap, a falling zap, a pulse body for meat and a thump under it. Three variants.</summary>
        public static Sd LaserLight(int v)
        {
            float k = v == 0 ? 1f : v == 1 ? 0.93f : 1.08f;
            var s = new Sd(0.5f);
            s.Saw(t => Ex(t, 3000f * k, 330f * k, 0.05f), t => 0.42f * Hit(t, 12f, 0.0006f));
            s.Fm(t => Ex(t, 1900f * k, 400f * k, 0.07f), 2.01f, t => 3.5f * MathF.Exp(-t * 16f), t => 0.38f * Hit(t, 10f, 0.0006f));
            s.Pulse(t => Ex(t, 880f * k, 170f * k, 0.07f), t => 0.55f * Hit(t, 11f, 0.001f), t => 0.3f);
            s.Mix(Noise(0.5f, 11u + (uint)v, t => Hit(t, 220f, 0.0002f)).HighPass(2800f), -7f);
            s.Sine(t => Ex(t, 165f, 54f, 0.03f), t => 0.95f * Hit(t, 15f, 0.0012f));
            s.Drive(1.3f);
            s.LowPass(t => 9000f, 0.1f);
            s.Reverb(0.45f, 0.1f, 0.5f, 0.5f, 4f, 0.25f);
            return s.Edges(0.4f, 14f).Normalize(-1f);
        }

        /// <summary>Earth's gun: colder and rougher, a ring-modulated buzz on a lower, longer fall.</summary>
        public static Sd LaserEnemy(int v)
        {
            float k = v == 0 ? 1f : v == 1 ? 0.9f : 1.12f;
            var s = new Sd(0.55f);
            s.Pulse(t => Ex(t, 1700f * k, 150f * k, 0.09f), t => 0.55f * Hit(t, 9f, 0.0008f), t => 0.22f + 0.1f * MathF.Sin(t * 60f));
            s.Saw(t => Ex(t, 2600f * k, 260f * k, 0.04f), t => 0.3f * Hit(t, 16f, 0.0006f));
            s.RingMod(t => 88f * k, 0.55f);
            s.Mix(Noise(0.55f, 31u + (uint)v, t => Hit(t, 170f, 0.0002f)).HighPass(2200f), -8f);
            s.Sine(t => Ex(t, 120f, 40f, 0.04f), t => 1.0f * Hit(t, 11f, 0.002f));
            s.Comb(780f * k, 0.5f, 0.25f);
            s.Drive(1.6f);
            s.LowPass(t => 7500f, 0.12f);
            s.Reverb(0.6f, 0.12f, 0.6f, 0.55f, 4f, 0.3f);
            return s.Edges(0.4f, 14f).Normalize(-1f);
        }

        /// <summary>The heavy gun: a slow wallop. The charge snaps, the bolt leaves with a falling roar and a sub boom, and the room rings.</summary>
        public static Sd LaserHeavy(int v)
        {
            float k = v == 0 ? 1f : 0.94f;
            var s = new Sd(1.3f);
            s.Saw(t => Ex(t, 1100f * k, 62f * k, 0.13f), t => 0.55f * Hit(t, 4.2f, 0.004f));
            s.Saw(t => Ex(t, 1130f * k, 64f * k, 0.13f), t => 0.45f * Hit(t, 4.2f, 0.004f), 0.3f);
            s.Pulse(t => Ex(t, 440f * k, 90f * k, 0.12f), t => 0.5f * Hit(t, 5f, 0.003f), t => 0.2f);
            s.Ladder(t => Ex(t, 5200f, 380f, 0.16f), 0.3f, 1.4f);
            s.Mix(Noise(1.3f, 51u + (uint)v, t => Hit(t, 60f, 0.0003f)).HighPass(900f).LowPass(t => Ex(t, 9000f, 700f, 0.08f)), -5f);
            s.Sine(t => Ex(t, 130f, 36f, 0.12f), t => 1.25f * Hit(t, 3.2f, 0.004f));
            s.Sine(t => Ex(t, 62f, 29f, 0.3f), t => 0.6f * Hit(t, 2.4f, 0.01f));
            var ping = new Sd(1.3f); ping.Clang(1180f * k, 0.5f, 0.35f, 5u); s.Mix(ping, -8f, 0.002f);
            s.Drive(1.5f);
            s.Reverb(1.4f, 0.28f, 1.0f, 0.5f, 6f, 0.9f);
            return s.Edges(0.4f, 40f).Normalize(-1f);
        }

        /// <summary>A capital ship's turret: the same, bigger and slower. Heard from kilometres away, so the tail is long.</summary>
        public static Sd LaserTurret(int v)
        {
            var s = new Sd(1.8f);
            s.Saw(t => Ex(t, 700f, 38f, 0.2f), t => 0.55f * Hit(t, 3.0f, 0.006f));
            s.Saw(t => Ex(t, 716f, 39f, 0.2f), t => 0.45f * Hit(t, 3.0f, 0.006f), 0.4f);
            s.Ladder(t => Ex(t, 3200f, 200f, 0.25f), 0.35f, 1.6f);
            s.Mix(Noise(1.8f, 71u + (uint)v, t => Hit(t, 30f, 0.0004f)).LowPass(t => Ex(t, 6000f, 400f, 0.1f)), -5f);
            s.Sine(t => Ex(t, 95f, 27f, 0.2f), t => 1.4f * Hit(t, 2.0f, 0.006f));
            s.Sine(t => Ex(t, 48f, 22f, 0.5f), t => 0.7f * Hit(t, 1.6f, 0.02f));
            s.Drive(1.4f);
            s.Reverb(2.4f, 0.35f, 1.3f, 0.55f, 12f, 1.4f);
            return s.Edges(0.5f, 60f).Normalize(-1f);
        }

        public static Sd FlakShell()
        {
            var s = new Sd(0.5f);
            s.Sine(t => Ex(t, 150f, 55f, 0.025f), t => 1.0f * Hit(t, 14f, 0.002f));
            s.Mix(Noise(0.5f, 91u, t => Hit(t, 35f, 0.001f)).LowPass(t => Ex(t, 3200f, 260f, 0.05f)), -2f);
            s.Pulse(t => Ex(t, 420f, 120f, 0.04f), t => 0.3f * Hit(t, 18f), t => 0.4f);
            s.Drive(1.2f);
            s.Reverb(0.4f, 0.08f, 0.5f, 0.6f, 3f, 0.15f);
            return s.Edges(0.5f, 18f).Normalize(-1f);
        }

        /// <summary>A flak burst: a hard crack, a gut thump, and shrapnel pattering away.</summary>
        public static Sd FlakBurst()
        {
            var s = new Sd(1.2f);
            s.Mix(Noise(1.2f, 93u, t => Hit(t, 55f, 0.0002f)).HighPass(700f), -1f);
            s.Sine(t => Ex(t, 140f, 46f, 0.04f), t => 1.2f * Hit(t, 9f, 0.002f));
            s.Mix(Noise(1.2f, 94u, t => Hit(t, 7f, 0.002f)).BandPass(t => Ex(t, 2400f, 420f, 0.18f), 0.6f), -4f);
            s.Crackle(t => 420f * MathF.Exp(-t * 5f), t => 0.5f * MathF.Exp(-t * 4f), 95u, 1800f, 6500f, 5f);
            s.Drive(1.4f);
            s.Reverb(0.9f, 0.15f, 0.8f, 0.55f, 4f, 0.5f);
            return s.Edges(0.4f, 40f).Normalize(-1f);
        }

        /// <summary>A missile leaving the rail: the clunk of the release, the ignition, a hiss that climbs as it accelerates away.</summary>
        public static Sd MissileLaunch(bool torpedo)
        {
            float len = torpedo ? 2.8f : 1.8f, sp = torpedo ? 0.55f : 1f;
            var s = new Sd(len);
            var clunk = new Sd(0.4f); clunk.Clang(torpedo ? 150f : 260f, 0.12f, 0.9f, torpedo ? 3u : 4u); clunk.Sine(t => Ex(t, 130f, 60f, 0.02f), t => 0.8f * Hit(t, 25f, 0.001f));
            s.Mix(clunk, 0f, 0f);
            s.Mix(Noise(len, 111u, t => Dsp.Smooth(t / (0.18f / sp)) * MathF.Exp(-t * 1.1f * sp)).BandPass(t => 350f + 2800f * Dsp.Clamp01(t / (1.1f / sp)), 0.45f, 2), 2f, 0.04f);
            s.Mix(Noise(len, 112u, t => Dsp.Smooth(t / 0.1f) * MathF.Exp(-t * 0.9f * sp)).LowPass(900f, 0.2f), -3f, 0.03f);
            s.Sine(t => Ex(t, 88f, 44f, 0.1f), t => 1.0f * Hit(t, 4f * sp, 0.01f), 0f, 0.03f);
            s.Saw(t => 60f + 50f * t, t => 0.2f * Dsp.Smooth(t / 0.3f) * MathF.Exp(-t * 1.2f * sp), 0.2f);
            s.Drive(1.2f);
            s.Reverb(torpedo ? 1.6f : 0.9f, 0.18f, 1f, 0.55f, 8f, 0.6f);
            return s.Edges(0.5f, 60f).Normalize(-1f);
        }

        /// <summary>A passing missile's exhaust, looped (the nearest few follow their missiles).</summary>
        public static Sd MissileHiss()
        {
            var s = new Sd(2.5f);
            s.Mix(Noise(2.5f, 121u, t => 1f).BandPass(1500f, 0.35f, 2), 0f);
            s.Mix(Noise(2.5f, 122u, t => 0.8f + 0.2f * MathF.Sin(t * Dsp.TwoPi * 12f)).LowPass(500f), -6f);
            s.Sine(t => 120f, t => 0.25f);
            s.Tremolo(t => 24f, t => 0.12f);
            return s.MakeLoop(0.25f).Normalize(-1f);
        }

        public static Sd DecoyLaunch()
        {
            var s = new Sd(1.0f);
            s.Sine(t => Ex(t, 520f, 190f, 0.03f), t => 0.9f * Hit(t, 22f, 0.001f));
            s.Clang(1900f, 0.16f, 0.5f, 7u, 0.01f);
            s.Mix(Noise(1.0f, 131u, t => Hit(t, 3.2f, 0.01f)).HighPass(3800f), -8f, 0.03f);
            s.Crackle(t => 170f * MathF.Exp(-t * 2.8f), t => 0.55f, 132u, 4000f, 11000f, 4f, 0.04f);
            s.Sine(t => 900f + 3400f * Dsp.Clamp01(t / 0.7f), t => 0.1f * Dsp.Smooth(t / 0.3f) * MathF.Exp(-t * 3f), 0f, 0.1f);
            s.Reverb(0.8f, 0.12f, 0.6f, 0.4f, 3f, 0.4f);
            return s.Edges(0.4f, 40f).Normalize(-1f);
        }

        public static Sd DecoySuccess()
        {
            var s = new Sd(0.7f);
            s.Mix(Noise(0.7f, 141u, t => Hit(t, 22f, 0.0008f)).LowPass(t => Ex(t, 6500f, 600f, 0.05f)), 0f);
            s.Sine(t => Ex(t, 190f, 55f, 0.04f), t => 1.0f * Hit(t, 10f, 0.002f));
            s.Sine(t => Ex(t, 2400f, 500f, 0.08f), t => 0.3f * Hit(t, 16f));
            s.Drive(1.2f);
            s.Reverb(0.6f, 0.12f, 0.6f, 0.5f, 3f, 0.3f);
            return s.Edges(0.4f, 40f).Normalize(-1f);
        }

        /// <summary>The beam gathering: sparks pulled in, a rising whine, a growing rumble. 2.3 s, to match the charge.</summary>
        public static Sd BeamCharge()
        {
            const float L = 2.4f;
            var s = new Sd(L);
            s.Sine(t => 70f * MathF.Pow(14f, t / 2.3f), t => 0.35f * MathF.Pow(t / 2.3f, 1.6f) + 0.04f);
            s.Saw(t => 55f * MathF.Pow(12f, t / 2.3f), t => 0.28f * MathF.Pow(t / 2.3f, 2.4f), 0.2f);
            s.Ladder(t => 260f + 5200f * MathF.Pow(t / 2.3f, 2.2f), 0.5f, 1.3f);
            s.Mix(Noise(L, 151u, t => MathF.Pow(t / 2.3f, 3f)).BandPass(t => 600f + 6000f * MathF.Pow(t / 2.3f, 2f), 0.5f, 2), -6f);
            s.Sine(t => 34f + 14f * t / 2.3f, t => 1.0f * Dsp.Smooth(t / 0.7f) * (0.35f + 0.65f * t / 2.3f));
            s.Crackle(t => 8f + 260f * MathF.Pow(t / 2.3f, 2.2f), t => 0.25f * (0.4f + t / 2.3f), 152u, 2500f, 9000f, 5f);
            s.Tremolo(t => 6f + 18f * t / 2.3f, t => 0.25f * t / 2.3f);
            s.Drive(1.3f);
            s.Env(t => t < 2.3f ? 1f : MathF.Max(0f, 1f - (t - 2.3f) / 0.1f));
            s.Reverb(1.6f, 0.2f, 1.1f, 0.5f, 10f);
            return s.Edges(1f, 40f).Normalize(-1f);
        }

        /// <summary>The instant the beam fires: a wall-shaking crack, a falling shriek and a sub boom.</summary>
        public static Sd BeamFire()
        {
            var s = new Sd(2.2f);
            s.Mix(Noise(2.2f, 161u, t => Hit(t, 20f, 0.0003f)).HighPass(500f), 0f);
            s.Saw(t => Ex(t, 5200f, 160f, 0.1f), t => 0.55f * Hit(t, 3.5f, 0.001f));
            s.Saw(t => Ex(t, 5280f, 164f, 0.1f), t => 0.45f * Hit(t, 3.5f, 0.001f), 0.4f);
            s.Sine(t => Ex(t, 110f, 30f, 0.3f), t => 1.5f * Hit(t, 1.6f, 0.004f));
            s.Sine(t => Ex(t, 54f, 24f, 0.6f), t => 0.9f * Hit(t, 1.2f, 0.02f));
            s.Ladder(t => Ex(t, 9000f, 600f, 0.25f), 0.3f, 1.8f);
            s.Drive(1.8f);
            s.Reverb(2.0f, 0.3f, 1.2f, 0.5f, 6f, 1.2f);
            return s.Edges(0.4f, 80f).Normalize(-1f);
        }

        /// <summary>The beam's roar: a stack of low detuned saws beating against each other, a singing high partial, a band of noise breathing. Loops.</summary>
        public static Sd BeamLoop()
        {
            const float L = 4.5f;
            var s = new Sd(L);
            float[] f = { 55f, 55.5f, 82.5f, 110f, 110.75f, 165f, 220.5f };
            float[] g = { 0.8f, 0.7f, 0.45f, 0.4f, 0.35f, 0.22f, 0.12f };
            for (int i = 0; i < f.Length; i++) { float fi = f[i], gi = g[i]; int ii = i; s.Saw(t => fi * (1f + 0.002f * MathF.Sin(t * 6.1f + ii)), t => gi * (1f + 0.25f * MathF.Sin(t * Dsp.TwoPi * (6f + ii * 0.5f))), ii * 0.13f); }
            s.Mix(Noise(L, 171u, t => 0.7f + 0.3f * MathF.Sin(t * Dsp.TwoPi * 0.5f)).BandPass(t => 700f + 500f * MathF.Sin(t * Dsp.TwoPi * 0.5f), 0.55f, 2), -4f);
            s.Sine(t => 1100f + 40f * MathF.Sin(t * Dsp.TwoPi * 4f), t => 0.07f * (0.7f + 0.3f * MathF.Sin(t * Dsp.TwoPi * 0.75f)));
            s.Sine(t => 36f, t => 0.7f);
            s.Ladder(t => 2400f + 900f * MathF.Sin(t * Dsp.TwoPi * 0.25f), 0.25f, 1.4f);
            s.Drive(1.6f);
            return s.MakeLoop(0.4f).Normalize(-1f);
        }

        /// <summary>Where the beam bites: a hiss of molten metal, crackle and a low grind. Loops.</summary>
        public static Sd BeamImpactLoop()
        {
            const float L = 2.5f;
            var s = new Sd(L);
            s.Mix(Noise(L, 181u, t => 0.7f + 0.3f * MathF.Sin(t * Dsp.TwoPi * 8f)).HighPass(1800f), -2f);
            s.Crackle(t => 140f, t => 0.8f, 182u, 1500f, 8000f, 6f);
            s.Sine(t => 62f, t => 0.35f);
            s.Mix(Noise(L, 183u, t => 0.6f).BandPass(260f, 0.4f, 2), -3f);
            s.Drive(1.3f);
            return s.MakeLoop(0.3f).Normalize(-1f);
        }

        /// <summary>The beam letting go: the roar falls away and the metal ticks as it cools.</summary>
        public static Sd BeamEnd()
        {
            var s = new Sd(1.8f);
            s.Saw(t => Ex(t, 220f, 40f, 0.25f), t => 0.6f * Hit(t, 2.8f, 0.01f));
            s.Sine(t => Ex(t, 70f, 28f, 0.3f), t => 1.0f * Hit(t, 1.8f, 0.01f));
            s.Mix(Noise(1.8f, 191u, t => Hit(t, 3f, 0.01f)).LowPass(t => Ex(t, 3000f, 300f, 0.2f)), -6f);
            s.Crackle(t => 70f * MathF.Exp(-t * 2.2f), t => 0.5f, 192u, 1200f, 6000f, 6f);
            s.Ladder(t => Ex(t, 1800f, 160f, 0.3f), 0.2f, 1.3f);
            s.Drive(1.3f);
            s.Reverb(1.2f, 0.2f, 1f, 0.5f, 6f, 0.6f);
            return s.Edges(0.5f, 80f).Normalize(-1f);
        }

        /// <summary>A bolt passing close: a ripping hiss that rises into the ear and falls away, the Doppler of something fast.</summary>
        public static Sd NearMiss(int v)
        {
            float k = v == 0 ? 1f : 1.15f;
            var s = new Sd(0.8f);
            s.Mix(Noise(0.8f, 201u + (uint)v, t => MathF.Sin(Dsp.Clamp01(t / 0.55f) * Dsp.Pi) * MathF.Exp(-t * 1.2f)).BandPass(t => (Ex(t, 4600f, 480f, 0.2f)) * k, 0.55f, 2), 3f);
            s.Sine(t => Ex(t, 780f, 210f, 0.22f) * k, t => 0.25f * MathF.Sin(Dsp.Clamp01(t / 0.5f) * Dsp.Pi));
            s.Sine(t => Ex(t, 120f, 50f, 0.2f), t => 0.3f * MathF.Sin(Dsp.Clamp01(t / 0.4f) * Dsp.Pi));
            s.Drive(1.2f);
            return s.Edges(1f, 60f).Normalize(-1f);
        }
    }
}
