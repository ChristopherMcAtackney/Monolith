using System;
using System.Collections.Generic;
using Monolith.CombatHud;
using Monolith.Core;
using Monolith.Flight;
using Monolith.Hud;
using Monolith.Input;
using Monolith.Juice;
using Monolith.ShipArt;
using Monolith.Sky;
using Monolith.View;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.Game
{
    /// <summary>
    /// The combat range (<c>-mnScene range</c>, or <c>"scene": "range"</c> in a capture plan): the combat loop to fly and feel. The player's
    /// fighter on the real flight model and pad, target drones flying scripted loops, a capital hull with destructible parts (the real
    /// corvette), and emplacements that shoot back in a simple way. Everything fights through the same systems the game will use:
    /// <see cref="ControlInput"/> in, <see cref="CombatEvents"/> out to the juice, the HUD reading <see cref="ShipCombat"/>. What dies comes
    /// back after a while. No AI: the drones fly patterns and the emplacements turn and fire (<see cref="TurretGunner"/>).
    /// <c>sceneOptions</c> (capture plans): <c>{ "pilot": "scripted", "script": [...] }</c> see <see cref="RangeScript"/>;
    /// <c>"hullScale"</c> overrides the corvette's; <c>"juice": {...}</c> changes juice settings; <c>"noRespawn": true</c>.
    /// </summary>
    public sealed class CombatRange : MonoBehaviour
    {
        public const string SceneName = "range";

        GameWorld _world;
        FlightSystem _flight;
        CombatRig _rig;
        ShipViews _views;
        PlaceholderShips.Materials _mats;
        Transform _root;
        RangeData _data;
        JuiceDirector _juice;
        FlightHud _flightHud;
        CombatHud.CombatHud _hud;
        PlayerPilot _pad;
        RangePilot _pilot;
        ShipState _player;
        PartTurrets _partTurrets;
        bool _paused, _noRespawn, _noEmplacements, _noTurrets;
        float _playerDiedAt = -1f;

        readonly List<Slot> _slots = new List<Slot>();
        readonly Dictionary<int, Slot> _byShip = new Dictionary<int, Slot>();
        int _kills, _deaths, _partsDown;
        // The corvette's finest level of detail is drawn for the first moments so the graphics driver builds its shaders while the scene is
        // still loading, not on the first pass at it (a one-off 0.5 s stall otherwise).
        readonly List<LODGroup> _warmLods = new List<LODGroup>();

        public RangeScript Script { get; private set; }
        public int Kills => _kills;
        public ShipState Player => _player;
        public JuiceDirector Juice => _juice;
        public CombatRig Rig => _rig;

        /// <summary>One thing that respawns: how to make it again, and when.</summary>
        sealed class Slot
        {
            public string Name;
            public float RespawnSeconds;
            public float RespawnAt = -1f;
            public Func<ShipState> Make;
            public int ShipId = GameWorld.NoShip;
        }

        // ------------------------------------------------------------------------------------------------ building

        public static CombatRange Build(GameWorld world, FlightSystem flight, CombatRig rig, Transform parent, string sceneOptions = null)
        {
            var root = new GameObject("CombatRange");
            root.transform.SetParent(parent, false);
            var self = root.AddComponent<CombatRange>();
            self._world = world; self._flight = flight; self._rig = rig; self._root = root.transform;
            self._data = RangeData.Load();

            JObject opts = string.IsNullOrEmpty(sceneOptions) ? null : JObject.Parse(sceneOptions);
            self._noRespawn = opts != null && (bool)(opts["noRespawn"] ?? false);
            self._noEmplacements = opts != null && (bool)(opts["noEmplacements"] ?? false);
            self._noTurrets = opts != null && (bool)(opts["noTurrets"] ?? false);

            var view = CockpitView.Create(world, root.transform);
            SkyRig.Enable(view.Camera, root.transform);
            SkyWarmUp.Attach(view.Camera, root.transform);
            MakeShadowVolume(root.transform);
            self._views = ShipViews.Create(world, root.transform);
            self._mats = PlaceholderShips.MakeMaterials();

            var settings = JuiceSettings.Load();
            if (opts != null && opts["juice"] != null) Newtonsoft.Json.JsonConvert.PopulateObject(opts["juice"].ToString(), settings);
            self._juice = JuiceDirector.Create(world, view.Camera, root.transform, settings);
            self._juice.OwnsTimeScale = true;

            self.SpawnPlayer();
            self._partTurrets = new PartTurrets(world);
            world.Sim.Register(self._partTurrets);
            self.SpawnAll(opts);

            // -mnNoHud (or "hud": false in a capture plan's sceneOptions) takes looks without the HUD, as the juice sandbox does.
            bool hud = !CommandLine.Has("-mnNoHud") && (opts == null || opts["hud"] == null || (bool)opts["hud"]);
            self._flightHud = FlightHud.Create(world, view.Camera, root.transform);
            self._flightHud.ShowTarget = false;
            self._hud = CombatHud.CombatHud.Create(world, view.Camera, root.transform);
            self._flightHud.enabled = hud; self._hud.enabled = hud;

            // The pilot: the pad (or a script, for captures), wrapped so a dead ship flies nowhere.
            bool scripted = opts != null && string.Equals((string)opts["pilot"], "scripted", StringComparison.OrdinalIgnoreCase);
            IPilotSource inner;
            if (scripted)
            {
                self.Script = new RangeScript(world, self, (JArray)(opts["script"] ?? new JArray()));
                world.Sim.Register(self.Script);
                inner = self.Script;
                MnLog.Info($"RANGE scripted: {((JArray)(opts["script"] ?? new JArray())).Count} steps");
            }
            else
            {
                var controls = ControlsConfig.Load(out string source);
                self._pad = new PlayerPilot(world, self._player.Id, controls);
                inner = self._pad;
                MnLog.Info($"RANGE controls from {source}; {PlayerPilot.Devices()}");
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }
            self._pilot = new RangePilot(world, inner);
            world.Pilots.Attach(self._player.Id, self._pilot);
            world.Sim.Register(new RangeDirector(self));
            RangeChecks.Attach(self, root.transform);

            rig.Reaper.Reaped += self.OnReaped;
            int fps = CommandLine.GetInt("-mnFps", 0);
            if (fps > 0 && !CommandLine.Has("-mnCapture")) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = fps; }
            MnLog.Info($"RANGE ready: player {self._player.ClassName}, {self._data.drones.Count} drones, {self._data.emplacements.Count} emplacements, capital {self._data.capital?.ship}");
            return self;
        }

        static void MakeShadowVolume(Transform root)
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "MN_RangeShadows";
            var shadows = profile.Add<HDShadowSettings>(true);
            shadows.maxShadowDistance.Override(6000f);
            shadows.cascadeShadowSplitCount.Override(4);
            var contact = profile.Add<ContactShadows>(true);
            contact.enable.Override(true);
            contact.length.Override(0.6f);
            contact.maxDistance.Override(400f);
            var go = new GameObject("RangeShadowVolume");
            go.transform.SetParent(root, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 5f;
            volume.sharedProfile = profile;
        }

        static Vector3 V3(float[] a) => a != null && a.Length == 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

        // ------------------------------------------------------------------------------------------------ spawning

        void SpawnPlayer()
        {
            var p = _data.player;
            string shipId = CommandLine.Get("-mnShip", p.ship);
            var cls = ShipCatalog.Load(shipId);
            _player = _flight.Spawn(cls, V3(p.pos), Quaternion.Euler(V3(p.rot)), p.speedFraction * cls.TopSpeed, p.throttle, 0, "Player");
            _world.LocalPlayerShip = _player.Id;
            string defId = CombatCatalog.Get(shipId) != null ? shipId : "vanguard_fighter";
            _rig.Spawn(_player, defId);
        }

        void SpawnAll(JObject opts)
        {
            foreach (var d in _data.drones)
            {
                var spec = d;
                _slots.Add(new Slot { Name = spec.callsign, RespawnSeconds = _data.respawnSeconds, Make = () => SpawnDrone(spec) });
            }
            var only = opts != null && opts["emplacements"] is JArray names ? new HashSet<string>(names.ToObject<string[]>()) : null;
            for (int i = 0; i < _data.emplacements.Count && !_noEmplacements; i++)
            {
                var spec = _data.emplacements[i];
                if (only != null && !only.Contains(spec.callsign)) continue;
                uint seed = (uint)(1013904223 + i * 2654435761u);
                _slots.Add(new Slot { Name = spec.callsign, RespawnSeconds = _data.respawnSeconds * 2f, Make = () => SpawnEmplacement(spec, seed) });
            }
            if (_data.capital != null)
            {
                var spec = _data.capital;
                float hullScale = opts != null && opts["hullScale"] != null ? (float)opts["hullScale"] : spec.hullScale;
                _slots.Add(new Slot { Name = spec.callsign, RespawnSeconds = _data.capitalRespawnSeconds, Make = () => SpawnCapital(spec, hullScale) });
            }
            foreach (var s in _slots) Spawn(s);
        }

        void Spawn(Slot s)
        {
            var st = s.Make();
            if (_world.Tick > 2) MnLog.Info($"RANGE respawned {s.Name} at t={_world.Time:0.00}");
            s.ShipId = st != null ? st.Id : GameWorld.NoShip;
            s.RespawnAt = -1f;
            if (st != null) _byShip[st.Id] = s;
        }

        ShipState SpawnDrone(RangeData.DroneSpec d)
        {
            var cls = ShipCatalog.Load(d.@class);
            var st = _flight.Spawn(cls, V3(d.pos), Quaternion.Euler(V3(d.rot)), d.speedFraction * cls.TopSpeed, d.throttle, 1, d.callsign);
            var keys = new List<ScriptedPilot.Key>();
            foreach (var k in d.keys) keys.Add(new ScriptedPilot.Key(k.t, k.ToInput()));
            _world.Pilots.Attach(st.Id, new ScriptedPilot(keys, d.loop));
            var visual = PlaceholderShips.Fighter(d.callsign, cls.Size, _mats, _root);
            _views.Register(st.Id, visual.transform);
            _rig.Spawn(st, null, new PlaceholderVisual(visual.transform), visual.transform, _juice);
            return st;
        }

        ShipState SpawnEmplacement(RangeData.EmplacementSpec e, uint seed)
        {
            var cls = ShipCatalog.Load("range_turret");
            // They face the player's start at first and then follow the player's ship.
            Vector3 pos = V3(e.pos);
            var st = _flight.Spawn(cls, pos, Quaternion.LookRotation((Vector3.zero - pos).normalized, Vector3.up), 0f, 0f, 1, e.callsign);
            var hull = PlaceholderShips.Hull(e.callsign, 16f, 9f, 8f, (int)(seed & 0xFFFF), _mats, _root);
            _views.Register(st.Id, hull.transform);
            _rig.Spawn(st, e.def, new PlaceholderVisual(hull.transform), hull.transform, _juice);
            _world.Pilots.Attach(st.Id, new TurretGunner(_world, st.Id, e.range, e.scatter, e.missileEvery, e.waitScale, seed));
            return st;
        }

        ShipState SpawnCapital(RangeData.CapitalSpec c, float hullScale)
        {
            var cls = ShipCatalog.Load(c.ship);
            var st = _flight.Spawn(cls, V3(c.pos), Quaternion.Euler(V3(c.rot)), 0f, 0f, c.team, c.callsign);
            _world.Pilots.Attach(st.Id, new ScriptedPilot(new[] { new ScriptedPilot.Key(0f, default(ControlInput)) }));
            if (ShipArtCatalog.Exists(c.ship))
            {
                var go = ShipArtCatalog.Instantiate(c.ship, _root);
                var lod = go.GetComponent<LODGroup>();
                if (lod != null && _world.Tick < 5) { lod.ForceLOD(0); _warmLods.Add(lod); }
                _views.Register(st.Id, go.transform);
                var damage = go.GetComponent<ShipDamage>();
                // The juice drives the model's hull scorching and part wrecks (with chunks it owns) on the events combat raises; combat keeps
                // the damaged looks and the accumulated hull damage.
                var visual = new ShipArtVisual(_world, st.Id, damage, juiceShowsWrecksAndHits: true);
                _rig.Spawn(st, c.ship, visual, go.transform, _juice, ShipArtVisual.Parts(damage), hullScale, null, new ShipArtJuiceDamage(go));
                if (!_noTurrets) _partTurrets.Add(st.Id);
            }
            else
            {
                MnLog.Warn($"RANGE: no model for {c.ship}: a placeholder hull with no parts stands in");
                var hull = PlaceholderShips.Hull(c.callsign, 374f, 70f, 60f, 3, _mats, _root);
                _views.Register(st.Id, hull.transform);
                _rig.Spawn(st, c.ship, new PlaceholderVisual(hull.transform), hull.transform, _juice, null, hullScale);
            }
            return st;
        }

        // ------------------------------------------------------------------------------------------------ the session

        /// <summary>A reaped ship's slot is queued to come back.</summary>
        void OnReaped(ShipCombat c)
        {
            _partTurrets.Remove(c.Id);
            _juice.ForgetShip(c.Id);
            if (_byShip.TryGetValue(c.Id, out var slot))
            {
                _byShip.Remove(c.Id);
                slot.ShipId = GameWorld.NoShip;
                if (!_noRespawn) slot.RespawnAt = _world.Time + slot.RespawnSeconds;
                if (c.Killer == _player.Id) _kills++;
            }
        }

        /// <summary>Per tick: bring back what has been dead long enough, and watch the player.</summary>
        void Tick()
        {
            if (_warmLods.Count > 0 && _world.Tick >= 90)
            {
                foreach (var l in _warmLods) if (l != null) l.ForceLOD(-1);
                _warmLods.Clear();
            }
            foreach (var s in _slots)
                if (s.ShipId == GameWorld.NoShip && s.RespawnAt >= 0f && _world.Time >= s.RespawnAt) Spawn(s);

            var pc = _world.Combat.Get(_player.Id);
            if (pc != null && !pc.Alive && _playerDiedAt < 0f)
            {
                _playerDiedAt = _world.Time;
                _deaths++;
                _hud.Banner = "SHIP LOST";
                _hud.BannerSub = _pad != null ? "PRESS Y TO LAUNCH AGAIN" : "RESPAWNING";
            }
            if (_playerDiedAt >= 0f && (_pad == null && _world.Time - _playerDiedAt > 3f && !_noRespawn)) RespawnPlayer();

            if (_world.Tick % 6 == 0) UpdateInfo(pc);
        }

        void UpdateInfo(ShipCombat pc)
        {
            int drones = 0;
            foreach (var c in _world.Combat.All) if (c.Alive && c.Ship.Team == 1) drones++;
            _hud.Info = new[]
            {
                $"RANGE   KILLS {_kills}   LOST {_deaths}   HOSTILES {drones}",
                _pad != null ? "START: PAUSE / CONTROLS" : "SCRIPTED RUN",
            };
        }

        public void RespawnPlayer()
        {
            var p = _data.player;
            var cls = ShipCatalog.Load(_player.ClassId);
            _rig.Damage.Reset(_player.Id);
            _flight.Place(_player.Id, V3(p.pos), Quaternion.Euler(V3(p.rot)), p.speedFraction * cls.TopSpeed, p.throttle);
            _pad?.Attach(_player.Id);
            _player.Target = GameWorld.NoShip;
            _playerDiedAt = -1f;
            _hud.Banner = null; _hud.BannerSub = null;
        }

        /// <summary>Put the player's ship somewhere (the script's place and face steps).</summary>
        public void PlacePlayer(Vector3 pos, Vector3 eulerRot, float speedFraction, float throttle)
        {
            var cls = ShipCatalog.Load(_player.ClassId);
            _flight.Place(_player.Id, pos, Quaternion.Euler(eulerRot), speedFraction * cls.TopSpeed, throttle);
        }

        // ------------------------------------------------------------------------------------------------ the pad, pause and respawn

        void Update()
        {
            if (_pad == null) return;
            _flightHud.Footer = PlayerPilot.HasPad ? null : "NO PAD FOUND - KEYBOARD";
            if (_paused)
            {
                if (_pad.SystemPressed(SystemAction.Pause) || _pad.SystemPressed(SystemAction.Resume)) SetPaused(false);
                else if (_pad.SystemPressed(SystemAction.Respawn)) { RespawnPlayer(); SetPaused(false); }
                else if (_pad.SystemPressed(SystemAction.Quit)) Application.Quit(0);
                return;
            }
            if (_pad.SystemPressed(SystemAction.Pause)) SetPaused(true);
            else if (_playerDiedAt >= 0f && _pad.SystemPressed(SystemAction.Respawn)) RespawnPlayer();
            else if (_playerDiedAt >= 0f && _world.Time - _playerDiedAt > 8f) RespawnPlayer();
        }

        void SetPaused(bool paused)
        {
            _paused = paused;
            _juice.BaseTimeScale = paused ? 0f : 1f;
            _flightHud.Overlay = paused ? PauseText : null;
        }

        static readonly string[] PauseText =
        {
            "PAUSED",
            "START RESUME      Y RESPAWN      B QUIT",
            "RT FAST GUN   LB HEAVY GUN   RB FIRE MISSILE   R3 NEXT MISSILE   B DECOYS",
            "LT TARGET AHEAD   A NEXT HOSTILE   VIEW NEAREST ATTACKER   Y TAP NEXT PART",
            "HOLD Y: D-PAD UP GUNS  LEFT SHIELDS  RIGHT ENGINES  DOWN BALANCED",
            "LOCK-ON MISSILES NEED A STEADY TONE; HEAT-SEEKERS NEED ONLY THE TARGET IN FRONT",
            "L3 AFTERBURNER   X MATCH SPEED   D-PAD THROTTLE PRESETS",
        };

        // ------------------------------------------------------------------------------------------------ the pieces

        /// <summary>Wraps the player's pilot: a lost ship flies nowhere and fires nothing until it is launched again.</summary>
        sealed class RangePilot : IPilotSource
        {
            readonly GameWorld _world;
            readonly IPilotSource _inner;
            public RangePilot(GameWorld world, IPilotSource inner) { _world = world; _inner = inner; }

            public ControlInput Sample(in SimFrame frame)
            {
                var c = _world.Combat.Get(_world.LocalPlayerShip);
                var input = _inner.Sample(frame);
                if (c != null && !c.Alive) return new ControlInput { Throttle = input.Throttle };
                return input;
            }
        }

        /// <summary>Runs the range's per-tick work on the sim clock (so a capture and a real-time run behave alike).</summary>
        sealed class RangeDirector : ISimSystem
        {
            readonly CombatRange _range;
            public RangeDirector(CombatRange range) { _range = range; }
            public int Order => SimOrder.Late + 10;
            public void Step(in SimFrame frame) => _range.Tick();
        }
    }
}
