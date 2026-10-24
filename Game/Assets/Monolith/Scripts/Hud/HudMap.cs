using System.Collections.Generic;
using UnityEngine;

namespace Monolith.Hud
{
    /// <summary>
    /// The battle map (design section 5): blips on a radar-style plan, no 3D renderings of ships, very retro. Plan view with your heading up and you at the centre; rings for range;
    /// every other ship a blip on a stem that rises or drops by how far above or below your plane it is; a hollow diamond for a friend, a solid one for an enemy, a boxed cross for a
    /// capital ship (named), a triangle for a missile, brackets on your target, and a scan line that sweeps round. The seat's switch draws it over the view and the cabin's plan
    /// table draws it into a screen, both through this one function, in the style the player has chosen.
    /// </summary>
    public static class HudMap
    {
        public static void Draw(HudLines L, List<Contact> contacts, Vector2 centre, float radius, float range, float time, bool hasPlayer = true, float alpha = 1f)
        {
            float lw = L.Lw(0.9f);
            float unit = radius / 300f;
            var ink = L.Ink; ink.a *= alpha;
            var dim = L.Dim; dim.a *= alpha;
            var faint = L.Faint; faint.a *= alpha;
            var warn = L.Warn; warn.a *= alpha;

            L.Disc(centre, radius, HudLines.Shade(0.62f * alpha));
            // the rings, with their ranges
            for (int i = 1; i <= 3; i++)
            {
                L.Ring(centre, radius * i / 3f, lw * (i == 3 ? 1.1f : 0.65f), i == 3 ? new Color(ink.r, ink.g, ink.b, ink.a * 0.6f) : faint);
                L.Text(HudContacts.RangeText(range * i / 3f), centre + new Vector2(radius * i / 3f + 6f * unit, -4f * unit * 1.5f), 9.5f * Mathf.Max(unit, 0.9f), dim, TextAlign.Left, L.Style.TrackLabel, TextRole.Label);
            }
            L.Line(centre + new Vector2(-radius, 0f), centre + new Vector2(radius, 0f), lw * 0.5f, faint);
            L.Line(centre + new Vector2(0f, -radius), centre + new Vector2(0f, radius), lw * 0.5f, faint);
            for (int a = 0; a < 360; a += 15)
            {
                bool major = a % 90 == 0;
                float rad = a * Mathf.Deg2Rad;
                Vector2 d = new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad));
                float r0 = radius * (major ? 0.93f : a % 30 == 0 ? 0.96f : 0.98f);
                L.Line(centre + d * r0, centre + d * radius, lw * 0.7f, major ? ink : dim);
            }
            L.Text("N", centre + new Vector2(0f, -radius - 26f * Mathf.Max(unit, 0.9f)), 11f * Mathf.Max(unit, 0.9f), dim, TextAlign.Center, L.Style.TrackLabel, TextRole.Label);

            // the scan line: a bright arm trailing a fade, once round every four seconds
            float sweep = time * (Mathf.PI * 2f / 4f);
            for (int k = 0; k < 16; k++)
            {
                float ang = sweep - k * 0.045f;
                Vector2 d = new Vector2(Mathf.Sin(ang), -Mathf.Cos(ang));
                L.Line(centre + d * radius * 0.04f, centre + d * radius * 0.985f, lw * 0.8f, new Color(ink.r, ink.g, ink.b, ink.a * 0.34f * (1f - k / 16f)));
            }

            if (!hasPlayer) return;
            float t = 9f * unit;
            L.FillTri(centre + new Vector2(0f, -t * 1.2f), centre + new Vector2(-t * 0.8f, t * 0.8f), centre + new Vector2(t * 0.8f, t * 0.8f), ink);

            float scale = radius / range;
            for (int i = 0; i < contacts.Count; i++)
            {
                var c = contacts[i];
                Vector2 flat = new Vector2(c.Rel.x, c.Rel.z);
                float dist = flat.magnitude;
                bool outside = dist * scale > radius * 0.97f;
                Vector2 plot = outside ? flat / Mathf.Max(dist, 1e-3f) * radius * 0.97f : flat * scale;
                var p = centre + new Vector2(plot.x, -plot.y);
                float alt = outside ? 0f : Mathf.Clamp(c.Rel.y * scale, -radius * 0.25f, radius * 0.25f);
                var tip = p + new Vector2(0f, -alt);
                var col = c.Missile ? (c.Incoming ? warn : dim) : c.Hostile ? ink : dim;
                if (outside) col.a *= 0.7f;
                if (c.Missile && c.Incoming && Mathf.Repeat(time * 5f, 1f) > 0.55f) col.a *= 0.35f;
                if (!outside && Mathf.Abs(alt) > 1.5f * unit)
                {
                    L.Line(p + new Vector2(-2.5f * unit, 0f), p + new Vector2(2.5f * unit, 0f), lw * 0.7f, faint);
                    L.Line(p, tip, lw * 0.7f, col);
                }
                if (c.Missile)
                {
                    float m = (c.Torpedo ? 7f : 5.5f) * unit;
                    L.FillTri(tip + new Vector2(0f, -m), tip + new Vector2(-m * 0.85f, m * 0.8f), tip + new Vector2(m * 0.85f, m * 0.8f), col);
                    continue;
                }
                float r = (c.Big ? 7.5f : 4.5f) * unit;
                if (c.Hostile)
                {
                    if (c.Big) { L.Rect(tip - Vector2.one * r, tip + Vector2.one * r, lw, col); L.Box(tip, Vector2.one * r * 0.4f, 0f, col); }
                    else L.Box(tip, Vector2.one * r * 0.75f, 0f, col);
                }
                else
                {
                    L.Diamond(tip, r, lw, col);
                    if (c.Big) { L.Line(tip + new Vector2(-r, 0f), tip + new Vector2(r, 0f), lw * 0.7f, col); L.Line(tip + new Vector2(0f, -r), tip + new Vector2(0f, r), lw * 0.7f, col); }
                }
                if (c.IsTarget) L.Brackets(tip, r + 5f * unit, (r + 5f * unit) * 0.55f, lw, ink);
                if (c.Big && !outside && !string.IsNullOrEmpty(c.Callsign))
                    L.Text(c.Callsign.ToUpperInvariant(), tip + new Vector2(r + 6f * unit, -5f * Mathf.Max(unit, 0.9f)), 10f * Mathf.Max(unit, 0.9f), dim, TextAlign.Left, L.Style.TrackLabel, TextRole.Label);
            }
        }
    }
}
