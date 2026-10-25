using System;
using System.Collections.Generic;
using System.Globalization;
using Monolith.Core;
using Monolith.Hud;
using UnityEngine;

namespace Monolith.Cabin
{
    /// <summary>
    /// The cabin's own displays: where the HUD goes when the pilot gets up. Each display on the model (DISPLAY_*) gets a RenderTexture that line graphics are drawn into with the
    /// same stroke font and shader as the HUD (single-colour, rough, spare) and shows as the emissive of a surface; the material is an instance of an editor-made asset
    /// (MN_CabScreen), so the player build keeps its shader variant. Dash: flight and weapons. Left: the ship's parts and what needs a hand. Right: radio and directives. The plan
    /// table: the briefing, the objectives, the battle map, the debrief. The rack's bays: what is loaded. The breaker, the two repair panels.
    /// </summary>
    public sealed class CabinDisplays : IDisposable
    {
        // The displays share the HUD's style: its typeface, its line and glow, its ink (the plan table and the repair panels in its second tone) and its one warning colour.
        static Color Red => HudStyles.Current.Warn;

        sealed class Disp
        {
            public CabinLayout.DisplayDef Def;
            public RenderTexture RT;
            public HudLines Lines;
            public Material Mat;
            public Renderer Renderer;
            public float NextAt, Period;
            public bool Teal;
            public bool Cleared;
        }

        readonly GameWorld _w;
        readonly CabinSystem _cab;
        readonly Material _line;
        readonly List<Disp> _all = new List<Disp>();
        readonly MaterialPropertyBlock _props = new MaterialPropertyBlock();
        float _nits = 900f, _nitsNow = 900f;
        string _prevState;

        public CabinDisplays(GameWorld world, CabinSystem cab, Transform cabinRoot, Shader lineShader)
        {
            _w = world; _cab = cab;
            _line = new Material(lineShader) { name = "MN_CabinDisplayLines" };
            foreach (var d in cab.Layout.Displays)
            {
                var t = cabinRoot.Find(d.Name);
                if (t == null) { MnLog.Warn($"CABIN display {d.Name} is not in the model"); continue; }
                var r = t.GetComponent<Renderer>();
                var rt = new RenderTexture(d.W, d.H, 0, RenderTextureFormat.ARGB32) { name = d.Name, useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Trilinear, anisoLevel = 8, wrapMode = TextureWrapMode.Clamp };
                rt.Create();
                var mat = r.material;       // an instance of the asset MN_CabScreen
                mat.SetTexture("_EmissiveColorMap", rt);
                bool teal = d.Name.Contains("table") || d.Name.Contains("panel");
                _all.Add(new Disp
                {
                    Def = d, RT = rt, Lines = new HudLines(), Mat = mat, Renderer = r, Teal = teal,
                    Period = d.Name.Contains("table") ? 1f / 24f : 1f / 12f,
                });
            }
        }

        public int Count => _all.Count;

        public void Dispose()
        {
            foreach (var d in _all) { if (d.RT != null) { d.RT.Release(); UnityEngine.Object.Destroy(d.RT); } if (d.Mat != null) UnityEngine.Object.Destroy(d.Mat); d.Lines?.Dispose(); }
            _all.Clear();
            if (_line != null) UnityEngine.Object.Destroy(_line);
        }

        /// <summary>Redraw what is due. <paramref name="lit">0 off, 1 dim, 2 on.</paramref></summary>
        public void Update(float now, int lit, bool visible)
        {
            var l = _cab.Cfg.light;
            float target = (lit == 2 ? l.displayUnits : lit == 1 ? l.displayUnitsDim : 0f) * _cab.Board.NitsPerUnit;
            _nitsNow = Mathf.MoveTowards(_nitsNow, target, Mathf.Max(200f, _cab.Board.NitsPerUnit) * Time.unscaledDeltaTime * 3f);
            foreach (var d in _all)
            {
                d.Mat.SetColor("_EmissiveColor", Color.white * _nitsNow);
                if (!visible) continue;
                if (_nitsNow <= 1f) { if (!d.Cleared) { Clear(d); d.Cleared = true; } continue; }
                d.Cleared = false;
                if (now < d.NextAt) continue;
                d.NextAt = now + d.Period;
                Draw(d, now);
            }
        }

