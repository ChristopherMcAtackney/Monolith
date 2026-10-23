using UnityEngine;

namespace Monolith.Look
{
    /// <summary>
    /// The dirt on the lens or canopy, drawn once at start-up from a fixed seed (so it is the same every run): 2048 x 1024, three
    /// channels the glare lights up differently. R is fine dust, fibres and scratches; G is greasy smudges; B is out-of-focus dust discs
    /// (bokeh) with a brighter rim. It sits still on the screen, as dirt on glass does.
    /// </summary>
    public static class DustTexture
    {
        public const int Width = 2048, Height = 1024;
        static Texture2D _shared;

        public static Texture2D Get()
        {
            if (_shared == null) _shared = Create(7);
            return _shared;
        }

        public static Texture2D Create(int seed)
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
                float x = Rand() * Width, y = Rand() * Height, a = Rand() * Mathf.PI * 2f, curve = Range(-0.05f, 0.05f);
                float len = Range(40f, 170f), amp = Range(0.35f, 0.9f);
                for (float t = 0; t < len; t += 0.8f)
                {
                    Disc(r, x, y, 0.85f, amp, 1.2f);
                    a += curve + Range(-0.03f, 0.03f);
                    x += Mathf.Cos(a) * 0.8f; y += Mathf.Sin(a) * 0.8f;
                }
            }
            // Scratches: a few long, straight, faint lines.
            for (int i = 0; i < 7; i++)
            {
                float x = Rand() * Width, y = Rand() * Height, a = Rand() * Mathf.PI, len = Range(120f, 420f), amp = Range(0.15f, 0.4f);
                for (float t = 0; t < len; t += 0.8f)
                {
                    Disc(r, x, y, 0.7f, amp, 1.2f);
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

            // Bokeh: round, a little brighter at the rim.
            for (int i = 0; i < 80; i++)
            {
                float cx = Rand() * Width, cy = Rand() * Height, rad = Range(7f, 32f), amp = Range(0.25f, 0.8f);
                int x0 = Mathf.Max(0, (int)(cx - rad - 2)), x1 = Mathf.Min(Width - 1, (int)(cx + rad + 2));
                int y0 = Mathf.Max(0, (int)(cy - rad - 2)), y1 = Mathf.Min(Height - 1, (int)(cy + rad + 2));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        float edge = 1f - Mathf.SmoothStep(rad * 0.88f, rad * 1.02f, d);
                        float body = 0.35f + 0.65f * Mathf.SmoothStep(rad * 0.5f, rad * 0.95f, d);
                        int k = y * Width + x;
                        b[k] = Mathf.Max(b[k], amp * edge * body);
                    }
            }

            var pixels = new Color32[Width * Height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32((byte)(Mathf.Clamp01(r[i]) * 255f + 0.5f), (byte)(Mathf.Clamp01(g[i]) * 255f + 0.5f), (byte)(Mathf.Clamp01(b[i]) * 255f + 0.5f), 255);

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
