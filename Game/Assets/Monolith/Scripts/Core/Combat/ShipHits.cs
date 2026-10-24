using UnityEngine;

namespace Monolith.Core
{
    /// <summary>Where a swept shot met a ship.</summary>
    public struct ShipHit
    {
        /// <summary>Fraction of the shot's path this tick (0 at the start): the same time fraction for every ship, so the smallest wins.</summary>
        public float T;
        public Vector3 Point, Normal;
        /// <summary>The part struck directly (a protruding turret, an engine bell), or -1.</summary>
        public int Part;
        /// <summary>The shot met the shield bubble, not the hull.</summary>
        public bool OnShield;
    }

    /// <summary>
    /// Hit tests of a moving shot against a ship's <see cref="HitShape"/>, shared by weapons (and available to the AI for line-of-fire checks).
    /// The segment is the shot's path over this tick. The ship has already moved this tick, so the segment's start is shifted by the
    /// ship's own displacement: that is the shot's path as the ship sees it, and neither a fast shot nor a fast ship can tunnel through.
    /// </summary>
    public static class ShipHits
    {
        public static bool Segment(ShipState ship, ShipCombat combat, Vector3 from, Vector3 to, out ShipHit hit)
        {
            hit = default;
            var shape = combat.Shape;
            Vector3 shifted = from + (ship.Position - ship.PreviousPosition);
            float reach = Mathf.Max(shape.BoundRadius, ship.Radius) * 1.2f + 6f;
            Vector3 near = Geo.ClosestOnSegment(shifted, to, ship.Position);
            if ((near - ship.Position).sqrMagnitude > reach * reach) return false;

            var inv = Quaternion.Inverse(ship.Rotation);
            Vector3 a = inv * (shifted - ship.Position), b = inv * (to - ship.Position);
            float best = float.MaxValue;
            Vector3 normal = Vector3.up;
            int part = -1;
            bool onShield = false;

            if (combat.Shield > 0f && shape.HasBubble)
            {
                if (Geo.SegmentEllipsoid(a, b, shape.ShieldCentre, shape.ShieldRadii, out float t, out Vector3 n)) { best = t; normal = n; onShield = true; }
            }
            else
            {
                for (int i = 0; i < shape.Boxes.Length; i++)
                    if (Geo.SegmentBox(a, b, shape.Boxes[i].Centre, shape.Boxes[i].Half, out float t, out Vector3 n) && t < best) { best = t; normal = n; }
                var parts = combat.Parts;
                for (int i = 0; i < parts.Count; i++)
                {
                    var p = parts[i];
                    if (p.Destroyed) continue;
                    if (Geo.SegmentSphere(a, b, p.LocalPosition, p.Radius, out float t, out Vector3 n) && t < best) { best = t; normal = n; part = i; }
                }
            }
            if (best == float.MaxValue) return false;
            hit.T = best;
            hit.Point = ship.Position + ship.Rotation * (a + (b - a) * best);
            hit.Normal = ship.Rotation * normal;
            hit.Part = part;
            hit.OnShield = onShield;
            return true;
        }

        /// <summary>The distance from a world point to the nearest part of a ship's hull (0 inside): for blasts.</summary>
        public static float DistanceToHull(ShipState ship, ShipCombat combat, Vector3 world, out Vector3 nearestWorld)
        {
            var inv = Quaternion.Inverse(ship.Rotation);
            Vector3 p = inv * (world - ship.Position);
            float best = float.MaxValue;
            Vector3 bestPoint = Vector3.zero;
            var boxes = combat.Shape.Boxes;
            for (int i = 0; i < boxes.Length; i++)
            {
                Vector3 q = Geo.ClosestOnBox(p, boxes[i].Centre, boxes[i].Half);
                float d = (q - p).sqrMagnitude;
                if (d < best) { best = d; bestPoint = q; }
            }
            if (best == float.MaxValue) { nearestWorld = ship.Position; return Vector3.Distance(world, ship.Position); }
            nearestWorld = ship.Position + ship.Rotation * bestPoint;
            return Mathf.Sqrt(best);
        }

        /// <summary>The world-space oriented boxes of a ship's hull (collisions).</summary>
        public static int Boxes(ShipState ship, ShipCombat combat, Obb[] into)
        {
            var boxes = combat.Shape.Boxes;
            int n = Mathf.Min(boxes.Length, into.Length);
            for (int i = 0; i < n; i++)
                into[i] = new Obb(ship.Position + ship.Rotation * boxes[i].Centre, boxes[i].Half, ship.Rotation);
            return n;
        }
    }

    /// <summary>Who fights whom.</summary>
    public static class Teams
    {
        /// <summary>Different sides, and neither is neutral (a negative team never fights and is never hit by bolts).</summary>
        public static bool Hostile(int a, int b) => a != b && a >= 0 && b >= 0;
    }
}
