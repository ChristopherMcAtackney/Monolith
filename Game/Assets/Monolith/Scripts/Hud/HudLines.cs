using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Monolith.Hud
{
    /// <summary>
    /// Builds the HUD as soft-edged line quads in pixel space (y down from the top-left), one mesh per frame, drawn by
    /// pass 2 of Hidden/Monolith/Lines. Single-colour line graphics and a stroke font: the player's tech is rough and spare.
    /// </summary>
    public sealed class HudLines
    {
        readonly List<Vector3> _pos = new List<Vector3>(4096);
        readonly List<Vector4> _uv = new List<Vector4>(4096);
        readonly List<Color> _col = new List<Color>(4096);
        readonly List<int> _idx = new List<int>(6144);
        public readonly Mesh Mesh;

        public HudLines()
        {
            Mesh = new Mesh { name = "MN_Hud", indexFormat = IndexFormat.UInt32 };
            Mesh.MarkDynamic();
        }

        public void Clear()
        {
            _pos.Clear(); _uv.Clear(); _col.Clear(); _idx.Clear();
        }

        public void Commit()
        {
            Mesh.Clear(true);
            Mesh.SetVertices(_pos);
            Mesh.SetUVs(0, _uv);
            Mesh.SetColors(_col);
            Mesh.SetTriangles(_idx, 0, false);
            Mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
        }

        /// <summary>A line <paramref name="width"/> pixels wide with square ends.</summary>
        public void Line(Vector2 a, Vector2 b, float width, Color c)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            Vector2 dir = len > 1e-4f ? d / len : Vector2.right;
            Vector2 n = new Vector2(-dir.y, dir.x);
            float e = width * 0.5f + 1f;                 // half extent including the soft edge
            Vector2 ext = dir * (width * 0.5f);
            Vector2 a0 = a - ext, b0 = b + ext;
            int v = _pos.Count;
            _pos.Add(a0 - n * e); _uv.Add(new Vector4(-1f, 0, 0, 0)); _col.Add(c);
            _pos.Add(a0 + n * e); _uv.Add(new Vector4(1f, 0, 0, 0)); _col.Add(c);
            _pos.Add(b0 - n * e); _uv.Add(new Vector4(-1f, 0, 0, 0)); _col.Add(c);
            _pos.Add(b0 + n * e); _uv.Add(new Vector4(1f, 0, 0, 0)); _col.Add(c);
            _idx.Add(v); _idx.Add(v + 1); _idx.Add(v + 2); _idx.Add(v + 2); _idx.Add(v + 1); _idx.Add(v + 3);
        }

        /// <summary>A filled axis-aligned box (drawn as one fat line).</summary>
        public void Fill(Vector2 min, Vector2 max, Color c)
        {
            float w = max.x - min.x, h = max.y - min.y;
            if (w < 0.5f || h < 0.5f) return;
            if (w >= h)
            {
                float my = 0.5f * (min.y + max.y);
                Line(new Vector2(min.x + h * 0.5f, my), new Vector2(max.x - h * 0.5f, my), h, c);
            }
            else
            {
                float mx = 0.5f * (min.x + max.x);
                Line(new Vector2(mx, min.y + w * 0.5f), new Vector2(mx, max.y - w * 0.5f), w, c);
            }
        }

        public void Rect(Vector2 min, Vector2 max, float width, Color c)
        {
            Line(new Vector2(min.x, min.y), new Vector2(max.x, min.y), width, c);
            Line(new Vector2(max.x, min.y), new Vector2(max.x, max.y), width, c);
            Line(new Vector2(max.x, max.y), new Vector2(min.x, max.y), width, c);
            Line(new Vector2(min.x, max.y), new Vector2(min.x, min.y), width, c);
        }

        /// <summary>An arc from angle a0 to a1 (radians, 0 = right, clockwise on screen since y is down).</summary>
        public void Arc(Vector2 centre, float radius, float a0, float a1, float width, Color c, int segments = 12)
        {
            Vector2 prev = centre + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius;
            for (int i = 1; i <= segments; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)segments);
                Vector2 p = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                Line(prev, p, width, c);
                prev = p;
            }
        }

        /// <summary>Text in the stroke font; <paramref name="size"/> is the cap height in pixels. Returns the width drawn.</summary>
        public float Text(string s, Vector2 topLeft, float size, float width, Color c, TextAlign align = TextAlign.Left)
        {
            float unit = size / StrokeFont.Height;
            float w = StrokeFont.Measure(s) * unit;
            if (align == TextAlign.Center) topLeft.x -= w * 0.5f;
            else if (align == TextAlign.Right) topLeft.x -= w;
            float x = topLeft.x;
            foreach (char ch in s.ToUpperInvariant())
            {
                var strokes = StrokeFont.Glyph(ch);
                if (strokes != null)
                    foreach (var stroke in strokes)
                        for (int i = 1; i < stroke.Length; i++)
                            Line(new Vector2(x + stroke[i - 1].x * unit, topLeft.y + stroke[i - 1].y * unit),
                                 new Vector2(x + stroke[i].x * unit, topLeft.y + stroke[i].y * unit), width, c);
                x += StrokeFont.Advance * unit;
            }
            return w;
        }
    }

    public enum TextAlign { Left, Center, Right }

    /// <summary>
    /// A tiny vector font on a 4 x 8 grid (x right, y down), drawn as polylines: digits, capitals and a little
    /// punctuation. Each glyph is strokes of "xy" digit pairs, e.g. "00 40 48" = (0,0) to (4,0) to (4,8).
    /// </summary>
    public static class StrokeFont
    {
        public const float Height = 8f;
        public const float Advance = 6f;

        static readonly Dictionary<char, Vector2[][]> Glyphs = new Dictionary<char, Vector2[][]>();

        static StrokeFont()
        {
            Def('0', "00 40 48 08 00", "08 40");
            Def('1', "11 20 28", "08 48");
            Def('2', "00 40 44 04 08 48");
            Def('3', "00 40 48 08", "14 44");
            Def('4', "00 04 44", "40 48");
            Def('5', "40 00 04 44 48 08");
            Def('6', "40 00 08 48 44 04");
            Def('7', "00 40 48");
            Def('8', "00 40 48 08 00", "04 44");
            Def('9', "44 04 00 40 48 08");
            Def('A', "08 00 40 48", "04 44");
            Def('B', "00 30 41 43 34 04", "34 45 47 38 08 00");
            Def('C', "40 00 08 48");
            Def('D', "00 30 42 46 38 08 00");
            Def('E', "40 00 08 48", "04 34");
            Def('F', "40 00 08", "04 34");
            Def('G', "40 00 08 48 44 24");
            Def('H', "00 08", "40 48", "04 44");
            Def('I', "00 40", "20 28", "08 48");
            Def('J', "40 48 08 06");
            Def('K', "00 08", "40 04 48");
            Def('L', "00 08 48");
            Def('M', "08 00 24 40 48");
            Def('N', "08 00 48 40");
            Def('O', "00 40 48 08 00");
            Def('P', "08 00 40 44 04");
            Def('Q', "00 40 48 08 00", "26 48");
            Def('R', "08 00 40 44 04", "24 48");
            Def('S', "40 00 04 44 48 08");
            Def('T', "00 40", "20 28");
            Def('U', "00 08 48 40");
            Def('V', "00 28 40");
            Def('W', "00 08 24 48 40");
            Def('X', "00 48", "40 08");
            Def('Y', "00 24 40", "24 28");
            Def('Z', "00 40 08 48");
            Def('.', "27 28");
            Def(',', "27 18");
            Def(':', "22 23", "25 26");
            Def('-', "04 44");
            Def('+', "04 44", "22 26");
            Def('/', "08 40");
            Def('>', "00 44 08");
            Def('<', "40 04 48");
            Def('(', "30 12 16 38");
            Def(')', "10 32 36 18");
            Def('=', "13 33", "15 35");
            Def('%', "08 40", "00 11", "37 48");
            Def('\'', "20 22");
        }

        static void Def(char c, params string[] strokes)
        {
            var list = new Vector2[strokes.Length][];
            for (int s = 0; s < strokes.Length; s++)
            {
                var pts = strokes[s].Split(' ');
                list[s] = new Vector2[pts.Length];
                for (int i = 0; i < pts.Length; i++) list[s][i] = new Vector2(pts[i][0] - '0', pts[i][1] - '0');
            }
            Glyphs[c] = list;
        }

        public static Vector2[][] Glyph(char c) => Glyphs.TryGetValue(c, out var g) ? g : null;

        public static float Measure(string s) => string.IsNullOrEmpty(s) ? 0f : s.Length * Advance - (Advance - 4f);
    }
}
