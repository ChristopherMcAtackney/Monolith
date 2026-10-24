using System;

namespace Monolith.Audio
{
    public enum RadioStyle { Crew, Earth, Alien }

    /// <summary>
    /// The radio: what turns a dry spoken line into something that comes out of a ship's comms. Crew voices are band-limited and a little
    /// saturated with a squelch before and after; Earth's would-be gods are vast, cold and reverberant with a machine edge (doubled an octave
    /// down, a metallic comb, a long hall) and a power-up swell before the first word; alien allies get an inhuman texture (a ring-modulated
    /// double, a wandering resonance, a purr and chitter underneath). The same code runs in the tools that make the built-in clips and in the
    /// game, for a clip the player drops in: so their own recordings sound like they came over the same radio.
    /// </summary>
    public static class RadioFx
    {
        public const int OutRate = 48000;

        public static RadioStyle StyleOf(string name)
        {
            switch ((name ?? "").ToLowerInvariant())
            {
                case "earth": return RadioStyle.Earth;
                case "alien": return RadioStyle.Alien;
                default: return RadioStyle.Crew;
            }
        }

        /// <summary>Resample (Hermite) a mono buffer to another rate.</summary>
        public static float[] Resample(float[] x, int from, int to)
        {
            if (from == to) return (float[])x.Clone();
            double ratio = (double)from / to;
            int n = (int)(x.Length / ratio);
            var y = new float[n];
            for (int i = 0; i < n; i++)
            {
                double p = i * ratio; int i1 = (int)p; float f = (float)(p - i1);
                float a = x[Math.Max(0, i1 - 1)], b = x[Math.Min(x.Length - 1, i1)], c = x[Math.Min(x.Length - 1, i1 + 1)], d = x[Math.Min(x.Length - 1, i1 + 2)];
                y[i] = Dsp.Hermite(a, b, c, d, f);
            }
            return y;
        }

        /// <summary>Trim silence from both ends (keeping a little), by a threshold relative to the loudest part.</summary>
        public static float[] TrimSilence(float[] x, int rate, float thresholdDb = -42f, float keepMs = 25f)
        {
            float peak = 0f; foreach (var v in x) peak = MathF.Max(peak, MathF.Abs(v));
            if (peak < 1e-5f) return x;
            float th = peak * Db.ToLin(thresholdDb);
            int a = 0, b = x.Length - 1;
            int win = Math.Max(1, rate / 200);
            for (; a < x.Length - win; a++) { float m = 0; for (int k = 0; k < win; k += 4) m = MathF.Max(m, MathF.Abs(x[a + k])); if (m > th) break; }
            for (; b > a + win; b--) { float m = 0; for (int k = 0; k < win; k += 4) m = MathF.Max(m, MathF.Abs(x[b - k])); if (m > th) break; }
            int keep = (int)(keepMs * 0.001f * rate);
            a = Math.Max(0, a - keep); b = Math.Min(x.Length - 1, b + keep * 3);
            var y = new float[b - a + 1];
            Array.Copy(x, a, y, 0, y.Length);
            return y;
        }

        /// <summary>Granular delay-line pitch shift (two crossfaded read heads): robotic when pushed, which suits the machines.</summary>
        public static float[] PitchShift(float[] x, float semitones, int rate, float windowMs = 38f)
        {
            float r = MathF.Pow(2f, semitones / 12f);
            int w = Math.Max(64, (int)(windowMs * 0.001f * rate));
            var y = new float[x.Length];
            float inc = (1f - r) / w;
            float p0 = 0f, p1 = 0.5f;
            for (int i = 0; i < x.Length; i++)
            {
                p0 += inc; p0 -= MathF.Floor(p0);
                p1 += inc; p1 -= MathF.Floor(p1);
                float g0 = MathF.Sin(Dsp.Pi * p0); g0 *= g0;
                float g1 = MathF.Sin(Dsp.Pi * p1); g1 *= g1;
                y[i] = Tap(x, i - p0 * w) * g0 + Tap(x, i - p1 * w) * g1;
            }
            return y;
        }

        static float Tap(float[] x, float pos)
        {
            int i = (int)MathF.Floor(pos); float f = pos - i;
            float a = At(x, i - 1), b = At(x, i), c = At(x, i + 1), d = At(x, i + 2);
            return Dsp.Hermite(a, b, c, d, f);
        }

        static float At(float[] x, int i) => i < 0 || i >= x.Length ? 0f : x[i];

