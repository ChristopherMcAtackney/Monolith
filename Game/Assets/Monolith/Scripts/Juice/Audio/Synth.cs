using System;
using UnityEngine;

namespace Monolith.Juice
{
    /// <summary>
    /// A few lines of sound synthesis for the placeholder effects: oscillators with sweeping pitch, shaped noise, swept filters,
    /// envelopes. Every function works on mono float buffers at <see cref="Rate"/> and uses fixed seeds, so the same code always
    /// makes the same sound. (The real audio system will replace the sounds this builds.)
    /// </summary>
    public static class Synth
    {
        public const int Rate = 44100;
        public const float TwoPi = Mathf.PI * 2f;

        public static float[] Buf(float seconds) => new float[Mathf.Max(1, (int)(seconds * Rate))];
        public static float T(int i) => i / (float)Rate;

        /// <summary>exp(-t * k), the plain decay.</summary>
        public static float Decay(float t, float k) => Mathf.Exp(-t * k);

        /// <summary>A short rise then a decay: 1 ms attack by default.</summary>
        public static float Hit(float t, float decay, float attack = 0.001f) => Mathf.Min(1f, t / attack) * Mathf.Exp(-t * decay);

        public static void AddSine(float[] b, Func<float, float> freq, Func<float, float> amp, float phase0 = 0f)
        {
            double ph = phase0;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate;
                ph += TwoPi * freq(t) / Rate;
                b[i] += Mathf.Sin((float)ph) * amp(t);
            }
        }

