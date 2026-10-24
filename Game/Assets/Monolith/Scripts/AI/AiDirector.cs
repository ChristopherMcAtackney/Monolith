using System;
using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.AI
{
    /// <summary>
    /// The AI's shared side (order just before the pilots are sampled): the brains of every AI ship (<see cref="AiPilot"/> for fighters and
    /// bombers, <see cref="CapitalPilot"/> for capital ships), what they all need to know once per tick (the capital hulls to keep clear of, who
    /// is attacking whom, torpedoes in flight, which of a pilot's shots hit), the wingman orders from the player's quick menu, the radio, and the
    /// measurements (<see cref="AiStats"/>). It reads <see cref="GameWorld"/> and listens to <see cref="GameEvents"/>; every ship it flies is flown
    /// through its <see cref="ControlInput"/>, and turrets through <see cref="FireRequest"/>s (<see cref="TurretGunnery"/>).
    /// </summary>
    public sealed class AiDirector : ISimSystem, IDisposable
    {
        public readonly GameWorld World;
        public readonly AiData Data;
        public int Level { get; private set; }
        /// <summary>The team the player flies for (FS2 slows the other side's turns and guns on easy levels).</summary>
        public int PlayerTeam = 0;
        public readonly AiStats Stats;
        public readonly AiRadio Radio;
        public readonly Wing PlayerWing;

        readonly List<AiPilot> _pilots = new List<AiPilot>();
        readonly Dictionary<int, AiPilot> _byShip = new Dictionary<int, AiPilot>();
        readonly List<CapitalPilot> _capitals = new List<CapitalPilot>();
        readonly List<IDisposable> _subs = new List<IDisposable>();

        // Once per tick.
        readonly List<ShipCombat> _big = new List<ShipCombat>();
        readonly List<ShipCombat> _small = new List<ShipCombat>();
        readonly Dictionary<int, int> _attackers = new Dictionary<int, int>();
        readonly Dictionary<int, int> _homers = new Dictionary<int, int>();
        readonly Dictionary<int, Torpedo> _torpedoes = new Dictionary<int, Torpedo>();
        readonly List<int> _stale = new List<int>();
        readonly HashSet<int> _beaming = new HashSet<int>();

        /// <summary>A guided weapon in flight, as the weapons system reports it every tick (missile_track).</summary>
        public sealed class Torpedo
        {
            public int Id, Owner, Target, Team;
            public string Weapon;
            public Vector3 Position, Velocity;
            public int Tick;
            public bool IsTorpedo;
        }

        public AiDirector(GameWorld world, AiData data = null, int level = -1)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Data = data ?? AiData.Default;
            Level = level < 0 ? Data.Difficulty.Default : Data.ClampLevel(level);
            ApplyLevelToWorld();
            Stats = new AiStats(this);
            Radio = new AiRadio(this);
            PlayerWing = new Wing(this);
            _subs.Add(GameEvents.Subscribe(CombatEvents.MissileTrack, OnTrack));
            _subs.Add(GameEvents.Subscribe(CombatEvents.HitShield, OnHit));
            _subs.Add(GameEvents.Subscribe(CombatEvents.HitHull, OnHit));
            _subs.Add(GameEvents.Subscribe(CombatEvents.Collision, OnCollision));
            _subs.Add(GameEvents.Subscribe(CombatEvents.MissileWarning, OnWarning));
            _subs.Add(GameEvents.Subscribe(CombatEvents.BeamCharge, e => _beaming.Add(e.Entity)));
            _subs.Add(GameEvents.Subscribe(CombatEvents.BeamEnd, e => _beaming.Remove(e.Entity)));
        }

        public void Dispose()
        {
            foreach (var s in _subs) s.Dispose();
            _subs.Clear();
            Stats.Dispose();
        }

        public int Order => SimOrder.Pilots - 10;
        public IReadOnlyList<AiPilot> Pilots => _pilots;
        public IReadOnlyList<CapitalPilot> CapitalPilots => _capitals;
        /// <summary>Capital ships and anything big (their hulls are what fighters steer clear of).</summary>
        public IReadOnlyList<ShipCombat> Big => _big;
        /// <summary>Fighters and bombers, alive.</summary>
        public IReadOnlyList<ShipCombat> Small => _small;
        public IEnumerable<Torpedo> Torpedoes => _torpedoes.Values;
        public string LevelName => Data.Difficulty.Levels[Level];

        public void SetLevel(int level)
        {
            Level = Data.ClampLevel(level);
            ApplyLevelToWorld();
            foreach (var p in _pilots) p.Reskill();
            foreach (var m in PlayerWing.Members) PlayerWing.ApplyLevel(m);
        }

        /// <summary>FS2's mercy on the player by level: how much of a weapon hit the player's ship takes (the damage system reads it).</summary>
        void ApplyLevelToWorld()
        {
            World.PlayerDamageScale = Data.Difficulty.PlayerDamageScale[Level];
            World.PlayerPartDamageScale = Data.Difficulty.PlayerPartDamageScale[Level];
        }

        // ------------------------------------------------------------------------------------------------ brains

        /// <summary>Fly <paramref name="shipId"/> with a fighter brain (attach it to the ship's pilot slot yourself, or pass <paramref name="attach"/>).</summary>
        public AiPilot AddPilot(int shipId, string profile, AiRole role, bool attach = true, string statsKey = null)
        {
            var ship = World.Ships.Get(shipId) ?? throw new ArgumentException($"no ship {shipId}");
            var p = new AiPilot(this, shipId, profile, role, (uint)(shipId * 2654435761u) ^ 0x9E3779B9u);
            _pilots.Add(p);
            _byShip[shipId] = p;
            if (attach) World.Pilots.Attach(shipId, p);
            Stats.Track(shipId, statsKey ?? p.Skill.Key);
            return p;
        }

        public CapitalPilot AddCapital(int shipId, CapitalOrders orders, bool attach = true)
        {
            var p = new CapitalPilot(this, shipId, orders);
            _capitals.Add(p);
            if (attach) World.Pilots.Attach(shipId, p);
            return p;
        }

        /// <summary>Stop flying a ship (it died, or the scene is being reset). Its pilot slot is left to the caller.</summary>
        public void Remove(int shipId)
        {
            if (_byShip.TryGetValue(shipId, out var p)) { _pilots.Remove(p); _byShip.Remove(shipId); }
            _capitals.RemoveAll(c => c.Ship == shipId);
            PlayerWing.Remove(shipId);
        }

        public void Clear()
        {
            _pilots.Clear(); _byShip.Clear(); _capitals.Clear(); _torpedoes.Clear(); _beaming.Clear();
            PlayerWing.Clear();
        }

        public AiPilot PilotOf(int shipId) => _byShip.TryGetValue(shipId, out var p) ? p : null;

        /// <summary>True when a ship is firing (or charging) a beam: it can only fire one at a time.</summary>
        public bool Beaming(int shipId) => _beaming.Contains(shipId);

        // ------------------------------------------------------------------------------------------------ the tick

        public void Step(in SimFrame frame)
        {
            _big.Clear(); _small.Clear(); _attackers.Clear(); _homers.Clear();
            var all = World.Combat.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (!c.Alive || c.Ship == null) continue;
                if (IsBig(c)) _big.Add(c); else _small.Add(c);
                int t = c.Ship.Target;
                if (t != GameWorld.NoShip && Teams.Hostile(c.Ship.Team, World.Ships.Get(t)?.Team ?? -1) && !IsBig(c))
                    _attackers[t] = (_attackers.TryGetValue(t, out int n) ? n : 0) + 1;
            }
            // Guided weapons the weapons system stopped reporting are gone.
            _stale.Clear();
            foreach (var kv in _torpedoes) if (frame.Tick - kv.Value.Tick > 2) _stale.Add(kv.Key);
            foreach (var id in _stale) _torpedoes.Remove(id);
            foreach (var t in _torpedoes.Values)
                if (t.Target != GameWorld.NoShip && !t.IsTorpedo) _homers[t.Target] = (_homers.TryGetValue(t.Target, out int n) ? n : 0) + 1;

            PlayerWing.Step(frame);
            Stats.Step(frame);
        }

        public static bool IsBig(ShipCombat c) =>
            c.SizeClass == CombatEvents.SizeCorvette || c.SizeClass == CombatEvents.SizeCapital || c.Shape.BoundRadius > 60f;

        /// <summary>A pilot changed target this tick: keep the count straight at once, so pilots choosing in the same tick see each other (FS2's attacker cap).</summary>
        public void NoteTarget(int oldTarget, int newTarget)
        {
            if (oldTarget == newTarget) return;
            if (oldTarget != GameWorld.NoShip && _attackers.TryGetValue(oldTarget, out int o)) _attackers[oldTarget] = Mathf.Max(0, o - 1);
            if (newTarget != GameWorld.NoShip) _attackers[newTarget] = (_attackers.TryGetValue(newTarget, out int n) ? n : 0) + 1;
        }

        /// <summary>How many fighters and bombers (of any side) have this ship as their target now.</summary>
        public int AttackersOn(int shipId) => _attackers.TryGetValue(shipId, out int n) ? n : 0;

        /// <summary>How many missiles (not torpedoes) are homing on this ship now.</summary>
        public int HomersOn(int shipId) => _homers.TryGetValue(shipId, out int n) ? n : 0;

        public bool Hostile(int a, int b)
        {
            var sa = World.Ships.Get(a); var sb = World.Ships.Get(b);
            return sa != null && sb != null && Teams.Hostile(sa.Team, sb.Team);
        }

        public bool IsHostileSide(int team) => team != PlayerTeam;

        // ------------------------------------------------------------------------------------------------ events

        void OnTrack(GameEvent e)
        {
            if (string.IsNullOrEmpty(e.Subject) || e.Subject == "missile_decoy") return;
            int id = (int)e.Value;
            if (!_torpedoes.TryGetValue(id, out var t))
            {
                t = new Torpedo { Id = id, Owner = e.Entity, Weapon = e.Subject, IsTorpedo = e.Subject.StartsWith("torpedo", StringComparison.Ordinal) };
                t.Team = World.Ships.Get(e.Entity)?.Team ?? -1;
                _torpedoes[id] = t;
                Stats.OnMissile(e.Entity, t.IsTorpedo);
            }
            t.Position = e.Position; t.Velocity = e.Direction; t.Target = e.Other; t.Tick = World.Tick;
        }

        void OnHit(GameEvent e)
        {
            Stats.OnHit(e);
            if (e.Other != GameWorld.NoShip && _byShip.TryGetValue(e.Other, out var shooter)) shooter.OnMyHit(e.Entity, World.Time);
        }

        void OnCollision(GameEvent e)
        {
            Stats.OnCollision(e);
            if (_byShip.TryGetValue(e.Entity, out var p)) p.OnCollision(e.Other, e.Position, e.Direction, World.Time);
            if (_byShip.TryGetValue(e.Other, out var q)) q.OnCollision(e.Entity, e.Position, -e.Direction, World.Time);
        }

        void OnWarning(GameEvent e)
        {
            if (_byShip.TryGetValue(e.Entity, out var p)) p.OnMissileWarning(World.Time);
        }
    }
}