        static void Normalize(float[] x, float peakDb)
        {
            float p = 0f; foreach (var v in x) p = MathF.Max(p, MathF.Abs(v));
            if (p < 1e-7f) return;
            float g = Db.ToLin(peakDb) / p;
            for (int i = 0; i < x.Length; i++) x[i] *= g;
        }

        /// <summary>A leveller: the loud syllables come down and the quiet ones up, the way a radio's AGC does.</summary>
        static void Level(float[] x, int rate, float thresholdDb, float ratio, float attackMs, float releaseMs, float makeupDb)
        {
            float atk = Dsp.TauToCoef(attackMs * 0.001f, rate), rel = Dsp.TauToCoef(releaseMs * 0.001f, rate);
            float env = 0f, g = 1f, mk = Db.ToLin(makeupDb);
            for (int i = 0; i < x.Length; i++)
            {
                float a = MathF.Abs(x[i]);
                env += (a - env) * (a > env ? atk : rel);
                float lv = Db.FromLin(env);
                float over = lv - thresholdDb;
                float target = over > 0f ? Db.ToLin(-over * (1f - 1f / ratio)) : 1f;
                g += (target - g) * (target < g ? atk : rel);
                x[i] *= g * mk;
            }
        }

        static float[] Noise(int n, uint seed, float lowHz, float highHz, int rate)
        {
            var r = new Rng(seed);
            var lo = Biquad.Make(BiquadType.HighPass, rate, lowHz, 0.7f);
            var hi = Biquad.Make(BiquadType.LowPass, rate, highHz, 0.7f);
            var y = new float[n];
            for (int i = 0; i < n; i++) y[i] = hi.Process(lo.Process(r.Signed()));
            return y;
        }

        static void AddAt(float[] dst, float[] src, int at, float g)
        {
            for (int i = 0; i < src.Length; i++) { int j = at + i; if (j >= 0 && j < dst.Length) dst[j] += src[i] * g; }
        }

        /// <summary>
        /// Make a radio line from a dry spoken buffer (mono, any rate). Returns mono at <see cref="OutRate"/>, levelled to about
        /// <paramref name="targetLufs"/> integrated.
        /// </summary>
        public static float[] Process(float[] dry, int rate, RadioStyle style, uint seed = 1, float targetLufs = -23f)
        {
            float[] x = Resample(dry, rate, OutRate);
            x = TrimSilence(x, OutRate);
            Normalize(x, -3f);
            float[] y;
            switch (style)
            {
                case RadioStyle.Earth: y = Earth(x, seed); break;
                case RadioStyle.Alien: y = Alien(x, seed); break;
                default: y = Crew(x, seed); break;
            }
            // level to the target loudness
            var m = new LoudnessMeter(OutRate, 1);
            m.Add(y);
            float lufs = m.Integrated;
            if (lufs > -100f) { float g = Db.ToLin(targetLufs - lufs); for (int i = 0; i < y.Length; i++) y[i] *= g; }
            // never over -1.5 dBFS
            float p = 0f; foreach (var v in y) p = MathF.Max(p, MathF.Abs(v));
            float ceil = Db.ToLin(-1.5f);
            if (p > ceil) { float g = ceil / p; for (int i = 0; i < y.Length; i++) y[i] *= g; }
            return y;
        }

        // ------------------------------------------------------------------------------------------------ crew