        static void Clear(Disp d)
        {
            var prev = RenderTexture.active;
            Graphics.SetRenderTarget(d.RT);
            GL.Clear(false, true, new Color(0, 0, 0, 0));
            Graphics.SetRenderTarget(prev);
        }

        void Draw(Disp d, float now)
        {
            var L = d.Lines;
            float w = d.Def.W, h = d.Def.H;
            // The display is a small surface seen from a metre or two: its lines and glow are sized by its height, not by a screen's.
            L.GlowScale = 0.45f;
            L.Clear(Mathf.Clamp(h / 300f, 0.9f, 2.6f));
            var style = L.Style;
            var ink = d.Teal ? style.Ink2 : style.Ink;
            float lw = L.Lw();
            string n = d.Def.Name;
            if (n == "DISPLAY_dash") DrawDash(L, w, h, lw, ink, now);
            else if (n == "DISPLAY_left") DrawParts(L, w, h, lw, ink, now);
            else if (n == "DISPLAY_right") DrawComms(L, w, h, lw, ink, now);
            else if (n == "DISPLAY_table") DrawTable(L, w, h, lw, ink, now);
            else if (n.StartsWith("DISPLAY_rack_", StringComparison.Ordinal)) DrawRack(L, w, h, lw, ink, now, n.Substring(13));
            else if (n == "DISPLAY_breaker") DrawBreaker(L, w, h, lw, ink, now);
            else if (n.StartsWith("DISPLAY_panel_", StringComparison.Ordinal)) DrawPanel(L, w, h, lw, ink, now, n.Substring(14));
            L.Commit();
            var prev = RenderTexture.active;
            Graphics.SetRenderTarget(d.RT);
            GL.Clear(false, true, new Color(0, 0, 0, 0));
            _line.SetMatrix("_MN_Matrix", GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(0f, w, h, 0f, -1f, 1f), true));
            _line.SetVector("_MN_Screen", new Vector4(w, h, 1f / w, 1f / h));
            _line.SetFloat("_MN_Dim", 0f);
            L.DrawNow(_line);
            Graphics.SetRenderTarget(prev);
        }

        // ------------------------------------------------------------------------------------------------ small helpers

