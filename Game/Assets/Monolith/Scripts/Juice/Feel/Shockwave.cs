using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Juice
{
    /// <summary>
    /// The shockwave of a big blast: an expanding sphere that shoves every ship it passes through
    /// <see cref="GameWorld.Knocks"/> (Flight loosens the damping for about 2 s and turns the knock into spin, as FS2 does after a
    /// shockwave) and shakes, rumbles and flashes the player when it arrives. Strength falls off with distance.
    /// </summary>
    public sealed class Shockwaves
    {
        sealed class Wave
        {
            public Vector3 Centre;
            public float Radius, Speed, MaxRadius, Strength, HalfRadius;
            public readonly HashSet<int> Hit = new HashSet<int>();
            public bool PlayerHit;
        }

        readonly JuiceContext _c;
        readonly Emitters _fx;
        readonly List<Wave> _waves = new List<Wave>();

        public Shockwaves(JuiceContext c, Emitters fx) { _c = c; _fx = fx; }

        public int Active => _waves.Count;

        /// <summary>Start a wave from <paramref name="centre"/>. <paramref name="strength"/> 1 is a capital ship's death.</summary>
        public void Start(Vector3 centre, float strength, float maxRadius, float speed)
        {
            if (!_c.Settings.effects.shockwave) return;
            _waves.Add(new Wave { Centre = centre, Speed = speed, MaxRadius = maxRadius * _c.Size, Strength = strength, HalfRadius = maxRadius * _c.Size * 0.4f });
        }

        public void Update(float dt)
        {
            for (int i = _waves.Count - 1; i >= 0; i--)
            {
                var w = _waves[i];
                float before = w.Radius;
                w.Radius += w.Speed * dt;
                foreach (var ship in _c.World.Ships.All)
                {
                    if (w.Hit.Contains(ship.Id)) continue;
                    float d = Vector3.Distance(ship.Position, w.Centre);
                    if (d > w.Radius || d > w.MaxRadius) continue;
                    w.Hit.Add(ship.Id);
                    Shove(w, ship, d);
                }
                if (w.Radius > w.MaxRadius * 1.1f) _waves.RemoveAt(i);
            }
        }

        void Shove(Wave w, ShipState ship, float distance)
        {
            float fall = w.Strength * JuiceContext.Falloff(distance, w.HalfRadius);
            if (fall < 0.02f) return;
            Vector3 dir = ship.Position - w.Centre;
            dir = dir.sqrMagnitude < 1f ? ship.Rotation * Vector3.back : dir.normalized;
            // A shove of up to about 30 m/s (FS2's full-loosening change), on a point near the nose so it also spins the ship.
            float shove = Mathf.Max(0f, _c.Settings.levels.shove);
            float dv = 26f * Mathf.Min(1f, fall * 1.15f) * shove;
            float mass = ship.Mass > 1f ? ship.Mass : 10000f;
            if (ship.Radius > 120f) dv *= 0.12f;                // hulls the size of a town barely notice
            var local = new Vector3(_c.Rng.Range(-0.12f, 0.12f) * ship.Radius, _c.Rng.Range(-0.1f, 0.1f) * ship.Radius, ship.Radius * 0.25f);
            _c.World.Knocks.Add(new ShipKnock { Ship = ship.Id, Impulse = dir * (mass * dv), Point = local, Shock = Mathf.Clamp01(fall * 0.7f * shove) });

            if (_c.IsPlayer(ship.Id))
            {
                _c.Trauma.Add(0.78f * fall);
                _c.Rumble.Pulse(1f * Mathf.Min(1f, fall * 1.6f), 0.75f * Mathf.Min(1f, fall * 1.6f), 0.9f);
                _c.Audio.Play(JuiceCue.Shockwave, ship.Position, Mathf.Clamp01(0.5f + fall), distance);
                _c.Hits.EdgeFlash(new Vector3(1f, 0.55f, 0.20f) * (0.14f * fall), 0.35f);
                MnLog.Info($"JUICE shockwave reaches the player: {distance:0} m from the blast, strength {fall:0.00}, shove {dv:0.0} m/s");
            }
        }

        public void Clear() { _waves.Clear(); }
    }
}
