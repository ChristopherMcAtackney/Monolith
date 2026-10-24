using System.Collections.Generic;
using System.Globalization;
using Monolith.Core;
using Monolith.Hud;
using UnityEngine;

namespace Monolith.HudUi
{
    public sealed partial class HudRenderer
    {
        static readonly Color PlateColor = HudLines.Shade(0.50f);
        float _headerBottom;

        static readonly Dictionary<string, string> SideCache = new Dictionary<string, string>();

        static string Side(bool hostile, string size)
        {
            string key = (hostile ? "H" : "F") + size;
            if (SideCache.TryGetValue(key, out var v)) return v;
            if (SideCache.Count > 100) SideCache.Clear();
            return SideCache[key] = (hostile ? "HOSTILE" : "FRIENDLY") + "   " + (size ?? "ship").ToUpperInvariant();
        }

        // ------------------------------------------------------------------------------------------------ small parts

        /// <summary>A dark plate with a hairline border and ink ticks at its corners.</summary>
        void Plate(Vector2 min, Vector2 max, Color edge, float alpha = 1f)
        {
            L.Fill(min, max, A(PlateColor, alpha));
            L.Rect(min, max, lw * 0.6f, A(edge, 0.30f));
            float k = 11f * s;
            foreach (var corner in new[] { new Vector2(min.x, min.y), new Vector2(max.x, min.y), new Vector2(min.x, max.y), new Vector2(max.x, max.y) })
            {
                float sx = corner.x == min.x ? 1f : -1f, sy = corner.y == min.y ? 1f : -1f;
                L.Line(corner, corner + new Vector2(sx * k, 0f), lw, edge);
                L.Line(corner, corner + new Vector2(0f, sy * k), lw, edge);
            }
        }

        /// <summary>A section heading: tracked small caps, a rule under it and (when the style has them) a few of the aliens' glyphs. Returns the y under it.</summary>
        float Heading(string text, Vector2 pos, float width, TextAlign align = TextAlign.Left)
        {
            float cap = Cap(TextRole.Label);
            float tw = T(text, pos, TextRole.Label, dim, align);
            float x0 = align == TextAlign.Right ? pos.x - width : pos.x;
            float y = pos.y + cap + 9f * s;
            if (St.HeadingRule) L.Line(new Vector2(x0, y), new Vector2(x0 + width, y), lw * 0.6f, A(ink, 0.35f));
            int glyphs = Mathf.RoundToInt(St.Glyphs * 4f);
            if (glyphs > 0)
            {
                float gs = cap * 0.95f;
                float gx = align == TextAlign.Right ? pos.x - tw - 14f * s - glyphs * gs : pos.x + tw + 14f * s;
                if (align == TextAlign.Right) gx = pos.x - tw - 14f * s - glyphs * gs;
                L.GlyphRun(new Vector2(gx, pos.y), glyphs, gs, text.GetHashCode() & 0xff, lw * 0.6f, A(ink, 0.5f));
            }
            return y + 10f * s;
        }

        /// <summary>A row of equal cells filled to a fraction (a hull or shield reading).</summary>
        void CellBar(Vector2 pos, float width, float h, float v01, int n, Color on, Color off)
        {
            float gap = 2.5f * s;
            float cw = (width - gap * (n - 1)) / n;
            v01 = Mathf.Clamp01(v01);
            for (int i = 0; i < n; i++)
            {
                float f = Mathf.Clamp01(v01 * n - i);
                var a = new Vector2(pos.x + i * (cw + gap), pos.y);
                var b = a + new Vector2(cw, h);
                L.Fill(a, b, f > 0.5f ? on : off);
                if (f > 0.05f && f <= 0.5f) L.Fill(a, new Vector2(a.x + cw * f * 2f, b.y), on);
            }
        }

        // ------------------------------------------------------------------------------------------------ the target card (top right)

