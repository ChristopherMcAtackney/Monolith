using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Juice
{
    /// <summary>What the juice knows about a ship's looks: its visual (to break up), its size and its shield.</summary>
    public sealed class ShipLook
    {
        public int Id;
        public Transform Visual;
        /// <summary>Bounds of the visual in its own space (centre, half extents).</summary>
        public Vector3 LocalCentre, HalfExtents;
        /// <summary>Shield bubble radii in metres (zero = no shield).</summary>
        public Vector3 ShieldRadii;
        public string SizeClass;
        public ShieldDraw Shield;
        public float ShieldRim;
        public float Radius;
    }

    /// <summary>The shared toolbox every juice module draws on (one per director). Modules never reference each other, only this.</summary>
    public sealed class JuiceContext
    {
        public GameWorld World;
        public Camera Camera;
        public JuiceSettings Settings;
        public FxParticles Particles;
        public FxRng Rng;
        public FxFrame Frame;
        public FxBatch Add => Frame.Add;
        public FxBatch Alpha => Frame.Alpha;
        public LightPool Lights;
        public IJuiceAudio Audio;
        public Trauma Trauma;
        public RumbleOut Rumble;
        public HitStop HitStop;
        public Feel Feel;
        public Emitters Fx;
        public HitEffects Hits;
        public Debris Debris;
        public Shockwaves Shockwaves;
        public Transform Root;
        public readonly Dictionary<int, ShipLook> Looks = new Dictionary<int, ShipLook>();

        /// <summary>The effect clock in seconds (sim time while running; stands still when paused).</summary>
        public float Time;
        public float Dt;

        public Vector3 CameraPosition => Camera != null ? Camera.transform.position : Vector3.zero;
        public float DistanceToCamera(Vector3 p) => Vector3.Distance(CameraPosition, p);
        public ShipState Player => World.Ships.Get(World.LocalPlayerShip);
        public bool IsPlayer(int ship) => ship != GameWorld.NoShip && ship == World.LocalPlayerShip;

        /// <summary>A count scaled by the particle density setting (at least one).</summary>
        public int N(int baseCount) => Mathf.Max(1, Mathf.RoundToInt(baseCount * Settings.levels.density));
        public float Size => Settings.levels.size;
        public float Bright => Settings.FlashScale;

        /// <summary>Simulated time right now for things that move analytically between ticks (bolts): ticks run so far plus how far into the next one.</summary>
        public float RenderSimTime => (World.Tick - 1 + World.Alpha) * Sim.Dt;

        /// <summary>A ship's pose now, interpolated for drawing.</summary>
        public bool Pose(int ship, out Vector3 position, out Quaternion rotation)
        {
            var s = World.Ships.Get(ship);
            if (s == null) { position = default; rotation = Quaternion.identity; return false; }
            position = s.RenderPosition(World.Alpha);
            rotation = s.RenderRotation(World.Alpha);
            return true;
        }

        public ShipLook LookOf(int ship) => ship != GameWorld.NoShip && Looks.TryGetValue(ship, out var l) ? l : null;

        /// <summary>The size class of a ship from its look, else from its radius: fighter, bomber, corvette, capital.</summary>
        public string SizeClassOf(int ship, float radiusHint = 0f)
        {
            var look = LookOf(ship);
            if (look != null && !string.IsNullOrEmpty(look.SizeClass)) return look.SizeClass;
            float r = radiusHint;
            if (r <= 0f) { var s = World.Ships.Get(ship); r = s != null ? s.Radius : 0f; }
            return SizeClassFromRadius(r);
        }

        public static string SizeClassFromRadius(float r)
        {
            if (r < 12f) return CombatEvents.SizeFighter;
            if (r < 40f) return CombatEvents.SizeBomber;
            if (r < 160f) return CombatEvents.SizeCorvette;
            return CombatEvents.SizeCapital;
        }

        /// <summary>Distance fall-off for shake and rumble: 1 at the source, 0.5 at <paramref name="halfDistance"/>.</summary>
        public static float Falloff(float distance, float halfDistance) => 1f / (1f + (distance * distance) / (halfDistance * halfDistance));
    }
}
