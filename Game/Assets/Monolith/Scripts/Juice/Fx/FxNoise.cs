using UnityEngine;

namespace Monolith.Juice
{
    /// <summary>
    /// The 64 cubed tiling noise the fireball and beam shaders read, built in code at start-up (about 60 ms): channel r is Perlin-Worley (the
    /// billowy base of a cloud or a blast), g is Worley (cells that make the cauliflower edges), b is a finer Worley, a is Perlin. Every channel
    /// tiles in all three axes, so it can be scrolled and scaled freely.
    /// </summary>
    public static class FxNoise
    {
        public const int Size = 64;
        static Texture3D _tex;

        public static Texture3D Get()
        {
            if (_tex != null) return _tex;
            _tex = Build(Size);
            return _tex;
        }

        public static Texture3D Build(int n)
        {
            var px = new Color32[n * n * n];
            var rng = new System.Random(4242);
            // Periodic Worley feature points: one per cell of a 4, 8 and 16 cell lattice.
            var w4 = Points(rng, 4); var w8 = Points(rng, 8); var w16 = Points(rng, 12);
            int[] perm = new int[512];
            var order = new int[256];
            for (int i = 0; i < 256; i++) order[i] = i;
            for (int i = 255; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
            for (int i = 0; i < 512; i++) perm[i] = order[i & 255];

            for (int z = 0; z < n; z++)
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float fx = x / (float)n, fy = y / (float)n, fz = z / (float)n;
                float p0 = Perlin(perm, fx * 4f, fy * 4f, fz * 4f, 4) * 0.55f + Perlin(perm, fx * 8f, fy * 8f, fz * 8f, 8) * 0.30f + Perlin(perm, fx * 16f, fy * 16f, fz * 16f, 16) * 0.15f;
                p0 = p0 * 0.5f + 0.5f;
                float wa = 1f - Worley(w4, 4, fx, fy, fz);
                float wb = 1f - Worley(w8, 8, fx, fy, fz);
                float wc = 1f - Worley(w16, 12, fx, fy, fz);
                // Perlin-Worley: Worley billows eaten into by Perlin.
                float pw = Mathf.Clamp01(Remap(p0, 0f, 1f, wa * 0.9f, 1f));
                float g = Mathf.Clamp01(wa * 0.55f + wb * 0.30f + wc * 0.15f);
                float b = Mathf.Clamp01(wb * 0.55f + wc * 0.45f);
                px[(z * n + y) * n + x] = new Color32(B(pw), B(g), B(b), B(p0));
            }
            var tex = new Texture3D(n, n, n, TextureFormat.RGBA32, false) { name = "MN_Noise3D", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            tex.SetPixelData(px, 0);
            tex.Apply(false, true);
            return tex;
        }

        static byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
        static float Remap(float v, float a, float b, float c, float d) => c + (v - a) / (b - a) * (d - c);

        static Vector3[] Points(System.Random rng, int cells)
        {
            var p = new Vector3[cells * cells * cells];
            for (int i = 0; i < p.Length; i++) p[i] = new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());
            return p;
        }

        /// <summary>Distance to the nearest feature point (periodic over the unit cube), 0 to about 1.</summary>
        static float Worley(Vector3[] pts, int cells, float x, float y, float z)
        {
            float cx = x * cells, cy = y * cells, cz = z * cells;
            int ix = Mathf.FloorToInt(cx), iy = Mathf.FloorToInt(cy), iz = Mathf.FloorToInt(cz);
            float best = 4f;
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int jx = ix + dx, jy = iy + dy, jz = iz + dz;
                int wx = ((jx % cells) + cells) % cells, wy = ((jy % cells) + cells) % cells, wz = ((jz % cells) + cells) % cells;
                var p = pts[(wz * cells + wy) * cells + wx];
                float ddx = jx + p.x - cx, ddy = jy + p.y - cy, ddz = jz + p.z - cz;
                float d = ddx * ddx + ddy * ddy + ddz * ddz;
                if (d < best) best = d;
            }
            return Mathf.Clamp01(Mathf.Sqrt(best));
        }

        // Gradient noise, periodic with the given lattice period.
        static float Perlin(int[] perm, float x, float y, float z, int period)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y), zi = Mathf.FloorToInt(z);
            float xf = x - xi, yf = y - yi, zf = z - zi;
            float u = Fade(xf), v = Fade(yf), w = Fade(zf);
            int x0 = Wrap(xi, period), x1 = Wrap(xi + 1, period), y0 = Wrap(yi, period), y1 = Wrap(yi + 1, period), z0 = Wrap(zi, period), z1 = Wrap(zi + 1, period);
            float n000 = Grad(perm[perm[perm[x0] + y0] + z0], xf, yf, zf);
            float n100 = Grad(perm[perm[perm[x1] + y0] + z0], xf - 1, yf, zf);
            float n010 = Grad(perm[perm[perm[x0] + y1] + z0], xf, yf - 1, zf);
            float n110 = Grad(perm[perm[perm[x1] + y1] + z0], xf - 1, yf - 1, zf);
            float n001 = Grad(perm[perm[perm[x0] + y0] + z1], xf, yf, zf - 1);
            float n101 = Grad(perm[perm[perm[x1] + y0] + z1], xf - 1, yf, zf - 1);
            float n011 = Grad(perm[perm[perm[x0] + y1] + z1], xf, yf - 1, zf - 1);
            float n111 = Grad(perm[perm[perm[x1] + y1] + z1], xf - 1, yf - 1, zf - 1);
            return Mathf.Lerp(Mathf.Lerp(Mathf.Lerp(n000, n100, u), Mathf.Lerp(n010, n110, u), v), Mathf.Lerp(Mathf.Lerp(n001, n101, u), Mathf.Lerp(n011, n111, u), v), w) * 1.15f;
        }

        static int Wrap(int i, int period) => ((i % period) + period) % period;
        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        static float Grad(int h, float x, float y, float z)
        {
            switch (h & 15)
            {
                case 0: return x + y; case 1: return -x + y; case 2: return x - y; case 3: return -x - y;
                case 4: return x + z; case 5: return -x + z; case 6: return x - z; case 7: return -x - z;
                case 8: return y + z; case 9: return -y + z; case 10: return y - z; case 11: return -y - z;
                case 12: return x + y; case 13: return -y + z; case 14: return -x + y; default: return -y - z;
            }
        }
    }
}
