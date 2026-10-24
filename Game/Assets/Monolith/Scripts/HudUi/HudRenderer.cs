using System.Collections.Generic;
using System.Globalization;
using Monolith.Core;
using Monolith.Hud;
using UnityEngine;

namespace Monolith.HudUi
{
    /// <summary>
    /// Draws a <see cref="HudFrame"/> on a <see cref="HudLines"/> canvas: the whole HUD, and nothing else reads the game. The layout is FreeSpace 2's, kept spare. Two thick curved
    /// gauges stand either side of the reticle, segments of one circle round it: on the left your speed (the thick band) with the throttle setting beside it (the thin band, on the
    /// same speed scale so the band chases the marker); on the right your weapon energy (thick) with the afterburner's fuel (thin). Their numbers sit outside them. The target is
    /// boxed where it is, with a terse card in the top right (class, name, hull and shield, range, the part, the lock); the radar is bottom centre with the shield and hull as
    /// two short arcs either side of it and the power choice beneath; your guns and missiles are bottom right; the mission's directives, the radio and notices are on the left and
    /// top. Warnings (an incoming missile, damage, a lost system) are the only thing in the accent colour. Everything is set in 1080p pixels times the canvas scale.
    /// </summary>
    public sealed partial class HudRenderer
    {
        /// <summary>The rectangles of the main elements as last drawn, by name (for the layout checks).</summary>
        public readonly Dictionary<string, Rect> Boxes = new Dictionary<string, Rect>();

        HudLines L;
        HudFrame F;
        HudStyle St;
        float s, W, H, lw;
        Vector2 C;
        Color ink, dim, faint, warn;

        // Numbers and their upper-case names are drawn every frame, so they are made once.
        static readonly string[] Ints = new string[4096];
        static readonly string[] Pcts = new string[101];
        static readonly Dictionary<string, string> UpCache = new Dictionary<string, string>();

        static string I(float v)
        {
            int n = Mathf.RoundToInt(v);
            if (n < 0 || n >= Ints.Length) return n.ToString(CultureInfo.InvariantCulture);
            return Ints[n] ??= n.ToString(CultureInfo.InvariantCulture);
        }

