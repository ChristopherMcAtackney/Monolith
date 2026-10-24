using System;
using Monolith.Core;
using Monolith.Hud;
using UnityEngine;

namespace Monolith.HudUi
{
    /// <summary>
    /// Fills a <see cref="HudFrame"/> from the game: the local player's ship, its combat state and its target, the radar's contacts, the mission board and the cabin board. The one
    /// place the HUD reads <see cref="GameWorld"/>; it reads and never writes. The camera is for where things fall on screen (the target, its part, the lead point).
    /// </summary>
    public static class HudReader
    {
        /// <summary>The text of a model's part name: "SUB_flak_P02" reads "FLAK P02".</summary>
        public static string PartLabel(string name)
        {
            if (string.IsNullOrEmpty(name)) return "PART";
            string n = name.StartsWith("SUB_", StringComparison.Ordinal) ? name.Substring(4) : name;
            return n.Replace('_', ' ').ToUpperInvariant();
        }

        /// <summary>Read the world into <paramref name="f"/> (cleared first). Overlay, banner, info and the feeds (radio, notices) are the HUD component's own and are left alone.</summary>
        public static void Read(GameWorld w, Camera cam, HudFrame f, float time)
        {
            f.ResetWorld();
            f.Time = time;
            if (cam != null) { f.Width = cam.pixelWidth; f.Height = cam.pixelHeight; }

            var cab = w.Cabin;
            f.MapOpen = cab != null && cab.MapOpen && cab.State == CabinBoard.Phase.Seated;

            var ship = w.Ships.Get(w.LocalPlayerShip);
            var me = w.Combat.Get(w.LocalPlayerShip);
            ReadMission(w, f);
            if (ship == null) return;
            f.HasShip = true;
            f.ClassName = ship.ClassName;

            // flight
            f.Speed = ship.ForwardSpeed; f.TopSpeed = Mathf.Max(1f, ship.TopSpeed); f.BoostSpeed = Mathf.Max(f.TopSpeed, ship.AfterburnerSpeed);
            f.Throttle = ship.Throttle; f.Matching = ship.MatchingSpeed; f.Burning = ship.AfterburnerOn;
            f.BrakeOn = ship.BrakeOn; f.Braking = ship.Braking; f.BrakeResume = ship.BrakeResume;
            f.Fuel01 = ship.FuelMax > 0f ? Mathf.Clamp01(ship.Fuel / ship.FuelMax) : 0f;
            f.SpeedScale = ship.SpeedScale;

            // contacts for the radar and the map
            HudContacts.Gather(w, ship, f.Contacts);
            f.RadarRange = HudContacts.PickRadarRange(f.Contacts);
            f.MapRange = HudContacts.PickMapRange(f.Contacts);

            if (me == null) return;
            f.Alive = me.Alive;
            f.Hull01 = me.Hull01; f.Shield01 = me.Shield01; f.HasShield = me.ShieldMax > 0f;
            f.HasEnergy = me.EnergyMax > 0f; f.Energy01 = f.HasEnergy ? Mathf.Clamp01(me.Energy / me.EnergyMax) : 0f;
            f.Power = me.Power;
            f.SelfHeals = me.SelfHeals;
            f.Mending = me.SelfHeals && me.TimeSinceDamage > 6f && me.Hull01 < 0.999f;
            f.SensorStatic = Mathf.Clamp01(me.SensorStatic);

            // arms
            f.GunName = me.PrimaryGun != null ? me.PrimaryName : null; f.GunOnline = me.PrimaryOnline;
            f.HeavyName = me.HeavyGun != null ? me.HeavyName : null; f.HeavyOnline = me.HeavyOnline;
            f.LaunchersOffline = (me.Flags & CombatFlags.LaunchersOffline) != 0;
            for (int i = 0; i < me.Missiles.Count; i++)
            {
                var m = me.Missiles[i];
                float reload = 1f;
                if (w.Time < m.ReadyAt) { var d = WeaponCatalog.Get(m.Weapon); if (d != null && d.FireWait > 0.01f) reload = Mathf.Clamp01(1f - (m.ReadyAt - w.Time) / d.FireWait); }
                var ml = f.AddMissile(); ml.Name = m.Name; ml.Ammo = m.Ammo; ml.Selected = i == me.SelectedMissile; ml.Offline = f.LaunchersOffline; ml.Reload01 = reload;
            }
            f.Decoys = me.Decoys; f.DecoysMax = me.DecoysMax;

            // lock
            var slot = me.SelectedSlot;
            var def = slot != null ? WeaponCatalog.Get(slot.Weapon) : null;
            f.Lock = me.Lock; f.LockProgress = me.LockProgress01;
            if (def != null) { f.WeaponNeedsLock = def.NeedsLock; f.WeaponSeeks = !def.NeedsLock; f.LockWeaponName = slot.Name; }

            // threats
            f.IncomingMissiles = me.IncomingMissiles; f.IncomingNearest = me.IncomingNearest; f.IncomingEta = me.IncomingEta;
            f.LockedOnMe = me.LockedOnMe; f.BeingLocked = me.BeingLocked;
            if (me.IncomingMissiles > 0 && cam != null)
            {
                Vector3 local = cam.transform.InverseTransformDirection(me.IncomingDirection);
                var dir = new Vector2(local.x, -local.y);
                if (local.z < 0.9f && dir.sqrMagnitude > 1e-4f) { f.IncomingOffAxis = true; f.IncomingDir = dir.normalized; }
            }

            // systems you feel
            ReadSystems(me, f);

            // the quick menu
            f.QuickMenu = me.Alive && w.Pilots.Current(me.Id).Held(ControlButtons.Orders);

            // the target: who it is and how far (from the ship's own position when there is no camera, as in a headless check)
            var t = w.Ships.Get(ship.Target);
            float a = w.Alpha;
            Vector3 eye = cam != null ? cam.transform.position : ship.Position;
            float h = cam != null ? cam.pixelHeight : f.Height;
            float fov = cam != null ? h * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) : 1f;
            if (t != null)
            {
                var tc = w.Combat.Get(t.Id);
                Vector3 tp = t.RenderPosition(a);
                f.HasTarget = true;
                f.TargetHostile = me.TargetHostile;
                f.TargetName = t.Callsign ?? "";
                f.TargetClass = t.ClassName ?? "";
                f.TargetSize = tc != null ? tc.SizeClass : "ship";
                f.TargetDist = Vector3.Distance(tp, eye);
                f.TargetSpeed = Mathf.Max(0f, t.ForwardSpeed);
                if (tc != null) { f.TargetHull01 = tc.Hull01; f.TargetHasShield = tc.ShieldMax > 0f; f.TargetShield01 = tc.Shield01; f.TargetHasParts = tc.Parts.Count > 0; }
                if (ship.TargetPart >= 0 && tc != null && ship.TargetPart < tc.Parts.Count)
                {
                    var part = tc.Parts[ship.TargetPart];
                    f.HasPart = true; f.PartName = PartLabel(part.Name); f.PartHealth01 = part.Health01;
                }
                if (cam != null)
                {
                    Vector3 sp = cam.WorldToScreenPoint(tp);
                    f.TargetScreen = new Vector2(sp.x, h - sp.y);
                    f.TargetOnScreen = sp.z > 0f && f.TargetScreen.x > 0f && f.TargetScreen.x < cam.pixelWidth && f.TargetScreen.y > 0f && f.TargetScreen.y < h;
                    f.TargetRadiusPx = Mathf.Clamp(t.Radius / Mathf.Max(1f, sp.z) * fov * 1.15f, 28f * h / 1080f, h * 0.42f);
                    Vector3 local = cam.transform.InverseTransformPoint(tp);
                    var dir = new Vector2(local.x, -local.y);
                    f.TargetDir = dir.sqrMagnitude < 1e-6f ? Vector2.down : dir.normalized;
                    if (f.HasPart)
                    {
                        var part = tc.Parts[ship.TargetPart];
                        Vector3 pw = t.RenderPosition(a) + t.RenderRotation(a) * part.LocalPosition;
                        Vector3 ps = cam.WorldToScreenPoint(pw);
                        f.PartOnScreen = ps.z > 0f;
                        f.PartScreen = new Vector2(ps.x, h - ps.y);
                        f.PartRadiusPx = Mathf.Max(11f * h / 1080f, part.Radius / Mathf.Max(1f, ps.z) * fov * 1.3f);
                    }
                }
            }
            if (cam == null) return;
            if (me.HasLead)
            {
                Vector3 lp = cam.WorldToScreenPoint(me.LeadPoint);
                if (lp.z > 0f) { f.HasLead = true; f.LeadScreen = new Vector2(lp.x, h - lp.y); }
            }
        }

