using Monolith.Hud;
using UnityEngine;

namespace Monolith.HudUi
{
    public sealed partial class HudRenderer
    {
        /// <summary>
        /// The start screen (<see cref="HudFrame.Menu"/>), drawn with the HUD's own parts so it reads as the same tech: a plate with corner ticks for each list, tracked headings with a rule (and the
        /// style's glyphs), the cell bars of the gauges for the level, the two main gauges between the lists showing the chosen difficulty and how hard Earth hits you, and the highlighted
        /// row boxed as the target is. Everything is placed from the middle of the screen, so any screen shape gets the same arrangement.
        /// </summary>
        void DrawMenu()
        {
            var m = F.Menu;
            float fade = Mathf.Clamp01(m.Fade);
            if (fade <= 0.003f) return;
            // the whole screen fades in with its contents
            ink = A(ink, fade); dim = A(dim, fade); faint = A(faint, fade); warn = A(warn, fade);
            float capV = Cap(TextRole.Value), capL = Cap(TextRole.Label), capB = Cap(TextRole.Body);

            // ---- the title, with its rule, and the HUD style in the corner
            float capT = Cap(TextRole.Display) * 1.25f;
            float ty = C.y - 496f * s;
            string title = Up(m.Title);
            float trT = L.Tracking(TextRole.Display) + 0.10f;
            L.Text(title, new Vector2(C.x, ty), capT, ink, TextAlign.Center, trT, TextRole.Display);
            float tw = L.Measure(title, capT, trT);
            float ry = ty + capT + 18f * s;
            L.Line(new Vector2(C.x - tw * 0.5f, ry), new Vector2(C.x + tw * 0.5f, ry), lw * 0.8f, A(ink, 0.5f));
            T(Up(m.Subtitle), new Vector2(C.x, ry + 14f * s), TextRole.Label, dim, TextAlign.Center);
            Box("menuTitle", new Vector2(C.x - tw * 0.5f, ty), new Vector2(C.x + tw * 0.5f, ry + 14f * s + capL));
            if (!string.IsNullOrEmpty(m.Corner))
            {
                string corner = Up(m.Corner);
                float cw = L.Measure(corner, TextRole.Label);
                T(corner, new Vector2(C.x + 900f * s, 44f * s), TextRole.Label, dim, TextAlign.Right);
                Box("menuCorner", new Vector2(C.x + 900f * s - cw, 44f * s), new Vector2(C.x + 900f * s, 44f * s + capL));
            }

            // ---- the two lists
            float pw = 530f * s, pad = 24f * s;
            float top = C.y - 340f * s, bottom = C.y + 300f * s;
            float lx0 = C.x - 900f * s, rx1 = C.x + 900f * s, rx0 = rx1 - pw;
            float rowH = capV + 24f * s, headH = capL + 34f * s, quitGap = 14f * s;

            // the scenarios (a long list scrolls to keep the highlighted row in view)
            Plate(new Vector2(lx0, top), new Vector2(lx0 + pw, bottom), ink, 0.55f);
            Box("menuScenarios", new Vector2(lx0, top), new Vector2(lx0 + pw, bottom));
            float headGap = 20f * s;
            float RowHeight(int i) => m.Rows[i].Heading ? headH + (i > 0 ? headGap : 0f) : rowH + (m.Rows[i].Quit ? quitGap : 0f);
            float avail = bottom - top - 2f * pad;
            int start = 0;
            float Span(int a, int b) { float h = 0f; for (int k = a; k <= b; k++) h += RowHeight(k); return h; }
            while (start < m.Selected && Span(start, m.Selected) > avail) start++;
            if (start > 0 && m.Rows[start - 1].Heading) start--;
            float y = top + pad;
            int last = start - 1;
            for (int i = start; i < m.Rows.Count; i++)
            {
                float h = RowHeight(i);
                if (y + h > top + pad + avail + 0.5f) break;
                var row = m.Rows[i];
                last = i;
                if (row.Heading) { float gy = i > start ? headGap : 0f; Heading(Up(row.Label), new Vector2(lx0 + pad, y + gy), pw - 2f * pad); y += h - (i > 0 && i == start ? headGap : 0f); continue; }
                if (row.Quit) { L.Line(new Vector2(lx0 + pad, y + quitGap * 0.45f), new Vector2(lx0 + pw - pad, y + quitGap * 0.45f), lw * 0.6f, A(ink, 0.30f)); y += quitGap; }
                bool sel = i == m.Selected, focus = sel && !m.LevelFocus;
                MenuRow(Up(row.Label), lx0 + pad, lx0 + pw - pad, y, rowH, sel, focus, 1f, capV);
                y += rowH;
            }
            if (start > 0) L.FillTri(new Vector2(lx0 + pw - pad, top + 9f * s), new Vector2(lx0 + pw - pad - 6f * s, top + 17f * s), new Vector2(lx0 + pw - pad + 6f * s, top + 17f * s), A(ink, 0.7f));
            if (last < m.Rows.Count - 1) L.FillTri(new Vector2(lx0 + pw - pad, bottom - 9f * s), new Vector2(lx0 + pw - pad - 6f * s, bottom - 17f * s), new Vector2(lx0 + pw - pad + 6f * s, bottom - 17f * s), A(ink, 0.7f));

            // the difficulty (its cells fill to the level, like the HUD's own bars)
            int n = Mathf.Max(1, m.Levels.Count);
            float rb = top + pad + headH + n * rowH + pad * 0.6f + capL + 8f * s;
            float lvA = m.LevelApplies ? 1f : 0.38f;
            Plate(new Vector2(rx0, top), new Vector2(rx1, rb), ink, 0.55f * (m.LevelApplies ? 1f : 0.8f));
            Box("menuLevels", new Vector2(rx0, top), new Vector2(rx1, rb));
            Heading("DIFFICULTY", new Vector2(rx0 + pad, top + pad), pw - 2f * pad);
            y = top + pad + headH;
            float cellW = 11f * s, cellGap = 3f * s, cellH = capL * 0.95f;
            for (int k = 0; k < m.Levels.Count; k++)
            {
                bool sel = m.LevelApplies && k == m.Level, focus = sel && m.LevelFocus;
                MenuRow(Up(m.Levels[k]), rx0 + pad, rx1 - pad, y, rowH, sel, focus, lvA, capV, n * (cellW + cellGap) + 16f * s);
                float cx = rx1 - pad - n * (cellW + cellGap) + cellGap;
                for (int c = 0; c < n; c++)
                {
                    var a = new Vector2(cx + c * (cellW + cellGap), y + (rowH - cellH) * 0.5f);
                    L.Fill(a, a + new Vector2(cellW, cellH), c <= k ? A(sel ? ink : dim, lvA) : A(faint, lvA * 0.8f));
                }
                y += rowH;
            }
            T(m.LevelApplies ? "A MISSION'S SKILL LEVEL" : "MISSIONS ONLY", new Vector2(rx0 + pad, y + pad * 0.3f), TextRole.Label, A(dim, m.LevelApplies ? 0.8f : 1f));

            // ---- the two main gauges between the lists: the level, and how hard Earth's guns hit you at it
            var g = Geom();
            float fillL = m.LevelApplies ? (m.Level + 1f) / n : 0f;
            float fillR = m.LevelApplies && m.Level < m.Stat.Count ? Mathf.Clamp01(m.Stat[m.Level]) : 0f;
            Gauge(g, true, fillL, 0f, ink, ink, -1f, false);
            Gauge(g, false, fillR, 0f, ink, ink, -1f, false);
            Box("menuGauges", new Vector2(C.x - g.ROuter - 14f * s, C.y - g.ROuter * Mathf.Sin(g.Half) - 8f * s), new Vector2(C.x + g.ROuter + 14f * s, C.y + g.ROuter * Mathf.Sin(g.Half) + 8f * s));
            string level = m.LevelApplies && m.Level < m.Levels.Count ? Up(m.Levels[m.Level]) : "NO DIFFICULTY";
            T(level, new Vector2(C.x, m.LevelApplies ? C.y - capV - 4f * s : C.y - capV * 0.5f), TextRole.Value, m.LevelApplies ? ink : dim, TextAlign.Center);
            if (m.LevelApplies && m.Level < m.Stat.Count) T(Up(m.StatName) + "  " + Pct(m.Stat[m.Level]), new Vector2(C.x, C.y + 8f * s), TextRole.Label, dim, TextAlign.Center);

            // ---- about the highlighted scenario, and the keys
            float yd = C.y + 296f * s;
            if (!string.IsNullOrEmpty(m.Description))
            {
                var lines = L.Wrap(m.Description, capB, L.Tracking(TextRole.Body), 640f * s);
                int count = Mathf.Min(lines.Count, 4);
                float dy = yd;
                for (int i = 0; i < count; i++)
                {
                    string ln = i == count - 1 && lines.Count > count ? L.Ellipsize(lines[i] + " ...", capB, L.Tracking(TextRole.Body), 640f * s) : lines[i];
                    T(ln, new Vector2(C.x, dy), TextRole.Body, i == 0 ? A(ink, 0.85f) : dim, TextAlign.Center);
                    dy += capB + 10f * s;
                }
                if (count > 0) Box("menuAbout", new Vector2(C.x - 320f * s, yd), new Vector2(C.x + 320f * s, dy));
            }
            if (!string.IsNullOrEmpty(m.Hint))
            {
                float hw = L.Measure(m.Hint, TextRole.Label);
                float hy = C.y + 452f * s;
                T(m.Hint, new Vector2(C.x, hy), TextRole.Label, dim, TextAlign.Center);
                Box("menuHint", new Vector2(C.x - hw * 0.5f, hy), new Vector2(C.x + hw * 0.5f, hy + capL));
            }
        }

