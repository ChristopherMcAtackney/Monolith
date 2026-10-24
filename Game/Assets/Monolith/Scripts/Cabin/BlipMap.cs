using System.Globalization;
using Monolith.Core;
using Monolith.Hud;
using UnityEngine;

namespace Monolith.Cabin
{
    /// <summary>
    /// The retro battle map (design section 5): blips on a radar-style plan, no 3D renderings of ships. The plan table shows it, and the seat's switch to the map draws it over
    /// the view. Plan view with your heading up and you at the centre; rings for range; every other ship a blip on a stem that rises or drops by how far above or below your
    /// plane it is (FS2's radar): a hollow diamond for a friend, a filled square for an enemy, a bigger box with a cross for a capital ship, brackets on your target.
    /// Single-colour line graphics, like everything the player's tech draws.
    /// </summary>
    public static class BlipMap
    {
        static float _range = 3000f;
        static readonly float[] Ranges = { 1500f, 3000f, 6000f, 12000f, 25000f, 60000f };

        /// <summary>The map's range (m), the smallest step that holds every enemy within 8 km and every capital ship in play (with a little hysteresis, so it does not flutter).</summary>
        public static float PickRange(GameWorld w, ShipState me)
        {
            float need = 0f;
            foreach (var s in w.Ships.All)
            {
                if (s.Id == me.Id) continue;
                var c = w.Combat.Get(s.Id);
                if (c != null && !c.Alive) continue;
                float d = Vector3.Distance(s.Position, me.Position);
                bool big = s.Radius > 60f;
                if (d < 8000f || big) need = Mathf.Max(need, d);
            }
            float want = Ranges[Ranges.Length - 1];
            foreach (var r in Ranges) if (need * 1.12f <= r) { want = r; break; }
            if (want > _range || want < _range * 0.55f) _range = want;
            return _range;
        }

        public static string RangeText(float m) => m >= 1000f ? (m / 1000f).ToString(m % 1000f == 0f ? "0" : "0.0", CultureInfo.InvariantCulture) + " KM" : Mathf.RoundToInt(m).ToString(CultureInfo.InvariantCulture) + " M";

