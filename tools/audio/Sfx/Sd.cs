using System;
using System.Collections.Generic;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    /// <summary>
    /// The sound-design toolkit: a mono buffer you layer generators into and shape with filters, envelopes, saturation and reverb. Everything is
    /// synthesised (nothing sampled). Functions of time take seconds from the start of the buffer.
    /// </summary>
    public sealed class Sd
    {
        public const int Rate = 48000;
        public float[] X;
        public int N => X.Length;
        public float Seconds => X.Length / (float)Rate;

        public Sd(float seconds) { X = new float[Math.Max(1, (int)(seconds * Rate))]; }
        public Sd(float[] x) { X = x; }

        public static float T(int i) => i / (float)Rate;

        // ------------------------------------------------------------------------------------------------ generators (add into the buffer)

        public Sd Sine(Func<float, float> freq, Func<float, float> amp, float phase0 = 0f, float startSec = 0f)
        {
            float ph = phase0;
            for (int i = (int)(startSec * Rate); i < X.Length; i++)
            {
                float t = T(i) - startSec;
                ph += freq(t) / Rate; ph -= MathF.Floor(ph);
                X[i] += MathF.Sin(ph * Dsp.TwoPi) * amp(t);
            }
            return this;
        }

        public Sd Saw(Func<float, float> freq, Func<float, float> amp, float phase0 = 0f)
        {
            float ph = phase0;
            for (int i = 0; i < X.Length; i++)
            {
                float t = T(i);
                float f = freq(t); float dt = f / Rate;
                ph += dt; ph -= MathF.Floor(ph);
                X[i] += Osc.Saw(ph, dt) * amp(t);
            }
            return this;
        }

        public Sd Pulse(Func<float, float> freq, Func<float, float> amp, Func<float, float> width, float phase0 = 0f)
        {
            float ph = phase0;
            for (int i = 0; i < X.Length; i++)
            {
                float t = T(i);
                float f = freq(t); float dt = f / Rate;
                ph += dt; ph -= MathF.Floor(ph);
                X[i] += Osc.Pulse(ph, dt, Dsp.Clamp(width(t), 0.05f, 0.95f)) * amp(t);
            }
            return this;
        }

        /// <summary>White noise (colour 1) or progressively duller noise (colour toward 0 is a one-pole low-pass of the white).</summary>
        public Sd Noise(Func<float, float> amp, uint seed, float colour = 1f, float startSec = 0f)
        {
            var r = new Rng(seed);
            float lp = 0f;
            for (int i = (int)(startSec * Rate); i < X.Length; i++)
            {
                float t = T(i) - startSec;
                float w = r.Signed();
                lp += (w - lp) * colour;
                X[i] += (colour >= 1f ? w : lp * (1f + (1f - colour) * 2.5f)) * amp(t);
            }
            return this;
        }

        /// <summary>Frequency modulation: carrier at freq, modulator at ratio x carrier, with a time-varying index.</summary>
        public Sd Fm(Func<float, float> freq, float ratio, Func<float, float> index, Func<float, float> amp)
        {
            float pc = 0f, pm = 0f;
            for (int i = 0; i < X.Length; i++)
            {
                float t = T(i); float f = freq(t);
                pm += f * ratio / Rate; pm -= MathF.Floor(pm);
                pc += f / Rate; pc -= MathF.Floor(pc);
                X[i] += MathF.Sin(pc * Dsp.TwoPi + index(t) * MathF.Sin(pm * Dsp.TwoPi)) * amp(t);
            }
            return this;
        }

        /// <summary>Modal synthesis: a struck object as a set of decaying sinusoids (frequency, T60 seconds, amplitude), started at startSec.</summary>
        public Sd Modal(float[] freqs, float[] t60, float[] amps, float startSec = 0f, float gain = 1f)
        {
            int s0 = (int)(startSec * Rate);
            for (int m = 0; m < freqs.Length; m++)
            {
                float w = Dsp.TwoPi * freqs[m] / Rate, k = 6.9078f / MathF.Max(t60[m], 0.005f) / Rate, a = amps[m] * gain;
                float env = 1f, ph = 0f;
                for (int i = s0; i < X.Length && env > 0.0003f; i++)
                {
                    ph += w;
                    X[i] += MathF.Sin(ph) * env * a;
                    env *= 1f - k;
                }
            }
            return this;
        }

        /// <summary>A struck metal plate or bar: inharmonic modes (the ratios of a free bar), each decaying, with a seeded detune. Higher modes die sooner.</summary>
        public Sd Clang(float baseHz, float t60, float gain, uint seed, float startSec = 0f, float brightness = 1f)
        {
            float[] ratios = { 1f, 2.756f, 5.404f, 8.933f, 13.34f, 18.64f };
            var r = new Rng(seed);
            var f = new float[ratios.Length]; var d = new float[ratios.Length]; var a = new float[ratios.Length];
            for (int i = 0; i < f.Length; i++)
            {
                f[i] = baseHz * ratios[i] * (1f + r.Signed() * 0.012f);
                d[i] = t60 / (1f + i * 0.9f);
                a[i] = (i == 0 ? 1f : 0.7f / (1f + i * 0.5f)) * MathF.Pow(brightness, i * 0.5f);
                if (f[i] > Rate * 0.45f) a[i] = 0f;
            }
            return Modal(f, d, a, startSec, gain);
        }

        /// <summary>Random short resonant pops (debris, sparks, sizzle): density in events per second at time t.</summary>
        public Sd Crackle(Func<float, float> density, Func<float, float> amp, uint seed, float minHz = 1500f, float maxHz = 7000f, float decayMs = 6f, float startSec = 0f)
        {
            var r = new Rng(seed);
            int s0 = (int)(startSec * Rate);
            for (int i = s0; i < X.Length; i++)
            {
                float t = T(i) - startSec;
                if (r.Float() < density(t) / Rate)
                {
                    float f = minHz * MathF.Pow(maxHz / minHz, r.Float());
                    float a = amp(t) * (0.3f + 0.7f * r.Float());
                    float w = Dsp.TwoPi * f / Rate, k = 6.9078f / (decayMs * 0.001f * (0.5f + r.Float())) / Rate;
                    float env = 1f, ph = 0f;
                    for (int j = i; j < X.Length && env > 0.002f; j++) { ph += w; X[j] += MathF.Sin(ph) * env * a; env *= 1f - k; }
                }
            }
            return this;
        }

        // ------------------------------------------------------------------------------------------------ processors (in place)

        public Sd Env(Func<float, float> g) { for (int i = 0; i < X.Length; i++) X[i] *= g(T(i)); return this; }

        public Sd Gain(float db) { float g = Db.ToLin(db); for (int i = 0; i < X.Length; i++) X[i] *= g; return this; }

        public Sd Svf(Func<float, float> cutoff, float res, int mode, float stages = 1)
        {
            var f = new Svf(); var f2 = new Svf();
            for (int i = 0; i < X.Length; i++)
            {
                float c = cutoff(T(i));
                if ((i & 7) == 0) { f.Set(c, res, Rate); f2.Set(c, res * 0.6f, Rate); }
                f.Process(X[i]);
                float y = mode == 0 ? f.Low : mode == 1 ? f.Band : f.High;
                if (stages > 1) { f2.Process(y); y = mode == 0 ? f2.Low : mode == 1 ? f2.Band : f2.High; }
                X[i] = y;
            }
            return this;
        }

        public Sd LowPass(Func<float, float> cutoff, float res = 0.15f, int poles = 1) => Svf(cutoff, res, 0, poles);
        public Sd LowPass(float hz, float res = 0.15f, int poles = 1) => Svf(t => hz, res, 0, poles);
        public Sd HighPass(Func<float, float> cutoff, float res = 0.15f, int poles = 1) => Svf(cutoff, res, 2, poles);
        public Sd HighPass(float hz, float res = 0.15f, int poles = 1) => Svf(t => hz, res, 2, poles);
        /// <summary>Band-pass; res 0..1 sets the width (higher = narrower).</summary>
        public Sd BandPass(Func<float, float> centre, float res = 0.7f, int poles = 1) => Svf(centre, res, 1, poles);
        public Sd BandPass(float hz, float res = 0.7f, int poles = 1) => Svf(t => hz, res, 1, poles);

        /// <summary>Ladder low-pass (the Moog's): warmer and with a bite when resonant.</summary>
        public Sd Ladder(Func<float, float> cutoff, float res, float drive = 1f)
        {
            var l = new Ladder();
            for (int i = 0; i < X.Length; i++) { if ((i & 7) == 0) l.Set(cutoff(T(i)), res, Rate); X[i] = l.Process(X[i], drive); }
            return this;
        }

        public Sd Drive(float amount, float bias = 0f)
        {
            var dc = new DcBlock();
            for (int i = 0; i < X.Length; i++) X[i] = dc.Process(Dsp.Saturate(X[i], 1f + amount, bias));
            return this;
        }

        public Sd HardClip(float level) { for (int i = 0; i < X.Length; i++) X[i] = Dsp.Clamp(X[i], -level, level) / level; return this; }

        /// <summary>Ring modulation with a sine (a metallic, robotic edge).</summary>
        public Sd RingMod(Func<float, float> freq, float mix = 1f)
        {
            float ph = 0f;
            for (int i = 0; i < X.Length; i++)
            {
                ph += freq(T(i)) / Rate; ph -= MathF.Floor(ph);
                X[i] = X[i] * (1f - mix) + X[i] * MathF.Sin(ph * Dsp.TwoPi) * mix;
            }
            return this;
        }

        /// <summary>Amplitude modulation: tremolo with depth 0..1 at the rate in Hz.</summary>
        public Sd Tremolo(Func<float, float> rate, Func<float, float> depth)
        {
            float ph = 0f;
            for (int i = 0; i < X.Length; i++)
            {
                float t = T(i);
                ph += rate(t) / Rate; ph -= MathF.Floor(ph);
                X[i] *= 1f - depth(t) * 0.5f * (1f - MathF.Cos(ph * Dsp.TwoPi));
            }
            return this;
        }

        /// <summary>A comb filter (feedback): rings at rate/delay Hz. Metallic resonance.</summary>
        public Sd Comb(float hz, float feedback, float mix = 0.5f)
        {
            int d = Math.Max(2, (int)(Rate / hz));
            var buf = new float[d]; int w = 0;
            for (int i = 0; i < X.Length; i++)
            {
                float y = X[i] + buf[w] * feedback;
                buf[w] = y; w++; if (w >= d) w = 0;
                X[i] = X[i] * (1f - mix) + y * mix * (1f - feedback * 0.5f);
            }
            return this;
        }

        /// <summary>A flanger / phaser-ish sweep: the signal against a delayed copy of itself with moving delay (ms).</summary>
        public Sd Flange(Func<float, float> delayMs, float feedback = 0.4f, float mix = 0.5f)
        {
            var d = new DelayLine((int)(0.03f * Rate));
            float fb = 0f;
            for (int i = 0; i < X.Length; i++)
            {
                d.Write(X[i] + fb * feedback);
                float w = d.Read(delayMs(T(i)) * 0.001f * Rate);
                fb = w;
                X[i] = X[i] * (1f - mix * 0.5f) + w * mix;
            }
            return this;
        }

        /// <summary>Pitch shift by resampling with a time-varying ratio (a Doppler or a tape slow-down): ratio(t) of the original's speed at output time t.</summary>
        public Sd Resample(Func<float, float> ratio, float outSeconds = -1f)
        {
            int n = outSeconds > 0 ? (int)(outSeconds * Rate) : X.Length;
            var y = new float[n];
            double pos = 0;
            for (int i = 0; i < n; i++)
            {
                int i1 = (int)pos; float fr = (float)(pos - i1);
                float a = i1 - 1 >= 0 && i1 - 1 < X.Length ? X[i1 - 1] : 0f, b = i1 >= 0 && i1 < X.Length ? X[i1] : 0f;
                float c = i1 + 1 < X.Length ? X[i1 + 1] : 0f, d = i1 + 2 < X.Length ? X[i1 + 2] : 0f;
                y[i] = Dsp.Hermite(a, b, c, d, fr);
                pos += ratio(T(i));
                if (pos >= X.Length) break;
            }
            X = y;
            return this;
        }

        public Sd Echo(float seconds, float feedback, float mix, float lpHz = 4000f)
        {
            int d = Math.Max(1, (int)(seconds * Rate));
            var line = new DelayLine(d + 4);
            float lp = 0f; float c = Dsp.OnePoleCoef(lpHz, Rate);
            for (int i = 0; i < X.Length; i++)
            {
                float w = line.ReadInt(d - 1);
                lp += (w - lp) * c;
                line.Write(X[i] + lp * feedback);
                X[i] += w * mix;
            }
            return this;
        }

        /// <summary>Add a reverb tail (mono in, summed to mono out). The tail extends the buffer by <paramref name="extraSeconds"/>. mix is the wet level relative to dry.</summary>
        public Sd Reverb(float rt60, float mix, float size = 1f, float damping = 0.4f, float preMs = 10f, float extraSeconds = 0f, float lowCut = 90f)
        {
            if (extraSeconds > 0f)
            {
                // the dry part ends where it ends: close it gently so the tail does not start from a click
                int fade = Math.Min(X.Length, (int)(0.06f * Rate));
                for (int i = 0; i < fade; i++) { float g = i / (float)fade; X[X.Length - 1 - i] *= g; }
                Array.Resize(ref X, X.Length + (int)(extraSeconds * Rate));
            }
            var r = new FdnReverb(Rate, rt60, size, damping, preMs) { LowCut = lowCut };
            float comp = Db.ToLin(3.6f - 3.2f * MathF.Log(MathF.Max(rt60, 0.3f) / 1.5f, 2f));
            for (int i = 0; i < X.Length; i++)
            {
                float x = X[i];
                r.Process(x, x, out float l, out float rr);
                X[i] = x + (l + rr) * 0.5f * comp * mix * 2f;
            }
            return this;
        }

        public Sd Normalize(float peakDb = -1f)
        {
            float p = 0f; for (int i = 0; i < X.Length; i++) p = MathF.Max(p, MathF.Abs(X[i]));
            if (p > 1e-9f) { float g = Db.ToLin(peakDb) / p; for (int i = 0; i < X.Length; i++) X[i] *= g; }
            return this;
        }

        public Sd Edges(float inMs = 1.5f, float outMs = 20f)
        {
            int a = Math.Min(X.Length, (int)(inMs * 0.001f * Rate)), b = Math.Min(X.Length, (int)(outMs * 0.001f * Rate));
            for (int i = 0; i < a; i++) X[i] *= i / (float)a;
            for (int i = 0; i < b; i++) { float g = i / (float)b; X[X.Length - 1 - i] *= g * g; }
            return this;
        }

        /// <summary>Cut the tail where the sound has died away (below <paramref name="floorDb"/> and staying there), with a short fade: a voice then frees itself as soon as it is inaudible.</summary>
        public Sd TrimSilence(float floorDb = -62f, float keepSeconds = 0.05f, float fadeSeconds = 0.06f)
        {
            float th = Db.ToLin(floorDb);
            int last = 0;
            for (int i = X.Length - 1; i >= 0; i--) if (MathF.Abs(X[i]) > th) { last = i; break; }
            int end = Math.Min(X.Length, last + (int)(keepSeconds * Rate));
            if (end >= X.Length - 16) return this;
            Array.Resize(ref X, end);
            int f = Math.Min(X.Length, (int)(fadeSeconds * Rate));
            for (int i = 0; i < f; i++) { float g = i / (float)f; X[X.Length - 1 - i] *= g * g; }
            return this;
        }

        public Sd Trim(float seconds) { Array.Resize(ref X, Math.Min(X.Length, (int)(seconds * Rate))); return this; }

        public Sd Pad(float seconds) { if (seconds * Rate > X.Length) Array.Resize(ref X, (int)(seconds * Rate)); return this; }

        /// <summary>Mix another buffer in at an offset, at a gain in dB.</summary>
        public Sd Mix(Sd o, float db = 0f, float atSec = 0f)
        {
            int off = (int)(atSec * Rate); float g = Db.ToLin(db);
            int need = off + o.X.Length;
            if (need > X.Length) Array.Resize(ref X, need);
            for (int i = 0; i < o.X.Length; i++) X[off + i] += o.X[i] * g;
            return this;
        }

        public Sd Clone() => new Sd((float[])X.Clone());

        /// <summary>A seamless loop: the end crossfades into the start. The result is shorter than the input by the crossfade.</summary>
        public Sd MakeLoop(float crossfadeSeconds)
        {
            int xf = (int)(crossfadeSeconds * Rate);
            int n = X.Length - xf;
            var y = new float[n];
            Array.Copy(X, y, n);
            for (int i = 0; i < xf; i++)
            {
                float f = (float)i / xf;
                float a = MathF.Sqrt(1f - f), b = MathF.Sqrt(f);        // equal power
                y[i] = X[i] * b + X[n + i] * a;
            }
            X = y;
            return this;
        }

        /// <summary>The distant version: dulled, smeared, quieter, with a longer tail (sound that has travelled).</summary>
        public Sd Far(float amount = 1f)
        {
            var c = Clone();
            float lp = 1800f / (0.6f + amount);
            c.LowPass(lp, 0.1f, 2);
            // smear the attack: a short moving average
            int w = (int)((0.006f + 0.012f * amount) * Rate);
            var y = new float[c.X.Length]; double s = 0;
            for (int i = 0; i < y.Length; i++) { s += c.X[i]; if (i >= w) s -= c.X[i - w]; y[i] = (float)(s / w); }
            c.X = y;
            c.Reverb(1.2f + amount, 0.7f, 1.4f, 0.65f, 25f, 0.8f + amount);
            c.Gain(-4f * amount);
            return c;
        }

        // ------------------------------------------------------------------------------------------------ envelopes

        /// <summary>exp(-t/tau) after a linear attack.</summary>
        public static float Hit(float t, float decayHz, float attack = 0.001f) => t < attack ? t / attack : MathF.Exp(-(t - attack) * decayHz);

        public static float Swell(float t, float riseSec, float fallSec, float total)
        {
            if (t < riseSec) return Dsp.Smooth(t / riseSec);
            if (t > total - fallSec) return Dsp.Smooth((total - t) / fallSec);
            return 1f;
        }

        public static float Expo(float t, float f0, float f1, float tau) => f1 + (f0 - f1) * MathF.Exp(-t / tau);
    }
}
