using System;
using System.Collections.Generic;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    /// <summary>The controller curves of a track the voices read while they sound.</summary>
    public sealed class TrackAuto
    {
        public Curve Expression = new Curve { Default = 1f };    // CC11
        public Curve Mod = new Curve { Default = 0f };           // CC1: vibrato depth
        public Curve Cutoff = new Curve { Default = 0.5f };      // CC74: 0.5 = as the patch, +-0.5 = +-2 octaves
        public Curve Pan = new Curve { Default = 0.5f };         // CC10
        public Curve Vowel = new Curve { Default = 0f };         // CC20: morph between the first and second vowel
        public Curve Bend = new Curve { Default = 0f };          // pitch bend, -1..1
    }

    /// <summary>Renders one MIDI note of a patch into stereo buffers. Everything per sample is allocation-free.</summary>
    public static class SynthVoice
    {
        const int Ctl = 16;     // control-rate update interval in samples

        public static void Render(Patch p, MidiNote note, double lastKey, double glideGap, TrackAuto au, float[] L, float[] R, int rate, uint seed, double endCut)
        {
            switch (p.Kind)
            {
                case VoiceKind.Fm: RenderFm(p, note, au, L, R, rate, seed); break;
                case VoiceKind.Formant: RenderFormant(p, note, au, L, R, rate, seed); break;
                default: RenderSub(p, note, lastKey, glideGap, au, L, R, rate, seed, endCut); break;
            }
        }

        // ------------------------------------------------------------------------------------------------ subtractive

        static void RenderSub(Patch p, MidiNote note, double lastKey, double glideGap, TrackAuto au, float[] L, float[] R, int rate, uint seed, double endCut)
        {
            float fs = rate;
            int i0 = (int)Math.Round(note.Start * fs);
            double dur = note.Dur;
            if (endCut > 0 && endCut < note.Start + dur) dur = Math.Max(0.005, endCut - note.Start);       // a mono voice is cut by the next note
            int nGate = Math.Max(1, (int)Math.Round(dur * fs));
            int maxLen = nGate + (int)((p.Amp[3] * 1.5f + 0.15f) * fs);
            if (i0 >= L.Length) return;

            var rng = new Rng(seed);
            float vel = note.Vel / 127f;
            float velGain = (1f - p.VelSens) + p.VelSens * vel * vel;
            float velCut = (vel - 0.6f) * p.VelCut;

            var amp = new Adsr(); amp.Set(p.Amp[0], p.Amp[1], p.Amp[2], p.Amp[3], fs); amp.Gate(true);
            var fenv = new Adsr(); fenv.Set(p.FEnv[0], p.FEnv[1], p.FEnv[2], p.FEnv[3], fs); fenv.Gate(true);

            int nOsc = p.Osc.Count, nUni = Math.Max(1, p.Unison);
            var phase = new float[nOsc * nUni]; var uniCents = new float[nUni]; var uniPan = new float[nUni];
            var driftS = new Drift[nUni];
            for (int u = 0; u < nUni; u++)
            {
                float x = nUni == 1 ? 0f : (u / (float)(nUni - 1)) * 2f - 1f;
                uniCents[u] = x * p.UniCents;
                uniPan[u] = x * p.Spread;
                driftS[u] = new Drift(Osc.Hash(seed, (uint)u, 11), 0.7f, fs);
            }
            for (int k = 0; k < phase.Length; k++) phase[k] = rng.Float();
            float subPhase = rng.Float();
            float noiseLp = 0f;

            var ladL = new Ladder(); var ladR = new Ladder();
            var svfL = new Svf(); var svfR = new Svf(); var svf2L = new Svf(); var svf2R = new Svf();
            float key = note.Key;
            double basePan = (au.Pan.Eval(note.Start) - 0.5) * 2.0;
            float balL = (float)(1.0 - Math.Max(0.0, basePan)), balR = (float)(1.0 - Math.Max(0.0, -basePan));

            // glide: the note starts at the last pitch and slides to its own
            float glideSemis = 0f, glideCoef = 0f;
            if (p.Glide > 0f && lastKey >= 0 && glideGap <= (p.Mono ? 0.08 : 0.25))
            {
                glideSemis = (float)(lastKey - key);
                glideCoef = MathF.Exp(-1f / (MathF.Max(p.Glide, 0.005f) * fs / 3f));
            }

            float cutoff = p.Cut, fcur = 0f;
            float expr = 1f, mod = 0f, cc74 = 0f, bend = 0f;
            float semisNow = 0f;
            float lfoPhase = rng.Float();
            float freqBase = Dsp.MidiToHz(key);
            float dtScale = 1f / fs;
            bool released = false;
            float lastAmp = 0f;

            for (int i = 0; i < maxLen; i++)
            {
                int idx = i0 + i;
                if (idx >= L.Length) break;
                if (i == nGate) { amp.Gate(false); fenv.Gate(false); released = true; }
                float t = i * dtScale;
                double tAbs = note.Start + t;

                if ((i % Ctl) == 0)
                {
                    expr = au.Expression.Eval(tAbs);
                    mod = au.Mod.Eval(tAbs);
                    cc74 = (au.Cutoff.Eval(tAbs) - 0.5f) * 4f;
                    bend = au.Bend.Eval(tAbs) * p.BendRange;
                    glideSemis *= MathF.Pow(glideCoef == 0f ? 0f : glideCoef, Ctl);
                    float scoop = p.ScoopSemis != 0f ? -p.ScoopSemis * MathF.Exp(-t / p.ScoopTime) : 0f;
                    float vibDepth = (p.VibCents * Dsp.Clamp01((t - p.VibDelay) / 0.4f) + 22f * mod) * MathF.Sin(Dsp.TwoPi * p.VibRate * t) / 100f;
                    lfoPhase += p.LfoRate * Ctl * dtScale; if (lfoPhase > 1f) lfoPhase -= 1f;
                    semisNow = bend + glideSemis + scoop + vibDepth;
                    // filter cutoff
                    float lfo = MathF.Sin(lfoPhase * Dsp.TwoPi);
                    float fe = fenv.Level;
                    float octs = p.FEnvAmt * fe + cc74 + velCut + p.LfoCut * lfo + p.ExprToCut * (expr - 1f) + p.KeyTrack * (key - 60f) / 12f;
                    cutoff = MathF.Min(p.Cut * MathF.Pow(2f, octs), 0.45f * fs);
                    if (p.Filter == FilterKind.Ladder) { ladL.Set(cutoff, p.Res, fs); ladR.Set(cutoff, p.Res, fs); }
                    else if (p.Filter == FilterKind.Svf12 || p.Filter == FilterKind.Svf24)
                    { svfL.Set(cutoff, p.Res, fs); svfR.Set(cutoff, p.Res, fs); svf2L.Set(cutoff, p.Res * 0.5f, fs); svf2R.Set(cutoff, p.Res * 0.5f, fs); }
                }

                float sl = 0f, sr = 0f;
                for (int u = 0; u < nUni; u++)
                {
                    float dr = driftS[u].Next() * p.Drift;
                    for (int o = 0; o < nOsc; o++)
                    {
                        var od = p.Osc[o];
                        float cents = od.Cents + uniCents[u] + dr + od.Semis * 100f;
                        float f = freqBase * MathF.Pow(2f, (semisNow * 100f + cents) / 1200f);
                        float dt = f * dtScale;
                        int pi = o * nUni + u;
                        float ph = phase[pi] + dt; if (ph >= 1f) ph -= 1f; phase[pi] = ph;
                        float w = od.Width;
                        if (od.PwmDepth > 0f) w = Dsp.Clamp(w + od.PwmDepth * MathF.Sin(Dsp.TwoPi * od.PwmRate * (t + o * 0.37f + u * 0.11f)), 0.06f, 0.94f);
                        float v = Osc.Wave(od.Wave, ph, dt, w) * od.Level;
                        float pu = uniPan[u];
                        sl += v * (1f - MathF.Max(0f, pu)); sr += v * (1f - MathF.Max(0f, -pu));
                    }
                }
                float norm = 1f / MathF.Sqrt(nUni);
                sl *= norm; sr *= norm;
                if (p.Sub > 0f)
                {
                    float fsub = freqBase * 0.5f * MathF.Pow(2f, semisNow / 12f);
                    subPhase += fsub * dtScale; if (subPhase >= 1f) subPhase -= 1f;
                    float sv = (p.SubWave == 2 ? (subPhase < 0.5f ? 1f : -1f) * 0.7f : p.SubWave == 3 ? Osc.Sine(subPhase) : Osc.Tri(subPhase)) * p.Sub;
                    sl += sv; sr += sv;
                }
                if (p.Noise > 0f)
                {
                    float nz = rng.Signed();
                    noiseLp += (nz - noiseLp) * p.NoiseColor;
                    float nv = noiseLp * p.Noise;
                    sl += nv; sr += nv;
                }

                switch (p.Filter)
                {
                    case FilterKind.Ladder: sl = ladL.Process(sl, p.Drive) * (1f + p.Res * 0.9f); sr = ladR.Process(sr, p.Drive) * (1f + p.Res * 0.9f); break;
                    case FilterKind.Svf12: svfL.Process(sl); svfR.Process(sr); sl = svfL.Low; sr = svfR.Low; break;
                    case FilterKind.Svf24: svfL.Process(sl); svf2L.Process(svfL.Low); svfR.Process(sr); svf2R.Process(svfR.Low); sl = svf2L.Low; sr = svf2R.Low; break;
                }

                float a = amp.Next();
                if (p.AmpPercussive && !released) { a *= MathF.Exp(-t / MathF.Max(p.Amp[1], 0.02f) * 3f); }
                if (!amp.Active) break;
                float g = a * velGain * expr * p.Gain;
                lastAmp = g;
                L[idx] += sl * g * balL; R[idx] += sr * g * balR;
            }
        }

        // ------------------------------------------------------------------------------------------------ FM (bells, electric piano)

        static void RenderFm(Patch p, MidiNote note, TrackAuto au, float[] L, float[] R, int rate, uint seed)
        {
            float fs = rate;
            int i0 = (int)Math.Round(note.Start * fs);
            int nGate = Math.Max(1, (int)Math.Round(note.Dur * fs));
            float relT = p.Amp[3];
            float maxDecay = 0f; foreach (var f in p.Fm) maxDecay = MathF.Max(maxDecay, f.AmpDecay);
            int maxLen = nGate + (int)((relT + 0.1f) * fs) + (int)(MathF.Min(maxDecay, 6f) * fs * 0.9f);
            if (i0 >= L.Length) return;
            var rng = new Rng(seed);
            float vel = note.Vel / 127f;
            float velGain = (1f - p.VelSens) + p.VelSens * vel * vel;
            float freq = Dsp.MidiToHz(note.Key);
            int np = p.Fm.Count;
            var ph = new float[np]; var phm = new float[np];
            for (int k = 0; k < np; k++) { ph[k] = rng.Float() * 0.02f; phm[k] = 0f; }
            double basePan = (au.Pan.Eval(note.Start) - 0.5) * 2.0;
            float pan = (float)basePan;
            var dr = new Drift(Osc.Hash(seed, 3), 0.6f, fs);
            float releaseEnv = 1f; float relCoef = MathF.Exp(-1f / (MathF.Max(relT, 0.01f) * fs / 4.6f));
            for (int i = 0; i < maxLen; i++)
            {
                int idx = i0 + i; if (idx >= L.Length) break;
                float t = i / fs;
                if (i >= nGate) { releaseEnv *= relCoef; if (releaseEnv < 1e-4f) break; }
                float expr = au.Expression.Eval(note.Start + t);
                float y = 0f;
                float f0 = freq * MathF.Pow(2f, dr.Next() * p.Drift / 1200f);
                for (int k = 0; k < np; k++)
                {
                    var pr = p.Fm[k];
                    float idxv = pr.IndexSustain + (pr.Index - pr.IndexSustain) * MathF.Exp(-t / MathF.Max(pr.IndexDecay, 0.01f));
                    idxv *= 0.6f + 0.4f * vel;
                    phm[k] += f0 * pr.ModRatio / fs; if (phm[k] >= 1f) phm[k] -= 1f;
                    ph[k] += f0 * pr.CarrierRatio / fs; if (ph[k] >= 1f) ph[k] -= 1f;
                    float m = MathF.Sin(Dsp.TwoPi * phm[k]) * idxv;
                    float c = MathF.Sin(Dsp.TwoPi * ph[k] + m);
                    y += c * pr.Level * MathF.Exp(-t * 6.9078f / MathF.Max(pr.AmpDecay, 0.02f));
                }
                float aatt = Dsp.Clamp01(t / MathF.Max(p.Amp[0], 0.0015f));
                y *= aatt * releaseEnv * velGain * expr * p.Gain;
                L[idx] += y * (1f - MathF.Max(0f, pan)); R[idx] += y * (1f - MathF.Max(0f, -pan));
            }
        }

        // ------------------------------------------------------------------------------------------------ formant (choir)

        static void RenderFormant(Patch p, MidiNote note, TrackAuto au, float[] L, float[] R, int rate, uint seed)
        {
            float fs = rate;
            int i0 = (int)Math.Round(note.Start * fs);
            int nGate = Math.Max(1, (int)Math.Round(note.Dur * fs));
            int maxLen = nGate + (int)((p.Amp[3] * 1.5f + 0.15f) * fs);
            if (i0 >= L.Length) return;
            var rng = new Rng(seed);
            var amp = new Adsr(); amp.Set(p.Amp[0], p.Amp[1], p.Amp[2], p.Amp[3], fs); amp.Gate(true);
            float vel = note.Vel / 127f;
            float velGain = (1f - p.VelSens) + p.VelSens * vel * vel;
            int nVoices = Math.Max(1, p.Unison);
            var ph = new float[nVoices]; var cents = new float[nVoices]; var pans = new float[nVoices];
            var dr = new Drift[nVoices]; var vibPh = new float[nVoices];
            // three formants per voice and side
            var f1 = new Svf[nVoices]; var f2 = new Svf[nVoices]; var f3 = new Svf[nVoices];
            for (int u = 0; u < nVoices; u++)
            {
                float x = nVoices == 1 ? 0f : (u / (float)(nVoices - 1)) * 2f - 1f;
                cents[u] = x * p.UniCents + rng.Signed() * 3f; pans[u] = x * p.Spread;
                ph[u] = rng.Float(); dr[u] = new Drift(Osc.Hash(seed, (uint)u, 5), 0.5f, fs); vibPh[u] = rng.Float();
            }
            float freq = Dsp.MidiToHz(note.Key);
            float breathLp = 0f;
            for (int i = 0; i < maxLen; i++)
            {
                int idx = i0 + i; if (idx >= L.Length) break;
                if (i == nGate) amp.Gate(false);
                float t = i / fs;
                double tAbs = note.Start + t;
                float expr = au.Expression.Eval(tAbs);
                float morph = Dsp.Clamp01(au.Vowel.Eval(tAbs));
                float vibAmt = (p.VibCents * Dsp.Clamp01((t - p.VibDelay) / 0.6f) + 20f * au.Mod.Eval(tAbs));
                if ((i % Ctl) == 0)
                {
                    var va = p.Vowels[0]; var vb = p.Vowels.Length > 1 ? p.Vowels[1] : va;
                    for (int u = 0; u < nVoices; u++)
                    {
                        float q = Dsp.Lerp(va[6], vb[6], morph);
                        // an SVF's resonance is 1 - 1/(2Q) style; map a Q of ~8 to 0.94
                        float r = 1f - 1f / (2f * q);
                        f1[u].Set(Dsp.Lerp(va[0], vb[0], morph), r, fs);
                        f2[u].Set(Dsp.Lerp(va[1], vb[1], morph), r, fs);
                        f3[u].Set(Dsp.Lerp(va[2], vb[2], morph), r, fs);
                    }
                }
                float sl = 0f, sr = 0f;
                for (int u = 0; u < nVoices; u++)
                {
                    vibPh[u] += (p.VibRate + u * 0.17f) / fs; if (vibPh[u] >= 1f) vibPh[u] -= 1f;
                    float c = cents[u] + dr[u].Next() * p.Drift + vibAmt * MathF.Sin(Dsp.TwoPi * vibPh[u]);
                    float f = freq * MathF.Pow(2f, c / 1200f);
                    float dt = f / fs;
                    ph[u] += dt; if (ph[u] >= 1f) ph[u] -= 1f;
                    float src = Osc.Saw(ph[u], dt);
                    breathLp += (rng.Signed() - breathLp) * 0.3f;
                    src += breathLp * p.Breath * 4f;
                    f1[u].Process(src); f2[u].Process(src); f3[u].Process(src);
                    var va = p.Vowels[0]; var vb = p.Vowels.Length > 1 ? p.Vowels[1] : va;
                    float g1 = Dsp.Lerp(va[3], vb[3], morph), g2 = Dsp.Lerp(va[4], vb[4], morph), g3 = Dsp.Lerp(va[5], vb[5], morph);
                    float v = f1[u].Band * g1 + f2[u].Band * g2 + f3[u].Band * g3;
                    float pu = pans[u];
                    sl += v * (1f - MathF.Max(0f, pu)); sr += v * (1f - MathF.Max(0f, -pu));
                }
                float a = amp.Next(); if (!amp.Active) break;
                float g = a * velGain * expr * p.Gain / MathF.Sqrt(nVoices);
                L[idx] += sl * g; R[idx] += sr * g;
            }
        }
    }
}