        void DrawTargetCard()
        {
            float pw = 372f * s, x1 = W - 44f * s, x0 = x1 - pw, y0 = 44f * s, pad = 18f * s;
            float capL = Cap(TextRole.Label), capB = Cap(TextRole.Body), capV = Cap(TextRole.Value);
            if (!F.HasTarget)
            {
                float hh = capL + 2 * 16f * s;
                Plate(new Vector2(x0, y0), new Vector2(x1, y0 + hh), ink, 0.6f);
                T("NO TARGET", new Vector2(x0 + pad, y0 + 16f * s), TextRole.Label, dim);
                Box("target", new Vector2(x0, y0), new Vector2(x1, y0 + hh));
                return;
            }
            float stat = F.SensorStatic;
            bool blind = stat >= 0.99f;
            bool flicker = !blind && stat > 0.05f && Mathf.Repeat(F.Time * 13f, 1f) < stat * 0.7f;
            var col = F.TargetHostile ? ink : dim;
            if (blind || flicker)
            {
                float hh = capL * 2 + 3 * 16f * s;
                Plate(new Vector2(x0, y0), new Vector2(x1, y0 + hh), warn, 0.9f);
                T(blind ? "NO SENSORS" : "TARGET", new Vector2(x0 + pad, y0 + 16f * s), TextRole.Label, blind ? warn : dim);
                T("....", new Vector2(x0 + pad, y0 + 16f * s + capL + 14f * s), TextRole.Value, dim);
                Box("target", new Vector2(x0, y0), new Vector2(x1, y0 + hh));
                return;
            }
            bool showShield = F.TargetHasShield;
            bool partRow = F.HasPart || F.TargetHasParts;
            bool lockRow = F.WeaponNeedsLock || F.WeaponSeeks;
            float rowLabel = capL + 12f * s, rowName = capV + 12f * s, rowClass = capB + 10f * s, rowBar = capB + 12f * s, rowRange = capV + 14f * s, rowPart = capB + 12f * s;
            float hgt = pad + rowLabel + rowName + rowClass + rowBar + (showShield ? rowBar : 0f) + rowRange + (partRow ? rowPart : 0f) + (lockRow ? rowBar + 4f * s : 0f) + pad - 4f * s;
            Plate(new Vector2(x0, y0), new Vector2(x1, y0 + hgt), ink);
            float x = x0 + pad, y = y0 + pad;
            T(Side(F.TargetHostile, F.TargetSize), new Vector2(x, y), TextRole.Label, F.TargetHostile ? ink : dim);
            int gl = Mathf.RoundToInt(St.Glyphs * 3f);
            if (gl > 0) L.GlyphRun(new Vector2(x1 - pad - gl * capL * 0.95f, y), gl, capL * 0.95f, 7, lw * 0.6f, A(ink, 0.5f));
            y += rowLabel;
            float inner = pw - pad * 2f;
            T(L.Ellipsize(Up(F.TargetName ?? ""), capV, L.Tracking(TextRole.Value), inner), new Vector2(x, y), TextRole.Value, col);
            y += rowName;
            T(L.Ellipsize(F.TargetClass ?? "", capB, L.Tracking(TextRole.Body), inner), new Vector2(x, y), TextRole.Body, dim);
            y += rowClass;
            float labW = Mathf.Max(66f * s, L.Measure("SHLD", TextRole.Label) + 16f * s);
            float barX = x + labW, barW = pw - pad * 2f - labW - 88f * s;
            T("HULL", new Vector2(x, y + 1f * s), TextRole.Label, dim);
            CellBar(new Vector2(barX, y + 2f * s), barW, capB * 0.8f, F.TargetHull01, 12, F.TargetHull01 < 0.25f ? warn : ink, faint);
            T(Pct(F.TargetHull01), new Vector2(x1 - pad, y), TextRole.Body, F.TargetHull01 < 0.25f ? warn : ink, TextAlign.Right);
            y += rowBar;
            if (showShield)
            {
                T("SHLD", new Vector2(x, y + 1f * s), TextRole.Label, dim);
                CellBar(new Vector2(barX, y + 2f * s), barW, capB * 0.8f, F.TargetShield01, 12, ink, faint);
                T(Pct(F.TargetShield01), new Vector2(x1 - pad, y), TextRole.Body, ink, TextAlign.Right);
                y += rowBar;
            }
            T(RangeText(F.TargetDist), new Vector2(x, y), TextRole.Value, col);
            T(I(F.TargetSpeed) + " M/S", new Vector2(x1 - pad, y + (capV - capL) * 0.5f), TextRole.Label, dim, TextAlign.Right);
            y += rowRange;
            if (partRow)
            {
                if (F.HasPart)
                {
                    L.FillTri(new Vector2(x, y + 1f * s), new Vector2(x, y + capB - 1f * s), new Vector2(x + capB * 0.75f, y + capB * 0.5f), ink);
                    T(L.Ellipsize(F.PartName, capB, L.Tracking(TextRole.Body), inner - capB - 100f * s), new Vector2(x + capB + 8f * s, y), TextRole.Body, ink);
                    T(Pct(F.PartHealth01), new Vector2(x1 - pad, y), TextRole.Body, F.PartHealth01 < 0.25f ? warn : ink, TextAlign.Right);
                }
                else T(F.UsePadGlyphs ? "TAP Y   SUBSYSTEMS" : "G   SUBSYSTEMS", new Vector2(x, y), TextRole.Label, dim);
                y += rowPart;
            }
            if (lockRow) DrawLockRow(new Vector2(x, y + 2f * s), pw - pad * 2f, capB);
            Box("target", new Vector2(x0, y0), new Vector2(x1, y0 + hgt));
        }