        /// <summary>A band-limited-ish saw (a few sines would be cleaner; this is gritty on purpose).</summary>
        public static void AddSaw(float[] b, Func<float, float> freq, Func<float, float> amp, float shape = 0f)
        {
            double ph = 0;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate;
                ph += freq(t) / Rate;
                ph -= Math.Floor(ph);
                float saw = (float)ph * 2f - 1f;
                float sq = ph < 0.5 ? -1f : 1f;
                b[i] += Mathf.Lerp(saw, sq, shape) * amp(t);
            }
        }

        public static void AddNoise(float[] b, Func<float, float> amp, int seed)
        {
            var r = new System.Random(seed);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate;
                b[i] += ((float)r.NextDouble() * 2f - 1f) * amp(t);
            }
        }

        /// <summary>Random clicks (crackle): about <paramref name="density"/> per second at t, fading with <paramref name="amp"/>.</summary>
        public static void AddCrackle(float[] b, Func<float, float> density, Func<float, float> amp, int seed, float clickMs = 1.5f)
        {
            var r = new System.Random(seed);
            int len = Mathf.Max(2, (int)(clickMs * 0.001f * Rate));
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate;
                if (r.NextDouble() < density(t) / Rate)
                {
                    float a = amp(t) * (0.4f + 0.6f * (float)r.NextDouble()) * ((r.NextDouble() < 0.5) ? -1f : 1f);
                    for (int k = 0; k < len && i + k < b.Length; k++) b[i + k] += a * Mathf.Exp(-k * 4f / len);
                }
            }
        }

        /// <summary>A one-pole low-pass with a cutoff that follows time (twice, for 12 dB per octave).</summary>
        public static void LowPass(float[] b, Func<float, float> cutoffHz, int passes = 2)
        {
            for (int p = 0; p < passes; p++)
            {
                float y = 0f;
                for (int i = 0; i < b.Length; i++)
                {
                    float fc = Mathf.Clamp(cutoffHz(i / (float)Rate), 10f, Rate * 0.45f);
                    float a = 1f - Mathf.Exp(-TwoPi * fc / Rate);
                    y += a * (b[i] - y);
                    b[i] = y;
                }
            }
        }

        public static void HighPass(float[] b, Func<float, float> cutoffHz, int passes = 1)
        {
            for (int p = 0; p < passes; p++)
            {
                float y = 0f;
                for (int i = 0; i < b.Length; i++)
                {
                    float fc = Mathf.Clamp(cutoffHz(i / (float)Rate), 10f, Rate * 0.45f);
                    float a = 1f - Mathf.Exp(-TwoPi * fc / Rate);
                    y += a * (b[i] - y);
                    b[i] -= y;
                }
            }
        }

        /// <summary>A resonant band-pass (state variable) with a moving centre.</summary>
        public static void BandPass(float[] b, Func<float, float> centreHz, float q = 1.2f)
        {
            float low = 0f, band = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float fc = Mathf.Clamp(centreHz(i / (float)Rate), 40f, Rate * 0.2f);
                float f = 2f * Mathf.Sin(Mathf.PI * fc / Rate);
                float high = b[i] - low - band / q;
                band += f * high;
                low += f * band;
                b[i] = band;
            }
        }

        /// <summary>Multiply the buffer by an envelope.</summary>
        public static void Shape(float[] b, Func<float, float> env)
        {
            for (int i = 0; i < b.Length; i++) b[i] *= env(i / (float)Rate);
        }

        public static void Mix(float[] dst, float[] src, float gain = 1f, float delaySeconds = 0f)
        {
            int off = Mathf.RoundToInt(delaySeconds * Rate);
            for (int i = 0; i < src.Length; i++)
            {
                int j = i + off;
                if (j >= dst.Length) break;
                if (j >= 0) dst[j] += src[i] * gain;
            }
        }

        public static void SoftClip(float[] b, float drive = 1.5f)
        {
            float norm = 1f / (float)Math.Tanh(drive);
            for (int i = 0; i < b.Length; i++) b[i] = (float)Math.Tanh(b[i] * drive) * norm;
        }

        public static float Peak(float[] b)
        {
            float p = 0f;
            for (int i = 0; i < b.Length; i++) { float a = Mathf.Abs(b[i]); if (a > p) p = a; }
            return p;
        }

        public static float Rms(float[] b)
        {
            double s = 0;
            for (int i = 0; i < b.Length; i++) s += b[i] * (double)b[i];
            return (float)Math.Sqrt(s / Math.Max(1, b.Length));
        }

        public static void Normalize(float[] b, float peak = 0.8f)
        {
            float p = Peak(b);
            if (p < 1e-6f) return;
            float g = peak / p;
            for (int i = 0; i < b.Length; i++) b[i] *= g;
        }

        /// <summary>Fade the first and last <paramref name="seconds"/> so a clip never clicks.</summary>
        public static void Edges(float[] b, float fadeIn = 0.002f, float fadeOut = 0.02f)
        {
            int a = Mathf.Max(1, (int)(fadeIn * Rate)), z = Mathf.Max(1, (int)(fadeOut * Rate));
            for (int i = 0; i < a && i < b.Length; i++) b[i] *= i / (float)a;
            for (int i = 0; i < z && i < b.Length; i++) b[b.Length - 1 - i] *= i / (float)z;
        }

        /// <summary>Make a loop seamless by cross-fading its tail into its head.</summary>
        public static float[] SeamlessLoop(float[] b, float overlapSeconds)
        {
            int ov = Mathf.Min((int)(overlapSeconds * Rate), b.Length / 3);
            var o = new float[b.Length - ov];
            Array.Copy(b, o, o.Length);
            for (int i = 0; i < ov; i++)
            {
                float k = i / (float)ov;
                o[i] = b[i] * k + b[o.Length + i] * (1f - k);
            }
            return o;
        }

        /// <summary>The same sound heard from far away: dull, softened, a little late and longer (low frequencies carry).</summary>
        public static float[] Far(float[] near, float extraSeconds = 0.5f)
        {
            var f = new float[near.Length + (int)(extraSeconds * Rate)];
            Array.Copy(near, f, near.Length);
            LowPass(f, t => Mathf.Lerp(900f, 260f, Mathf.Clamp01(t / 2.5f)), 2);
            // Soften the attack: a short moving average smears the transient.
            int w = (int)(0.012f * Rate);
            var sm = new float[f.Length];
            double acc = 0;
            for (int i = 0; i < f.Length; i++)
            {
                acc += f[i];
                if (i >= w) acc -= f[i - w];
                sm[i] = (float)(acc / w);
            }
            // A little room: two quiet echoes.
            var o = new float[sm.Length];
            Array.Copy(sm, o, sm.Length);
            Mix(o, sm, 0.35f, 0.11f);
            Mix(o, sm, 0.2f, 0.27f);
            Edges(o, 0.01f, 0.12f);
            return o;
        }
    }
}
