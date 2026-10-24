using System;
using Monolith.Audio;

namespace Monolith.AudioTools
{
    /// <summary>Spectrogram + waveform plot of a buffer, to look at a sound without hearing it.</summary>
    public static class Spectro
    {
        public static void Fft(float[] re, float[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { float t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len;
                float wr = (float)Math.Cos(ang), wi = (float)Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    float cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = i + k + len / 2;
                        float xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi; re[a] += xr; im[a] += xi;
                        float ncr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = ncr;
                    }
                }
            }
        }

        static void Color(float t, out byte r, out byte g, out byte b)
        {
            t = Math.Clamp(t, 0f, 1f);
            // black, deep violet, red, orange, pale yellow
            float[] stops = { 0f, 0.25f, 0.5f, 0.75f, 1f };
            float[][] cols = { new[] { 0f, 0f, 0.02f }, new[] { 0.28f, 0.04f, 0.42f }, new[] { 0.78f, 0.18f, 0.28f }, new[] { 0.98f, 0.55f, 0.1f }, new[] { 0.99f, 0.98f, 0.62f } };
            int i = 0; while (i < 3 && t > stops[i + 1]) i++;
            float f = (t - stops[i]) / (stops[i + 1] - stops[i]);
            r = (byte)(255 * (cols[i][0] + (cols[i + 1][0] - cols[i][0]) * f));
            g = (byte)(255 * (cols[i][1] + (cols[i + 1][1] - cols[i][1]) * f));
            b = (byte)(255 * (cols[i][2] + (cols[i + 1][2] - cols[i][2]) * f));
        }

        /// <summary>Draws a waveform strip and a log-frequency spectrogram into the rgb buffer at (x0, y0).</summary>
        public static void Draw(byte[] rgb, int imgW, int x0, int y0, int w, int specH, int waveH, PcmBuffer b, float seconds0 = 0f, float seconds1 = -1f)
        {
            int rate = b.SampleRate;
            var mono = b.Mono();
            int i0 = (int)(seconds0 * rate), i1 = seconds1 < 0 ? mono.Length : Math.Min(mono.Length, (int)(seconds1 * rate));
            int n = Math.Max(1, i1 - i0);
            for (int px = 0; px < w; px++)
            {
                int a = i0 + (int)((long)n * px / w), c = i0 + (int)((long)n * (px + 1) / w);
                float lo = 0, hi = 0;
                for (int i = a; i < Math.Max(a + 1, c) && i < mono.Length; i++) { lo = Math.Min(lo, mono[i]); hi = Math.Max(hi, mono[i]); }
                int mid = y0 + waveH / 2;
                int ya = mid - (int)(Math.Min(1f, hi) * waveH / 2), yb = mid - (int)(Math.Max(-1f, lo) * waveH / 2);
                for (int y = y0; y < y0 + waveH; y++)
                {
                    int o = (y * imgW + x0 + px) * 3;
                    bool on = y >= ya && y <= Math.Max(yb, ya);
                    bool clip = (hi >= 0.999f || lo <= -0.999f) && on;
                    rgb[o] = (byte)(clip ? 255 : on ? 80 : 14); rgb[o + 1] = (byte)(clip ? 40 : on ? 160 : 14); rgb[o + 2] = (byte)(clip ? 40 : on ? 235 : 22);
                }
            }
            const int N = 4096;
            var win = new float[N];
            for (int i = 0; i < N; i++) win[i] = 0.5f - 0.5f * MathF.Cos(2 * MathF.PI * i / N);
            var re = new float[N]; var im = new float[N];
            float fmin = 30f, fmax = Math.Min(20000f, rate / 2f - 100f);
            var binOfRow = new float[specH];
            for (int y = 0; y < specH; y++)
            {
                float f = fmin * MathF.Pow(fmax / fmin, 1f - (y + 0.5f) / specH);
                binOfRow[y] = f / rate * N;
            }
            int ySpec = y0 + waveH + 2;
            for (int px = 0; px < w; px++)
            {
                int center = i0 + (int)((long)n * px / w + n / (2L * w));
                for (int i = 0; i < N; i++) { int idx = center - N / 2 + i; re[i] = idx >= 0 && idx < mono.Length ? mono[idx] * win[i] : 0f; im[i] = 0f; }
                Fft(re, im);
                for (int y = 0; y < specH; y++)
                {
                    float bin = binOfRow[y];
                    int k = (int)bin; if (k < 1) k = 1; if (k >= N / 2 - 1) k = N / 2 - 2;
                    float m = 0f;
                    int k2 = Math.Max(k, (int)(binOfRow[Math.Max(0, y - 1)] + 0.5f));
                    for (int kk = Math.Min(k, k2); kk <= Math.Max(k, k2); kk++) m = MathF.Max(m, MathF.Sqrt(re[kk] * re[kk] + im[kk] * im[kk]));
                    float db = 20f * MathF.Log10(m / (N * 0.25f) + 1e-9f);
                    Color((db + 105f) / 80f, out byte r, out byte g, out byte bl);
                    int o = ((ySpec + y) * imgW + x0 + px) * 3;
                    rgb[o] = r; rgb[o + 1] = g; rgb[o + 2] = bl;
                }
            }
            foreach (float f in new[] { 100f, 1000f, 10000f })
            {
                int y = (int)((1f - MathF.Log(f / fmin) / MathF.Log(fmax / fmin)) * specH);
                if (y < 0 || y >= specH) continue;
                for (int px = 0; px < 8; px++) { int o = ((ySpec + y) * imgW + x0 + px) * 3; rgb[o] = rgb[o + 1] = rgb[o + 2] = 255; }
            }
        }

