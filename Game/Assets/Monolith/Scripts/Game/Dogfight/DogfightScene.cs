using System;
using System.Collections.Generic;
using System.Globalization;
using Monolith.AI;
using Monolith.HudUi;
using Monolith.Core;
using Monolith.Flight;
using Monolith.Hud;
using Monolith.Input;
using Monolith.Juice;
using Monolith.Sky;
using Monolith.View;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.Game
{
    /// <summary>
    /// The dogfight (<c>-mnScene dogfight</c>, or <c>"scene": "dogfight"</c> in a capture plan): the player and two wingmen against Earth's
    /// waves near the Warden and our cruiser, the Small Mercy, which a bomber wing comes for (<see cref="DogfightBattle"/> has the rules). A loop
    /// to playtest the fighting: it ends in a plain win or loss, with fly-again and quit. Start pauses (and lists the controls; the D-pad
    /// changes the difficulty for the next run).
    /// sceneOptions: <c>pilot</c> ("ai": the AI flies your ship, for captures), <c>difficulty</c> ("Medium" or 0..4), <c>invulnerable</c>,
    /// <c>hud</c> (false for look shots), <c>dust</c> (false: no cockpit motion dust, for look shots), <c>stress</c> (the first three waves and the bombers at once, for perf), <c>spectate</c>
    /// (<c>{ "ship": "Censer 1", "offset": [x, y, z], "look": "target" | "ship" | callsign, "world": false }</c>: the capture camera rides a ship,
    /// never the player's), <c>juice</c> (settings). Flags: <c>-mnDifficulty</c>, <c>-mnAutopilot</c>, <c>-mnInvulnerable</c>, <c>-mnPacingLog file.csv</c>.
    /// </summary>
    public sealed class DogfightScene : MonoBehaviour
    {
        public const string SceneName = "dogfight";

        GameWorld _world;
        DogfightBattle _battle;
        AiDirector _ai;
        JuiceDirector _juice;
        PilotHud _hud;
        HudControls _hudControls;
        ControlsConfig _controls;
        PlayerPilot _pad;
        bool _paused;
        int _nextLevel;
        string _pacingLog;

        public DogfightBattle Battle => _battle;
        public AiDirector Ai => _ai;

        public static DogfightScene Build(GameWorld world, FlightSystem flight, CombatRig rig, Transform parent, string sceneOptions = null)
        {
            var root = new GameObject("Dogfight");
            root.transform.SetParent(parent, false);
            var self = root.AddComponent<DogfightScene>();
            self._world = world;
            JObject opts = string.IsNullOrEmpty(sceneOptions) ? null : JObject.Parse(sceneOptions);
            bool Opt(string key, string flag) => (opts != null && opts[key] != null && (bool)opts[key]) || (flag != null && CommandLine.Has(flag));

            // Look shots can leave out the cockpit's motion dust ("dust": false), which only makes sense from the cockpit.
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

            var data = DogfightData.Load();
            var aiData = AiData.Default;
            string level = (string)opts?["difficulty"] ?? CommandLine.Get("-mnDifficulty", data.difficulty);
            self._ai = new AiDirector(world, aiData, aiData.LevelOf(level));
            self._nextLevel = self._ai.Level;
            world.Sim.Register(self._ai);
            world.Sim.Register(new TurretGunnery(self._ai));

            self._battle = new DogfightBattle(world, flight, rig, self._ai, root.transform, views, self._juice, mats, data)
            {
                Autopilot = string.Equals((string)opts?["pilot"], "ai", StringComparison.OrdinalIgnoreCase) || CommandLine.Has("-mnAutopilot"),
                PlayerInvulnerable = Opt("invulnerable", "-mnInvulnerable"),
                Stress = Opt("stress", null),
            };
            world.Sim.Register(self._battle);
            self._battle.Start();
            self._pacingLog = CommandLine.Get("-mnPacingLog");
            self._battle.Ended += s => self.OnEnded(s);

            bool hud = !CommandLine.Has("-mnNoHud") && (opts == null || opts["hud"] == null || (bool)opts["hud"]);
            self._hud = PilotHud.Create(world, view.Camera, root.transform);
            self._hud.enabled = hud;
            self._hudControls = new HudControls(self._hud, ControlsConfig.Load(out _));      // the pause screen's switches; the pad's own controls replace these below

            if (!self._battle.Autopilot)
            {
                var controls = ControlsConfig.Load(out string source);
                self._controls = controls;
                self._hudControls = new HudControls(self._hud, controls);
                self._pad = new PlayerPilot(world, self._battle.Player.Id, controls);
                world.Pilots.Attach(self._battle.Player.Id, new StopWhenDead(world, self._pad));
                MnLog.Info($"DOGFIGHT controls from {source}; {PlayerPilot.Devices()}");
                if (!CommandLine.Has("-mnCapture")) { Cursor.visible = false; Cursor.lockState = CursorLockMode.Locked; }
            }
            self._keepLod0 = Opt("lod0", null);
            if (opts?["spectate"] is JObject spec) DogfightSpectator.Attach(world, view.Camera, root.transform, spec);

            if (Opt("pauseScreen", null)) self._hud.Overlay = self.PauseText();       // a capture of the pause screen
            int fps = CommandLine.GetInt("-mnFps", 0);
            if (fps > 0 && !CommandLine.Has("-mnCapture")) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = fps; }
            MnLog.Info($"DOGFIGHT ready at {self._ai.LevelName}: {(self._battle.Autopilot ? "AI flies the player" : "pad")}, hud {hud}");
            return self;
        }

        static void MakeShadowVolume(Transform root)
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "MN_DogfightShadows";
            var shadows = profile.Add<HDShadowSettings>(true);
            shadows.maxShadowDistance.Override(6000f);
            shadows.cascadeShadowSplitCount.Override(4);
            var contact = profile.Add<ContactShadows>(true);
            contact.enable.Override(true);
            contact.length.Override(0.6f);
            contact.maxDistance.Override(400f);
            var go = new GameObject("DogfightShadowVolume");
            go.transform.SetParent(root, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 5f;
            volume.sharedProfile = profile;
        }

        void OnEnded(DogfightBattle.State s)
        {
            MnLog.Info($"PACING loop {_battle.Loop}: {_battle.Log.Summary}");
            if (!string.IsNullOrEmpty(_pacingLog)) _battle.Log.Write(_pacingLog);
        }

        // ------------------------------------------------------------------------------------------------ the frame

        bool _keepLod0;

        void Update()
        {
            if (_battle.WarmLods.Count > 0 && _world.Tick >= 90 && !_keepLod0)
            {
                foreach (var l in _battle.WarmLods) if (l != null) l.ForceLOD(-1);
                _battle.WarmLods.Clear();
            }
            UpdateInfo();
            if (_pad == null) return;
            _hud.Footer = PlayerPilot.HasPad ? null : "NO PAD FOUND - KEYBOARD";
            var pad = Gamepad.current; var kb = Keyboard.current;
            _hudControls.Update(_paused);
            if (_paused)
            {
                _hudControls.UpdatePaused();
                if (pad != null && pad.dpad.left.wasPressedThisFrame || kb != null && kb.leftBracketKey.wasPressedThisFrame) _nextLevel = Mathf.Max(0, _nextLevel - 1);
                if (pad != null && pad.dpad.right.wasPressedThisFrame || kb != null && kb.rightBracketKey.wasPressedThisFrame) _nextLevel = Mathf.Min(_ai.Data.Difficulty.Count - 1, _nextLevel + 1);
                _hud.Overlay = PauseText();
                if (_pad.SystemPressed(SystemAction.Pause) || _pad.SystemPressed(SystemAction.Resume)) SetPaused(false);
                else if (_pad.SystemPressed(SystemAction.Respawn)) { SetPaused(false); Restart(); }
                else if (_pad.SystemPressed(SystemAction.Quit)) Application.Quit(0);
                return;
            }
            if (_pad.SystemPressed(SystemAction.Pause)) { SetPaused(true); return; }
            if (_battle.Outcome != DogfightBattle.State.Fighting)
            {
                bool again = pad != null && pad.buttonSouth.wasPressedThisFrame || kb != null && kb.enterKey.wasPressedThisFrame;
                bool quit = pad != null && pad.buttonEast.wasPressedThisFrame || kb != null && kb.f10Key.wasPressedThisFrame;
                if (again) Restart();
                else if (quit) Application.Quit(0);
            }
        }

        void Restart()
        {
            if (_nextLevel != _ai.Level) _ai.SetLevel(_nextLevel);
            _battle.Restart();
            _pad?.Attach(_battle.Player.Id);
            _hud.Banner = null; _hud.BannerSub = null;
        }

        void SetPaused(bool paused)
        {
            _paused = paused;
            _juice.BaseTimeScale = paused ? 0f : 1f;
            _hud.Overlay = paused ? PauseText() : null;
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
            lines.AddRange(_hudControls.SettingsLines());
            lines.Add("");
            lines.Add("Stop the bombers. Keep the Small Mercy alive. Take the Warden apart.");
            lines.Add("#CONTROLS");
            lines.Add("RT   LB   RB	FAST GUN   HEAVY GUN   FIRE MISSILE");
            lines.Add("R3   B	NEXT MISSILE   DECOYS");
            lines.Add("LT   A   VIEW	TARGET AHEAD   NEXT HOSTILE   NEAREST ATTACKER");
            lines.Add("Y (TAP)	NEXT PART OF THE TARGET");
            lines.Add("HOLD Y	QUICK MENU: POWER ON THE D-PAD, WING ORDERS ON A  B  X  LB");
            lines.Add("L3   X	AFTERBURNER   MATCH SPEED");
            lines.Add("VIEW TWICE   B	BATTLE MAP (NOT PAUSED)");
            return lines.ToArray();
        }

        void UpdateInfo()
        {
            if (_world.Tick % 6 != 0) return;
            float Hull(int id) { var c = _world.Combat.Get(id); return c != null && c.Alive ? c.Hull01 : 0f; }
            int hostiles = 0;
            foreach (var c in _world.Combat.All) if (c.Alive && c.Ship != null && c.Ship.Team == DogfightBattle.Earth && !AiDirector.IsBig(c)) hostiles++;
            string pct(float v) => Mathf.RoundToInt(v * 100f).ToString(CultureInfo.InvariantCulture) + "%";
            _hud.Info = new[]
            {
                $"WAVE {Mathf.Max(0, _battle.WaveIndex + 1)}/{_battle.WaveCount}   HOSTILES {hostiles}   KILLS {_battle.Kills}   {_ai.LevelName.ToUpperInvariant()}",
                $"MERCY {pct(Hull(_battle.FriendlyId))}   WARDEN {pct(Hull(_battle.CorvetteId))}",
                _pad != null ? "START: PAUSE / CONTROLS" : "AI FLYING",
            };
            if (_battle.Outcome != DogfightBattle.State.Fighting)
            {
                _hud.Banner = _battle.OutcomeText;
                _hud.BannerSub = _pad != null ? "A / ENTER  FLY AGAIN        B / F10  QUIT" : null;
            }
        }

        /// <summary>Wraps the player's pad: a lost ship flies nowhere and fires nothing until it is launched again.</summary>
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
    /// A capture-only camera that rides another ship (never the player's: design section 8 forbids an outside view of your own ship) for the
    /// look shots: it sits at an offset from the ship and looks at the ship, its target, or another named ship. Runs after the cockpit view.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public sealed class DogfightSpectator : MonoBehaviour
    {
        GameWorld _world;
        Camera _cam;
        string _ship, _look;
        Vector3 _offset;
        bool _worldOffset;

        public static void Attach(GameWorld world, Camera cam, Transform parent, JObject spec)
        {
            var go = new GameObject("DogfightSpectator");
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<DogfightSpectator>();
            s._world = world; s._cam = cam;
            s._ship = (string)spec["ship"];
            s._look = (string)spec["look"] ?? "target";
            var o = spec["offset"] as JArray;
            s._offset = o != null && o.Count == 3 ? new Vector3((float)o[0], (float)o[1], (float)o[2]) : new Vector3(0f, 12f, -60f);
            s._worldOffset = (bool?)spec["world"] ?? false;
        }

        ShipState Find(string callsign)
        {
            foreach (var s in _world.Ships.All) if (string.Equals(s.Callsign, callsign, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        void LateUpdate()
        {
            var ship = Find(_ship);
            if (ship == null || ship.Id == _world.LocalPlayerShip || _cam == null) return;
            float a = _world.Alpha;
            Vector3 p = ship.RenderPosition(a);
            var r = ship.RenderRotation(a);
            Vector3 eye = p + (_worldOffset ? _offset : r * _offset);
            Vector3 at = p + r * Vector3.forward * 60f;
            if (_look == "ship") at = p;
            else if (_look == "target") { var t = _world.Ships.Get(ship.Target); if (t != null) at = t.RenderPosition(a); }
            else { var t = Find(_look); if (t != null) at = t.RenderPosition(a); }
            Vector3 d = at - eye;
            _cam.transform.SetPositionAndRotation(eye, d.sqrMagnitude > 1f ? Quaternion.LookRotation(d, r * Vector3.up) : r);
        }
    }
}
