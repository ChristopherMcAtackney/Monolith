using System.Collections.Generic;
using UnityEngine;

namespace Monolith.View
{
    /// <summary>
    /// Placeholder starfield: soft points at infinity (pass 0 of Hidden/Monolith/Lines), many faint, a few bright, a band
    /// of extra faint ones standing in for the galactic plane. To be replaced by the Sky system's star catalogue.
    /// </summary>
    public static class Starfield
    {
        public static Mesh Build(ViewSettings.StarSettings s)
        {
            var rng = new System.Random(s.seed);
            int n = Mathf.Max(0, s.count);
            var pos = new List<Vector3>(n * 4);
            var uv = new List<Vector4>(n * 4);
            var col = new List<Color>(n * 4);
            var idx = new List<int>(n * 6);
            // A tilted "galactic plane" that a third of the stars crowd towards.
            var bandNormal = Quaternion.Euler(62f, 20f, 0f) * Vector3.up;
            for (int i = 0; i < n; i++)
            {
                Vector3 d = RandomDirection(rng);
                if (i % 3 == 0)
                {
                    float h = Vector3.Dot(d, bandNormal);
                    d = (d - bandNormal * h * 0.85f).normalized;
                }
                float u = (float)rng.NextDouble();
                float bright = 0.035f + 1.1f * Mathf.Pow(u, 14f);        // most stars faint, a handful bright
                float size = 0.9f + 1.6f * Mathf.Pow(u, 6f);              // pixels (radius)
                float t = (float)rng.NextDouble();
                var tint = t < 0.15f ? new Color(1f, 0.82f, 0.62f) : t < 0.35f ? new Color(0.78f, 0.86f, 1f) : new Color(1f, 0.97f, 0.92f);
                var c = tint * bright;
                int v = pos.Count;
                for (int k = 0; k < 4; k++)
                {
                    float cx = (k & 1) == 0 ? -1f : 1f, cy = (k & 2) == 0 ? -1f : 1f;
                    pos.Add(d);
                    uv.Add(new Vector4(cx * size, cy * size, cx, cy));
                    col.Add(c);
                }
                idx.Add(v); idx.Add(v + 1); idx.Add(v + 2); idx.Add(v + 2); idx.Add(v + 1); idx.Add(v + 3);
            }
            var mesh = new Mesh { name = "MN_Starfield", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(pos);
            mesh.SetUVs(0, uv);
            mesh.SetColors(col);
            mesh.SetTriangles(idx, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            return mesh;
        }

        static Vector3 RandomDirection(System.Random rng)
        {
            float z = (float)rng.NextDouble() * 2f - 1f;
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            float r = Mathf.Sqrt(1f - z * z);
            return new Vector3(r * Mathf.Cos(a), z, r * Mathf.Sin(a));
        }
    }
}
