using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// A stand-in for the AI the next builder writes: it flies a stationary emplacement the way any pilot would, through
    /// <see cref="ControlInput"/>. It turns the emplacement (pitch and yaw through the flight model) to face the player's ship plus lead,
    /// holds the fire button when it is lined up and in range, and now and then launches a heat-seeker. Shots go wide by a random
    /// amount (<c>scatter</c> metres) that changes every second or so, so a shield and a hull can be felt without being shredded.
    /// </summary>
    public sealed class TurretGunner : IPilotSource
    {
        readonly GameWorld _world;
        readonly int _self;
        readonly float _range, _scatter, _missileEvery, _waitScale;
        uint _rng;
        Vector3 _offset;
        float _offsetUntil, _nextMissile, _nextShot;

        public TurretGunner(GameWorld world, int self, float range, float scatter, float missileEvery, float waitScale, uint seed)
        {
            _world = world; _self = self; _range = range; _scatter = scatter; _missileEvery = missileEvery; _waitScale = Mathf.Max(1f, waitScale);
            _rng = seed == 0 ? 1u : seed;
            _nextMissile = 4f + Rand() * 4f;
        }

        float Rand() { _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5; return (_rng & 0xFFFFFF) / 16777216f; }

        public ControlInput Sample(in SimFrame frame)
        {
            var me = _world.Ships.Get(_self);
            var mc = _world.Combat.Get(_self);
            var target = _world.Ships.Get(_world.LocalPlayerShip);
            var tc = _world.Combat.Get(_world.LocalPlayerShip);
            var input = default(ControlInput);
            if (me == null || mc == null || !mc.Alive || target == null || tc == null || !tc.Alive) return input;

            input.TargetShip = target.Id;
            Vector3 muzzle = me.Position + me.Rotation * (mc.PrimaryHardpoints.Length > 0 ? mc.PrimaryHardpoints[0] : Vector3.zero);
            float dist = Vector3.Distance(muzzle, target.Position);

            if (frame.Time >= _offsetUntil)
            {
                _offsetUntil = frame.Time + 0.8f + Rand() * 0.8f;
                _offset = new Vector3(Rand() - 0.5f, Rand() - 0.5f, Rand() - 0.5f) * (2f * _scatter);
            }
            Vector3 aim = target.Position + _offset;
            float speed = mc.PrimarySpeed;
            Vector3 dir = (aim - me.Position).normalized;
            if (speed > 1f && Geo.SolveIntercept(muzzle, me.Velocity, aim, target.Velocity, speed, out Vector3 lead, out _, out _)) dir = lead;

            Vector3 local = Quaternion.Inverse(me.Rotation) * dir;
            input.Yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * 2.2f, -1f, 1f);
            input.Pitch = Mathf.Clamp(Mathf.Atan2(local.y, local.z) * 2.2f, -1f, 1f);

            bool lined = Vector3.Angle(me.Forward, dir) < 2.5f;
            // One shot at a time, spaced a multiple of the gun's own wait apart (the table rates are for a whole battle, not one emplacement).
            if (lined && dist < _range && mc.PrimaryOnline && frame.Time >= _nextShot)
            {
                input.Buttons |= ControlButtons.PrimaryFire;
                var gun = WeaponCatalog.Get(mc.PrimaryGun);
                _nextShot = frame.Time + (gun != null ? gun.FireWait : 1f) * _waitScale * (0.8f + 0.4f * Rand());
            }

            if (_missileEvery > 0f && dist < _range && frame.Time >= _nextMissile && mc.Missiles.Count > 0 && mc.Missiles[0].Ammo > 0)
            {
                input.SelectMissilePlusOne = 1;
                input.Buttons |= ControlButtons.MissileFire;
                // One launch attempt per interval (the heat seeker follows whatever is in its view cone: the emplacement points at the player).
                _nextMissile = frame.Time + _missileEvery * (0.8f + 0.4f * Rand());
            }
            return input;
        }
    }

    /// <summary>
    /// Makes a capital ship's turrets (parts of kind flak, aaf or turret) shoot at the player in a simple way, through
    /// <see cref="GameWorld.Fire"/>, so destroying one is a real silencing. A few turrets facing the player at a time, each firing at a
    /// fraction of its table rate, aiming with lead and a scatter that grows with range. The AI builder replaces this with real turret AI.
    /// </summary>
    public sealed class PartTurrets : ISimSystem
    {
        readonly GameWorld _world;
        readonly HashSet<int> _ships = new HashSet<int>();
        readonly List<(float d, int i)> _ready = new List<(float, int)>();
        uint _rng = 0x9E3779B1u;

        public PartTurrets(GameWorld world) { _world = world; }
        public int Order => SimOrder.Weapons - 10;
        /// <summary>How many turrets of one ship fire at once, at most.</summary>
        public int MaxActive = 3;
        /// <summary>Turrets fire at this multiple of their table fire wait (the table rates are for a whole battle).</summary>
        public float WaitScale = 3.5f;
        public float RangeMetres = 1300f;

        public void Add(int shipId) => _ships.Add(shipId);
        public void Remove(int shipId) => _ships.Remove(shipId);

        float Rand() { _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5; return (_rng & 0xFFFFFF) / 16777216f; }
        Vector3 Random3() { var v = new Vector3(Rand() * 2f - 1f, Rand() * 2f - 1f, Rand() * 2f - 1f); return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.up; }

        public void Step(in SimFrame frame)
        {
            var player = _world.Ships.Get(_world.LocalPlayerShip);
            var pc = _world.Combat.Get(_world.LocalPlayerShip);
            if (player == null || pc == null || !pc.Alive) return;
            foreach (int id in _ships)
            {
                var c = _world.Combat.Get(id);
                if (c == null || !c.Alive || c.Ship == null) continue;
                var ship = c.Ship;
                _ready.Clear();
                for (int i = 0; i < c.Parts.Count; i++)
                {
                    var p = c.Parts[i];
                    if (!p.IsTurret || p.Destroyed || p.Weapon == null || p.Cooldown > 0f) continue;
                    Vector3 pos = p.WorldPosition(ship), to = player.Position - pos;
                    float d = to.magnitude;
                    if (d > RangeMetres || d < 50f) continue;
                    if (Vector3.Dot(p.WorldForward(ship), to / d) < -0.15f) continue;       // the turret's own side of the hull
                    _ready.Add((d, i));
                }
                if (_ready.Count == 0) continue;
                _ready.Sort((a, b) => a.d.CompareTo(b.d));
                int fire = 0;
                foreach (var (d, i) in _ready)
                {
                    if (fire >= MaxActive) break;
                    var p = c.Parts[i];
                    var def = WeaponCatalog.Get(p.Weapon);
                    if (def == null) continue;
                    // Each ready turret fires with a probability that spaces its shots about WaitScale x its table wait apart.
                    if (Rand() > Sim.Dt / (def.FireWait * (WaitScale - 1f) + 0.01f)) continue;
                    fire++;
                    Vector3 pos = p.WorldPosition(ship);
                    Vector3 aim = player.Position;
                    if (Geo.SolveIntercept(pos, ship.Velocity, player.Position, player.Velocity, def.Speed, out _, out float t, out Vector3 meet)) aim = meet;
                    // Bolts go a little wide; flak is aimed to burst round the target, not on the lens.
                    float spread = 3f + d * 0.012f;
                    aim += new Vector3(Rand() - 0.5f, Rand() - 0.5f, Rand() - 0.5f) * (2f * spread);
                    if (def.Kind == WeaponKind.Flak) aim += (Random3() * (45f + 50f * Rand()));
                    _world.Fire.Add(new FireRequest { Ship = id, Part = i, Weapon = p.Weapon, AimPoint = aim, Target = player.Id });
                }
            }
        }
    }
}
