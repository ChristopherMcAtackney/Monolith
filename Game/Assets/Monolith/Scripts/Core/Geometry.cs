using System;
using UnityEngine;

namespace Monolith.Core
{
    /// <summary>
    /// Pure-maths shape tests the combat systems share (swept bolts against hulls and shield bubbles, ship-ship contact, the lead
    /// solver). No Unity physics: ships are <see cref="ShipState"/> poses plus a <see cref="HitShape"/>, so combat runs the same with
    /// or without anything on screen, and a 1,800 m/s bolt cannot tunnel through a 10 m fighter because every test is a swept segment.
    /// All "local" tests take points already in the shape's own frame.
    /// </summary>
    public static class Geo
    {
        /// <summary>
        /// A segment <paramref name="a"/> to <paramref name="b"/> against an axis-aligned box (centre, half extents). On a hit
        /// <paramref name="t"/> is the fraction along the segment (0 at a) of the entry point and <paramref name="normal"/> the face hit.
        /// A segment that starts inside reports t = 0 and the normal against its travel.
        /// </summary>
        public static bool SegmentBox(Vector3 a, Vector3 b, Vector3 centre, Vector3 half, out float t, out Vector3 normal)
        {
            t = 0f; normal = Vector3.up;
            Vector3 p = a - centre, d = b - a;
            float t0 = 0f, t1 = 1f;
            int axis = -1; float sign = 0f;
            for (int i = 0; i < 3; i++)
            {
                float pi = p[i], di = d[i], hi = half[i];
                if (Mathf.Abs(di) < 1e-9f)
                {
                    if (pi < -hi || pi > hi) return false;
                    continue;
                }
                float inv = 1f / di;
                float tn = (-hi - pi) * inv, tf = (hi - pi) * inv;
                float s = -1f;                       // entering through the min face (normal -axis)
                if (tn > tf) { float tmp = tn; tn = tf; tf = tmp; s = 1f; }
                if (tn > t0) { t0 = tn; axis = i; sign = s; }
                if (tf < t1) t1 = tf;
                if (t0 > t1) return false;
            }
            t = t0;
            if (axis < 0)
            {
                normal = d.sqrMagnitude > 1e-12f ? -d.normalized : Vector3.up;
                return true;
            }
            normal = Vector3.zero; normal[axis] = sign;
            return true;
        }

        /// <summary>A segment against a sphere. A segment that starts inside reports t = 0.</summary>
        public static bool SegmentSphere(Vector3 a, Vector3 b, Vector3 centre, float radius, out float t, out Vector3 normal)
        {
            t = 0f; normal = Vector3.up;
            Vector3 d = b - a;
            float len2 = d.sqrMagnitude;
            Vector3 m = a - centre;
            float cc = m.sqrMagnitude - radius * radius;
            if (len2 < 1e-12f)
            {
                if (cc > 0f) return false;
                normal = m.sqrMagnitude > 1e-12f ? m.normalized : Vector3.up;
                return true;
            }
            float bb = Vector3.Dot(m, d);
            if (cc > 0f && bb > 0f) return false;
            float disc = bb * bb - len2 * cc;
            if (disc < 0f) return false;
            float tt = (-bb - Mathf.Sqrt(disc)) / len2;
            if (tt < 0f) tt = 0f;
            if (tt > 1f) return false;
            t = tt;
            Vector3 hit = a + d * t;
            Vector3 n = hit - centre;
            normal = n.sqrMagnitude > 1e-12f ? n.normalized : -d.normalized;
            return true;
        }

        /// <summary>A segment against an axis-aligned ellipsoid (centre, radii): scale to a unit sphere and test there.</summary>
        public static bool SegmentEllipsoid(Vector3 a, Vector3 b, Vector3 centre, Vector3 radii, out float t, out Vector3 normal)
        {
            Vector3 inv = new Vector3(1f / radii.x, 1f / radii.y, 1f / radii.z);
            Vector3 sa = Vector3.Scale(a - centre, inv), sb = Vector3.Scale(b - centre, inv);
            if (!SegmentSphere(sa, sb, Vector3.zero, 1f, out t, out var n)) { normal = Vector3.up; return false; }
            // The normal of an ellipsoid at a point is the gradient: unit-sphere normal divided by the radii.
            Vector3 g = Vector3.Scale(n, inv);
            normal = g.sqrMagnitude > 1e-12f ? g.normalized : n;
            return true;
        }

        /// <summary>The point of a box (centre, half extents) nearest to <paramref name="p"/>.</summary>
        public static Vector3 ClosestOnBox(Vector3 p, Vector3 centre, Vector3 half)
        {
            Vector3 q = p - centre;
            return centre + new Vector3(Mathf.Clamp(q.x, -half.x, half.x), Mathf.Clamp(q.y, -half.y, half.y), Mathf.Clamp(q.z, -half.z, half.z));
        }

