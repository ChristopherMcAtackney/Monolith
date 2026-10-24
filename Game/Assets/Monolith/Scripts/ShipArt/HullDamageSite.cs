using UnityEngine;

namespace Monolith.ShipArt
{
    /// <summary>
    /// A place on the hull that can be torn open: an empty named <c>DMG_site_NN</c> (forward = the outward surface normal) with a hidden
    /// breach mesh under it (a crater of torn plating with glowing embers). Hits near the site accumulate in <see cref="Damage01"/>; past
    /// <see cref="BreachAt"/> the breach shows.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HullDamageSite : MonoBehaviour
    {
        public float Radius = 5f;
        public float BreachAt = 0.55f;
        [SerializeField] GameObject _breach;
        public float Damage01 { get; private set; }
        public bool Breached => _breach != null && _breach.activeSelf;

        public void Configure(GameObject breach, float radius)
        {
            _breach = breach;
            Radius = radius;
            if (_breach != null) _breach.SetActive(false);
        }

        public void SetDamage(float d01)
        {
            Damage01 = Mathf.Clamp01(d01);
            if (_breach != null) _breach.SetActive(Damage01 >= BreachAt);
        }
    }
}
