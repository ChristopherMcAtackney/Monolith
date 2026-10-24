using System;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    /// <summary>The ship around the pilot (engine, afterburner, cabin air, alarms), arrivals and departures, Earth's horn, and the small sounds of the interface.</summary>
    public static class SfxShip
    {
        static float Hit(float t, float k, float a = 0.001f) => Sd.Hit(t, k, a);
        static float Ex(float t, float a, float b, float tau) => Sd.Expo(t, a, b, tau);
        static Sd Noise(float len, uint seed, Func<float, float> amp, float colour = 1f) { var n = new Sd(len); n.Noise(amp, seed, colour); return n; }

        /// <summary>The engine's low hum: two detuned oscillators that beat slowly, a sub, soft harmonics, a breath of air. 4 s, closes on itself exactly (every frequency is a multiple of 0.25 Hz).</summary>
        public static Sd EngineLoop()
        {
            const float L = 4f;
            var s = new Sd(L);
            s.Saw(t => 55f, t => 0.7f);
            s.Saw(t => 56.25f, t => 0.7f, 0.3f);
            s.Pulse(t => 110.5f, t => 0.25f, t => 0.3f + 0.15f * MathF.Sin(t * Dsp.TwoPi * 0.5f));
            s.Sine(t => 27.5f, t => 1.0f);
            s.Sine(t => 165f, t => 0.1f);
            s.Ladder(t => 300f + 90f * MathF.Sin(t * Dsp.TwoPi * 0.25f), 0.3f, 1.3f);
            s.Tremolo(t => 2f, t => 0.18f);
            s.Drive(1.4f);
            var air = Noise(L + 0.5f, 401u, t => 0.55f).LowPass(520f, 0.1f, 2).MakeLoop(0.5f);
            s.Mix(air, -14f);
            return s.Normalize(-3f);
        }

        /// <summary>The afterburner: a roar of burning fuel, the flutter of flame, a deep rumble. 3 s loop.</summary>
        public static Sd AfterburnerLoop()
        {
            const float L = 3.4f;
            var s = new Sd(L);
            var flutter = Noise(L, 411u, t => 1f, 0.02f).LowPass(30f, 0.1f);
            s.Mix(Noise(L, 412u, t => 1f).BandPass(t => 900f, 0.3f, 2), 0f);
            s.Mix(Noise(L, 413u, t => 1f).LowPass(240f, 0.2f, 2), 4f);
            s.Mix(Noise(L, 414u, t => 1f).HighPass(3000f), -14f);
            s.Env(t => 0.65f + 0.35f * Math.Clamp(flutter.X[Math.Min(flutter.N - 1, (int)(t * Sd.Rate))] * 14f + 0.4f, 0f, 1f));
            s.Sine(t => 41f + 3f * MathF.Sin(t * 5f), t => 0.9f);
            s.Fm(t => 640f, 0.5f, t => 3f + 2f * MathF.Sin(t * 3f), t => 0.06f);
            s.Drive(1.6f);
            return s.MakeLoop(0.4f).Normalize(-3f);
        }

        public static Sd AfterburnerOn()
        {
            var s = new Sd(1.4f);
            s.Mix(Noise(1.4f, 421u, t => Dsp.Smooth(t / 0.25f) * MathF.Exp(-t * 1.4f)).BandPass(t => 300f + 1400f * Dsp.Clamp01(t / 0.9f), 0.35f, 2), 3f);
            s.Sine(t => Ex(t, 120f, 40f, 0.2f), t => 1.3f * Dsp.Smooth(t / 0.08f) * MathF.Exp(-t * 2.2f));
            s.Mix(Noise(1.4f, 422u, t => Hit(t, 30f, 0.001f)).HighPass(1400f), -8f, 0.04f);
            s.Drive(1.5f);
            s.Reverb(0.6f, 0.1f, 0.6f, 0.6f, 3f, 0.3f);
            return s.Edges(0.4f, 80f).Normalize(-2f);
        }

        public static Sd AfterburnerOff()
        {
            var s = new Sd(1.0f);
            s.Mix(Noise(1.0f, 431u, t => MathF.Exp(-t * 4.5f)).LowPass(t => Ex(t, 2400f, 220f, 0.2f), 0.2f, 2), 2f);
            s.Sine(t => Ex(t, 60f, 32f, 0.3f), t => 1.0f * MathF.Exp(-t * 3.5f));
            s.Drive(1.3f);
            return s.Edges(0.5f, 100f).Normalize(-3f);
        }

        /// <summary>Cabin air: ventilation hiss, the 120 Hz hum of a fan, now and then a tick of cooling metal. 8 s.</summary>
        public static Sd CabinAir()
        {
            const float L = 8.5f;
            var s = new Sd(L);
            s.Mix(Noise(L, 441u, t => 1f).LowPass(700f, 0.1f, 2).HighPass(90f), -4f);
            s.Mix(Noise(L, 442u, t => 0.7f + 0.3f * MathF.Sin(t * Dsp.TwoPi * 0.125f)).BandPass(2400f, 0.4f, 1), -16f);
            s.Sine(t => 120f, t => 0.1f);
            s.Sine(t => 240.25f, t => 0.04f);
            s.Sine(t => 60f, t => 0.08f);
            var rng = new Rng(443);
            for (int i = 0; i < 5; i++) s.Clang(rng.Range(600f, 1400f), 0.06f, 0.06f, (uint)(444 + i), rng.Range(0.5f, L - 1.5f));
            return s.MakeLoop(0.5f).Normalize(-12f);
        }

        /// <summary>The hull alarm: two tones, alternating, from a small cheap speaker. A 2 s loop.</summary>
        public static Sd AlarmHull()
        {
            var s = new Sd(2.5f);
            for (int i = 0; i < 6; i++)
            {
                float f = (i & 1) == 0 ? 760f : 1010f, at = i * (1f / 3f);
                s.Pulse(t => f * (1f + 0.003f * MathF.Sin(t * 40f)), t => t < at || t > at + 0.31f ? 0f : 0.7f * Dsp.Smooth((t - at) / 0.006f) * Dsp.Smooth((at + 0.31f - t) / 0.012f), t => 0.35f);
            }
            s.BandPass(1100f, 0.3f, 1);
            s.Drive(1.8f);
            s.Reverb(0.5f, 0.08f, 0.4f, 0.7f, 2f, 0.1f);
            s.Trim(2.0f);
            return s.Normalize(-3f);
        }

        /// <summary>A single warning chirp (three of them make a master warning).</summary>
        public static Sd AlarmBeep(float hz = 1180f)
        {
            var s = new Sd(0.22f);
            s.Pulse(t => hz, t => 0.8f * Dsp.Smooth(t / 0.004f) * Dsp.Smooth((0.2f - t) / 0.01f), t => 0.4f);
            s.Sine(t => hz * 2f, t => 0.2f * Hit(t, 14f));
            s.BandPass(hz * 1.2f, 0.25f, 1);
            s.Drive(1.4f);
            return s.Normalize(-3f);
        }

        /// <summary>A system going down: a short electrical failure, a tone dying.</summary>
        public static Sd SystemFail()
        {
            var s = new Sd(0.9f);
            s.Sine(t => Ex(t, 900f, 110f, 0.12f), t => 0.7f * Hit(t, 5f, 0.002f));
            s.Mix(Noise(0.9f, 451u, t => Hit(t, 10f, 0.001f)).HighPass(1800f), -6f);
            s.Crackle(t => 160f * MathF.Exp(-t * 4f), t => 0.5f, 452u, 1500f, 9000f, 4f);
            s.Tremolo(t => 22f, t => 0.6f * (1f - t));
            s.Drive(1.5f);
            return s.Edges(0.4f, 100f).Normalize(-2f);
        }

        /// <summary>An arrival: the flash. A rising charge, then a crack, a sub bump and a fading shimmer. 'big' for a capital ship.</summary>
        public static Sd JumpIn(bool big)
        {
            float L = big ? 5.2f : 2.4f, z = big ? 1.8f : 1f;
            var s = new Sd(L);
            float flash = 0.24f * z;
            s.Sine(t => 300f * MathF.Pow(18f, Dsp.Clamp01(t / flash)), t => t < flash ? 0.5f * Dsp.Smooth(t / flash) : 0f);
            s.Saw(t => 120f * MathF.Pow(24f, Dsp.Clamp01(t / flash)), t => t < flash ? 0.3f * MathF.Pow(t / flash, 2f) : 0f);
            s.Mix(Noise(L, 461u, t => Hit(t, 24f / z, 0.0004f)).HighPass(700f), 1f, flash);
            s.Sine(t => Ex(t, 120f, 32f, 0.12f * z), t => 1.4f * Hit(t, 2.6f / z, 0.004f), 0f, flash);
            s.Sine(t => Ex(t, 60f, 28f, 0.5f * z), t => 0.8f * Hit(t, 1.3f / z, 0.02f), 0f, flash);
            for (int i = 0; i < 5; i++)
            {
                float f = 2200f * MathF.Pow(1.37f, i);
                s.Sine(t => f * (1f + 0.01f * MathF.Sin(t * 7f + f)), t => 0.1f * Hit(t, 1.8f / z, 0.01f) / (1f + i * 0.4f), 0f, flash);
            }
            s.Mix(Noise(L, 462u, t => Hit(t, 3f / z, 0.01f)).BandPass(t => Ex(t, 5000f, 800f, 0.3f * z), 0.4f, 2), -8f, flash);
            s.Flange(t => 1.5f + 1.2f * MathF.Sin(t * 5f), 0.4f, 0.3f);
            s.Drive(1.4f);
            s.Reverb(big ? 3.2f : 1.5f, 0.28f, big ? 1.4f : 1f, 0.5f, 6f, big ? 1.5f : 0.8f);
            return s.Edges(0.4f, 150f).Normalize(-1f);
        }

        /// <summary>A departure: space folding shut. A swell that sucks inward, a pop.</summary>
        public static Sd JumpOut(bool big)
        {
            float L = big ? 3.4f : 2.0f, z = big ? 1.6f : 1f;
            var s = new Sd(L);
            float end = L - 0.5f * z;
            s.Mix(Noise(L, 471u, t => MathF.Pow(Dsp.Clamp01(t / end), 2.2f) * (t < end ? 1f : 0f)).BandPass(t => 200f + 6000f * MathF.Pow(Dsp.Clamp01(t / end), 2f), 0.4f, 2), 2f);
            s.Sine(t => 900f * MathF.Pow(0.08f, Dsp.Clamp01(t / end)) + 40f, t => 0.4f * MathF.Pow(Dsp.Clamp01(t / end), 1.5f) * (t < end ? 1f : 0f));
            s.Sine(t => 38f, t => 0.9f * Dsp.Smooth(t / (0.5f * end)) * (t < end ? 1f : 0f));
            s.Mix(Noise(L, 472u, t => Hit(t, 40f, 0.0003f)).HighPass(1200f), -3f, end);
            s.Sine(t => Ex(t, 110f, 30f, 0.1f), t => 1.2f * Hit(t, 6f, 0.002f), 0f, end);
            s.Drive(1.4f);
            s.Reverb(big ? 2.4f : 1.2f, 0.2f, 1.1f, 0.5f, 5f, 0.8f);
            return s.Edges(1f, 120f).Normalize(-1f);
        }

        /// <summary>The Reaper-like horn that announces Earth's capital ships: a chord of low brass far below the range of speakers, swelling on with a hard bite then settling into a sustained dread, and the long fall.</summary>
        public static Sd EarthHorn(bool big)
        {
            float L = big ? 9.0f : 5.5f, z = big ? 1f : 0.62f;
            var s = new Sd(L);
            float[] roots = { 36.71f, 55.0f, 73.42f, 110.0f };        // D1, A1, D2, A2
            float[] gains = { 1.0f, 0.75f, 0.85f, 0.4f };
            for (int r = 0; r < roots.Length; r++)
                for (int d = -1; d <= 1; d++)
                {
                    float f0 = roots[r] * MathF.Pow(2f, d * 14f / 1200f), g = gains[r];
                    int rr = r; int dd = d;
                    s.Saw(t => f0 * (1f + 0.045f * MathF.Exp(-t / (0.35f * z)) - 0.0035f * Dsp.Clamp01(t / L)), t => g * Sd.Swell(t, 0.1f, 2.8f * z, L) * (0.8f + 0.2f * MathF.Sin(t * 0.9f + rr + dd)), (r * 0.31f + (d + 1) * 0.17f));
                }
            s.Ladder(t => 70f + 2300f * MathF.Exp(-t / (0.5f * z)) + 420f * MathF.Exp(-t / (2.2f * z)) * (0.8f + 0.2f * MathF.Sin(t * 0.7f)), 0.28f, 1.6f);
            s.Drive(2.4f, 0.05f);
            s.Sine(t => 36.71f * (1f + 0.04f * MathF.Exp(-t / (0.4f * z))), t => 1.3f * Sd.Swell(t, 0.12f, 3f * z, L));
            var scream = new Sd(L);
            scream.Saw(t => 440f * (1f + 0.02f * MathF.Exp(-t / 0.4f)), t => 0.22f * Sd.Swell(t, 0.2f, 2f * z, L)).BandPass(t => 700f + 500f * MathF.Exp(-t / 0.8f), 0.55f, 2);
            s.Mix(scream, -9f);
            s.Mix(Noise(L, 481u, t => Hit(t, 6f, 0.005f)).LowPass(t => Ex(t, 3800f, 300f, 0.3f)), -9f);
            s.Reverb(big ? 5.5f : 3.6f, 0.45f, 1.6f, 0.45f, 18f, big ? 3.0f : 2.0f, 40f);
            return s.Edges(1f, 600f).Normalize(-1f);
        }

        /// <summary>A scan sweeping past the ship: a slow rising pulse of energy that crawls over the hull, then a long falling shimmer. 4 s.</summary>
        public static Sd ScanSweep()
        {
            const float L = 4.5f;
            var s = new Sd(L);
            float peak = 1.7f;
            s.Sine(t => 180f + 520f * MathF.Pow(Dsp.Clamp01(t / peak), 1.5f) - 360f * Dsp.Clamp01((t - peak) / 2.4f), t => 0.5f * Dsp.Smooth(t / 1.2f) * Dsp.Smooth((L - t) / 1.2f));
            s.Sine(t => 360.5f + 1040f * MathF.Pow(Dsp.Clamp01(t / peak), 1.5f) - 720f * Dsp.Clamp01((t - peak) / 2.4f), t => 0.18f * Dsp.Smooth(t / 1.4f) * Dsp.Smooth((L - t) / 1.4f));
            s.Mix(Noise(L, 495u, t => Dsp.Smooth(t / 1.4f) * Dsp.Smooth((L - t) / 1.6f)).BandPass(t => 900f + 2600f * Dsp.Smooth(Dsp.Clamp01(t / peak)), 0.6f, 2), -11f);
            s.Tremolo(t => 7f, t => 0.35f);
            s.Sine(t => 41f, t => 0.35f * Dsp.Smooth(t / 1.5f) * Dsp.Smooth((L - t) / 1.5f));
            s.Drive(1.3f);
            s.Reverb(2.6f, 0.45f, 1.3f, 0.5f, 18f, 1.5f);
            return s.Edges(20f, 300f).Normalize(-3f);
        }

        // ------------------------------------------------------------------------------------------------ the interface

        static Sd Tone(float len, Func<float, float> f, float a = 0.7f, float decay = 18f)
        {
            var s = new Sd(len);
            s.Sine(f, t => a * Hit(t, decay, 0.002f));
            s.Sine(t => f(t) * 2.01f, t => 0.15f * a * Hit(t, decay * 1.5f, 0.002f));
            return s;
        }

        public static Sd UiClick() => Tone(0.07f, t => 1700f, 0.7f, 70f).Normalize(-6f).Edges(0.3f, 8f);
        public static Sd UiSelect()
        {
            var s = Tone(0.3f, t => 880f, 0.7f, 14f);
            s.Mix(Tone(0.3f, t => 1320f, 0.7f, 14f), -2f, 0.07f);
            return s.Reverb(0.5f, 0.1f, 0.4f, 0.6f, 2f, 0.2f).Normalize(-6f).Edges(0.3f, 30f);
        }
        public static Sd TargetBlip() => Tone(0.16f, t => Ex(t, 1900f, 1250f, 0.02f), 0.6f, 24f).Normalize(-8f).Edges(0.3f, 20f);
        public static Sd LockAcquired()
        {
            var s = new Sd(0.5f);
            s.Sine(t => 1320f, t => 0.5f * Hit(t, 7f, 0.003f));
            s.Sine(t => 1980f, t => 0.35f * Hit(t, 7f, 0.003f), 0f, 0.09f);
            s.Sine(t => 2640f, t => 0.25f * Hit(t, 8f, 0.003f), 0f, 0.18f);
            s.Echo(0.12f, 0.35f, 0.4f, 5000f);
            return s.Normalize(-6f).Edges(0.3f, 40f);
        }
        public static Sd LockLost() => Tone(0.4f, t => Ex(t, 1100f, 330f, 0.12f), 0.6f, 8f).Normalize(-7f).Edges(0.3f, 40f);
        public static Sd PowerBlip() => Tone(0.2f, t => 640f + 220f * Dsp.Clamp01(t / 0.05f), 0.6f, 16f).Normalize(-8f).Edges(0.3f, 20f);

        /// <summary>Radio squelch: the carrier opening (a click, a burst of static) and closing (a shorter burst, a click).</summary>
        public static Sd SquelchOpen()
        {
            var s = new Sd(0.16f);
            s.Mix(Noise(0.16f, 491u, t => Hit(t, 24f, 0.0004f)).BandPass(2200f, 0.35f, 1), 0f);
            s.Sine(t => 1800f, t => 0.25f * Hit(t, 60f, 0.0004f));
            return s.Normalize(-9f).Edges(0.2f, 10f);
        }
        public static Sd SquelchClose()
        {
            var s = new Sd(0.2f);
            s.Mix(Noise(0.2f, 492u, t => Hit(t, 30f, 0.0003f)).BandPass(1700f, 0.35f, 1), 0f);
            s.Sine(t => 1200f, t => 0.2f * Hit(t, 80f, 0.0003f));
            return s.Normalize(-10f).Edges(0.2f, 12f);
        }
    }
}
