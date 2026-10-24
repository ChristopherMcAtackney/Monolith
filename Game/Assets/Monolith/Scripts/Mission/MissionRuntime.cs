using System;
using System.Collections.Generic;
using System.Globalization;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Mission
{
    /// <summary>
    /// Runs one mission on the 60 Hz sim (order <see cref="SimOrder.Mission"/>, after weapons and damage, so it reacts to what just happened):
    /// brings ships and wings in on their arrival cues and the events' actions (through <see cref="IMissionHost"/>), holds capped arrivals back,
    /// sends wings' later waves, evaluates every event's condition each tick (FS2's events: delays, repeat counts and intervals, chaining,
    /// directives), tracks objectives, plays the radio and the music state, decides the end, and logs the pacing timeline. Publishes the
    /// directives and objectives on <see cref="GameWorld.Mission"/> for the HUD.
    /// <para>Each tick: arrivals waiting for the cap that now fit; departures that jump; arrival and departure cues and wing waves; objectives;
    /// the player's death; events in file order; the radio; the cabin. Once a second: the pacing sample and the music.</para>
    /// </summary>
    public sealed class MissionRuntime : ISimSystem, IDisposable
    {
        public readonly GameWorld World;
        public readonly MissionFile File;
        public readonly IMissionHost Host;
        public readonly ICabin Cabin;
        public readonly MissionBoard Board = new MissionBoard();
        public readonly MissionRadio Radio;
        public readonly MissionMusic Music;
        public readonly MissionLog Log = new MissionLog();

        public float StartTime { get; private set; }
        public float Now { get; private set; }
        public bool Started { get; private set; }
        public bool Ended { get; private set; }
        public string Result { get; private set; }
        public string ResultText { get; private set; }
        public float EndedAt { get; private set; } = -1f;
        public int Kills { get; private set; }
        public readonly List<string> Losses = new List<string>();
        /// <summary>When the first hostile fighter or bomber arrived (the fight's start, for the pacing), or -1.</summary>
        public float FightFrom { get; private set; } = -1f;
        public event Action<MissionRuntime> OnEnded;
        /// <summary>The scene holds the mission from ending on the player's death while this says true (the death sequence plays first; the failure flow follows).</summary>
        public Func<bool> HoldPlayerDeath;

        public int Order => SimOrder.Mission;

        // ------------------------------------------------------------------------------------------------ records

        public sealed class ShipRec
        {
            public string Name;
            public MissionFile.ShipDef Def;
            public WingRec Wing;
            public MissionFile.GroupDef Group;
            /// <summary>Its place in its wing (all waves), 0-based, and in its wave.</summary>
            public int Index, InWave, Wave;
            public int Team;
            public int Id = GameWorld.NoShip;
            public float ArrivedAt = -1f, DestroyedAt = -1f, DepartedAt = -1f, DisabledAt = -1f, DisarmedAt = -1f;
            public bool Queued, Departing, Wingman, IsPlayer;
            public float ArriveAt = -1f, DepartAt = -1f, JumpAt = -1f;
            public Vector3? DepartTo;
            public int DepartBay = GameWorld.NoShip;
            public bool OnWaypoints;
            public string WaypointThen;
            public MissionFile.Loadout Loadout => (MissionFile.Loadout)Def ?? Group;
            public MissionFile.ArrivalDef Arrival => Def != null ? Def.Arrival : Wing.Def.Arrival;
            public MissionFile.DepartureDef Departure => Def != null ? Def.Departure : Wing.Def.Departure;
            public bool Present => ArrivedAt >= 0f && DestroyedAt < 0f && DepartedAt < 0f;
        }

        public sealed class WingRec
        {
            public MissionFile.WingDef Def;
            public readonly List<ShipRec> Ships = new List<ShipRec>();
            public int WavesSent;
            public float ArriveAt = -1f, NextWaveAt = -1f, DepartAt = -1f;
            public int PerWave => Def.PerWave;
        }

        public enum EventState { Waiting, Live, Scheduled, Rearm, Done, Failed }

        public sealed class EventRec
        {
            public MissionFile.EventDef Def;
            public EventState State;
            public float TrueAt = -1f, DueAt = -1f, NextAt = -1f, FirstFiredAt = -1f, LastFiredAt = -1f, FailedAt = -1f;
            public int Fired;
        }

        public sealed class ObjectiveRec
        {
            public MissionFile.ObjectiveDef Def;
            public MissionBoard.Objective View;
            public float At = -1f;
        }

        readonly Dictionary<string, ShipRec> _ships = new Dictionary<string, ShipRec>();
        readonly List<ShipRec> _shipList = new List<ShipRec>();
        readonly Dictionary<string, WingRec> _wings = new Dictionary<string, WingRec>();
        readonly List<WingRec> _wingList = new List<WingRec>();
        readonly Dictionary<int, ShipRec> _byId = new Dictionary<int, ShipRec>();
        readonly List<EventRec> _events = new List<EventRec>();
        readonly Dictionary<string, EventRec> _eventByName = new Dictionary<string, EventRec>();
        readonly List<ObjectiveRec> _objectives = new List<ObjectiveRec>();
        readonly Dictionary<string, ObjectiveRec> _objectiveById = new Dictionary<string, ObjectiveRec>();
        readonly Dictionary<string, MissionFile.MessageDef> _messages = new Dictionary<string, MissionFile.MessageDef>();
        readonly List<ShipRec> _capQueue = new List<ShipRec>();
        readonly HashSet<int> _beaming = new HashSet<int>();
        readonly List<IDisposable> _subs = new List<IDisposable>();
        readonly List<float> _hulls = new List<float>();
        ShipRec _player;
        int _cap = int.MaxValue;

        public IReadOnlyList<ShipRec> Ships => _shipList;
        public IReadOnlyList<WingRec> Wings => _wingList;
        public IReadOnlyList<EventRec> Events => _events;
        public IReadOnlyList<ObjectiveRec> Objectives => _objectives;
        public ShipRec PlayerRec => _player;

        public MissionRuntime(GameWorld world, MissionFile file, IMissionHost host, ICabin cabin = null)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            File = file ?? throw new ArgumentNullException(nameof(file));
            Host = host ?? throw new ArgumentNullException(nameof(host));
            Cabin = cabin ?? new CabinStub();
            Radio = new MissionRadio(this);
            Music = new MissionMusic(this, file.Music.Tracks);
            Log.QuietRange = file.Pacing.QuietRange;

            foreach (var s in file.Ships)
            {
                var r = new ShipRec { Name = s.Name, Def = s, Team = s.Team, Wingman = s.Wingman, IsPlayer = s.Name == file.PlayerName };
                _ships[r.Name] = r; _shipList.Add(r);
                if (r.IsPlayer) _player = r;
            }
            foreach (var w in file.Wings)
            {
                var wr = new WingRec { Def = w };
                int k = 0;
                for (int wave = 0; wave < Mathf.Max(1, w.Waves); wave++)
                {
                    int inWave = 0;
                    foreach (var g in w.Groups)
                        for (int j = 0; j < g.Count; j++)
                        {
                            var r = new ShipRec { Name = $"{w.Name} {k + 1}", Wing = wr, Group = g, Index = k, InWave = inWave++, Wave = wave, Team = w.Team, Wingman = w.Wingmen || g.Wingman };
                            k++;
                            wr.Ships.Add(r);
                            _ships[r.Name] = r; _shipList.Add(r);
                        }
                }
                _wings[w.Name] = wr; _wingList.Add(wr);
            }
            foreach (var e in file.Events) { var er = new EventRec { Def = e }; _events.Add(er); _eventByName[e.Name] = er; }
            foreach (var o in file.Objectives)
            {
                var or = new ObjectiveRec { Def = o, View = new MissionBoard.Objective { Id = o.Id, Kind = o.Kind, Text = o.Text, Hidden = o.Hidden, State = MissionBoard.State.Active } };
                _objectives.Add(or); _objectiveById[o.Id] = or;
            }
            foreach (var m in file.Messages) _messages[m.Id] = m;

            _subs.Add(GameEvents.Subscribe(CombatEvents.ShipDestroyed, OnDestroyed));
            _subs.Add(GameEvents.Subscribe(CombatEvents.BeamCharge, e => _beaming.Add(e.Entity)));
            _subs.Add(GameEvents.Subscribe(CombatEvents.BeamEnd, e => _beaming.Remove(e.Entity)));
        }

        public void Dispose()
        {
            foreach (var s in _subs) s.Dispose();
            _subs.Clear();
            if (World.Mission == Board) World.Mission = null;
        }

        // ------------------------------------------------------------------------------------------------ start

        /// <summary>Bring in everything present at the start (ships in file order, then wings) and publish the board.</summary>
        public void Start()
        {
            if (Started) throw new InvalidOperationException("a mission runtime starts once; make a new one to fly again");
            Started = true;
            StartTime = Now = World.Time;
            Board.Id = File.Id; Board.Name = File.Name; Board.StartedAt = StartTime;
            foreach (var o in _objectives) Board.Objectives.Add(o.View);
            World.Mission = Board;
            var hullNames = new List<string>(File.Pacing.Hulls);
            Log.Begin(StartTime, hullNames);
            if (File.ArrivalCap != null) _cap = Mathf.RoundToInt(File.ArrivalCap.At(Host.Level));
            Music.Start(File.Music.Start, File.Music.Auto);
            Note("start", $"{File.Name} ({File.Id}) at {Host.LevelName}{(Host.PlayerIsAi ? ", the AI flying the player" : "")}");
            foreach (var r in _shipList) if (r.Def != null && r.Def.Arrival.AtStart) Spawn(r, true);
            foreach (var w in _wingList) if (w.Def.Arrival.AtStart) SendWave(w, true);
            MnLog.Info($"MISSION start '{File.Name}' at {Host.LevelName}: {File.Ships.Count} ships, {File.Wings.Count} wings, {File.Events.Count} events, {File.Objectives.Count} objectives{(File.ArrivalCap != null ? $", arrival cap {_cap}" : "")}");
        }

        // ------------------------------------------------------------------------------------------------ the tick

        public void Step(in SimFrame frame)
        {
            if (!Started) return;
            Now = frame.Time;
            if (!Ended)
            {
                DrainQueue();
                Track();
                Cues();
                CheckObjectives();
                if (!Ended && File.PlayerDeathEnds && _player != null && _player.DestroyedAt >= 0f && !(HoldPlayerDeath != null && HoldPlayerDeath())) End("defeat", "SHIP LOST");
                for (int i = 0; i < _events.Count && !Ended; i++) StepEvent(_events[i]);
                Cabin.Step(this);
                if (frame.Tick % 6 == 0) UpdateDirectives();
            }
            Radio.Step(Now);
            if (frame.Tick % Sim.Hz == 0) Sample();
        }

        /// <summary>Disabled and disarmed ships, departures that are due to jump, waypoint orders that have finished.</summary>
        void Track()
        {
            foreach (var r in _shipList)
            {
                if (!r.Present) continue;
                var c = World.Combat.Get(r.Id);
                if (c != null && c.Alive)
                {
                    if (r.DisabledAt < 0f && (c.Flags & CombatFlags.EnginesDown) != 0) { r.DisabledAt = Now; Note("disabled", r.Name); }
                    bool disarmed = (c.Flags & CombatFlags.TurretsSilenced) != 0 || (!IsBig(c) && (c.Flags & CombatFlags.PrimaryOffline) != 0 && (c.HeavyGun == null || (c.Flags & CombatFlags.HeavyOffline) != 0));
                    if (r.DisarmedAt < 0f && disarmed) { r.DisarmedAt = Now; Note("disarmed", r.Name); }
                }
                if (r.OnWaypoints && Host.OrderFinished(r.Id))
                {
                    r.OnWaypoints = false;
                    Note("order", $"{r.Name} reached the end of its waypoints");
                    if (r.WaypointThen == "depart") BeginDeparture(r, null, GameWorld.NoShip, 0f);
                }
                if (r.Departing) StepDeparture(r);
            }
        }

        void Cues()
        {
            foreach (var r in _shipList)
            {
                if (r.Def == null) continue;
                var a = r.Def.Arrival;
                if (r.ArrivedAt < 0f && !r.Queued && !a.AtStart && a.Cue != Cond.Never)
                {
                    if (r.ArriveAt < 0f && a.Cue.Eval(this)) r.ArriveAt = Now + a.Delay;
                    if (r.ArriveAt >= 0f && Now >= r.ArriveAt) Spawn(r, false);
                }
                CueDeparture(r, r.Def.Departure);
            }
            foreach (var w in _wingList)
            {
                var a = w.Def.Arrival;
                if (w.WavesSent == 0 && !a.AtStart && a.Cue != Cond.Never)
                {
                    if (w.ArriveAt < 0f && a.Cue.Eval(this)) w.ArriveAt = Now + a.Delay;
                    if (w.ArriveAt >= 0f && Now >= w.ArriveAt) SendWave(w, false);
                }
                // FS2's wing waves: the next wave when this one is down to the threshold (all of it in, and a few seconds later).
                if (w.WavesSent > 0 && w.WavesSent < w.Def.Waves)
                {
                    if (w.NextWaveAt < 0f && WaveAllIn(w, w.WavesSent - 1) && AliveInWave(w, w.WavesSent - 1) <= w.Def.WaveThreshold) w.NextWaveAt = Now + w.Def.WaveDelay;
                    if (w.NextWaveAt >= 0f && Now >= w.NextWaveAt) { w.NextWaveAt = -1f; SendWave(w, false); }
                }
                var d = w.Def.Departure;
                if (d != null && w.DepartAt < 0f && w.WavesSent > 0 && d.Cue.Eval(this)) w.DepartAt = Now + d.Delay;
                if (w.DepartAt >= 0f && Now >= w.DepartAt)
                {
                    w.DepartAt = float.MaxValue;
                    foreach (var r in w.Ships) if (r.Present && !r.Departing) BeginDeparture(r, d.To, d.Bay != null ? IdOf(d.Bay) : GameWorld.NoShip, d.JumpAfter);
                }
            }
        }

        void CueDeparture(ShipRec r, MissionFile.DepartureDef d)
        {
            if (d == null || !r.Present || r.Departing) return;
            if (r.DepartAt < 0f && d.Cue.Eval(this)) r.DepartAt = Now + d.Delay;
            if (r.DepartAt >= 0f && Now >= r.DepartAt) BeginDeparture(r, d.To, d.Bay != null ? IdOf(d.Bay) : GameWorld.NoShip, d.JumpAfter);
        }

        bool WaveAllIn(WingRec w, int wave) { foreach (var r in w.Ships) if (r.Wave == wave && r.ArrivedAt < 0f) return false; return true; }
        int AliveInWave(WingRec w, int wave) { int n = 0; foreach (var r in w.Ships) if (r.Wave == wave && r.Present) n++; return n; }

        // ------------------------------------------------------------------------------------------------ events

        void StepEvent(EventRec e)
        {
            var d = e.Def;
            switch (e.State)
            {
                case EventState.Done:
                case EventState.Failed:
                    return;
                case EventState.Waiting:
                    if (d.After != null)
                    {
                        var p = _eventByName[d.After];
                        if (p.FirstFiredAt < 0f || Now < p.FirstFiredAt + d.AfterDelay) return;
                    }
                    e.State = EventState.Live;
                    goto case EventState.Live;
                case EventState.Rearm:
                    if (Now < e.NextAt) return;
                    e.State = EventState.Live;
                    goto case EventState.Live;
                case EventState.Live:
                    if (d.FailWhen != null && d.FailWhen.Eval(this))
                    {
                        e.State = EventState.Failed; e.FailedAt = Now;
                        Note("event", $"'{d.Name}' failed");
                        return;
                    }
                    if (!d.When.Eval(this)) return;
                    e.TrueAt = Now;
                    if (d.Delay > 0f) { e.DueAt = Now + d.Delay; e.State = EventState.Scheduled; return; }
                    Fire(e);
                    return;
                case EventState.Scheduled:
                    if (Now >= e.DueAt) Fire(e);
                    return;
            }
        }

        void Fire(EventRec e)
        {
            var d = e.Def;
            e.Fired++;
            if (e.FirstFiredAt < 0f) e.FirstFiredAt = Now;
            e.LastFiredAt = Now;
            if (d.Log) Note("event", d.Name);
            foreach (var a in d.Do) RunAction(a);
            GameEvents.Raise(MissionEvents.EventFired, d.Name, e.Fired);
            if (d.Repeat < 0 || e.Fired < d.Repeat) { e.State = EventState.Rearm; e.NextAt = Now + d.Interval; }
            else e.State = EventState.Done;
        }

        public void RunAction(Act a)
        {
            try { a.Run(this); }
            catch (Exception ex) { MnLog.Error($"MISSION action '{a}' threw: {ex}"); Note("error", $"action '{a}' threw {ex.GetType().Name}: {ex.Message}"); }
        }

        public EventRec EventNamed(string name) => name != null && _eventByName.TryGetValue(name, out var e) ? e : null;
        public MissionFile.MessageDef MessageNamed(string id) => id != null && _messages.TryGetValue(id, out var m) ? m : null;

        // ------------------------------------------------------------------------------------------------ objectives and directives

        void CheckObjectives()
        {
            foreach (var o in _objectives)
            {
                if (o.View.State != MissionBoard.State.Active) continue;
                if (o.Def.Fail != null && o.Def.Fail.Eval(this)) SetObjective(o.Def.Id, "failed");
                else if (o.Def.Complete != null && o.Def.Complete.Eval(this)) SetObjective(o.Def.Id, "complete");
            }
        }

        public MissionBoard.State ObjectiveState(string id) => _objectiveById.TryGetValue(id, out var o) ? o.View.State : MissionBoard.State.Active;

        public void SetObjective(string id, string set)
        {
            if (!_objectiveById.TryGetValue(id, out var o)) return;
            var v = o.View;
            switch (set)
            {
                case "show": v.Hidden = false; break;
                case "hide": v.Hidden = true; break;
                default:
                {
                    var s = set == "complete" ? MissionBoard.State.Done : set == "failed" ? MissionBoard.State.Failed : MissionBoard.State.Active;
                    if (s == v.State) return;
                    v.State = s; o.At = Now;
                    if (s == MissionBoard.State.Done) v.Hidden = false;     // a secret bonus shows once it is met
                    GameEvents.Raise(MissionEvents.ObjectiveChanged, id, s == MissionBoard.State.Done ? 1f : s == MissionBoard.State.Failed ? -1f : 0f);
                    if (s != MissionBoard.State.Active) Music.Sting(s == MissionBoard.State.Done ? "goal_complete" : "goal_failed");
                    break;
                }
            }
            Board.Version++;
            Note("objective", $"{id} {set}: {o.Def.Text}");
        }

        void UpdateDirectives()
        {
            const float Linger = 4f;
            var list = Board.Directives;
            int n = 0;
            foreach (var e in _events)
            {
                var d = e.Def;
                if (d.DirectiveText == null) continue;
                MissionBoard.State st; float since;
                if (e.State == EventState.Live) { if (d.DirectiveShow != null && !d.DirectiveShow.Eval(this)) continue; st = MissionBoard.State.Active; since = Now; }
                else if ((e.State == EventState.Scheduled || e.State == EventState.Done || e.State == EventState.Rearm) && e.TrueAt >= 0f && Now < e.TrueAt + Linger) { st = MissionBoard.State.Done; since = e.TrueAt; }
                else if (e.State == EventState.Failed && Now < e.FailedAt + Linger) { st = MissionBoard.State.Failed; since = e.FailedAt; }
                else continue;
                if (n < list.Count)
                {
                    var l = list[n];
                    if (l.Text != d.DirectiveText || l.State != st) { l.Text = d.DirectiveText; l.State = st; l.Since = since; Board.Version++; }
                }
                else { list.Add(new MissionBoard.Directive { Text = d.DirectiveText, State = st, Since = since }); Board.Version++; }
                n++;
            }
            if (list.Count > n) { list.RemoveRange(n, list.Count - n); Board.Version++; }
        }

        // ------------------------------------------------------------------------------------------------ arrivals

        /// <summary>The "arrive" action: a ship (if it has not come yet) or a wing's next wave.</summary>
        public void Arrive(string name)
        {
            if (_wings.TryGetValue(name, out var w))
            {
                if (w.WavesSent >= Mathf.Max(1, w.Def.Waves)) { Note("arrive", $"{name}: no waves left"); return; }
                SendWave(w, false);
                return;
            }
            var r = Rec(name);
            if (r == null || r.ArrivedAt >= 0f || r.Queued) return;
            if (r.Wing != null && IsCapped(r.Wing)) { Enqueue(r); DrainQueue(); return; }
            Spawn(r, false);
        }

        void SendWave(WingRec w, bool atStart)
        {
            int wave = w.WavesSent++;
            Note("wave", $"{w.Def.Name} wave {wave + 1}/{Mathf.Max(1, w.Def.Waves)} ({w.PerWave} ships)");
            foreach (var r in w.Ships)
            {
                if (r.Wave != wave) continue;
                if (IsCapped(w)) Enqueue(r);
                else Spawn(r, atStart);
            }
            DrainQueue();
        }

        bool IsCapped(WingRec w) => w.Def.Capped && File.ArrivalCap != null && w.Def.Team == File.ArrivalCap.Team;

        void Enqueue(ShipRec r) { if (!r.Queued && r.ArrivedAt < 0f) { r.Queued = true; _capQueue.Add(r); } }

        int CappedAlive()
        {
            int n = 0;
            foreach (var w in _wingList)
            {
                if (!IsCapped(w)) continue;
                foreach (var r in w.Ships) if (r.Present) { var c = World.Combat.Get(r.Id); if (c != null && c.Alive) n++; }
            }
            return n;
        }

        /// <summary>Arrivals held back by the cap come in, oldest first, while the team has room.</summary>
        void DrainQueue()
        {
            while (_capQueue.Count > 0 && CappedAlive() < _cap)
            {
                var r = _capQueue[0];
                _capQueue.RemoveAt(0);
                r.Queued = false;
                Spawn(r, false);
            }
        }

        public int QueuedCount => _capQueue.Count;

        void Spawn(ShipRec r, bool atStart)
        {
            var a = r.Arrival;
            var load = r.Loadout;
            Place(r, a, out Vector3 pos, out Quaternion rot);
            var spawn = new ShipSpawn
            {
                Name = r.Name, Class = load.Class, Ai = load.Ai, Role = load.Role, Team = r.Team, Position = pos, Rotation = rot,
                Speed = a.Speed, Throttle = a.Throttle >= 0f ? a.Throttle : a.Speed, IsPlayer = r.IsPlayer, Wingman = r.Wingman,
                Invulnerable = load.Invulnerable, SelfHeal = load.SelfHeal, HullScale = load.HullScale, Hull01 = load.Hull / 100f,
                Ammo = load.Ammo, Capital = load.Capital, Look = load.Look,
            };
            int id = Host.Spawn(spawn);
            r.Id = id; r.ArrivedAt = Now; r.Queued = false;
            _byId[id] = r;
            ApplyOrders(r);
            var s = World.Ships.Get(id);
            var c = World.Combat.Get(id);
            if (!atStart)
            {
                if (a.Effect == "jump" && s != null)
                    GameEvents.RaiseAt(MissionEvents.JumpIn, s.Position, s.Forward, c?.SizeClass ?? CombatEvents.SizeFighter, s.Radius, id);
                bool hostile = _player != null && Teams.Hostile(_player.Team, r.Team);
                if (r.Wing == null || r.InWave == 0) Music.Sting(hostile ? "enemy_arrival" : "friendly_arrival");
            }
            if (FightFrom < 0f && _player != null && Teams.Hostile(_player.Team, r.Team) && c != null && !IsBig(c)) FightFrom = Now;
            Note("arrive", r.Name + (atStart ? " (start)" : ""));
        }

        /// <summary>Where a ship comes in: its anchor (a point, near a ship, a ship's bay), facing, and its place in its wing's formation.</summary>
        void Place(ShipRec r, MissionFile.ArrivalDef a, out Vector3 pos, out Quaternion rot)
        {
            Vector3 anchor = Vector3.zero;
            Quaternion baseRot = Quaternion.identity;
            if (a.At.HasValue) anchor = a.At.Value;
            else if (a.Near != null)
            {
                var n = PresentShip(a.Near) ?? World.Ships.Get(World.LocalPlayerShip);
                if (n != null)
                {
                    if (a.Offset.HasValue) { anchor = n.Position + n.Rotation * a.Offset.Value; baseRot = n.Rotation; }
                    else
                    {
                        // FS2's "near ship": a set distance off the anchor, in a direction of its own (fixed by the name, so runs repeat).
                        uint h = 2166136261u; foreach (char ch in r.Wing != null ? r.Wing.Def.Name : r.Name) h = (h ^ ch) * 16777619u;
                        float yaw = (h & 0xFFFF) / 65535f * 360f, pitch = ((h >> 16) & 0xFF) / 255f * 50f - 25f;
                        anchor = n.Position + Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward * Mathf.Max(a.Distance, 100f);
                        baseRot = Quaternion.LookRotation((n.Position - anchor).normalized, Vector3.up);
                    }
                }
            }
            else if (a.Bay != null)
            {
                var b = PresentShip(a.Bay);
                if (b != null)
                {
                    anchor = b.Position + b.Rotation * (a.Offset ?? new Vector3(0f, -b.Radius * 0.3f, b.Radius * 0.2f));
                    baseRot = b.Rotation;
                }
            }

            if (a.Facing != null)
            {
                var t = World.Ships.Get(IdOf(a.Facing)) ?? World.Ships.Get(World.LocalPlayerShip);
                Vector3 toward = t != null ? t.Position - anchor : Vector3.forward;
                rot = toward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toward.normalized, Vector3.up) : baseRot;
            }
            else if (a.FacingDir.HasValue) rot = Quaternion.LookRotation(a.FacingDir.Value.normalized, Vector3.up);
            else if (a.Rot.HasValue) rot = Quaternion.Euler(a.Rot.Value);
            else rot = baseRot;

            if (r.Wing == null) { pos = anchor; return; }
            int k = r.InWave, n2 = r.Wing.PerWave;
            Vector3 sp = a.Spacing, offset;
            if (a.Offsets != null && a.Offsets.Count > 0) offset = a.Offsets[k % a.Offsets.Count];
            else switch (a.Formation)
            {
                case "line": { float c = k - (n2 - 1) * 0.5f; offset = new Vector3(c * sp.x, 0f, -Mathf.Abs(c) * sp.z); break; }
                case "column": offset = new Vector3(0f, k * sp.y, -k * sp.z); break;
                case "none": offset = Vector3.zero; break;
                default: { int rank = (k + 1) / 2; float side = k % 2 == 0 ? 1f : -1f; offset = new Vector3(side * rank * sp.x, rank * sp.y, -rank * sp.z); break; }
            }
            pos = anchor + rot * offset;
        }

        // ------------------------------------------------------------------------------------------------ orders

        static bool Standing(OrderKind k) => k == OrderKind.Guard || k == OrderKind.Form || k == OrderKind.Bomb || k == OrderKind.Engage;

        /// <summary>A ship's orders from the mission file when it arrives: its goal (or its wing's), and what it goes back to.</summary>
        void ApplyOrders(ShipRec r)
        {
            var load = r.Loadout;
            var goal = load.Goal ?? r.Wing?.Def.Goal;
            var fallback = load.Fallback ?? r.Wing?.Def.Fallback ?? (goal != null && Standing(goal.Kind) ? goal : null);
            if (goal == null && fallback == null) return;
            GiveOrders(r, goal, fallback, r.Wing != null ? r.Index : 0);
        }

        /// <summary>Returns true when the ship's orders changed (an order it is already carrying out changes nothing).</summary>
        bool GiveOrders(ShipRec r, MissionFile.OrderDef goal, MissionFile.OrderDef fallback, int k)
        {
            if (goal != null && goal.Kind == OrderKind.Depart) { BeginDeparture(r, goal.To, GameWorld.NoShip, 3f); return true; }
            var cur = goal != null ? Resolve(goal, k) : null;
            var fb = fallback != null && fallback.Kind != OrderKind.Depart && fallback.Kind != OrderKind.Waypoints ? Resolve(fallback, k) : null;
            bool changed = Host.SetOrders(r.Id, cur, fb);
            if (goal != null && goal.Kind == OrderKind.Waypoints) { r.OnWaypoints = goal.Then != "loop"; r.WaypointThen = goal.Then; }
            return changed;
        }

        /// <summary>The "order" action: to a ship, or to every ship of a wing that is in the fight.</summary>
        public void OrderShips(string name, MissionFile.OrderDef order, bool standing)
        {
            int k = 0; bool changed = false;
            foreach (var r in RecsOf(name))
            {
                if (!r.Present) continue;
                changed |= GiveOrders(r, order, standing ? order : null, k++);
            }
            if (changed) Note("order", $"{name}: {order}");
        }

        ShipOrder Resolve(MissionFile.OrderDef d, int k)
        {
            var o = new ShipOrder { Kind = d.Kind, Path = d.Path, Speed = d.Speed, Then = d.Then };
            if (d.Target != null)
            {
                if (_wings.TryGetValue(d.Target, out var w))
                {
                    var members = new List<ShipRec>();
                    foreach (var m in w.Ships) if (m.Present) members.Add(m);
                    if (members.Count == 0) foreach (var m in w.Ships) if (m.ArrivedAt >= 0f) members.Add(m);
                    o.Target = members.Count > 0 ? members[k % members.Count].Id : GameWorld.NoShip;
                }
                else o.Target = IdOf(d.Target);
            }
            if (d.Parts != null)
            {
                var c = World.Combat.Get(o.Target);
                if (c != null)
                    foreach (var name in d.Parts) { var p = c.FindPart(name); if (p != null && !p.Destroyed) { o.Part = p.Index; break; } }
            }
            return o;
        }

        // ------------------------------------------------------------------------------------------------ departures

        /// <summary>The "depart" action: a ship, or every ship of a wing in the fight, flies out and jumps.</summary>
        public void Depart(string name, Vector3? to, string bay, float jumpAfter)
        {
            int bayId = bay != null ? IdOf(bay) : GameWorld.NoShip;
            foreach (var r in RecsOf(name)) if (r.Present && !r.Departing) BeginDeparture(r, to, bayId, jumpAfter);
        }

        void BeginDeparture(ShipRec r, Vector3? to, int bay, float jumpAfter)
        {
            if (r.Departing || !r.Present) return;
            r.Departing = true; r.OnWaypoints = false;
            r.DepartTo = to; r.DepartBay = bay;
            var bs = World.Ships.Get(bay);
            Host.FlyOut(r.Id, bs != null ? bs.Position : to);
            r.JumpAt = bs != null ? Now + 60f : Now + Mathf.Max(0f, jumpAfter);
            Note("depart", $"{r.Name} leaving" + (bs != null ? $" for {bs.Callsign}'s bay" : ""));
        }

        void StepDeparture(ShipRec r)
        {
            var s = World.Ships.Get(r.Id);
            if (s == null) return;
            var bay = World.Ships.Get(r.DepartBay);
            bool docked = bay != null && Vector3.Distance(s.Position, bay.Position) < bay.Radius * 0.6f + 30f;
            if (!docked && Now < r.JumpAt) return;
            if (bay == null) GameEvents.RaiseAt(MissionEvents.JumpOut, s.Position, s.Forward, World.Combat.Get(r.Id)?.SizeClass ?? CombatEvents.SizeFighter, s.Radius, r.Id);
            r.Departing = false;
            r.DepartedAt = Now;
            Host.Remove(r.Id);
            Note("departed", r.Name + (bay != null ? " (docked)" : " (jumped)"));
        }

        // ------------------------------------------------------------------------------------------------ deaths, the end

        void OnDestroyed(GameEvent e)
        {
            if (!_byId.TryGetValue(e.Entity, out var r) || r.DestroyedAt >= 0f) return;
            r.DestroyedAt = World.Time;
            r.Departing = false;
            if (_player != null && e.Other == _player.Id && e.Entity != _player.Id) Kills++;
            if (_player != null && r != _player && !Teams.Hostile(_player.Team, r.Team)) Losses.Add(r.Name);
            Note("destroyed", r.Name + (e.Other != GameWorld.NoShip && World.Ships.Get(e.Other) != null ? " by " + World.Ships.Get(e.Other).Callsign : ""));
        }

        /// <summary>The "end" action (and the player's death): the mission is over; the debrief and the pacing stop counting.</summary>
        public void End(string result, string text)
        {
            if (Ended) return;
            CheckObjectives();      // objectives that hang on what just happened (the event that ends it) settle first
            Ended = true;
            Result = result; ResultText = text ?? (result == "victory" ? "MISSION COMPLETE" : result == "defeat" ? "MISSION FAILED" : "MISSION OVER");
            EndedAt = Now;
            Board.Ended = true; Board.Result = Result; Board.ResultText = ResultText; Board.Prompt = null; Board.Version++;
            UpdateDirectives();
            Music.OnEnd(result, _player != null && _player.DestroyedAt >= 0f);
            GameEvents.Raise(MissionEvents.MissionEnd, ResultText, result == "victory" ? 1f : result == "defeat" ? -1f : 0f);
            Note("end", $"{result}: {ResultText}");
            MnLog.Info($"MISSION end '{File.Name}': {result} \"{ResultText}\" after {Now - StartTime:0.0} s, kills by the player {Kills}, losses {Losses.Count}; {Log.Summary}");
            OnEnded?.Invoke(this);
        }

        /// <summary>The debrief: the result, each objective, kills and losses, and the mission's debrief stages whose conditions hold.</summary>
        public List<string> Debrief()
        {
            var lines = new List<string>();
            foreach (var o in _objectives)
            {
                if (o.View.Hidden && o.View.State != MissionBoard.State.Done) continue;
                string st = o.View.State == MissionBoard.State.Done ? "DONE" : o.View.State == MissionBoard.State.Failed ? "FAILED" : "NOT DONE";
                lines.Add($"{o.Def.Kind.ToUpperInvariant()}  {o.Def.Text.ToUpperInvariant()}  {st}");
            }
            lines.Add($"KILLS {Kills}   LOSSES {(Losses.Count == 0 ? "NONE" : string.Join(", ", Losses).ToUpperInvariant())}");
            foreach (var d in File.Debriefing) if (d.When.Eval(this)) lines.Add(d.Text);
            return lines;
        }

        // ------------------------------------------------------------------------------------------------ the pacing

        void Sample()
        {
            var player = World.Ships.Get(World.LocalPlayerShip);
            int threats = 0, engaging = 0, close = 0; float nearest = float.MaxValue; bool capitalClose = false;
            int team = _player != null ? _player.Team : 0;
            foreach (var c in World.Combat.All)
            {
                if (!c.Alive || c.Ship == null || !Teams.Hostile(team, c.Ship.Team)) continue;
                float d = player != null ? Vector3.Distance(c.Ship.Position, player.Position) : float.MaxValue;
                if (IsBig(c)) { if (d < MissionMusic.CloseRange) capitalClose = true; continue; }
                threats++;
                if (player != null && c.Ship.Target == player.Id) engaging++;
                if (d < MissionMusic.CloseRange) close++;
                nearest = Mathf.Min(nearest, d);
            }
            _hulls.Clear();
            foreach (var h in File.Pacing.Hulls) { var c = CombatOf(h); _hulls.Add(c != null && c.Alive ? c.Hull01 : 0f); }
            var pc = player != null ? World.Combat.Get(player.Id) : null;
            bool fighting = !Ended && FightFrom >= 0f;
            Log.Sample(Now, fighting, threats, engaging, nearest, pc != null && pc.Alive ? pc.Hull01 : 0f, _hulls, Music.State);
            if (!Ended) Music.Sample(close, engaging, capitalClose);
        }

        // ------------------------------------------------------------------------------------------------ what conditions read

        public static bool IsBig(ShipCombat c) =>
            c.SizeClass == CombatEvents.SizeCorvette || c.SizeClass == CombatEvents.SizeCapital || c.Shape.BoundRadius > 60f;   // AiDirector.IsBig's rule

        public bool IsBeaming(int ship) => _beaming.Contains(ship);

        ShipRec Rec(string name)
        {
            if (name == MissionFile.Player) return _player;
            return name != null && _ships.TryGetValue(name, out var r) ? r : null;
        }

        public ShipRec RecOf(string name) => Rec(name);
        public WingRec WingOf(string name) => name != null && _wings.TryGetValue(name, out var w) ? w : null;

        IEnumerable<ShipRec> RecsOf(string name)
        {
            if (_wings.TryGetValue(name, out var w)) { foreach (var r in w.Ships) yield return r; yield break; }
            var one = Rec(name);
            if (one != null) yield return one;
        }

        /// <summary>The ship id a name has had (even if it has died since), or <see cref="GameWorld.NoShip"/>.</summary>
        public int IdOf(string name) { var r = Rec(name); return r != null ? r.Id : GameWorld.NoShip; }

        /// <summary>Ids of a ship, or of a wing's ships, that are in the fight.</summary>
        public IEnumerable<int> IdsOf(string name) { foreach (var r in RecsOf(name)) if (r.Present) yield return r.Id; }

        /// <summary>A ship that is in the fight and alive, or null.</summary>
        public ShipState ShipOf(string name)
        {
            var r = Rec(name);
            if (r == null || !r.Present) return null;
            var c = World.Combat.Get(r.Id);
            return c != null && !c.Alive ? null : World.Ships.Get(r.Id);
        }

        ShipState PresentShip(string name) { var r = Rec(name); return r != null && r.Id != GameWorld.NoShip ? World.Ships.Get(r.Id) : null; }

        public ShipCombat CombatOf(string name) { var r = Rec(name); return r != null && r.Id != GameWorld.NoShip ? World.Combat.Get(r.Id) : null; }

        public bool HasArrived(string name) { var r = Rec(name); return r != null && r.ArrivedAt >= 0f; }
        public bool HasDeparted(string name) { var r = Rec(name); return r != null && r.DepartedAt >= 0f; }

        /// <summary>When a fact became true of a ship, or of every ship of a wing (for arrivals: its first wave, or with any its first ship), or -1.</summary>
        public float Since(string name, Fact fact, bool any)
        {
            if (_wings.TryGetValue(name, out var w))
            {
                if (fact == Fact.Arrived && any)
                {
                    float first = -1f;
                    foreach (var r in w.Ships) if (r.ArrivedAt >= 0f && (first < 0f || r.ArrivedAt < first)) first = r.ArrivedAt;
                    return first;
                }
                float last = -1f;
                foreach (var r in w.Ships)
                {
                    if (fact == Fact.Arrived && r.Wave > 0) continue;
                    float t = FactTime(r, fact);
                    if (t < 0f) return -1f;
                    last = Mathf.Max(last, t);
                }
                return last;
            }
            var one = Rec(name);
            return one != null ? FactTime(one, fact) : -1f;
        }

        static float FactTime(ShipRec r, Fact f)
        {
            switch (f)
            {
                case Fact.Arrived: return r.ArrivedAt;
                case Fact.Destroyed: return r.DestroyedAt;
                case Fact.Departed: return r.DepartedAt;
                case Fact.Disabled: return r.DisabledAt;
                default: return r.DisarmedAt;
            }
        }

        public void CountDestroyed(string name, out int dead, out int total)
        {
            dead = total = 0;
            foreach (var r in RecsOf(name)) { total++; if (r.DestroyedAt >= 0f) dead++; }
        }

        /// <summary>A living wingman of the player's (ships and wings marked as wingmen), first in the file first.</summary>
        public ShipState AnyWingman()
        {
            foreach (var r in _shipList)
                if (r.Wingman && !r.IsPlayer && r.Present) { var s = ShipOf(r.Name); if (s != null) return s; }
            return null;
        }

        public void Note(string kind, string text)
        {
            Log.Add(Now, kind, text);
            if (kind != "radio" && kind != "music") MnLog.Info($"MISSION t={Now - StartTime:0.0} {kind}: {text}");
        }

        public override string ToString() => $"{File.Name}: t={Now - StartTime:0.0}s{(Ended ? " ended " + Result : "")}";
    }
}
