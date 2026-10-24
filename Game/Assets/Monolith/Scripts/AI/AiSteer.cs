using Monolith.Core;
using UnityEngine;

namespace Monolith.AI
{
    /// <summary>
    /// Turning a ship to a direction through its stick, the way a pilot would, knowing the ship's handling (<see cref="ShipState.TurnRate"/>
    /// and <see cref="ShipState.TurnLag"/>, FS2's rotation time and rotdamp). The flight model chases the goal turn rate with a lag, so the
    /// controller asks for a rate proportional to the angle still to go after the turn already under way has run out:
    /// <c>rate = K (error - lag x current rate)</c>, about critically damped, the whole goal vector scaled down together when it is more than
    /// the ship can turn (so a diagonal turn stays diagonal). Roll banks into big turns and levels to a wanted "up" otherwise; the flight
    /// model's auto-bank (half the yaw rate) is allowed for.
    /// </summary>
    public static class AiSteer
    {
        /// <summary>The flight model's auto-bank: yawing rolls the ship by this share of the yaw rate (FS2, flight_rules.json autoBankFraction).</summary>
        public const float AutoBank = 0.5f;

        /// <summary>
        /// Set pitch, yaw and roll in <paramref name="input"/> to turn <paramref name="me"/> towards <paramref name="goalDir"/> (world, any length).
        /// <paramref name="turnScale"/> is the share of the turn rate the pilot uses (FS2 slows Earth's turns on easy levels).
        /// <paramref name="up"/>, if given, is the world direction the pilot wants overhead when the turn is small (formation, capital ships);
        /// <paramref name="bank"/> 0..1 is how much the pilot rolls into big turns.
        /// Returns the angle still to go, radians.
        /// </summary>
        public static float TurnTo(ShipState me, Vector3 goalDir, float turnScale, ref ControlInput input, Vector3? up = null, float bank = 0.6f, Vector3 goalRate = default)
        {
            if (goalDir.sqrMagnitude < 1e-8f) goalDir = me.Forward;
            var inv = Quaternion.Inverse(me.Rotation);
            Vector3 d = (inv * goalDir).normalized;
            float angle = Mathf.Acos(Mathf.Clamp(d.z, -1f, 1f));
            Vector2 lat = new Vector2(d.x, d.y);
            float lm = lat.magnitude;
            Vector2 axis = lm > 1e-4f ? lat / lm : (angle > 1f ? new Vector2(0f, 1f) : Vector2.zero);   // straight behind: pull up (a loop)

            float lag = Mathf.Max(0.05f, me.TurnLag);
            float k = 2.2f / lag;
            Vector3 rate = me.AngularVelocity;                 // +x nose down, +y nose right, +z roll left
            float yawRate = rate.y, pitchUpRate = -rate.x, rollRightRate = -rate.z;
            Vector3 max = me.TurnRate * Mathf.Clamp(turnScale, 0.05f, 1f);

            // Pitch and yaw: one goal vector, scaled down as a whole if it asks for more than the ship turns. A goal that is itself turning (a lead
            // point on a manoeuvring target) is fed forward, or the nose would trail it by about 1.5 x lag x its rate.
            Vector3 gr = inv * goalRate;                     // world angular velocity of the goal direction, ship-local (+x nose down, +y nose right)
            float ffYaw = gr.y, ffPitchUp = -gr.x;
            float gy = ffYaw + k * (axis.x * angle - lag * (yawRate - ffYaw));
            float gp = ffPitchUp + k * (axis.y * angle - lag * (pitchUpRate - ffPitchUp));
            float over = Mathf.Max(Mathf.Abs(gy) / Mathf.Max(1e-3f, max.y), Mathf.Abs(gp) / Mathf.Max(1e-3f, max.x));
            if (over > 1f) { gy /= over; gp /= over; }
            input.Yaw = Mathf.Clamp(gy / Mathf.Max(1e-3f, me.TurnRate.y), -1f, 1f);
            input.Pitch = Mathf.Clamp(gp / Mathf.Max(1e-3f, me.TurnRate.x), -1f, 1f);

            // Roll: into a big turn (the target overhead, or below if that is nearer), else level to the wanted up.
            float rollErr = 0f, weight = 0f;
            if (bank > 0f && angle > 0.2f && lm > 0.05f)
            {
                float phi = Mathf.Atan2(d.x, d.y);                     // from overhead, round to the right
                if (Mathf.Abs(phi) > 1.9f) phi -= Mathf.Sign(phi) * Mathf.PI;   // well below: put it under the canopy instead
                rollErr = phi;
                weight = bank * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 0.7f, angle));
            }
            if (up.HasValue && weight < 1f)
            {
                Vector3 u = inv * up.Value;
                if (new Vector2(u.x, u.y).sqrMagnitude > 1e-4f)
                {
                    float levelErr = Mathf.Atan2(u.x, u.y);             // roll right by this to bring "up" overhead
                    rollErr = Mathf.Lerp(levelErr, rollErr, weight);
                    weight = 1f;
                }
            }
            float groll = weight > 0f ? k * 0.8f * (rollErr - lag * rollRightRate) * Mathf.Max(weight, 0.0001f) : -k * 0.5f * lag * rollRightRate;
            groll = Mathf.Clamp(groll, -max.z, max.z);
            // The flight model adds roll right of AutoBank x the yaw rate asked for; take it out so the roll is what was wanted.
            float autoBank = input.Yaw * me.TurnRate.y * AutoBank;
            input.Roll = Mathf.Clamp((groll - autoBank) / Mathf.Max(1e-3f, me.TurnRate.z), -1f, 1f);
            return angle;
        }

        /// <summary>The throttle setting (0..1) that asks for <paramref name="speed"/> m/s.</summary>
        public static float ThrottleFor(ShipState me, float speed) => me.TopSpeed > 0.1f ? Mathf.Clamp01(speed / me.TopSpeed) : 0f;

        /// <summary>Unit direction from a ship to a point, or its nose if the point is on top of it.</summary>
        public static Vector3 Dir(ShipState me, Vector3 to)
        {
            Vector3 d = to - me.Position;
            return d.sqrMagnitude > 1e-4f ? d.normalized : me.Forward;
        }
    }
}
