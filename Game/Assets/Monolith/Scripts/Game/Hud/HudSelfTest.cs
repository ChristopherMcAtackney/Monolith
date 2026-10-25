using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Monolith.Collision;
using Monolith.Core;
using Monolith.Damage;
using Monolith.Flight;
using Monolith.Hud;
using Monolith.HudUi;
using Monolith.Input;
using Monolith.Targeting;
using Monolith.Weapons;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// Headless checks of the HUD: <c>Monolith.exe -batchmode -nographics -mnHudSelfTest [-mnOut dir]</c>. They cover the type (the fonts load, every character the game prints has a glyph,
    /// the type scale is a clear four-step one at a readable size, nothing is set tight), the data interface (a real world read into a <see cref="HudFrame"/>), the layout (every style at
    /// eight screen shapes, with a full frame, a threatened one, the quick menu, the map and a pause screen: nothing leaves the screen, no two panels or two words overlap, the
    /// middle of the screen stays clear), the radar's and the gauges' geometry, the styles themselves, the pause screens' switches, and what a frame costs on the CPU.
    /// Each line logs <c>HUD PASS</c> or <c>HUD FAIL</c> with the numbers; the result goes to hud_selftest.txt when <c>-mnOut</c> is given.
    /// </summary>
    public static class HudSelfTest
    {
        const string Flag = "-mnHudSelfTest";
        static int _checks, _fails;
        static readonly List<string> Lines = new List<string>();

        public static bool Requested => CommandLine.Has(Flag);

        static void Check(bool ok, string what)
        {
            _checks++;
            if (!ok) _fails++;
            string line = $"HUD {(ok ? "PASS" : "FAIL")}: {what}";
            Lines.Add(line);
            MnLog.Info(line);
        }

        static string N(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        static readonly (int w, int h)[] Sizes = { (1280, 720), (1366, 768), (1600, 900), (1920, 1080), (2560, 1440), (3840, 2160), (2560, 1080), (1280, 1024) };

        public static bool Run()
        {
            _checks = 0; _fails = 0; Lines.Clear();
            string was = HudStyles.Current.Name;
            var wasArgs = Environment.GetCommandLineArgs();
            try
            {
                Fonts();
                Scale();
                Styles();
                Geometry();
                DataInterface();
                Layout(1f);
                ScaleAndOpacity();
                StartMenuScreen();
                PauseScreens();
                Cost();
            }
            catch (Exception e)
            {
                _fails++;
                MnLog.Error("HUD selftest threw: " + e);
                Lines.Add("HUD FAIL: threw " + e.Message);
            }
            CommandLine.SetArgs(wasArgs);
            HudStyles.FileOverride = null;
            HudStyles.Reload();
            HudStyles.Set(was);
            string summary = $"HUD SELFTEST {(_fails == 0 ? "OK" : "FAILED")}: {_checks - _fails} of {_checks} checks pass";
            MnLog.Info(summary);
            Lines.Add(summary);
            string dir = CommandLine.Get("-mnOut");
            if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); File.WriteAllLines(Path.Combine(dir, "hud_selftest.txt"), Lines); }
            return _fails == 0;
        }

        // ------------------------------------------------------------------------------------------------ the type

        static void Fonts()
        {
            var all = new List<string>();
            for (char c = ' '; c <= '~'; c++) all.Add(c.ToString());
            foreach (var st in HudStyles.Styles)
            {
                var f = st.Font;
                Check(f.Id == st.FontId && f.GlyphCount >= 95 && f.Size > 0 && f.Spread >= 6, $"{st.Name}: font {f.Family} loads ({f.GlyphCount} glyphs, {f.AtlasW}x{f.AtlasH} atlas, spread {f.Spread}, {f.Licence})");
                int missing = 0;
                foreach (var g in all) if (!f.Has(g[0])) missing++;
                Check(missing == 0, $"{st.Name}: every printable ASCII character has a glyph ({missing} missing)");
                Check(f.Licence != null && f.Licence.Contains("Open Font"), $"{st.Name}: the font is under the SIL Open Font License");
            }
            string lic = RepoPath("docs/licences/fonts.md");
            if (File.Exists(lic)) { string t = File.ReadAllText(lic); Check(t.Contains("Oxanium") && t.Contains("B612") && t.Contains("Jura") && t.Contains("SIL Open Font License"), "docs/licences/fonts.md records each font with its licence"); }
            else MnLog.Info("HUD note: docs/licences/fonts.md not found (not run from the repo); licence record not checked");

            // every character the missions print (directives, messages, briefings, objectives) is in every font
            var chars = new HashSet<char>();
            foreach (var asset in Resources.LoadAll<TextAsset>("Missions"))
            {
                try { Collect(JToken.Parse(asset.text), chars); } catch { }
            }
            foreach (var asset in Resources.LoadAll<TextAsset>("Combat/ships")) { try { Collect(JToken.Parse(asset.text), chars); } catch { } }
            foreach (var st in HudStyles.Styles)
            {
                var miss = new List<char>();
                foreach (var c in chars) if (c >= ' ' && !st.Font.Has(c) && !st.Font.Has(char.ToUpperInvariant(c)) && c != ' ') miss.Add(c);
                Check(miss.Count == 0, $"{st.Name}: every character in the mission and ship files has a glyph ({chars.Count} distinct; missing: {new string(miss.ToArray())})");
            }
        }

        static void Collect(JToken t, HashSet<char> into)
        {
            if (t.Type == JTokenType.String) { foreach (var c in (string)t) into.Add(c); }
            else foreach (var c in t.Children()) Collect(c, into);
        }

        static string RepoPath(string rel)
        {
            string dir = Path.GetDirectoryName(Application.dataPath);
            for (int i = 0; i < 6 && dir != null; i++, dir = Path.GetDirectoryName(dir))
                if (File.Exists(Path.Combine(dir, rel))) return Path.Combine(dir, rel);
            return rel;
        }

        static void Scale()
        {
            var L = new HudLines();
            foreach (var st in HudStyles.Styles)
            {
                L.Clear(1f, st);
                float label = L.Cap(TextRole.Label), body = L.Cap(TextRole.Body), value = L.Cap(TextRole.Value), disp = L.Cap(TextRole.Display);
                var f = st.Font;
                float labelEm = label / f.CapRatio;
                Check(label < body && body < value && value < disp, $"{st.Name}: the type scale steps up (cap heights {N(label)}, {N(body)}, {N(value)}, {N(disp)} px at 1080p)");
                Check(label >= 12.5f && body >= 16f && labelEm >= 17f, $"{st.Name}: the smallest type is readable at 1080p (label cap {N(label)} px, {N(labelEm)} px to the em; body cap {N(body)} px)");
                Check(disp / body >= 2.5f, $"{st.Name}: the big number is at least 2.5 times the running text ({N(disp / body)})");
                // not condensed: a digit is at least as wide as 0.55 of the cap height, and small tracked caps have tracking
                float digit = f.Measure("0", 100f);
                float cap100 = 100f * f.CapRatio;
                Check(digit / cap100 >= 0.62f, $"{st.Name}: the figures are not condensed (a digit is {N(digit / cap100)} of the cap height wide)");
                Check(st.TrackLabel >= 0.10f && st.TrackBody >= 0.02f, $"{st.Name}: the type is tracked, not tight (labels {N(st.TrackLabel)} em, body {N(st.TrackBody)} em)");
                // the glow is restrained
                Check(st.GlowStrength <= 0.45f && st.GlowRadius <= 4f, $"{st.Name}: the glow is restrained (strength {N(st.GlowStrength)}, radius {N(st.GlowRadius)} px)");
            }
            // the small-window floor: type never shrinks below 0.72 of 1080p
            L.Clear(0.4f, HudStyles.Styles[0]);
            Check(L.Cap(TextRole.Label) >= 0.72f * HudStyles.Styles[0].CapLabel - 0.01f, "type holds a floor on a small window");
        }

        static void Styles()
        {
            var names = new HashSet<string>();
            var fonts = new HashSet<string>();
            var inks = new List<Color>();
            foreach (var s in HudStyles.Styles) { names.Add(s.Name); fonts.Add(s.FontId); inks.Add(s.Ink); }
            Check(HudStyles.Styles.Count >= 2 && names.Count == HudStyles.Styles.Count, $"{HudStyles.Styles.Count} distinct HUD styles ({string.Join(", ", HudStyles.Names)})");
            Check(fonts.Count == HudStyles.Styles.Count, "each style has its own typeface");
            bool distinctInk = true;
            for (int i = 0; i < inks.Count; i++) for (int j = i + 1; j < inks.Count; j++) if (Vector4.Distance(inks[i], inks[j]) < 0.25f) distinctInk = false;
            Check(distinctInk, "each style has its own ink");
            Check(HudStyles.Set("meridian") && HudStyles.Current.Name == "meridian" && HudStyles.Set("CIPHER") && HudStyles.Current.Name == "cipher" && !HudStyles.Set("nope") && HudStyles.Current.Name == "cipher", "styles switch by name, ignoring case, and an unknown name changes nothing");
            string a = HudStyles.Current.Name;
            var n1 = HudStyles.Cycle(1, false).Name;
            var n2 = HudStyles.Cycle(-1, false).Name;
            Check(n1 != a && n2 == a, "cycling forward and back returns to the same style");
            HudStyles.Set("halo");
            bool arcsDiffer = HudStyles.Styles[0].ArcR != HudStyles.Styles[1].ArcR && HudStyles.Styles[1].ArcR != HudStyles.Styles[2].ArcR && HudStyles.Styles[0].ArcThick != HudStyles.Styles[2].ArcThick;
            Check(arcsDiffer, "the styles differ in arc radius and thickness");
            foreach (var s in HudStyles.Styles) Check(s.ArcThick >= 12f && s.ArcThick > 1.8f * s.ArcThin, $"{s.Name}: the main bar is a thick band ({N(s.ArcThick)} px) with a thin one beside it ({N(s.ArcThin)} px)");
            var w = new HudStyle { Ink = HudStyle.Srgb(255, 128, 0) };
            Check(Mathf.Abs(w.Ink.g - 0.2158f) < 0.01f, "colours are authored as sRGB and stored linear (the HUD's target is sRGB)");
            // the target readout's range text: every distance reads, including the half metre below a kilometre that its metre cache had no slot for
            var rts = new List<string>();
            try { foreach (var d in new[] { 0f, 999.4f, 999.5f, 999.99f, 1000f, 1049f, 1051f, 99900f, 250000f, -5f, float.NaN }) rts.Add(HudRenderer.RangeText(d)); } catch (Exception e) { rts.Add("threw " + e.GetType().Name); }
            string rt = string.Join("|", rts);
            Check(rt == "0 M|999 M|1.0 KM|1.0 KM|1.0 KM|1.0 KM|1.1 KM|99.9 KM|250 KM|0 M|0 M", "the target range reads at every distance, 999.5 m to 1 km included (it threw there): " + rt);
        }

        // ------------------------------------------------------------------------------------------------ geometry

        static void Geometry()
        {
            float half = 44f * Mathf.Deg2Rad;
            float l0 = HudRenderer.GaugeAngle(true, half, 0f), l1 = HudRenderer.GaugeAngle(true, half, 1f), r0 = HudRenderer.GaugeAngle(false, half, 0f), r1 = HudRenderer.GaugeAngle(false, half, 1f);
            // y is down: angle pi - half (left, below the horizon) up to pi + half (left, above); right: half (below) to -half (above)
            Check(Mathf.Sin(l0) > 0f && Mathf.Sin(l1) < 0f && Mathf.Cos(l0) < 0f && Mathf.Cos(l1) < 0f, "the left gauge fills from the bottom up the left side");
            Check(Mathf.Sin(r0) > 0f && Mathf.Sin(r1) < 0f && Mathf.Cos(r0) > 0f && Mathf.Cos(r1) > 0f, "the right gauge fills from the bottom up the right side");
            Check(Mathf.Abs(Mathf.Abs(l1 - l0) - 2f * half) < 1e-4f && HudRenderer.GaugeAngle(true, half, 0.5f) == Mathf.PI, "a gauge sweeps twice the half angle, half full at the horizon");

            // the radar: up is ahead, a stem rises for a contact above you and drops for one below
            var c = new Vector2(500f, 500f);
            HudRadar.Plot(new Contact { Rel = new Vector3(0f, 0f, 600f) }, c, 120f, 1500f, out var g, out var tip, out bool clamped);
            Check(g.y < c.y && Mathf.Abs(g.x - c.x) < 0.01f && !clamped, "ahead is up the radar");
            HudRadar.Plot(new Contact { Rel = new Vector3(0f, 0f, -600f) }, c, 120f, 1500f, out g, out tip, out clamped);
            Check(g.y > c.y, "behind is down the radar");
            HudRadar.Plot(new Contact { Rel = new Vector3(600f, 0f, 0f) }, c, 120f, 1500f, out g, out tip, out clamped);
            Check(g.x > c.x && Mathf.Abs(g.y - c.y) < 0.01f, "to the right is right on the radar");
            HudRadar.Plot(new Contact { Rel = new Vector3(0f, 400f, 300f) }, c, 120f, 1500f, out g, out tip, out clamped);
            Check(tip.y < g.y, $"a contact above your plane has its stem up ({N(g.y - tip.y)} px)");
            HudRadar.Plot(new Contact { Rel = new Vector3(0f, -400f, 300f) }, c, 120f, 1500f, out g, out tip, out clamped);
            Check(tip.y > g.y, $"a contact below your plane has its stem down ({N(tip.y - g.y)} px)");
            HudRadar.Plot(new Contact { Rel = new Vector3(9000f, 0f, 9000f) }, c, 120f, 1500f, out g, out tip, out clamped);
            float ex = (g.x - c.x) / 120f, ey = (g.y - c.y) / (120f * HudRadar.Squash);
            Check(clamped && ex * ex + ey * ey <= 1f, "a contact beyond range stays on the rim");

            // static: with the sensors gone the radar draws no blips; the same contacts draw some when clear
            var L = new HudLines();
            var contacts = new List<Contact> { new Contact { Rel = new Vector3(200f, 50f, 500f), Hostile = true, Dist = 540f }, new Contact { Rel = new Vector3(-300f, 0f, 200f), Dist = 360f } };
            L.Clear(1f, HudStyles.Styles[0]);
            HudRadar.Draw(L, contacts, c, 120f, 1500f, 0f, 1f);
            int clear = L.VertexCount;
            L.Clear(1f, HudStyles.Styles[0]);
            HudRadar.Draw(L, contacts, c, 120f, 1500f, 1f, 1f);
            Check(clear > 0 && L.VertexCount > 0, "the radar draws clear and in static");
            // the range: picked to hold the nearest hostiles, wider when they are far
            var near = new List<Contact> { new Contact { Hostile = true, Dist = 400f }, new Contact { Hostile = true, Dist = 700f } };
            var far = new List<Contact> { new Contact { Hostile = true, Dist = 5000f } };
            float rn = HudContacts.PickRadarRange(near), rf = HudContacts.PickRadarRange(far);
            Check(rn <= 1500f && rf >= 6000f, $"the radar range follows the nearest hostiles ({N(rn)} m for 700 m, {N(rf)} m for 5 km)");
            HudContacts.PickRadarRange(near);
        }

        // ------------------------------------------------------------------------------------------------ the data interface

        sealed class Hold : IPilotSource
        {
            public ControlInput In;
            public ControlInput Sample(in SimFrame frame) => In;
        }

        static void DataInterface()
        {
            GameEvents.Reset();
            var W = new GameWorld(new Sim());
            GameWorld.SetCurrent(W);
            W.Sim.Register(W.Pilots);
            var flight = new FlightSystem(W);
            W.Sim.Register(new TargetingSystem(W));
            W.Sim.Register(flight);
            W.Sim.Register(new AimSystem(W));
            var weapons = new WeaponSystem(W, null, 0x2545F491u);
            var damage = new DamageSystem(W);
            W.Sim.Register(weapons); W.Sim.Register(new CollisionSystem(W)); W.Sim.Register(damage);

            (ShipState s, ShipCombat c, Hold p) Make(string cls, Vector3 pos, int team, float speed, string name)
            {
                var klass = ShipCatalog.Load(cls);
                var st = flight.Spawn(klass, pos, Quaternion.identity, speed, klass.TopSpeed > 0f ? speed / klass.TopSpeed : 0f, team, name);
                var p = new Hold();
                p.In.Throttle = klass.TopSpeed > 0f ? speed / klass.TopSpeed : 0f;
                W.Pilots.Attach(st.Id, p);
                var d = CombatCatalog.Get(cls).Clone();
                var c = damage.Register(st, d, null, team == 0);
                return (st, c, p);
            }
            var me = Make("vanguard_fighter", Vector3.zero, 0, 28f, "Sable 1");
            W.LocalPlayerShip = me.s.Id;
            var foe = Make("gtf_ulysses", new Vector3(0f, 0f, 600f), 1, 0f, "Reaper 3");
            var foe2 = Make("gtf_myrmidon", new Vector3(800f, 100f, -200f), 1, 0f, "Reaper 4");
            var friend = Make("gtf_ulysses", new Vector3(-300f, 0f, 200f), 0, 0f, "Sable 2");
            me.s.Target = foe.s.Id;
            me.p.In.Buttons |= ControlButtons.Orders;
            for (int i = 0; i < 60; i++) W.Sim.StepOnce();

            var f = new HudFrame { Width = 1920f, Height = 1080f };
            HudReader.Read(W, null, f, 1f);
            Check(f.HasShip && f.Alive && f.ClassName == me.s.ClassName, "the frame knows the player's ship");
            Check(Mathf.Abs(f.Speed - me.s.ForwardSpeed) < 0.01f && f.Speed > 20f && Mathf.Abs(f.Throttle - me.s.Throttle) < 0.01f, $"speed and throttle are the ship's ({N(f.Speed)} m/s, {N(f.Throttle)})");
            Check(f.TopSpeed == me.s.TopSpeed && f.BoostSpeed == me.s.AfterburnerSpeed && f.BoostSpeed > f.TopSpeed, $"the afterburner's top speed is above cruise ({N(f.TopSpeed)} and {N(f.BoostSpeed)} m/s)");
            Check(Mathf.Abs(f.Fuel01 - me.s.Fuel / me.s.FuelMax) < 0.001f && f.Fuel01 > 0.9f, $"afterburner fuel is the tank's ({N(f.Fuel01)})");
            Check(f.HasShield && f.Shield01 > 0.99f && f.Hull01 > 0.99f && f.HasEnergy && f.Energy01 > 0.5f && f.Power == PowerMode.Balanced, "hull, shield, energy and the power choice are read");
            Check(f.Missiles.Count >= 2 && f.Missiles[0].Ammo > 0 && f.Decoys == me.c.Decoys && f.Decoys > 0 && f.GunName != null, $"the guns, {f.Missiles.Count} missile types and {f.Decoys} decoys are read");
            Check(f.HasTarget && f.TargetName == "Reaper 3" && Mathf.Abs(f.TargetDist - Vector3.Distance(me.s.Position, foe.s.Position)) < 0.5f && f.TargetHostile, $"the target is Reaper 3 at {N(f.TargetDist)} m, hostile");
            Check(f.Contacts.Count == 3 && CountHostile(f) == 2, $"the radar has the three other ships, two of them hostile ({f.Contacts.Count})");
            Check(f.RadarRange <= 3000f && f.MapRange >= f.RadarRange * 0.5f, $"the radar and map ranges are picked ({N(f.RadarRange)} and {N(f.MapRange)} m)");
            Check(f.QuickMenu, "holding the orders button opens the quick menu");
            bool anyTarget = false; foreach (var ct in f.Contacts) if (ct.IsTarget) anyTarget = true;
            Check(anyTarget, "the radar marks the player's target");

            // damage you feel
            me.c.SensorStatic = 0.6f; me.c.Flags |= CombatFlags.EnginesCrippled | CombatFlags.PrimaryOffline; me.c.IncomingMissiles = 2; me.c.IncomingNearest = 800f; me.c.IncomingEta = 4f; me.c.LockedOnMe = true;
            me.c.Hull = me.c.HullMax * 0.4f;
            me.c.Lock = LockPhase.Acquiring; me.c.LockProgress01 = 0.5f;
            HudReader.Read(W, null, f, 1f);
            Check(Mathf.Abs(f.SensorStatic - 0.6f) < 1e-4f && f.Systems.Exists(l => l.Text.StartsWith("SENSORS")) && f.Systems.Exists(l => l.Text.Contains("GUN MOUNT")), "damaged sensors and a lost gun mount show in the systems list and the static");
            Check(f.IncomingMissiles == 2 && f.LockedOnMe && Mathf.Abs(f.IncomingNearest - 800f) < 1f && Mathf.Abs(f.Hull01 - 0.4f) < 0.01f, "an incoming missile, a lock on you and a low hull are read");
            Check(f.Lock == LockPhase.Acquiring && Mathf.Abs(f.LockProgress - 0.5f) < 1e-4f, "the lock's progress is read");

            // the mission and the cabin
            W.Mission = new MissionBoard { Name = "test" };
            W.Mission.Directives.Add(new MissionBoard.Directive { Text = "Destroy the Warden", State = MissionBoard.State.Active });
            W.Mission.Directives.Add(new MissionBoard.Directive { Text = "Protect the Small Mercy", State = MissionBoard.State.Done });
            W.Mission.Prompt = "Leave the seat";
            W.Cabin = new CabinBoard { MapOpen = true };
            HudReader.Read(W, null, f, 1f);
            Check(f.MissionRunning && f.Directives.Count == 2 && f.Directives[1].State == MissionBoard.State.Done && f.Prompt == "Leave the seat", "the mission's directives and prompt are read");
            Check(f.MapOpen, "the cabin's map switch opens the HUD's battle map");
            W.Cabin.State = CabinBoard.Phase.Standing;
            HudReader.Read(W, null, f, 1f);
            Check(!f.MapOpen, "the map is the seat's: standing in the cabin it is not drawn on the HUD");
            W.Mission.Ended = true;
            HudReader.Read(W, null, f, 1f);
            Check(!f.MissionRunning && f.Directives.Count == 0, "a finished mission shows no directives");

            // missiles on the radar come from the weapons system's tracks
            using (var tracker = new HudMissiles(W))
            {
                GameEvents.RaiseAt(CombatEvents.MissileTrack, new Vector3(0f, 0f, 300f), new Vector3(0f, 0f, -150f), "missile_heat", 77f, foe.s.Id, me.s.Id);
                GameEvents.RaiseAt(CombatEvents.MissileTrack, new Vector3(0f, 0f, 200f), Vector3.zero, "missile_decoy", 78f, me.s.Id, GameWorld.NoShip);
                HudReader.Read(W, null, f, 1f);
                int msl = 0, incoming = 0;
                foreach (var ct in f.Contacts) { if (ct.Missile) msl++; if (ct.Incoming) incoming++; }
                Check(msl == 1 && incoming == 1, $"a missile in flight at the player is a radar contact, a decoy is not ({msl} missile, {incoming} incoming)");
            }
            GameWorld.SetCurrent(null);
            GameEvents.Reset();
            _ = foe2; _ = friend;
        }

        static int CountHostile(HudFrame f) { int n = 0; foreach (var c in f.Contacts) if (c.Hostile && !c.Missile) n++; return n; }

        // ------------------------------------------------------------------------------------------------ the layout

        static readonly string[] Panels = { "header", "notices", "directives", "target", "arms", "radio", "speed", "energy", "shieldHull", "systems" };

        static HudFrame MakeFrame(float w, float h, string kind, float seed)
        {
            var f = new HudFrame { Width = w, Height = h, Time = 3.3f + seed, HasShip = true, ClassName = "Vanguard fighter" };
            f.Speed = 134f; f.TopSpeed = 70f; f.BoostSpeed = 235f; f.Throttle = 0.7f; f.Fuel01 = 0.62f; f.Burning = true;
            f.Hull01 = 0.72f; f.Shield01 = 0.35f; f.Energy01 = 0.88f; f.Power = PowerMode.Shields;
            f.GunName = "Needle"; f.HeavyName = "Hammer";
            f.Missiles.Add(new HudFrame.MissileLine { Name = "Ember", Ammo = 12 });
            f.Missiles.Add(new HudFrame.MissileLine { Name = "Lance", Ammo = 6, Selected = true, Reload01 = 0.4f });
            f.Missiles.Add(new HudFrame.MissileLine { Name = "Anvil", Ammo = 2 });
            f.Decoys = 25;
            f.HasTarget = true; f.TargetHostile = true; f.TargetName = "Reaper 3"; f.TargetClass = "GTF Ulysses"; f.TargetSize = "fighter"; f.TargetHull01 = 0.8f; f.TargetShield01 = 0.5f; f.TargetHasShield = true;
            f.TargetDist = 1840f; f.TargetSpeed = 96f; f.TargetOnScreen = true; f.TargetRadiusPx = 40f * h / 1080f;
            f.TargetScreen = new Vector2(w * 0.5f + 70f * h / 1080f * Mathf.Sin(seed * 7f), h * 0.5f + 50f * h / 1080f * Mathf.Cos(seed * 5f));
            f.HasPart = true; f.PartName = "FLAK P02"; f.PartHealth01 = 0.85f; f.PartOnScreen = true; f.PartScreen = f.TargetScreen + new Vector2(14f, 6f); f.PartRadiusPx = 14f; f.TargetHasParts = true;
            f.HasLead = true; f.LeadScreen = f.TargetScreen + new Vector2(30f, -20f);
            f.Lock = LockPhase.Acquiring; f.LockProgress = 0.64f; f.WeaponNeedsLock = true; f.LockWeaponName = "Lance";
            f.RadarRange = 3000f; f.MapRange = 6000f;
            var rng = new System.Random((int)(seed * 1000f) + 3);
            for (int i = 0; i < 14; i++)
                f.Contacts.Add(new Contact { Rel = new Vector3((float)rng.NextDouble() * 4000f - 2000f, (float)rng.NextDouble() * 800f - 400f, (float)rng.NextDouble() * 4000f - 1500f), Hostile = i % 3 != 0, Big = i == 5, Callsign = i == 5 ? "Warden" : "Reaper " + i, IsTarget = i == 1, Dist = 900f + i * 100f });
            f.Contacts.Add(new Contact { Rel = new Vector3(100f, 0f, 400f), Missile = true, Hostile = true, Incoming = true });
            f.Systems.Add(new HudFrame.SystemLine { Text = "SENSORS 44%" });
            f.Systems.Add(new HudFrame.SystemLine { Text = "ENGINES 50%  MAX 60%" });
            f.Systems.Add(new HudFrame.SystemLine { Text = "LAUNCHER LOST", Bad = true });
            f.MissionRunning = true;
            f.Directives.Add(new HudFrame.DirectiveLine { Text = "Destroy the Warden before it reaches the Small Mercy", State = MissionBoard.State.Active });
            f.Directives.Add(new HudFrame.DirectiveLine { Text = "Keep the Small Mercy alive", State = MissionBoard.State.Active });
            f.Directives.Add(new HudFrame.DirectiveLine { Text = "Protect the freighter Tamsin", State = MissionBoard.State.Done });
            f.Directives.Add(new HudFrame.DirectiveLine { Text = "Escort the bombers home", State = MissionBoard.State.Failed });
            f.Radio.Add(new HudFrame.FeedLine { Speaker = "Sable 2", Text = "Contact, three o'clock high. Break left, break left, they have a lock on you and the Warden's guns are turning.", Alpha = 1f });
            f.Notices.Add(new HudFrame.FeedLine { Text = "LOCK ACQUIRED", Alpha = 1f });
            f.Notices.Add(new HudFrame.FeedLine { Text = "SHIELD DOWN", Alert = true, Alpha = 0.8f });
            f.Notices.Add(new HudFrame.FeedLine { Text = "TARGET FLAK P02 DESTROYED", Alpha = 0.6f });
            f.Info = new[] { "ESCORT   T+3:25   HOSTILES 7   KILLS 4   MEDIUM", "TAMSIN 100%   LECTOR 64%", "START: PAUSE / CONTROLS" };
            f.LastOrder = "COVER";
            if (kind == "threat") { f.IncomingMissiles = 2; f.IncomingNearest = 640f; f.IncomingEta = 2.2f; f.IncomingOffAxis = true; f.IncomingDir = new Vector2(0.6f, -0.8f); f.LockedOnMe = true; f.SensorStatic = 0.4f; f.DamageFlash = 0.7f; }
            if (kind == "menu") { f.QuickMenu = true; f.HasTarget = false; f.HasPart = false; f.HasLead = false; f.Lock = LockPhase.None; }
            if (kind == "map") f.MapOpen = true;
            if (kind == "pause") f.Overlay = new[] { "PAUSED", "START\tRESUME", "Y\tFLY AGAIN", "#SETTINGS", "LB   I\tFLIGHT PITCH   INVERTED", "D-PAD UP / DOWN   H\tHUD STYLE   HALO", "", "A long line of ordinary text that has to wrap onto a second line because it is much longer than the plate is wide, to see that wrapping keeps it on the plate.", "#CONTROLS", "RT   LB   RB\tFAST GUN   HEAVY GUN   FIRE MISSILE" };
            if (kind == "bare") { f.HasTarget = false; f.HasPart = false; f.HasLead = false; f.Directives.Clear(); f.Radio.Clear(); f.Notices.Clear(); f.Info = null; f.Systems.Clear(); f.Burning = false; f.Lock = LockPhase.None; }
            return f;
        }

        static bool Overlaps(Rect a, Rect b, float slack) => a.xMin < b.xMax - slack && b.xMin < a.xMax - slack && a.yMin < b.yMax - slack && b.yMin < a.yMax - slack;

        /// <param name="mul">The player's HUD scale on top of the screen's own (1 is the design size).</param>
        static void Layout(float mul)
        {
            string at = mul == 1f ? "" : $" at scale {N(mul)}";
            var L = new HudLines { RecordTexts = true, TypeFloor = 0.72f * mul };      // (the HUD brings the type's floor down with the player's scale)
            var R = new HudRenderer();
            string[] kinds = { "full", "threat", "bare", "menu", "map", "pause" };
            int shots = 0, offScreen = 0, panelOverlap = 0, textOverlap = 0, centre = 0, tiny = 0;
            var firstFail = new List<string>();
            foreach (var st in HudStyles.Styles)
                foreach (var (w, h) in Sizes)
                    foreach (var kind in kinds)
                    {
                        var f = MakeFrame(w, h, kind, w * 0.0007f + h * 0.0003f);
                        L.Clear(HudRenderer.ScaleFor(w, h) * mul, st);
                        R.Draw(L, f);
                        shots++;
                        string tag = $"{st.Name} {w}x{h} {kind}{at}";
                        float s = HudRenderer.ScaleFor(w, h) * mul;
                        // 1. nothing leaves the screen (text and every named panel)
                        foreach (var t in L.Texts)
                            if (t.Bounds.xMin < -1f || t.Bounds.yMin < -1f || t.Bounds.xMax > w + 1f || t.Bounds.yMax > h + 1f)
                            { offScreen++; if (firstFail.Count < 12) firstFail.Add($"{tag}: '{t.Text}' leaves the screen {t.Bounds}"); }
                        foreach (var kv in R.Boxes)
                            if (kv.Key != "banner" && (kv.Value.xMin < -2f || kv.Value.yMin < -2f || kv.Value.xMax > w + 2f || kv.Value.yMax > h + 2f))
                            { offScreen++; if (firstFail.Count < 12) firstFail.Add($"{tag}: panel {kv.Key} leaves the screen {kv.Value}"); }
                        // 2. the type is never tiny
                        foreach (var t in L.Texts) if (t.Cap < 8.5f * mul) { tiny++; if (firstFail.Count < 12) firstFail.Add($"{tag}: '{t.Text}' is {t.Cap} px tall"); }
                        if (kind == "pause" || kind == "map" || kind == "menu") { CheckScreenKinds(R, L, f, tag, s, firstFail, ref textOverlap); continue; }
                        // 3. no two panels overlap
                        for (int i = 0; i < Panels.Length; i++)
                            for (int j = i + 1; j < Panels.Length; j++)
                                if (R.Boxes.TryGetValue(Panels[i], out var a) && R.Boxes.TryGetValue(Panels[j], out var b) && Overlaps(a, b, 3f))
                                { panelOverlap++; if (firstFail.Count < 12) firstFail.Add($"{tag}: {Panels[i]} {a} overlaps {Panels[j]} {b}"); }
                        // 4. no two words overlap
                        for (int i = 0; i < L.Texts.Count; i++)
                            for (int j = i + 1; j < L.Texts.Count; j++)
                                if (Overlaps(L.Texts[i].Bounds, L.Texts[j].Bounds, 2.5f))
                                {
                                    // the target's own tag sits next to its bracket where it is: it may meet the gauges' numbers only if the target is dragged there; skip those frames
                                    textOverlap++; if (firstFail.Count < 12) firstFail.Add($"{tag}: '{L.Texts[i].Text}' overlaps '{L.Texts[j].Text}'");
                                }
                        // 5. the middle of the screen is clear of panels
                        var mid = new Rect(w * 0.5f - 120f * s, h * 0.5f - 120f * s, 240f * s, 240f * s);
                        foreach (var kv in R.Boxes)
                            if (kv.Key != "reticle" && kv.Key != "banner" && kv.Key != "prompt" && kv.Key != "lockedOn" && kv.Key != "lockWarn" && Overlaps(kv.Value, mid, 1f))
                            { centre++; if (firstFail.Count < 12) firstFail.Add($"{tag}: {kv.Key} {kv.Value} enters the middle of the screen"); }
                    }
            foreach (var m in firstFail) MnLog.Info("HUD detail: " + m);
            Check(offScreen == 0, $"nothing leaves the screen{at}: {shots} frames (3 styles x 8 screen shapes x 6 states), {offScreen} words or panels off screen");
            Check(tiny == 0, $"no type under {N(8.5f * mul)} px tall at any size{at} ({tiny})");
            Check(panelOverlap == 0, $"no two panels overlap{at} ({panelOverlap} overlaps)");
            Check(textOverlap == 0, $"no two words overlap{at} ({textOverlap} overlaps)");
            Check(centre == 0, $"the middle of the screen is clear of panels{at} ({centre} intrusions)");
            if (mul != 1f) return;

            // a very long name, a full directive list and a long radio line wrap and stay on the screen
            var longF = MakeFrame(1920f, 1080f, "full", 0.3f);
            longF.TargetName = "Constellation Class Heavy Warship Mercy Of The Long Dark";
            longF.TargetClass = "Earth Heavy Cruiser Of The Third Fleet Line";
            for (int i = 0; i < 6; i++) longF.Directives.Add(new HudFrame.DirectiveLine { Text = "Another directive that goes on for quite a while to see that the list holds " + i });
            foreach (var st in HudStyles.Styles)
            {
                L.Clear(1f, st); R.Draw(L, longF);
                bool ok = true;
                foreach (var t in L.Texts) if (t.Bounds.xMax > 1921f || t.Bounds.yMax > 1081f || t.Bounds.xMin < -1f) { ok = false; MnLog.Info($"HUD detail: {st.Name} long frame: '{t.Text}' {t.Bounds}"); }
                Check(ok, $"{st.Name}: a very long target name, class and a long directive list stay on the screen");
            }
        }

        static void CheckScreenKinds(HudRenderer R, HudLines L, HudFrame f, string tag, float s, List<string> firstFail, ref int textOverlap)
        {
            // within a plate or a map screen, no two words overlap
            Rect? plate = R.Boxes.TryGetValue("overlay", out var o) ? o : R.Boxes.TryGetValue("menu", out var m) ? m : (Rect?)null;
            for (int i = 0; i < L.Texts.Count; i++)
            {
                if (plate.HasValue && !plate.Value.Contains(L.Texts[i].Bounds.center)) continue;
                for (int j = i + 1; j < L.Texts.Count; j++)
                {
                    if (plate.HasValue && !plate.Value.Contains(L.Texts[j].Bounds.center)) continue;
                    if (Overlaps(L.Texts[i].Bounds, L.Texts[j].Bounds, 2.5f)) { textOverlap++; if (firstFail.Count < 12) firstFail.Add($"{tag}: '{L.Texts[i].Text}' overlaps '{L.Texts[j].Text}'"); }
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ the player's scale and opacity

        static bool Near(float a, float b, float tol = 0.002f) => Mathf.Abs(a - b) <= tol;

        static void ScaleAndOpacity()
        {
            // the defaults: a clear step back from the design size and full strength, but not too far
            float ds = HudStyles.DefaultScale, dop = HudStyles.DefaultOpacity;
            Check(ds >= 0.7f && ds <= 0.92f && dop >= 0.6f && dop <= 0.88f, $"the defaults step the HUD back clearly and not too far: scale {N(ds)}, opacity {N(dop)} (design size and full strength are 1 and 1)");

            // where they come from: the defaults, hud.json, the command line (which wins), the limits, an unreadable file
            string dir = Path.Combine(Path.GetTempPath(), "mn_hudtest_" + System.Diagnostics.Process.GetCurrentProcess().Id);
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "hud.json");
            var wasArgs = Environment.GetCommandLineArgs();
            try
            {
                CommandLine.SetArgs(new[] { "Monolith.exe" });
                HudStyles.FileOverride = file;
                HudStyles.Reload();
                Check(Near(HudStyles.Scale, ds) && Near(HudStyles.Opacity, dop), $"with no file and no flag the scale and opacity are the defaults ({N(HudStyles.Scale)}, {N(HudStyles.Opacity)})");
                File.WriteAllText(file, "{ \"style\": \"cipher\", \"textScale\": 1.1, \"scale\": 0.7, \"opacity\": 0.6 }");
                HudStyles.Reload();
                Check(Near(HudStyles.Scale, 0.7f) && Near(HudStyles.Opacity, 0.6f) && Near(HudStyles.TextScale, 1.1f) && HudStyles.Current.Name == "cipher", "hud.json's scale, opacity, textScale and style are read (0.7, 0.6, 1.1, cipher)");
                CommandLine.SetArgs(new[] { "Monolith.exe", "-mnHudScale", "0.95", "-mnHudOpacity", "0.5" });
                HudStyles.Reload();
                Check(Near(HudStyles.Scale, 0.95f) && Near(HudStyles.Opacity, 0.5f), "-mnHudScale and -mnHudOpacity win over the file");
                CommandLine.SetArgs(new[] { "Monolith.exe", "-mnHudScale", "9", "-mnHudOpacity", "0.01" });
                HudStyles.Reload();
                Check(Near(HudStyles.Scale, HudStyles.MaxScale) && Near(HudStyles.Opacity, HudStyles.MinOpacity), $"absurd values are held to the limits (scale {N(HudStyles.MinScale)} to {N(HudStyles.MaxScale)}, opacity {N(HudStyles.MinOpacity)} to 1): the HUD can never be made invisible");
                CommandLine.SetArgs(new[] { "Monolith.exe" });
                File.WriteAllText(file, "{ this is not json");
                HudStyles.Reload();
                Check(Near(HudStyles.Scale, ds) && Near(HudStyles.Opacity, dop), "an unreadable hud.json means the defaults");
                File.WriteAllText(file, "{ \"scale\": \"big\", \"opacity\": null, \"textScale\": [1] }");
                HudStyles.Reload();
                Check(Near(HudStyles.Scale, ds) && Near(HudStyles.Opacity, dop) && Near(HudStyles.TextScale, 1f), "values of the wrong type mean the defaults");

                // saving keeps what the player chose and anything else in the file, and writes the two numbers when the file has none
                File.WriteAllText(file, "{ \"style\": \"halo\", \"scale\": 0.7, \"opacity\": 0.6, \"note\": \"mine\" }");
                HudStyles.Reload();
                var o = HudStyles.SettingsJson(JObject.Parse(File.ReadAllText(file)));
                Check(Near((float)o["scale"], 0.7f) && Near((float)o["opacity"], 0.6f) && (string)o["note"] == "mine" && (string)o["style"] == "halo", "a save keeps the player's scale, opacity and any other keys");
                File.WriteAllText(file, "{ \"style\": \"halo\", \"textScale\": 1.0 }");
                HudStyles.Reload();
                var o2 = HudStyles.SettingsJson(JObject.Parse(File.ReadAllText(file)));
                Check(Near((float)o2["scale"], ds) && Near((float)o2["opacity"], dop), "a save writes scale and opacity into a hud.json that has none, so they are there to find and change");
            }
            finally
            {
                CommandLine.SetArgs(wasArgs);
                HudStyles.FileOverride = null;
                HudStyles.Reload();
                try { Directory.Delete(dir, true); } catch { /* a temp folder */ }
            }

            // opacity touches every vertex that is not a warning and no vertex that is one
            var R = new HudRenderer();
            int inkTotal = 0, warnTotal = 0, bad = 0;
            foreach (var st in HudStyles.Styles)
            {
                var f = MakeFrame(1920f, 1080f, "threat", 0.5f);
                var full = new HudLines { Opacity = 1f };
                full.Clear(1f, st); R.Draw(full, f);
                var half = new HudLines { Opacity = 0.5f };
                half.Clear(1f, st); R.Draw(half, f);
                if (full.VertexCount != half.VertexCount) { bad++; continue; }
                for (int i = 0; i < full.VertexCount; i++)
                {
                    var a = full.VertexColor(i); var b = half.VertexColor(i);
                    bool sameRgb = a.r == b.r && a.g == b.g && a.b == b.b;
                    if (full.IsWarn(a)) { warnTotal++; if (!sameRgb || a.a != b.a) bad++; }
                    else { inkTotal++; if (!sameRgb || b.a != (byte)(a.a * 0.5f + 0.5f)) bad++; }
                }
            }
            Check(bad == 0 && inkTotal > 600 && warnTotal > 60, $"opacity 0.5 halves the alpha of every ink, plate and shadow vertex and leaves every warning vertex at full strength ({inkTotal} vertices scaled, {warnTotal} warning vertices untouched, {bad} wrong; 3 styles, a frame with an incoming missile, a lock on you and a lost system)");

            // readable at 1080p at the default scale: the smallest type of each size, and the dimmest text
            float minLabel = 999f, minBody = 999f, minValue = 999f;
            string[] kinds = { "full", "threat", "bare", "menu" };
            var L = new HudLines { RecordTexts = true };
            foreach (var st in HudStyles.Styles)
                foreach (var kind in kinds)
                {
                    L.Clear(HudRenderer.ScaleFor(1920, 1080) * ds, st);
                    R.Draw(L, MakeFrame(1920f, 1080f, kind, 0.5f));
                    foreach (var t in L.Texts)
                    {
                        if (t.Role == TextRole.Label) minLabel = Mathf.Min(minLabel, t.Cap);
                        else if (t.Role == TextRole.Body) minBody = Mathf.Min(minBody, t.Cap);
                        else if (t.Role == TextRole.Value) minValue = Mathf.Min(minValue, t.Cap);
                    }
                }
            Check(minLabel >= 11f && minBody >= 14f && minValue >= 19f, $"at 1080p and the default scale the smallest type is still readable: label cap {N(minLabel)} px (at least 11), body {N(minBody)} (14), value {N(minValue)} (19); the design sizes are 13, 16.5 and 23");
            foreach (var st in HudStyles.Styles)
                Check(st.Dim * dop >= 0.48f, $"{st.Name}: the dimmest text keeps {N(st.Dim * dop * 100f)}% of full brightness at the default opacity (at least 48)");

            // the HUD takes less of the screen and costs no more to build
            var busy = MakeFrame(1920f, 1080f, "threat", 0.5f);
            foreach (var st in HudStyles.Styles)
            {
                var a = new HudLines(); a.Clear(HudRenderer.ScaleFor(1920, 1080), st); R.Draw(a, busy); a.Areas(out float sa0, out float ta0);
                var b = new HudLines(); b.Clear(HudRenderer.ScaleFor(1920, 1080) * ds, st); R.Draw(b, busy); b.Areas(out float sa1, out float ta1);
                float ratio = (sa1 + ta1) / (sa0 + ta0);
                Check(ratio <= 0.85f && b.VertexCount <= a.VertexCount * 1.02f, $"{st.Name}: at the default scale the HUD covers {N(ratio * 100f)}% of the screen area it did (shapes {N(sa0 / 1e6f)} to {N(sa1 / 1e6f)} Mpx, type {N(ta0 / 1e6f)} to {N(ta1 / 1e6f)} Mpx), {a.VertexCount} to {b.VertexCount} vertices");
            }
            foreach (var st in HudStyles.Styles)
            {
                double Time(float opacity)
                {
                    var c = new HudLines { Opacity = opacity };
                    for (int i = 0; i < 30; i++) { c.Clear(1f, st); R.Draw(c, busy); }
                    var sw = Stopwatch.StartNew();
                    for (int i = 0; i < 200; i++) { c.Clear(1f, st); R.Draw(c, busy); }
                    return sw.Elapsed.TotalMilliseconds / 200;
                }
                double best1 = 1e9, best08 = 1e9;
                for (int k = 0; k < 5; k++) { best1 = Math.Min(best1, Time(1f)); best08 = Math.Min(best08, Time(dop)); }
                Check(best08 <= best1 * 1.15 + 0.01, $"{st.Name}: building a busy frame at opacity {N(dop)} takes {best08:0.000} ms against {best1:0.000} ms at full strength (best of 5: the opacity is one compare a vertex)");
            }
            Layout(ds);
        }

        // ------------------------------------------------------------------------------------------------ the start screen, drawn by the HUD's renderer

        static void StartMenuScreen()
        {
            var L = new HudLines { RecordTexts = true };
            var R = new HudRenderer();
            string[] panels = { "menuTitle", "menuCorner", "menuScenarios", "menuLevels", "menuGauges", "menuAbout", "menuHint" };
            int shots = 0, offScreen = 0, overlap = 0, textOverlap = 0, tiny = 0, empty = 0;
            var firstFail = new List<string>();
            // the plain game's list, a list with twelve more missions (it scrolls), and a very long mission name
            for (int variant = 0; variant < 3; variant++)
            {
                var menu = Monolith.Game.StartCatalog.Build(null, useSaved: false);
                if (variant >= 1)
                    for (int i = 0; i < 12; i++) menu.Entries.Insert(4 + i, new Monolith.Start.StartEntry { Id = "extra" + i, Title = variant == 2 && i == 3 ? "The Very Long Name Of A Mission Somebody Wrote Beside The Game" : "Extra mission " + i, Detail = "A mission someone wrote, with a briefing that goes on for quite a while so that the line about it has to wrap onto more than one line and then be cut short at four lines whatever it says after that.", Kind = Monolith.Start.EntryKind.Mission });
                var data = new HudMenu();
                menu.Build(data);
                var cursors = new List<(int index, bool level)> { (0, false), (0, true), (menu.Entries.Count - 5, false), (menu.Entries.Count - 2, false), (menu.Entries.Count - 1, false) };
                foreach (var st in HudStyles.Styles)
                    foreach (var (w, h) in Sizes)
                        foreach (var (index, level) in cursors)
                        {
                            menu.Index = index; menu.LevelFocus = level; menu.Level = level ? 4 : 2;
                            menu.Sync(data);
                            data.Corner = "HUD  " + st.Title + "   F9"; data.Hint = "D-PAD  MOVE          A  CHOOSE          B  QUIT ROW"; data.Fade = 1f;
                            var f = new HudFrame { Width = w, Height = h, Menu = data, Time = 1f };
                            L.Clear(HudRenderer.ScaleFor(w, h), st);
                            R.Draw(L, f);
                            shots++;
                            string tag = $"{st.Name} {w}x{h} v{variant} cursor {index}{(level ? " level" : "")}";
                            if (L.VertexCount < 400 || L.Texts.Count < 12) { empty++; if (firstFail.Count < 12) firstFail.Add($"{tag}: only {L.VertexCount} vertices and {L.Texts.Count} words"); }
                            foreach (var t in L.Texts)
                            {
                                if (t.Bounds.xMin < -1f || t.Bounds.yMin < -1f || t.Bounds.xMax > w + 1f || t.Bounds.yMax > h + 1f) { offScreen++; if (firstFail.Count < 12) firstFail.Add($"{tag}: '{t.Text}' leaves the screen {t.Bounds}"); }
                                if (t.Cap < 8.5f) { tiny++; if (firstFail.Count < 12) firstFail.Add($"{tag}: '{t.Text}' is {t.Cap} px tall"); }
                            }
                            foreach (var p in panels)
                                if (R.Boxes.TryGetValue(p, out var r) && (r.xMin < -2f || r.yMin < -2f || r.xMax > w + 2f || r.yMax > h + 2f)) { offScreen++; if (firstFail.Count < 12) firstFail.Add($"{tag}: {p} leaves the screen {r}"); }
                            for (int i = 0; i < panels.Length; i++)
                                for (int j = i + 1; j < panels.Length; j++)
                                    if (R.Boxes.TryGetValue(panels[i], out var a) && R.Boxes.TryGetValue(panels[j], out var b) && Overlaps(a, b, 3f)) { overlap++; if (firstFail.Count < 12) firstFail.Add($"{tag}: {panels[i]} {a} overlaps {panels[j]} {b}"); }
                            for (int i = 0; i < L.Texts.Count; i++)
                                for (int j = i + 1; j < L.Texts.Count; j++)
                                    if (Overlaps(L.Texts[i].Bounds, L.Texts[j].Bounds, 2.5f)) { textOverlap++; if (firstFail.Count < 12) firstFail.Add($"{tag}: '{L.Texts[i].Text}' overlaps '{L.Texts[j].Text}'"); }
                        }
            }
            foreach (var m in firstFail) MnLog.Info("HUD detail: " + m);
            Check(empty == 0, $"the start screen draws something real in every style and shape ({shots} frames: 3 lists x 3 styles x 8 screen shapes x 5 cursors; {empty} nearly empty)");
            Check(offScreen == 0, $"the start screen: nothing leaves the screen ({offScreen} words or panels off screen)");
            Check(overlap == 0, $"the start screen: no two panels overlap ({overlap} overlaps)");
            Check(textOverlap == 0, $"the start screen: no two words overlap ({textOverlap} overlaps)");
            Check(tiny == 0, $"the start screen: no type under 8.5 px tall ({tiny})");

            // it is drawn in the HUD's own style: the three styles draw it differently, and a fade-in draws less
            var menu0 = Monolith.Game.StartCatalog.Build(null, useSaved: false);
            var d0 = new HudMenu(); menu0.Build(d0); menu0.Sync(d0); d0.Fade = 1f; d0.Hint = "x"; d0.Corner = "y";
            var counts = new List<int>();
            foreach (var st in HudStyles.Styles)
            {
                L.Clear(1f, st); R.Draw(L, new HudFrame { Width = 1920f, Height = 1080f, Menu = d0, Time = 1f });
                counts.Add(L.VertexCount);
            }
            Check(counts[0] != counts[1] && counts[1] != counts[2], $"the start screen reads differently in the three styles ({counts[0]}, {counts[1]}, {counts[2]} vertices)");
            // the level's name and the line under it in the gauges are clear of each other (the words must not touch, as the check on overlaps would let them)
            foreach (var st in HudStyles.Styles)
            {
                L.Clear(1f, st); R.Draw(L, new HudFrame { Width = 1920f, Height = 1080f, Menu = d0, Time = 1f });
                Rect? name = null, line = null;
                foreach (var t in L.Texts) { if (t.Text == "MEDIUM" && t.Bounds.center.x > 800f && t.Bounds.center.x < 1120f) name = t.Bounds; else if (t.Text.StartsWith("DAMAGE TO YOU")) line = t.Bounds; }
                Check(name.HasValue && line.HasValue && line.Value.yMin - name.Value.yMax >= 6f, $"{st.Name}: the level's name and the line under it in the gauges have a gap of {(name.HasValue && line.HasValue ? N(line.Value.yMin - name.Value.yMax) : "?")} px (at least 6)");
            }
            L.Clear(1f, HudStyles.Styles[0]); d0.Fade = 0f; R.Draw(L, new HudFrame { Width = 1920f, Height = 1080f, Menu = d0, Time = 1f });
            Check(L.VertexCount == 0, "faded out, the start screen draws nothing");
        }

        // ------------------------------------------------------------------------------------------------ the pause screens

        static void PauseScreens()
        {
            var controls = ControlsConfig.Load(out _);
            var go = new GameObject("HudSelfTest");
            try
            {
                var hud = go.AddComponent<PilotHud>();
                var hc = new HudControls(hud, controls);
                var rows = hc.SettingsLines();
                bool flight = rows.Exists(r => r.Contains("FLIGHT PITCH")), look = rows.Exists(r => r.Contains("CABIN LOOK")), style = rows.Exists(r => r.Contains("HUD STYLE"));
                Check(flight && look, "the shared pause settings carry both invert switches (flight pitch, cabin look), read from controls.json");
                Check(style, "the shared pause settings carry the HUD style switch");
                // the four scenes hold the shared switches and build their pause text from them (their pause screens are captured in gauntlet/shots/hud/hud_r1_pause*.json)
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
                foreach (var t in new[] { typeof(DogfightScene), typeof(CombatRange), typeof(FlightPlaytest), typeof(Monolith.Game.MissionScene) })
                {
                    var field = t.GetField("_hudControls", flags);
                    var pause = t.GetMethod("PauseText", flags);
                    Check(field != null && field.FieldType == typeof(HudControls) && pause != null && pause.ReturnType == typeof(string[]), $"{t.Name}'s pause screen is built with the shared settings (invert switches and HUD style)");
                }
                // flipping the switch changes the row
                bool before = controls.invert.flight;
                controls.invert.flight = !before;
                var rows2 = hc.SettingsLines();
                Check(rows2.Exists(r => r.Contains("FLIGHT PITCH") && r.Contains(before ? "NORMAL" : "INVERTED")), "the row follows the invert switch");
                controls.invert.flight = before;
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // ------------------------------------------------------------------------------------------------ cost

        static void Cost()
        {
            var L = new HudLines();
            var R = new HudRenderer();
            foreach (var st in HudStyles.Styles)
            {
                var f = MakeFrame(1920f, 1080f, "threat", 0.5f);
                for (int i = 0; i < 20; i++) { L.Clear(1f, st); R.Draw(L, f); }
                var sw = Stopwatch.StartNew();
                const int n = 300;
                for (int i = 0; i < n; i++) { L.Clear(1f, st); R.Draw(L, f); }
                double ms = sw.Elapsed.TotalMilliseconds / n;
                R.Timings = new Dictionary<string, long>();
                for (int i = 0; i < n; i++) { L.Clear(1f, st); R.Draw(L, f); }
                var parts = new List<string>();
                foreach (var kv in R.Timings) parts.Add($"{kv.Key.Replace("Draw", "")} {kv.Value * 1000.0 / Stopwatch.Frequency / n:0.000}");
                MnLog.Info($"HUD cost {st.Name} (ms per frame): {string.Join(", ", parts)}");
                R.Timings = null;
                if (st.Name == "halo") { L.Clear(1f, st); R.Draw(L, f); L.Areas(out float sa, out float ta); MnLog.Info($"HUD area halo: shapes {sa / 1e6f:0.000} Mpx, type {ta / 1e6f:0.000} Mpx; biggest quads:"); foreach (var q in L.Biggest(14)) MnLog.Info("HUD   " + q); }
                Check(ms < 1.0, $"{st.Name}: building a busy frame takes {ms:0.000} ms of CPU ({L.VertexCount} vertices, {L.GlyphCount} glyphs)");
            }
        }
    }
}