        static string Pct(float v)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(v * 100f), 0, 100);
            return Pcts[n] ??= n.ToString(CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>Upper case, remembered (a name is the same every frame).</summary>
        static string Up(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (UpCache.TryGetValue(s, out var u)) return u;
            if (UpCache.Count > 600) UpCache.Clear();
            return UpCache[s] = s.ToUpperInvariant();
        }

        float Blink(float hz, float low = 0.4f) => Mathf.Repeat(F.Time * hz, 1f) < 0.58f ? 1f : low;
        Color A(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        float T(string text, Vector2 pos, TextRole role, Color col, TextAlign align = TextAlign.Left, float trackScale = 1f) => L.Type(text, pos, role, col, align, trackScale);
        float Cap(TextRole r) => L.Cap(r);

        void Box(string name, Vector2 min, Vector2 max) => Boxes[name] = Rect.MinMaxRect(min.x, min.y, max.x, max.y);

        /// <summary>The scale a screen of this shape gets (1 at 1920 x 1080): by height on a wide screen, by width on a narrow one, so the layout always fits.</summary>
        public static float ScaleFor(float width, float height) => Mathf.Min(height / 1080f, width / 1920f);

        /// <summary>Per-part timings (ticks), kept only when a check asks for them.</summary>
        public Dictionary<string, long> Timings;

        void Prof(string name, System.Action part)
        {
            if (Timings == null) { part(); return; }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            part();
            Timings.TryGetValue(name, out long v);
            Timings[name] = v + System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        }

        /// <summary>Draw one frame. The canvas must have been cleared with the scale (screen height over 1080) and the style.</summary>
        public void Draw(HudLines canvas, HudFrame frame)
        {
            L = canvas; F = frame; St = canvas.Style;
            s = canvas.Scale; W = frame.Width; H = frame.Height; C = new Vector2(W * 0.5f, H * 0.5f);
            lw = L.Lw();
            ink = L.Ink; dim = L.Dim; faint = L.Faint; warn = L.Warn;
            Boxes.Clear();

            bool screen = F.Overlay != null && F.Overlay.Length > 0;       // a pause, briefing or debrief screen: the flight HUD steps back
            if (screen) { }
            else if (F.HasShip && F.Alive)
            {
                if (F.MapOpen) Prof("DrawMapScreen", DrawMapScreen);
                else
                {
                    Prof("DrawReticle", DrawReticle);
                    Prof("DrawTarget", DrawTarget);
                    Prof("DrawGauges", DrawGauges);
                    Prof("DrawRadarBlock", DrawRadarBlock);
                    Prof("DrawArms", DrawArms);
                    Prof("DrawTargetCard", DrawTargetCard);
                    Prof("DrawSystems", DrawSystems);
                    Prof("DrawQuickMenu", DrawQuickMenu);
                }
                Prof("DrawThreats", DrawThreats);
                Prof("DrawHeader", DrawHeader);
                Prof("DrawDirectives", DrawDirectives);
                Prof("DrawRadio", DrawRadio);
                Prof("DrawNotices", DrawNotices);
                Prof("DrawPrompt", DrawPrompt);
            }
            else if (F.HasShip) Prof("DrawHeader", DrawHeader);
            Prof("DrawBanner", DrawBanner);
            if (F.DamageFlash > 0.01f) DrawEdge();
            Prof("DrawOverlay", DrawOverlay);
            DrawFooter();
        }

        // ------------------------------------------------------------------------------------------------ the reticle and the lead pip

        void DrawReticle()
        {
            float r = 16f * s;
            var col = A(ink, 0.92f);
            switch (St.Reticle)
            {
                case ReticleKind.Cross:
                    foreach (var d in new[] { Vector2.left, Vector2.right, Vector2.up, Vector2.down })
                        L.Line(C + d * 9f * s, C + d * 22f * s, lw, col);
                    break;
                case ReticleKind.Brackets:
                    L.Brackets(C, r, 8f * s, lw, col);
                    break;
                default:
                    for (int i = 0; i < 4; i++)
                    {
                        float mid = Mathf.PI * 0.25f + i * Mathf.PI * 0.5f;
                        L.Band(C, r, lw, mid - 0.5f, mid + 0.5f, col);
                    }
                    break;
            }
            L.Disc(C, 1.9f * s, col);
            Box("reticle", C - Vector2.one * 24f * s, C + Vector2.one * 24f * s);
        }

        void DrawLead()
        {
            if (!F.HasLead) return;
            var q = F.LeadScreen;
            L.Ring(q, 8.5f * s, lw, ink);
            L.Disc(q, 1.7f * s, ink);
            L.Line(q + new Vector2(0f, -8.5f * s), q + new Vector2(0f, -14f * s), lw * 0.8f, ink);
        }

        // ------------------------------------------------------------------------------------------------ the target in the world

        void DrawTarget()
        {
            if (!F.HasTarget) { DrawLead(); return; }
            float stat = F.SensorStatic;
            bool blind = stat >= 0.99f;
            var jitter = Vector2.zero;
            if (stat > 0.05f)
            {
                uint h = (uint)(Mathf.FloorToInt(F.Time * 20f) * 2654435761u + 77u);
                h ^= h << 13; h ^= h >> 17; h ^= h << 5;
                jitter = new Vector2((h & 255) / 255f - 0.5f, ((h >> 8) & 255) / 255f - 0.5f) * (stat * 28f * s);
            }
            var col = F.TargetHostile ? ink : dim;
            string range = RangeText(F.TargetDist);
            if (F.TargetOnScreen)
            {
                var p = F.TargetScreen + jitter;
                float rad = F.TargetRadiusPx;
                float k = rad * 0.45f;
                L.Brackets(p, rad, k, lw, col);
                if (!F.TargetHostile) { float d = 5f * s; L.Diamond(p + new Vector2(0f, -rad - 10f * s), d, lw * 0.8f, col); }
                if (F.HasPart && F.PartOnScreen)
                {
                    var pp = F.PartScreen + jitter;
                    float pr = F.PartRadiusPx;
                    L.Ring(pp, pr, lw, ink);
                    L.Line(pp + new Vector2(-pr - 8f * s, 0f), pp + new Vector2(-pr * 0.4f, 0f), lw, ink);
                    L.Line(pp + new Vector2(pr + 8f * s, 0f), pp + new Vector2(pr * 0.4f, 0f), lw, ink);
                }
                DrawLockMarks(p, rad);
                if (!blind)
                {
                    // the tag: name over range, at the box's upper right (or inside its upper left when that would leave the screen)
                    string name = Up(F.TargetName ?? "");
                    name = L.Ellipsize(name, Cap(TextRole.Value), L.Tracking(TextRole.Value), 300f * s);
                    float nw = Mathf.Max(L.Measure(name, TextRole.Value), L.Measure(range, TextRole.Label));
                    float tx = p.x + rad + 18f * s;
                    TextAlign al = TextAlign.Left;
                    if (tx + nw > W - 24f * s) { tx = p.x - rad - 18f * s; al = TextAlign.Right; }
                    float ty = Mathf.Clamp(p.y - rad, 24f * s, H - 80f * s);
                    L.NoRecord = true;      // world-anchored: it goes where the target is, over whatever is there
                    T(name, new Vector2(tx, ty), TextRole.Value, col, al);
                    T(range, new Vector2(tx, ty + Cap(TextRole.Value) + 8f * s), TextRole.Label, col, al);
                    L.NoRecord = false;
                }
            }
            else
            {
                // off screen: an arrow on a ring round the reticle, pointing the way to turn
                var dir = F.TargetDir;
                float ring = 112f * s;
                var tip = C + dir * (ring + 18f * s);
                L.Arrow(tip, dir, 16f * s, col);
                L.Band(C, ring, lw * 0.7f, Mathf.Atan2(dir.y, dir.x) - 0.28f, Mathf.Atan2(dir.y, dir.x) + 0.28f, A(col, 0.55f));
                L.NoRecord = true;
                if (!blind) T(range, C + dir * (ring + 52f * s) + new Vector2(0f, -Cap(TextRole.Label) * 0.5f), TextRole.Label, col, TextAlign.Center);
                L.NoRecord = false;
            }
            if (!blind) DrawLead();
        }

        /// <summary>The lock closing in on the target: a diamond that tightens as the lock builds and holds, with its word beneath.</summary>
        void DrawLockMarks(Vector2 p, float rad)
        {
            if (!F.HasTarget) return;
            if (!F.WeaponNeedsLock)
            {
                if (F.WeaponSeeks && F.Lock == LockPhase.Locked) T("SEEKER LIVE", new Vector2(p.x, p.y + rad + 10f * s), TextRole.Label, ink, TextAlign.Center);
                return;
            }
            if (F.Lock == LockPhase.None && F.LockProgress <= 0f) return;
            bool locked = F.Lock == LockPhase.Locked;
            float prog = Mathf.Clamp01(F.LockProgress);
            float r = Mathf.Lerp(rad * 2.1f, rad * 1.18f, locked ? 1f : prog);
            float flash = locked ? Blink(5f, 0.55f) : 1f;
            var col = A(ink, flash);
            float w = locked ? lw * 1.7f : lw;
            Vector2 t = p + new Vector2(0f, -r), rt = p + new Vector2(r, 0f), b = p + new Vector2(0f, r), l = p + new Vector2(-r, 0f);
            L.Line(t, rt, w, col); L.Line(rt, b, w, col); L.Line(b, l, w, col); L.Line(l, t, w, col);
            if (!locked) L.Band(p, rad * 1.5f, lw * 1.3f, -Mathf.PI * 0.5f, -Mathf.PI * 0.5f + prog * Mathf.PI * 2f, ink);
            // the word sits on a plate: dark letters on the ink when the lock is made
            L.NoRecord = true;
            string word = locked ? "LOCKED" : "LOCKING " + Pct(prog);
            float cap = Cap(TextRole.Label) * 1.1f;
            float tw = L.Measure(word, cap, L.Tracking(TextRole.Label)) + 18f * s;
            var min = new Vector2(p.x - tw * 0.5f, p.y + rad * 1.5f + 12f * s);
            var max = min + new Vector2(tw, cap + 12f * s);
            if (locked) { L.Fill(min, max, A(ink, flash)); L.Text(word, new Vector2(p.x, min.y + 6f * s), cap, HudLines.Shade(0.96f), TextAlign.Center, L.Tracking(TextRole.Label), TextRole.Label); }
            else { L.Fill(min, max, HudLines.Shade(0.55f)); L.Rect(min, max, lw * 0.7f, ink); L.Text(word, new Vector2(p.x, min.y + 6f * s), cap, ink, TextAlign.Center, L.Tracking(TextRole.Label), TextRole.Label); }
            L.NoRecord = false;
        }

        static readonly string[] RangeM = new string[1000], RangeKm = new string[1000];

        static string RangeText(float dist)
        {
            if (dist >= 1000f)
            {
                int d = Mathf.RoundToInt(dist / 100f);              // tenths of a kilometre
                if (d >= 1000) return (d / 10).ToString(CultureInfo.InvariantCulture) + " KM";
                return RangeKm[d] ??= (d / 10).ToString(CultureInfo.InvariantCulture) + "." + (d % 10).ToString(CultureInfo.InvariantCulture) + " KM";
            }
            int m = Mathf.RoundToInt(dist);
            return RangeM[m] ??= m.ToString(CultureInfo.InvariantCulture) + " M";
        }

        // ------------------------------------------------------------------------------------------------ the two main gauges

        /// <summary>The angle (radians, 0 = right, clockwise on screen) a gauge's fill reaches at <paramref name="t"/> (0 empty, 1 full): the left gauge fills from the bottom up the left side, the right one up the right.</summary>
        public static float GaugeAngle(bool left, float half, float t)
        {
            float aBot = left ? Mathf.PI - half : half, aTop = left ? Mathf.PI + half : -half;
            return Mathf.Lerp(aBot, aTop, Mathf.Clamp01(t));
        }

        struct ArcGeom { public float R, T, T2, Gap, Half, RThin, ROuter; }

        ArcGeom Geom()
        {
            var g = new ArcGeom { R = St.ArcR * s, T = St.ArcThick * s, T2 = St.ArcThin * s, Gap = St.ArcGap * s, Half = St.ArcHalfSweep * Mathf.Deg2Rad };
            g.RThin = g.R + g.T * 0.5f + g.Gap + g.T2 * 0.5f;
            g.ROuter = g.RThin + g.T2 * 0.5f;
            return g;
        }

        void DrawGauges()
        {
            var g = Geom();
            // ---- left: speed (thick), the throttle setting (thin) on the same speed scale
            float speed01 = Mathf.Clamp01(F.Speed / F.BoostSpeed);
            float cruise01 = Mathf.Clamp01(F.TopSpeed / F.BoostSpeed);
            float thr01 = Mathf.Clamp01(F.Throttle * F.TopSpeed / F.BoostSpeed);
            var speedCol = F.EnginesDown ? warn : ink;
            Gauge(g, true, speed01, thr01, speedCol, ink, cruise01);
            // numbers outside it
            float nx = C.x - (g.ROuter + 34f * s);
            float dCap = Cap(TextRole.Display);
            float top = C.y - dCap * 0.5f - 18f * s;
            T(I(Mathf.Max(0f, F.Speed)), new Vector2(nx, top), TextRole.Display, speedCol, TextAlign.Right);
            T("M/S  SPEED", new Vector2(nx, top + dCap + 14f * s), TextRole.Label, dim, TextAlign.Right);
            T("THROTTLE  " + Pct(F.Throttle), new Vector2(nx, top + dCap + 14f * s + Cap(TextRole.Label) + 10f * s), TextRole.Label, F.Matching ? ink : dim, TextAlign.Right);
            float statusY = top - Cap(TextRole.Value) - 18f * s;
            float below = top + dCap + 60f * s;
            if (F.BrakeOn)
            {
                // the retro brake: BRAKING while the retros fire (the word pulses), BRAKE ON once stopped, and the throttle a second click gives back
                T(F.Braking ? "BRAKING" : "BRAKE ON", new Vector2(nx, statusY), TextRole.Value, F.Braking ? A(ink, Blink(7f, 0.65f)) : ink, TextAlign.Right);
                float yRes = top + dCap + 14f * s + (Cap(TextRole.Label) + 10f * s) * 2f;
                T("RESUME  " + Pct(F.BrakeResume), new Vector2(nx, yRes), TextRole.Label, dim, TextAlign.Right);
                below = yRes + Cap(TextRole.Label) + 8f * s;
                DrawBrakeMarks(g, speed01);
            }
            else if (F.Matching) T("MATCHING", new Vector2(nx, statusY), TextRole.Value, ink, TextAlign.Right);
            else if (F.EnginesDown) T("ENGINES OFF", new Vector2(nx, statusY), TextRole.Value, warn, TextAlign.Right);
            Box("speed", new Vector2(nx - 150f * s, top - Cap(TextRole.Value) - 20f * s), new Vector2(nx, below));

            // ---- right: weapon energy (thick), the afterburner's fuel (thin)
            bool lowEnergy = F.HasEnergy && F.Energy01 < 0.15f;
            var enCol = lowEnergy ? A(warn, Blink(4f, 0.55f)) : ink;
            bool lowFuel = F.Fuel01 < 0.18f;
            var fuelCol = lowFuel && F.Burning ? A(warn, Blink(6f, 0.3f)) : ink;
            Gauge(g, false, F.HasEnergy ? F.Energy01 : 0f, F.Fuel01, enCol, fuelCol, -1f);
            float rx = C.x + (g.ROuter + 34f * s);
            T(F.HasEnergy ? I(F.Energy01 * 100f) : "--", new Vector2(rx, top), TextRole.Display, enCol, TextAlign.Left);
            T("WEAPON ENERGY", new Vector2(rx, top + dCap + 14f * s), TextRole.Label, dim, TextAlign.Left);
            T("AFTERBURNER  " + Pct(F.Fuel01), new Vector2(rx, top + dCap + 14f * s + Cap(TextRole.Label) + 10f * s), TextRole.Label, F.Burning ? fuelCol : dim, TextAlign.Left);
            if (F.Burning) T(F.Fuel01 > 0.005f ? "BURNING" : "FUEL OUT", new Vector2(rx, top - Cap(TextRole.Value) - 18f * s), TextRole.Value, F.Fuel01 > 0.005f ? ink : warn, TextAlign.Left);
            else if (lowEnergy) T("ENERGY LOW", new Vector2(rx, top - Cap(TextRole.Value) - 18f * s), TextRole.Value, warn, TextAlign.Left);
            Box("energy", new Vector2(rx, top - Cap(TextRole.Value) - 20f * s), new Vector2(rx + 200f * s, top + dCap + 60f * s));
            Box("arcL", new Vector2(C.x - g.ROuter - 14f * s, C.y - g.ROuter * Mathf.Sin(g.Half) - 8f * s), new Vector2(C.x - g.R * Mathf.Cos(g.Half) * 0.9f, C.y + g.ROuter * Mathf.Sin(g.Half) + 8f * s));
            Box("arcR", new Vector2(C.x + g.R * Mathf.Cos(g.Half) * 0.9f, C.y - g.ROuter * Mathf.Sin(g.Half) - 8f * s), new Vector2(C.x + g.ROuter + 14f * s, C.y + g.ROuter * Mathf.Sin(g.Half) + 8f * s));
        }

        /// <summary>
        /// The brake on the speed gauge: while the retros fire, a row of ticks runs down the thick band from the needle toward zero (they
        /// bunch up as the speed falls), and the thin band shows the throttle a second click gives back as a ghost fill with its own marker.
        /// </summary>
        void DrawBrakeMarks(ArcGeom g, float speed01)
        {
            float resume01 = Mathf.Clamp01(F.BrakeResume * F.TopSpeed / F.BoostSpeed);
            if (resume01 > 0.01f)
            {
                float aBot = GaugeAngle(true, g.Half, 0f), aRes = GaugeAngle(true, g.Half, resume01);
                L.Band(C, g.RThin, g.T2, aBot, aRes, A(ink, 0.3f));
                var d = new Vector2(Mathf.Cos(aRes), Mathf.Sin(aRes));
                float r0 = g.RThin - g.T2 * 0.5f - 5f * s, r1 = g.RThin + g.T2 * 0.5f + 5f * s;
                var tan = new Vector2(-d.y, d.x) * 4f * s;
                L.Line(C + d * r0 + tan, C + d * r1 + tan, lw * 0.9f, ink);          // a bracket, open toward the fill
                L.Line(C + d * r0 - tan, C + d * r1 - tan, lw * 0.9f, ink);
            }
            if (F.Braking && speed01 > 0.02f)
            {
                float phase = Mathf.Repeat(F.Time * 2.4f, 1f);
                for (int k = 0; k < 4; k++)
                {
                    float f = Mathf.Repeat(1f - (k + phase) / 4f, 1f);                  // 1 at the needle, falling toward 0 over each cycle
                    float a = GaugeAngle(true, g.Half, speed01 * f);
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    L.Line(C + d * (g.R - g.T * 0.5f - 3f * s), C + d * (g.R + g.T * 0.5f + 3f * s), lw * 0.9f, A(ink, 0.2f + 0.7f * f));
                }
            }
        }

        /// <summary>One gauge: the thick band (<paramref name="main01"/>) and the thin one beside it (<paramref name="thin01"/>), on the left or the right of the reticle, filling from the bottom up.</summary>
        void Gauge(ArcGeom g, bool left, float main01, float thin01, Color mainCol, Color thinCol, float mark01)
        {
            float aBot = left ? Mathf.PI - g.Half : g.Half, aTop = left ? Mathf.PI + g.Half : -g.Half;
            float At(float t) => Mathf.Lerp(aBot, aTop, Mathf.Clamp01(t));
            var track = faint;
            main01 = Mathf.Clamp01(main01); thin01 = Mathf.Clamp01(thin01);

            if (St.ArcCells > 0)
            {
                // cells, like a battery: the empty ones past the mark (the afterburner's range) are the dimmest
                L.Cells(C, g.R, g.T, aBot, aTop, St.ArcCells, St.ArcCellGap * s, main01, mainCol, track, mark01 > 0f ? mark01 : 2f);
            }
            else if (St.ArcBezel)
            {
                if (mark01 > 0f && mark01 < 1f) { L.Band(C, g.R, g.T, aBot, At(mark01), track); L.Band(C, g.R, g.T, At(mark01), aTop, A(track, 0.5f)); }
                else L.Band(C, g.R, g.T, aBot, aTop, track);
                if (main01 > 0.003f) L.Band(C, g.R, g.T, aBot, At(main01), mainCol);
            }
            else
            {
                // a plate: a faint slab with a hairline either side, the fill set into it
                if (mark01 > 0f && mark01 < 1f) { L.Band(C, g.R, g.T, aBot, At(mark01), A(track, 0.8f)); L.Band(C, g.R, g.T, At(mark01), aTop, A(track, 0.4f)); }
                else L.Band(C, g.R, g.T, aBot, aTop, A(track, 0.8f));
                L.Band(C, g.R - g.T * 0.5f - 1.5f * s, lw * 0.6f, aBot, aTop, A(ink, 0.45f));
                L.Band(C, g.R + g.T * 0.5f + 1.5f * s, lw * 0.6f, aBot, aTop, A(ink, 0.45f));
                if (main01 > 0.003f) L.Band(C, g.R, g.T - 6f * s, aBot, At(main01), mainCol);
            }
            // a needle at the end of the fill reads the value to the pixel
            if (main01 > 0.01f && St.ArcCells == 0)
            {
                float a = At(main01);
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                L.Line(C + d * (g.R - g.T * 0.5f - 4f * s), C + d * (g.R + g.T * 0.5f + 4f * s), lw * 1.3f, mainCol);
            }
            // the thin band
            L.Band(C, g.RThin, g.T2, aBot, aTop, track);
            if (thin01 > 0.003f) L.Band(C, g.RThin, g.T2, aBot, At(thin01), thinCol);
            if (thin01 > 0.01f)
            {
                float a = At(thin01);
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                L.FillTri(C + d * (g.RThin + g.T2 * 0.5f + 1f * s), C + d * (g.RThin + g.T2 * 0.5f + 11f * s) + new Vector2(-d.y, d.x) * 5f * s, C + d * (g.RThin + g.T2 * 0.5f + 11f * s) - new Vector2(-d.y, d.x) * 5f * s, thinCol);
            }
            // the mark: where cruising speed ends and the afterburner's range begins, and the throttle presets on the thin band
            if (mark01 > 0f && mark01 < 1f)
            {
                float a = At(mark01);
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                L.Line(C + d * (g.R - g.T * 0.5f - 7f * s), C + d * (g.RThin + g.T2 * 0.5f + 4f * s), lw * 1.2f, ink);
            }
            if (left)
                foreach (var f in new[] { 1f / 3f, 2f / 3f })
                {
                    float a = At(f * F.TopSpeed / F.BoostSpeed);
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    L.Line(C + d * (g.RThin - g.T2 * 0.5f - 2.5f * s), C + d * (g.RThin + g.T2 * 0.5f + 2.5f * s), lw * 0.7f, A(ink, 0.65f));
                }
            // a fine scale outside (Meridian)
            if (St.ArcBezel)
            {
                for (int i = 0; i <= 40; i++)
                {
                    float a = At(i / 40f);
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    bool major = i % 10 == 0, mid = i % 5 == 0;
                    float r0 = g.ROuter + 6f * s, r1 = r0 + (major ? 11f : mid ? 7f : 4f) * s;
                    L.Line(C + d * r0, C + d * r1, lw * (major ? 0.9f : 0.6f), major ? dim : A(dim, 0.6f));
                }
            }
            // ornament: a row of the aliens' glyphs along the outside (Cipher, a little in Halo), kept faint
            int glyphs = Mathf.RoundToInt(St.Glyphs * 11f);
            if (glyphs > 0)
            {
                float gs = 10f * s;
                for (int i = 0; i < glyphs; i++)
                {
                    float a = At((i + 0.5f) / glyphs);
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    L.Glyph(C + d * (g.ROuter + 15f * s) - new Vector2(gs * 0.4f, gs * 0.5f), gs, (left ? 11 : 29) + i * 5, lw * 0.6f, A(ink, 0.38f));
                }
            }
        }
    }
}
