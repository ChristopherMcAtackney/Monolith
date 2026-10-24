using System.Collections.Generic;
using Monolith.Juice;
using Monolith.ShipArt;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// Lets the juice drive a ship model made by the ship art pipeline (a <see cref="ShipDamage"/> on its prefab) without referencing it:
    /// the juice asks for parts, hull hits and wrecks, and this passes them to <see cref="IShipDamage"/>. Lives in Game, the one assembly
    /// that may see both systems.
    /// </summary>
    public sealed class ShipArtJuiceDamage : IJuiceShipDamage
    {
        readonly ShipDamage _dmg;
        readonly List<JuicePart> _parts = new List<JuicePart>();
        readonly List<Renderer> _hull = new List<Renderer>();
        readonly List<Collider> _colliders = new List<Collider>();
        readonly Transform _root;

        public ShipArtJuiceDamage(GameObject ship)
        {
            _root = ship.transform;
            _dmg = ship.GetComponent<ShipDamage>();
            Refresh();
            foreach (var c in ship.GetComponentsInChildren<Collider>(true)) _colliders.Add(c);
            // The carcass: renderers that are not part of any subsystem and not a lower LOD.
            var subs = new HashSet<Transform>();
            foreach (var s in _dmg.Subsystems) subs.Add(s.transform);
            foreach (var r in ship.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name.Contains("_LOD1") || r.name.Contains("_LOD2") || r.name.EndsWith("_COL")) continue;
                bool inSub = false;
                for (var t = r.transform; t != null && t != _root; t = t.parent) if (subs.Contains(t)) { inSub = true; break; }
                if (!inSub) _hull.Add(r);
            }
        }

        void Refresh()
        {
            _parts.Clear();
            foreach (var s in _dmg.Subsystems)
                _parts.Add(new JuicePart { Name = s.name, Kind = s.Kind, Pivot = s.transform, Radius = s.Radius, Destroyed = s.Destroyed });
        }

        public IReadOnlyList<JuicePart> Parts { get { Refresh(); return _parts; } }
        public float HullDamage => _dmg != null ? _dmg.HullDamage : 0f;
        public IReadOnlyList<Renderer> HullRenderers => _hull;
        public IReadOnlyList<Collider> Colliders => _colliders;

        public void HullHit(Vector3 worldPoint, Vector3 worldNormal, float radiusMetres, float severity01) { if (_dmg != null) _dmg.AddHullHit(worldPoint, worldNormal, radiusMetres, severity01); }

        public void SetHullDamage(float fraction01) { if (_dmg != null) _dmg.SetHullDamage(fraction01); }

        public List<JuiceChunk> DestroyPart(string partName)
        {
            var list = new List<JuiceChunk>();
            var sub = _dmg.Find(partName);
            if (sub == null || sub.Destroyed) return list;
            // Wreck first (this shows the debris at rest), then let the debris go: it leaves the hierarchy and the effects own it.
            sub.SetState(SubsystemState.Destroyed, 0f);
            foreach (var (piece, v, spin) in sub.DetachDebris()) list.Add(new JuiceChunk { Piece = piece, Velocity = v, SpinDegPerSec = spin });
            return list;
        }

        public void Hide()
        {
            foreach (var r in _root.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (var c in _colliders) if (c != null) c.enabled = false;
            foreach (var l in _root.GetComponentsInChildren<LODGroup>(true)) l.enabled = false;
        }
    }
}