        static float[] Crew(float[] x, uint seed)
        {
            int R = OutRate;
            Level(x, R, -22f, 3.2f, 4f, 90f, 6f);
            var hp1 = Biquad.Make(BiquadType.HighPass, R, 320f, 0.75f); var hp2 = Biquad.Make(BiquadType.HighPass, R, 320f, 0.6f);
            var lp1 = Biquad.Make(BiquadType.LowPass, R, 3300f, 0.75f); var lp2 = Biquad.Make(BiquadType.LowPass, R, 3600f, 0.6f);
            var pk = Biquad.Make(BiquadType.Peak, R, 1700f, 0.9f, 3.5f);
            var dc = new DcBlock();
            for (int i = 0; i < x.Length; i++)
            {
                float v = lp2.Process(lp1.Process(hp2.Process(hp1.Process(x[i]))));
                v = pk.Process(v);
                x[i] = dc.Process(Dsp.Saturate(v, 2.2f, 0.06f));
            }
            Normalize(x, -4f);
            // the carrier: squelch opens (a click and a burst), a noise bed runs under the line, squelch closes
            int lead = (int)(0.16 * R), tail = (int)(0.28 * R);
            var y = new float[lead + x.Length + tail];
            Array.Copy(x, 0, y, lead, x.Length);
            var bed = Noise(y.Length, seed + 7, 280f, 3200f, R);
            var rng = new Rng(seed + 3);
            float flutter = 0f;
            for (int i = 0; i < y.Length; i++)
            {
                flutter += (rng.Signed() - flutter) * 0.0008f;
                float env = i < lead ? 0.55f : i < lead + x.Length ? 0.28f : 0.5f * MathF.Exp(-(i - lead - x.Length) / (0.07f * R));
                y[i] += bed[i] * env * 0.05f * (1f + flutter * 6f);
            }
            var open = Noise((int)(0.09 * R), seed + 11, 500f, 3000f, R);
            for (int i = 0; i < open.Length; i++) { float e = MathF.Exp(-i / (0.02f * R)) * (i < 30 ? i / 30f : 1f); y[i] += open[i] * e * 0.9f + MathF.Sin(i * Dsp.TwoPi * 1750f / R) * MathF.Exp(-i / (0.008f * R)) * 0.28f; }
            var close = Noise((int)(0.2 * R), seed + 13, 400f, 2800f, R);
            int cat = lead + x.Length;
            for (int i = 0; i < close.Length; i++) { float e = MathF.Exp(-i / (0.045f * R)); if (cat + i < y.Length) y[cat + i] += close[i] * e * 0.6f; }
            if (cat + 10 < y.Length) for (int i = 0; i < 40; i++) y[cat + i] += MathF.Sin(i * Dsp.TwoPi * 1250f / R) * MathF.Exp(-i / 10f) * 0.22f;
            return y;
        }

        // ------------------------------------------------------------------------------------------------ Earth

        static float[] Earth(float[] x, uint seed)
        {
            int R = OutRate;
            Level(x, R, -24f, 2.5f, 8f, 200f, 4f);
            // the voice of a chorus of one: itself, an octave down, a fourth below that
            var down = PitchShift(x, -12f, R, 46f);
            var low = PitchShift(x, -5f, R, 42f);
            var up = PitchShift(x, 7.02f, R, 36f);
            int lead = (int)(1.0 * R), tail = (int)(3.4 * R);
            var y = new float[lead + x.Length + tail];
            for (int i = 0; i < x.Length; i++) y[lead + i] = x[i] * 0.95f + down[i] * 0.55f + low[i] * 0.26f + up[i] * 0.10f;
            // a metallic edge: a short comb, and a slow amplitude flutter like a failing carrier
            var comb = new DelayLine((int)(0.02f * R));
            float ph = 0f;
            var dc = new DcBlock();
            for (int i = 0; i < y.Length; i++)
            {
                float c = comb.Read(0.0067f * R);
                float v = y[i] + c * 0.5f;
                comb.Write(v);
                ph += 31f / R; ph -= MathF.Floor(ph);
                v *= 0.88f + 0.12f * MathF.Sin(ph * Dsp.TwoPi);
                y[i] = dc.Process(Dsp.Saturate(v, 2.6f, 0.03f));
            }
            // cold: the highs trimmed, a hollow at the middle, weight below
            var hs = Biquad.Make(BiquadType.HighShelf, R, 4500f, 0.7f, -4f);
            var ls = Biquad.Make(BiquadType.LowShelf, R, 180f, 0.7f, 2f);
            var hp = Biquad.Make(BiquadType.HighPass, R, 90f, 0.7f);
            var pk = Biquad.Make(BiquadType.Peak, R, 2100f, 0.8f, -3f);
            for (int i = 0; i < y.Length; i++) y[i] = pk.Process(hp.Process(ls.Process(hs.Process(y[i]))));
            Normalize(y, -4f);
            // the power-up before the first word: a sub swell and a metallic shimmer
            var swell = new float[lead];
            var nz = Noise(lead, seed + 21, 600f, 5000f, R);
            for (int i = 0; i < lead; i++)
            {
                float t = i / (float)R, e = MathF.Pow(Dsp.Clamp01(t / 0.95f), 2.2f);
                swell[i] = MathF.Sin(Dsp.TwoPi * (36f + 10f * t) * t) * 0.30f * e + nz[i] * 0.035f * e * e + MathF.Sin(Dsp.TwoPi * 880f * t) * 0.012f * e * e;
            }
            AddAt(y, swell, 0, 1f);
            // the hall
            var rev = new FdnReverb(R, 4.8f, 1.6f, 0.5f, 35f, 1.3f, seed + 5) { LowCut = 110f };
            var o = new float[y.Length];
            float comp = Db.ToLin(3.6f - 3.2f * MathF.Log(4.8f / 1.5f, 2f));
            for (int i = 0; i < y.Length; i++)
            {
                rev.Process(y[i], y[i], out float l, out float r);
                o[i] = y[i] * 0.78f + (l + r) * 0.5f * comp * 1.9f;
            }
            return o;
        }