        void DrawLockRow(Vector2 pos, float width, float cap)
        {
            T("LOCK", new Vector2(pos.x, pos.y + 1f * s), TextRole.Label, dim);
            float labW = Mathf.Max(66f * s, L.Measure("SHLD", TextRole.Label) + 16f * s);
            float barX = pos.x + labW, barW = width - labW - 150f * s;
            bool locked = F.Lock == LockPhase.Locked;
            float prog = F.WeaponNeedsLock ? (locked ? 1f : F.LockProgress) : (locked ? 1f : 0f);
            CellBar(new Vector2(barX, pos.y + 2f * s), barW, cap * 0.8f, prog, 10, ink, faint);
            string word = F.WeaponNeedsLock ? (locked ? "LOCKED" : F.Lock == LockPhase.Acquiring ? "SEEKING" : "NO LOCK") : (locked ? "SEEKER" : "NO SEEK");
            T(word, new Vector2(pos.x + width, pos.y + 1f * s), TextRole.Label, locked ? ink : dim, TextAlign.Right);
            // the tone: three arcs that beat at the rate the lock tone beeps, and hold steady once the lock is made
            var tc = new Vector2(barX + barW + 18f * s, pos.y + cap * 0.5f + 1f * s);
            bool on = locked || F.Lock == LockPhase.Acquiring;
            float rate = Mathf.Lerp(2f, 9f, F.LockProgress);
            float lit = locked ? 1f : on ? (Mathf.Repeat(F.Time * rate, 1f) < 0.5f ? 1f : 0.2f) : 0.15f;
            for (int i = 0; i < 3; i++)
                L.Band(tc, (4f + i * 4.2f) * s, lw * 0.9f, -0.75f, 0.75f, A(ink, lit * (1f - i * 0.18f)));
        }

        // ------------------------------------------------------------------------------------------------ the radar block (bottom centre)

