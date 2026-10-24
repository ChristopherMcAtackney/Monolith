using System;
using System.Collections.Generic;
using System.Globalization;
using Monolith.AI;
using Monolith.CombatHud;
using Monolith.Core;
using Monolith.Flight;
using Monolith.Hud;
using Monolith.Input;
using Monolith.Juice;
using Monolith.Mission;
using Monolith.Sky;
using Monolith.View;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Monolith.Game
{
    /// <summary>
    /// Any mission, played (<c>-mnMission &lt;name or file&gt;</c>, or <c>-mnScene mission</c>, or <c>"scene": "mission"</c> in a capture plan): the
    /// cockpit, the Sky, the juice and the plain HUD around a <see cref="MissionRuntime"/> running the mission file. The briefing is shown first
    /// (paused; A or Enter launches), directives and radio on the HUD, the debrief at the end with fly-again and quit. Start pauses (controls,
    /// objectives, difficulty for the next run).
    /// sceneOptions: <c>mission</c> (name or path), <c>pilot</c> ("ai": the AI flies your ship), <c>difficulty</c>, <c>invulnerable</c>, <c>hud</c>,
    /// <c>dust</c>, <c>juice</c>, <c>spectate</c> (as the dogfight's), <c>lod0</c>. Flags: <c>-mnDifficulty</c>, <c>-mnAutopilot</c>, <c>-mnInvulnerable</c>,
    /// <c>-mnSkipBriefing</c>, <c>-mnPacingLog dir</c> (writes the timeline when a run ends).
    /// </summary>
    public sealed class MissionScene : MonoBehaviour
    {
        public const string SceneName = "mission";

        GameWorld _world;
        FlightSystem _flight;
        CombatRig _rig;
        AiDirector _ai;
        JuiceDirector _juice;
        Jumps _jumps;
        FlightHud _flightHud;
        CombatHud.CombatHud _hud;
        PlayerPilot _pad;
        GameMissionHost _host;
        MissionRuntime _rt;
        PlayerKeeper _keeper;
        MissionFile _file;
        string _error;
        bool _paused, _briefing, _keepLod0;
        int _nextLevel, _runs;
        string _pacingLog;

        public MissionRuntime Runtime => _rt;

        public static MissionScene Build(GameWorld world, FlightSystem flight, CombatRig rig, Transform parent, string sceneOptions = null)
        {
            var root = new GameObject("Mission");
            root.transform.SetParent(parent, false);
            var self = root.AddComponent<MissionScene>();
            self._world = world; self._flight = flight; self._rig = rig;
            JObject opts = string.IsNullOrEmpty(sceneOptions) ? null : JObject.Parse(sceneOptions);
            bool Opt(string key, string flag) => (opts != null && opts[key] != null && (bool)opts[key]) || (flag != null && CommandLine.Has(flag));

            string name = (string)opts?["mission"] ?? CommandLine.Get("-mnMission", "dogfight");
            try { self._file = MissionLibrary.Load(name); }
            catch (Exception e) { self._error = e.Message; MnLog.Error("MISSION could not load: " + e.Message); }

            var viewSettings = ViewSettings.Load();
            if (opts != null && opts["dust"] != null && !(bool)opts["dust"]) viewSettings.dust.count = 0;
            var view = CockpitView.Create(world, root.transform, viewSettings);
            SkyRig.Enable(view.Camera, root.transform);
            SkyWarmUp.Attach(view.Camera, root.transform);
            var views = ShipViews.Create(world, root.transform);
            var mats = PlaceholderShips.MakeMaterials();

            var settings = JuiceSettings.Load();
            if (opts != null && opts["juice"] != null) Newtonsoft.Json.JsonConvert.PopulateObject(opts["juice"].ToString(), settings);
            self._juice = JuiceDirector.Create(world, view.Camera, root.transform, settings);
            self._juice.OwnsTimeScale = true;
            self._jumps = Jumps.Attach(self._juice);

            var aiData = AiData.Default;
            string level = (string)opts?["difficulty"] ?? CommandLine.Get("-mnDifficulty", self._file?.Difficulty);
            self._ai = new AiDirector(world, aiData, aiData.LevelOf(level));
            self._nextLevel = self._ai.Level;
            world.Sim.Register(self._ai);
            world.Sim.Register(new TurretGunnery(self._ai));

            bool autopilot = string.Equals((string)opts?["pilot"], "ai", StringComparison.OrdinalIgnoreCase) || CommandLine.Has("-mnAutopilot");
            self._host = new GameMissionHost(world, flight, rig, self._ai, root.transform, views, self._juice, mats)
            {
                Autopilot = autopilot,
                PlayerInvulnerable = Opt("invulnerable", "-mnInvulnerable"),
            };
            self._pacingLog = CommandLine.Get("-mnPacingLog");

            bool hud = !CommandLine.Has("-mnNoHud") && (opts == null || opts["hud"] == null || (bool)opts["hud"]);
            self._flightHud = FlightHud.Create(world, view.Camera, root.transform);
            self._flightHud.ShowTarget = false;
            self._hud = CombatHud.CombatHud.Create(world, view.Camera, root.transform);
            self._flightHud.enabled = hud; self._hud.enabled = hud;

            if (self._file != null) self.StartMission();
            if (!autopilot && self._file != null)
            {
                var controls = ControlsConfig.Load(out string source);
                self._pad = new PlayerPilot(world, world.LocalPlayerShip, controls);
                world.Pilots.Attach(world.LocalPlayerShip, new StopWhenDead(world, self._pad));
                MnLog.Info($"MISSION controls from {source}; {PlayerPilot.Devices()}");
                if (!CommandLine.Has("-mnCapture")) { Cursor.visible = false; Cursor.lockState = CursorLockMode.Locked; }
            }
            self._keepLod0 = Opt("lod0", null);
            if (opts?["spectate"] is JObject spec) DogfightSpectator.Attach(world, view.Camera, root.transform, spec);
            // The briefing first (paused), unless the AI flies or this is a capture or a check.
            if (self._file != null && self._pad != null && !CommandLine.Has("-mnCapture") && !CommandLine.Has("-mnSkipBriefing")) self.SetBriefing(true);

            int fps = CommandLine.GetInt("-mnFps", 0);
            if (fps > 0 && !CommandLine.Has("-mnCapture")) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = fps; }
            MnLog.Info($"MISSION scene ready: '{self._file?.Name ?? name}' at {self._ai.LevelName}, {(autopilot ? "AI flies the player" : "pad")}, hud {hud}");
            return self;
        }

        void OnDestroy()
        {
            _jumps?.Dispose();
            _rt?.Dispose();
            _host?.Dispose();
        }

        void StartMission()
        {
            _runs++;
            _rt = new MissionRuntime(_world, _file, _host, new CabinStub { AutoRepairSeconds = _host.Autopilot ? 8f : -1f });
            _world.Sim.Register(_rt);
            _keeper = new PlayerKeeper(_world, _rig, _rt, _host.PlayerInvulnerable);
            _world.Sim.Register(_keeper);
            _rt.OnEnded += OnEnded;
            _rt.Start();
        }

        void OnEnded(MissionRuntime rt)
        {
            MnLog.Info($"PACING run {_runs}: {rt.Log.Summary}");
            if (!string.IsNullOrEmpty(_pacingLog)) rt.Log.Write(_runs > 1 ? _pacingLog + "_" + _runs : _pacingLog);
        }

        void Restart()
        {
            if (_nextLevel != _ai.Level) _ai.SetLevel(_nextLevel);
            _world.Sim.Unregister(_rt); _world.Sim.Unregister(_keeper);
            _rt.Dispose();
            _host.Clear();
            StartMission();
            _pad?.Attach(_world.LocalPlayerShip);
            if (_pad != null) _world.Pilots.Attach(_world.LocalPlayerShip, new StopWhenDead(_world, _pad));
            _hud.Banner = null; _hud.BannerSub = null;
            _flightHud.Overlay = null;
        }

        // ------------------------------------------------------------------------------------------------ the frame

        void Update()
        {
            if (_host != null && _host.WarmLods.Count > 0 && _world.Tick >= 90 && !_keepLod0)
            {
                foreach (var l in _host.WarmLods) if (l != null) l.ForceLOD(-1);
                _host.WarmLods.Clear();
            }
            if (_file == null) { _flightHud.Overlay = new[] { "MISSION NOT LOADED", _error ?? "" }; if (Keyboard.current?.f10Key.wasPressedThisFrame == true || Gamepad.current?.buttonEast.wasPressedThisFrame == true) Application.Quit(1); return; }
            UpdateInfo();
            if (_pad == null) return;
            _flightHud.Footer = PlayerPilot.HasPad ? null : "NO PAD FOUND - KEYBOARD";
            var pad = Gamepad.current; var kb = Keyboard.current;
            if (_briefing)
            {
                if (pad != null && pad.buttonSouth.wasPressedThisFrame || kb != null && kb.enterKey.wasPressedThisFrame) SetBriefing(false);
                else if (pad != null && pad.buttonEast.wasPressedThisFrame || kb != null && kb.f10Key.wasPressedThisFrame) Application.Quit(0);
                return;
            }
            if (_paused)
            {
                if (pad != null && pad.dpad.left.wasPressedThisFrame || kb != null && kb.leftBracketKey.wasPressedThisFrame) _nextLevel = Mathf.Max(0, _nextLevel - 1);
                if (pad != null && pad.dpad.right.wasPressedThisFrame || kb != null && kb.rightBracketKey.wasPressedThisFrame) _nextLevel = Mathf.Min(_ai.Data.Difficulty.Count - 1, _nextLevel + 1);
                _flightHud.Overlay = PauseText();
                if (_pad.SystemPressed(SystemAction.Pause) || _pad.SystemPressed(SystemAction.Resume)) SetPaused(false);
                else if (_pad.SystemPressed(SystemAction.Respawn)) { SetPaused(false); Restart(); }
                else if (_pad.SystemPressed(SystemAction.Quit)) Application.Quit(0);
                return;
            }
            if (_pad.SystemPressed(SystemAction.Pause)) { SetPaused(true); return; }
            if (_rt.Ended)
            {
                bool again = pad != null && pad.buttonSouth.wasPressedThisFrame || kb != null && kb.enterKey.wasPressedThisFrame;
                bool quit = pad != null && pad.buttonEast.wasPressedThisFrame || kb != null && kb.f10Key.wasPressedThisFrame;
                if (again) Restart();
                else if (quit) Application.Quit(0);
            }
        }

        void SetBriefing(bool on)
        {
            _briefing = on;
            _juice.BaseTimeScale = on ? 0f : 1f;
            _world.Sim.TimeScale = on ? 0f : 1f;
            _flightHud.Overlay = on ? BriefingText() : null;
        }

        void SetPaused(bool paused)
        {
            _paused = paused;
            _juice.BaseTimeScale = paused ? 0f : 1f;
            _flightHud.Overlay = paused ? PauseText() : null;
        }

        string[] BriefingText()
        {
            var b = _file.Briefing;
            var lines = new List<string> { (b.Title ?? _file.Name).ToUpperInvariant() };
            foreach (var st in b.Stages) lines.Add(st);
            foreach (var q in b.Questions) { lines.Add(""); lines.Add("> " + q.Key.ToUpperInvariant()); lines.Add(q.Value); }
            lines.Add("");
            foreach (var o in _file.Objectives) if (!o.Hidden) lines.Add($"{o.Kind.ToUpperInvariant()}: {o.Text.ToUpperInvariant()}");
            lines.Add("");
            lines.Add($"A / ENTER  LAUNCH        B / F10  QUIT        {_ai.LevelName.ToUpperInvariant()}");
            return lines.ToArray();
        }

        string[] PauseText()
        {
            var lines = new List<string>
            {
                "PAUSED",
                "START RESUME      Y FLY AGAIN      B QUIT",
                $"D-PAD LEFT/RIGHT  DIFFICULTY {_ai.Data.Difficulty.Levels[_nextLevel].ToUpperInvariant()}" + (_nextLevel != _ai.Level ? "  (NEXT RUN)" : ""),
                "",
            };
            var board = _world.Mission;
            if (board != null)
                foreach (var o in board.Objectives)
                    if (!o.Hidden) lines.Add($"{o.Kind.ToUpperInvariant()}: {o.Text.ToUpperInvariant()}  {(o.State == MissionBoard.State.Done ? "DONE" : o.State == MissionBoard.State.Failed ? "FAILED" : "")}");
            lines.Add("");
            lines.Add("HOLD Y: A ATTACK MY TARGET  B DISABLE MY TARGET  X COVER ME  LB FORM UP");
            lines.Add("HOLD Y: D-PAD UP GUNS  LEFT SHIELDS  RIGHT ENGINES  DOWN BALANCED");
            lines.Add("RT FAST GUN   LB HEAVY GUN   RB FIRE MISSILE   R3 NEXT MISSILE   B DECOYS");
            lines.Add("LT TARGET AHEAD   A NEXT HOSTILE   VIEW NEAREST ATTACKER   Y TAP NEXT PART");
            lines.Add("L3 AFTERBURNER   X MATCH SPEED   D-PAD THROTTLE PRESETS");
            return lines.ToArray();
        }

        void UpdateInfo()
        {
            if (_world.Tick % 6 != 0 || _rt == null) return;
            int hostiles = 0;
            var me = _world.Ships.Get(_world.LocalPlayerShip);
            foreach (var c in _world.Combat.All) if (c.Alive && c.Ship != null && me != null && Teams.Hostile(me.Team, c.Ship.Team) && !MissionRuntime.IsBig(c)) hostiles++;
            float t = Mathf.Max(0f, _rt.Now - _rt.StartTime);
            var hulls = new List<string>();
            foreach (var h in _file.Pacing.Hulls)
            {
                var c = _rt.CombatOf(h);
                string v = !_rt.HasArrived(h) ? "-" : _rt.HasDeparted(h) ? "GONE" : c != null && c.Alive ? Mathf.RoundToInt(c.Hull01 * 100f).ToString(CultureInfo.InvariantCulture) + "%" : "LOST";
                hulls.Add($"{h.ToUpperInvariant()} {v}");
            }
            _hud.Info = new[]
            {
                $"{_file.Name.ToUpperInvariant()}   T+{(int)(t / 60f)}:{(int)(t % 60f):00}   HOSTILES {hostiles}   KILLS {_rt.Kills}   {_ai.LevelName.ToUpperInvariant()}",
                string.Join("   ", hulls),
                _pad != null ? "START: PAUSE / CONTROLS / OBJECTIVES" : "AI FLYING",
            };
            if (_rt.Ended)
            {
                _hud.Banner = _rt.ResultText;
                if (_pad != null)
                {
                    _hud.BannerSub = null;
                    var lines = new List<string> { "" };
                    lines.AddRange(_rt.Debrief());
                    lines.Add("");
                    lines.Add("A / ENTER  FLY AGAIN        B / F10  QUIT");
                    if (!_paused) _flightHud.Overlay = lines.ToArray();
                }
            }
        }

        /// <summary>Wraps the player's pad: a lost ship flies nowhere and fires nothing.</summary>
        sealed class StopWhenDead : IPilotSource
        {
            readonly GameWorld _world;
            readonly IPilotSource _inner;
            public StopWhenDead(GameWorld world, IPilotSource inner) { _world = world; _inner = inner; }
            public ControlInput Sample(in SimFrame frame)
            {
                var c = _world.Combat.Get(_world.LocalPlayerShip);
                var input = _inner.Sample(frame);
                return c != null && !c.Alive ? new ControlInput { Throttle = input.Throttle } : input;
            }
        }
    }

    /// <summary>
    /// A test harness's invulnerable player (soaks, the auto-play check, captures): every 5 s of the fight, a player ship below a tenth of its
    /// hull or with a part destroyed is made whole again, so destroyed engines do not leave it crawling under the guns (the dogfight did the
    /// same). Runs right after the mission runtime.
    /// </summary>
    public sealed class PlayerKeeper : ISimSystem
    {
        readonly GameWorld _w;
        readonly CombatRig _rig;
        readonly MissionRuntime _rt;
        readonly bool _on;
        public PlayerKeeper(GameWorld w, CombatRig rig, MissionRuntime rt, bool on) { _w = w; _rig = rig; _rt = rt; _on = on; }
        public int Order => SimOrder.Mission;

        public void Step(in SimFrame frame)
        {
            if (!_on || _rt.Ended || !_rt.Started || frame.Tick % (5 * Sim.Hz) != 0) return;
            var pc = _w.Combat.Get(_w.LocalPlayerShip);
            if (pc != null && (pc.Hull01 < 0.1f || pc.Parts.Exists(p => p.Destroyed))) { _rig.Damage.Reset(_w.LocalPlayerShip); pc.Invulnerable = true; }
        }
    }
}
