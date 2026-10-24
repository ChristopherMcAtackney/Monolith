using Monolith.Core;
using UnityEngine;

namespace Monolith.AI
{
    /// <summary>
    /// Lead for a shot at a ship that may be turning. <see cref="Geo.SolveIntercept"/> assumes the target flies straight; a fighter in a hard turn
    /// (FS2's ships turn a full circle in two or three seconds, so they circle in a few tens of metres) is somewhere else by the time a bolt
    /// arrives. A pilot who reads the turn leads along the curve: the target's path is extrapolated with its present turn rate (its nose and so
    /// its velocity keep turning, as FS2's flight model makes them), and the shot's time of flight is solved against that path. <c>turnSense</c>
    /// 0..1 is how much of the turn the pilot reads (the brains pass their accuracy: a drone half-reads a turn, an ace reads it).
    /// </summary>
    public static class AiAim
    {
        public static bool Lead(Vector3 shooterPos, Vector3 shooterVel, ShipState target, Vector3 aimAt, float muzzleSpeed, float turnSense,
                                out Vector3 dir, out float time, out Vector3 meet)
        {
            if (!Geo.SolveIntercept(shooterPos, shooterVel, aimAt, target.Velocity, muzzleSpeed, out dir, out time, out meet)) return false;
            float spin = target.AngularVelocity.magnitude;
            if (turnSense <= 0.01f || spin < 0.05f) return true;
            // Where the aim point is relative to the ship's centre (a part), carried round with the ship.
            Vector3 offsetLocal = Quaternion.Inverse(target.Rotation) * (aimAt - target.Position);
            // Nobody reads a turn more than about a quarter circle ahead: past that the target will have done something else.
            turnSense *= Mathf.Clamp01(1.2f / Mathf.Max(1e-3f, spin * time));
            float t = time;
            for (int iter = 0; iter < 3; iter++)
            {
                Vector3 p = Curved(target, offsetLocal, t, turnSense);
                Vector3 rel = p - (shooterPos + shooterVel * t);
                float tNew = rel.magnitude / Mathf.Max(1f, muzzleSpeed);
                if (Mathf.Abs(tNew - t) < 0.002f) { t = tNew; break; }
                t = tNew;
            }
            Vector3 at = Curved(target, offsetLocal, t, turnSense);
            Vector3 r = at - (shooterPos + shooterVel * t);
            if (r.sqrMagnitude < 1e-4f) return true;
            dir = r.normalized; time = t; meet = at;
            return true;
        }

        /// <summary>The target's aim point after <paramref name="t"/> seconds if it keeps turning at its present rate (<paramref name="w"/> of it).</summary>
        static Vector3 Curved(ShipState s, Vector3 offsetLocal, float t, float w)
        {
            Vector3 spin = s.AngularVelocity * w;                 // ship-local, rad/s, Unity's rotation sense (as the flight model turns it)
            float rate = spin.magnitude;
            Vector3 axis = spin / Mathf.Max(1e-6f, rate);
            Vector3 vLocal = Quaternion.Inverse(s.Rotation) * s.Velocity;
            const int steps = 6;
            float h = t / steps;
            Vector3 p = s.Position;
            for (int i = 0; i < steps; i++)
            {
                var q = s.Rotation * Quaternion.AngleAxis(rate * (i + 0.5f) * h * Mathf.Rad2Deg, axis);
                p += q * vLocal * h;
            }
            var end = s.Rotation * Quaternion.AngleAxis(rate * t * Mathf.Rad2Deg, axis);
            return p + end * offsetLocal;
        }
    }
}