        void DrawRadarBlock()
        {
            float rr = 138f * s;
            var rc = new Vector2(C.x, H - 150f * s);
            HudRadar.Draw(L, F.Contacts, rc, rr, F.RadarRange, F.SensorStatic, F.Time);
            Box("radar", rc + new Vector2(-rr, -rr * HudRadar.Squash - 16f * s), rc + new Vector2(rr, rr * HudRadar.Squash + 4f * s));
            T(HudContacts.RangeText(F.RadarRange), rc + new Vector2(rr * 0.98f, rr * HudRadar.Squash + 8f * s), TextRole.Label, dim, TextAlign.Right);
            if (F.SensorStatic > 0.5f) T(F.SensorStatic >= 0.99f ? "NO SIGNAL" : "INTERFERENCE", rc + new Vector2(0f, -Cap(TextRole.Label) * 0.5f - 22f * s), TextRole.Label, A(warn, Blink(3f, 0.6f)), TextAlign.Center);

            // the shield (left) and the hull (right): two short thick arcs either side, the same band as the main gauges
            float ar = 178f * s, at = Mathf.Max(11f * s, St.ArcThick * 0.55f * s), half = 31f * Mathf.Deg2Rad;
            float sh = F.HasShield ? F.Shield01 : 0f;
            ShortArc(rc, ar, at, true, half, sh, F.HasShield ? ink : faint, 12);
            var hullCol = F.Hull01 < 0.3f ? A(warn, Blink(4f, 0.6f)) : ink;
            ShortArc(rc, ar, at, false, half, F.Hull01, hullCol, 12);
            float lx = rc.x - ar - at * 0.5f - 24f * s, rx = rc.x + ar + at * 0.5f + 24f * s;
            float vy = rc.y - Cap(TextRole.Value) - 4f * s;
            T("SHIELD", new Vector2(lx, vy), TextRole.Label, dim, TextAlign.Right);
            T(F.HasShield ? I(F.Shield01 * 100f) : "--", new Vector2(lx, vy + Cap(TextRole.Label) + 8f * s), TextRole.Value, F.HasShield ? ink : dim, TextAlign.Right);
            T("HULL", new Vector2(rx, vy), TextRole.Label, dim, TextAlign.Left);
            T(I(F.Hull01 * 100f), new Vector2(rx, vy + Cap(TextRole.Label) + 8f * s), TextRole.Value, hullCol, TextAlign.Left);
            Box("shieldHull", new Vector2(lx - 90f * s, rc.y - ar * Mathf.Sin(half) - 8f * s), new Vector2(rx + 90f * s, rc.y + ar * Mathf.Sin(half) + 8f * s));

            // the power choice under the radar
            string[] names = { "BAL", "SHD", "GUN", "ENG" };
            int sel = (int)F.Power;
            float py = H - 44f * s, step = 64f * s;
            float x = C.x - step * 1.5f;
            T("POWER", new Vector2(x - 122f * s, py), TextRole.Label, dim, TextAlign.Left);
            for (int i = 0; i < 4; i++)
            {
                bool on = i == sel;
                float cx = x + i * step;
                T(names[i], new Vector2(cx, py), TextRole.Label, on ? ink : A(dim, 0.8f), TextAlign.Center);
                if (on) L.Line(new Vector2(cx - 17f * s, py + Cap(TextRole.Label) + 8f * s), new Vector2(cx + 17f * s, py + Cap(TextRole.Label) + 8f * s), lw * 1.1f, ink);
            }
            Box("power", new Vector2(x - 80f * s, py - 2f * s), new Vector2(x + 3 * step + 40f * s, py + Cap(TextRole.Label) + 12f * s));
        }

        /// <summary>A short thick arc beside the radar: a band with a track, filling bottom to top.</summary>
        void ShortArc(Vector2 centre, float r, float thick, bool left, float half, float v01, Color col, int cells)
        {
            float aBot = left ? Mathf.PI - half : half, aTop = left ? Mathf.PI + half : -half;
            if (St.ArcCells > 0) L.Cells(centre, r, thick, aBot, aTop, cells, 2.2f * s, v01, col, faint);
            else
            {
                L.Band(centre, r, thick, aBot, aTop, faint);
                if (v01 > 0.003f) L.Band(centre, r, thick, aBot, Mathf.Lerp(aBot, aTop, Mathf.Clamp01(v01)), col);
            }
        }

        // ------------------------------------------------------------------------------------------------ guns and missiles (bottom right)

