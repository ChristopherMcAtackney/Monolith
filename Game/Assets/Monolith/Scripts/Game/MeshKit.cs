using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Monolith.Game
{
    /// <summary>
    /// Builds flat-shaded placeholder meshes out of boxes and tapered boxes, with UVs in world metres divided by
    /// <see cref="UvMetres"/> so one panel texture tiles at the same scale on every face (that is what makes a big hull
    /// read as big). Placeholder geometry for the flight playtest; real ships come from Blender.
    /// </summary>
    public sealed class MeshKit
    {
        public float UvMetres = 32f;
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Vector3> _n = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<int> _t = new List<int>();

        public int VertexCount => _v.Count;

        /// <summary>An axis-aligned box (optionally rotated about its centre).</summary>
        public void Box(Vector3 centre, Vector3 size, Quaternion? rotation = null)
        {
            var h = size * 0.5f;
            Tapered(centre, rotation ?? Quaternion.identity, -h.z, h.z, new Vector2(h.x, h.y), new Vector2(h.x, h.y), Vector2.zero);
        }

        /// <summary>
        /// A box whose cross-section changes from <paramref name="back"/> (half width, half height at z0) to
        /// <paramref name="front"/> (at z1), the front face shifted by <paramref name="frontShift"/> (x, y). Local z runs back to front.
        /// </summary>
        public void Tapered(Vector3 centre, Quaternion rot, float z0, float z1, Vector2 back, Vector2 front, Vector2 frontShift)
        {
            var c = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                bool f = (i & 4) != 0;
                var hw = f ? front : back;
                var sh = f ? frontShift : Vector2.zero;
                float x = ((i & 1) == 0 ? -hw.x : hw.x) + sh.x;
                float y = ((i & 2) == 0 ? -hw.y : hw.y) + sh.y;
                c[i] = centre + rot * new Vector3(x, y, f ? z1 : z0);
            }
            Quad(c[0], c[2], c[3], c[1]);   // back (-z)
            Quad(c[4], c[5], c[7], c[6]);   // front (+z)
            Quad(c[0], c[4], c[6], c[2]);   // left (-x)
            Quad(c[1], c[3], c[7], c[5]);   // right (+x)
            Quad(c[2], c[6], c[7], c[3]);   // top (+y)
            Quad(c[0], c[1], c[5], c[4]);   // bottom (-y)
        }

        /// <summary>An n-sided prism along local z (engine bells, pipes).</summary>
        public void Prism(Vector3 centre, Quaternion rot, float radius, float length, int sides, bool caps = true)
        {
            var back = new Vector3[sides];
            var front = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                var p = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
                back[i] = centre + rot * (p + new Vector3(0f, 0f, -length * 0.5f));
                front[i] = centre + rot * (p + new Vector3(0f, 0f, length * 0.5f));
            }
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                Quad(back[i], back[j], front[j], front[i]);
            }
            if (!caps) return;
            var bc = centre + rot * new Vector3(0f, 0f, -length * 0.5f);
            var fc = centre + rot * new Vector3(0f, 0f, length * 0.5f);
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                Tri(bc, back[j], back[i]);
                Tri(fc, front[i], front[j]);
            }
        }

        /// <summary>
        /// A prism with the cross-section <paramref name="poly"/> (points in counter-clockwise order, x right, y up) from
        /// local z0 to z1; the front cross-section is scaled by <paramref name="frontScale"/> (a blunt taper, never a wedge).
        /// </summary>
        public void Extrude(Vector2[] poly, float z0, float z1, Vector3 centre, Quaternion rot, float frontScale = 1f, bool caps = true)
        {
            int n = poly.Length;
            var back = new Vector3[n];
            var front = new Vector3[n];
            Vector2 mid = Vector2.zero;
            for (int i = 0; i < n; i++)
            {
                back[i] = centre + rot * new Vector3(poly[i].x, poly[i].y, z0);
                front[i] = centre + rot * new Vector3(poly[i].x * frontScale, poly[i].y * frontScale, z1);
                mid += poly[i] / n;
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Quad(back[i], back[j], front[j], front[i]);
            }
            if (!caps) return;
            var bc = centre + rot * new Vector3(mid.x, mid.y, z0);
            var fc = centre + rot * new Vector3(mid.x * frontScale, mid.y * frontScale, z1);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Tri(bc, back[j], back[i]);
                Tri(fc, front[i], front[j]);
            }
        }

        /// <summary>A rectangle of half size (hx, hy) with its corners cut by <paramref name="chamfer"/>, counter-clockwise.</summary>
        public static Vector2[] Chamfered(float hx, float hy, float chamfer)
        {
            float c = Mathf.Min(chamfer, Mathf.Min(hx, hy) * 0.9f);
            return new[]
            {
                new Vector2(hx, -hy + c), new Vector2(hx, hy - c), new Vector2(hx - c, hy), new Vector2(-hx + c, hy),
                new Vector2(-hx, hy - c), new Vector2(-hx, -hy + c), new Vector2(-hx + c, -hy), new Vector2(hx - c, -hy),
            };
        }

        /// <summary>
        /// An open tube along local z from <paramref name="zMouth"/> (the open end) to <paramref name="zBase"/>: outer wall,
        /// inner wall and the rim at the mouth. With <paramref name="glow"/>, the inner wall and a disc deep inside go in that
        /// kit instead, with UVs that run a radial gradient texture from hot (deep) to dark (at the mouth).
        /// </summary>
        public void Tube(Vector3 centre, Quaternion rot, float rOuter, float rInner, float zMouth, float zBase, int sides, MeshKit glow)
        {
            var om = new Vector3[sides]; var ob = new Vector3[sides]; var im = new Vector3[sides]; var ib = new Vector3[sides];
            var dir = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                dir[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                om[i] = centre + rot * new Vector3(dir[i].x * rOuter, dir[i].y * rOuter, zMouth);
                ob[i] = centre + rot * new Vector3(dir[i].x * rOuter, dir[i].y * rOuter, zBase);
                im[i] = centre + rot * new Vector3(dir[i].x * rInner, dir[i].y * rInner, zMouth);
                ib[i] = centre + rot * new Vector3(dir[i].x * rInner, dir[i].y * rInner, zBase);
            }
            float zDisc = Mathf.Lerp(zBase, zMouth, 0.12f);
            var disc = new Vector3[sides];
            for (int i = 0; i < sides; i++) disc[i] = centre + rot * new Vector3(dir[i].x * rInner, dir[i].y * rInner, zDisc);
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                Quad(om[i], om[j], ob[j], ob[i]);                 // outer wall, facing out
                Quad(om[i], im[i], im[j], om[j]);                 // rim at the mouth, facing back
                var inner = glow ?? this;
                // Inner wall from the disc to the mouth, facing in: gradient from warm-hot (deep) to dark (mouth).
                inner.QuadUV(im[j], im[i], disc[i], disc[j],
                    new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.62f, 0.5f), new Vector2(0.62f, 0.5f));
                if (glow != null)
                {
                    var dc = centre + rot * new Vector3(0f, 0f, zDisc);
                    glow.TriUV(dc, disc[j], disc[i], new Vector2(0.5f, 0.5f),
                        new Vector2(0.5f + 0.5f * dir[j].x, 0.5f + 0.5f * dir[j].y), new Vector2(0.5f + 0.5f * dir[i].x, 0.5f + 0.5f * dir[i].y));
                }
            }
        }

        /// <summary>A quad with explicit UVs (glow gradients).</summary>
        public void QuadUV(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            var n = Vector3.Cross(b - a, d - a).normalized;
            int i0 = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            _n.Add(n); _n.Add(n); _n.Add(n); _n.Add(n);
            _uv.Add(ua); _uv.Add(ub); _uv.Add(uc); _uv.Add(ud);
            _t.Add(i0); _t.Add(i0 + 1); _t.Add(i0 + 2);
            _t.Add(i0); _t.Add(i0 + 2); _t.Add(i0 + 3);
        }

        public void TriUV(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            int i0 = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c);
            _n.Add(n); _n.Add(n); _n.Add(n);
            _uv.Add(ua); _uv.Add(ub); _uv.Add(uc);
            _t.Add(i0); _t.Add(i0 + 1); _t.Add(i0 + 2);
        }

        /// <summary>A single flat quad (windows, glow discs); vertices clockwise seen from the front.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var n = Vector3.Cross(b - a, d - a).normalized;
            if (n.sqrMagnitude < 0.5f) n = Vector3.Cross(c - b, a - b).normalized;
            // UVs: project on the two axes the face is most nearly aligned with, in world metres.
            var an = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
            int i0 = _v.Count;
            foreach (var p in new[] { a, b, c, d })
            {
                Vector2 uv = an.x >= an.y && an.x >= an.z ? new Vector2(p.z, p.y) : an.y >= an.z ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y);
                _v.Add(p); _n.Add(n); _uv.Add(uv / UvMetres);
            }
            _t.Add(i0); _t.Add(i0 + 1); _t.Add(i0 + 2);
            _t.Add(i0); _t.Add(i0 + 2); _t.Add(i0 + 3);
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            int i0 = _v.Count;
            foreach (var p in new[] { a, b, c })
            {
                _v.Add(p); _n.Add(n); _uv.Add(new Vector2(p.x, p.z) / UvMetres);
            }
            _t.Add(i0); _t.Add(i0 + 1); _t.Add(i0 + 2);
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name, indexFormat = _v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(_v);
            m.SetNormals(_n);
            m.SetUVs(0, _uv);
            m.SetTriangles(_t, 0, true);
            m.RecalculateTangents();
            return m;
        }
    }
}
