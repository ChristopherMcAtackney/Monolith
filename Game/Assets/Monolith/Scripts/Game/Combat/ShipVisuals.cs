using System.Collections.Generic;
using Monolith.Core;
using Monolith.ShipArt;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// Shows combat's damage on a Blender-model ship through ShipArt's <see cref="IShipDamage"/> (the art side's contract, see
    /// art_src/PIPELINE.md): parts swap to damaged and wrecked looks and throw debris, hull hits scorch the plating, accumulated
    /// damage opens breaches. Also reads a model's parts as combat <see cref="PartDef"/>s (name, kind, position, size, relative hp).
    /// </summary>
    public sealed class ShipArtVisual : IShipVisual
    {
        readonly ShipDamage _damage;
        readonly Transform _root;
        readonly GameWorld _world;
        readonly int _shipId;
        readonly bool _juiceShows;

        /// <param name="juiceShowsWrecksAndHits">
        /// True when the juice has been given this model's <c>ShipArtJuiceDamage</c> (the juice then scorches the hull on <c>hit_hull</c> and
        /// swaps a part to its wreck, with chunks it owns, on <c>subsystem_destroyed</c>): this visual leaves those two to it, so nothing is
        /// done twice, and keeps the parts' damaged looks and the hull's accumulated damage, which are combat's to say.
        /// </param>
        public ShipArtVisual(GameWorld world, int shipId, ShipDamage damage, bool juiceShowsWrecksAndHits = false)
        {
            _world = world; _shipId = shipId; _damage = damage; _root = damage.transform; _juiceShows = juiceShowsWrecksAndHits;
        }

        public void SetPartHealth(string partName, float health01) => _damage.SetSubsystemHealth(partName, health01);

        public void DestroyPart(string partName)
        {
            if (_juiceShows) return;
            var sub = _damage.Find(partName);
            if (sub == null) return;
            _damage.DestroySubsystem(partName);
            // The wreck stays; its chunks are thrown clear and fly on with the ship's velocity for a few seconds.
            var ship = _world.Ships.Get(_shipId);
            Vector3 carry = ship != null ? ship.Velocity : Vector3.zero;
            foreach (var (piece, v, spin) in sub.DetachDebris())
                if (piece != null) DebrisFlight.Launch(piece, carry + v, spin, 6f);
        }

        public void HullHit(Vector3 worldPoint, Vector3 worldNormal, float radiusMetres, float severity01)
        {
            if (!_juiceShows) _damage.AddHullHit(worldPoint, worldNormal, radiusMetres, severity01);
        }

        public void SetHullDamage(float damage01) => _damage.SetHullDamage(damage01);
        public void ResetDamage() => _damage.ResetDamage();

        /// <summary>The model's named parts as combat parts, in the model's own (ship-local) frame.</summary>
        public static List<PartDef> Parts(ShipDamage damage)
        {
            var list = new List<PartDef>();
            var root = damage.transform;
            foreach (var s in damage.Subsystems)
            {
                list.Add(new PartDef
                {
                    Name = s.name, Kind = s.Kind,
                    Position = root.InverseTransformPoint(s.transform.position),
                    Forward = root.InverseTransformDirection(s.transform.forward),
                    Radius = s.Radius, HpWeight = s.Hp,
                });
            }
            return list;
        }
    }

    /// <summary>Shows damage on a placeholder ship (no parts to swap): a ship with no engines left loses its exhaust glow.</summary>
    public sealed class PlaceholderVisual : IShipVisual
    {
        readonly Transform _exhaust;
        int _enginesDown;
        public PlaceholderVisual(Transform root) { _exhaust = root != null ? root.Find("exhaust") : null; }

        public void SetPartHealth(string partName, float health01) { }
        public void DestroyPart(string partName)
        {
            if (partName != null && partName.StartsWith("engine") && ++_enginesDown >= 2 && _exhaust != null) _exhaust.gameObject.SetActive(false);
        }
        public void HullHit(Vector3 worldPoint, Vector3 worldNormal, float radiusMetres, float severity01) { }
        public void SetHullDamage(float damage01) { }
        public void ResetDamage() { _enginesDown = 0; if (_exhaust != null) _exhaust.gameObject.SetActive(true); }
    }

    /// <summary>A chunk thrown off a destroyed part: flies straight with its spin, then goes away.</summary>
    public sealed class DebrisFlight : MonoBehaviour
    {
        Vector3 _velocity, _spin;
        float _life;

        public static void Launch(Transform piece, Vector3 velocity, Vector3 spinDegPerSec, float life)
        {
            var d = piece.gameObject.GetComponent<DebrisFlight>() ?? piece.gameObject.AddComponent<DebrisFlight>();
            d._velocity = velocity; d._spin = spinDegPerSec; d._life = life;
            piece.gameObject.SetActive(true);
        }

        void Update()
        {
            float dt = Time.deltaTime * (GameWorld.Current != null ? GameWorld.Current.Sim.TimeScale : 1f);
            transform.position += _velocity * dt;
            transform.Rotate(_spin * dt, Space.Self);
            _life -= Time.deltaTime;
            if (_life <= 0f) Destroy(gameObject);
        }
    }
}