        void DrawArms()
        {
            float pw = 372f * s, x1 = W - 44f * s, x0 = x1 - pw, pad = 18f * s;
            float capB = Cap(TextRole.Body), capL = Cap(TextRole.Label), capV = Cap(TextRole.Value);
            float row = capV + 12f * s;
            float labW = L.Measure("MISSILE", TextRole.Label) + 18f * s, nameX = labW + 30f * s;
            int rows = 1 + (F.HeavyName != null ? 1 : 0) + F.Missiles.Count + 1;
            float hgt = pad * 2f + rows * row - 6f * s;
            float y0 = H - 44f * s - hgt;
            Plate(new Vector2(x0, y0), new Vector2(x1, y0 + hgt), ink, 0.85f);
            float x = x0 + pad, y = y0 + pad;
            void Row(string label, string name, string right, bool selected, bool online, bool dimmed)
            {
                var c = !online ? warn : dimmed ? dim : ink;
                T(label, new Vector2(x, y + (capV - capL) * 0.5f), TextRole.Label, dim);
                if (selected) L.FillTri(new Vector2(x + labW + 4f * s, y + 2f * s), new Vector2(x + labW + 4f * s, y + capV - 2f * s), new Vector2(x + labW + 4f * s + capV * 0.7f, y + capV * 0.5f), ink);
                T(name, new Vector2(x + nameX, y), TextRole.Value, c);
                if (!string.IsNullOrEmpty(right)) T(right, new Vector2(x1 - pad, y), TextRole.Value, c, TextAlign.Right);
                y += row;
            }
            Row("GUN", F.GunName != null ? Up(F.GunName) : "--", F.GunName != null && !F.GunOnline ? "OFFLINE" : null, false, F.GunName == null || F.GunOnline, F.GunName == null);
            if (F.HeavyName != null) Row("HEAVY", Up(F.HeavyName), !F.HeavyOnline ? "OFFLINE" : null, false, F.HeavyOnline, false);
            for (int i = 0; i < F.Missiles.Count; i++)
            {
                var m = F.Missiles[i];
                float ry = y;
                Row(i == 0 ? "MISSILE" : "", Up(m.Name), m.Selected && m.Offline ? "OFFLINE" : I(m.Ammo), m.Selected, !(m.Selected && m.Offline), !m.Selected || m.Ammo <= 0);
                if (m.Selected && m.Reload01 < 0.999f)
                    L.Line(new Vector2(x + nameX, ry + capV + 6f * s), new Vector2(x + nameX + (pw - pad * 2f - nameX) * m.Reload01, ry + capV + 6f * s), lw * 0.9f, dim);
            }
            Row("DECOY", F.Decoys > 0 ? "READY" : "EMPTY", I(F.Decoys), false, true, F.Decoys <= 0);
            Box("arms", new Vector2(x0, y0), new Vector2(x1, y0 + hgt));
        }

        // ------------------------------------------------------------------------------------------------ the ship's own systems (bottom left)

        void DrawSystems()
        {
            if (F.Systems.Count == 0) return;
            float x = 44f * s, capB = Cap(TextRole.Body), row = capB + 14f * s;
            float y = H - 62f * s - F.Systems.Count * row;
            Box("systems", new Vector2(x - 6f * s, y - 6f * s), new Vector2(x + 330f * s, H - 56f * s));
            float blink = Blink(2.5f, 0.6f);
            foreach (var l in F.Systems)
            {
                var col = l.Bad ? A(warn, blink) : ink;
                L.Fill(new Vector2(x, y + capB * 0.25f), new Vector2(x + capB * 0.5f, y + capB * 0.75f), col);
                T(l.Text, new Vector2(x + capB * 0.5f + 12f * s, y), TextRole.Body, col);
                y += row;
            }
        }

        // ------------------------------------------------------------------------------------------------ the left and top of the screen

        void DrawHeader()
        {
            float x = 44f * s, y = 40f * s;
            float capL = Cap(TextRole.Label);
            float wrapW = 400f * s;
            if (!string.IsNullOrEmpty(F.ClassName)) T(Up(F.ClassName), new Vector2(x, y), TextRole.Label, dim);
            float maxW = F.ClassName != null ? L.Measure(Up(F.ClassName), TextRole.Label) : 0f;
            y += capL + 16f * s;
            if (F.Info != null)
            {
                float step = capL + 11f * s;
                foreach (var line in F.Info)
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    foreach (var part in L.Wrap(line, capL, L.Tracking(TextRole.Label), wrapW))
                    {
                        float w = T(part, new Vector2(x, y), TextRole.Label, dim);
                        maxW = Mathf.Max(maxW, w);
                        y += step;
                    }
                }
            }
            _headerBottom = y;
            Box("header", new Vector2(x, 40f * s), new Vector2(x + Mathf.Max(maxW, 120f * s), y));
        }

