using System.Collections.Generic;
using UnityEngine;

namespace Monolith.Core
{
    /// <summary>Builds a ship's <see cref="ShipCombat"/> from its definition (what <c>DamageSystem.Register</c> does for you).</summary>
    public static class ShipCombatFactory
    {
        public static ShipCombat Create(ShipState ship, ShipCombatDef def, CombatRules rules, bool selfHeals, IShipVisual visual)
        {
            var c = new ShipCombat
            {
                Id = ship.Id, Ship = ship, DefId = def.Id, SizeClass = def.SizeClass,
                HullMax = def.HullMax, Hull = def.HullMax,
                ShieldMax = def.ShieldMax(rules), SelfHeals = selfHeals, Visual = visual,
            };
            c.Shield = c.ShieldMax;

            // The hit shape: the boxes, and for shielded ships a bubble (the data's, else derived from the hull's extents like the juice's).
            var shape = new HitShape { Boxes = def.Boxes.ToArray(), BoundRadius = def.BoundRadius > 0f ? def.BoundRadius : ship.Radius };
            Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
            foreach (var b in shape.Boxes) { min = Vector3.Min(min, b.Centre - b.Half); max = Vector3.Max(max, b.Centre + b.Half); }
            if (c.ShieldMax > 0f)
            {
                shape.ShieldCentre = def.ShieldRadii.x > 0f ? def.ShieldCentre : 0.5f * (min + max);
                shape.ShieldRadii = def.ShieldRadii.x > 0f ? def.ShieldRadii : HitShape.BubbleFor(0.5f * (max - min));
            }
            c.Shape = shape;

            foreach (var pd in def.Parts)
            {
                float hp = Mathf.Max(1f, pd.HpPercent * pd.HpWeight * 0.01f * def.HullMax);
                c.Parts.Add(new CombatPart
                {
                    Index = c.Parts.Count, Name = pd.Name, Kind = pd.Kind, LocalPosition = pd.Position, LocalForward = pd.Forward,
                    Radius = pd.Radius, Hp = hp, MaxHp = hp, Gun = pd.Gun, Weapon = pd.Weapon,
                });
            }

            // Arms.
            c.PrimaryGun = def.PrimaryGun; c.HeavyGun = def.HeavyGun;
            c.PrimaryHardpoints = def.PrimaryHardpoints; c.HeavyHardpoints = def.HeavyHardpoints;
            var pw = WeaponCatalog.Get(def.PrimaryGun); var hw = WeaponCatalog.Get(def.HeavyGun);
            c.PrimaryName = pw?.Name; c.HeavyName = hw?.Name;
            c.PrimarySpeed = pw != null ? pw.Speed : 0f; c.HeavySpeed = hw != null ? hw.Speed : 0f;
            c.PrimaryOnline = pw != null; c.HeavyOnline = hw != null;
            c.EnergyMax = def.WeaponEnergy; c.Energy = def.WeaponEnergy;
            c.EnergyRechargePerSecond = def.PowerOutput * rules.WeaponEnergyLevel;
            foreach (var m in def.Missiles)
            {
                var w = WeaponCatalog.Require(m.Key);
                c.Missiles.Add(new MissileSlot { Weapon = w.Id, Name = w.Name, Ammo = m.Value, AmmoMax = m.Value });
            }
            c.Decoys = c.DecoysMax = def.Decoys;
            return c;
        }

        /// <summary>
        /// Parts for a ship whose model supplies them: each model part (name, kind, position, size and relative hit points) is valued by
        /// the definition's <see cref="ShipCombatDef.PartKinds"/> and added to <paramref name="def"/>. Kinds the definition does not know
        /// are left out (the model can have decorative systems).
        /// </summary>
        public static void AddModelParts(ShipCombatDef def, IEnumerable<PartDef> modelParts)
        {
            var list = new List<PartDef>(modelParts);
            // A class that lists both its own parts (the stand-ins a headless lab uses) and what its model's parts are worth gets only the model's, when a model is there.
            if (def.PartKinds.Count > 0 && def.Parts.Count > 0 && list.Exists(m => def.PartKinds.ContainsKey(m.Kind))) def.Parts = new List<PartDef>();
            foreach (var mp in list)
            {
                if (!def.PartKinds.TryGetValue(mp.Kind, out var kind)) continue;
                def.Parts.Add(new PartDef
                {
                    Name = mp.Name, Kind = mp.Kind, Position = mp.Position, Forward = mp.Forward, Radius = mp.Radius,
                    HpPercent = kind.HpPercent, HpWeight = Mathf.Max(0.1f, mp.HpWeight), Weapon = kind.Weapon, Gun = mp.Gun,
                });
            }
        }
    }
}
