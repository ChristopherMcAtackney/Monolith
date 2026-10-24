using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Monolith.Core;
using Monolith.Hud;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.CombatHud
{
    /// <summary>
    /// A plain, working combat HUD for playtesting (the rough alien-tech one is a later piece, so this is kept small and every
    /// colour, size and position is a constant at the top of <see cref="Draw"/> to restyle). It reads only <see cref="GameWorld"/>:
    /// the player's <see cref="ShipCombat"/> and the target's. Lead pip, target bracket and readout (class, name, hull and shield,
    /// distance, the targeted part), lock progress, incoming-missile and lock warnings, shield, hull and weapon-energy bars, the
    /// power choice, ammunition, damage you feel, the quick menu, and short notices. Drawn after post-processing like the flight
    /// HUD (see <see cref="FlightHud"/>), with the same line shader, so captures show it.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public sealed class CombatHud : MonoBehaviour
    {
        public static readonly Color Ink = FlightHud.Ink;
        public static readonly Color Alert = new Color(1.0f, 0.28f, 0.12f, 0.96f);

        GameWorld _world;
        Camera _cam;
        HudLines _lines;
        CombatAudio _audio;
        readonly List<IDisposable> _subs = new List<IDisposable>();
        readonly Notice[] _notices = new Notice[6];
        int _noticeNext;
        float _damageFlash;

        struct Notice { public string Text; public float Born; public bool Alert; }

        // Radio lines (radio_line events: wingmen acknowledging orders, the crews, Earth), top centre, newest last.
        readonly Notice[] _radio = new Notice[3];
        int _radioNext;
        string _lastOrder;

        /// <summary>Lines at the top left under the class name (the range's score, hints).</summary>
        public string[] Info;
        /// <summary>Shown big in the middle (ship lost, paused...). Null for none.</summary>
        public string Banner, BannerSub;
        /// <summary>The 1080p-relative scale of everything, for restyling.</summary>
        public float Scale = 1f;

        public static CombatHud Create(GameWorld world, Camera camera, Transform parent, bool audio = true)
        {
            var go = new GameObject("CombatHud");
            go.transform.SetParent(parent, false);
            var hud = go.AddComponent<CombatHud>();
            hud._world = world;
            hud._cam = camera;
            hud._lines = new HudLines();

            var shader = Resources.Load<Shader>(FlightHud.ShaderPath) ?? throw new FileNotFoundException($"Resources/{FlightHud.ShaderPath}.shader is missing");
            var volume = go.AddComponent<CustomPassVolume>();
            volume.isGlobal = true;
            volume.injectionPoint = CustomPassInjectionPoint.AfterPostProcess;
            volume.priority = -1f;       // after the flight HUD's pass
            volume.customPasses.Add(new CombatPass
            {
                name = "MN Combat HUD",
                Material = new Material(shader) { name = "MN_CombatHudLines" },
                Lines = hud._lines,
                Camera = camera,
            });
            if (audio) hud._audio = CombatAudio.Create(go.transform);

            hud._subs.Add(GameEvents.SubscribeAll(hud.OnEvent));
            return hud;
        }

        void OnDestroy()
        {
            foreach (var s in _subs) s.Dispose();
            _subs.Clear();
        }

        // ------------------------------------------------------------------------------------------------ notices

        void Say(string text, bool alert = false)
        {
            _notices[_noticeNext % _notices.Length] = new Notice { Text = text, Born = Time.unscaledTime, Alert = alert };
            _noticeNext++;
        }

        void OnEvent(GameEvent e)
        {
            int me = _world.LocalPlayerShip;
            if (me == GameWorld.NoShip) return;
            switch (e.Id)
            {
                case CombatEvents.SubsystemDestroyed:
                    if (e.Entity == me) Say(PartLabel(e.Subject) + " DESTROYED", true);
                    else if (e.Other == me) Say("TARGET " + PartLabel(e.Subject) + " DESTROYED");
                    break;
                case CombatEvents.PartDamaged:
                    if (e.Entity == me) Say(PartLabel(e.Subject) + " DAMAGED", true);
                    break;
                case CombatEvents.ShieldDown:
                    if (e.Entity == me) { Say("SHIELD DOWN", true); _damageFlash = 1f; }
                    break;
                case CombatEvents.HitHull:
                    if (e.Entity == me) _damageFlash = Mathf.Max(_damageFlash, 0.6f);
                    break;
                case CombatEvents.ShipDestroyed:
                    if (e.Other == me && e.Entity != me) Say("DESTROYED " + (_world.Ships.Get(e.Entity)?.Callsign ?? "TARGET"));
                    break;
                case CombatEvents.LockAcquired:
                    if (e.Entity == me) Say("LOCK ACQUIRED");
                    break;
                case CombatEvents.LockLost:
                    if (e.Entity == me) Say("LOCK LOST");
                    break;
                case CombatEvents.MissileWarning:
                    if (e.Entity == me) Say("MISSILE INBOUND", true);
                    break;
                case CombatEvents.DecoySuccess:
                    if (e.Entity == me) Say("DECOY HIT");
                    break;
                case CombatEvents.ShotDown:
                    if (e.Other == me) Say("TORPEDO SHOT DOWN");
                    break;
                case CombatEvents.PowerChanged:
                    if (e.Entity == me) Say("POWER " + (e.Subject ?? "").ToUpperInvariant());
                    break;
                case CombatEvents.Collision:
                    if (e.Entity == me || e.Other == me) Say("COLLISION", true);
                    break;
                case CombatEvents.RadioLine:
                    _radio[_radioNext % _radio.Length] = new Notice { Text = e.Subject, Born = Time.unscaledTime, Alert = e.Value > 0f };
                    _radioNext++;
                    break;
                case CombatEvents.WingOrder:
                    if (e.Entity == me) _lastOrder = (e.Subject ?? "").ToUpperInvariant();
                    break;
            }
        }

        // ------------------------------------------------------------------------------------------------ the frame

        void LateUpdate()
        {
            _lines.Clear();
            if (_cam != null)
            {
                Draw(_cam.pixelWidth, _cam.pixelHeight);
                if (_audio != null) _audio.Drive(_world);
            }
            _lines.Commit();
            _damageFlash = Mathf.MoveTowards(_damageFlash, 0f, Time.unscaledDeltaTime * 2.2f);
        }

        void Draw(float w, float h)
        {
            float s = h / 1080f * Scale;
            float lw = Mathf.Max(1.5f, 2f * s);
            var c = new Vector2(w * 0.5f, h * 0.5f);
            var ink = Ink;
            var dim = new Color(ink.r, ink.g, ink.b, ink.a * 0.45f);
            var alert = Alert;
            float blink = Mathf.Repeat(Time.unscaledTime * 3.5f, 1f) < 0.55f ? 1f : 0.25f;
            var L = _lines;

            var me = _world.Combat.Get(_world.LocalPlayerShip);
            if (me == null || me.Ship == null) { DrawBanner(L, c, s, lw, ink, dim); return; }
            var ship = me.Ship;

            if (me.Alive)
            {
                DrawTarget(me, ship, c, w, h, s, lw, ink, dim, alert, blink);
                DrawWarnings(me, ship, c, w, h, s, lw, alert, blink);
                DrawBars(me, w, h, s, lw, ink, dim, alert, blink);
                DrawArms(me, w, h, s, lw, ink, dim, alert, blink);
                DrawDamage(me, s, h, lw, ink, dim, alert, blink);
                DrawQuickMenu(me, c, s, lw, ink, dim);
            }
            else
            {
                Banner = Banner ?? "SHIP LOST";
            }
            DrawNotices(L, s, lw, ink, alert);
            DrawRadio(L, c, s, lw, ink);
            DrawInfo(L, s, lw, dim);
            DrawMission(L, c, s, lw, ink, dim, alert, blink);
            DrawBanner(L, c, s, lw, ink, dim);
            if (_damageFlash > 0.01f) DrawEdge(L, w, h, s, new Color(alert.r, alert.g, alert.b, 0.5f * _damageFlash), _damageFlash);
        }

        // ------------------------------------------------------------------------------------------------ the target

        void DrawTarget(ShipCombat me, ShipState ship, Vector2 c, float w, float h, float s, float lw, Color ink, Color dim, Color alert, float blink)
        {
            var L = _lines;
            var t = _world.Ships.Get(ship.Target);
            if (t == null) return;
            var tc = _world.Combat.Get(t.Id);
            float a = _world.Alpha;
            Vector3 tp = t.RenderPosition(a);
            float dist = Vector3.Distance(tp, _cam.transform.position);
            bool hostile = me.TargetHostile;
            float stat = me.SensorStatic;
            Vector2 jitter = stat > 0.05f ? new Vector2(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f)) * (stat * 14f * s) : Vector2.zero;
            bool blind = stat >= 0.99f;
            var col = hostile ? ink : dim;

            Vector3 sp = _cam.WorldToScreenPoint(tp);
            var p = new Vector2(sp.x, h - sp.y) + jitter;
            bool onScreen = sp.z > 0f && p.x > 0f && p.x < w && p.y > 0f && p.y < h;
            float fov = h * 0.5f / Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float rad = Mathf.Clamp(t.Radius / Mathf.Max(1f, sp.z) * fov * 1.15f, 18f * s, h * 0.42f);

            if (onScreen)
            {
                // The bracket: corners for a hostile, a square-cornered dashed look for a friend (just dimmer, plus FRIEND in the readout).
                float k = rad * 0.45f;
                for (int i = 0; i < 4; i++)
                {
                    float sx = (i & 1) == 0 ? -1f : 1f, sy = (i & 2) == 0 ? -1f : 1f;
                    var corner = p + new Vector2(sx * rad, sy * rad);
                    L.Line(corner, corner - new Vector2(sx * k, 0f), lw, col);
                    L.Line(corner, corner - new Vector2(0f, sy * k), lw, col);
                }
                // The targeted part: a ring on it.
                if (ship.TargetPart >= 0 && tc != null && ship.TargetPart < tc.Parts.Count)
                {
                    var part = tc.Parts[ship.TargetPart];
                    Vector3 pw = t.RenderPosition(a) + t.RenderRotation(a) * part.LocalPosition;
                    Vector3 ps = _cam.WorldToScreenPoint(pw);
                    if (ps.z > 0f)
                    {
                        var pp = new Vector2(ps.x, h - ps.y) + jitter;
                        float pr = Mathf.Max(10f * s, part.Radius / Mathf.Max(1f, ps.z) * fov * 1.3f);
                        L.Arc(pp, pr, 0f, Mathf.PI * 2f, lw, ink, 18);
                        L.Line(pp + new Vector2(-pr - 6f * s, 0f), pp + new Vector2(-pr * 0.5f, 0f), lw, ink);
                        L.Line(pp + new Vector2(pr + 6f * s, 0f), pp + new Vector2(pr * 0.5f, 0f), lw, ink);
                    }
                }
                // Lock: the diamond closes in as the lock builds, then holds.
                DrawLock(me, ship, p, rad, s, lw, ink, alert, blink);
                if (!blind) L.Text(RangeText(dist), new Vector2(p.x + rad + 8f * s, p.y - rad), 12f * s, lw * 0.75f, col);
            }
            else
            {
                Vector3 local = _cam.transform.InverseTransformPoint(tp);
                var dir = new Vector2(local.x, -local.y);
                if (dir.sqrMagnitude < 1e-6f) dir = Vector2.down;
                dir.Normalize();
                float ring = 150f * s;
                var tip = c + dir * (ring + 14f * s);
                var n = new Vector2(-dir.y, dir.x);
                L.Line(tip, c + dir * ring + n * 9f * s, lw, col);
                L.Line(tip, c + dir * ring - n * 9f * s, lw, col);
                if (!blind) L.Text(RangeText(dist), c + dir * (ring + 30f * s) + new Vector2(0f, -6f * s), 12f * s, lw * 0.75f, col, TextAlign.Center);
            }

            // The lead pip for the fast gun.
            if (me.HasLead && !blind)
            {
                Vector3 lp = _cam.WorldToScreenPoint(me.LeadPoint);
                if (lp.z > 0f)
                {
                    var q = new Vector2(lp.x, h - lp.y);
                    float r = 7f * s;
                    L.Line(q + new Vector2(0f, -r), q + new Vector2(r, 0f), lw, ink);
                    L.Line(q + new Vector2(r, 0f), q + new Vector2(0f, r), lw, ink);
                    L.Line(q + new Vector2(0f, r), q + new Vector2(-r, 0f), lw, ink);
                    L.Line(q + new Vector2(-r, 0f), q + new Vector2(0f, -r), lw, ink);
                    L.Fill(q - Vector2.one * (1.2f * s), q + Vector2.one * (1.2f * s), ink);
                }
            }

            // The readout panel, top right: a terse monitor.
            float x = w - 36f * s, y = 40f * s;
            var right = TextAlign.Right;
            L.Text(blind ? "NO SENSORS" : (hostile ? "TARGET" : "FRIEND"), new Vector2(x, y), 12f * s, lw * 0.75f, dim, right);
            if (blind || (stat > 0.05f && UnityEngine.Random.value < stat * 0.7f)) { y += 24f * s; L.Text("....", new Vector2(x, y), 14f * s, lw * 0.8f, dim, right); return; }
            string klass = tc != null ? tc.SizeClass.ToUpperInvariant() : "SHIP";
            y += 22f * s;
            L.Text(klass + "  " + (t.Callsign ?? "").ToUpperInvariant(), new Vector2(x, y), 16f * s, lw, ink, right);
            y += 26f * s;
            L.Text((t.ClassName ?? "").ToUpperInvariant(), new Vector2(x, y), 12f * s, lw * 0.75f, dim, right);
            y += 22f * s;
            if (tc != null)
            {
                string hull = "HULL " + Pct(tc.Hull01);
                string shield = tc.ShieldMax > 0f ? "   SHLD " + Pct(tc.Shield01) : "";
                L.Text(hull + shield, new Vector2(x, y), 14f * s, lw * 0.8f, tc.Hull01 < 0.25f ? alert : ink, right);
                y += 22f * s;
            }
            L.Text(RangeText(dist) + "   " + Mathf.RoundToInt(Mathf.Max(0f, t.ForwardSpeed)).ToString(CultureInfo.InvariantCulture) + " M/S", new Vector2(x, y), 14f * s, lw * 0.8f, ink, right);
            y += 22f * s;
            if (ship.TargetPart >= 0 && tc != null && ship.TargetPart < tc.Parts.Count)
            {
                var part = tc.Parts[ship.TargetPart];
                L.Text("> " + PartLabel(part.Name) + "  " + Pct(part.Health01), new Vector2(x, y), 14f * s, lw * 0.8f, ink, right);
            }
            else if (tc != null && tc.Parts.Count > 0) L.Text("TAP Y: SUBSYSTEMS", new Vector2(x, y), 11f * s, lw * 0.7f, dim, right);
        }

        void DrawLock(ShipCombat me, ShipState ship, Vector2 p, float rad, float s, float lw, Color ink, Color alert, float blink)
        {
            var L = _lines;
            var slot = me.SelectedSlot;
            if (slot == null || me.Lock == LockPhase.None && me.LockProgress01 <= 0f) return;
            var def = WeaponCatalog.Get(slot.Weapon);
            if (def == null) return;
            if (!def.NeedsLock)
            {
                if (me.Lock == LockPhase.Locked) L.Text("SEEK", new Vector2(p.x, p.y + rad + 6f * s), 12f * s, lw * 0.8f, ink, TextAlign.Center);
                return;
            }
            float prog = me.LockProgress01;
            float r = Mathf.Lerp(rad * 2.1f, rad * 1.15f, prog);
            bool locked = me.Lock == LockPhase.Locked;
            var col = locked ? alert : ink;
            if (!locked || blink > 0.5f)
            {
                L.Line(p + new Vector2(0f, -r), p + new Vector2(r, 0f), lw, col);
                L.Line(p + new Vector2(r, 0f), p + new Vector2(0f, r), lw, col);
                L.Line(p + new Vector2(0f, r), p + new Vector2(-r, 0f), lw, col);
                L.Line(p + new Vector2(-r, 0f), p + new Vector2(0f, -r), lw, col);
            }
            if (!locked) L.Arc(p, rad * 1.5f, -Mathf.PI * 0.5f, -Mathf.PI * 0.5f + prog * Mathf.PI * 2f, lw * 1.2f, ink, 24);
            L.Text(locked ? "LOCKED" : "LOCKING " + Mathf.RoundToInt(prog * 100f).ToString(CultureInfo.InvariantCulture),
                   new Vector2(p.x, p.y + rad + 6f * s), 12f * s, lw * 0.9f, col, TextAlign.Center);
        }

        // ------------------------------------------------------------------------------------------------ warnings

        void DrawWarnings(ShipCombat me, ShipState ship, Vector2 c, float w, float h, float s, float lw, Color alert, float blink)
        {
            var L = _lines;
            float y = 120f * s;
            if (me.IncomingMissiles > 0)
            {
                float rate = me.IncomingEta < 2.5f ? 1f : 0.55f;
                float b = Mathf.Repeat(Time.unscaledTime * (me.IncomingEta < 2.5f ? 8f : 4f), 1f) < 0.6f ? 1f : 0.2f;
                var col = new Color(alert.r, alert.g, alert.b, alert.a * b);
                L.Text("MISSILE", new Vector2(c.x, y), 26f * s, lw * 1.3f, col, TextAlign.Center);
                L.Text(Mathf.RoundToInt(me.IncomingNearest).ToString(CultureInfo.InvariantCulture) + " M   " + (me.IncomingEta < 90f ? me.IncomingEta.ToString("0.0", CultureInfo.InvariantCulture) + " S" : ""),
                       new Vector2(c.x, y + 34f * s), 14f * s, lw * 0.9f, alert, TextAlign.Center);
                // An arrow round the reticle toward the missile.
                Vector3 local = _cam.transform.InverseTransformDirection(me.IncomingDirection);
                var dir = new Vector2(local.x, -local.y);
                if (local.z < 0.9f && dir.sqrMagnitude > 1e-4f)
                {
                    dir.Normalize();
                    var n = new Vector2(-dir.y, dir.x);
                    float ring = 230f * s;
                    var tip = c + dir * (ring + 26f * s);
                    L.Line(tip, c + dir * ring + n * 12f * s, lw * 1.3f, col);
                    L.Line(tip, c + dir * ring - n * 12f * s, lw * 1.3f, col);
                }
                y += 66f * s;
                _ = rate;
            }
            if (me.LockedOnMe) { L.Text("LOCKED ON", new Vector2(c.x, y), 20f * s, lw * 1.2f, new Color(alert.r, alert.g, alert.b, alert.a * blink), TextAlign.Center); y += 30f * s; }
            else if (me.BeingLocked > 0) { L.Text("LOCK WARNING", new Vector2(c.x, y), 16f * s, lw, alert, TextAlign.Center); y += 26f * s; }
        }

        // ------------------------------------------------------------------------------------------------ bars

        void DrawBars(ShipCombat me, float w, float h, float s, float lw, Color ink, Color dim, Color alert, float blink)
        {
            var L = _lines;
            float x = 40f * s, y = h - 200f * s, bw = 230f * s, bh = 12f * s, gap = 26f * s;
            Bar(L, "HULL", me.Hull01, x, y, bw, bh, s, lw, me.Hull01 < 0.3f ? alert : ink, dim, Pct(me.Hull01));
            y += gap;
            Bar(L, "SHLD", me.Shield01, x, y, bw, bh, s, lw, ink, dim, me.ShieldMax > 0f ? Pct(me.Shield01) : "--");
            y += gap;
            bool dry = me.EnergyMax > 0f && me.Energy < 0.15f * me.EnergyMax;
            Bar(L, "ENRG", me.EnergyMax > 0f ? me.Energy / me.EnergyMax : 0f, x, y, bw, bh, s, lw, dry ? alert : ink, dim, me.EnergyMax > 0f ? Pct(me.Energy / me.EnergyMax) : "--");
            y += gap + 4f * s;

            // The power choice: four marks, the chosen one boxed.
            string[] names = { "BAL", "SHD", "GUN", "ENG" };
            L.Text("PWR", new Vector2(x, y), 12f * s, lw * 0.75f, dim);
            float px = x + 60f * s;
            for (int i = 0; i < 4; i++)
            {
                bool on = (int)me.Power == i;
                var col = on ? ink : dim;
                L.Text(names[i], new Vector2(px + i * 50f * s, y), 12f * s, lw * 0.8f, col);
                if (on) L.Rect(new Vector2(px + i * 50f * s - 5f * s, y - 5f * s), new Vector2(px + i * 50f * s + 30f * s, y + 18f * s), lw * 0.8f, ink);
            }
        }

        static void Bar(HudLines L, string label, float v, float x, float y, float bw, float bh, float s, float lw, Color ink, Color dim, string value)
        {
            L.Text(label, new Vector2(x, y), 12f * s, lw * 0.75f, dim);
            float bx = x + 60f * s;
            L.Rect(new Vector2(bx, y - 1f * s), new Vector2(bx + bw, y + bh), lw * 0.75f, dim);
            v = Mathf.Clamp01(v);
            if (v > 0.003f) L.Fill(new Vector2(bx + 2f * s, y + 1f * s), new Vector2(bx + 2f * s + (bw - 4f * s) * v, y + bh - 2f * s), ink);
            L.Text(value, new Vector2(bx + bw + 10f * s, y), 12f * s, lw * 0.8f, ink);
        }

        // ------------------------------------------------------------------------------------------------ guns and missiles

        void DrawArms(ShipCombat me, float w, float h, float s, float lw, Color ink, Color dim, Color alert, float blink)
        {
            var L = _lines;
            float x = w - 40f * s, y = h - 250f * s;
            var right = TextAlign.Right;
            string pg = me.PrimaryGun != null ? me.PrimaryName : "--";
            string hg = me.HeavyGun != null ? me.HeavyName : "--";
            L.Text("GUN  " + pg.ToUpperInvariant() + (me.PrimaryGun != null && !me.PrimaryOnline ? "  OFFLINE" : ""), new Vector2(x, y), 14f * s, lw * 0.85f, me.PrimaryGun != null && !me.PrimaryOnline ? alert : ink, right);
            y += 24f * s;
            if (me.HeavyGun != null)
            {
                L.Text("HVY  " + hg.ToUpperInvariant() + (!me.HeavyOnline ? "  OFFLINE" : ""), new Vector2(x, y), 14f * s, lw * 0.85f, !me.HeavyOnline ? alert : ink, right);
                y += 24f * s;
            }
            for (int i = 0; i < me.Missiles.Count; i++)
            {
                var m = me.Missiles[i];
                bool sel = i == me.SelectedMissile;
                var col = m.Ammo <= 0 ? dim : ink;
                string line = (sel ? "> " : "  ") + m.Name.ToUpperInvariant() + "  " + m.Ammo.ToString(CultureInfo.InvariantCulture);
                if (sel && (me.Flags & CombatFlags.LaunchersOffline) != 0) line += "  OFFLINE";
                float wd = L.Text(line, new Vector2(x, y), sel ? 16f * s : 13f * s, lw * (sel ? 1f : 0.8f), col, right);
                y += (sel ? 26f : 22f) * s;
                // Reload: a thin line under the selected one while it is cooling.
                if (sel && _world.Time < m.ReadyAt)
                {
                    var def = WeaponCatalog.Get(m.Weapon);
                    if (def != null && def.FireWait > 0.01f)
                    {
                        float f = Mathf.Clamp01(1f - (m.ReadyAt - _world.Time) / def.FireWait);
                        L.Line(new Vector2(x - wd, y - 4f * s), new Vector2(x - wd + wd * f, y - 4f * s), lw * 0.8f, dim);
                    }
                }
            }
            L.Text("DECOYS  " + me.Decoys.ToString(CultureInfo.InvariantCulture), new Vector2(x, y + 4f * s), 13f * s, lw * 0.8f, me.Decoys > 0 ? ink : dim, right);
        }

        // ------------------------------------------------------------------------------------------------ damage you feel

        void DrawDamage(ShipCombat me, float s, float h, float lw, Color ink, Color dim, Color alert, float blink)
        {
            var L = _lines;
            float x = 40f * s, y = h - 330f * s;
            void Line(string text, bool bad)
            {
                L.Text(text, new Vector2(x, y), 13f * s, lw * 0.85f, bad ? new Color(alert.r, alert.g, alert.b, alert.a * (blink > 0.5f ? 1f : 0.55f)) : ink);
                y -= 22f * s;
            }
            var f = me.Flags;
            if (me.SensorStatic > 0.02f) Line(me.SensorStatic >= 0.99f ? "SENSORS OFF" : "SENSORS " + Pct(1f - me.SensorStatic), me.SensorStatic >= 0.99f);
            if ((f & CombatFlags.EnginesDown) != 0) Line("ENGINES OFF", true);
            else if ((f & CombatFlags.EnginesCrippled) != 0) Line("ENGINES " + Pct(me.EngineHealth01) + "  MAX " + Pct(me.Ship.SpeedScale), false);
            if ((f & CombatFlags.PrimaryOffline) != 0) Line("GUN MOUNT LOST", true);
            if ((f & CombatFlags.HeavyOffline) != 0) Line("HEAVY MOUNT LOST", true);
            if ((f & CombatFlags.LaunchersOffline) != 0) Line("LAUNCHER LOST", true);
            if ((f & CombatFlags.ReactorDown) != 0) Line("REACTOR OFF", true);
            if (me.SelfHeals && me.TimeSinceDamage > 6f && (me.Hull01 < 0.999f)) Line("MENDING", false);
        }

        // ------------------------------------------------------------------------------------------------ quick menu

        void DrawQuickMenu(ShipCombat me, Vector2 c, float s, float lw, Color ink, Color dim)
        {
            if (!_world.Pilots.Current(me.Id).Held(ControlButtons.Orders)) return;
            var L = _lines;
            float x = c.x - 240f * s, y = c.y + 100f * s, ww = 480f * s, hh = 170f * s;
            L.Rect(new Vector2(x, y), new Vector2(x + ww, y + hh), lw * 0.9f, ink);
            L.Text("POWER", new Vector2(x + 16f * s, y + 14f * s), 14f * s, lw, ink);
            string[] pw = { "D-PAD UP    GUNS", "D-PAD LEFT  SHIELDS", "D-PAD RIGHT ENGINES", "D-PAD DOWN  BALANCED" };
            PowerMode[] modes = { PowerMode.Guns, PowerMode.Shields, PowerMode.Engines, PowerMode.Balanced };
            for (int i = 0; i < 4; i++)
                L.Text((me.Power == modes[i] ? "> " : "  ") + pw[i], new Vector2(x + 16f * s, y + (42f + i * 26f) * s), 13f * s, lw * 0.85f, me.Power == modes[i] ? ink : dim);
            L.Text("WING", new Vector2(x + 260f * s, y + 14f * s), 14f * s, lw, ink);
            string[] od = { "A  ATTACK MY TARGET", "B  DISABLE MY TARGET", "X  COVER ME", "LB FORM UP" };
            string[] key = { "ATTACK", "DISABLE", "COVER", "FORM" };
            for (int i = 0; i < 4; i++)
                L.Text((_lastOrder == key[i] ? "> " : "") + od[i], new Vector2(x + 260f * s, y + (42f + i * 26f) * s), 12f * s, lw * 0.75f, _lastOrder == key[i] ? ink : dim);
        }

        // ------------------------------------------------------------------------------------------------ text bits

        void DrawNotices(HudLines L, float s, float lw, Color ink, Color alert)
        {
            float y = 80f * s;
            float now = Time.unscaledTime;
            for (int k = 0; k < _notices.Length; k++)
            {
                int idx = _noticeNext - 1 - k;
                if (idx < 0) break;
                var n = _notices[idx % _notices.Length];
                float age = now - n.Born;
                if (n.Text == null || age > 5f) continue;
                float a = Mathf.Clamp01(5f - age) * Mathf.Clamp01(age * 8f + 0.3f);
                var col = n.Alert ? alert : ink;
                L.Text(n.Text, new Vector2(24f * s, y), 13f * s, lw * 0.85f, new Color(col.r, col.g, col.b, col.a * a));
                y += 22f * s;
            }
        }

        void DrawRadio(HudLines L, Vector2 c, float s, float lw, Color ink)
        {
            float now = Time.unscaledTime;
            float y = 34f * s;
            for (int k = _radio.Length - 1; k >= 0; k--)
            {
                int idx = _radioNext - 1 - k;
                if (idx < 0) continue;
                var n = _radio[idx % _radio.Length];
                float age = now - n.Born;
                if (n.Text == null || age > 7f) continue;
                float a = Mathf.Clamp01((7f - age) * 0.8f) * Mathf.Clamp01(age * 8f + 0.3f);
                L.Text(n.Text, new Vector2(c.x, y), 13f * s, lw * 0.85f, new Color(ink.r, ink.g, ink.b, ink.a * a), TextAlign.Center);
                y += 22f * s;
            }
        }

        void DrawInfo(HudLines L, float s, float lw, Color dim)
        {
            if (Info == null) return;
            float y = 240f * s;
            foreach (var line in Info)
            {
                L.Text(line ?? "", new Vector2(24f * s, y), 11f * s, lw * 0.7f, dim);
                y += 18f * s;
            }
        }

        /// <summary>The running mission's directives (what to do now: done ones dim, failed ones in alert) and the cabin's call out of the seat.</summary>
        void DrawMission(HudLines L, Vector2 c, float s, float lw, Color ink, Color dim, Color alert, float blink)
        {
            var m = _world.Mission;
            if (m == null) return;
            float y = 320f * s;
            if (m.Directives.Count > 0 && !m.Ended)
            {
                L.Text("DIRECTIVES", new Vector2(24f * s, y), 11f * s, lw * 0.7f, dim);
                y += 20f * s;
                foreach (var d in m.Directives)
                {
                    string text = (d.Text ?? "").ToUpperInvariant() + (d.State == MissionBoard.State.Done ? "  DONE" : d.State == MissionBoard.State.Failed ? "  FAILED" : "");
                    var col = d.State == MissionBoard.State.Done ? dim : d.State == MissionBoard.State.Failed ? alert : ink;
                    L.Text(text, new Vector2(24f * s, y), 13f * s, lw * 0.85f, col);
                    y += 21f * s;
                }
            }
            if (!string.IsNullOrEmpty(m.Prompt))
                L.Text("[ " + m.Prompt.ToUpperInvariant() + " ]", new Vector2(c.x, c.y + 170f * s), 15f * s, lw, new Color(ink.r, ink.g, ink.b, ink.a * (0.45f + 0.55f * blink)), TextAlign.Center);
        }

        void DrawBanner(HudLines L, Vector2 c, float s, float lw, Color ink, Color dim)
        {
            if (string.IsNullOrEmpty(Banner)) return;
            L.Text(Banner, new Vector2(c.x, c.y - 120f * s), 40f * s, lw * 1.6f, ink, TextAlign.Center);
            if (!string.IsNullOrEmpty(BannerSub)) L.Text(BannerSub, new Vector2(c.x, c.y - 56f * s), 14f * s, lw * 0.85f, dim, TextAlign.Center);
        }

        static void DrawEdge(HudLines L, float w, float h, float s, Color col, float k)
        {
            float t = 10f * s * (0.5f + k);
            L.Line(new Vector2(0f, t * 0.5f), new Vector2(w, t * 0.5f), t, col);
            L.Line(new Vector2(0f, h - t * 0.5f), new Vector2(w, h - t * 0.5f), t, col);
            L.Line(new Vector2(t * 0.5f, 0f), new Vector2(t * 0.5f, h), t, col);
            L.Line(new Vector2(w - t * 0.5f, 0f), new Vector2(w - t * 0.5f, h), t, col);
        }

        // ------------------------------------------------------------------------------------------------ formatting

        static string Pct(float v) => Mathf.RoundToInt(Mathf.Clamp01(v) * 100f).ToString(CultureInfo.InvariantCulture) + "%";

        static string RangeText(float dist) =>
            dist >= 1000f ? (dist / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " KM" : Mathf.RoundToInt(dist).ToString(CultureInfo.InvariantCulture) + " M";

        /// <summary>"SUB_flak_P02" becomes "FLAK P02".</summary>
        public static string PartLabel(string name)
        {
            if (string.IsNullOrEmpty(name)) return "PART";
            string n = name.StartsWith("SUB_", StringComparison.Ordinal) ? name.Substring(4) : name;
            return n.Replace('_', ' ').ToUpperInvariant();
        }
    }

    /// <summary>Draws the combat HUD mesh in pixel space on top of the finished frame.</summary>
    sealed class CombatPass : CustomPass
    {
        public Material Material;
        public HudLines Lines;
        public Camera Camera;
        readonly MaterialPropertyBlock _props = new MaterialPropertyBlock();
        static readonly int MatrixId = Shader.PropertyToID("_MN_Matrix");
        static readonly int ScreenId = Shader.PropertyToID("_MN_Screen");

        protected override bool executeInSceneView => false;

        protected override void Execute(CustomPassContext ctx)
        {
            if (ctx.hdCamera.camera != Camera || Material == null) return;
            float w = ctx.hdCamera.actualWidth, h = ctx.hdCamera.actualHeight;
            float collapse = HudCollapse.Amount;
            if (collapse >= 0.999f) return;
            var m = HudCollapse.Matrix(w, h, collapse);
            CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ClearFlag.None);
            _props.SetMatrix(MatrixId, m);
            _props.SetVector(ScreenId, new Vector4(w, h, 1f / w, 1f / h));
            _props.SetFloat(HudCollapse.DimId, HudCollapse.Dim(collapse));
            ctx.cmd.DrawMesh(Lines.Mesh, Matrix4x4.identity, Material, 0, 2, _props);
        }
    }
}
