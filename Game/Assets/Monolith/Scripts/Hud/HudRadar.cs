using System.Collections.Generic;
using UnityEngine;

namespace Monolith.Hud
{
    /// <summary>
    /// The radar, after FreeSpace 2's: a disc seen at a tilt, you at the centre facing up the screen. Every contact stands on the plane at its range and bearing; a stem rises or
    /// drops from there by how far above or below your plane it is, so a blip with a stem up is over you and one with a stem down is under you. A hollow diamond is a friend, a
    /// solid one an enemy, a boxed cross a capital ship, a triangle a missile (in the warning colour when it is coming for you). When the sensors are damaged the picture breaks
    /// up in static and, at last, goes. Single-colour, drawn with the style's ink.
    /// </summary>
    public static class HudRadar
    {
        public const float Squash = 0.46f;

        /// <summary>Where a contact lands on the radar (the tip of its stem) and where its stem stands on the plane, given the screen centre, the disc's half-width and the range.</summary>
        public static void Plot(Contact c, Vector2 centre, float radius, float range, out Vector2 ground, out Vector2 tip, out bool clamped)
        {
            float b = radius * Squash;
            Vector2 flat = new Vector2(c.Rel.x, c.Rel.z);
            float fd = flat.magnitude;
            float k = radius / range;
            Vector2 g = new Vector2(c.Rel.x * k, -c.Rel.z * k * Squash / 1f);
            // clamp the ground position inside the ellipse (an enemy beyond range stays on the rim, in its direction)
            float ex = g.x / radius, ey = g.y / b;
            float e = Mathf.Sqrt(ex * ex + ey * ey);
            clamped = e > 0.985f;
            if (clamped && e > 1e-4f) { float f = 0.985f / e; g *= f; }
            // the stem: height above the plane, scaled like the plan and capped
            float alt = Mathf.Clamp(c.Rel.y * k * 0.8f, -radius * 0.32f, radius * 0.32f);
            if (clamped) alt *= 0.55f;
            ground = centre + g;
            tip = ground + new Vector2(0f, -alt);
            _ = fd;
        }

        /// <param name="contacts">Everything to plot (the player's frame).</param>
        /// <param name="staticAmount">0 clear .. 1 the sensors are gone.</param>
        /// <param name="alpha">Overall fade.</param>
        public static void Draw(HudLines L, List<Contact> contacts, Vector2 centre, float radius, float range, float staticAmount, float time, float alpha = 1f)
        {
            float s = L.Scale, lw = L.Lw(0.8f);
            float b = radius * Squash;
            var ink = L.Ink; ink.a *= alpha;
            var dim = L.Dim; dim.a *= alpha;
            var faint = L.Faint; faint.a *= alpha;
            var warn = L.Warn; warn.a *= alpha;

            // the plate, the rings, the cross and the nose mark
            L.FillEllipse(centre, radius, b, HudLines.Shade(0.50f * alpha));
            L.EllipseBand(centre, radius, b, lw * 1.25f, 0f, 0f, new Color(ink.r, ink.g, ink.b, ink.a * 0.62f), true);
            L.EllipseBand(centre, radius * 0.5f, b * 0.5f, lw * 0.7f, 0f, 0f, faint, true);
            L.EllipseBand(centre, radius * 1.075f, b * 1.075f, lw * 0.6f, Mathf.PI * 1.12f, Mathf.PI * 1.88f, faint);    // an outer arc over the nose: you face this way
            for (int i = 0; i < 4; i++)
            {
                float ang = Mathf.PI * 0.25f + i * Mathf.PI * 0.5f;
                L.Line(centre + new Vector2(Mathf.Cos(ang) * radius * 0.16f, Mathf.Sin(ang) * b * 0.16f), centre + new Vector2(Mathf.Cos(ang) * radius * 0.97f, Mathf.Sin(ang) * b * 0.97f), lw * 0.6f, faint);
            }
            L.FillTri(centre + new Vector2(0f, -b - 8f * s), centre + new Vector2(-4.5f * s, -b - 15f * s), centre + new Vector2(4.5f * s, -b - 15f * s), dim);
            // you: a small wedge at the centre
            L.FillTri(centre + new Vector2(0f, -5.5f * s), centre + new Vector2(-4.2f * s, 3.5f * s), centre + new Vector2(4.2f * s, 3.5f * s), ink);

            float blind = Mathf.Clamp01(staticAmount);
            int frame = Mathf.FloorToInt(time * 24f);
            if (blind < 0.995f)
            {
                for (int i = 0; i < contacts.Count; i++)
                {
                    var c = contacts[i];
                    if (blind > 0.05f)
                    {
                        // contacts drop out and shiver as the sensors fail
                        uint h = (uint)(i * 7919 + frame * 104729 + 17);
                        h ^= h << 13; h ^= h >> 17; h ^= h << 5;
                        if ((h & 255) / 255f < blind * 0.75f) continue;
                    }
                    DrawBlip(L, c, centre, radius, range, blind, frame, i, ink, dim, faint, warn, alpha);
                }
            }
            if (blind > 0.03f) DrawStatic(L, centre, radius, b, blind, frame, ink, alpha);
        }

