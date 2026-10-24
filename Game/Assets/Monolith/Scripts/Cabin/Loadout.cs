using System.Collections.Generic;
using Monolith.Core;
using Monolith.Mission;

namespace Monolith.Cabin
{
    /// <summary>
    /// What the weapon rack holds: two guns and two missile types, picked from the mission's small set by loading them before launch. It feeds the combat system's own
    /// arms (the player's <see cref="ShipCombat"/>): gun 1 is the fast gun's slot, gun 2 the heavy gun's; the two missile types are the ship's only missile slots, each with
    /// the ammunition the ship's definition gives that type. Nothing here is a menu: the rack's bays call <see cref="Next"/> one module at a time.
    /// </summary>
    public sealed class Loadout
    {
        public readonly List<string> GunOptions, MissileOptions;
        public readonly string[] Guns = new string[2];
        public readonly string[] Missiles = new string[2];

        public Loadout(MissionFile.CabinDef def, CabinConfig.Rack cfg)
        {
            GunOptions = new List<string>(def?.RackGuns ?? cfg.guns);
            MissileOptions = new List<string>(def?.RackMissiles ?? cfg.missiles);
            var dg = def?.DefaultGuns ?? cfg.defaultGuns;
            var dm = def?.DefaultMissiles ?? cfg.defaultMissiles;
            for (int i = 0; i < 2; i++)
            {
                Guns[i] = i < dg.Count ? dg[i] : GunOptions[i % GunOptions.Count];
                Missiles[i] = i < dm.Count ? dm[i] : MissileOptions[i % MissileOptions.Count];
            }
        }

        public bool IsLoaded(string weaponId) => Guns[0] == weaponId || Guns[1] == weaponId || Missiles[0] == weaponId || Missiles[1] == weaponId;

        /// <summary>The next module for a bay: the next gun for a gun bay (either may repeat: two of one is a choice), the next missile type not already in the other bay.</summary>
        public string Next(bool gun, int slot)
        {
            if (gun)
            {
                int i = GunOptions.IndexOf(Guns[slot]);
                Guns[slot] = GunOptions[(i + 1) % GunOptions.Count];
                return Guns[slot];
            }
            string other = Missiles[1 - slot];
            int j = MissileOptions.IndexOf(Missiles[slot]);
            for (int k = 1; k <= MissileOptions.Count; k++)
            {
                string cand = MissileOptions[(j + k) % MissileOptions.Count];
                if (cand != other) { Missiles[slot] = cand; return cand; }
            }
            return Missiles[slot];
        }

        /// <summary>Put the loadout into a ship's arms (before launch, with nothing in flight).</summary>
        public void Apply(ShipCombat c)
        {
            if (c == null) return;
            var def = CombatCatalog.Get(c.DefId);
            var g0 = WeaponCatalog.Get(Guns[0]); var g1 = WeaponCatalog.Get(Guns[1]);
            if (g0 != null) { c.PrimaryGun = g0.Id; c.PrimaryName = g0.Name; c.PrimarySpeed = g0.Speed; }
            if (g1 != null) { c.HeavyGun = g1.Id; c.HeavyName = g1.Name; c.HeavySpeed = g1.Speed; }
            c.PrimaryReadyAt = c.HeavyReadyAt = 0f;
            c.Missiles.Clear();
            foreach (var id in Missiles)
            {
                var w = WeaponCatalog.Get(id);
                if (w == null) continue;
                int n = 6;
                if (def != null) foreach (var m in def.Missiles) if (m.Key == id) n = m.Value;
                c.Missiles.Add(new MissileSlot { Weapon = w.Id, Name = w.Name, Ammo = n, AmmoMax = n });
            }
            c.SelectedMissile = 0;
            c.Lock = LockPhase.None; c.LockProgress01 = 0f; c.LockWeapon = null;
        }

        public string Label(string weaponId) => (WeaponCatalog.Get(weaponId)?.Name ?? weaponId ?? "-").ToUpperInvariant();
    }
}
