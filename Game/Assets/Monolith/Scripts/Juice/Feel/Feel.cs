using Monolith.Core;
using UnityEngine;

namespace Monolith.Juice
{
    /// <summary>
    /// Turns "something happened over there" into shake, rumble and (optionally) a freeze for the player, by distance and by whether
    /// the player earned it. The research rule (design section 9): success gets the loudest reaction. A kill the player makes is
    /// stronger than the same blast somewhere else; hits and blasts that merely happen are smaller; nothing here changes
    /// what is drawn, so nothing can hide the target or the HUD.
    /// </summary>
    public sealed class Feel
    {
        public const float SuccessTrauma = 1.55f, SuccessRumble = 1.3f;
        readonly JuiceContext _c;

        public Feel(JuiceContext c) { _c = c; }

        /// <summary>
        /// A blast, hit or impact at <paramref name="pos"/>. <paramref name="trauma"/> is the shake it adds at the source, fading with
        /// distance (half strength at <paramref name="halfDistance"/>); rumble motors likewise (more gently).
        /// </summary>
        public void Impact(Vector3 pos, float trauma, float halfDistance, float rumbleLow, float rumbleHigh, float rumbleSeconds, bool success = false)
        {
            float d = _c.DistanceToCamera(pos);
            float fall = JuiceContext.Falloff(d, halfDistance);
            float t = trauma * fall * (success ? SuccessTrauma : 1f);
            _c.Trauma.Add(t);
            float rf = Mathf.Sqrt(fall) * (success ? SuccessRumble : 1f);
            _c.Rumble.Pulse(rumbleLow * rf, rumbleHigh * rf, rumbleSeconds);
        }

        /// <summary>The freeze, only when the player made the kill and the switch is on.</summary>
        public void MaybeHitStop(float scale01)
        {
            var hs = _c.Settings.hitStop;
            if (!hs.enabled || CommandLine.Has("-mnCapture")) return;
            _c.HitStop.Trigger(hs.ms * Mathf.Clamp01(scale01), hs.scale);
        }
    }
}
