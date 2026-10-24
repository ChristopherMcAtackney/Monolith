using System;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    /// <summary>
    /// The drum machine: a LinnDrum-and-Simmons style kit synthesised from sines and noise (nothing sampled). Each hit is a mono buffer; the
    /// renderer places them from the MIDI drum track (General MIDI note numbers, plus 33 = cinematic boom, 34 = taiko/timpani hit).
    /// </summary>
    public static class Drums
    {
        /// <summary>The kit is made 15 dB under full scale, so that 0 dB on the drum track sits with the calibrated synth patches.</summary>
        const float KitLevel = 0.18f;

        public static float[] Hit(int note, int velocity, int rate, uint seed)
        {
            var h = HitRaw(note, velocity, rate, seed);
            for (int i = 0; i < h.Length; i++) h[i] *= KitLevel;
            return h;
        }

        static float[] HitRaw(int note, int velocity, int rate, uint seed)
        {
            float v = Math.Max(0.05f, velocity / 127f);
            var rng = new Rng(Osc.Hash(seed, (uint)note, (uint)velocity));
            switch (note)
            {
                case 33: return Boom(rate, rng, v);
                case 34: return Taiko(rate, rng, v);
                case 35: case 36: return Kick(rate, rng, v, 52f, 0.30f, 150f);
                case 37: return Rim(rate, rng, v);
                case 38: return Snare(rate, rng, v, 190f, 0.20f);
                case 40: return Snare(rate, rng, v, 235f, 0.16f);
                case 39: return Clap(rate, rng, v);
                case 41: return Tom(rate, rng, v, 78f, 0.55f);
                case 43: return Tom(rate, rng, v, 98f, 0.50f);
                case 45: return Tom(rate, rng, v, 124f, 0.46f);
                case 47: return Tom(rate, rng, v, 152f, 0.42f);
                case 48: return Tom(rate, rng, v, 184f, 0.38f);
                case 50: return Tom(rate, rng, v, 222f, 0.34f);
                case 42: case 44: return Hat(rate, rng, v, 0.042f);
                case 46: return Hat(rate, rng, v, 0.28f);
                case 49: case 57: return Crash(rate, rng, v, 2.4f);
                case 51: case 53: return Crash(rate, rng, v, 1.1f);
                case 55: return Crash(rate, rng, v, 0.9f);
                case 56: return Cowbell(rate, rng, v);
                default: return Rim(rate, rng, v);
            }
        }

        /// <summary>Send levels a drum gets by default: (gated reverb, hall, plate).</summary>
        public static (float gate, float hall, float plate) Sends(int note)
        {
            switch (note)
            {
                case 38: case 40: return (0.75f, 0f, 0.1f);
                case 39: return (0.55f, 0f, 0.15f);
                case 41: case 43: case 45: case 47: case 48: case 50: return (0.65f, 0.1f, 0f);
                case 33: case 34: return (0f, 0.55f, 0f);
                case 49: case 57: case 51: case 55: return (0f, 0.3f, 0f);
                case 42: case 44: case 46: return (0f, 0f, 0.08f);
                case 37: case 56: return (0f, 0f, 0.2f);
                default: return (0f, 0f, 0f);
            }
        }

        static float[] Buf(float seconds, int rate) => new float[(int)(seconds * rate)];

        static void Norm(float[] b, float peak)
        {
            float p = 0; foreach (var x in b) p = MathF.Max(p, MathF.Abs(x));
            if (p > 0) { float g = peak / p; for (int i = 0; i < b.Length; i++) b[i] *= g; }
        }

        static float[] Kick(int rate, Rng rng, float v, float baseHz, float decay, float sweepHz)
        {
            var b = Buf(decay * 2.4f + 0.1f, rate);
            float ph = 0f;
            var hp = Biquad.Make(BiquadType.HighPass, rate, 2500f, 0.7f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)rate;
                float f = baseHz + sweepHz * MathF.Exp(-t / 0.016f) + 40f * MathF.Exp(-t / 0.06f);
                ph += f / rate; if (ph > 1f) ph -= 1f;
                float body = MathF.Sin(ph * Dsp.TwoPi) * MathF.Exp(-t / decay);
                float click = hp.Process(rng.Signed()) * MathF.Exp(-t / 0.0035f) * 0.45f;
                b[i] = Dsp.Tanh((body * 1.15f + click) * (1.2f + v)) ;
            }
            Norm(b, 0.95f * (0.45f + 0.55f * v));
            return b;
        }

        static float[] Boom(int rate, Rng rng, float v)
        {
            var b = Buf(4.5f, rate);
            float ph = 0f, ph2 = 0f;
            var lp = Biquad.Make(BiquadType.LowPass, rate, 400f, 0.7f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)rate;
                float f = 34f + 70f * MathF.Exp(-t / 0.18f);
                ph += f / rate; ph2 += f * 1.5f / rate;
                float body = MathF.Sin(ph * Dsp.TwoPi) * MathF.Exp(-t / 1.1f) + 0.3f * MathF.Sin(ph2 * Dsp.TwoPi) * MathF.Exp(-t / 0.35f);
                float nz = lp.Process(rng.Signed()) * MathF.Exp(-t / 0.5f) * 0.7f;
                b[i] = Dsp.Tanh((body + nz) * 1.6f);
            }
            Norm(b, 0.95f * (0.5f + 0.5f * v));
            return b;
        }

        static float[] Taiko(int rate, Rng rng, float v)
        {
            var b = Buf(3.0f, rate);
            float ph = 0f, ph2 = 0f, ph3 = 0f;
            var bp = Biquad.Make(BiquadType.BandPass, rate, 900f, 0.8f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)rate;
                float f = 56f + 38f * MathF.Exp(-t / 0.05f);
                ph += f / rate; ph2 += f * 1.58f / rate; ph3 += f * 2.3f / rate;
                float body = MathF.Sin(ph * Dsp.TwoPi) * MathF.Exp(-t / 0.7f)
                           + 0.35f * MathF.Sin(ph2 * Dsp.TwoPi) * MathF.Exp(-t / 0.28f) + 0.2f * MathF.Sin(ph3 * Dsp.TwoPi) * MathF.Exp(-t / 0.14f);
                float skin = bp.Process(rng.Signed()) * MathF.Exp(-t / 0.02f) * 0.9f;
                b[i] = Dsp.Tanh((body + skin) * 1.4f);
            }
            Norm(b, 0.95f * (0.45f + 0.55f * v));
            return b;
        }

        static float[] Snare(int rate, Rng rng, float v, float toneHz, float tail)
        {
            var b = Buf(tail + 0.4f, rate);
            float ph = 0f, ph2 = 0f;
            var bp = Biquad.Make(BiquadType.BandPass, rate, 2200f, 0.55f);
            var hp = Biquad.Make(BiquadType.HighPass, rate, 600f, 0.7f);
            var hp2 = Biquad.Make(BiquadType.HighPass, rate, 5200f, 0.7f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)rate;
                float f = toneHz * (1f + 0.55f * MathF.Exp(-t / 0.012f));
                ph += f / rate; ph2 += f * 1.75f / rate;
                float tone = (MathF.Sin(ph * Dsp.TwoPi) * MathF.Exp(-t / 0.075f) + 0.5f * MathF.Sin(ph2 * Dsp.TwoPi) * MathF.Exp(-t / 0.05f)) * 0.85f;
                float nz = rng.Signed();
                float body = bp.Process(hp.Process(nz)) * (0.6f * MathF.Exp(-t / 0.04f) + 0.55f * MathF.Exp(-t / tail));
                float crack = hp2.Process(nz) * MathF.Exp(-t / 0.011f) * 0.8f;
                b[i] = Dsp.Tanh((tone + body * 1.6f + crack) * (1.1f + v));
            }
            Norm(b, 0.95f * (0.4f + 0.6f * v));
            return b;
        }

        static float[] Tom(int rate, Rng rng, float v, float hz, float decay)
        {
            var b = Buf(decay * 2.2f, rate);
            float ph = 0f;
            var bp = Biquad.Make(BiquadType.BandPass, rate, 1400f, 0.9f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)rate;
                float f = hz * (1f + 1.1f * MathF.Exp(-t / 0.035f));
                ph += f / rate; if (ph > 1f) ph -= 1f;
                float body = MathF.Sin(ph * Dsp.TwoPi) * MathF.Exp(-t / decay);
                float tick = bp.Process(rng.Signed()) * MathF.Exp(-t / 0.008f) * 0.5f;
                b[i] = Dsp.Tanh((body + tick) * (1.2f + v));
            }
            Norm(b, 0.93f * (0.45f + 0.55f * v));
            return b;
        }

        static readonly float[] Metal = { 205.3f, 304.4f, 369.6f, 522.7f, 540.0f, 800.0f };

        static float[] Hat(int rate, Rng rng, float v, float decay)
        {
            var b = Buf(decay * 4f + 0.05f, rate);
            var ph = new float[6];
            var hp = Biquad.Make(BiquadType.HighPass, rate, 7200f, 0.8f);
            var bp = Biquad.Make(BiquadType.BandPass, rate, 10000f, 0.7f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)rate;
                float s = 0f;
                for (int k = 0; k < 6; k++) { ph[k] += Metal[k] * 2.6f / rate; if (ph[k] > 1f) ph[k] -= 1f; s += ph[k] < 0.5f ? 1f : -1f; }
                s = s / 6f * 0.7f + rng.Signed() * 0.35f;
                b[i] = hp.Process(bp.Process(s)) * MathF.Exp(-t / decay) * (t < 0.0008f ? t / 0.0008f : 1f);
            }
            Norm(b, 0.55f * (0.4f + 0.6f * v));
            return b;
        }

        static float[] Clap(int rate, Rng rng, float v)
        {
            var b = Buf(0.5f, rate);
            var bp = Biquad.Make(BiquadType.BandPass, rate, 1300f, 1.2f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)rate;
                float env = 0f;
                for (int k = 0; k < 4; k++) { float tk = t - k * 0.0095f; if (tk >= 0f) env += MathF.Exp(-tk / 0.004f) * 0.8f; }
                float tail = t >= 0.038f ? MathF.Exp(-(t - 0.038f) / 0.09f) : 0f;
                b[i] = bp.Process(rng.Signed()) * (env + tail);
            }
            Norm(b, 0.9f * (0.45f + 0.55f * v));
            return b;
        }

        static float[] Rim(int rate, Rng rng, float v)
        {
            var b = Buf(0.12f, rate);
            float ph = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)rate;
                ph += (1750f + 700f * MathF.Exp(-t / 0.003f)) / rate;
                b[i] = (MathF.Sin(ph * Dsp.TwoPi) * 0.8f + rng.Signed() * 0.4f) * MathF.Exp(-t / 0.012f);
            }
            Norm(b, 0.7f * (0.4f + 0.6f * v));
            return b;
        }

        static float[] Cowbell(int rate, Rng rng, float v)
        {
            var b = Buf(0.5f, rate);
            float p1 = 0f, p2 = 0f;
            var bp = Biquad.Make(BiquadType.BandPass, rate, 1200f, 1.5f);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)rate;
                p1 += 540f / rate; p2 += 800f / rate;
                float s = ((p1 % 1f) < 0.5f ? 1f : -1f) + ((p2 % 1f) < 0.5f ? 1f : -1f);
                b[i] = bp.Process(s) * (MathF.Exp(-t / 0.015f) * 0.7f + MathF.Exp(-t / 0.2f) * 0.3f);
            }
            Norm(b, 0.6f * (0.4f + 0.6f * v));
            return b;
        }

        static float[] Crash(int rate, Rng rng, float v, float length)
        {
            var b = Buf(length * 1.8f, rate);
            var hp = Biquad.Make(BiquadType.HighPass, rate, 3200f, 0.7f);
            var ph = new float[6];
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)rate;
                float s = 0f;
                for (int k = 0; k < 6; k++) { ph[k] += Metal[k] * 3.1f / rate; if (ph[k] > 1f) ph[k] -= 1f; s += ph[k] < 0.5f ? 1f : -1f; }
                float n = rng.Signed() * 0.8f + s / 6f * 0.4f;
                float env = MathF.Exp(-t / (length * 0.33f)) * (1f - MathF.Exp(-t / 0.002f)) + 0.25f * MathF.Exp(-t / 0.05f);
                b[i] = hp.Process(n) * env;
            }
            Norm(b, 0.75f * (0.4f + 0.6f * v));
            return b;
        }
    }
}