        void DrawNotices()
        {
            float x = 44f * s, y = Mathf.Max(150f * s, _headerBottom + 12f * s), capB = Cap(TextRole.Body), row = capB + 14f * s;
            Box("notices", new Vector2(x, y), new Vector2(x + 380f * s, y + 5 * row));
            foreach (var n in F.Notices)
            {
                var col = n.Alert ? A(warn, n.Alpha) : A(ink, n.Alpha);
                T(n.Text, new Vector2(x, y), TextRole.Body, col);
                y += row;
            }
        }

        void DrawDirectives()
        {
            if (F.Directives.Count == 0) return;
            float capBn = Cap(TextRole.Body);
            float noticesBottom = Mathf.Max(150f * s, _headerBottom + 12f * s) + 5 * (capBn + 14f * s);
            float top = Mathf.Max(392f * s, noticesBottom + 14f * s);
            float x = 44f * s, y = top, width = 392f * s;
            float y1 = Heading("DIRECTIVES", new Vector2(x, y), width);
            y = y1;
            float capB = Cap(TextRole.Body);
            float textX = x + capB + 14f * s;
            // what is still to do first, then what failed, then what is done (the newest of those last)
            var order = new List<HudFrame.DirectiveLine>(F.Directives.Count);
            foreach (var d in F.Directives) if (d.State == MissionBoard.State.Active) order.Add(d);
            foreach (var d in F.Directives) if (d.State == MissionBoard.State.Failed) order.Add(d);
            foreach (var d in F.Directives) if (d.State == MissionBoard.State.Done) order.Add(d);
            float limit = H - 120f * s - (F.Systems.Count > 0 ? F.Systems.Count * (capB + 14f * s) + 30f * s : 0f);
            int shown = 0;
            foreach (var d in order)
            {
                var probe = L.Wrap(d.Text, capB, L.Tracking(TextRole.Body), width - (textX - x));
                if (shown > 0 && y + probe.Count * (capB + 12f * s) + 9f * s > limit)
                {
                    T("+ " + (order.Count - shown) + " MORE", new Vector2(textX, y), TextRole.Label, dim);
                    y += Cap(TextRole.Label) + 12f * s;
                    break;
                }
                shown++;
                bool done = d.State == MissionBoard.State.Done, failed = d.State == MissionBoard.State.Failed;
                var col = failed ? warn : done ? dim : ink;
                var lines = L.Wrap(d.Text, capB, L.Tracking(TextRole.Body), width - (textX - x));
                float my = y + capB * 0.5f;
                if (done) { L.Line(new Vector2(x + 1f * s, my), new Vector2(x + capB * 0.38f, my + capB * 0.32f), lw, col); L.Line(new Vector2(x + capB * 0.38f, my + capB * 0.32f), new Vector2(x + capB * 0.9f, my - capB * 0.42f), lw, col); }
                else if (failed) { L.Line(new Vector2(x, my - capB * 0.4f), new Vector2(x + capB * 0.8f, my + capB * 0.4f), lw, col); L.Line(new Vector2(x + capB * 0.8f, my - capB * 0.4f), new Vector2(x, my + capB * 0.4f), lw, col); }
                else L.Rect(new Vector2(x + 1f * s, my - capB * 0.32f), new Vector2(x + capB * 0.65f, my + capB * 0.32f), lw * 0.8f, col);
                for (int i = 0; i < lines.Count; i++)
                {
                    T(lines[i], new Vector2(textX, y), TextRole.Body, col);
                    y += capB + 12f * s;
                }
                y += 9f * s;
            }
            Box("directives", new Vector2(x, top), new Vector2(x + width, y));
        }