        static string Pct(float v) => Mathf.RoundToInt(Mathf.Clamp01(v) * 100f).ToString(CultureInfo.InvariantCulture) + "%";
        static string I(float v) => Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture);

        static void Bar(HudLines L, float x, float y, float w, float h, float frac, float lw, Color ink, Color dim)
        {
            L.Rect(new Vector2(x, y), new Vector2(x + w, y + h), lw * 0.8f, dim);
            if (frac > 0.005f) L.Fill(new Vector2(x + 2f, y + 2f), new Vector2(x + 2f + (w - 4f) * Mathf.Clamp01(frac), y + h - 2f), ink);
        }

        /// <summary>Break text into lines no wider than <paramref name="maxWidth"/> pixels at a cap height, in the display's own type.</summary>
        static List<string> WrapW(HudLines L, string text, float cap, float maxWidth) => L.Wrap(text, cap, L.Style.TrackBody, maxWidth);

        static Color Dim(Color c, float k = 0.4f) => new Color(c.r, c.g, c.b, c.a * k);

        // ------------------------------------------------------------------------------------------------ the dash

        void DrawDash(HudLines L, float w, float h, float lw, Color ink, float now)
        {
            var dim = Dim(ink);
            var me = _w.Ships.Get(_w.LocalPlayerShip);
            var c = _w.Combat.Get(_w.LocalPlayerShip);
            L.Rect(new Vector2(3f, 3f), new Vector2(w - 3f, h - 3f), lw * 0.7f, dim);
            if (me == null) return;
            float u = h / 352f;
            // speed
            L.Text(I(Mathf.Max(0f, me.ForwardSpeed)), new Vector2(150f * u, 40f * u), 70f * u, lw * 1.6f, ink, TextAlign.Right);
            L.Text("M/S", new Vector2(165f * u, 98f * u), 16f * u, lw * 0.9f, dim);
            L.Text("THR " + Pct(me.Throttle), new Vector2(30f * u, 150f * u), 20f * u, lw, ink);
            Bar(L, 30f * u, 186f * u, 230f * u, 14f * u, me.ForwardSpeed / Mathf.Max(1f, me.AfterburnerSpeed), lw, ink, dim);
            L.Text(me.AfterburnerOn ? "AB BURN" : "AB " + Pct(me.FuelMax > 0f ? me.Fuel / me.FuelMax : 0f), new Vector2(30f * u, 214f * u), 16f * u, lw * 0.9f, me.AfterburnerOn ? ink : dim);
            L.Text(_cab.Powered ? "POWER ON" : "POWER OFF", new Vector2(30f * u, 250f * u), 16f * u, lw * 0.9f, dim);
            L.Text("HDG " + I(Mathf.Repeat(me.Rotation.eulerAngles.y, 360f)).PadLeft(3, '0'), new Vector2(30f * u, 280f * u), 16f * u, lw * 0.9f, dim);
            // defence
            if (c != null)
            {
                float x = 330f * u;
                L.Text("HULL", new Vector2(x, 40f * u), 18f * u, lw, ink);
                Bar(L, x + 90f * u, 38f * u, 220f * u, 20f * u, c.Hull01, lw, c.Hull01 < 0.3f ? Red : ink, dim);
                L.Text("SHLD", new Vector2(x, 84f * u), 18f * u, lw, ink);
                Bar(L, x + 90f * u, 82f * u, 220f * u, 20f * u, c.Shield01, lw, ink, dim);
                L.Text("ENRG", new Vector2(x, 128f * u), 18f * u, lw, ink);
                Bar(L, x + 90f * u, 126f * u, 220f * u, 20f * u, c.EnergyMax > 0f ? c.Energy / c.EnergyMax : 0f, lw, ink, dim);
                L.Text("PWR " + c.Power.ToString().ToUpperInvariant(), new Vector2(x, 176f * u), 16f * u, lw * 0.9f, dim);
                string arms = "";
                foreach (var m in c.Missiles) arms += (c.SelectedSlot == m ? "> " : "  ") + m.Name.ToUpperInvariant() + " " + m.Ammo + "   ";
                L.Text("DECOYS " + c.Decoys, new Vector2(x, 206f * u), 16f * u, lw * 0.9f, dim);
                int ml = 0;
                foreach (var m in c.Missiles) { L.Text((c.SelectedSlot == m ? "> " : "  ") + m.Name.ToUpperInvariant() + " " + m.Ammo, new Vector2(x, (236f + ml * 24f) * u), 16f * u, lw * 0.9f, c.SelectedSlot == m ? ink : dim); ml++; }
            }
            // the target
            var t = _w.Ships.Get(me.Target);
            float tx = 700f * u;
            L.Text("TARGET", new Vector2(tx, 40f * u), 16f * u, lw * 0.9f, dim);
            if (t != null)
            {
                var tc = _w.Combat.Get(t.Id);
                L.Text((t.Callsign ?? "-").ToUpperInvariant(), new Vector2(tx, 68f * u), 24f * u, lw * 1.1f, ink);
                L.Text((t.ClassName ?? "").ToUpperInvariant(), new Vector2(tx, 106f * u), 14f * u, lw * 0.8f, dim);
                L.Text(BlipMap.RangeText(Vector3.Distance(t.Position, me.Position)), new Vector2(tx, 134f * u), 18f * u, lw, ink);
                if (tc != null) { L.Text("HULL " + Pct(tc.Hull01), new Vector2(tx, 168f * u), 16f * u, lw * 0.9f, ink); }
            }
            else L.Text("NONE", new Vector2(tx, 68f * u), 24f * u, lw * 1.1f, dim);
            var m2 = _w.Mission;
            if (m2 != null && m2.Directives.Count > 0 && !m2.Ended)
            {
                L.Text("DIRECTIVE", new Vector2(tx, 232f * u), 14f * u, lw * 0.8f, dim);
                var d0 = m2.Directives[0];
                var lines = WrapW(L, d0.Text ?? "", 16f * u, w - tx - 24f * u);
                for (int i = 0; i < Mathf.Min(3, lines.Count); i++) L.Text(lines[i], new Vector2(tx, (254f + i * 22f) * u), 16f * u, lw * 0.9f, ink);
            }
        }

        // ------------------------------------------------------------------------------------------------ parts and what needs a hand

        void DrawParts(HudLines L, float w, float h, float lw, Color ink, float now)
        {
            var dim = Dim(ink);
            var c = _w.Combat.Get(_w.LocalPlayerShip);
            float u = w / 512f;
            L.Rect(new Vector2(3f, 3f), new Vector2(w - 3f, h - 3f), lw * 0.7f, dim);
            L.Text("SYSTEMS", new Vector2(28f * u, 26f * u), 26f * u, lw * 1.2f, ink);
            if (c == null) return;
            float y = 90f * u;
            bool blink = Mathf.Repeat(now * 2.5f, 1f) < 0.6f;
            foreach (var p in c.Parts)
            {
                bool lost = p.Destroyed;
                var col = lost ? (blink ? Red : Dim(Red, 0.5f)) : p.Health01 < 0.5f ? ink : dim;
                L.Text(CabinSystem.PartLabel(p.Name), new Vector2(28f * u, y), 18f * u, lw, col);
                Bar(L, 28f * u, y + 28f * u, 300f * u, 12f * u, p.Health01, lw, lost ? Red : ink, dim);
                L.Text(lost ? "LOST" : p.Health01 < 0.5f ? "DAMAGED" : "OK", new Vector2(w - 28f * u, y + 2f * u), 16f * u, lw * 0.9f, col, TextAlign.Right);
                y += 66f * u;
            }
            int lostN = 0;
            foreach (var p in c.Parts) if (p.Destroyed) lostN++;
            if (lostN > 0)
            {
                L.Text("HAND REPAIR", new Vector2(28f * u, h - 96f * u), 20f * u, lw, blink ? Red : Dim(Red, 0.5f));
                L.Text("SEAT THE MODULE AT ITS PANEL", new Vector2(28f * u, h - 62f * u), 13f * u, lw * 0.8f, dim);
            }
            else L.Text("SELF HEALING", new Vector2(28f * u, h - 62f * u), 14f * u, lw * 0.85f, dim);
        }

        // ------------------------------------------------------------------------------------------------ radio and directives

        readonly List<string> _radio = new List<string>();
        IDisposable _radioSub;

        void EnsureRadio()
        {
            if (_radioSub != null) return;
            _radioSub = GameEvents.Subscribe(CombatEvents.RadioLine, e => { _radio.Add(e.Subject ?? ""); if (_radio.Count > 6) _radio.RemoveAt(0); });
        }

        void DrawComms(HudLines L, float w, float h, float lw, Color ink, float now)
        {
            EnsureRadio();
            var dim = Dim(ink);
            float u = w / 512f;
            L.Rect(new Vector2(3f, 3f), new Vector2(w - 3f, h - 3f), lw * 0.7f, dim);
            L.Text("COMMS", new Vector2(28f * u, 26f * u), 26f * u, lw * 1.2f, ink);
            float y = 84f * u;
            for (int i = _radio.Count - 1; i >= 0 && y < h * 0.55f; i--)
            {
                var lines = WrapW(L, _radio[i], 14f * u, w - 56f * u);
                foreach (var ln in lines) { L.Text(ln, new Vector2(28f * u, y), 14f * u, lw * 0.85f, i == _radio.Count - 1 ? ink : dim); y += 21f * u; }
                y += 8f * u;
            }
            var m = _w.Mission;
            float dy = h * 0.60f;
            L.Line(new Vector2(28f * u, dy - 12f * u), new Vector2(w - 28f * u, dy - 12f * u), lw * 0.6f, dim);
            L.Text("DIRECTIVES", new Vector2(28f * u, dy), 16f * u, lw * 0.9f, dim);
            dy += 28f * u;
            if (m != null && !m.Ended)
                foreach (var dr in m.Directives)
                {
                    var lines = WrapW(L, (dr.Text ?? "") + (dr.State == MissionBoard.State.Done ? "  - done" : dr.State == MissionBoard.State.Failed ? "  - failed" : ""), 14f * u, w - 56f * u);
                    foreach (var ln in lines) { L.Text(ln, new Vector2(28f * u, dy), 14f * u, lw * 0.85f, dr.State == MissionBoard.State.Done ? dim : ink); dy += 21f * u; }
                    dy += 6f * u;
                    if (dy > h - 40f * u) break;
                }
        }

        // ------------------------------------------------------------------------------------------------ the plan table

        void DrawTable(HudLines L, float w, float h, float lw, Color ink, float now)
        {
            var dim = Dim(ink);
            float u = w / 1024f;
            var page = _cab.CurrentPage;
            var file = _cab.File;
            L.Rect(new Vector2(4f, 4f), new Vector2(w - 4f, h - 4f), lw * 0.8f, dim);
            L.Text(page.Title ?? "", new Vector2(36f * u, 30f * u), 28f * u, lw * 1.3f, ink);
            if (_cab.TablePages.Count > 1)
            {
                // page marks: small squares, the current one filled
                float px = w - 40f * u;
                for (int i = _cab.TablePages.Count - 1; i >= 0; i--)
                {
                    var a = new Vector2(px - 8f * u, 36f * u); var b = new Vector2(px + 8f * u, 52f * u);
                    if (i == _cab.TablePage) L.Fill(a, b, ink); else L.Rect(a, b, lw * 0.7f, dim);
                    px -= 26f * u;
                }
            }
            L.Line(new Vector2(36f * u, 78f * u), new Vector2(w - 36f * u, 78f * u), lw * 0.7f, dim);
            switch (page.Kind)
            {
                case CabinSystem.TableKind.Briefing:
                    if (file != null && page.Index < file.Briefing.Stages.Count)
                    {
                        var lines = WrapW(L, file.Briefing.Stages[page.Index], 22f * u, w - 96f * u);
                        float y = 120f * u;
                        foreach (var ln in lines) { L.Text(ln, new Vector2(48f * u, y), 22f * u, lw * 1.0f, ink); y += 40f * u; }
                    }
                    L.Text("STAGE " + (page.Index + 1) + " OF " + (file?.Briefing.Stages.Count ?? 0), new Vector2(48f * u, h - 70f * u), 16f * u, lw * 0.9f, dim);
                    break;
                case CabinSystem.TableKind.Question:
                    if (file != null && page.Index < file.Briefing.Questions.Count)
                    {
                        var q = file.Briefing.Questions[page.Index];
                        float y = 120f * u;
                        foreach (var ln in WrapW(L, "> " + q.Key, 20f * u, w - 96f * u)) { L.Text(ln, new Vector2(48f * u, y), 20f * u, lw, dim); y += 34f * u; }
                        y += 18f * u;
                        foreach (var ln in WrapW(L, q.Value, 22f * u, w - 96f * u)) { L.Text(ln, new Vector2(48f * u, y), 22f * u, lw, ink); y += 40f * u; }
                    }
                    break;
                case CabinSystem.TableKind.Objectives:
                {
                    float y = 120f * u;
                    var m = _w.Mission;
                    if (m != null)
                        foreach (var o in m.Objectives)
                        {
                            if (o.Hidden && o.State != MissionBoard.State.Done) continue;
                            var col = o.State == MissionBoard.State.Failed ? Red : o.State == MissionBoard.State.Done ? dim : ink;
                            L.Text(o.Kind.ToUpperInvariant(), new Vector2(48f * u, y), 14f * u, lw * 0.8f, dim);
                            foreach (var ln in WrapW(L, o.Text + (o.State == MissionBoard.State.Done ? "  - done" : o.State == MissionBoard.State.Failed ? "  - failed" : ""), 20f * u, w - 96f * u)) { y += 24f * u; L.Text(ln, new Vector2(48f * u, y), 20f * u, lw, col); y += 14f * u; }
                            y += 26f * u;
                        }
                    else if (file != null)
                        foreach (var o in file.Objectives) { if (o.Hidden) continue; L.Text(o.Kind.ToUpperInvariant() + ": " + o.Text.ToUpperInvariant(), new Vector2(48f * u, y), 18f * u, lw, ink); y += 36f * u; }
                    break;
                }
                case CabinSystem.TableKind.Map:
                    BlipMap.Draw(L, _w, new Vector2(w * 0.5f, h * 0.53f), Mathf.Min(w, h) * 0.40f, lw, ink, now);
                    break;
                case CabinSystem.TableKind.Debrief:
                {
                    float y = 110f * u;
                    var rt = _cab.Runtime;
                    if (rt != null)
                    {
                        L.Text((rt.ResultText ?? "").ToUpperInvariant(), new Vector2(48f * u, y), 34f * u, lw * 1.5f, rt.Result == "defeat" ? Red : ink);
                        y += 76f * u;
                        foreach (var raw in rt.Debrief())
                            foreach (var ln in WrapW(L, raw, 18f * u, w - 96f * u)) { L.Text(ln, new Vector2(48f * u, y), 18f * u, lw * 0.95f, ink); y += 30f * u; }
                    }
                    break;
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ the rack, the breaker, the panels

        void DrawRack(HudLines L, float w, float h, float lw, Color ink, float now, string bay)
        {
            var dim = Dim(ink);
            float u = h / 96f;
            bool gun = bay.StartsWith("gun", StringComparison.Ordinal);
            int slot = bay.EndsWith("1", StringComparison.Ordinal) ? 1 : 0;
            string id = gun ? _cab.Loadout.Guns[slot] : _cab.Loadout.Missiles[slot];
            var def = WeaponCatalog.Get(id);
            var c = _w.Combat.Get(_w.LocalPlayerShip);
            L.Text((gun ? "GUN " : "MSL ") + (slot + 1), new Vector2(12f * u, 10f * u), 22f * u, lw * 1.1f, dim);
            L.Text((def?.Name ?? id).ToUpperInvariant(), new Vector2(w - 12f * u, 8f * u), 32f * u, lw * 1.4f, ink, TextAlign.Right);
            string sub = gun ? (def != null ? (def.FireWait < 0.3f ? "FAST" : "HEAVY") : "") : AmmoOf(c, id);
            L.Text(_cab.RackOpen ? sub : "LOCKED", new Vector2(w - 12f * u, 56f * u), 20f * u, lw, _cab.RackOpen ? dim : Red, TextAlign.Right);
            if (_cab.Action == CabinSystem.Act.Rack && _cab.ActiveItem != null && _cab.ActiveItem.Bay == bay)
                Bar(L, 12f * u, 64f * u, 150f * u, 14f * u, _cab.ActionProgress, lw, ink, dim);
        }

        static string AmmoOf(ShipCombat c, string id)
        {
            if (c == null) return "";
            foreach (var m in c.Missiles) if (m.Weapon == id) return "X " + m.Ammo;
            var def = CombatCatalog.Get(c?.DefId);
            if (def != null) foreach (var m in def.Missiles) if (m.Key == id) return "X " + m.Value;
            return "";
        }

        void DrawBreaker(HudLines L, float w, float h, float lw, Color ink, float now)
        {
            var dim = Dim(ink);
            float u = h / 160f;
            bool on = _cab.Powered;
            L.Text("MAIN", new Vector2(14f * u, 14f * u), 30f * u, lw * 1.3f, dim);
            L.Text(on ? "ON" : "OFF", new Vector2(w - 14f * u, 6f * u), 74f * u, lw * 2.4f, on ? ink : Red, TextAlign.Right);
            if (_cab.Action == CabinSystem.Act.Breaker) Bar(L, 14f * u, 120f * u, w - 28f * u, 20f * u, _cab.ActionProgress, lw, ink, dim);
        }

        void DrawPanel(HudLines L, float w, float h, float lw, Color ink, float now, string panel)
        {
            var dim = Dim(ink);
            float u = h / 128f;
            var c = _w.Combat.Get(_w.LocalPlayerShip);
            string need = null; float prog = 0f;
            if (c != null)
                foreach (var p in c.Parts)
                    if (p.Destroyed && (p.Kind == "engine" || p.Kind == "reactor" ? "engine" : "arms") == panel) { need = p.Name; break; }
            if (need != null) prog = _cab.RepairProgress(need);
            L.Text(panel == "engine" ? "DRIVE" : "ARMS", new Vector2(12f * u, 10f * u), 22f * u, lw * 1.1f, dim);
            if (need == null) { L.Text("SEALED", new Vector2(w - 12f * u, 10f * u), 26f * u, lw * 1.2f, dim, TextAlign.Right); return; }
            bool blink = Mathf.Repeat(now * 3f, 1f) < 0.6f;
            L.Text("MODULE LOST", new Vector2(w - 12f * u, 6f * u), 24f * u, lw * 1.1f, blink ? Red : Dim(Red, 0.5f), TextAlign.Right);
            L.Text(CabinSystem.PartLabel(need), new Vector2(12f * u, 50f * u), 18f * u, lw, ink);
            Bar(L, 12f * u, 88f * u, w - 24f * u, 22f * u, prog, lw, ink, dim);
        }
    }
}
