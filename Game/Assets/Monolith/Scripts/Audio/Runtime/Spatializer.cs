using UnityEngine;

namespace Monolith.Audio
{
    /// <summary>The pilot's ears: where the listener is and how it moves (the player's ship, or a free camera).</summary>
    public struct ListenerPose
    {
        public Vector3 Position, Velocity;
        public Quaternion Rotation;
        public static ListenerPose Default => new ListenerPose { Rotation = Quaternion.identity };
    }

    /// <summary>
    /// Puts a sound in space: loudness falling with distance, left and right from where it is relative to the pilot's nose, a duller sound
    /// from behind and from far away, and a little Doppler for what flies past. The mixer is stereo, so this is a pan law and a filter,
    /// not a head model: enough to hear which side an explosion is on and how far it is.
    /// </summary>
    public static class Spatializer
    {
        /// <summary>Speed of sound for the Doppler shift (m/s). Space has none; this one is chosen to be audible at fighter speeds without being silly.</summary>
        public const float DopplerSpeed = 1800f;

        public struct Result { public float Gain, Pan, CutoffHz, Pitch, Distance; }

        public static Result Compute(in ListenerPose l, Vector3 pos, Vector3 vel, float minDist, float maxDist, bool doppler)
        {
            var r = new Result { Pitch = 1f, CutoffHz = 0f };
            Vector3 d = pos - l.Position;
            float dist = d.magnitude;
            r.Distance = dist;
            float md = Mathf.Max(minDist, 1f);
            float att = dist <= md ? 1f : Mathf.Pow(md / dist, 0.85f);
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(maxDist * 0.6f, maxDist, dist));
            r.Gain = att * fade;

            Vector3 local = Quaternion.Inverse(l.Rotation) * d;
            float horiz = Mathf.Sqrt(local.x * local.x + local.z * local.z);
            float side = horiz > 0.01f ? local.x / horiz : 0f;
            // sources very close to the ears do not swing hard from side to side
            float closeness = Mathf.Clamp01(dist / 40f);
            r.Pan = Mathf.Clamp(side * 0.85f * Mathf.Lerp(0.3f, 1f, closeness), -0.9f, 0.9f);

            float behind = dist > 0.01f ? Mathf.Clamp01(-local.z / dist) : 0f;
            float cut = 18000f * Mathf.Exp(-dist / 4200f);
            cut = Mathf.Max(cut, 1800f);
            cut *= Mathf.Lerp(1f, 0.42f, behind);
            r.CutoffHz = cut > 15000f ? 0f : cut;

            if (doppler && dist > 1f)
            {
                Vector3 dir = d / dist;
                float vListener = Vector3.Dot(l.Velocity, dir);      // toward the source is positive
                float vSource = Vector3.Dot(vel, dir);               // away from the listener is positive
                r.Pitch = Mathf.Clamp((DopplerSpeed + vListener) / (DopplerSpeed + vSource), 0.82f, 1.22f);
            }
            return r;
        }
    }
}
