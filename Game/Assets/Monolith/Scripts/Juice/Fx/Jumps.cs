using System;
using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Juice
{
    /// <summary>
    /// A ship jumping in or out (the mission system's <c>jump_in</c> and <c>jump_out</c>, see <see cref="MissionEvents"/>): a cold blue-white flash
    /// with a long anamorphic streak where the ship appears or vanishes, a small haze, a short light on nearby hulls, the sound
    /// and a little shake when it is close. Sized by the ship's radius, so a corvette's arrival is a much bigger event than a fighter's. A first
    /// look: FS2's warp-in is a hole the ship slides out of; ours is a flash until the effect gets its own round in the juice sandbox.
    /// A scene that raises jumps attaches it to its director (<c>Jumps.Attach(juice)</c>, the mission scene does) and disposes it with the scene.
    /// </summary>
    public sealed class Jumps : IDisposable
    {
        static readonly Vector3 Cold = new Vector3(0.62f, 0.80f, 1f);

        readonly JuiceContext _c;
        readonly Emitters _fx;
        readonly Feel _feel;
        readonly List<IDisposable> _subs = new List<IDisposable>();

        public Jumps(JuiceContext c, Emitters fx, Feel feel) { _c = c; _fx = fx; _feel = feel; }

        /// <summary>Draw <c>jump_in</c> and <c>jump_out</c> through this director's effects, lights, sound and feel.</summary>
        public static Jumps Attach(JuiceDirector juice)
        {
            var c = juice.Context;
            var j = new Jumps(c, c.Fx, c.Feel);
            j._subs.Add(GameEvents.Subscribe(MissionEvents.JumpIn, e => j.Safe(e, true)));
            j._subs.Add(GameEvents.Subscribe(MissionEvents.JumpOut, e => j.Safe(e, false)));
            return j;
        }

        public void Dispose() { foreach (var s in _subs) s.Dispose(); _subs.Clear(); }

        void Safe(in GameEvent e, bool arriving)
        {
            try { OnJump(e, arriving); }
            catch (Exception ex) { MnLog.Error($"JUICE jump handler threw: {ex}"); }
        }

        public void OnJump(in GameEvent e, bool arriving)
        {
            if (!e.HasPosition || !_c.Settings.effects.explosions) return;
            float r = Mathf.Max(e.Value, 10f) * _c.Size;
            Vector3 dir = e.Direction.sqrMagnitude > 1e-4f ? e.Direction.normalized : Vector3.forward;
            // Arriving, the light is where the ship came out (just behind it); leaving, where it went (just ahead).
            Vector3 at = e.Position + dir * (arriving ? -0.4f : 0.6f) * r;
            // A soft, blown-out core, a long thin streak through it (the read of a jump), and a small cold haze; no ring (it reads as a bubble).
            _fx.Flash(at, r * 1.1f, Cold * 40f, 0.12f + r * 0.001f, 0.15f, 2.4f, 1.4f);
            _fx.Flash(at, r * 3.2f, Cold * 0.6f, 0.45f + r * 0.003f, 0f, 1.8f, 1.3f);
            _fx.Flare(at, r * 16f, r * 0.07f, Cold * 22f, 0.22f + r * 0.0015f, 2.2f);
            _fx.Flare(at, r * 6f, r * 0.03f, Cold * 8f, 0.6f + r * 0.003f, 1.6f);
            if (_c.Settings.effects.lights)
                _c.Lights.Flash(at, new Color(0.62f, 0.8f, 1f), 1.4e6f * r * r * _c.Bright, Mathf.Max(400f, r * 30f), 0.45f + r * 0.004f, 1.6f, null, Mathf.Min(r * 0.4f, 40f));
            float dist = _c.DistanceToCamera(at);
            _c.Audio.Play(arriving ? JuiceCue.BeamCharge : JuiceCue.Shockwave, at, Mathf.Clamp(0.4f + r / 200f, 0.4f, 1f), dist);
            _feel.Impact(at, Mathf.Clamp(r / 300f, 0.05f, 0.35f), 250f + r * 6f, 0.25f, 0.1f, 0.25f);
        }
    }
}