        static void ReadMission(GameWorld w, HudFrame f)
        {
            var m = w.Mission;
            if (m == null) return;
            f.MissionRunning = !m.Ended;
            f.Prompt = m.Prompt;
            if (m.Ended) return;
            foreach (var d in m.Directives) f.AddDirective(d.Text ?? "", d.State, w.Time - d.Since);
        }

        static void ReadSystems(ShipCombat me, HudFrame f)
        {
            var fl = me.Flags;
            void Add(string text, bool bad) => f.AddSystem(text, bad);
            string Pct(float v) => Mathf.RoundToInt(Mathf.Clamp01(v) * 100f).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
            if (me.SensorStatic > 0.02f) Add(me.SensorStatic >= 0.99f ? "SENSORS OFF" : "SENSORS " + Pct(1f - me.SensorStatic), me.SensorStatic >= 0.99f);
            f.EnginesDown = (fl & CombatFlags.EnginesDown) != 0; f.EnginesCrippled = (fl & CombatFlags.EnginesCrippled) != 0;
            if (f.EnginesDown) Add("ENGINES OFF", true);
            else if (f.EnginesCrippled) Add("ENGINES " + Pct(me.EngineHealth01) + "  MAX " + Pct(me.Ship.SpeedScale), false);
            if ((fl & CombatFlags.PrimaryOffline) != 0) Add("GUN MOUNT LOST", true);
            if ((fl & CombatFlags.HeavyOffline) != 0) Add("HEAVY MOUNT LOST", true);
            if ((fl & CombatFlags.LaunchersOffline) != 0) Add("LAUNCHER LOST", true);
            if ((fl & CombatFlags.ReactorDown) != 0) Add("REACTOR OFF", true);
            if (f.Mending) Add("MENDING", false);
        }
    }
}
