using System;
using System.Threading;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Look
{
    /// <summary>
    /// The dirt on the lens or canopy, drawn from a fixed seed (so it is the same every run): 2048 x 1024, three
    /// channels the glare lights up differently. R is fine dust, fibres and scratches; G is greasy smudges; B is out-of-focus dust discs
    /// (bokeh), soft and lumpy. It sits still on the screen, as dirt on glass does.
    /// <para>
    /// Drawing it takes half a second of arithmetic (534 ms measured), and it used to be done on the main thread inside the lens pass the first time the Sun was in front of the
    /// camera: a 0.5 s frame wherever that first happened (behind the loading screen in a played start, in the middle of the player's death in the juice sandbox and its checks,
    /// where the tumble first turns the camera to the Sun). Now <see cref="Prepare"/> starts the drawing on a worker thread as soon as the look is built, the camera look polls
    /// <see cref="Poll"/> once a frame and makes the texture from the finished pixels (a few milliseconds) as soon as they are ready, and <see cref="Get"/> waits for the worker if
    /// the lens needs the texture before that (a capture whose first frame has the Sun in it), so the picture is the same as before whichever way it arrives.
    /// </para>
    /// </summary>
    public static class DustTexture
    {
        public const int Width = 2048, Height = 1024;
        const int Seed = 7;
        static Texture2D _shared;
        static Thread _worker;
        static Color32[] _pixels;
        static double _drawMs;
        static string _error;

        /// <summary>The texture, made (the pixels waited for, or drawn here if nothing started them) and uploaded now if it is not already.</summary>
        public static Texture2D Get()
        {
            if (_shared == null)
            {
                if (_worker == null && _pixels == null) Prepare();
                Finish(true);
            }
            return _shared;
        }

        /// <summary>The texture exists (the lens can use it this frame without a pause).</summary>
        public static bool Ready => _shared != null;

        /// <summary>Start drawing the dirt on a worker thread (once; nothing to do when the texture exists). Only plain arithmetic runs on it: no engine objects.</summary>
        public static void Prepare()
        {
            if (_shared != null || _worker != null || _pixels != null) return;
            var worker = new Thread(() =>
            {
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var px = Draw(Seed);
                    _drawMs = sw.Elapsed.TotalMilliseconds;
                    _pixels = px;
                }
                catch (Exception e) { _error = e.Message; }               // the main thread draws it itself (and says so) rather than lose the lens dust
            })
            { IsBackground = true, Name = "MN lens dust", Priority = System.Threading.ThreadPriority.BelowNormal };
            _worker = worker;
            worker.Start();
        }

        /// <summary>Once a frame: when the worker has finished, make the texture from its pixels (never waits).</summary>
        public static void Poll()
        {
            if (_shared == null && _worker != null && !_worker.IsAlive) Finish(false);
        }

        static void Finish(bool wait)
        {
            if (_worker != null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                if (wait) _worker.Join(); else if (_worker.IsAlive) return;
                _worker = null;
                if (wait && sw.Elapsed.TotalMilliseconds > 5.0) MnLog.Info($"LOOK the lens dust texture was waited for: {sw.Elapsed.TotalMilliseconds:0} ms (the drawing took {_drawMs:0} ms on its thread)");
            }
            var swTex = System.Diagnostics.Stopwatch.StartNew();
            if (_pixels == null) { _pixels = Draw(Seed); MnLog.Warn("LOOK the lens dust texture had to be drawn on the main thread" + (_error != null ? " (the worker failed: " + _error + ")" : "")); }
            _shared = Upload(_pixels);
            _pixels = null;
            MnLog.Info($"LOOK the lens dust texture is ready at frame {Time.frameCount}: drawn in {_drawMs:0} ms off the main thread, made in {swTex.Elapsed.TotalMilliseconds:0.0} ms");
        }

        /// <summary>The finished texture for a seed, drawn here and now (the tools; the game goes through <see cref="Get"/>).</summary>
        public static Texture2D Create(int seed) => Upload(Draw(seed));

        static Texture2D Upload(Color32[] pixels)
        {
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true)
            {
                name = "MN_LensDust",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>The pixels. Plain arithmetic on arrays (no engine objects), so it can run on any thread (the loading self-test checks that a thread draws the same dirt).</summary>
        public static Color32[] Draw(int seed)
        {
            var rng = new System.Random(seed);
            float Rand() => (float)rng.NextDouble();
            float Range(float a, float b) => a + (b - a) * Rand();

            var r = new float[Width * Height];
            var g = new float[Width * Height];
            var b = new float[Width * Height];

            // Fine dust: many tiny soft specks, a few larger.
            for (int i = 0; i < 1400; i++)
                Disc(r, Rand() * Width, Rand() * Height, Mathf.Exp(Range(Mathf.Log(0.6f), Mathf.Log(3.4f))), Mathf.Lerp(0.25f, 1f, Mathf.Pow(Rand(), 2f)), 1.4f);

            // Fibres: short curved hairs.
            for (int i = 0; i < 26; i++)
            {
                float x = Rand() * Width, y = Rand() * Height, a = Rand() * Mathf.PI * 2f, curve = Range(-0.012f, 0.012f);
                float len = Range(40f, 170f), amp = Range(0.25f, 0.6f);
                for (float t = 0; t < len; t += 0.8f)
                {
                    Disc(r, x, y, 1.1f, amp, 1.0f);
                    a += curve + Range(-0.012f, 0.012f);
                    x += Mathf.Cos(a) * 0.8f; y += Mathf.Sin(a) * 0.8f;
                }
            }
            // Scratches: a few long, straight, faint lines.
            for (int i = 0; i < 7; i++)
            {
                float x = Rand() * Width, y = Rand() * Height, a = Rand() * Mathf.PI, len = Range(120f, 420f), amp = Range(0.06f, 0.18f);
                for (float t = 0; t < len; t += 0.8f)
                {
                    Disc(r, x, y, 1.2f, amp, 1.0f);
                    x += Mathf.Cos(a) * 0.8f; y += Mathf.Sin(a) * 0.8f;
                }
            }

            // Smudges: big soft blobs shaped by a slow noise, so they look streaked rather than round.
            for (int i = 0; i < 55; i++)
                Disc(g, Rand() * Width, Rand() * Height, Range(45f, 230f), Range(0.25f, 0.85f), 2.0f);
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    float n = 0.5f * Mathf.PerlinNoise(x * 0.006f + seed, y * 0.009f) + 0.3f * Mathf.PerlinNoise(x * 0.021f + 31f, y * 0.03f + seed) + 0.2f * Mathf.PerlinNoise(x * 0.07f, y * 0.1f + 7f);
                    int k = y * Width + x;
                    g[k] = Mathf.Clamp01(g[k]) * Mathf.SmoothStep(0.25f, 0.8f, n);
                }

            // Bokeh: soft round blobs with no edge to them, lumpy inside (out-of-focus dust is never a clean disc or ring).
            for (int i = 0; i < 80; i++)
            {
                float cx = Rand() * Width, cy = Rand() * Height, rad = Range(8f, 38f), amp = Range(0.2f, 0.7f);
                float sx = Range(0.8f, 1.25f), nOff = Rand() * 100f;
                int x0 = Mathf.Max(0, (int)(cx - rad * 1.3f - 2)), x1 = Mathf.Min(Width - 1, (int)(cx + rad * 1.3f + 2));
                int y0 = Mathf.Max(0, (int)(cy - rad * 1.3f - 2)), y1 = Mathf.Min(Height - 1, (int)(cy + rad * 1.3f + 2));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = (x - cx) * sx, dy = (y - cy) / sx;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float edge = 1f - Mathf.SmoothStep(rad * 0.45f, rad * 1.2f, d);
                        float body = 0.8f + 0.2f * Mathf.SmoothStep(rad * 0.4f, rad * 1.0f, d);
                        float lump = 0.65f + 0.7f * Mathf.PerlinNoise(x * 0.045f + nOff, y * 0.045f + nOff * 0.7f);
                        int k = y * Width + x;
                        b[k] = Mathf.Max(b[k], amp * edge * body * lump);
                    }
            }

            var pixels = new Color32[Width * Height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32((byte)(Mathf.Clamp01(r[i]) * 255f + 0.5f), (byte)(Mathf.Clamp01(g[i]) * 255f + 0.5f), (byte)(Mathf.Clamp01(b[i]) * 255f + 0.5f), 255);

            return pixels;
        }

        /// <summary>Adds a soft disc: value amp at the centre, falling to 0 at radius with the given power.</summary>
        static void Disc(float[] buf, float cx, float cy, float radius, float amp, float power)
        {
            int x0 = Mathf.Max(0, (int)(cx - radius - 1)), x1 = Mathf.Min(Width - 1, (int)(cx + radius + 1));
            int y0 = Mathf.Max(0, (int)(cy - radius - 1)), y1 = Mathf.Min(Height - 1, (int)(cy + radius + 1));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / radius;
                    if (d >= 1f) continue;
                    buf[y * Width + x] += amp * Mathf.Pow(1f - d, power);
                }
        }
    }
}