        // ------------------------------------------------------------------------------------------------ alien

        static float[] Alien(float[] x, uint seed)
        {
            int R = OutRate;
            Level(x, R, -22f, 2.8f, 5f, 120f, 4f);
            var low = PitchShift(x, -3.2f, R, 40f);
            var dbl = PitchShift(x, 1.7f, R, 34f);
            int lead = (int)(0.22 * R), tail = (int)(0.5 * R);
            var y = new float[lead + x.Length + tail];
            var dly = new DelayLine((int)(0.05f * R));
            float ph = 0f, wob = 0f;
            var rng = new Rng(seed + 2);
            var sv = new Svf();
            var dc = new DcBlock();
            for (int i = 0; i < x.Length; i++)
            {
                float s = low[i] * 0.75f + x[i] * 0.3f;
                dly.Write(dbl[i]);
                float d = dly.Read(0.0113f * R);
                float v = s + d * 0.55f;
                // ring modulation: an inhuman buzz
                ph += 66f / R; ph -= MathF.Floor(ph);
                v = v * 0.66f + v * MathF.Sin(ph * Dsp.TwoPi) * 0.5f;
                // a resonance that wanders like a throat that does not work the way ours do
                wob += 0.37f / R; wob -= MathF.Floor(wob);
                if ((i & 15) == 0) sv.Set(900f + 650f * MathF.Sin(wob * Dsp.TwoPi), 0.8f, R);
                sv.Process(v);
                v = v * 0.55f + sv.Band * 0.8f;
                y[lead + i] = dc.Process(Dsp.Saturate(v, 2.0f, 0.04f));
            }
            // a purr under the line and a scatter of clicks over it
            var purr = Noise(y.Length, seed + 31, 60f, 380f, R);
            float pph = 0f;
            for (int i = lead; i < lead + x.Length; i++)
            {
                pph += 29f / R; pph -= MathF.Floor(pph);
                y[i] += purr[i] * 0.10f * (0.5f + 0.5f * MathF.Sin(pph * Dsp.TwoPi));
                if (rng.Float() < 14f / R)
                {
                    float f = rng.Range(1800f, 5200f); float e = 1f;
                    for (int j = 0; j < 280 && i + j < y.Length; j++) { y[i + j] += MathF.Sin(j * Dsp.TwoPi * f / R) * e * 0.05f; e *= 0.985f; }
                }
            }
            var hp = Biquad.Make(BiquadType.HighPass, R, 200f, 0.7f); var lp = Biquad.Make(BiquadType.LowPass, R, 5200f, 0.7f);
            for (int i = 0; i < y.Length; i++) y[i] = lp.Process(hp.Process(y[i]));
            Normalize(y, -4f);
            // the carrier: a bubbling chirp up on opening, a chirp down on closing
            for (int i = 0; i < (int)(0.12 * R); i++) { float t = i / (float)R; y[i] += MathF.Sin(Dsp.TwoPi * (500f * t + 9000f * t * t)) * MathF.Exp(-t / 0.05f) * 0.2f; }
            int cat = lead + x.Length;
            for (int i = 0; i < (int)(0.16 * R) && cat + i < y.Length; i++) { float t = i / (float)R; y[cat + i] += MathF.Sin(Dsp.TwoPi * (2200f * t - 7000f * t * t)) * MathF.Exp(-t / 0.05f) * 0.18f; }
            var rev = new FdnReverb(R, 1.1f, 0.7f, 0.5f, 6f, 1.0f, seed + 9) { LowCut = 200f };
            var o = new float[y.Length];
            float comp = Db.ToLin(3.6f - 3.2f * MathF.Log(1.1f / 1.5f, 2f));
            for (int i = 0; i < y.Length; i++) { rev.Process(y[i], y[i], out float l, out float r); o[i] = y[i] + (l + r) * 0.5f * comp * 0.35f; }
            return o;
        }
    }
}
