using UnityEngine;

namespace Monolith.Hud
{
    public sealed partial class HudLines
    {
        // A table for sine, so a gauge of a hundred rows is not a hundred calls into the maths library.
        const int SinN = 2048;
        static readonly float[] SinTab = MakeSinTab();
        static float[] MakeSinTab() { var t = new float[SinN + 2]; for (int i = 0; i < t.Length; i++) t[i] = Mathf.Sin(i * (Mathf.PI * 2f / SinN)); return t; }

        static float FastSin(float a)
        {
            float x = a * (SinN / (Mathf.PI * 2f));
            int i = (int)Mathf.Floor(x);
            float f = x - i;
            i &= SinN - 1;
            return SinTab[i] + (SinTab[i + 1] - SinTab[i]) * f;
        }

        static void SinCos(float a, out float sin, out float cos)
        {
            sin = FastSin(a);
            cos = FastSin(a + Mathf.PI * 0.5f);
        }

        // The style's colours as this frame's palette: the ink, the same dimmer (secondary lines and type), fainter (an empty gauge track), and the one warning accent.
        public Color Ink => Style.Ink;
        public Color Dim => Style.InkA(Style.Dim);
        public Color Faint => Style.InkA(Style.Faint);
        public Color Warn => Style.Warn;
        /// <summary>The dark of plates and shadows (near black, a touch cool) at an alpha.</summary>
        public static Color Shade(float a) => new Color(0.0025f, 0.0045f, 0.007f, a);
        /// <summary>A line width in pixels at this frame's scale, never under a pixel and a half.</summary>
        public float Lw(float k = 1f) => Mathf.Max(1.5f, 2f * Scale * k);

        /// <summary>A filled ellipse (a translucent plate): one quad, the edge a little harder up and down than across.</summary>
        public void FillEllipse(Vector2 centre, float a, float b, Color col)
        {
            if (col.a <= 0.003f || a < 1f || b < 1f) return;
            float Pad = PadOf(col);
            float ex = a + Pad, ey = b + Pad, k = a / b;
            float w = -(2f + a);
            Grow(4, 6, 0);
            Color32 c = col;
            int i0 = V(centre + new Vector2(-ex, -ey), -ex, -ey * k, 0f, w, c);
            int i1 = V(centre + new Vector2(ex, -ey), ex, -ey * k, 0f, w, c);
            int i2 = V(centre + new Vector2(-ex, ey), -ex, ey * k, 0f, w, c);
            int i3 = V(centre + new Vector2(ex, ey), ex, ey * k, 0f, w, c);
            ShapeQuad(i0, i1, i2, i3);
        }

        /// <summary>
        /// A band of an ellipse (semi-axes <paramref name="a"/> across and <paramref name="b"/> up) between two angles: the radar's tilted rings. Angle 0 is the right, increasing clockwise
        /// on screen. <paramref name="closed"/> draws the whole ring with no ends.
        /// </summary>
        public void EllipseBand(Vector2 centre, float a, float b, float thick, float a0, float a1, Color col, bool closed = false)
        {
            if (col.a <= 0.003f) return;
            float Pad = PadOf(col);
            thick = Mathf.Max(1f, thick);
            float ht = thick * 0.5f, e = ht + Pad;
            float span = closed ? Mathf.PI * 2f : Mathf.Abs(a1 - a0);
            float dir = a1 >= a0 ? 1f : -1f;
            int segs = Mathf.Clamp(Mathf.CeilToInt(span / 0.16f), 6, 120);
            int rows = closed ? segs + 1 : segs + 3;
            Grow(rows * 2, (rows - 1) * 6, 0);
            Color32 c = col;
            int first = _nv;
            // arc length of the ellipse is only needed for the ends, so use the mean radius
            float mean = (a + b) * 0.5f, len = span * mean;
            for (int k = 0; k < rows; k++)
            {
                float t, s;
                if (closed) { t = a0 + dir * span * k / segs; s = 1e5f + len * k / segs; }
                else
                {
                    if (k == 0) s = -Pad; else if (k == rows - 1) s = len + Pad; else s = len * (k - 1) / segs;
                    t = a0 + dir * s / mean;
                }
                SinCos(t, out float sn, out float cs);
                Vector2 p = centre + new Vector2(cs * a, sn * b);
                Vector2 n = new Vector2(cs * b, sn * a).normalized;          // the outward normal of an ellipse
                V(p - n * e, -e, ht, s, closed ? 1e6f : len, c);
                V(p + n * e, e, ht, s, closed ? 1e6f : len, c);
            }
            for (int k = 0; k < rows - 1; k++)
            {
                int i = first + k * 2;
                ShapeQuad(i, i + 1, i + 2, i + 3);
            }
        }