        static void DrawBlip(HudLines L, Contact c, Vector2 centre, float radius, float range, float blind, int frame, int index, Color ink, Color dim, Color faint, Color warn, float alpha)
        {
            float s = L.Scale, lw = L.Lw(0.8f);
            Plot(c, centre, radius, range, out var ground, out var tip, out bool clamped);
            if (blind > 0.05f)
            {
                uint h = (uint)(index * 6007 + frame * 15485863 + 3);
                h ^= h << 13; h ^= h >> 17; h ^= h << 5;
                var j = new Vector2((h & 255) / 255f - 0.5f, ((h >> 8) & 255) / 255f - 0.5f) * (blind * 18f * s);
                ground += j; tip += j;
            }
            var col = c.Missile ? (c.Hostile ? (c.Incoming ? warn : dim) : dim) : c.Hostile ? ink : dim;
            if (clamped) col.a *= 0.65f;
            if (c.Missile && c.Incoming && Mathf.Repeat(frame / 24f * 5f, 1f) > 0.55f) col.a *= 0.35f;      // an incoming missile blinks
            float alt = ground.y - tip.y;
            if (Mathf.Abs(alt) > 2f * s)
            {
                L.Disc(ground, 1.5f * s, new Color(col.r, col.g, col.b, col.a * 0.55f));
                L.Line(ground, tip, lw * 0.7f, new Color(col.r, col.g, col.b, col.a * 0.8f));
            }
            if (c.Missile)
            {
                float r = (c.Torpedo ? 7f : 5.4f) * s;
                L.FillTri(tip + new Vector2(0f, -r), tip + new Vector2(-r * 0.85f, r * 0.8f), tip + new Vector2(r * 0.85f, r * 0.8f), col);
                return;
            }
            float u = (c.Big ? 6f : 3.8f) * s;
            if (c.Hostile)
            {
                if (c.Big) { L.Rect(tip - Vector2.one * u, tip + Vector2.one * u, lw, col); L.Box(tip, Vector2.one * u * 0.42f, 0f, col); }
                else L.Box(tip, Vector2.one * u * 0.78f, Mathf.PI * 0.25f, col);
            }
            else
            {
                L.Diamond(tip, u * 1.05f, lw, col);
                if (c.Big) { L.Line(tip + new Vector2(-u, 0f), tip + new Vector2(u, 0f), lw * 0.7f, col); L.Line(tip + new Vector2(0f, -u), tip + new Vector2(0f, u), lw * 0.7f, col); }
            }
            if (c.IsTarget)
            {
                float k = u + 4.5f * s;
                L.Brackets(tip, k, k * 0.55f, lw * 0.9f, new Color(ink.r, ink.g, ink.b, ink.a));
            }
        }

        static void DrawStatic(HudLines L, Vector2 centre, float radius, float b, float blind, int frame, Color ink, float alpha)
        {
            float s = L.Scale;
            int n = Mathf.RoundToInt(10f + 46f * blind);
            for (int i = 0; i < n; i++)
            {
                uint h = (uint)(i * 2654435761u + (uint)frame * 40503u);
                h ^= h << 13; h ^= h >> 17; h ^= h << 5;
                float a = (h & 1023) / 1023f * Mathf.PI * 2f;
                float r = Mathf.Sqrt(((h >> 10) & 1023) / 1023f) * 0.96f;
                var p = centre + new Vector2(Mathf.Cos(a) * radius * r, Mathf.Sin(a) * b * r);
                float len = (4f + ((h >> 20) & 15) * 1.7f) * s;
                float o = (((h >> 24) & 255) / 255f) * (0.25f + 0.45f * blind);
                L.Line(p, p + new Vector2(len, 0f), Mathf.Max(1f, 1.4f * s), new Color(ink.r, ink.g, ink.b, ink.a * o * alpha));
            }
        }
    }
}
