using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Monolith.Hud
{
    public enum TextAlign { Left, Center, Right }

    /// <summary>The four sizes of type the HUD uses (cap heights set by the <see cref="HudStyle"/>): small tracked labels, running text, readings, and the big number or banner.</summary>
    public enum TextRole { Label, Body, Value, Display }

    /// <summary>
    /// The HUD's canvas: line graphics and type built as quads in pixel space (y down from the top-left), one mesh per frame with two parts, the shapes (pass 3 of
    /// <c>Hidden/Monolith/Lines</c>) and the type (pass 4, from a signed-distance font atlas). Each shape carries its own distance field, so every edge is one pixel wide at any size
    /// and the glow and the dark keyline come from the same distance. Single-colour, as the player's tech is. Cabin displays draw with the same canvas into render textures.
    /// Everything is sized in screen pixels; callers multiply by their own scale (<see cref="Scale"/> is that scale, so the glow and the type follow it).
    /// </summary>
    public sealed partial class HudLines
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Vtx { public Vector2 P; public Color32 C; public Vector4 Uv; }          // Unity lays a stream out Position, Color, TexCoord0: this is that order

        static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
        };

        Vtx[] _v = new Vtx[8192];
        int _nv;
        int[] _si = new int[12288], _ti = new int[12288];
        int _ns, _nt, _ni;
        int[] _ii = new int[96];
        readonly SubMeshDescriptor[] _subs = new SubMeshDescriptor[3];

        public readonly Mesh Mesh;
        /// <summary>The style everything is drawn in (set by <see cref="Clear"/>) and the screen scale of this frame.</summary>
        public HudStyle Style { get; private set; }
        public float Scale { get; private set; } = 1f;
        /// <summary>Pixels around a shape that its quad reaches past the shape, for the glow and the keyline.</summary>
        public float Pad { get; private set; } = 8f;
        public int VertexCount => _nv;
        public int GlyphCount { get; private set; }
        /// <summary>Every piece of text drawn this frame, with the pixel size it was drawn at, for the checks (only while <see cref="RecordTexts"/> is on).</summary>
        public readonly List<TextRecord> Texts = new List<TextRecord>(256);
        public bool RecordTexts;
        /// <summary>Leave the text drawn from now on out of the record (type anchored to something moving in the world, which goes wherever that goes).</summary>
        public bool NoRecord;

        public struct TextRecord { public string Text; public float Cap, Em; public Rect Bounds; public TextRole Role; }

        static readonly int GlowId = Shader.PropertyToID("_MN_Glow");
        static readonly int KeyId = Shader.PropertyToID("_MN_Key");
        static readonly int FontPId = Shader.PropertyToID("_MN_FontP");
        static readonly int FontTexId = Shader.PropertyToID("_MN_FontTex");
        static readonly int ImageTexId = Shader.PropertyToID("_MN_ImageTex");

        /// <summary>The picture <see cref="Image"/> draws (one per canvas).</summary>
        public Texture ImageTexture;

        public HudLines()
        {
            Mesh = new Mesh { name = "MN_Hud", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
            Mesh.MarkDynamic();
            Style = HudStyles.Current;
        }

        /// <summary>Start a frame: forget everything, and set the style and the screen scale (1 at 1080p) that sizes the glow, the keyline and the type.</summary>
        public void Clear(float scale = 1f, HudStyle style = null)
        {
            _nv = 0; _ns = 0; _nt = 0; _ni = 0; GlyphCount = 0;
            Texts.Clear();
            Style = style ?? HudStyles.Current;
            Scale = scale;
            Pad = 2.1f * GlowPx + KeyPx + 1.5f;
        }

        /// <summary>A share of the style's glow: a cabin display is small and seen from a way off, so it takes less.</summary>
        public float GlowScale = 1f;
        /// <summary>How far a quad reaches past its shape: the full pad for a lit colour that glows, only the keyline's for a faint or a dark one (a plate, a track), which have no glow worth the pixels.</summary>
        float PadOf(Color c) => c.a < 0.45f || Mathf.Max(c.r, Mathf.Max(c.g, c.b)) < 0.05f ? KeyPx + 1.5f : Pad;

        float GlowPx => Mathf.Max(1.2f, Style.GlowRadius * Scale * GlowScale);
        float KeyPx => Mathf.Max(0.8f, Style.KeyPx * Scale);

        /// <summary>The area (pixels squared) the shapes and the type cover as drawn this frame, counting overlap: what the pixel shader works through (for the cost checks).</summary>
        public void Areas(out float shapes, out float text)
        {
            float Sum(int[] idx, int n)
            {
                float a = 0f;
                for (int i = 0; i + 2 < n; i += 3)
                {
                    Vector2 p0 = _v[idx[i]].P, p1 = _v[idx[i + 1]].P, p2 = _v[idx[i + 2]].P;
                    a += Mathf.Abs((p1.x - p0.x) * (p2.y - p0.y) - (p2.x - p0.x) * (p1.y - p0.y)) * 0.5f;
                }
                return a;
            }
            shapes = Sum(_si, _ns); text = Sum(_ti, _nt);
        }

        /// <summary>The biggest quads of the shapes (for the cost checks): each as its bounding box and area.</summary>
        public List<string> Biggest(int n)
        {
            var list = new List<(float a, string d)>();
            for (int i = 0; i + 5 < _ns; i += 6)
            {
                Vector2 p0 = _v[_si[i]].P, p1 = _v[_si[i + 1]].P, p2 = _v[_si[i + 2]].P, p3 = _v[_si[i + 5]].P;
                float a = Mathf.Abs((p1.x - p0.x) * (p2.y - p0.y) - (p2.x - p0.x) * (p1.y - p0.y)) * 0.5f * 2f;
                list.Add((a, $"{a:0} px2 at ({Mathf.Min(p0.x, p3.x):0},{Mathf.Min(p0.y, p3.y):0}) uv {_v[_si[i]].Uv}"));
            }
            list.Sort((x, y) => y.a.CompareTo(x.a));
            var r = new List<string>();
            for (int i = 0; i < n && i < list.Count; i++) r.Add(list[i].d);
            return r;
        }

        public void Commit()
        {
            if (_nv == 0)
            {
                Mesh.Clear(true);
                return;
            }
            const MeshUpdateFlags f = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds;
            Mesh.SetVertexBufferParams(_nv, Layout);
            Mesh.SetVertexBufferData(_v, 0, 0, _nv, 0, f);
            Mesh.SetIndexBufferParams(_ns + _nt + _ni, IndexFormat.UInt32);
            Mesh.SetIndexBufferData(_si, 0, 0, _ns, f);
            Mesh.SetIndexBufferData(_ti, 0, _ns, _nt, f);
            if (_ni > 0) Mesh.SetIndexBufferData(_ii, 0, _ns + _nt, _ni, f);
            _subs[0] = new SubMeshDescriptor(0, _ns);
            _subs[1] = new SubMeshDescriptor(_ns, _nt);
            _subs[2] = new SubMeshDescriptor(_ns + _nt, _ni);
            Mesh.subMeshCount = 3;
            Mesh.SetSubMeshes(_subs, 0, 3, f);
            Mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
        }

        // ------------------------------------------------------------------------------------------------ drawing the mesh

        /// <summary>Set the glow, keyline and font uniforms this frame's style needs (on a property block, for a command buffer).</summary>
        public void SetParams(MaterialPropertyBlock p)
        {
            var font = Style.Font;
            p.SetVector(GlowId, new Vector4(GlowPx, Mathf.Pow(Style.GlowStrength, 2.2f), Pad, 1.4427f / GlowPx));
            p.SetVector(KeyId, new Vector4(KeyPx, Style.KeyAlpha, Style.Bold * Mathf.Max(Scale, 0.72f), 0f));
            p.SetVector(FontPId, new Vector4(font.AtlasW, font.AtlasH, font.Spread, 0f));
            p.SetTexture(FontTexId, font.Texture);
        }

        /// <summary>Queue both parts of the mesh (shapes, then type) on a command buffer; the caller has set the matrix and screen vectors on <paramref name="p"/>.</summary>
        public void Draw(CommandBuffer cmd, Material mat, MaterialPropertyBlock p)
        {
            if (_nv == 0) return;
            SetParams(p);
            if (_ni > 0) { if (ImageTexture != null) p.SetTexture(ImageTexId, ImageTexture); cmd.DrawMesh(Mesh, Matrix4x4.identity, mat, 2, 5, p); }      // a picture is the backdrop: first
            if (_ns > 0) cmd.DrawMesh(Mesh, Matrix4x4.identity, mat, 0, 3, p);
            if (_nt > 0) cmd.DrawMesh(Mesh, Matrix4x4.identity, mat, 1, 4, p);
        }

        /// <summary>Draw both parts now into the active render target (a cabin display); the caller has set the matrix and screen vectors on <paramref name="mat"/>.</summary>
        public void DrawNow(Material mat)
        {
            if (_nv == 0) return;
            var font = Style.Font;
            mat.SetVector(GlowId, new Vector4(GlowPx, Mathf.Pow(Style.GlowStrength, 2.2f), Pad, 1.4427f / GlowPx));
            mat.SetVector(KeyId, new Vector4(KeyPx, Style.KeyAlpha, Style.Bold * Mathf.Max(Scale, 0.72f), 0f));
            mat.SetVector(FontPId, new Vector4(font.AtlasW, font.AtlasH, font.Spread, 0f));
            mat.SetTexture(FontTexId, font.Texture);
            if (_ni > 0) { if (ImageTexture != null) mat.SetTexture(ImageTexId, ImageTexture); mat.SetPass(5); Graphics.DrawMeshNow(Mesh, Matrix4x4.identity, 2); }
            if (_ns > 0) { mat.SetPass(3); Graphics.DrawMeshNow(Mesh, Matrix4x4.identity, 0); }
            if (_nt > 0) { mat.SetPass(4); Graphics.DrawMeshNow(Mesh, Matrix4x4.identity, 1); }
        }

        // ------------------------------------------------------------------------------------------------ buffers

        void Grow(int addV, int addS, int addT)
        {
            if (_nv + addV > _v.Length) System.Array.Resize(ref _v, Mathf.Max(_v.Length * 2, _nv + addV));
            if (_ns + addS > _si.Length) System.Array.Resize(ref _si, Mathf.Max(_si.Length * 2, _ns + addS));
            if (_nt + addT > _ti.Length) System.Array.Resize(ref _ti, Mathf.Max(_ti.Length * 2, _nt + addT));
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        int V(Vector2 p, float u0, float u1, float u2, float u3, Color32 c)
        {
            ref var v = ref _v[_nv];
            v.P.x = p.x; v.P.y = p.y;
            v.C = c;
            v.Uv.x = u0; v.Uv.y = u1; v.Uv.z = u2; v.Uv.w = u3;
            return _nv++;
        }

        void ShapeQuad(int a, int b, int c, int d)
        {
            _si[_ns++] = a; _si[_ns++] = b; _si[_ns++] = c; _si[_ns++] = c; _si[_ns++] = b; _si[_ns++] = d;
        }

        /// <summary>
        /// A picture over the pixel rectangle <paramref name="min"/>..<paramref name="max"/>, showing the part <paramref name="uv"/> of <see cref="ImageTexture"/> (texture space: v up). Where the picture's own edge is inside the
        /// screen it can fade out over <paramref name="featherU"/> and <paramref name="featherV"/> of its width and height. <paramref name="tint"/> multiplies it and its alpha is how much shows. Drawn under everything else.
        /// </summary>
        public void Image(Vector2 min, Vector2 max, Rect uv, float featherU, float featherV, Color tint)
        {
            if (tint.a <= 0.003f || ImageTexture == null) return;
            if (_nv + 4 > _v.Length) System.Array.Resize(ref _v, _v.Length * 2);
            if (_ni + 6 > _ii.Length) System.Array.Resize(ref _ii, _ii.Length * 2);
            Color32 c = tint;
            int a = V(new Vector2(min.x, min.y), uv.xMin, uv.yMax, featherU, featherV, c);
            int b = V(new Vector2(max.x, min.y), uv.xMax, uv.yMax, featherU, featherV, c);
            int d = V(new Vector2(min.x, max.y), uv.xMin, uv.yMin, featherU, featherV, c);
            int e = V(new Vector2(max.x, max.y), uv.xMax, uv.yMin, featherU, featherV, c);
            _ii[_ni++] = a; _ii[_ni++] = b; _ii[_ni++] = d; _ii[_ni++] = d; _ii[_ni++] = b; _ii[_ni++] = e;
        }

        /// <summary>A plain colour over a pixel rectangle, in the picture's layer (under every shape and word, over nothing): the backdrop of a full screen picture.</summary>
        public void Backdrop(Vector2 min, Vector2 max, Color color)
        {
            if (color.a <= 0.003f) return;
            if (_nv + 4 > _v.Length) System.Array.Resize(ref _v, _v.Length * 2);
            if (_ni + 6 > _ii.Length) System.Array.Resize(ref _ii, _ii.Length * 2);
            Color32 c = color;
            int a = V(new Vector2(min.x, min.y), 0f, 0f, -1f, -1f, c);
            int b = V(new Vector2(max.x, min.y), 0f, 0f, -1f, -1f, c);
            int d = V(new Vector2(min.x, max.y), 0f, 0f, -1f, -1f, c);
            int e = V(new Vector2(max.x, max.y), 0f, 0f, -1f, -1f, c);
            _ii[_ni++] = a; _ii[_ni++] = b; _ii[_ni++] = d; _ii[_ni++] = d; _ii[_ni++] = b; _ii[_ni++] = e;
        }

        // ------------------------------------------------------------------------------------------------ shapes

        /// <summary>A line <paramref name="width"/> pixels wide with square ends (they reach half a width past each point, so joints close).</summary>
        public void Line(Vector2 a, Vector2 b, float width, Color col)
        {
            if (col.a <= 0.003f) return;
            float Pad = PadOf(col);
            width = Mathf.Max(1f, width);
            Vector2 d = b - a;
            float len = d.magnitude;
            Vector2 dir = len > 1e-4f ? d / len : Vector2.right;
            Vector2 n = new Vector2(-dir.y, dir.x);
            float hw = width * 0.5f, e = hw + Pad, L = len + width;
            Vector2 a0 = a - dir * (hw + Pad), b0 = b + dir * (hw + Pad);
            Grow(4, 6, 0);
            Color32 c = col;
            int i0 = V(a0 - n * e, -e, hw, -Pad, L, c);
            int i1 = V(a0 + n * e, e, hw, -Pad, L, c);
            int i2 = V(b0 - n * e, -e, hw, L + Pad, L, c);
            int i3 = V(b0 + n * e, e, hw, L + Pad, L, c);
            ShapeQuad(i0, i1, i2, i3);
        }

        /// <summary>A filled axis-aligned box.</summary>
        public void Fill(Vector2 min, Vector2 max, Color col)
        {
            if (col.a <= 0.003f) return;
            float Pad = PadOf(col);
            float w = max.x - min.x, h = max.y - min.y;
            if (w < 0.25f || h < 0.25f) return;
            Grow(4, 6, 0);
            Color32 c = col;
            float hw = w * 0.5f, p = Pad;
            int i0 = V(new Vector2(min.x - p, min.y - p), -hw - p, hw, -p, h, c);
            int i1 = V(new Vector2(max.x + p, min.y - p), hw + p, hw, -p, h, c);
            int i2 = V(new Vector2(min.x - p, max.y + p), -hw - p, hw, h + p, h, c);
            int i3 = V(new Vector2(max.x + p, max.y + p), hw + p, hw, h + p, h, c);
            ShapeQuad(i0, i1, i2, i3);
        }

        /// <summary>A filled box turned by <paramref name="angle"/> (radians): a diamond is a square at 45 degrees.</summary>
        public void Box(Vector2 centre, Vector2 half, float angle, Color col)
        {
            if (col.a <= 0.003f) return;
            float Pad = PadOf(col);
            float cs = Mathf.Cos(angle), sn = Mathf.Sin(angle);
            Vector2 ax = new Vector2(cs, sn), ay = new Vector2(-sn, cs);
            float p = Pad;
            Grow(4, 6, 0);
            Color32 c = col;
            float h2 = half.y * 2f;
            int i0 = V(centre - ax * (half.x + p) - ay * (half.y + p), -half.x - p, half.x, -p, h2, c);
            int i1 = V(centre + ax * (half.x + p) - ay * (half.y + p), half.x + p, half.x, -p, h2, c);
            int i2 = V(centre - ax * (half.x + p) + ay * (half.y + p), -half.x - p, half.x, h2 + p, h2, c);
            int i3 = V(centre + ax * (half.x + p) + ay * (half.y + p), half.x + p, half.x, h2 + p, h2, c);
            ShapeQuad(i0, i1, i2, i3);
        }

        public void Rect(Vector2 min, Vector2 max, float width, Color col)
        {
            Line(new Vector2(min.x, min.y), new Vector2(max.x, min.y), width, col);
            Line(new Vector2(max.x, min.y), new Vector2(max.x, max.y), width, col);
            Line(new Vector2(max.x, max.y), new Vector2(min.x, max.y), width, col);
            Line(new Vector2(min.x, max.y), new Vector2(min.x, min.y), width, col);
        }

        // Signed distance (pixels, positive inside) to the line through p -> q for a triangle wound with positive area (y down).
        static float EdgeDist(Vector2 p, Vector2 q, Vector2 x)
        {
            Vector2 e = q - p;
            float l = e.magnitude;
            return l < 1e-5f ? 0f : (e.x * (x.y - p.y) - e.y * (x.x - p.x)) / l;
        }

        /// <summary>A filled triangle with one-pixel edges.</summary>
        public void FillTri(Vector2 a, Vector2 b, Vector2 c, Color col)
        {
            if (col.a <= 0.003f) return;
            float Pad = PadOf(col);
            float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (Mathf.Abs(area) < 0.01f) return;
            if (area < 0f) { var t = b; b = c; c = t; }
            // Push each vertex out along the sum of its two edges' outward normals so the quad reaches Pad past every side.
            Vector2 Grown(Vector2 p, Vector2 prev, Vector2 next)
            {
                Vector2 e1 = (p - prev).normalized, e2 = (next - p).normalized;
                Vector2 n1 = new Vector2(e1.y, -e1.x), n2 = new Vector2(e2.y, -e2.x);
                float k = 1f + Vector2.Dot(n1, n2);
                return p + (n1 + n2) * (Pad / Mathf.Max(0.15f, k));
            }
            Vector2 ga = Grown(a, c, b), gb = Grown(b, a, c), gc = Grown(c, b, a);
            Grow(3, 3, 0);
            Color32 cc = col;
            int i0 = V(ga, EdgeDist(a, b, ga), EdgeDist(b, c, ga), EdgeDist(c, a, ga), -1f, cc);
            int i1 = V(gb, EdgeDist(a, b, gb), EdgeDist(b, c, gb), EdgeDist(c, a, gb), -1f, cc);
            int i2 = V(gc, EdgeDist(a, b, gc), EdgeDist(b, c, gc), EdgeDist(c, a, gc), -1f, cc);
            _si[_ns++] = i0; _si[_ns++] = i1; _si[_ns++] = i2;
        }

        /// <summary>A ring <paramref name="width"/> pixels thick about <paramref name="radius"/>, as one quad (radius 0: a disc of radius width/2).</summary>
        public void Ring(Vector2 centre, float radius, float width, Color col)
        {
            if (col.a <= 0.003f) return;
            float Pad = PadOf(col);
            width = Mathf.Max(1f, width);
            float ht = width * 0.5f, e = radius + ht + Pad;
            float w = -(2f + ht);
            Grow(4, 6, 0);
            Color32 c = col;
            int i0 = V(centre + new Vector2(-e, -e), -e, -e, radius, w, c);
            int i1 = V(centre + new Vector2(e, -e), e, -e, radius, w, c);
            int i2 = V(centre + new Vector2(-e, e), -e, e, radius, w, c);
            int i3 = V(centre + new Vector2(e, e), e, e, radius, w, c);
            ShapeQuad(i0, i1, i2, i3);
        }

        public void Disc(Vector2 centre, float radius, Color col) => Ring(centre, 0f, radius * 2f, col);

        /// <summary>
        /// A band of a circle from angle <paramref name="a0"/> to <paramref name="a1"/> (radians, 0 = right, increasing clockwise on screen), <paramref name="thick"/> pixels thick
        /// about the radius <paramref name="r"/>; the ends are cut square and anti-aliased. One triangle strip, so a long arc has no seams.
        /// </summary>
        public void Band(Vector2 centre, float r, float thick, float a0, float a1, Color col, int segments = 0)
        {
            if (col.a <= 0.003f) return;
            float Pad = PadOf(col);
            float span = Mathf.Abs(a1 - a0);
            if (span < 1e-4f) return;
            thick = Mathf.Max(1f, thick);
            float dir = a1 >= a0 ? 1f : -1f;
            float ht = thick * 0.5f, e = ht + Pad;
            float len = span * r;
            if (segments <= 0) segments = Mathf.Clamp(Mathf.CeilToInt(span / Mathf.Min(0.10f, 30f / Mathf.Max(r, 1f))), 1, 160);
            // rows: one before the start (the glow), segments + 1 along the arc, one after the end
            int rows = segments + 3;
            Grow(rows * 2, (rows - 1) * 6, 0);
            Color32 c = col;
            int first = _nv;
            for (int k = 0; k < rows; k++)
            {
                float s;      // pixels along the arc
                if (k == 0) s = -Pad; else if (k == rows - 1) s = len + Pad; else s = len * (k - 1) / segments;
                float ang = a0 + dir * s / r;
                SinCos(ang, out float sa, out float ca);
                Vector2 rad = new Vector2(ca, sa);
                V(centre + rad * (r - e), -e, ht, s, len, c);
                V(centre + rad * (r + e), e, ht, s, len, c);
            }
            for (int k = 0; k < rows - 1; k++)
            {
                int i = first + k * 2;
                ShapeQuad(i, i + 1, i + 2, i + 3);
            }
        }

        /// <summary>The legacy thin arc (the cabin's displays): a band of <paramref name="width"/> pixels.</summary>
        public void Arc(Vector2 centre, float radius, float a0, float a1, float width, Color col, int segments = 12) =>
            Band(centre, radius, width, a0, a1, col);

        /// <summary>A gauge of <paramref name="n"/> cells along an arc: the track in <paramref name="off"/>, the cells up to <paramref name="fill01"/> in <paramref name="on"/> (the last one part-filled).</summary>
        public void Cells(Vector2 centre, float r, float thick, float a0, float a1, int n, float gapPx, float fill01, Color on, Color off, float dimAfter = 2f)
        {
            if (n < 1) return;
            float step = (a1 - a0) / n;
            float gapAng = gapPx / r * Mathf.Sign(step);
            fill01 = Mathf.Clamp01(fill01);
            for (int i = 0; i < n; i++)
            {
                float s0 = a0 + step * i + gapAng * 0.5f, s1 = a0 + step * (i + 1) - gapAng * 0.5f;
                float f = Mathf.Clamp01(fill01 * n - i);
                var track = (i + 0.5f) / n > dimAfter ? new Color(off.r, off.g, off.b, off.a * 0.45f) : off;     // past the mark: the afterburner's range, the dimmest
                if (f < 0.999f) Band(centre, r, thick, f <= 0.001f ? s0 : Mathf.Lerp(s0, s1, f), s1, track, 1);
                if (f > 0.001f) Band(centre, r, thick, s0, Mathf.Lerp(s0, s1, f), on, 1);
            }
        }

        public void Diamond(Vector2 c, float r, float lw, Color col)
        {
            Vector2 t = c + new Vector2(0f, -r), rt = c + new Vector2(r, 0f), b = c + new Vector2(0f, r), l = c + new Vector2(-r, 0f);
            Line(t, rt, lw, col); Line(rt, b, lw, col); Line(b, l, lw, col); Line(l, t, lw, col);
        }

        /// <summary>Four corner brackets round a point (a target box), each arm <paramref name="k"/> long.</summary>
        public void Brackets(Vector2 c, float half, float k, float lw, Color col)
        {
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? -1f : 1f, sy = (i & 2) == 0 ? -1f : 1f;
                var corner = c + new Vector2(sx * half, sy * half);
                Line(corner, corner - new Vector2(sx * k, 0f), lw, col);
                Line(corner, corner - new Vector2(0f, sy * k), lw, col);
            }
        }

        // ------------------------------------------------------------------------------------------------ type

        /// <summary>Cap height in pixels of a role at this frame's scale (the type never scales below 0.72 of 1080p, so a small window stays readable).</summary>
        public float Cap(TextRole role)
        {
            float s = Mathf.Max(Scale, 0.72f) * HudStyles.TextScale;
            switch (role)
            {
                case TextRole.Label: return Style.CapLabel * s;
                case TextRole.Body: return Style.CapBody * s;
                case TextRole.Value: return Style.CapValue * s;
                default: return Style.CapDisplay * s;
            }
        }

        public float Tracking(TextRole role)
        {
            switch (role)
            {
                case TextRole.Label: return Style.TrackLabel;
                case TextRole.Body: return Style.TrackBody;
                case TextRole.Value: return Style.TrackValue;
                default: return Style.TrackDisplay;
            }
        }

        /// <summary>The width of a string at a cap height (pixels) and tracking (ems).</summary>
        public float Measure(string s, float cap, float tracking)
        {
            var f = Style.Font;
            return f.Measure(s, cap / f.CapRatio, tracking);
        }

        public float Measure(string s, TextRole role) => Measure(s, Cap(role), Tracking(role));

        /// <summary>
        /// Type: <paramref name="pos"/> is the top of the capitals (the left, centre or right of the text by <paramref name="align"/>); <paramref name="cap"/> is the cap height in pixels
        /// and <paramref name="tracking"/> the extra space after each letter in ems. Returns the width drawn.
        /// </summary>
        public float Text(string s, Vector2 pos, float cap, Color col, TextAlign align, float tracking, TextRole role = TextRole.Body)
        {
            if (string.IsNullOrEmpty(s) || col.a <= 0.003f) return 0f;
            var f = Style.Font;
            float em = cap / f.CapRatio, k = em / f.Size;
            float w = f.Measure(s, em, tracking);
            float x = align == TextAlign.Center ? pos.x - w * 0.5f : align == TextAlign.Right ? pos.x - w : pos.x;
            float baseline = Mathf.Round(pos.y + cap);
            Grow(s.Length * 4, 0, s.Length * 6);
            Color32 c = col;
            float aw = f.AtlasW, ah = f.AtlasH;
            float pen = x;
            for (int i = 0; i < s.Length; i++)
            {
                var g = f.Get(s[i]);
                if (g.W > 0)
                {
                    float x0 = pen + g.Bx * k, y0 = baseline - g.By * k, x1 = x0 + g.W * k, y1 = y0 + g.H * k;
                    float u0 = g.X / aw, u1 = (g.X + g.W) / aw, vt = 1f - g.Y / ah, vb = 1f - (g.Y + g.H) / ah;
                    int a = V(new Vector2(x0, y0), u0, vt, 0f, 0f, c);
                    int b = V(new Vector2(x1, y0), u1, vt, 0f, 0f, c);
                    int cI = V(new Vector2(x0, y1), u0, vb, 0f, 0f, c);
                    int d = V(new Vector2(x1, y1), u1, vb, 0f, 0f, c);
                    _ti[_nt++] = a; _ti[_nt++] = b; _ti[_nt++] = cI; _ti[_nt++] = cI; _ti[_nt++] = b; _ti[_nt++] = d;
                    GlyphCount++;
                }
                pen += g.Adv * k + (i < s.Length - 1 ? tracking * em : 0f);
            }
            if (RecordTexts && !NoRecord) Texts.Add(new TextRecord { Text = s, Cap = cap, Em = em, Role = role, Bounds = new Rect(x, baseline - cap, w, cap) });
            return w;
        }

        /// <summary>Type in one of the four roles.</summary>
        public float Type(string s, Vector2 pos, TextRole role, Color col, TextAlign align = TextAlign.Left, float trackingScale = 1f) =>
            Text(s, pos, Cap(role), col, align, Tracking(role) * trackingScale, role);

        /// <summary>The legacy text call (the cabin's displays): <paramref name="size"/> is the cap height in pixels; the stroke width is ignored (the type is a font now).</summary>
        public float Text(string s, Vector2 topLeft, float size, float width, Color col, TextAlign align = TextAlign.Left)
        {
            float track = size >= Style.CapDisplay * 0.6f ? Style.TrackDisplay : size >= Style.CapValue * 0.9f ? Style.TrackValue : size >= Style.CapBody * 0.95f ? Style.TrackBody : Style.TrackLabel;
            return Text(s, topLeft, size, col, align, track, TextRole.Body);
        }

        /// <summary>The string cut with an ellipsis to fit <paramref name="maxWidth"/> pixels.</summary>
        public string Ellipsize(string s, float cap, float tracking, float maxWidth)
        {
            if (string.IsNullOrEmpty(s) || Measure(s, cap, tracking) <= maxWidth) return s;
            int n = s.Length;
            while (n > 1 && Measure(s.Substring(0, n) + "...", cap, tracking) > maxWidth) n--;
            return s.Substring(0, n).TrimEnd() + "...";
        }

        /// <summary>Break a string into lines no wider than <paramref name="maxWidth"/> pixels at a cap height and tracking; words are never split.</summary>
        public List<string> Wrap(string text, float cap, float tracking, float maxWidth, List<string> into = null)
        {
            // the same text wraps the same way every frame: keep the answer (the lists are shared, so callers only read them)
            if (into == null && !string.IsNullOrEmpty(text))
            {
                var key = new WrapKey { Text = text, Cap = Mathf.RoundToInt(cap * 8f), Width = Mathf.RoundToInt(maxWidth), Track = Mathf.RoundToInt(tracking * 1000f), Font = Style.FontId };
                if (WrapCache.TryGetValue(key, out var hit)) return hit;
                if (WrapCache.Count > 400) WrapCache.Clear();
                var made = WrapNow(text, cap, tracking, maxWidth, new List<string>());
                WrapCache[key] = made;
                return made;
            }
            return WrapNow(text, cap, tracking, maxWidth, into ?? new List<string>());
        }

        struct WrapKey : System.IEquatable<WrapKey>
        {
            public string Text, Font; public int Cap, Width, Track;
            public bool Equals(WrapKey o) => Cap == o.Cap && Width == o.Width && Track == o.Track && Text == o.Text && Font == o.Font;
            public override bool Equals(object o) => o is WrapKey k && Equals(k);
            public override int GetHashCode() => Text.GetHashCode() * 31 + Cap * 7 + Width * 13 + Track;
        }
        static readonly Dictionary<WrapKey, List<string>> WrapCache = new Dictionary<WrapKey, List<string>>();

        List<string> WrapNow(string text, float cap, float tracking, float maxWidth, List<string> into)
        {
            if (string.IsNullOrEmpty(text)) return into;
            foreach (var para in text.Split('\n'))
            {
                var words = para.Split(' ');
                string cur = "";
                foreach (var word in words)
                {
                    string trial = cur.Length == 0 ? word : cur + " " + word;
                    if (cur.Length > 0 && Measure(trial, cap, tracking) > maxWidth) { into.Add(cur); cur = word; }
                    else cur = trial;
                }
                into.Add(cur);
            }
            return into;
        }
    }
}