        /// <summary>One selectable row of a list: boxed and marked when highlighted (brighter when the cursor is in this list), the label set in the value face.</summary>
        void MenuRow(string label, float x0, float x1, float y, float rowH, bool selected, bool focus, float alpha, float capV, float reserve = 0f)
        {
            if (selected)
            {
                var a = new Vector2(x0 - 8f * s, y); var b = new Vector2(x1 + 8f * s, y + rowH);
                L.Fill(a, b, A(ink, (focus ? 0.34f : 0.16f) * alpha));
                L.Rect(a, b, lw * 0.7f, A(ink, (focus ? 0.9f : 0.4f) * alpha));
                float my = y + rowH * 0.5f, mh = 6.5f * s;
                L.FillTri(new Vector2(x0 + 17f * s, my), new Vector2(x0 + 6f * s, my - mh), new Vector2(x0 + 6f * s, my + mh), A(ink, (focus ? 1f : 0.6f) * alpha));
            }
            label = L.Ellipsize(label, capV, L.Tracking(TextRole.Value), x1 - x0 - 34f * s - reserve);
            var col = selected ? A(ink, (focus ? 1f : 0.85f) * alpha) : A(dim, 0.95f * alpha);
            T(label, new Vector2(x0 + 28f * s, y + (rowH - capV) * 0.5f), TextRole.Value, col);
        }
    }
}
