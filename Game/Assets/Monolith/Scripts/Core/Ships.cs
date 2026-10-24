using System.Collections.Generic;
using UnityEngine;

namespace Monolith.Core
{
    /// <summary>
    /// What every system may know about one ship's flight, published once per tick by Flight (owner: Flight, except
    /// <see cref="Target"/>, owner: Targeting). Everyone else reads. Poses are kept for this tick and the one before so
    /// views draw <c>Lerp(Previous, current, GameWorld.Alpha)</c>.
    /// Axes are Unity's: x right, y up, z forward. Angular velocity is ship-local in Unity's rotation sense:
    /// +x nose down, +y nose right, +z roll left.
    /// </summary>
    public sealed class ShipState
    {
        public int Id;
        /// <summary>Ship class data id (Resources/Ships/&lt;id&gt;.json) and display name.</summary>
        public string ClassId, ClassName;
        public string Callsign;
        /// <summary>0 is the player's side; anything else is someone else's (owner: whoever spawned the ship).</summary>
        public int Team;
        /// <summary>Rough bounding radius in metres (targeting, HUD brackets).</summary>
        public float Radius;
        /// <summary>Mass in kg (owner: whoever spawned the ship; Flight sets it from the ship class). Shockwaves size their shoves by it.</summary>
        public float Mass = 10000f;

        public Vector3 Position, PreviousPosition;
        public Quaternion Rotation = Quaternion.identity, PreviousRotation = Quaternion.identity;
        /// <summary>World velocity, m/s.</summary>
        public Vector3 Velocity;
        /// <summary>Ship-local turn rates, rad/s (+x nose down, +y nose right, +z roll left).</summary>
        public Vector3 AngularVelocity;
        /// <summary>Speed along the nose, m/s.</summary>
        public float ForwardSpeed;
        public float TopSpeed, AfterburnerSpeed;
        /// <summary>The class's best turn rates, rad/s (pitch, yaw, roll), and its turn-rate time constant in seconds (owner: Flight, set at spawn).
        /// An AI flies the ship with them the way FS2's AI reads its ship's physics.</summary>
        public Vector3 TurnRate = new Vector3(2f, 2f, 1.2f);
        public float TurnLag = 0.35f;
        /// <summary>The throttle setting flight used this tick (0..1): the pilot's, or the matched target speed.</summary>
        public float Throttle;
        public bool MatchingSpeed;
        public bool AfterburnerOn;
        /// <summary>The retro-thruster brake is on (the pilot clicked R3: held until released, even at a standstill), and the retros are firing now (it is on and the ship is still moving forward). Owner: Flight.</summary>
        public bool BrakeOn, Braking;
        /// <summary>The throttle setting a second click of the brake will give back (0..1). Owner: Flight (the pilot sends it in the throttle while the brake is on), read by the HUD.</summary>
        public float BrakeResume;
        public float Fuel, FuelMax;
        /// <summary>0..1, how loosened the damping is after a knock (1 right after a big hit, fading to 0 over ~2 s).</summary>
        public float Knock;
        /// <summary>The ship this one has targeted, or <see cref="GameWorld.NoShip"/> (owner: Targeting).</summary>
        public int Target = GameWorld.NoShip;
        /// <summary>The index of the targeted part on the target's <see cref="ShipCombat.Parts"/>, or -1 for the whole ship (owner: Targeting).</summary>
        public int TargetPart = -1;
        /// <summary>
        /// Speed multiplier from damaged engines and the power choice (owner: Damage; Flight reads it): 1 is normal, below 1 caps the top
        /// and afterburner speed, a little above 1 is the engines-favoured bonus.
        /// </summary>
        public float SpeedScale = 1f;

        public Vector3 Forward => Rotation * Vector3.forward;
        public Vector3 RenderPosition(float alpha) => Vector3.LerpUnclamped(PreviousPosition, Position, alpha);
        public Quaternion RenderRotation(float alpha) => Quaternion.SlerpUnclamped(PreviousRotation, Rotation, alpha);
    }

    /// <summary>Every ship in the world by id (owner: Flight adds and removes; ids come from <see cref="NewId"/>).</summary>
    public sealed class ShipRegistry
    {
        readonly Dictionary<int, ShipState> _byId = new Dictionary<int, ShipState>();
        readonly List<ShipState> _all = new List<ShipState>();
        int _nextId = 1;

        /// <summary>In spawn order, so iterating is deterministic.</summary>
        public IReadOnlyList<ShipState> All => _all;
        public int Count => _all.Count;

        public int NewId() => _nextId++;

        public void Add(ShipState ship)
        {
            if (ship == null) throw new System.ArgumentNullException(nameof(ship));
            if (_byId.ContainsKey(ship.Id)) throw new System.ArgumentException($"ship id {ship.Id} already exists");
            _byId.Add(ship.Id, ship);
            _all.Add(ship);
        }

        public bool Remove(int id)
        {
            if (!_byId.TryGetValue(id, out var s)) return false;
            _byId.Remove(id);
            _all.Remove(s);
            return true;
        }

        /// <summary>The ship, or null.</summary>
        public ShipState Get(int id) => _byId.TryGetValue(id, out var s) ? s : null;
    }

    /// <summary>
    /// A shove to a ship: weapon hits, collisions, shockwaves. Anything may add one to <see cref="GameWorld.Knocks"/>;
    /// Flight applies them at the start of its next step (velocity and spin change, then damping loosened for up to
    /// ~2 s, fading, as FS2 does after whacks and shockwaves).
    /// </summary>
    public struct ShipKnock
    {
        public int Ship;
        /// <summary>Impulse in N s, world space. The ship's velocity changes by Impulse / mass.</summary>
        public Vector3 Impulse;
        /// <summary>Ship-local point the impulse acts at (metres from the centre). Off-centre hits also spin the ship.</summary>
        public Vector3 Point;
        /// <summary>0..1 shockwave strength: also loosens turn damping and shakes the ship for 2 s. 0 for a plain hit.</summary>
        public float Shock;
    }

    /// <summary>Pending knocks (owner: anyone adds, Flight drains).</summary>
    public sealed class KnockQueue
    {
        readonly List<ShipKnock> _pending = new List<ShipKnock>();

        public int Count => _pending.Count;

        public void Add(in ShipKnock knock) => _pending.Add(knock);

        /// <summary>Flight only: move every pending knock into <paramref name="into"/> and empty the queue.</summary>
        public void Drain(List<ShipKnock> into)
        {
            into.AddRange(_pending);
            _pending.Clear();
        }
    }
}