        /// <summary>A whole circle as a band with no ends (a ring that needs a thickness varied by nothing: use <see cref="Ring"/> for most things).</summary>
        public void Polyline(Vector2[] pts, int count, float width, Color col, bool closed = false)
        {
            for (int i = 1; i < count; i++) Line(pts[i - 1], pts[i], width, col);
            if (closed && count > 2) Line(pts[count - 1], pts[0], width, col);
        }

        /// <summary>A chevron (an open arrowhead) at <paramref name="tip"/> pointing along <paramref name="dir"/>: the arms sweep back <paramref name="size"/> pixels.</summary>
        public void Chevron(Vector2 tip, Vector2 dir, float size, float lw, Color col)
        {
            dir.Normalize();
            Vector2 n = new Vector2(-dir.y, dir.x);
            Line(tip, tip - dir * size + n * size * 0.8f, lw, col);
            Line(tip, tip - dir * size - n * size * 0.8f, lw, col);
        }

        /// <summary>A solid arrowhead.</summary>
        public void Arrow(Vector2 tip, Vector2 dir, float size, Color col)
        {
            dir.Normalize();
            Vector2 n = new Vector2(-dir.y, dir.x);
            FillTri(tip, tip - dir * size + n * size * 0.62f, tip - dir * size - n * size * 0.62f, col);
        }

        /// <summary>
        /// One alien glyph, kept to ornament: a few strokes on a small grid, the same every time for the same <paramref name="seed"/>, in the manner of the Independence Day
        /// invaders' writing (nested angles, a dot, a bar) and never in place of a number or a word. <paramref name="size"/> is its height in pixels.
        /// </summary>
        public void Glyph(Vector2 topLeft, float size, int seed, float lw, Color col)
        {
            uint h = (uint)seed * 2654435761u + 0x9E3779B9u;
            uint Next() { h ^= h << 13; h ^= h >> 17; h ^= h << 5; return h; }
            float u = size / 4f;
            Vector2 P(int gx, int gy) => topLeft + new Vector2(gx * u * 0.75f, gy * u);
            int strokes = 2 + (int)(Next() % 3);
            int px = (int)(Next() % 3), py = (int)(Next() % 5);
            for (int i = 0; i < strokes; i++)
            {
                int qx = (int)(Next() % 3), qy = (int)(Next() % 5);
                if (qx == px && qy == py) qy = (qy + 2) % 5;
                Line(P(px, py), P(qx, qy), lw, col);
                // every other stroke lifts the pen
                if ((Next() & 1) == 0) { px = qx; py = qy; } else { px = (int)(Next() % 3); py = (int)(Next() % 5); }
            }
            if ((Next() & 3) == 0) Disc(P(2, 0) + new Vector2(u * 0.4f, 0f), Mathf.Max(1f, lw * 0.9f), col);
        }

        /// <summary>A run of <paramref name="count"/> glyphs with a gap between them, starting at <paramref name="topLeft"/> (the width drawn is returned).</summary>
        public float GlyphRun(Vector2 topLeft, int count, float size, int seed, float lw, Color col)
        {
            float step = size * 1.0f;
            for (int i = 0; i < count; i++) Glyph(topLeft + new Vector2(i * step, 0f), size, seed * 31 + i * 7, lw, col);
            return count * step;
        }
    }
}
