using System;
using System.Collections.Generic;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    /// <summary>Checks of the mixer and the DSP core against known signals (run: MnAudio mixertest).</summary>
    public static class MixerTests
    {
        static int _fail;
        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
            if (!ok) _fail++;
        }

        static Clip Sine(float hz, float seconds, float amp, int rate = 48000, int ch = 1)
        {
            int n = (int)(seconds * rate);
            var b = new PcmBuffer(rate, ch, n);
            for (int c = 0; c < ch; c++) for (int i = 0; i < n; i++) b.Ch[c][i] = amp * MathF.Sin(2f * MathF.PI * hz * i / rate);
            return Clip.FromPcm(b, "sine" + hz);
        }

        static float Rms(float[] x, int a, int n) { double s = 0; for (int i = a; i < a + n; i++) s += x[i] * x[i]; return (float)Math.Sqrt(s / n); }

        public static int Run()
        {
            _fail = 0;
            const int R = 48000;
            Console.WriteLine("loudness meter");
            {
                var b = new PcmBuffer(R, 2, R * 10);
                for (int c = 0; c < 2; c++) for (int i = 0; i < b.Frames; i++) b.Ch[c][i] = 0.1f * MathF.Sin(2f * MathF.PI * 997f * i / R);
                var m = LoudnessMeter.Measure(b);
                Check(Math.Abs(m.Integrated - (-20f)) < 0.15f, $"997 Hz sine at 0.1 in both channels reads {m.Integrated:0.00} LUFS (want -20.00)");
                Check(Math.Abs(m.PeakDb - (-20f)) < 0.05f, $"sample peak {m.PeakDb:0.00} dBFS");
                float tp = LoudnessMeter.TruePeakDb(b);
                Check(Math.Abs(tp - (-20f)) < 0.2f, $"true peak {tp:0.00} dBTP");
            }

            Console.WriteLine("voices");
            {
                var mx = new Mixer(R);
                var clip = Sine(1000f, 2f, 0.5f);
                mx.Play(clip, new PlayParams { Gain = 1f, Pan = 0f, Pitch = 1f, Bus = Bus.Sfx, Priority = 1 });
                var l = new float[R]; var r = new float[R];
                mx.RenderPlanar(l, r, 0, R);
                float rl = Rms(l, 4000, 20000), rr = Rms(r, 4000, 20000);
                float expect = 0.5f / MathF.Sqrt(2f) / MathF.Sqrt(2f);
                Check(Math.Abs(rl - expect) < 0.01f && Math.Abs(rr - expect) < 0.01f, $"centre pan: rms {rl:0.0000}/{rr:0.0000} (want {expect:0.0000}, -3 dB per side)");
                var mx2 = new Mixer(R);
                mx2.Play(clip, new PlayParams { Gain = 1f, Pan = -1f, Pitch = 1f, Bus = Bus.Sfx, Priority = 1 });
                mx2.RenderPlanar(l, r, 0, R);
                Check(Rms(r, 4000, 20000) < 0.002f && Rms(l, 4000, 20000) > 0.3f, "hard-left pan: right channel silent");
                // pitch: an octave up halves the length
                var mx3 = new Mixer(R);
                mx3.Play(clip, new PlayParams { Gain = 1f, Pan = 0f, Pitch = 2f, Bus = Bus.Sfx, Priority = 1 });
                var l3 = new float[R * 2]; var r3 = new float[R * 2];
                mx3.RenderPlanar(l3, r3, 0, R * 2);
                Check(Rms(l3, 30000, 4000) > 0.1f && Rms(l3, 50000, 4000) < 0.001f, "pitch 2.0 finishes in about 1 s");
                Check(mx3.ActiveVoices == 0, "finished voices are freed");
                // sample rate conversion: a 24 kHz clip plays at the right pitch (same duration) on a 48 kHz mixer
                var mx4 = new Mixer(R);
                var c24 = Sine(1000f, 1f, 0.5f, 24000);
                mx4.Play(c24, new PlayParams { Gain = 1f, Pan = 0f, Pitch = 1f, Bus = Bus.Sfx, Priority = 1 });
                var l4 = new float[R * 2]; var r4 = new float[R * 2];
                mx4.RenderPlanar(l4, r4, 0, R * 2);
                int z = 0; for (int i = 2000; i < 40000; i++) if (l4[i] <= 0 && l4[i + 1] > 0) z++;
                float hz = z / (38000f / R);
                Check(Math.Abs(hz - 1000f) < 15f, $"a 24 kHz clip at 48 kHz measures {hz:0} Hz (want 1000)");
            }

            Console.WriteLine("loops");
            {
                var mx = new Mixer(R);
                var clip = Sine(480f, 0.5f, 0.5f);          // 480 Hz: 240 cycles in 0.5 s: closes exactly
                mx.Play(clip, new PlayParams { Gain = 1f, Pan = 0f, Pitch = 1f, Bus = Bus.Sfx, Loop = true, Priority = 1 });
                var l = new float[R * 3]; var r = new float[R * 3];
                mx.RenderPlanar(l, r, 0, R * 3);
                float maxStep = 0; for (int i = 1000; i < l.Length - 1; i++) maxStep = MathF.Max(maxStep, MathF.Abs(l[i + 1] - l[i]));
                float expectStep = 0.5f * 0.7071f * 2f * MathF.PI * 480f / R * 1.02f;
                Check(maxStep < expectStep * 1.05f, $"a looped sine has no discontinuity at the seam (max step {maxStep:0.0000}, sine's own {expectStep:0.0000})");
            }

            Console.WriteLine("master limiter");
            {
                var mx = new Mixer(R);
                var clip = Sine(300f, 2f, 1.0f);
                for (int k = 0; k < 6; k++) mx.Play(clip, new PlayParams { Gain = 1f, Pan = (k - 2.5f) * 0.1f, Pitch = 1f + k * 0.003f, Bus = Bus.Sfx, Priority = 1 });
                var l = new float[R]; var r = new float[R];
                mx.RenderPlanar(l, r, 0, R);
                Check(mx.MasterPeak > 2f, $"six full-scale voices sum to {mx.MasterPeak:0.00} before the limiter");
                Check(mx.PostLimiterPeak <= 0.9f, $"limited peak {Db.FromLin(mx.PostLimiterPeak):0.00} dBFS (ceiling -1.00)");
                Check(mx.ClippedSamples == 0, "no clipped samples after the limiter");
                // a transient on a quiet bed is limited without the bed pumping long
            }

            Console.WriteLine("limiter against a brute-force reference");
            {
                var rng = new Rng(77);
                int n = R * 3;
                var a = new float[2][]; a[0] = new float[n]; a[1] = new float[n];
                for (int i = 0; i < n; i++) { float burst = (i / 4000) % 7 == 3 ? 3.5f : 0.4f; a[0][i] = rng.Signed() * burst; a[1][i] = rng.Signed() * burst * 0.8f; }
                var b = new float[2][]; b[0] = (float[])a[0].Clone(); b[1] = (float[])a[1].Clone();
                var lim = new Limiter(R, 2, -1f, 2f, 120f);
                lim.Process(b, 0, n);
                // reference: sliding minimum by brute force, the same smoothing
                int look = Math.Max(8, (int)(0.002f * R));
                var need = new float[n]; float ceil = Db.ToLin(-1f);
                for (int i = 0; i < n; i++) { float pk = Math.Max(Math.Abs(a[0][i]), Math.Abs(a[1][i])); need[i] = pk > ceil ? ceil / pk : 1f; }
                float rr = 1f; var rb = new float[look]; for (int k = 0; k < look; k++) rb[k] = 1f; double sum = look; float rel = Dsp.TauToCoef(0.12f, R);
                float maxErr = 0f, peak = 0f;
                for (int i = 0; i < n; i++)
                {
                    float gmin = 1f; for (int k = Math.Max(0, i - look + 1); k <= i; k++) gmin = Math.Min(gmin, need[k]);
                    rr = gmin < rr ? gmin : rr + (gmin - rr) * rel;
                    sum += rr - rb[i % look]; rb[i % look] = rr;
                    float g = (float)(sum / look);
                    int src = i - (look - 1);
                    float want = src >= 0 ? Math.Clamp(a[0][src] * g, -ceil, ceil) : 0f;
                    maxErr = Math.Max(maxErr, Math.Abs(want - b[0][i]));
                    peak = Math.Max(peak, Math.Max(Math.Abs(b[0][i]), Math.Abs(b[1][i])));
                }
                Check(maxErr < 1e-4f, $"the limiter matches the brute-force reference (largest difference {maxErr:0.000000})");
                Check(peak <= ceil + 1e-6f, $"and never passes its ceiling on bursts (peak {peak:0.0000} vs {ceil:0.0000})");
            }

            Console.WriteLine("ducking");
            {
                var mx = new Mixer(R);
                var music = Sine(220f, 6f, 0.25f, R, 2);
                var voice = Sine(1000f, 2f, 0.3f);
                mx.MusicPlay(music, Quant.Now, 0f, 0f);
                var l = new float[R * 6]; var r = new float[R * 6];
                mx.RenderPlanar(l, r, 0, R);               // 1 s of music alone
                float before = Rms(l, 30000, 10000);
                mx.Play(voice, new PlayParams { Gain = 1f, Bus = Bus.Voice, Priority = 5 });
                var l2 = new float[R * 3]; var r2 = new float[R * 3];
                mx.RenderPlanar(l2, r2, 0, R * 3);
                // isolate the music: analyse via a notch is overkill; use the bus peak instead: the music bus gain during speech
                float during = Rms(l2, 30000, 4800);
                // The master carries music + voice; compare with an equal run without the duck to infer: simply check the bus duck state through a second mixer
                var mxN = new Mixer(R) { VoiceDuckMusicDb = 0f, VoiceDuckSfxDb = 0f };
                mxN.MusicPlay(music, Quant.Now, 0f, 0f);
                mxN.RenderPlanar(l, r, 0, R);
                mxN.Play(voice, new PlayParams { Gain = 1f, Bus = Bus.Voice, Priority = 5 });
                var l3 = new float[R * 3]; var r3 = new float[R * 3];
                mxN.RenderPlanar(l3, r3, 0, R * 3);
                // the difference between the two renders is the music change (voice is identical)
                var diffD = new float[12000]; var diffN = new float[12000];
                for (int i = 0; i < diffD.Length; i++) { diffD[i] = l2[30000 + i] - 0f; diffN[i] = l3[30000 + i]; }
                // music RMS = sqrt(total^2 - voice^2) approximately; cleaner: render the voice alone
                var mxV = new Mixer(R);
                mxV.Play(voice, new PlayParams { Gain = 1f, Bus = Bus.Voice, Priority = 5 });
                var lv = new float[R * 3]; var rv = new float[R * 3];
                mxV.RenderPlanar(lv, rv, 0, R * 3);
                var musicD = new float[12000]; var musicN = new float[12000];
                for (int i = 0; i < 12000; i++) { musicD[i] = l2[30000 + i] - lv[30000 + i]; musicN[i] = l3[30000 + i] - lv[30000 + i]; }
                float db = Db.FromLin(Rms(musicD, 0, 12000) / Rms(musicN, 0, 12000));
                Check(db < -8f && db > -10f, $"music is {db:0.0} dB under speech (want -9)");
                // and it comes back
                var l4 = new float[R * 3]; var r4 = new float[R * 3];
                mx.RenderPlanar(l4, r4, 0, R * 3);
                float after = Rms(l4, R * 2, 8000);
                float full = Rms(l, 30000, 10000);
                Check(Math.Abs(Db.FromLin(after / full)) < 0.6f, $"music returns to full level after speech ({Db.FromLin(after / full):0.00} dB)");
            }

            Console.WriteLine("music deck: aligned cross-fade on the bar");
            {
                var mx = new Mixer(R);
                mx.SetMusicTempo(100f, 4, 8f);
                // two 8 s loops: 24 bars? no: 100 BPM = 2.4 s a bar; use 8 s loops (3.33 bars) only for the alignment arithmetic
                var a = Sine(200f, 8f, 0.2f, R, 2);
                var b = Sine(300f, 8f, 0.2f, R, 2);
                mx.MusicPlay(a, Quant.Bar, 0.05f, 0.05f);
                var l = new float[R * 8]; var r = new float[R * 8];
                mx.RenderPlanar(l, r, 0, R);               // 1 s
                mx.MusicPlay(b, Quant.Bar, 0.05f, 0.05f);  // asked at 1.0 s: next bar line is 2.4 s
                mx.RenderPlanar(l, r, R, R * 3);
                // find when 300 Hz appears: zero-crossing spacing
                int firstB = -1;
                for (int i = R; i < R * 4 - 200 && firstB < 0; i++)
                {
                    // 300 Hz period = 160 samples, 200 Hz = 240: measure half-period between two successive upward crossings
                    if (l[i] <= 0 && l[i + 1] > 0) { int j = i + 1; while (j < i + 400 && !(l[j] <= 0 && l[j + 1] > 0)) j++; if (j - i < 190 && j - i > 120) firstB = i; }
                }
                double t = firstB / (double)R;
                Check(firstB > 0 && Math.Abs(t - 2.4) < 0.12, $"the new loop is heard from {t:0.000} s (the bar line is at 2.400 s)");
            }

            Console.WriteLine("tones and meters");
            {
                var mx = new Mixer(R);
                mx.SetTone(0, 880f, 0.5f, 4f, 0.5f);
                var l = new float[R * 2]; var r = new float[R * 2];
                mx.RenderPlanar(l, r, 0, R * 2);
                Check(Rms(l, 10000, 40000) > 0.01f, "a gated tone sounds");
                mx.SetTone(0, 880f, 0f, 4f, 0.5f);
                mx.RenderPlanar(l, r, 0, R);
                mx.RenderPlanar(l, r, 0, R);
                Check(Rms(l, 0, R) < 1e-4f, "and goes quiet when its gain is 0");
            }

            Console.WriteLine(_fail == 0 ? "MIXER TESTS PASS" : $"MIXER TESTS FAILED: {_fail}");
            return _fail == 0 ? 0 : 1;
        }
    }
}
