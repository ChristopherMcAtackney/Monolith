using System;
using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Hud
{
    /// <summary>One thing on the radar or the battle map, in the player's own frame (x right, y up, z forward), metres.</summary>
    public struct Contact
    {
        public Vector3 Rel;
        public float Dist, Radius;
        public bool Hostile, Big, IsTarget, Missile, Torpedo, Incoming;
        public string Callsign;
        public int Id;
    }

    /// <summary>
    /// Keeps the guided missiles in flight (from the weapons system's <c>missile_track</c> event, raised every tick for each one) so the radar and the map can show them: the HUD
    /// reads <c>GameWorld</c> only, and the world has no list of projectiles. A track is dropped when its events stop (a hit, a decoy, a shoot-down).
    /// </summary>
    public sealed class HudMissiles : IDisposable
    {
        public struct Track { public Vector3 Pos, Vel; public int Launcher, Target, Tick; public bool Torpedo; }

        /// <summary>The tracker the running HUD made (null when there is none), so the cabin's table map can read it too.</summary>
        public static HudMissiles Current { get; private set; }

        readonly Dictionary<int, Track> _tracks = new Dictionary<int, Track>();
        readonly List<int> _drop = new List<int>();
        IDisposable _sub;
        GameWorld _world;

        public HudMissiles(GameWorld world)
        {
            _world = world;
            _sub = GameEvents.Subscribe(CombatEvents.MissileTrack, OnTrack);
            Current = this;
        }

        public void Dispose()
        {
            _sub?.Dispose();
            _sub = null;
            if (Current == this) Current = null;
        }

        void OnTrack(GameEvent e)
        {
            if (e.Subject == "missile_decoy") return;          // a decoy is flown as a track too, but it is not a missile
            int id = (int)e.Value;
            _tracks[id] = new Track { Pos = e.Position, Vel = e.Direction, Launcher = e.Entity, Target = e.Other, Tick = e.Tick, Torpedo = e.Subject != null && e.Subject.StartsWith("torpedo", StringComparison.Ordinal) };
        }

        /// <summary>Every missile still being tracked (stale ones dropped).</summary>
        public IEnumerable<Track> Live()
        {
            int now = _world != null ? _world.Tick : 0;
            _drop.Clear();
            foreach (var kv in _tracks) if (now - kv.Value.Tick > 3) _drop.Add(kv.Key);
            foreach (var id in _drop) _tracks.Remove(id);
            return _tracks.Values;
        }

        public int Count => _tracks.Count;
    }

    /// <summary>Gathers the radar's and the map's contacts from the world: every other living ship and every missile in flight, in the player's frame.</summary>
    public static class HudContacts
    {
        /// <summary>Everything within <paramref name="maxDist"/> (0: all) of the player, nearest first not guaranteed. <paramref name="into"/> is cleared.</summary>
        public static void Gather(GameWorld w, ShipState me, List<Contact> into, float maxDist = 0f, HudMissiles missiles = null)
        {
            into.Clear();
            if (me == null) return;
            var inv = Quaternion.Inverse(me.Rotation);
            var ships = w.Ships.All;
            for (int i = 0; i < ships.Count; i++)
            {
                var s = ships[i];
                if (s.Id == me.Id) continue;
                var c = w.Combat.Get(s.Id);
                if (c != null && !c.Alive) continue;
                Vector3 rel = inv * (s.Position - me.Position);
                float d = rel.magnitude;
                if (maxDist > 0f && d > maxDist) continue;
                into.Add(new Contact
                {
                    Rel = rel, Dist = d, Radius = s.Radius, Hostile = Teams.Hostile(me.Team, s.Team), Big = s.Radius > 60f,
                    IsTarget = s.Id == me.Target, Callsign = s.Callsign, Id = s.Id,
                });
            }
            missiles ??= HudMissiles.Current;
            if (missiles != null)
                foreach (var t in missiles.Live())
                {
                    Vector3 rel = inv * (t.Pos - me.Position);
                    float d = rel.magnitude;
                    if (maxDist > 0f && d > maxDist) continue;
                    var launcher = w.Ships.Get(t.Launcher);
                    bool hostile = launcher != null && Teams.Hostile(me.Team, launcher.Team);
                    into.Add(new Contact { Rel = rel, Dist = d, Missile = true, Torpedo = t.Torpedo, Hostile = hostile, Incoming = t.Target == me.Id && hostile, Id = -1000 - t.Launcher });
                }
        }

        static readonly float[] RadarRanges = { 1500f, 3000f, 6000f, 12000f };
        static float _radarRange = 3000f;

        /// <summary>The radar's range (m): the smallest step that holds the nearest few hostile ships, with a little hysteresis so it does not flutter.</summary>
        public static float PickRadarRange(List<Contact> contacts)
        {
            float need = 0f;
            int counted = 0;
            // the nearest six hostile ships (and any missile coming at us) decide the range
            float[] nearest = new float[6];
            for (int i = 0; i < nearest.Length; i++) nearest[i] = float.MaxValue;
            foreach (var c in contacts)
            {
                if (c.Missile ? !c.Incoming : !c.Hostile || c.Big) continue;
                for (int k = 0; k < nearest.Length; k++)
                    if (c.Dist < nearest[k]) { for (int m = nearest.Length - 1; m > k; m--) nearest[m] = nearest[m - 1]; nearest[k] = c.Dist; break; }
            }
            for (int i = 0; i < nearest.Length && nearest[i] < float.MaxValue; i++) { need = Mathf.Max(need, nearest[i]); counted++; }
            float want = RadarRanges[RadarRanges.Length - 1];
            foreach (var r in RadarRanges) if (need * 1.1f <= r) { want = r; break; }
            if (counted == 0) want = 3000f;
            if (want > _radarRange || want < _radarRange * 0.55f) _radarRange = want;
            return _radarRange;
        }

        static readonly float[] MapRanges = { 1500f, 3000f, 6000f, 12000f, 25000f, 60000f };
        static float _mapRange = 3000f;

        /// <summary>The battle map's range (m): the smallest step that holds every enemy within 8 km and every capital ship in play.</summary>
        public static float PickMapRange(List<Contact> contacts)
        {
            float need = 0f;
            foreach (var c in contacts)
            {
                if (c.Missile) continue;
                if (c.Dist < 8000f || c.Big) need = Mathf.Max(need, c.Dist);
            }
            float want = MapRanges[MapRanges.Length - 1];
            foreach (var r in MapRanges) if (need * 1.12f <= r) { want = r; break; }
            if (want > _mapRange || want < _mapRange * 0.55f) _mapRange = want;
            return _mapRange;
        }

        public static string RangeText(float m) =>
            m >= 1000f ? (m / 1000f).ToString(m % 1000f == 0f ? "0" : "0.0", System.Globalization.CultureInfo.InvariantCulture) + " KM" : Mathf.RoundToInt(m).ToString(System.Globalization.CultureInfo.InvariantCulture) + " M";
    }
}
