using System.Collections.Generic;
using UnityEngine;

namespace Monolith.Juice
{
    /// <summary>One separately destroyable part of a ship model (a turret, an engine, the bridge), as the juice sees it.</summary>
    public struct JuicePart
    {
        public string Name, Kind;
        public Transform Pivot;
        public float Radius;
        public bool Destroyed;
    }

    /// <summary>A chunk of a wrecked part, freed from the ship: it flies from now on under the effects' control.</summary>
    public struct JuiceChunk
    {
        public Transform Piece;
        /// <summary>World velocity relative to the ship (m/s) and spin (degrees per second).</summary>
        public Vector3 Velocity, SpinDegPerSec;
    }

    /// <summary>
    /// What the juice may ask of a ship model that has real parts and damage looks (the Game assembly adapts the ship art's
    /// <c>IShipDamage</c> to this, so the juice never references the art). Without one, a ship is just a visual that is cut into pieces.
    /// </summary>
    public interface IJuiceShipDamage
    {
        IReadOnlyList<JuicePart> Parts { get; }
        /// <summary>0 to 1: how much hull damage is showing now.</summary>
        float HullDamage { get; }
        /// <summary>A hit on the hull at a world point (a scorch decal, a damage site that opens). Severity 0 to 1.</summary>
        void HullHit(Vector3 worldPoint, Vector3 worldNormal, float radiusMetres, float severity01);
        /// <summary>0 to 1: the whole hull's accumulated damage (more and bigger scorches, then breaches).</summary>
        void SetHullDamage(float fraction01);
        /// <summary>Swap a part to its wreck and let its debris go. Returns the chunks (empty when the part is unknown or already gone).</summary>
        List<JuiceChunk> DestroyPart(string partName);
        /// <summary>The pieces of the hull that stay in place when parts are destroyed (the carcass renderers), for the break-up.</summary>
        IReadOnlyList<Renderer> HullRenderers { get; }
        /// <summary>The ship's collision proxies, for bolts and beams to hit the real shape.</summary>
        IReadOnlyList<Collider> Colliders { get; }
        /// <summary>Hide the whole model (the break-up replaced it).</summary>
        void Hide();
    }
}
