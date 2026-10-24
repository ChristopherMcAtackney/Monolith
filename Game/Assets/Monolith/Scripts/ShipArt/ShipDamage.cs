using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.ShipArt
{
    /// <summary>
    /// What the combat system calls to make a ship look damaged. It owns no game rules (hit points, what a hit does): those live in
    /// the combat system, which tells this component what to show.
    /// </summary>
    public interface IShipDamage
    {
        IReadOnlyList<ShipSubsystem> Subsystems { get; }
        ShipSubsystem Find(string nameOrKind);
        /// <summary>Destroyed parts show their wreck and throw debris; health between 0 and 1 may show a damaged look.</summary>
        void SetSubsystemHealth(string nameOrKind, float health01);
        void DestroySubsystem(string nameOrKind, float debrisAge = 0f);
        /// <summary>A hit on the hull: scorches the plating there (a decal) and hurts the nearest damage site. Severity 0 to 1.</summary>
        void AddHullHit(Vector3 worldPoint, Vector3 worldNormal, float radiusMetres, float severity01);
        /// <summary>0 to 1: the whole hull's accumulated damage. Higher values show more and bigger scorches and open the damage sites.</summary>
        void SetHullDamage(float fraction01);
        /// <summary>Back to a factory-fresh ship.</summary>
        void ResetDamage();
        IReadOnlyList<ShipFxHook> Hooks(string kind = null);
        event Action<ShipSubsystem, SubsystemState, SubsystemState> SubsystemChanged;
    }

    /// <summary>
    /// A ship model's damage presentation (see <see cref="IShipDamage"/>). Subsystems swap to their destroyed look; hull hits and
    /// accumulated damage paint scorch, crater and streak decals (HDRP decal projectors, a pooled ring so cost is bounded) and open the
    /// model's damage sites. FX hooks (sparks, vents) are exposed for the effects system. Added to the prefab by ShipArtSetup.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShipDamage : MonoBehaviour, IShipDamage
    {
        /// <summary>Atlas of scorch decals: 4 x 4 cells, set by ShipArtSetup. Rows 0..3 = small scorches, blasts, craters, streaks.</summary>
        [SerializeField] Material _decalMaterial;
        [SerializeField] Vector3[] _damagePoints = new Vector3[0], _damageNormals = new Vector3[0];
        [SerializeField] int _maxDecals = 96;
        /// <summary>1 for a capital ship; small ships (fighters) scale every scorch and the depth of its projection down, so a hit's radius (metres, set for big hulls) still reads right.</summary>
        [SerializeField] float _scorchScale = 1f;

        readonly List<ShipSubsystem> _subs = new List<ShipSubsystem>();
        readonly List<ShipFxHook> _hooks = new List<ShipFxHook>();
        readonly List<HullDamageSite> _sites = new List<HullDamageSite>();
        readonly List<DecalProjector> _decals = new List<DecalProjector>();
        int _next, _planApplied;
        float _hullDamage;
        float[] _siteHit = new float[0];
        bool _collected;

        public IReadOnlyList<ShipSubsystem> Subsystems { get { Collect(); return _subs; } }
        public IReadOnlyList<HullDamageSite> Sites { get { Collect(); return _sites; } }
        public float HullDamage => _hullDamage;
        public int DecalCount => _decals.FindAll(d => d != null && d.gameObject.activeSelf).Count;
        public event Action<ShipSubsystem, SubsystemState, SubsystemState> SubsystemChanged;

        public void Configure(Material decalMaterial, Vector3[] points, Vector3[] normals, float scorchScale = 1f)
        {
            _decalMaterial = decalMaterial; _damagePoints = points; _damageNormals = normals; _scorchScale = Mathf.Max(0.02f, scorchScale);
        }

        void Awake() => Collect();

        void Collect()
        {
            if (_collected) return;
            _collected = true;
            GetComponentsInChildren(true, _subs);
            GetComponentsInChildren(true, _hooks);
            GetComponentsInChildren(true, _sites);
            _siteHit = new float[_sites.Count];
            foreach (var s in _subs) s.StateChanged += (sub, a, b) => SubsystemChanged?.Invoke(sub, a, b);
        }

        public ShipSubsystem Find(string nameOrKind)
        {
            Collect();
            foreach (var s in _subs) if (s.name == nameOrKind) return s;
            foreach (var s in _subs) if (s.Kind == nameOrKind && !s.Destroyed) return s;
            return null;
        }

        public IReadOnlyList<ShipFxHook> Hooks(string kind = null)
        {
            Collect();
            return kind == null ? _hooks : _hooks.FindAll(h => h.Kind == kind);
        }

        public void SetSubsystemHealth(string nameOrKind, float health01) => Find(nameOrKind)?.SetHealth(health01);

        public void DestroySubsystem(string nameOrKind, float debrisAge = 0f) => Find(nameOrKind)?.SetState(SubsystemState.Destroyed, debrisAge);

        public void AddHullHit(Vector3 worldPoint, Vector3 worldNormal, float radiusMetres, float severity01)
        {
            Collect();
            severity01 = Mathf.Clamp01(severity01);
            int row = severity01 < 0.25f ? 0 : severity01 < 0.55f ? 1 : severity01 < 0.85f ? 2 : 3;
            int seed = Mathf.RoundToInt(worldPoint.x * 7.1f + worldPoint.y * 13.7f + worldPoint.z * 3.3f);
            PlaceDecal(worldPoint, worldNormal, Mathf.Max(0.5f, radiusMetres) * 4.0f * _scorchScale, row, seed);
            for (int i = 0; i < _sites.Count; i++)
            {
                float d = Vector3.Distance(_sites[i].transform.position, worldPoint);
                float reach = _sites[i].Radius * 2.5f;
                if (d > reach) continue;
                _siteHit[i] = Mathf.Clamp01(_siteHit[i] + severity01 * (1f - d / reach) * 0.8f);
                _sites[i].SetDamage(Mathf.Max(_siteHit[i], _sites[i].Damage01));
            }
        }

        /// <summary>The hull's progressive damage is a fixed, ordered plan of hits (the model's sampled hull points): damage f shows the first f of it, larger.</summary>
        public void SetHullDamage(float fraction01)
        {
            Collect();
            _hullDamage = Mathf.Clamp01(fraction01);
            int n = _damagePoints.Length;
            int want = Mathf.RoundToInt(_hullDamage * Mathf.Min(n, _maxDecals));
            if (want < _planApplied) { ClearDecals(); _planApplied = 0; }
            for (; _planApplied < want; _planApplied++)
            {
                float sev = Mathf.Clamp01(0.15f + 0.85f * Mathf.Abs(Mathf.Sin(_planApplied * 2.399f)) * (0.4f + _hullDamage));
                var p = transform.TransformPoint(_damagePoints[_planApplied]);
                var nrm = transform.TransformDirection(_damageNormals[_planApplied]);
                AddHullHit(p, nrm, 1.5f + 3.5f * sev, sev * Mathf.Lerp(0.4f, 1f, _hullDamage));
            }
            // Damage sites open in order of exposure once the hull is hurt badly enough.
            for (int i = 0; i < _sites.Count; i++)
            {
                float open = Mathf.Clamp01((_hullDamage - 0.25f - 0.08f * i) / 0.35f);
                _sites[i].SetDamage(Mathf.Max(_siteHit[i], open));
            }
        }

        public void ResetDamage()
        {
            Collect();
            ClearDecals();
            _planApplied = 0; _hullDamage = 0f;
            for (int i = 0; i < _siteHit.Length; i++) _siteHit[i] = 0f;
            foreach (var s in _sites) s.SetDamage(0f);
            foreach (var s in _subs) s.SetState(SubsystemState.Intact);
        }

        void ClearDecals()
        {
            foreach (var d in _decals) if (d != null) d.gameObject.SetActive(false);
            _next = 0;
        }

        void PlaceDecal(Vector3 point, Vector3 normal, float size, int row, int seed)
        {
            if (_decalMaterial == null) return;
            DecalProjector dp = null;
            foreach (var d in _decals) if (d != null && !d.gameObject.activeSelf) { dp = d; break; }
            if (dp == null && _decals.Count < _maxDecals)
            {
                var go = new GameObject("MN_HullDecal");
                go.transform.SetParent(transform, false);
                dp = go.AddComponent<DecalProjector>();
                dp.material = _decalMaterial;
                dp.drawDistance = 4000f;
                dp.fadeScale = 0.9f;
                dp.startAngleFade = 90f;
                dp.endAngleFade = 90f;
                _decals.Add(dp);
            }
            else if (dp == null)
            {
                dp = _decals[_next];
                _next = (_next + 1) % _maxDecals;
            }
            var rng = new System.Random(seed);
            float spin = (float)rng.NextDouble() * 360f;
            int col = rng.Next(4);
            var n = normal.normalized;
            var t = dp.transform;
            t.gameObject.SetActive(true);
            t.position = point + n * (0.6f * _scorchScale);          // the projection volume reaches 1.25 m below the surface point and 2.25 m above it (plates stand up to half a metre)
            t.rotation = Quaternion.LookRotation(-n, Mathf.Abs(Vector3.Dot(n, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up) * Quaternion.Euler(0f, 0f, spin);
            dp.size = new Vector3(size, size, 3.5f * _scorchScale);
            dp.pivot = Vector3.zero;
            dp.uvScale = new Vector2(0.25f, 0.25f);
            dp.uvBias = new Vector2(col * 0.25f, row * 0.25f);
        }
    }
}