        /// <summary>Draw the map into <paramref name="L"/> within a circle of <paramref name="radius"/> pixels at <paramref name="centre"/>.</summary>
        public static void Draw(HudLines L, GameWorld w, Vector2 centre, float radius, float lw, Color ink, float time, float? fixedRange = null)
        {
            var me = w.Ships.Get(w.LocalPlayerShip);
            var dim = new Color(ink.r, ink.g, ink.b, ink.a * 0.38f);
            var faint = new Color(ink.r, ink.g, ink.b, ink.a * 0.18f);
            float unit = radius / 300f;
            // The frame: rings, the cross, the range marks.
            float range = me != null ? (fixedRange ?? PickRange(w, me)) : (fixedRange ?? 3000f);
            for (int i = 1; i <= 3; i++)
            {
                L.Arc(centre, radius * i / 3f, 0f, Mathf.PI * 2f, lw * (i == 3 ? 1.0f : 0.6f), i == 3 ? dim : faint, 48);
                L.Text(RangeText(range * i / 3f), centre + new Vector2(radius * i / 3f + 4f * unit, -2f * unit), 9f * unit, lw * 0.6f, dim);
            }
            L.Line(centre + new Vector2(-radius, 0f), centre + new Vector2(radius, 0f), lw * 0.5f, faint);
            L.Line(centre + new Vector2(0f, -radius), centre + new Vector2(0f, radius), lw * 0.5f, faint);
            for (int a = 0; a < 360; a += 30)
            {
                float r0 = radius * (a % 90 == 0 ? 0.94f : 0.97f), r1 = radius;
                float rad = a * Mathf.Deg2Rad;
                L.Line(centre + new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad)) * r0, centre + new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad)) * r1, lw * 0.7f, dim);
            }
            if (me == null) return;
            // You: a triangle at the centre.
            Tri(L, centre, 9f * unit, lw, ink);
            float scale = radius / range;
            var inv = Quaternion.Inverse(me.Rotation);
            foreach (var s in w.Ships.All)
            {
                if (s.Id == me.Id) continue;
                var c = w.Combat.Get(s.Id);
                if (c != null && !c.Alive) continue;
                Vector3 rel = inv * (s.Position - me.Position);
                Vector2 flat = new Vector2(rel.x, rel.z);
                float dist = flat.magnitude;
                bool outside = dist * scale > radius * 0.97f;
                Vector2 plot = outside ? flat / dist * radius * 0.97f : flat * scale;
                var p = centre + new Vector2(plot.x, -plot.y);
                float alt = outside ? 0f : Mathf.Clamp(rel.y * scale, -radius * 0.25f, radius * 0.25f);
                var tip = p + new Vector2(0f, -alt);
                bool hostile = Teams.Hostile(me.Team, s.Team);
                bool big = s.Radius > 60f;
                var col = hostile ? ink : dim;
                if (!outside && Mathf.Abs(alt) > 1.5f * unit) { L.Line(p + new Vector2(-2.5f * unit, 0f), p + new Vector2(2.5f * unit, 0f), lw * 0.7f, faint); L.Line(p, tip, lw * 0.7f, col); }
                float r = (big ? 7.5f : 4.5f) * unit;
                if (outside) col = new Color(col.r, col.g, col.b, col.a * 0.7f);
                if (hostile) { if (big) { L.Rect(tip - Vector2.one * r, tip + Vector2.one * r, lw, col); L.Fill(tip - Vector2.one * r * 0.4f, tip + Vector2.one * r * 0.4f, col); } else L.Fill(tip - Vector2.one * r * 0.8f, tip + Vector2.one * r * 0.8f, col); }
                else
                {
                    Diamond(L, tip, r, lw, col);
                    if (big) { L.Line(tip + new Vector2(-r, 0f), tip + new Vector2(r, 0f), lw * 0.7f, col); L.Line(tip + new Vector2(0f, -r), tip + new Vector2(0f, r), lw * 0.7f, col); }
                }
                if (s.Id == me.Target)
                {
                    float k = r + 5f * unit, t = k * 0.55f;
                    for (int i = 0; i < 4; i++)
                    {
                        float sx = (i & 1) == 0 ? -1f : 1f, sy = (i & 2) == 0 ? -1f : 1f;
                        var corner = tip + new Vector2(sx * k, sy * k);
                        L.Line(corner, corner - new Vector2(sx * t, 0f), lw, ink);
                        L.Line(corner, corner - new Vector2(0f, sy * t), lw, ink);
                    }
                }
                if (big && !outside && !string.IsNullOrEmpty(s.Callsign)) L.Text(s.Callsign, tip + new Vector2(r + 5f * unit, -4f * unit), 9f * unit, lw * 0.6f, dim);
            }
        }

        static void Tri(HudLines L, Vector2 c, float r, float lw, Color col)
        {
            var a = c + new Vector2(0f, -r * 1.2f); var b = c + new Vector2(-r * 0.8f, r * 0.8f); var d = c + new Vector2(r * 0.8f, r * 0.8f);
            L.Line(a, b, lw, col); L.Line(b, d, lw, col); L.Line(d, a, lw, col);
        }

        static void Diamond(HudLines L, Vector2 c, float r, float lw, Color col)
        {
            L.Line(c + new Vector2(0f, -r), c + new Vector2(r, 0f), lw, col); L.Line(c + new Vector2(r, 0f), c + new Vector2(0f, r), lw, col);
            L.Line(c + new Vector2(0f, r), c + new Vector2(-r, 0f), lw, col); L.Line(c + new Vector2(-r, 0f), c + new Vector2(0f, -r), lw, col);
        }
    }
}
