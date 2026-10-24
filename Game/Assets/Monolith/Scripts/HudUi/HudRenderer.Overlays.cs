using System.Collections.Generic;
using System.Globalization;
using Monolith.Core;
using Monolith.Hud;
using UnityEngine;

namespace Monolith.HudUi
{
    public sealed partial class HudRenderer
    {
        // ------------------------------------------------------------------------------------------------ the quick menu (hold Y)

        void DrawQuickMenu()
        {
            if (!F.QuickMenu) return;
            float pw = 640f * s, x0 = C.x - pw * 0.5f, y0 = C.y + 78f * s;
            float capL = Cap(TextRole.Label), capV = Cap(TextRole.Value);
            float row = capV + 13f * s, pad = 20f * s;
            float hgt = pad * 2f + capL + 26f * s + row * 4f;
            L.Fill(new Vector2(x0, y0), new Vector2(x0 + pw, y0 + hgt), HudLines.Shade(0.93f));
            Plate(new Vector2(x0, y0), new Vector2(x0 + pw, y0 + hgt), ink, 0.01f);
            Box("menu", new Vector2(x0, y0), new Vector2(x0 + pw, y0 + hgt));
            float colW = pw * 0.5f;
            bool pad4 = F.UsePadGlyphs;

            float y = Heading("POWER", new Vector2(x0 + pad, y0 + pad), colW - pad * 2f);
            string[] pn = { "GUNS", "SHIELDS", "ENGINES", "BALANCED" };
            PowerMode[] modes = { PowerMode.Guns, PowerMode.Shields, PowerMode.Engines, PowerMode.Balanced };
            string[] pk = { "6", "5", "7", "8" };
            for (int i = 0; i < 4; i++)
            {
                bool on = F.Power == modes[i];
                var col = on ? ink : dim;
                var gc = new Vector2(x0 + pad + 14f * s, y + capV * 0.5f + i * row);
                if (pad4) DPadGlyph(gc, i, 12f * s, col); else KeyCap(gc, pk[i], col);
                T(pn[i], new Vector2(x0 + pad + 52f * s, y + i * row), TextRole.Value, col);
                if (on) L.Disc(new Vector2(x0 + colW - pad - 8f * s, gc.y), 4f * s, ink);
            }

            float y2 = Heading("WING ORDERS", new Vector2(x0 + colW + pad, y0 + pad), colW - pad * 2f);
            string[] on_ = { "ATTACK MY TARGET", "DISABLE MY TARGET", "COVER ME", "FORM UP" };
            string[] ok = { "ATTACK", "DISABLE", "COVER", "FORM" };
            string[] pbtn = { "A", "B", "X", "LB" };
            string[] kbtn = { "F1", "F2", "F3", "F4" };
            for (int i = 0; i < 4; i++)
            {
                bool on = F.LastOrder == ok[i];
                var col = on ? ink : dim;
                var gc = new Vector2(x0 + colW + pad + 16f * s, y2 + capV * 0.5f + i * row);
                if (pad4) ButtonGlyph(gc, pbtn[i], 13f * s, col); else KeyCap(gc, kbtn[i], col);
                T(on_[i], new Vector2(x0 + colW + pad + 54f * s, y2 + i * row), TextRole.Value, col);
            }
            L.Line(new Vector2(x0 + colW, y0 + pad), new Vector2(x0 + colW, y0 + hgt - pad), lw * 0.6f, A(ink, 0.3f));
        }

        /// <summary>A pad's D-pad as a small cross with one arm lit (0 up, 1 left, 2 right, 3 down).</summary>
        void DPadGlyph(Vector2 c, int arm, float r, Color col)
        {
            Vector2[] dirs = { Vector2.up * -1f, Vector2.left, Vector2.right, Vector2.up };
            for (int i = 0; i < 4; i++)
            {
                bool lit = i == arm;
                var p = c + dirs[i] * r * 0.62f;
                var half = (i == 0 || i == 3) ? new Vector2(r * 0.24f, r * 0.34f) : new Vector2(r * 0.34f, r * 0.24f);
                L.Fill(p - half, p + half, lit ? col : A(col, 0.3f));
            }
            L.Fill(c - Vector2.one * r * 0.24f, c + Vector2.one * r * 0.24f, A(col, 0.3f));
        }

