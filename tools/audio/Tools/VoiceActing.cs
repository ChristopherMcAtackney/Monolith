using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    /// <summary>
    /// The direction a line gets between the text-to-speech and the radio: how hurried the speaker is. A tense or frantic line is asked for in a
    /// strained voice by the model (cast.json), and then done here: the long pauses a calm read leaves are squeezed out, the line is sped up to the
    /// rate of someone under fire (a time stretch that keeps the voice's character, ffmpeg's rubberband), the pitch is pushed up a little as a frightened
    /// voice rises, and the breaths a person takes between shouts are put in (a sharp inhale before the line, a gasp in the longest gap, a ragged
    /// breath out at the end of a shout). Offline only: the player's own recordings are never sped up; only the radio filter touches them.
    /// </summary>
    public static class VoiceActing
    {
        public struct Spec
        {
            public float TargetWps;      // words a second the line should reach (the stretch never slows a line)
            public float MaxTempo;
            public float PitchSemis;
            public float MaxGapS;        // internal pauses longer than this are cut down to it
            public float InhaleDb;       // level of the breath in, relative to the speech peak (0 = none)
            public bool Gasp, BreathOut;
        }

        public static Spec For(RadioIntensity i)
        {
            switch (i)
            {
                case RadioIntensity.Tense: return new Spec { TargetWps = 3.5f, MaxTempo = 1.22f, PitchSemis = 0f, MaxGapS = 0.20f, InhaleDb = -27f, Gasp = false, BreathOut = false };
                case RadioIntensity.Frantic: return new Spec { TargetWps = 4.4f, MaxTempo = 1.32f, PitchSemis = 0f, MaxGapS = 0.11f, InhaleDb = -21f, Gasp = true, BreathOut = true };
                default: return new Spec { TargetWps = 0f, MaxTempo = 1f, PitchSemis = 0f, MaxGapS = 9f, InhaleDb = 0f };
            }
        }

        public static int Words(string text) => Regex.Matches(text ?? "", "[A-Za-z']+").Count;

        static float Peak(float[] x) { float p = 0f; foreach (var v in x) p = Math.Max(p, Math.Abs(v)); return p; }

        /// <summary>Per 5 ms frame: is it quiet (below a threshold relative to the loudest part)?</summary>
        static bool[] QuietFrames(float[] x, int win)
        {
            int n = x.Length / win;
            var quiet = new bool[n];
            float th = Math.Max(0.004f, 0.03f * Peak(x));
            for (int i = 0; i < n; i++) { double s = 0; for (int k = 0; k < win; k++) { float v = x[i * win + k]; s += v * v; } quiet[i] = Math.Sqrt(s / win) < th; }
            return quiet;
        }

        /// <summary>Speech seconds between the first and last sound.</summary>
        public static float SpeechSeconds(float[] x, int rate)
        {
            int win = Math.Max(1, rate / 200);
            var q = QuietFrames(x, win);
            int a = Array.IndexOf(q, false), b = Array.LastIndexOf(q, false);
            return a < 0 ? 0f : (b - a + 1) * win / (float)rate;
        }

        /// <summary>
        /// Cut every pause inside the line that is longer than <paramref name="maxGapS"/> down to that length (the middle is removed, with a short
        /// crossfade). <paramref name="longestGapAt"/> is where the longest of the pauses was, in samples of the result (-1 with none).
        /// </summary>
        public static float[] SqueezePauses(float[] x, int rate, float maxGapS, out int longestGapAt)
        {
            longestGapAt = -1;
            int win = Math.Max(1, rate / 200);          // 5 ms
            if (x.Length / win < 4) return x;
            var quiet = QuietFrames(x, win);
            int first = Array.IndexOf(quiet, false), last = Array.LastIndexOf(quiet, false);
            if (first < 0) return x;
            var o = new List<float>(x.Length);
            int maxFrames = Math.Max(1, (int)(maxGapS / 0.005f));
            int longest = 0, pos = first;
            while (pos <= last)
            {
                bool q = quiet[pos];
                int e = pos;
                while (e <= last && quiet[e] == q) e++;
                if (!q) Append(o, x, pos * win, e * win, rate);
                else
                {
                    int gl = e - pos;
                    if (gl > maxFrames)
                    {
                        int h = maxFrames / 2;
                        Append(o, x, pos * win, (pos + h) * win, rate);
                        int join = o.Count;
                        Append(o, x, (e - (maxFrames - h)) * win, e * win, rate);
                        if (gl > longest) { longest = gl; longestGapAt = join; }
                    }
                    else
                    {
                        Append(o, x, pos * win, e * win, rate);
                        if (gl > longest) { longest = gl; longestGapAt = o.Count - gl * win / 2; }
                    }
                }
                pos = e;
            }
            return o.ToArray();
        }

        /// <summary>Append a piece with a 3 ms crossfade into what is there.</summary>
        static void Append(List<float> dst, float[] src, int a, int b, int rate)
        {
            a = Math.Max(0, a); b = Math.Min(src.Length, b);
            if (b <= a) return;
            int fade = Math.Min((int)(0.003f * rate), Math.Min(b - a, dst.Count));
            for (int i = 0; i < fade; i++)
            {
                float t = (i + 1f) / (fade + 1f);
                dst[dst.Count - fade + i] = dst[dst.Count - fade + i] * (1f - t) + src[a + i] * t;
            }
            for (int i = fade; i < b - a; i++) dst.Add(src[a + i]);
        }

        /// <summary>Time-stretch and pitch-shift with ffmpeg's rubberband (formants preserved, so the same person is simply quicker and higher).</summary>
        public static float[] Stretch(float[] x, int rate, float tempo, float semis)
        {
            if (Math.Abs(tempo - 1f) < 0.005f && Math.Abs(semis) < 0.01f) return x;
            string tmp = Path.Combine(Path.GetTempPath(), "mn_va_" + Guid.NewGuid().ToString("N"));
            string a = tmp + "_in.wav", b = tmp + "_out.wav";
            try
            {
                Wav.Write(a, PcmBuffer.FromMono(rate, x), 16, false);
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                string pitch = Math.Pow(2.0, semis / 12.0).ToString("0.0000", ic);
                string tp = tempo.ToString("0.0000", ic);
                int code = Paths.Run(Paths.Ffmpeg, $"-y -v error -i \"{a}\" -af \"rubberband=tempo={tp}:pitch={pitch}:formant=preserved:transients=crisp\" -c:a pcm_s16le \"{b}\"", out var o);
                if (code != 0) throw new Exception("ffmpeg rubberband failed: " + o);
                return Wav.Read(b).Mono();
            }
            finally { try { File.Delete(a); File.Delete(b); } catch { } }
        }

        /// <summary>A breath: filtered noise, a quick inhale that swells or a ragged exhale that dies away.</summary>
        public static float[] Breath(int rate, float seconds, bool inhale, float peak, uint seed)
        {
            int n = (int)(seconds * rate);
            var y = new float[n];
            var rng = new Rng(seed);
            var bp = new Svf();
            var hp = Biquad.Make(BiquadType.HighPass, rate, 180f, 0.7f);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float env = inhale ? MathF.Pow(MathF.Sin(MathF.Min(1f, t * 1.15f) * Dsp.Pi * 0.5f), 1.6f) * (t > 0.88f ? (1f - t) / 0.12f : 1f)
                                   : MathF.Exp(-t * 4.2f) * MathF.Min(1f, t / 0.06f);
                float fc = inhale ? 700f + 900f * t : 1500f - 800f * t;
                if ((i & 31) == 0) bp.Set(fc, 0.9f, rate);
                float nz = rng.Signed();
                bp.Process(nz);
                float v = bp.Band * 0.8f + nz * 0.12f;
                lp += (v - lp) * 0.35f;
                y[i] = hp.Process(lp) * env;
            }
            float p = Peak(y);
            if (p > 1e-6f) for (int i = 0; i < n; i++) y[i] *= peak / p;
            return y;
        }

        /// <summary>Direct a dry line: returns the performance (same rate, mono) and a note for the log. A calm line is left as it is.</summary>
        public static float[] Direct(float[] dry, int rate, RadioIntensity inten, string text, uint seed, out string note)
        {
            note = "";
            if (inten == RadioIntensity.Calm) return dry;
            var spec = For(inten);
            var x = RadioFx.TrimSilence(dry, rate, -42f, 5f);
            float before = SpeechSeconds(x, rate);
            x = SqueezePauses(x, rate, spec.MaxGapS, out int gapAt);
            float speech = SpeechSeconds(x, rate);
            int words = Words(text);
            float wps = speech > 0.05f ? words / speech : spec.TargetWps;
            float tempo = Math.Clamp(spec.TargetWps / Math.Max(wps, 0.5f), 1f, spec.MaxTempo);
            x = Stretch(x, rate, tempo, spec.PitchSemis);
            float peak = Peak(x);
            if (peak > 1e-5f) { float g = 0.7f / peak; for (int i = 0; i < x.Length; i++) x[i] *= g; peak = 0.7f; }
            float after = SpeechSeconds(x, rate);

            // the breaths: a tense line breathes in on about half of them, a frantic one on every one
            float pk = peak * MathF.Pow(10f, spec.InhaleDb / 20f);
            float[] inhale = null;
            if (spec.InhaleDb < 0f && (inten == RadioIntensity.Frantic || (Dsp.StableHash(text) & 1u) == 0u))
                inhale = Breath(rate, inten == RadioIntensity.Frantic ? 0.17f : 0.13f, true, pk, seed + 41);
            int lead = inhale == null ? 0 : inhale.Length + (int)(0.015f * rate);
            int tail = spec.BreathOut ? (int)(0.19f * rate) : 0;
            var body = new float[lead + x.Length + tail];
            Array.Copy(x, 0, body, lead, x.Length);
            if (inhale != null) for (int i = 0; i < inhale.Length; i++) body[i] += inhale[i];
            if (spec.Gasp && gapAt > 0 && after > 1.2f)
            {
                int at = lead + (int)(gapAt / tempo);                 // the gap moved with the stretch
                var g = Breath(rate, 0.09f, true, pk * 0.8f, seed + 43);
                for (int i = 0; i < g.Length && at + i < lead + x.Length - 8; i++) body[at + i] += g[i];
            }
            if (spec.BreathOut)
            {
                var o = Breath(rate, 0.22f, false, pk * 0.9f, seed + 47);
                int at = lead + x.Length - (int)(0.03f * rate);
                for (int i = 0; i < o.Length && at + i < body.Length; i++) body[at + i] += o[i];
            }
            note = $"{wps:0.0}->{words / Math.Max(after, 0.05f):0.0} words/s, speech {before:0.0}->{after:0.0} s, tempo {tempo:0.00}, pitch +{spec.PitchSemis:0.0}";
            return body;
        }
    }
}
