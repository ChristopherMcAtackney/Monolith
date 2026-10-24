using System;
using System.Collections.Generic;
using Monolith.AI;
using Monolith.Core;
using Monolith.Flight;
using Monolith.Juice;
using Monolith.Mission;
using Monolith.ShipArt;
using Monolith.View;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// The mission system's hands in the game (<see cref="IMissionHost"/>): spawns a ship with the flight model, the combat rig, its look (a
    /// Blender model when ShipArt has one, else a placeholder hull or fighter) and its pilot (the AI's fighter brain, a capital helm and gunners,
    /// or the autopilot for the player's ship in soaks and checks), gives AI orders, flies ships out, frees and holds beams, and removes ships
    /// that leave. Headless when built without a root transform (the self-test, the auto-play check). Each spawn makes the same calls in the
    /// same order as the dogfight's own code did, so the dogfight's port plays tick for tick the same.
    /// </summary>
    public sealed class GameMissionHost : IMissionHost, IDisposable
    {
        readonly GameWorld _w;
        readonly FlightSystem _flight;
        readonly CombatRig _rig;
        public readonly AiDirector Ai;
        readonly Transform _root;
        readonly ShipViews _views;
        readonly JuiceDirector _juice;
        readonly PlaceholderShips.Materials _mats;
        readonly Dictionary<int, IPilotSource> _flyers = new Dictionary<int, IPilotSource>();
        /// <summary>Ship models forced to their nearest LOD for the first ticks (the scene lets them go), as the dogfight does.</summary>
        public readonly List<LODGroup> WarmLods = new List<LODGroup>();
        public readonly List<int> Spawned = new List<int>();

        /// <summary>The AI flies the player's ship.</summary>
        public bool Autopilot;
        /// <summary>The player's ship takes damage but never dies (soaks, checks, captures).</summary>
        public bool PlayerInvulnerable;

        public GameMissionHost(GameWorld w, FlightSystem flight, CombatRig rig, AiDirector ai, Transform root = null, ShipViews views = null,
                               JuiceDirector juice = null, PlaceholderShips.Materials mats = null)
        {
            _w = w; _flight = flight; _rig = rig; Ai = ai; _root = root; _views = views; _juice = juice; _mats = mats;
            _rig.Reaper.Reaped += OnReaped;
        }

        public void Dispose() => _rig.Reaper.Reaped -= OnReaped;

        Transform _scenery;

        public void Scenery(MissionFile.SceneryDef sc)
        {
            if (!Visual || _scenery != null || sc == null || (sc.Rocks.Count == 0 && sc.Fields.Count == 0)) return;
            _scenery = IceRocks.Scenery(sc, _root);
        }

        bool Visual => _root != null;
        public int Level => Ai.Level;
        public string LevelName => Ai.LevelName;
        public int LevelOf(string name) => Ai.Data.LevelOf(name);
        public bool PlayerIsAi => Autopilot;

        void OnReaped(ShipCombat c)
        {
            Ai.Remove(c.Id);
            _juice?.ForgetShip(c.Id);
            _flyers.Remove(c.Id);
        }

        // ------------------------------------------------------------------------------------------------ spawning

        static bool IsBigClass(string cls, ShipClass klass)
        {
            var def = CombatCatalog.Get(cls);
            if (def != null) return def.SizeClass == CombatEvents.SizeCorvette || def.SizeClass == CombatEvents.SizeCapital;
            return klass.Size.z > 120f;
        }

        public int Spawn(ShipSpawn s)
        {
            var cls = ShipCatalog.Load(s.Class);
            var st = _flight.Spawn(cls, s.Position, s.Rotation, s.Speed * cls.TopSpeed, s.Throttle, s.Team, s.Name);
            string defId = CombatCatalog.Get(s.Class) != null ? s.Class : null;
            bool big = IsBigClass(s.Class, cls);
            if (s.IsPlayer)
            {
                // First person only: the player's own ship is never drawn.
                _w.LocalPlayerShip = st.Id;
                _rig.Spawn(st, defId ?? "vanguard_fighter");
            }
            else if (ShipArtCatalog.Exists(s.Class))
            {
                var go = ShipArtCatalog.Instantiate(s.Class, _root);
                var damage = go.GetComponent<ShipDamage>();
                var parts = damage != null ? ShipArtVisual.Parts(damage) : null;
                if (Visual)
                {
                    var lod = go.GetComponent<LODGroup>();
                    if (lod != null) { lod.ForceLOD(0); WarmLods.Add(lod); }
                    _views.Register(st.Id, go.transform);
                    var visual = damage != null ? new ShipArtVisual(_w, st.Id, damage, juiceShowsWrecksAndHits: true) : null;
                    _rig.Spawn(st, defId, visual, go.transform, _juice, parts, s.HullScale, s.SelfHeal, damage != null ? new ShipArtJuiceDamage(go) : null);
                }
                else
                {
                    _rig.Spawn(st, defId, null, null, null, parts, s.HullScale, s.SelfHeal);
                    UnityEngine.Object.Destroy(go);
                }
            }
            else if (big)
            {
                if (Visual)
                {
                    var go = PlaceholderLook(s, cls);
                    _views.Register(st.Id, go.transform);
                    _rig.Spawn(st, defId, new PlaceholderVisual(go.transform), go.transform, _juice, null, s.HullScale, s.SelfHeal);
                }
                else _rig.Spawn(st, defId, null, null, null, null, s.HullScale, s.SelfHeal);
            }
            else
            {
                if (Visual)
                {
                    var go = PlaceholderShips.Fighter(s.Name, cls.Size, _mats, _root);
                    _views.Register(st.Id, go.transform);
                    _rig.Spawn(st, defId, new PlaceholderVisual(go.transform), go.transform, _juice, null, s.HullScale, s.SelfHeal);
                }
                else _rig.Spawn(st, defId, null, null, null, null, s.HullScale, s.SelfHeal);
            }

            var c = _w.Combat.Get(st.Id);
            if (c != null)
            {
                if (s.Hull01 < 0.999f) { c.Hull = c.HullMax * s.Hull01; c.Visual?.SetHullDamage(1f - s.Hull01); }
                if (s.Invulnerable || (s.IsPlayer && PlayerInvulnerable)) c.Invulnerable = true;
                if (s.Ammo != null)
                    foreach (var m in c.Missiles)
                        if (s.Ammo.TryGetValue(m.Weapon, out int n)) m.Ammo = m.AmmoMax = n;
            }

            if (s.IsPlayer)
            {
                if (Autopilot)
                {
                    var ap = Ai.AddPilot(st.Id, "autopilot", AiRole.Fighter);
                    ap.DefaultGoal = new AiGoal { Kind = GoalKind.Engage, Target = GameWorld.NoShip, Part = -1 };
                }
                Ai.Stats.Track(st.Id, Autopilot ? Ai.PilotOf(st.Id).Skill.Key + "(player ship)" : "player");
            }
            else if (s.Capital != null || big) Ai.AddCapital(st.Id, ToOrders(s.Capital));
            else
            {
                var p = Ai.AddPilot(st.Id, s.Ai ?? (Ai.IsHostileSide(s.Team) ? "drone" : "wingman"), s.Role == "bomber" ? AiRole.Bomber : AiRole.Fighter);
                if (s.Wingman) Ai.PlayerWing.Add(p);
            }
            Spawned.Add(st.Id);
            return st.Id;
        }

        /// <summary>
        /// A big ship with no Blender model: a placeholder hull (the mission's <c>look.hull</c>, else the class's size), built into a rock of ice when
        /// the look has one (a comet station). The class's own model, once ShipArt has it, replaces all of this without the mission changing.
        /// </summary>
        GameObject PlaceholderLook(ShipSpawn s, ShipClass cls)
        {
            var look = s.Look;
            bool hull = look == null || look.HasHull || !look.HasRock;
            var h = look != null && look.HasHull ? look : new MissionFile.LookDef { Length = cls.Size.z, Width = cls.Size.x, Height = cls.Size.y, Seed = s.Name.Length * 7 + 3 };
            if (look == null || !look.HasRock)
            {
                var only = PlaceholderShips.Hull(s.Name, h.Length, h.Width, h.Height, h.Seed, _mats, _root);
                if (look != null && look.HullOffset != Vector3.zero) foreach (Transform c in only.transform) c.localPosition += look.HullOffset;
                return only;
            }
            var root = new GameObject(s.Name);
            root.transform.SetParent(_root, false);
            IceRocks.Rock(s.Name + " nucleus", look.RockRadius, look.RockStretch, look.RockSeed, root.transform);
            if (hull)
            {
                var station = PlaceholderShips.Hull(s.Name + " station", h.Length, h.Width, h.Height, h.Seed, _mats, root.transform);
                station.transform.localPosition = look.HullOffset;
            }
            return root;
        }

        static CapitalOrders ToOrders(MissionFile.CapitalDef d)
        {
            if (d == null) return new CapitalOrders();
            return new CapitalOrders
            {
                Standoff = d.Standoff, Band = d.Band, Bearing = d.Bearing, Throttle = d.Throttle, TurretProfile = d.TurretProfile,
                TurretWaitScale = d.TurretWaitScale, BeamWaitScale = d.BeamWaitScale, HoldBeams = d.HoldBeams, HoldTurrets = d.HoldTurrets, Stationary = d.Stationary,
                Weapons = new Dictionary<string, string>(d.Weapons),
            };
        }

        // ------------------------------------------------------------------------------------------------ orders

        public bool SetOrders(int ship, ShipOrder current, ShipOrder fallback)
        {
            if (current != null && current.Kind == OrderKind.Waypoints)
            {
                var me = _w.Ships.Get(ship);
                if (me == null) return false;
                bool big = Ai.PilotOf(ship) == null;
                var wp = new WaypointPilot(_w, ship, current.Path, current.Speed, current.Then == "loop", big);
                _flyers[ship] = wp;
                _w.Pilots.Attach(ship, wp);
                return true;
            }
            var p = Ai.PilotOf(ship);
            if (p == null) return false;
            bool changed = false;
            if (fallback != null)
            {
                var g = ToGoal(fallback, ship);
                if (!Same(p.DefaultGoal, g)) { p.DefaultGoal = g; changed = true; }
            }
            if (current != null)
            {
                var g = ToGoal(current, ship);
                // A fighter that was flying waypoints goes back to its own brain for a fight order.
                if (_flyers.Remove(ship)) { _w.Pilots.Attach(ship, p); changed = true; }
                if (!Same(p.Goal, g)) { p.SetGoal(g); changed = true; }
            }
            return changed;
        }

        static bool Same(AiGoal a, AiGoal b) => a.Kind == b.Kind && a.Target == b.Target && a.Part == b.Part;

        AiGoal ToGoal(ShipOrder o, int ship)
        {
            switch (o.Kind)
            {
                case OrderKind.Attack: return new AiGoal { Kind = GoalKind.Attack, Target = o.Target, Part = o.Part };
                case OrderKind.Disable: return new AiGoal { Kind = GoalKind.Disable, Target = o.Target, Part = -1 };
                case OrderKind.Disarm:
                {
                    // No disarm brain of its own: attack the target's first living weapon part.
                    int part = o.Part;
                    var tc = _w.Combat.Get(o.Target);
                    if (part < 0 && tc != null)
                        foreach (var q in tc.Parts) if (!q.Destroyed && (q.IsTurret || q.Kind == "launcher" || q.Kind == "mount")) { part = q.Index; break; }
                    return new AiGoal { Kind = GoalKind.Attack, Target = o.Target, Part = part };
                }
                case OrderKind.Guard: return new AiGoal { Kind = GoalKind.Guard, Target = o.Target, Part = -1 };
                case OrderKind.Form: return new AiGoal { Kind = GoalKind.Form, Target = o.Target, Part = -1 };
                case OrderKind.Bomb: return new AiGoal { Kind = GoalKind.Bomb, Target = o.Target, Part = -1 };
                default: return new AiGoal { Kind = GoalKind.Engage, Target = GameWorld.NoShip, Part = -1 };
            }
        }

        public bool OrderFinished(int ship) => _flyers.TryGetValue(ship, out var f) && f is WaypointPilot wp && wp.Finished;

        public void FlyOut(int ship, Vector3? to)
        {
            Ai.Remove(ship);
            var fo = new FlyOutPilot(_w, ship, to);
            _flyers[ship] = fo;
            _w.Pilots.Attach(ship, fo);
        }

        public void Remove(int ship)
        {
            _w.Pilots.Detach(ship);
            _w.Combat.Remove(ship);
            _flight.Despawn(ship);
            _juice?.ForgetShip(ship);
            Ai.Remove(ship);
            _flyers.Remove(ship);
        }

        public void SetBeams(int ship, bool free)
        {
            foreach (var cp in Ai.CapitalPilots) if (cp.Ship == ship) cp.Orders.HoldBeams = !free;
        }

        public void SetTurrets(int ship, bool fire)
        {
            foreach (var cp in Ai.CapitalPilots) if (cp.Ship == ship) cp.Orders.HoldTurrets = !fire;
        }

        public void DestroyPart(int ship, int partIndex)
        {
            var c = _w.Combat.Get(ship);
            if (c == null || partIndex < 0 || partIndex >= c.Parts.Count) return;
            var p = c.Parts[partIndex];
            _w.Damage.Add(new DamageRequest
            {
                Ship = ship, Attacker = GameWorld.NoShip, Weapon = "mission", Amount = p.MaxHp * 10f + 10f, ShieldMult = 0f, HullMult = 0f, SubsystemMult = 1f,
                Part = partIndex, Kind = DamageKind.Debug, BypassShield = true, Point = p.WorldPosition(c.Ship),
            });
        }

        public void RestorePart(int ship, int partIndex)
        {
            var c = _w.Combat.Get(ship);
            if (c == null || partIndex < 0 || partIndex >= c.Parts.Count) return;
            var p = c.Parts[partIndex];
            // Back to a quarter (the living tech mends the rest); a token hit on it makes the damage system work out what the ship can do again.
            p.Hp = Mathf.Max(p.Hp, p.MaxHp * 0.25f);
            _w.Damage.Add(new DamageRequest { Ship = ship, Attacker = GameWorld.NoShip, Weapon = "mission", Amount = 0.001f, SubsystemMult = 1f, Part = partIndex, Kind = DamageKind.Debug, BypassShield = true, Point = p.WorldPosition(c.Ship) });
        }

        /// <summary>Take every ship this host brought in out of the world (a restart).</summary>
        public void Clear()
        {
            foreach (int id in Spawned)
            {
                if (_w.Ships.Get(id) == null) continue;
                _w.Pilots.Detach(id);
                _w.Combat.Remove(id);
                _flight.Despawn(id);
                _juice?.ForgetShip(id);
            }
            Spawned.Clear();
            _flyers.Clear();
            Ai.Clear();
            _rig.Weapons.ClearAll();
            _juice?.ClearAll();
            _w.LocalPlayerShip = GameWorld.NoShip;
        }
    }

    /// <summary>Flies a ship along waypoints at a set speed (a freighter's crossing, a patrol), then holds its heading, or loops.</summary>
    public sealed class WaypointPilot : IPilotSource
    {
        readonly GameWorld _w;
        readonly int _ship;
        readonly List<Vector3> _path;
        readonly float _speed;
        readonly bool _loop, _big;
        int _i;
        public bool Finished { get; private set; }

        public WaypointPilot(GameWorld w, int ship, List<Vector3> path, float speed, bool loop, bool big)
        {
            _w = w; _ship = ship; _path = path ?? new List<Vector3>(); _speed = speed; _loop = loop; _big = big;
        }

        public ControlInput Sample(in SimFrame frame)
        {
            var input = default(ControlInput);
            var me = _w.Ships.Get(_ship);
            if (me == null || _path.Count == 0) return input;
            var c = _w.Combat.Get(_ship);
            if (c != null && !c.Alive) return input;
            input.Throttle = _speed >= 0f ? _speed : (_big ? 0.6f : 0.75f);
            if (Finished) return input;
            Vector3 to = _path[_i];
            float reach = Mathf.Max(_big ? 200f : 60f, me.Radius * 2f);
            if (Vector3.Distance(me.Position, to) < reach)
            {
                _i++;
                if (_i >= _path.Count) { if (_loop) _i = 0; else { Finished = true; return input; } }
                to = _path[_i];
            }
            AiSteer.TurnTo(me, AiSteer.Dir(me, to), 1f, ref input, Vector3.up, _big ? 0f : 0.4f);
            return input;
        }
    }

    /// <summary>A ship leaving: AI off, full throttle and afterburner, towards a point (or on along its heading) until it jumps.</summary>
    public sealed class FlyOutPilot : IPilotSource
    {
        readonly GameWorld _w;
        readonly int _ship;
        readonly Vector3? _to;
        public FlyOutPilot(GameWorld w, int ship, Vector3? to) { _w = w; _ship = ship; _to = to; }

        public ControlInput Sample(in SimFrame frame)
        {
            var input = new ControlInput { Throttle = 1f, Buttons = ControlButtons.Afterburner };
            var me = _w.Ships.Get(_ship);
            if (me == null) return input;
            if (_to.HasValue) AiSteer.TurnTo(me, AiSteer.Dir(me, _to.Value), 1f, ref input, Vector3.up, 0.3f);
            return input;
        }
    }
}