        void ButtonGlyph(Vector2 c, string label, float r, Color col)
        {
            if (label.Length > 1)
            {
                var half = new Vector2(r * 1.25f, r * 0.82f);
                L.Rect(c - half, c + half, lw * 0.9f, col);
                T(label, new Vector2(c.x, c.y - Cap(TextRole.Label) * 0.5f), TextRole.Label, col, TextAlign.Center, 0.6f);
                return;
            }
            L.Ring(c, r, lw * 0.9f, col);
            T(label, new Vector2(c.x, c.y - Cap(TextRole.Label) * 0.5f), TextRole.Label, col, TextAlign.Center, 0f);
        }

        void KeyCap(Vector2 c, string label, Color col)
        {
            float cap = Cap(TextRole.Label);
            float w = Mathf.Max(26f * s, L.Measure(label, TextRole.Label) + 16f * s);
            var half = new Vector2(w * 0.5f, cap * 0.5f + 7f * s);
            L.Rect(c - half, c + half, lw * 0.9f, col);
            T(label, new Vector2(c.x, c.y - cap * 0.5f), TextRole.Label, col, TextAlign.Center, 0.4f);
        }

        // ------------------------------------------------------------------------------------------------ the battle map

        void DrawMapScreen()
        {
            float r = H * 0.36f;
            var mc = new Vector2(C.x, C.y + 48f * s);
            HudMap.Draw(L, F.Contacts, mc, r, F.MapRange, F.Time, F.HasShip);
            Box("map", mc - Vector2.one * r, mc + Vector2.one * r);
            // the title and the key, top right
            float x1 = W - 44f * s, y = 44f * s;
            T("BATTLE MAP", new Vector2(x1, y), TextRole.Value, ink, TextAlign.Right);
            y += Cap(TextRole.Value) + 12f * s;
            T("RANGE " + HudContacts.RangeText(F.MapRange) + "   LIVE, NOT PAUSED", new Vector2(x1, y), TextRole.Label, dim, TextAlign.Right);
            y += Cap(TextRole.Label) + 26f * s;
            float rowH = Cap(TextRole.Label) + 20f * s, kx = x1 - 190f * s;
            float y0 = y;
            void Key(int kind, string label)
            {
                var p = new Vector2(kx, y + Cap(TextRole.Label) * 0.5f);
                float u = 6f * s;
                switch (kind)
                {
                    case 0: L.Diamond(p, u, lw, dim); break;
                    case 1: L.Box(p, Vector2.one * u * 0.78f, 0f, ink); break;
                    case 2: L.Rect(p - Vector2.one * u * 1.2f, p + Vector2.one * u * 1.2f, lw, ink); L.Box(p, Vector2.one * u * 0.5f, 0f, ink); break;
                    case 3: L.FillTri(p + new Vector2(0f, -u), p + new Vector2(-u * 0.85f, u * 0.8f), p + new Vector2(u * 0.85f, u * 0.8f), warn); break;
                    default: L.Brackets(p, u * 1.3f, u * 0.7f, lw, ink); break;
                }
                T(label, new Vector2(kx + 22f * s, y), TextRole.Label, dim);
                y += rowH;
            }
            Key(0, "FRIEND"); Key(1, "ENEMY"); Key(2, "CAPITAL SHIP"); Key(3, "MISSILE"); Key(4, "YOUR TARGET");
            T("STEM UP  ABOVE YOU", new Vector2(x1, y + 4f * s), TextRole.Label, dim, TextAlign.Right);
            Box("mapKey", new Vector2(kx - 20f * s, 44f * s), new Vector2(x1, y + Cap(TextRole.Label) + 10f * s));
            _ = y0;
        }

