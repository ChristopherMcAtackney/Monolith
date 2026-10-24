using System;
using System.Collections.Generic;
using System.Globalization;
using Monolith.AI;
using Monolith.Cabin;
using Monolith.HudUi;
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
        PilotHud _hud;
        HudControls _hudControls;
        PlayerPilot _pad;
        GameMissionHost _host;
        MissionRuntime _rt;
        PlayerKeeper _keeper;
        // The cabin (Monolith.Cabin): the pilot as a person in the ship. Built for every played mission unless -mnNoCabin or "cabin": false.
        SkyRig _sky;
        CabinSystem _cabin;
        CabinRig _cabinRig;
        JObject _cabinOpts;
        bool _cabinOn, _cabinPrelaunch;
        MissionFile _file;
        string _error;
        bool _paused, _briefing, _keepLod0;
        ControlsConfig _controls;                 // the player's controls (null when the AI flies); the pause screen flips its invert switches
        string _controlsSource;
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
            self._sky = SkyRig.Enable(view.Camera, root.transform);
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
            self._hud = PilotHud.Create(world, view.Camera, root.transform);
            self._hud.enabled = hud;
            self._hudControls = new HudControls(self._hud, ControlsConfig.Load(out _), () => self._cabin != null);      // the pause screen's switches; the pad's own controls replace these below

            // The cabin is on in every played mission (a pad, or a scripted cabin in a capture plan); the AI-flown runs leave it out unless the plan asks.
            self._cabinOpts = opts?["cabin"] as JObject;
            self._cabinOn = !CommandLine.Has("-mnNoCabin") && !(opts?["cabin"] != null && opts["cabin"].Type == JTokenType.Boolean && !(bool)opts["cabin"]) && (!autopilot || self._cabinOpts != null);
            if (!autopilot && self._file != null) self._controls = ControlsConfig.Load(out self._controlsSource);      // before the mission starts: the cabin's pad input reads its invert switch
            if (self._file != null) self.StartMission();
            if (!autopilot && self._file != null)
            {
                var controls = self._controls;
                string source = self._controlsSource;
                self._hudControls = new HudControls(self._hud, controls, () => self._cabin != null);
                self._pad = new PlayerPilot(world, world.LocalPlayerShip, controls);
                world.Pilots.Attach(world.LocalPlayerShip, self.WrapPilot());
                MnLog.Info($"MISSION controls from {source}; {PlayerPilot.Devices()}");
                if (!CommandLine.Has("-mnCapture")) { Cursor.visible = false; Cursor.lockState = CursorLockMode.Locked; }
            }
            if (Opt("pauseScreen", null)) self._hud.Overlay = self.PauseText();       // a capture of the pause screen
            self._keepLod0 = Opt("lod0", null);
            if (opts?["spectate"] is JObject spec) DogfightSpectator.Attach(world, view.Camera, root.transform, spec);
            // The briefing first (paused), unless the AI flies or this is a capture or a check. A mission that starts in the cabin briefs at the plan table instead.
            if (self._file != null && self._pad != null && !CommandLine.Has("-mnCapture") && !CommandLine.Has("-mnSkipBriefing") && !self._cabinPrelaunch) self.SetBriefing(true);

            int fps = CommandLine.GetInt("-mnFps", 0);
            if (fps > 0 && !CommandLine.Has("-mnCapture")) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = fps; }
            MnLog.Info($"MISSION scene ready: '{self._file?.Name ?? name}' at {self._ai.LevelName}, {(autopilot ? "AI flies the player" : "pad")}, hud {hud}");
            return self;
        }

        void OnDestroy()
        {
            DestroyCabin();
            _jumps?.Dispose();
            _rt?.Dispose();
            _host?.Dispose();
        }

        void StartMission()
        {
            _runs++;
            MakeCabin();
            _rt = new MissionRuntime(_world, _file, _host, _cabin != null ? (ICabin)_cabin : new CabinStub { AutoRepairSeconds = _host.Autopilot ? 8f : -1f });
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
            DestroyCabin();
            _host.Clear();
            StartMission();
            _pad?.Attach(_world.LocalPlayerShip);
            if (_pad != null) _world.Pilots.Attach(_world.LocalPlayerShip, WrapPilot());
            _hud.Banner = null; _hud.BannerSub = null;
            _hud.Overlay = null;
        }

        // ------------------------------------------------------------------------------------------------ the cabin

        IPilotSource WrapPilot() => _cabin != null ? new CabinPilot(_world, _pad, _cabin, id => _pad.Attach(id)) : (IPilotSource)_pad;

        /// <summary>Build the cabin for this run (a new one each time the mission starts: it is the pilot's state).</summary>
        void MakeCabin()
        {
            if (!_cabinOn || _file == null || _cabin != null) return;
            try
            {
                var cfg = CabinConfig.Load(out string source);
                var layout = CabinLayout.Load();
                ICabinInput input = _cabinOpts?["script"] is JArray steps ? new ScriptedCabinInput(steps) : (ICabinInput)new PadCabinInput(cfg, _controls);
                _cabin = new CabinSystem(_world, layout, cfg, input, _host, _file);
                _world.Sim.Register(_cabin);
                _cabinRig = CabinRig.Create(_world, _cabin, _world.MainCamera, transform);
                _cabinRig.SunDirection = () => _sky != null ? _sky.SunDirection : Vector3.up;
                // A mission that starts in the cabin: the pilot is up at the plan table, the sim is held, the briefing is on the table and the rack is open.
                bool table = _file.Cabin.Start == "table" || (_cabinOpts != null && (bool?)_cabinOpts["prelaunch"] == true);
                bool capture = CommandLine.Has("-mnCapture");
                if (table && !CommandLine.Has("-mnSkipBriefing") && (!_host.Autopilot || capture))   // (the pad is made after the mission starts: a played run is one the AI is not flying)
                {
                    _cabin.Prelaunch = true;
                    _cabinPrelaunch = !capture;
                    _cabin.StartAtTable();
                    if (_cabinPrelaunch) { _juice.BaseTimeScale = 0f; _world.Sim.TimeScale = 0f; _cabin.Launched += Launch; }
                }
                MnLog.Info($"CABIN on: config {source}, {(input is ScriptedCabinInput ? "scripted" : "pad")}, {(_cabin.Prelaunch ? "starts at the plan table" : "starts seated")}");
            }
            catch (Exception e)
            {
                MnLog.Error("CABIN could not start (the mission runs without it): " + e);
                DestroyCabin();
            }
        }

        void Launch()
        {
            if (!_cabinPrelaunch) return;
            _cabinPrelaunch = false;
            _cabin.Prelaunch = false;
            _cabin.Launched -= Launch;
            if (!_paused) { _juice.BaseTimeScale = 1f; _world.Sim.TimeScale = 1f; }
            MnLog.Info("CABIN launch");
        }

        void DestroyCabin()
        {
            if (_cabinRig != null) Destroy(_cabinRig.gameObject);
            _cabinRig = null;
            if (_cabin != null) { _world.Sim.Unregister(_cabin); _cabin.Dispose(); }
            _cabin = null;
            _cabinPrelaunch = false;
        }

        // ------------------------------------------------------------------------------------------------ the frame

        void Update()
        {
            if (_host != null && _host.WarmLods.Count > 0 && _world.Tick >= 90 && !_keepLod0)
            {
                foreach (var l in _host.WarmLods) if (l != null) l.ForceLOD(-1);
                _host.WarmLods.Clear();
            }
            if (_file == null) { _hud.Overlay = new[] { "MISSION NOT LOADED", _error ?? "" }; if (Keyboard.current?.f10Key.wasPressedThisFrame == true || Gamepad.current?.buttonEast.wasPressedThisFrame == true) Application.Quit(1); return; }
            UpdateInfo();
            if (_pad == null) return;
            _hud.Footer = PlayerPilot.HasPad ? null : "NO PAD FOUND - KEYBOARD";
            var pad = Gamepad.current; var kb = Keyboard.current;
            _hudControls.Update(_paused || _briefing);
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
                _hudControls.UpdatePaused();
                _hud.Overlay = PauseText();
                if (_pad.SystemPressed(SystemAction.Pause) || _pad.SystemPressed(SystemAction.Resume)) SetPaused(false);
                else if (_pad.SystemPressed(SystemAction.Respawn)) { SetPaused(false); Restart(); }
                else if (_pad.SystemPressed(SystemAction.Quit)) Application.Quit(0);
                return;
            }
            if (_pad.SystemPressed(SystemAction.Pause)) { SetPaused(true); return; }
            if (_rt.Ended)
            {
                // Standing in the cabin, A and B are the context and back buttons (the plan table's pages): sit down to fly again, or use the pause menu.
                bool standing = _cabin != null && _cabin.Board.State != CabinBoard.Phase.Seated;
                bool again = !standing && (pad != null && pad.buttonSouth.wasPressedThisFrame || kb != null && kb.enterKey.wasPressedThisFrame);
                bool quit = !standing && (pad != null && pad.buttonEast.wasPressedThisFrame) || kb != null && kb.f10Key.wasPressedThisFrame;
                if (again) Restart();
                else if (quit) Application.Quit(0);
            }
        }

        void SetBriefing(bool on)
        {
            _briefing = on;
            _juice.BaseTimeScale = on ? 0f : 1f;
            _world.Sim.TimeScale = on ? 0f : 1f;
            _hud.Overlay = on ? BriefingText() : null;
        }

        void SetPaused(bool paused)
        {
            _paused = paused;
            if (_cabin != null) _cabin.Paused = paused;
            _juice.BaseTimeScale = paused || _cabinPrelaunch ? 0f : 1f;
            _hud.Overlay = paused ? PauseText() : null;
        }

        string[] BriefingText()
        {
            var b = _file.Briefing;
            var lines = new List<string> { b.Title ?? _file.Name };
            foreach (var st in b.Stages) lines.Add(st);
            foreach (var q in b.Questions) { lines.Add(""); lines.Add("#" + q.Key); lines.Add(q.Value); }
            lines.Add("#OBJECTIVES");
            foreach (var o in _file.Objectives) if (!o.Hidden) lines.Add($"{o.Kind.ToUpperInvariant()}	{o.Text}");
            lines.Add("");
            lines.Add($"A   ENTER	LAUNCH     ({_ai.LevelName.ToUpperInvariant()})");
            lines.Add("B   F10	QUIT");
            return lines.ToArray();
        }

        string[] PauseText()
        {
            var lines = new List<string>
            {
                "PAUSED",
                "START	RESUME",
                "Y	FLY AGAIN",
                "B	QUIT",
                $"D-PAD LEFT / RIGHT	DIFFICULTY   {_ai.Data.Difficulty.Levels[_nextLevel].ToUpperInvariant()}" + (_nextLevel != _ai.Level ? "   (NEXT RUN)" : ""),
            };
            lines.AddRange(_hudControls != null ? _hudControls.SettingsLines() : new List<string>());
            var board = _world.Mission;
            if (board != null)
            {
                lines.Add("#OBJECTIVES");
                foreach (var o in board.Objectives)
                    if (!o.Hidden) lines.Add($"{o.Kind.ToUpperInvariant()}	{o.Text}{(o.State == MissionBoard.State.Done ? "   DONE" : o.State == MissionBoard.State.Failed ? "   FAILED" : "")}");
            }
            lines.Add("#CONTROLS");
            lines.Add("RT   LB   RB	FAST GUN   HEAVY GUN   FIRE MISSILE");
            lines.Add("R3   B	NEXT MISSILE   DECOYS");
            lines.Add("LT   A   VIEW	TARGET AHEAD   NEXT HOSTILE   NEAREST ATTACKER");
            lines.Add("HOLD Y	QUICK MENU: POWER ON THE D-PAD, WING ORDERS ON A  B  X  LB");
            lines.Add("L3   X	AFTERBURNER   MATCH SPEED");
            lines.Add("HOLD VIEW	STAND UP     TWICE: BATTLE MAP (NOT PAUSED)");
            lines.Add("SOUND	F3 F4 VOICE   F5 F6 MUSIC   F7 F8 EFFECTS   - AND = MASTER   F2 OFF");
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
                    if (!_paused) _hud.Overlay = lines.ToArray();
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
    /// hull or with a part destroyed (unless the cabin is holding it for a hand repair) is made whole again, so destroyed engines do not leave it crawling under the guns (the dogfight did the
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
            // a part the cabin is holding for the pilot's hands stays down until they have knit it
            bool held = _rt.Cabin != null && _rt.Cabin.NeedsRepair(_w.LocalPlayerShip, null);
            if (pc != null && (pc.Hull01 < 0.1f || (!held && pc.Parts.Exists(p => p.Destroyed)))) { _rig.Damage.Reset(_w.LocalPlayerShip); pc.Invulnerable = true; }
        }
    }
}
