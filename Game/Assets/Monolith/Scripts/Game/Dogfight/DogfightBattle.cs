using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Monolith.AI;
using Monolith.Core;
using Monolith.Flight;
using Monolith.Juice;
using Monolith.ShipArt;
using Monolith.View;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// The dogfight's rules, with or without anything on screen (the scene gives it visuals; the soak runs it bare): it spawns the player, the
    /// wingmen, our cruiser and Earth's corvette, brings Earth's waves in on FS2's rule (the next wave a few seconds after the current one is
    /// two-thirds destroyed, never more fighters alive than the level's cap), sends the bomber wing and its escort at the cruiser, says what
    /// happens on the radio, decides the win or loss, and logs the pacing (threats alive, hostiles on the player, time without a threat).
    /// Runs on the sim clock (order <see cref="SimOrder.Mission"/>).
    /// </summary>
    public sealed class DogfightBattle : ISimSystem, IDisposable
    {
        public enum State { Fighting, Won, Lost }

        public const int Earth = 1;

        readonly GameWorld _w;
        readonly FlightSystem _flight;
        readonly CombatRig _rig;
        public readonly AiDirector Ai;
        public readonly DogfightData Data;
        readonly List<IDisposable> _subs = new List<IDisposable>();

        // Visuals (all null when headless).
        readonly Transform _root;
        readonly ShipViews _views;
        readonly JuiceDirector _juice;
        readonly PlaceholderShips.Materials _mats;
        public readonly List<LODGroup> WarmLods = new List<LODGroup>();

        public ShipState Player { get; private set; }
        public int FriendlyId { get; private set; } = GameWorld.NoShip;
        public int CorvetteId { get; private set; } = GameWorld.NoShip;
        public readonly List<AiPilot> Wingmen = new List<AiPilot>();
        /// <summary>The AI flies the player's ship (soaks, captures).</summary>
        public bool Autopilot;
        public bool PlayerInvulnerable;
        /// <summary>
        /// The autopilot plays the objective as a player would (soaks, captures): when no Earth fighter is near, it goes after the Warden's beam
        /// turrets, then the Warden itself, between waves.
        /// </summary>
        public bool AutopilotObjectives = true;
        /// <summary>
        /// The Warden runs: the autopilot goes for the Warden's turrets from the start and keeps at it whatever else is about (the beam
        /// turrets first, then the living turret nearest to it), as a player stripping the corvette would.
        /// </summary>
        public bool AutopilotStripsTurrets;
        /// <summary>The Warden runs: no Earth fighters or bombers at all (the Warden's guns alone).</summary>
        public bool NoFighters;
        float _objectiveAt;
        /// <summary>Perf runs: the first three waves and the bomber wing all at once, no cap (the busiest the loop gets, and then some).</summary>
        public bool Stress;
        bool _stressed;
        public State Outcome { get; private set; }
        public string OutcomeText { get; private set; }
        public float StartedAt { get; private set; }
        public float EndedAt { get; private set; } = -1f;
        public int Loop { get; private set; }
        public int WaveIndex => _wave;
        public int WaveCount => Data.waves.Count;
        public readonly Pacing Log = new Pacing();
        public event Action<State> Ended;

        readonly List<int> _spawned = new List<int>();
        readonly Dictionary<int, int> _waveOf = new Dictionary<int, int>();
        readonly Queue<(int wave, DogfightData.Group g, int k, string name)> _queue = new Queue<(int, DogfightData.Group, int, string)>();
        int[] _waveTotal, _waveDead;
        int _wave = -1;
        float _nextWaveAt = -1f, _bombersAt = -1f, _mercyRadioAt = -1f;
        bool _bombersSent, _saidHurt, _saidBeams, _saidBeamThreat;
        int _kills;
        Vector3 _playerStartPos; Quaternion _playerStartRot;

        public int Kills => _kills;

        public DogfightBattle(GameWorld w, FlightSystem flight, CombatRig rig, AiDirector ai, Transform root = null, ShipViews views = null, JuiceDirector juice = null,
                              PlaceholderShips.Materials mats = null, DogfightData data = null)
        {
            _w = w; _flight = flight; _rig = rig; Ai = ai; Data = data ?? DogfightData.Load();
            _root = root; _views = views; _juice = juice; _mats = mats;
            _rig.Reaper.Reaped += OnReaped;
            _subs.Add(GameEvents.Subscribe(CombatEvents.ShipDestroyed, OnDestroyed));
        }

        public void Dispose()
        {
            _rig.Reaper.Reaped -= OnReaped;
            foreach (var s in _subs) s.Dispose();
            _subs.Clear();
        }

        public int Order => SimOrder.Mission;
        bool Visual => _root != null;
        int Level => Ai.Level;

        // ------------------------------------------------------------------------------------------------ start, reset

        /// <summary>Spawn everything for a fresh loop (first time: the player too).</summary>
        public void Start()
        {
            Loop++;
            Outcome = State.Fighting; OutcomeText = null; EndedAt = -1f;
            StartedAt = _w.Time;
            _wave = -1; _nextWaveAt = StartedAt + Data.firstWaveAt.At(Level); _bombersAt = -1f; _bombersSent = false; _saidHurt = false; _saidBeams = false; _saidBeamThreat = false;
            _mercyRadioAt = -1f; _kills = 0; _stressed = false;
            _waveTotal = new int[Data.waves.Count]; _waveDead = new int[Data.waves.Count];
            for (int i = 0; i < Data.waves.Count; i++) _waveTotal[i] = Data.waves[i].Count;
            _queue.Clear(); _waveOf.Clear();
            Log.Begin(StartedAt);

            if (Player == null) SpawnPlayer();
            else
            {
                _rig.Damage.Reset(Player.Id);
                var cls = ShipCatalog.Load(Player.ClassId);
                _flight.Place(Player.Id, _playerStartPos, _playerStartRot, Data.player.speedFraction * cls.TopSpeed, Data.player.throttle);
                Player.Target = GameWorld.NoShip;
            }
            var pc = _w.Combat.Get(Player.Id);
            pc.Invulnerable = PlayerInvulnerable;
            if (Autopilot)
            {
                var ap = Ai.AddPilot(Player.Id, "autopilot", AiRole.Fighter);
                ap.DefaultGoal = new AiGoal { Kind = GoalKind.Engage, Target = GameWorld.NoShip, Part = -1 };
            }
            Ai.Stats.Track(Player.Id, Autopilot ? Ai.PilotOf(Player.Id).Skill.Key + "(player ship)" : "player");

            SpawnFriendly();
            SpawnCorvette();
            SpawnWingmen();
            MnLog.Info($"DOGFIGHT loop {Loop} at {Ai.LevelName}: {Data.waves.Count} waves, cap {Data.waveCap.At(Level)}, Mercy #{FriendlyId}, Warden #{CorvetteId}, autopilot {Autopilot}");
        }

        /// <summary>Clear the field (every ship but the player's, every shot and effect) and start again.</summary>
        public void Restart()
        {
            foreach (int id in _spawned)
            {
                if (_w.Ships.Get(id) == null) continue;
                _w.Pilots.Detach(id);
                _w.Combat.Remove(id);
                _flight.Despawn(id);
                _juice?.ForgetShip(id);
            }
            _spawned.Clear();
            Wingmen.Clear();
            Ai.Clear();
            _rig.Weapons.ClearAll();
            _juice?.ClearAll();
            FriendlyId = CorvetteId = GameWorld.NoShip;
            Start();
        }

        void SpawnPlayer()
        {
            var p = Data.player;
            string shipId = CommandLine.Get("-mnShip", p.ship);
            var cls = ShipCatalog.Load(shipId);
            _playerStartPos = DogfightData.V3(p.pos); _playerStartRot = Quaternion.Euler(DogfightData.V3(p.rot));
            Player = _flight.Spawn(cls, _playerStartPos, _playerStartRot, p.speedFraction * cls.TopSpeed, p.throttle, 0, p.callsign ?? "Player");
            _w.LocalPlayerShip = Player.Id;
            _rig.Spawn(Player, CombatCatalog.Get(shipId) != null ? shipId : "vanguard_fighter");
        }

        void SpawnWingmen()
        {
            for (int i = 0; i < Data.wingmen.Count; i++)
            {
                var wm = Data.wingmen[i];
                Vector3 offset = new Vector3(i % 2 == 0 ? -32f : 32f, i % 2 == 0 ? 5f : -5f, -30f * (1 + i / 2));
                Vector3 pos = Player.Position + Player.Rotation * offset;
                var st = SpawnFighter(wm.ship, wm.callsign, 0, pos, Player.Rotation, Data.player.speedFraction, wm.profile ?? "wingman", AiRole.Fighter, null);
                var pilot = Ai.PilotOf(st.Id);
                if (wm.ammo != null)
                    foreach (var m in _w.Combat.Get(st.Id).Missiles)
                        if (wm.ammo.TryGetValue(m.Weapon, out int n)) m.Ammo = m.AmmoMax = n;
                Ai.PlayerWing.Add(pilot);
                pilot.DefaultGoal = new AiGoal { Kind = GoalKind.Guard, Target = Player.Id, Part = -1 };
                // They start free to engage; "cover me" or "form up" puts them back on the player.
                pilot.SetGoal(new AiGoal { Kind = GoalKind.Engage, Target = GameWorld.NoShip, Part = -1 });
                Wingmen.Add(pilot);
            }
        }

        ShipState SpawnFighter(string cls, string callsign, int team, Vector3 pos, Quaternion rot, float speedFraction, string profile, AiRole role, DogfightData.Group g)
        {
            var klass = ShipCatalog.Load(cls);
            var st = _flight.Spawn(klass, pos, rot, speedFraction * klass.TopSpeed, speedFraction, team, callsign);
            if (Visual)
            {
                var go = PlaceholderShips.Fighter(callsign, klass.Size, _mats, _root);
                _views.Register(st.Id, go.transform);
                _rig.Spawn(st, CombatCatalog.Get(cls) != null ? cls : null, new PlaceholderVisual(go.transform), go.transform, _juice);
            }
            else _rig.Spawn(st, CombatCatalog.Get(cls) != null ? cls : null);
            var c = _w.Combat.Get(st.Id);
            if (g != null && g.torpedoes >= 0)
                foreach (var m in c.Missiles) if (m.Weapon.StartsWith("torpedo", StringComparison.Ordinal)) { m.Ammo = m.AmmoMax = g.torpedoes; }
            Ai.AddPilot(st.Id, profile, role);
            _spawned.Add(st.Id);
            return st;
        }

        void SpawnFriendly()
        {
            var f = Data.friendly;
            var cls = ShipCatalog.Load(f.ship);
            var st = _flight.Spawn(cls, DogfightData.V3(f.pos), Quaternion.Euler(DogfightData.V3(f.rot)), f.orders.throttle * cls.TopSpeed, f.orders.throttle, 0, f.callsign);
            // A capital ship does not mend: the diaspora's living tech is a fighter's (at a capital's size it would out-heal any beam).
            if (Visual)
            {
                var h = f.hull ?? new DogfightData.HullShape();
                var go = PlaceholderShips.Hull(f.callsign, h.length, h.width, h.height, h.seed, _mats, _root);
                _views.Register(st.Id, go.transform);
                _rig.Spawn(st, f.ship, new PlaceholderVisual(go.transform), go.transform, _juice, null, f.hullScale, false);
            }
            else _rig.Spawn(st, f.ship, null, null, null, null, f.hullScale, false);
            Ai.AddCapital(st.Id, f.orders.ToOrders());
            FriendlyId = st.Id;
            _spawned.Add(st.Id);
        }

        void SpawnCorvette()
        {
            var cv = Data.corvette;
            var cls = ShipCatalog.Load(cv.ship);
            var st = _flight.Spawn(cls, DogfightData.V3(cv.pos), Quaternion.Euler(DogfightData.V3(cv.rot)), cv.orders.throttle * cls.TopSpeed, cv.orders.throttle, Earth, cv.callsign);
            var go = ShipArtCatalog.Instantiate(cv.ship, _root);
            var damage = go.GetComponent<ShipDamage>();
            var parts = ShipArtVisual.Parts(damage);
            if (Visual)
            {
                var lod = go.GetComponent<LODGroup>();
                if (lod != null) { lod.ForceLOD(0); WarmLods.Add(lod); }
                _views.Register(st.Id, go.transform);
                var visual = new ShipArtVisual(_w, st.Id, damage, juiceShowsWrecksAndHits: true);
                _rig.Spawn(st, cv.ship, visual, go.transform, _juice, parts, cv.hullScale, null, new ShipArtJuiceDamage(go));
            }
            else
            {
                _rig.Spawn(st, cv.ship, null, null, null, parts, cv.hullScale);
                UnityEngine.Object.Destroy(go);
            }
            Ai.AddCapital(st.Id, cv.orders.ToOrders());
            CorvetteId = st.Id;
            _spawned.Add(st.Id);
        }

        // ------------------------------------------------------------------------------------------------ waves

        int EarthFightersAlive()
        {
            int n = 0;
            foreach (var kv in _waveOf) { var c = _w.Combat.Get(kv.Key); if (c != null && c.Alive && kv.Value >= 0) n++; }
            return n;
        }

        void QueueWave(int i)
        {
            var wave = Data.waves[i];
            int k = 0;
            foreach (var g in wave.ships)
                for (int j = 0; j < g.count; j++) _queue.Enqueue((i, g, k++, wave.name));
            Ai.Radio.Say("earth_wave", CorvetteId, _w.Ships.Get(CorvetteId) != null ? null : "EARTH");
            MnLog.Info($"DOGFIGHT wave {i + 1}/{Data.waves.Count} '{wave.name}' ({wave.Count} ships) at t={_w.Time - StartedAt:0.0}s");
            Log.Event(_w.Time, $"wave {i + 1} {wave.name}");
        }

        void SpawnQueued(int cap)
        {
            while (_queue.Count > 0 && EarthFightersAlive() < cap)
            {
                var (wave, g, k, name) = _queue.Dequeue();
                Vector3 anchor = DogfightData.V3(Data.arrivals[wave % Data.arrivals.Count]);
                Vector3 toward = (_w.Ships.Get(FriendlyId)?.Position ?? Player.Position) - anchor;
                var rot = Quaternion.LookRotation(toward.normalized, Vector3.up);
                int rank = (k + 1) / 2; float side = k % 2 == 0 ? 1f : -1f;
                Vector3 pos = anchor + rot * new Vector3(side * rank * 45f, rank * 8f, -rank * 40f);
                var st = SpawnFighter(g.@class, $"{name} {k + 1}", Earth, pos, rot, 0.9f, g.profile ?? "drone", AiRole.Fighter, g);
                _waveOf[st.Id] = wave;
                if (g.goal == "capital" && FriendlyId != GameWorld.NoShip)
                    Ai.PilotOf(st.Id).SetGoal(new AiGoal { Kind = GoalKind.Attack, Target = FriendlyId, Part = -1 });
            }
        }

        void SendBombers()
        {
            _bombersSent = true;
            var b = Data.bombers;
            Vector3 anchor = DogfightData.V3(b.pos);
            var mercy = _w.Ships.Get(FriendlyId);
            Vector3 toward = (mercy != null ? mercy.Position : Player.Position) - anchor;
            var rot = Quaternion.LookRotation(toward.normalized, Vector3.up);
            var bombers = new List<int>();
            for (int k = 0; k < b.bomber.count; k++)
            {
                Vector3 pos = anchor + rot * new Vector3((k - (b.bomber.count - 1) * 0.5f) * 60f, 0f, -Mathf.Abs(k - (b.bomber.count - 1) * 0.5f) * 30f);
                var st = SpawnFighter(b.bomber.@class, $"{b.name} {k + 1}", Earth, pos, rot, 0.9f, b.bomber.profile ?? "drone_bomber", AiRole.Bomber, b.bomber);
                var p = Ai.PilotOf(st.Id);
                if (mercy != null)
                {
                    p.DefaultGoal = new AiGoal { Kind = GoalKind.Bomb, Target = FriendlyId, Part = -1 };
                    p.SetGoal(p.DefaultGoal);
                }
                _waveOf[st.Id] = -1;
                bombers.Add(st.Id);
            }
            for (int k = 0; k < b.escort.count && bombers.Count > 0; k++)
            {
                Vector3 pos = anchor + rot * new Vector3((k % 2 == 0 ? -1f : 1f) * 150f, 60f, -120f);
                var st = SpawnFighter(b.escort.@class, $"{b.escort.name ?? "Escort"} {k + 1}", Earth, pos, rot, 0.9f, b.escort.profile ?? "drone_heavy", AiRole.Fighter, b.escort);
                var p = Ai.PilotOf(st.Id);
                p.DefaultGoal = new AiGoal { Kind = GoalKind.Guard, Target = bombers[k % bombers.Count], Part = -1 };
                p.SetGoal(p.DefaultGoal);
                _waveOf[st.Id] = -1;
            }
            Ai.Radio.Say("earth_bombers", CorvetteId, _w.Ships.Get(CorvetteId) != null ? null : "EARTH", null, true);
            _mercyRadioAt = _w.Time + 3.5f;
            MnLog.Info($"DOGFIGHT bombers '{b.name}' ({b.bomber.count} + {b.escort.count} escort) at t={_w.Time - StartedAt:0.0}s");
            Log.Event(_w.Time, "bombers");
        }

        // ------------------------------------------------------------------------------------------------ the tick

        public void Step(in SimFrame frame)
        {
            float now = frame.Time;
            if (Outcome == State.Fighting)
            {
                int cap = Stress ? 99 : Mathf.RoundToInt(Data.waveCap.At(Level));
                if (Stress && !_stressed && now >= StartedAt + 1.5f)
                {
                    _stressed = true;
                    for (int i = 0; i < Mathf.Min(3, Data.waves.Count); i++) { _wave = i; QueueWave(i); }
                    _nextWaveAt = -1f;
                    _bombersAt = now + 1f;
                }
                if (!Stress && !NoFighters && _nextWaveAt >= 0f && now >= _nextWaveAt && _wave + 1 < Data.waves.Count)
                {
                    _wave++;
                    _nextWaveAt = -1f;
                    QueueWave(_wave);
                    if (_wave == Data.bombers.withWave && !_bombersSent) _bombersAt = now + Data.bombers.delay;
                }
                SpawnQueued(cap);
                // FS2's rule: the next wave when this one is two-thirds destroyed (and all of it has arrived).
                if (!Stress && _wave >= 0 && _nextWaveAt < 0f && _wave + 1 < Data.waves.Count && QueuedFor(_wave) == 0
                    && _waveDead[_wave] >= Mathf.CeilToInt(Data.nextWaveAt.At(Level) * _waveTotal[_wave] - 1e-3f))
                    _nextWaveAt = now + Data.waveDelay.At(Level);
                if (_bombersAt >= 0f && now >= _bombersAt && !_bombersSent) SendBombers();
                if (_mercyRadioAt >= 0f && now >= _mercyRadioAt) { _mercyRadioAt = -1f; Ai.Radio.Say("bombers", FriendlyId, null, null, true); }
                Radio();
                if (Autopilot && AutopilotStripsTurrets && now >= _objectiveAt) { _objectiveAt = now + 1f; StripTurrets(); }
                else if (Autopilot && AutopilotObjectives && now >= _objectiveAt) { _objectiveAt = now + 5f; Objectives(); }
                // A test harness's invulnerable player keeps its parts too (destroyed engines would leave it crawling under the guns).
                if (PlayerInvulnerable && frame.Tick % (5 * Sim.Hz) == 0)
                {
                    var pc = _w.Combat.Get(Player.Id);
                    if (pc != null && (pc.Hull01 < 0.1f || pc.Parts.Exists(p => p.Destroyed))) { _rig.Damage.Reset(Player.Id); pc.Invulnerable = true; }
                }
                Judge(now);
            }
            if (frame.Tick % Sim.Hz == 0) Sample(now);
        }

        void Objectives()
        {
            var ap = Ai.PilotOf(Player.Id);
            var warden = _w.Combat.Get(CorvetteId);
            if (ap == null) return;
            bool fightersNear = false;
            foreach (var c in _w.Combat.All)
                if (c.Alive && c.Ship != null && c.Ship.Team == Earth && !AiDirector.IsBig(c) && Vector3.Distance(c.Ship.Position, Player.Position) < 1500f) { fightersNear = true; break; }
            int part = -1;
            if (warden != null && warden.Alive)
                foreach (var name in Data.corvette.orders.weapons.Keys) { var p = warden.FindPart(name); if (p != null && !p.Destroyed) { part = p.Index; break; } }
            // The beam turrets first, whatever else is about (they are what kills the Mercy); the Warden's hull only between waves.
            if (warden == null || !warden.Alive || (part < 0 && fightersNear))
            {
                if (ap.Goal.Kind != GoalKind.Engage) ap.SetGoal(new AiGoal { Kind = GoalKind.Engage, Target = GameWorld.NoShip, Part = -1 });
                return;
            }
            if (ap.Goal.Kind != GoalKind.Attack || ap.Goal.Target != CorvetteId || ap.Goal.Part != part)
                ap.SetGoal(new AiGoal { Kind = GoalKind.Attack, Target = CorvetteId, Part = part });
        }

        void StripTurrets()
        {
            var ap = Ai.PilotOf(Player.Id);
            var warden = _w.Combat.Get(CorvetteId);
            if (ap == null) return;
            // A decent player shakes off fighters on its tail before it goes back to the turrets.
            int onMe = 0;
            foreach (var c in _w.Combat.All)
                if (c.Alive && c.Ship != null && c.Ship.Team == Earth && !AiDirector.IsBig(c) && c.Ship.Target == Player.Id
                    && Vector3.Distance(c.Ship.Position, Player.Position) < 1200f) onMe++;
            if (warden == null || !warden.Alive || onMe >= 2)
            {
                if (ap.Goal.Kind != GoalKind.Engage) ap.SetGoal(new AiGoal { Kind = GoalKind.Engage, Target = GameWorld.NoShip, Part = -1 });
                return;
            }
            int part = -1;
            foreach (var name in Data.corvette.orders.weapons.Keys) { var p = warden.FindPart(name); if (p != null && !p.Destroyed) { part = p.Index; break; } }
            if (part < 0)
            {
                float best = float.MaxValue;
                foreach (var p in warden.Parts)
                {
                    if (!p.IsTurret || p.Destroyed) continue;
                    float d = (p.WorldPosition(warden.Ship) - Player.Position).sqrMagnitude;
                    if (d < best) { best = d; part = p.Index; }
                }
            }
            if (ap.Goal.Kind != GoalKind.Attack || ap.Goal.Target != CorvetteId || ap.Goal.Part != part)
                ap.SetGoal(new AiGoal { Kind = GoalKind.Attack, Target = CorvetteId, Part = part });
        }

        int QueuedFor(int wave) { int n = 0; foreach (var q in _queue) if (q.wave == wave) n++; return n; }

        void Radio()
        {
            if (!_saidBeamThreat && Ai.Beaming(CorvetteId)) { _saidBeamThreat = true; Ai.Radio.Say("beams_threat", FriendlyId, null, null, true); }
            var mercy = _w.Combat.Get(FriendlyId);
            if (mercy != null && mercy.Alive && !_saidHurt && mercy.Hull01 < 0.5f) { _saidHurt = true; Ai.Radio.Say("capital_hurt", FriendlyId, null, null, true); }
            var warden = _w.Combat.Get(CorvetteId);
            if (warden != null && warden.Alive && !_saidBeams && Data.corvette.orders.weapons.Count > 0)
            {
                bool any = false;
                foreach (var name in Data.corvette.orders.weapons.Keys) { var p = warden.FindPart(name); if (p != null && !p.Destroyed) any = true; }
                if (!any)
                {
                    _saidBeams = true;
                    Ai.Radio.Say("beams_down", FriendlyId, null, null, true);
                    // FS2's beam-free: with the Warden's beams gone the Mercy's own come off their lock.
                    foreach (var cp in Ai.CapitalPilots) if (cp.Ship == FriendlyId && cp.Orders.HoldBeams) { cp.Orders.HoldBeams = false; Log.Event(_w.Time, "mercy beams free"); }
                }
            }
        }

        void Judge(float now)
        {
            var pc = _w.Combat.Get(Player.Id);
            var mercy = _w.Combat.Get(FriendlyId);
            if (pc == null || !pc.Alive) { End(State.Lost, "SHIP LOST", now); return; }
            if (mercy == null || !mercy.Alive) { Ai.Radio.Say("lost_capital", Wingmen.Count > 0 ? Wingmen[0].Ship : Player.Id, null, null, true); End(State.Lost, "SMALL MERCY LOST", now); return; }
            var warden = _w.Combat.Get(CorvetteId);
            bool wardenGone = warden == null || !warden.Alive;
            if (wardenGone && _wave == Data.waves.Count - 1 && _queue.Count == 0 && _bombersSent && EarthAlive() == 0)
            {
                Ai.Radio.Say("won", Wingmen.Find(w => _w.Combat.Get(w.Ship)?.Alive == true)?.Ship ?? Player.Id, null, null, true);
                End(State.Won, "AREA CLEAR", now);
            }
        }

        int EarthAlive()
        {
            int n = 0;
            foreach (var c in _w.Combat.All) if (c.Alive && c.Ship != null && c.Ship.Team == Earth) n++;
            return n;
        }

        void End(State s, string text, float now)
        {
            Outcome = s; OutcomeText = text; EndedAt = now;
            MnLog.Info($"DOGFIGHT loop {Loop} {s}: {text} after {now - StartedAt:0.0}s, wave {_wave + 1}/{Data.waves.Count}, kills by the player {_kills}");
            Log.Event(now, s.ToString().ToLowerInvariant() + " " + text);
            Ended?.Invoke(s);
        }

        // ------------------------------------------------------------------------------------------------ deaths

        void OnDestroyed(GameEvent e)
        {
            if (_waveOf.TryGetValue(e.Entity, out int wave) && wave >= 0 && wave < _waveDead.Length) _waveDead[wave]++;
            if (e.Other == Player?.Id && e.Entity != Player.Id) _kills++;
            var dead = Wingmen.Find(w => w.Ship == e.Entity);
            if (dead != null)
            {
                var other = Wingmen.Find(w => w.Ship != e.Entity && _w.Combat.Get(w.Ship)?.Alive == true);
                Ai.Radio.Say("wingman_lost", other?.Ship ?? Player.Id, null, _w.Ships.Get(e.Entity)?.Callsign ?? "", true);
            }
        }

        void OnReaped(ShipCombat c)
        {
            Ai.Remove(c.Id);
            _juice?.ForgetShip(c.Id);
        }

        // ------------------------------------------------------------------------------------------------ pacing

        void Sample(float now)
        {
            if (Player == null) return;
            int threats = 0, engaging = 0; float nearest = float.MaxValue;
            foreach (var c in _w.Combat.All)
            {
                if (!c.Alive || c.Ship == null || c.Ship.Team != Earth || AiDirector.IsBig(c)) continue;
                threats++;
                if (c.Ship.Target == Player.Id) engaging++;
                nearest = Mathf.Min(nearest, Vector3.Distance(c.Ship.Position, Player.Position));
            }
            float Hull(int id) { var c = _w.Combat.Get(id); return c != null && c.Alive ? c.Hull01 : 0f; }
            Log.Sample(now, Outcome == State.Fighting && _wave >= 0, _wave + 1, threats, engaging, nearest, Hull(Player.Id), Hull(FriendlyId), Hull(CorvetteId));
        }

        /// <summary>
        /// The pacing log (design section 7, measured pacing): once a second, threats alive (Earth's fighters and bombers), how many have the
        /// player as their target, the nearest one's range, and whether it is quiet (none alive, or none within 2.5 km and none on the player).
        /// </summary>
        public sealed class Pacing
        {
            public const float QuietRange = 2500f;
            readonly StringBuilder _csv = new StringBuilder();
            readonly List<string> _events = new List<string>();
            float _t0, _quietSince = -1f;
            public float LongestQuiet { get; private set; }
            public float QuietSeconds { get; private set; }
            public float FightSeconds { get; private set; }
            public int MaxEngaging { get; private set; }
            public int MaxThreats { get; private set; }
            public float SumThreats { get; private set; }
            public int Samples { get; private set; }

            public void Begin(float t0)
            {
                _t0 = t0; _quietSince = -1f; LongestQuiet = 0f; QuietSeconds = 0f; FightSeconds = 0f; MaxEngaging = 0; MaxThreats = 0; SumThreats = 0f; Samples = 0;
                _csv.Clear(); _events.Clear();
                _csv.AppendLine("t,wave,threats,engaging_player,nearest_m,quiet,player_hull,mercy_hull,warden_hull");
            }

            public void Event(float t, string what) => _events.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.0},{1}", t - _t0, what));

            public void Sample(float t, bool fighting, int wave, int threats, int engaging, float nearest, float player, float mercy, float warden)
            {
                bool quiet = threats == 0 || (nearest > QuietRange && engaging == 0);
                _csv.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:0},{1},{2},{3},{4:0},{5},{6:0.00},{7:0.00},{8:0.00}",
                    t - _t0, wave, threats, engaging, nearest < 1e8f ? nearest : -1f, quiet ? 1 : 0, player, mercy, warden));
                if (!fighting) { _quietSince = -1f; return; }
                Samples++; FightSeconds += 1f; SumThreats += threats;
                MaxEngaging = Mathf.Max(MaxEngaging, engaging); MaxThreats = Mathf.Max(MaxThreats, threats);
                if (quiet)
                {
                    QuietSeconds += 1f;
                    if (_quietSince < 0f) _quietSince = t;
                    LongestQuiet = Mathf.Max(LongestQuiet, t - _quietSince + 1f);
                }
                else _quietSince = -1f;
            }

            public string Summary => string.Format(CultureInfo.InvariantCulture,
                "fight {0:0} s, longest quiet {1:0} s, quiet {2:0} s total, threats max {3} avg {4:0.0}, most on the player at once {5}",
                FightSeconds, LongestQuiet, QuietSeconds, MaxThreats, Samples > 0 ? SumThreats / Samples : 0f, MaxEngaging);

            public void Write(string path)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllText(path, _csv.ToString());
                File.WriteAllText(Path.ChangeExtension(path, ".events.csv"), "t,event\n" + string.Join("\n", _events) + "\n");
            }
        }
    }
}