        // ------------------------------------------------------------------------------------------------ banner, overlay, footer, edges

        void DrawBanner()
        {
            if (string.IsNullOrEmpty(F.Banner) && !(F.HasShip && !F.Alive)) return;
            string text = string.IsNullOrEmpty(F.Banner) ? "SHIP LOST" : F.Banner;
            var col = F.HasShip && !F.Alive ? warn : ink;
            float cap = Cap(TextRole.Display) * 1.5f;
            float y = C.y - 130f * s;
            L.Text(text.ToUpperInvariant(), new Vector2(C.x, y), cap, col, TextAlign.Center, L.Tracking(TextRole.Display) + 0.06f, TextRole.Display);
            float w = L.Measure(text.ToUpperInvariant(), cap, L.Tracking(TextRole.Display) + 0.06f);
            L.Line(new Vector2(C.x - w * 0.5f, y + cap + 22f * s), new Vector2(C.x + w * 0.5f, y + cap + 22f * s), lw * 0.8f, A(col, 0.5f));
            Box("banner", new Vector2(C.x - w * 0.5f, y), new Vector2(C.x + w * 0.5f, y + cap));
            if (!string.IsNullOrEmpty(F.BannerSub)) T(F.BannerSub, new Vector2(C.x, y + cap + 44f * s), TextRole.Body, dim, TextAlign.Center);
        }

        void DrawFooter()
        {
            if (string.IsNullOrEmpty(F.Footer)) return;
            T(F.Footer, new Vector2(44f * s, H - 36f * s), TextRole.Label, warn);
        }

        void DrawEdge()
        {
            DrawEdgePulse(F.DamageFlash * 0.9f);
        }

        /// <summary>A frame of the warning colour round the screen, nested lines fading inward.</summary>
        void DrawEdgePulse(float a)
        {
            if (a <= 0.01f) return;
            for (int i = 0; i < 3; i++)
            {
                float inset = (4f + i * 8f) * s;
                var col = A(warn, a * (0.6f - i * 0.18f));
                L.Rect(new Vector2(inset, inset), new Vector2(W - inset, H - inset), Mathf.Max(2f, 4f * s), col);
            }
        }

