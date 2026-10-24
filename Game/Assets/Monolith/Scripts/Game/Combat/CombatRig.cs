using System.Collections.Generic;
using Monolith.Collision;
using Monolith.Core;
using Monolith.Damage;
using Monolith.Flight;
using Monolith.Juice;
using Monolith.ShipArt;
using Monolith.Targeting;
using Monolith.Weapons;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// The combat systems, built and registered with the clock in one place (Game is the only assembly allowed to reference them all).
    /// They run in every scene and do nothing until a scene registers ships with <see cref="Spawn"/>. Also the one way to bring a ship
    /// into a fight and take it out again.
    /// </summary>
    public sealed class CombatRig
    {
        public WeaponSystem Weapons { get; private set; }
        public DamageSystem Damage { get; private set; }
        public CollisionSystem Collision { get; private set; }
        public ShipReaper Reaper { get; private set; }
        public GameWorld World { get; private set; }
        public FlightSystem Flight { get; private set; }

        public static CombatRig Create(GameWorld world, FlightSystem flight)
        {
            var rig = new CombatRig { World = world, Flight = flight };
            world.Sim.Register(new AimSystem(world));
            rig.Weapons = new WeaponSystem(world);
            rig.Damage = new DamageSystem(world);
            rig.Collision = new CollisionSystem(world);
            rig.Reaper = new ShipReaper(world, flight);
            world.Sim.Register(rig.Weapons);
            world.Sim.Register(rig.Collision);
            world.Sim.Register(rig.Damage);
            world.Sim.Register(rig.Reaper);
            return rig;
        }

        /// <summary>
        /// Make a spawned flight ship a combatant: its definition comes from <paramref name="defId"/> (default: its class id, then a plain
        /// fallback). A <paramref name="visual"/> hears about damage; <paramref name="juice"/> (optional) learns its looks so it can
        /// break up and carry a shield bubble sized to the hit shape.
        /// </summary>
        public ShipCombat Spawn(ShipState ship, string defId = null, IShipVisual visual = null, Transform shipTransform = null, JuiceDirector juice = null,
                                IEnumerable<PartDef> modelParts = null, float hullScale = 1f, bool? selfHeal = null, IJuiceShipDamage juiceDamage = null)
        {
            var def = (defId != null ? CombatCatalog.Get(defId) : CombatCatalog.Get(ship.ClassId)) ?? CombatCatalog.Fallback(ship.ClassId, new Vector3(10f, 4f, 13f), ship.Radius);
            def = def.Clone();
            def.HullScale = hullScale;
            if (modelParts != null) ShipCombatFactory.AddModelParts(def, modelParts);
            var c = Damage.Register(ship, def, visual, selfHeal);
            if (juice != null && shipTransform != null)
                juice.RegisterShip(ship.Id, shipTransform, c.Shape.HasBubble ? c.Shape.ShieldRadii : (Vector3?)null, juiceDamage);
            return c;
        }
    }

    /// <summary>
    /// Takes dead ships out of the world once their death has been shown (order <see cref="SimOrder.Late"/>: every listener of
    /// <c>ship_destroyed</c> has run by then). The local player is never removed here; whoever runs the session respawns it.
    /// </summary>
    public sealed class ShipReaper : ISimSystem
    {
        readonly GameWorld _world;
        readonly FlightSystem _flight;
        readonly List<ShipCombat> _dead = new List<ShipCombat>();

        /// <summary>Called for each ship just before it is removed.</summary>
        public System.Action<ShipCombat> Reaped;

        public ShipReaper(GameWorld world, FlightSystem flight) { _world = world; _flight = flight; }
        public int Order => SimOrder.Late;

        public void Step(in SimFrame frame)
        {
            _dead.Clear();
            var all = _world.Combat.All;
            for (int i = 0; i < all.Count; i++)
                if (!all[i].Alive && all[i].Id != _world.LocalPlayerShip) _dead.Add(all[i]);
            foreach (var c in _dead)
            {
                try { Reaped?.Invoke(c); }
                catch (System.Exception e) { MnLog.Error("reaper callback threw: " + e); }
                _world.Combat.Remove(c.Id);
                _world.Pilots.Detach(c.Id);
                _flight.Despawn(c.Id);
            }
        }
    }
}