        public static readonly float[] BandCentres = { 31.5f, 63f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f };

        /// <summary>Average power in octave bands (31 Hz to 16 kHz), in dB re full scale (per band, summed over the file).</summary>
        public static float[] Bands(PcmBuffer b)
        {
            const int N = 8192;
            var mono = b.Mono();
            var win = new float[N];
            for (int i = 0; i < N; i++) win[i] = 0.5f - 0.5f * MathF.Cos(2 * MathF.PI * i / N);
            var power = new double[BandCentres.Length];
            var re = new float[N]; var im = new float[N];
            int frames = 0;
            for (int s = 0; s + N <= mono.Length; s += N / 2)
            {
                for (int i = 0; i < N; i++) { re[i] = mono[s + i] * win[i]; im[i] = 0f; }
                Fft(re, im);
                for (int k = 1; k < N / 2; k++)
                {
                    float f = (float)k * b.SampleRate / N;
                    int band = (int)Math.Round(Math.Log(f / 31.5f, 2.0));
                    if (band < 0 || band >= power.Length) continue;
                    power[band] += (re[k] * re[k] + im[k] * im[k]) / (N * N * 0.375);
                }
                frames++;
            }
            var res = new float[power.Length];
            for (int i = 0; i < res.Length; i++) res[i] = (float)(10 * Math.Log10(power[i] / Math.Max(1, frames) * 2 + 1e-14));
            return res;
        }

        public static string BandsLine(PcmBuffer b)
        {
            var v = Bands(b);
            float mx = float.MinValue; foreach (var x in v) mx = Math.Max(mx, x);
            var sb = new System.Text.StringBuilder("bands (dB re loudest band) ");
            for (int i = 0; i < v.Length; i++) sb.Append($"{(BandCentres[i] >= 1000 ? (BandCentres[i] / 1000f).ToString("0.#") + "k" : BandCentres[i].ToString("0"))}:{v[i] - mx:0} ");
            return sb.ToString();
        }

        public static void Write(string path, PcmBuffer b, int w = 1500, int specH = 360, int waveH = 90)
        {
            int h = specH + waveH + 2;
            var rgb = new byte[w * h * 3];
            Draw(rgb, w, 0, 0, w, specH, waveH, b);
            Png.Write(path, w, h, rgb);
        }
    }
}