        /// <summary>The pause, briefing and debrief screen: lines of text on a plate in the middle. A tab splits a line into a key and what it does; a blank line is a gap; "#" starts a section.</summary>
        void DrawOverlay()
        {
            var lines = F.Overlay;
            if (lines == null || lines.Length == 0) return;
            float pw = Mathf.Min(980f * s, W - 80f * s), pad = 34f * s;
            float capB = Cap(TextRole.Body), capL = Cap(TextRole.Label), capD = Cap(TextRole.Value) * 1.35f;
            float textW = pw - pad * 2f;
            float keyW = 0f;
            foreach (var l in lines) { int t = l != null ? l.IndexOf('\t') : -1; if (t > 0) keyW = Mathf.Max(keyW, L.Measure(l.Substring(0, t).ToUpperInvariant(), TextRole.Label)); }
            keyW = keyW > 0f ? Mathf.Min(keyW + 26f * s, textW * 0.38f) : 0f;

            // lay out first, so the plate can be sized to it
            var rows = new List<(int kind, string a, string b, float h)>();
            float total = pad;
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i] ?? "";
                if (i == 0) { rows.Add((0, l.ToUpperInvariant(), null, capD + 30f * s)); total += capD + 30f * s; continue; }
                if (l.Length == 0) { rows.Add((1, null, null, 14f * s)); total += 14f * s; continue; }
                if (l[0] == '#') { rows.Add((2, l.Substring(1).Trim().ToUpperInvariant(), null, capL + 24f * s)); total += capL + 24f * s; continue; }
                int tab = l.IndexOf('\t');
                if (tab > 0)
                {
                    string key = l.Substring(0, tab).ToUpperInvariant(), desc = l.Substring(tab + 1);
                    var wrapped = L.Wrap(desc, capB, L.Tracking(TextRole.Body), textW - keyW);
                    float h = Mathf.Max(capB + 12f * s, wrapped.Count * (capB + 10f * s) + 2f * s);
                    rows.Add((3, key, string.Join("\n", wrapped), h)); total += h;
                    continue;
                }
                var w2 = L.Wrap(l, capB, L.Tracking(TextRole.Body), textW);
                float hh = w2.Count * (capB + 11f * s) + 5f * s;
                rows.Add((4, string.Join("\n", w2), null, hh)); total += hh;
            }
            total += pad;
            float maxH = H - 70f * s;
            float fit = total > maxH ? Mathf.Max(0.55f, (maxH - 2f * pad) / (total - 2f * pad)) : 1f;
            float ph = Mathf.Min(maxH, (total - 2f * pad) * fit + 2f * pad);
            float x0 = C.x - pw * 0.5f, y0 = Mathf.Max(24f * s, C.y - ph * 0.5f);
            if (!string.IsNullOrEmpty(F.Banner)) y0 = Mathf.Max(y0, C.y - 10f * s);       // under the banner (the debrief)
            ph = Mathf.Min(ph, H - y0 - 24f * s);
            L.Fill(new Vector2(x0, y0), new Vector2(x0 + pw, y0 + ph), HudLines.Shade(0.95f));
            Plate(new Vector2(x0, y0), new Vector2(x0 + pw, y0 + ph), ink, 0.01f);
            Box("overlay", new Vector2(x0, y0), new Vector2(x0 + pw, y0 + ph));
            float y = y0 + pad * 0.8f;
            foreach (var r in rows)
            {
                float h = r.h * (r.kind == 1 || r.kind == 3 || r.kind == 4 ? fit : 1f);
                switch (r.kind)
                {
                    case 0:
                    {
                        float cap = capD;
                        float tw = L.Measure(r.a, cap, L.Tracking(TextRole.Display));
                        if (tw > textW) cap *= textW / tw;
                        T2(r.a, new Vector2(C.x, y), cap, ink, TextAlign.Center, TextRole.Display);
                        L.Line(new Vector2(x0 + pad, y + cap + 16f * s), new Vector2(x0 + pw - pad, y + cap + 16f * s), lw * 0.7f, A(ink, 0.4f));
                        break;
                    }
                    case 2:
                        T(r.a, new Vector2(x0 + pad, y + 8f * s), TextRole.Label, dim);
                        break;
                    case 3:
                    {
                        var kc = new Vector2(x0 + pad + 6f * s, y + capB * 0.5f);
                        float kw = Mathf.Max(24f * s, L.Measure(r.a, TextRole.Label) + 14f * s);
                        var half = new Vector2(kw * 0.5f, capL * 0.5f + 6f * s);
                        var kcc = new Vector2(x0 + pad + kw * 0.5f, y + capB * 0.5f + 1f * s);
                        L.Rect(kcc - half, kcc + half, lw * 0.8f, A(ink, 0.8f));
                        T(r.a, new Vector2(kcc.x, kcc.y - capL * 0.5f), TextRole.Label, ink, TextAlign.Center);
                        float ly = y;
                        foreach (var ln in r.b.Split('\n')) { T(ln, new Vector2(x0 + pad + keyW, ly), TextRole.Body, ink); ly += (capB + 10f * s) * fit; }
                        _ = kc;
                        break;
                    }
                    case 4:
                    {
                        float ly = y;
                        foreach (var ln in r.a.Split('\n')) { T(ln, new Vector2(x0 + pad, ly), TextRole.Body, ink); ly += (capB + 11f * s) * fit; }
                        break;
                    }
                }
                y += h;
            }
        }

        float T2(string text, Vector2 pos, float cap, Color col, TextAlign align, TextRole role) => L.Text(text, pos, cap, col, align, L.Tracking(role), role);
    }
}
