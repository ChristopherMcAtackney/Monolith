using UnityEngine;

namespace Monolith.ShipArt
{
    /// <summary>
    /// Where an effect may be spawned on a ship: an empty named <c>FX_&lt;kind&gt;_&lt;NN&gt;</c> placed by the model script on the hull,
    /// forward (+z) pointing out of the surface. Kinds: "spark" (arcing from torn plating), "vent" (atmosphere or coolant venting
    /// from a breach), "fire", "smoke". <see cref="Size"/> is the opening's size in metres. The effects system reads
    /// <see cref="ShipDamage.Hooks"/> and spawns its own particles: this class only marks the place.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShipFxHook : MonoBehaviour
    {
        public string Kind;
        public float Size = 1f;
        public Vector3 Direction => transform.forward;
    }
}