        /// <summary>The point of segment a-b nearest to <paramref name="p"/>.</summary>
        public static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 d = b - a;
            float l2 = d.sqrMagnitude;
            if (l2 < 1e-12f) return a;
            return a + d * Mathf.Clamp01(Vector3.Dot(p - a, d) / l2);
        }

        /// <summary>
        /// Where to aim a projectile that leaves <paramref name="shooterPos"/> with the shooter's velocity plus <paramref name="muzzleSpeed"/>
        /// along the aim direction, so that it meets a target moving at constant velocity. <paramref name="aimDir"/> is the unit direction
        /// to fire along, <paramref name="time"/> the flight time, <paramref name="intercept"/> the meeting point. False when the target
        /// cannot be reached (it outruns the shot) or sits on top of the shooter.
        /// </summary>
        public static bool SolveIntercept(Vector3 shooterPos, Vector3 shooterVel, Vector3 targetPos, Vector3 targetVel, float muzzleSpeed,
                                          out Vector3 aimDir, out float time, out Vector3 intercept)
        {
            aimDir = Vector3.forward; time = 0f; intercept = targetPos;
            Vector3 r = targetPos - shooterPos, w = targetVel - shooterVel;
            float s2 = muzzleSpeed * muzzleSpeed;
            // |r + w t| = s t  ->  (w.w - s^2) t^2 + 2 r.w t + r.r = 0, smallest positive root.
            float a = Vector3.Dot(w, w) - s2, b = 2f * Vector3.Dot(r, w), c = Vector3.Dot(r, r);
            if (c < 1e-6f) return false;
            float tt;
            if (Mathf.Abs(a) < 1e-4f)
            {
                if (Mathf.Abs(b) < 1e-9f) return false;
                tt = -c / b;
            }
            else
            {
                float disc = b * b - 4f * a * c;
                if (disc < 0f) return false;
                float sq = Mathf.Sqrt(disc);
                float t1 = (-b - sq) / (2f * a), t2 = (-b + sq) / (2f * a);
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                tt = t1 > 1e-4f ? t1 : t2;
            }
            if (tt <= 1e-4f) return false;
            time = tt;
            Vector3 rel = r + w * tt;                         // where the target is, relative to the shooter, when the shot arrives
            aimDir = rel / (muzzleSpeed * tt);                // direction of the muzzle velocity
            if (aimDir.sqrMagnitude < 1e-9f) return false;
            aimDir.Normalize();
            intercept = targetPos + targetVel * tt;
            return true;
        }
    }

    /// <summary>An oriented box in world space (for ship-ship contact).</summary>
    public struct Obb
    {
        public Vector3 Centre, Half, AxisX, AxisY, AxisZ;

        public Obb(Vector3 centre, Vector3 half, Quaternion rot)
        {
            Centre = centre; Half = half;
            AxisX = rot * Vector3.right; AxisY = rot * Vector3.up; AxisZ = rot * Vector3.forward;
        }

        public Vector3 Axis(int i) => i == 0 ? AxisX : i == 1 ? AxisY : AxisZ;
        public float HalfOn(int i) => i == 0 ? Half.x : i == 1 ? Half.y : Half.z;

        /// <summary>Half the box's extent along <paramref name="dir"/> (unit).</summary>
        public float Extent(Vector3 dir) =>
            Half.x * Mathf.Abs(Vector3.Dot(AxisX, dir)) + Half.y * Mathf.Abs(Vector3.Dot(AxisY, dir)) + Half.z * Mathf.Abs(Vector3.Dot(AxisZ, dir));

        /// <summary>
        /// Separating-axis overlap test (15 axes). On overlap: <paramref name="depth"/> is the smallest penetration and
        /// <paramref name="normal"/> the unit axis along it, pointing from <paramref name="b"/> towards <paramref name="a"/>.
        /// </summary>
        public static bool Overlap(in Obb a, in Obb b, out Vector3 normal, out float depth)
        {
            normal = Vector3.up; depth = float.MaxValue;
            Vector3 d = a.Centre - b.Centre;
            for (int i = 0; i < 3; i++)
                if (!TestAxis(a, b, a.Axis(i), d, ref normal, ref depth)) return false;
            for (int i = 0; i < 3; i++)
                if (!TestAxis(a, b, b.Axis(i), d, ref normal, ref depth)) return false;
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    Vector3 axis = Vector3.Cross(a.Axis(i), b.Axis(j));
                    float m = axis.magnitude;
                    if (m < 1e-4f) continue;                  // parallel edges: the face axes already cover them
                    if (!TestAxis(a, b, axis / m, d, ref normal, ref depth)) return false;
                }
            return true;
        }

        static bool TestAxis(in Obb a, in Obb b, Vector3 axis, Vector3 d, ref Vector3 best, ref float bestDepth)
        {
            float dist = Vector3.Dot(d, axis);
            float overlap = a.Extent(axis) + b.Extent(axis) - Mathf.Abs(dist);
            if (overlap <= 0f) return false;
            if (overlap < bestDepth)
            {
                bestDepth = overlap;
                best = dist >= 0f ? axis : -axis;
            }
            return true;
        }
    }
}