        void DrawRadio()
        {
            float hdrRight = Boxes.TryGetValue("header", out var hb) ? hb.xMax : 0f;
            float y = 40f * s, maxW = Mathf.Clamp(2f * (C.x - hdrRight - 28f * s), 300f * s, 760f * s), capB = Cap(TextRole.Body), capL = Cap(TextRole.Label);
            float top = y;
            foreach (var r in F.Radio)
            {
                var col = A(r.Alert ? warn : ink, r.Alpha);
                var dcol = A(r.Alert ? warn : dim, r.Alpha);
                if (!string.IsNullOrEmpty(r.Speaker)) { T(Up(r.Speaker), new Vector2(C.x, y), TextRole.Label, dcol, TextAlign.Center); y += capL + 10f * s; }
                var lines = L.Wrap(r.Text, capB, L.Tracking(TextRole.Body), maxW);
                foreach (var ln in lines) { T(ln, new Vector2(C.x, y), TextRole.Body, col, TextAlign.Center); y += capB + 12f * s; }
                y += 12f * s;
            }
            Box("radio", new Vector2(C.x - maxW * 0.5f, top), new Vector2(C.x + maxW * 0.5f, Mathf.Max(y, top + 8f * s)));
        }

        void DrawPrompt()
        {
            if (string.IsNullOrEmpty(F.Prompt)) return;
            string text = Up("[ " + F.Prompt + " ]");
            T(text, new Vector2(C.x, C.y + 150f * s), TextRole.Value, A(ink, 0.45f + 0.55f * Blink(1.6f, 0.2f)), TextAlign.Center);
            Box("prompt", new Vector2(C.x - 200f * s, C.y + 150f * s), new Vector2(C.x + 200f * s, C.y + 150f * s + Cap(TextRole.Value)));
        }

        // ------------------------------------------------------------------------------------------------ warnings

        void DrawThreats()
        {
            float y = 156f * s;
            float capV = Cap(TextRole.Value), capB = Cap(TextRole.Body);
            if (F.IncomingMissiles > 0)
            {
                bool close = F.IncomingEta < 2.5f;
                float b = Blink(close ? 8f : 4f, 0.45f);
                var col = A(warn, b);
                float pw = 330f * s, hh = capV + capB + 40f * s;
                var min = new Vector2(C.x - pw * 0.5f, y);
                var max = min + new Vector2(pw, hh);
                Plate(min, max, A(warn, b), 0.9f);
                T("MISSILE INBOUND", new Vector2(C.x, y + 14f * s), TextRole.Value, col, TextAlign.Center);
                string eta = F.IncomingEta < 90f ? "   " + F.IncomingEta.ToString("0.0", CultureInfo.InvariantCulture) + " S" : "";
                T(I(F.IncomingNearest) + " M" + eta, new Vector2(C.x, y + 14f * s + capV + 10f * s), TextRole.Body, warn, TextAlign.Center);
                Box("threat", min, max);
                y = max.y + 12f * s;
                if (F.IncomingOffAxis)
                {
                    // an arrow round the reticle toward the missile, outside the target ring
                    var d = F.IncomingDir;
                    float ring = 226f * s;
                    L.Arrow(C + d * (ring + 26f * s), d, 24f * s, A(warn, b));
                    float ang = Mathf.Atan2(d.y, d.x);
                    L.Band(C, ring, lw * 1.4f, ang - 0.32f, ang + 0.32f, A(warn, b));
                }
                else
                {
                    // dead ahead: brackets round the reticle
                    L.Brackets(C, 70f * s, 24f * s, lw * 1.4f, A(warn, b));
                }
            }
            if (F.LockedOnMe) { T("LOCKED ON", new Vector2(C.x, y), TextRole.Value, A(warn, Blink(5f, 0.45f)), TextAlign.Center); y += capV + 14f * s; Box("lockedOn", new Vector2(C.x - 100f * s, y - capV - 14f * s), new Vector2(C.x + 100f * s, y)); }
            else if (F.BeingLocked > 0) { T("LOCK WARNING", new Vector2(C.x, y), TextRole.Body, warn, TextAlign.Center); y += capB + 12f * s; Box("lockWarn", new Vector2(C.x - 100f * s, y - capB - 12f * s), new Vector2(C.x + 100f * s, y)); }
            if (F.IncomingMissiles > 0 && F.IncomingEta < 2.5f) DrawEdgePulse(0.6f * Blink(8f, 0.2f));
        }
    }
}
