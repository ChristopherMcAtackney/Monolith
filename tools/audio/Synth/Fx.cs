using System;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    /// <summary>Stereo effects for the offline renderer: the chorus of a Juno, the ensemble of a string machine, tape-style ping-pong echo, saturation, glue compression, EQ. All work in place on planar buffers.</summary>
    public static class Fx
    {
        /// <summary>LFO rates are rounded so an integer number of cycles fits the loop: a loop's chorus comes back to where it started.</summary>
        public static float LockRate(float hz, double loopSeconds)
        {
            if (loopSeconds <= 0) return hz;
            double cycles = Math.Max(1.0, Math.Round(hz * loopSeconds));
            return (float)(cycles / loopSeconds);
        }

        public static void Chorus(float[] L, float[] R, int rate, string mode, double loopSeconds, float mix = 0.5f)
        {
            if (string.IsNullOrEmpty(mode)) return;
            if (mode == "ens") { Ensemble(L, R, rate, loopSeconds, mix); return; }
            float fs = rate;
            float r1 = 0.513f, d1 = 0.00058f, c1 = 0.0033f;
            float r2 = 0.83f, d2 = 0.0009f, c2 = 0.0042f;
            bool both = mode == "I+II";
            if (both) { r1 = 9.75f; d1 = 0.0001f; c1 = 0.0035f; }
            float rate1 = LockRate(mode == "II" ? r2 : r1, loopSeconds), depth = mode == "II" ? d2 : d1, center = mode == "II" ? c2 : c1;
            if (both) { rate1 = LockRate(9.75f, loopSeconds); }
            var dl = new DelayLine((int)(0.02f * fs)); var dr = new DelayLine((int)(0.02f * fs));
            var lpL = new Biquad(); var lpR = new Biquad();
            lpL.Set(BiquadType.LowPass, fs, 7500f, 0.6f); lpR.Set(BiquadType.LowPass, fs, 7500f, 0.6f);
            float ph = 0f, inc = rate1 / fs;
            // I+II also moves slowly: two LFOs
            float ph2 = 0.25f, inc2 = LockRate(0.83f, loopSeconds) / fs;
            for (int i = 0; i < L.Length; i++)
            {
                ph += inc; if (ph >= 1f) ph -= 1f;
                ph2 += inc2; if (ph2 >= 1f) ph2 -= 1f;
                float tri = 4f * MathF.Abs(ph - 0.5f) - 1f;
                float tri2 = 4f * MathF.Abs(ph2 - 0.5f) - 1f;
                float m = both ? (tri * d1 + tri2 * d2) : tri * depth;
                float cen = both ? 0.0038f : center;
                dl.Write(L[i]); dr.Write(R[i]);
                float wl = lpL.Process(dl.Read((cen + m) * fs));
                float wr = lpR.Process(dr.Read((cen - m) * fs));
                // the Juno's chorus is stereo: the left wet leans left, the right wet right, and they swap sides' modulation
                L[i] = L[i] * (1f - mix * 0.5f) + (wl * 0.85f + wr * 0.15f) * mix;
                R[i] = R[i] * (1f - mix * 0.5f) + (wr * 0.85f + wl * 0.15f) * mix;
            }
        }

        /// <summary>A three-phase string-machine ensemble: three modulated delays 120 degrees apart, slow and fast, spread across the stereo field.</summary>
        static void Ensemble(float[] L, float[] R, int rate, double loopSeconds, float mix)
        {
            float fs = rate;
            var lines = new DelayLine[3];
            for (int k = 0; k < 3; k++) lines[k] = new DelayLine((int)(0.03f * fs));
            float slow = LockRate(0.6f, loopSeconds), fast = LockRate(5.4f, loopSeconds);
            float ps = 0f, pf = 0f, is_ = slow / fs, if_ = fast / fs;
            var w = new float[3];
            for (int i = 0; i < L.Length; i++)
            {
                ps += is_; if (ps >= 1f) ps -= 1f;
                pf += if_; if (pf >= 1f) pf -= 1f;
                float mono = (L[i] + R[i]) * 0.5f;
                for (int k = 0; k < 3; k++) lines[k].Write(mono);
                for (int k = 0; k < 3; k++)
                {
                    float p = k / 3f;
                    float m = 0.0016f * MathF.Sin(Dsp.TwoPi * (ps + p)) + 0.0004f * MathF.Sin(Dsp.TwoPi * (pf + p));
                    w[k] = lines[k].Read((0.0065f + m) * fs);
                }
                float wl = w[0] * 0.8f + w[1] * 0.5f + w[2] * 0.1f, wr = w[2] * 0.8f + w[1] * 0.5f + w[0] * 0.1f;
                L[i] = L[i] * (1f - mix * 0.6f) + wl * mix * 0.8f;
                R[i] = R[i] * (1f - mix * 0.6f) + wr * mix * 0.8f;
            }
        }

        /// <summary>Tube-ish saturation with a little even-harmonic bias.</summary>
        public static void Saturate(float[] L, float[] R, float drive, float bias = 0.08f)
        {
            if (drive <= 0.001f) return;
            var dcL = new DcBlock(); var dcR = new DcBlock();
            for (int i = 0; i < L.Length; i++)
            {
                L[i] = dcL.Process(Dsp.Saturate(L[i], 1f + drive, bias));
                R[i] = dcR.Process(Dsp.Saturate(R[i], 1f + drive, bias));
            }
        }

        /// <summary>Tempo-synced ping-pong echo with a dark, saturating feedback path (tape). Returns the wet signal only.</summary>
        public static void PingPong(float[] inL, float[] inR, float[] wetL, float[] wetR, int rate, double delaySeconds, float feedback, float lpHz = 3200f, float hpHz = 200f, float sat = 0.4f)
        {
            float fs = rate;
            int d = (int)Math.Round(delaySeconds * fs);
            var dL = new DelayLine(d + 8); var dR = new DelayLine(d + 8);
            var lpA = new Biquad(); var lpB = new Biquad(); var hpA = new Biquad(); var hpB = new Biquad();
            lpA.Set(BiquadType.LowPass, fs, lpHz, 0.6f); lpB.Set(BiquadType.LowPass, fs, lpHz, 0.6f);
            hpA.Set(BiquadType.HighPass, fs, hpHz, 0.6f); hpB.Set(BiquadType.HighPass, fs, hpHz, 0.6f);
            float wob = 0f;
            for (int i = 0; i < inL.Length; i++)
            {
                wob += 0.37f / fs; if (wob > 1f) wob -= 1f;
                float flutter = 1f + 0.4f * MathF.Sin(Dsp.TwoPi * wob);
                float yL = dL.Read(d - 1 + flutter), yR = dR.Read(d - 1 - flutter);
                float mono = (inL[i] + inR[i]) * 0.5f;
                float fbL = hpA.Process(lpA.Process(Dsp.Saturate(yR * feedback, 1f + sat, 0.05f)));
                float fbR = hpB.Process(lpB.Process(Dsp.Saturate(yL * feedback, 1f + sat, 0.05f)));
                dL.Write(mono + fbL);
                dR.Write(fbR);
                wetL[i] = yL; wetR[i] = yR;
            }
        }

        /// <summary>A feed-forward glue compressor (soft knee, program-dependent release).</summary>
        public static void Compress(float[] L, float[] R, int rate, float thresholdDb, float ratio, float attackMs, float releaseMs, float makeupDb = 0f)
        {
            float fs = rate;
            float atk = Dsp.TauToCoef(attackMs * 0.001f, fs), rel = Dsp.TauToCoef(releaseMs * 0.001f, fs);
            float env = 0f, gainDb = 0f, makeup = Db.ToLin(makeupDb);
            float knee = 6f;
            for (int i = 0; i < L.Length; i++)
            {
                float a = MathF.Max(MathF.Abs(L[i]), MathF.Abs(R[i]));
                env += (a - env) * (a > env ? atk : rel);
                float levelDb = Db.FromLin(env);
                float over = levelDb - thresholdDb;
                float reduction;
                if (over <= -knee / 2f) reduction = 0f;
                else if (over >= knee / 2f) reduction = over * (1f - 1f / ratio);
                else { float x = over + knee / 2f; reduction = (1f - 1f / ratio) * x * x / (2f * knee); }
                float target = -reduction;
                gainDb += (target - gainDb) * (target < gainDb ? atk : rel);
                float g = Db.ToLin(gainDb) * makeup;
                L[i] *= g; R[i] *= g;
            }
        }

        public struct Band { public BiquadType Type; public float Freq, Q, GainDb; public Band(BiquadType t, float f, float q = 0.7f, float g = 0f) { Type = t; Freq = f; Q = q; GainDb = g; } }

        public static void Eq(float[] L, float[] R, int rate, params Band[] bands)
        {
            foreach (var b in bands)
            {
                var fl = new Biquad(); var fr = new Biquad();
                fl.Set(b.Type, rate, b.Freq, b.Q, b.GainDb); fr.Set(b.Type, rate, b.Freq, b.Q, b.GainDb);
                for (int i = 0; i < L.Length; i++) { L[i] = fl.Process(L[i]); R[i] = fr.Process(R[i]); }
            }
        }

        public static void Widen(float[] L, float[] R, float width)
        {
            for (int i = 0; i < L.Length; i++)
            {
                float m = (L[i] + R[i]) * 0.5f, s = (L[i] - R[i]) * 0.5f * width;
                L[i] = m + s; R[i] = m - s;
            }
        }

        public static void Gain(float[] L, float[] R, float g) { for (int i = 0; i < L.Length; i++) { L[i] *= g; R[i] *= g; } }

        public static void Add(float[] dstL, float[] dstR, float[] srcL, float[] srcR, float g = 1f)
        {
            for (int i = 0; i < dstL.Length; i++) { dstL[i] += srcL[i] * g; dstR[i] += srcR[i] * g; }
        }

        /// <summary>Run a signal through a reverb and return the wet output.</summary>
        public static void Hall(float[] inL, float[] inR, float[] wetL, float[] wetR, int rate, float rt60, float size, float damping, float preDelayMs, float lowCut = 120f)
        {
            var r = new FdnReverb(rate, rt60, size, damping, preDelayMs) { LowCut = lowCut };
            // the network's gain grows with the decay time (about 3 dB a doubling): level it so that 0 dB return is a wet signal 6 dB under steady noise going in
            float comp = Db.ToLin(3.6f - 3.2f * MathF.Log(MathF.Max(rt60, 0.3f) / 1.5f, 2f));
            for (int i = 0; i < inL.Length; i++) { r.Process(inL[i], inR[i], out float l, out float rr); wetL[i] = l * comp; wetR[i] = rr * comp; }
        }

        public static void Plate(float[] inL, float[] inR, float[] wetL, float[] wetR, int rate, float decay, float damping, float preDelayMs = 0f)
        {
            var r = new PlateReverb(rate, decay, damping, 0.9995f, preDelayMs);
            for (int i = 0; i < inL.Length; i++) { r.Process(inL[i], inR[i], out float l, out float rr); wetL[i] = l * 0.27f; wetR[i] = rr * 0.27f; }
        }
    }
}
