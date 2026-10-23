using System;
using System.Collections.Generic;
using System.IO;
using Monolith.Core;
using Monolith.Flight;
using Monolith.Hud;
using Monolith.Input;
using Monolith.Sky;
using Monolith.View;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.Game
{
    /// <summary>
    /// The flight playtest (design section 12's first deliverable), built from code: the player in our fighter, first
    /// person only, a few target ships flying loops on <see cref="ScriptedPilot"/> through the same flight model, three
    /// huge placeholder hulls, motion dust, a placeholder starfield and the minimal HUD. Layout in
    /// Resources/Flight/playtest.json. Command line: -mnShip &lt;class&gt; (the player's ship), -mnPilot demo (the player's
    /// ship flies the scripted demo; captures use it), -mnControls file, -mnFps n (cap the frame rate to try smoothness).
    /// </summary>
    public sealed class FlightPlaytest : MonoBehaviour
    {
        public const string ResourcePath = "Flight/playtest";
        /// <summary>-mnScene flight, or "scene": "flight" in a capture plan.</summary>
        public const string SceneName = "flight";

        GameWorld _world;
        FlightSystem _flight;
        PlayerPilot _pilot;
        FlightHud _hud;
        PlaytestData _data;
        ShipState _player;
        bool _paused;
        int _knocks;

        /// <param name="sceneOptions">A capture plan's "sceneOptions" JSON, e.g. {"pilot": "demo"} (same as -mnPilot demo).</param>
        public static FlightPlaytest Build(GameWorld world, FlightSystem flight, Transform parent, string sceneOptions = null)
        {
            var data = PlaytestData.Load();
            var root = new GameObject("FlightPlaytest");
            root.transform.SetParent(parent, false);
            var self = root.AddComponent<FlightPlaytest>();
            self._world = world;
            self._flight = flight;
            self._data = data;

            var view = CockpitView.Create(world, root.transform);
            // Sun, sky, ambient fill and exposure belong to the Sky system; this scene only widens the shadow range.
            SkyRig.Enable(view.Camera, root.transform);
            MakeShadowVolume(root.transform, data.lighting);
            var views = ShipViews.Create(world, root.transform);
            var mats = PlaceholderShips.MakeMaterials();

            foreach (var h in data.hulls)
            {
                var hull = PlaceholderShips.Hull(h.name, h.length, h.width, h.height, h.seed, mats, root.transform);
                hull.transform.SetPositionAndRotation(V3(h.pos), Quaternion.Euler(V3(h.rot)));
            }

            foreach (var t in data.targets)
            {
                var cls = ShipCatalog.Load(t.ship);
                var s = flight.Spawn(cls, V3(t.pos), Quaternion.Euler(V3(t.rot)), t.speedFraction * cls.TopSpeed, t.throttle, 1, t.callsign);
                world.Pilots.Attach(s.Id, new ScriptedPilot(Keys(t.keys), t.loop));
                views.Register(s.Id, PlaceholderShips.Fighter(t.callsign, cls.Size, mats, root.transform).transform);
            }

            string shipId = CommandLine.Get("-mnShip", data.player.ship);
            var playerClass = ShipCatalog.Load(shipId);
            self._player = flight.Spawn(playerClass, V3(data.player.pos), Quaternion.Euler(V3(data.player.rot)),
                data.player.speedFraction * playerClass.TopSpeed, data.player.throttle, 0, "Player");
            world.LocalPlayerShip = self._player.Id;

            self._hud = FlightHud.Create(world, view.Camera, root.transform);
            string pilotKind = CommandLine.Get("-mnPilot", "pad");
            if (!string.IsNullOrEmpty(sceneOptions))
            {
                var opts = Newtonsoft.Json.Linq.JObject.Parse(sceneOptions);
                pilotKind = (string)opts["pilot"] ?? pilotKind;
            }
            bool demo = string.Equals(pilotKind, "demo", StringComparison.OrdinalIgnoreCase);
            if (demo)
            {
                var director = new DemoDirector(world, flight, self._player.Id, data.demo);
                world.Sim.Register(director);
                world.Pilots.Attach(self._player.Id, director);
                MnLog.Info($"PLAYTEST demo pilot ({data.demo.keys.Count} keys, loop {data.demo.loop} s)");
            }
            else
            {
                var controls = ControlsConfig.Load(out string source);
                self._pilot = new PlayerPilot(world, self._player.Id, controls);
                world.Pilots.Attach(self._player.Id, self._pilot);
                MnLog.Info($"PLAYTEST controls from {source}; {PlayerPilot.Devices()}");
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }

            int fps = CommandLine.GetInt("-mnFps", 0);
            if (fps > 0 && !CommandLine.Has("-mnCapture"))
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = fps;
            }
            MnLog.Info($"PLAYTEST flight scene: player {playerClass.Name}, {data.targets.Count} targets, {data.hulls.Count} hulls");
            return self;
        }

        void Update()
        {
            if (_pilot == null) return;
            _hud.Footer = PlayerPilot.HasPad ? null : "NO PAD FOUND - KEYBOARD";

            if (_paused)
            {
                if (_pilot.SystemPressed(SystemAction.Pause) || _pilot.SystemPressed(SystemAction.Resume)) SetPaused(false);
                else if (_pilot.SystemPressed(SystemAction.Respawn)) { Respawn(); SetPaused(false); }
                else if (_pilot.SystemPressed(SystemAction.Quit)) Application.Quit(0);
                return;
            }
            if (_pilot.SystemPressed(SystemAction.Pause)) SetPaused(true);
            else if (_pilot.SystemPressed(SystemAction.TestKnock)) TestKnock();
        }

        void SetPaused(bool paused)
        {
            _paused = paused;
            _world.Sim.TimeScale = paused ? 0f : 1f;
            _hud.Overlay = paused ? PauseText : null;
        }

        static readonly string[] PauseText =
        {
            "PAUSED",
            "START / ESC  RESUME      Y / BACKSPACE  RESPAWN      B / F10  QUIT",
            "RIGHT STICK  PITCH + YAW         LEFT STICK  THROTTLE + ROLL",
            "L3  AFTERBURNER (CLICK ON / OFF)         X  MATCH TARGET SPEED",
            "LT  TARGET AHEAD     A  NEXT TARGET     DPAD  100 / 0 / 33 / 66 %",
            "VIEW (BACK)  TEST KNOCK: A BIG HIT LOOSENS THE DAMPING FOR 2 S",
        };

        void Respawn()
        {
            var p = _data.player;
            var cls = ShipCatalog.Load(_player.ClassId);
            _flight.Place(_player.Id, V3(p.pos), Quaternion.Euler(V3(p.rot)), p.speedFraction * cls.TopSpeed, p.throttle);
            _pilot.Attach(_player.Id);
        }

        /// <summary>A debug shove so the loosened damping after big hits can be felt before anything can hit you.</summary>
        void TestKnock()
        {
            var cls = ShipCatalog.Load(_player.ClassId);
            var rot = _player.Rotation;
            Vector3[] dirs = { Vector3.right, Vector3.up + Vector3.left * 0.5f, Vector3.left, Vector3.down + Vector3.right * 0.5f };
            var dir = rot * dirs[_knocks++ % dirs.Length].normalized;
            _world.Knocks.Add(new ShipKnock
            {
                Ship = _player.Id,
                Impulse = dir * (cls.Mass * 20f),          // a 20 m/s shove
                Point = new Vector3(0f, 0f, cls.Size.z * 0.4f),   // near the nose, so it also spins the ship
                Shock = 0f,
            });
        }

        static List<ScriptedPilot.Key> Keys(List<PlaytestData.InputKey> keys)
        {
            var list = new List<ScriptedPilot.Key>();
            foreach (var k in keys) list.Add(new ScriptedPilot.Key(k.t, k.ToInput()));
            return list;
        }

        /// <summary>Capital hulls are kilometres long: shadows out to <c>shadowDistance</c>, contact shadows for small detail.</summary>
        static void MakeShadowVolume(Transform root, PlaytestData.Lighting l)
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "MN_FlightShadows";
            var shadows = profile.Add<HDShadowSettings>(true);
            shadows.maxShadowDistance.Override(l.shadowDistance);
            shadows.cascadeShadowSplitCount.Override(4);
            var contact = profile.Add<ContactShadows>(true);
            contact.enable.Override(true);
            contact.length.Override(0.6f);
            contact.maxDistance.Override(400f);

            var go = new GameObject("FlightShadowVolume");
            go.transform.SetParent(root, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 5f;
            volume.sharedProfile = profile;
        }

        static Vector3 V3(float[] a) => a != null && a.Length == 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

        /// <summary>
        /// Plays the demo timeline for the player's ship (-mnPilot demo): inputs as a pilot source, and 'place' keys as a
        /// sim step just before flight, so a capture can cut between set-ups. Loops with the timeline.
        /// </summary>
        sealed class DemoDirector : IPilotSource, ISimSystem
        {
            readonly GameWorld _world;
            readonly FlightSystem _flight;
            readonly int _ship;
            readonly PlaytestData.Demo _demo;
            readonly int _loopTicks;

            public DemoDirector(GameWorld world, FlightSystem flight, int ship, PlaytestData.Demo demo)
            {
                _world = world; _flight = flight; _ship = ship; _demo = demo;
                _demo.keys.Sort((a, b) => a.t.CompareTo(b.t));
                _loopTicks = demo.loop > 0f ? Mathf.RoundToInt(demo.loop * Sim.Hz) : 0;
            }

            public int Order => SimOrder.Pilots + 50;

            int LocalTick(int tick) => _loopTicks > 0 ? tick % _loopTicks : tick;

            public void Step(in SimFrame frame)
            {
                int local = LocalTick(frame.Tick);
                foreach (var k in _demo.keys)
                {
                    if (k.place == null || Mathf.RoundToInt(k.t * Sim.Hz) != local) continue;
                    var cls = ShipCatalog.Load(_world.Ships.Get(_ship).ClassId);
                    _flight.Place(_ship, V3(k.place.pos), Quaternion.Euler(V3(k.place.rot)), k.place.speedFraction * cls.TopSpeed, k.throttle ?? 0f);
                }
            }

            public ControlInput Sample(in SimFrame frame)
            {
                float t = LocalTick(frame.Tick) * Sim.Dt;
                var input = default(ControlInput);
                foreach (var k in _demo.keys)
                {
                    if (k.t > t + 1e-5f) break;
                    input = k.ToInput(input);
                    // Buttons other than the afterburner are taps: held for the key's first tick only.
                    if (Mathf.RoundToInt(k.t * Sim.Hz) != LocalTick(frame.Tick))
                        input.Buttons &= ControlButtons.Afterburner;
                }
                return input;
            }
        }
    }

    /// <summary>Resources/Flight/playtest.json (format "monolith-flight-playtest/1").</summary>
    public sealed class PlaytestData
    {
        public const string Format = "monolith-flight-playtest/1";

        public string format, about;
        public Player player = new Player();
        public Lighting lighting = new Lighting();
        public List<HullSpec> hulls = new List<HullSpec>();
        public List<Target> targets = new List<Target>();
        public Demo demo = new Demo();

        public sealed class Player
        {
            public string ship = "vanguard_fighter";
            public float[] pos, rot;
            public float speedFraction, throttle;
        }

        public sealed class Lighting
        {
            public string about;
            public float shadowDistance = 6000f;
        }

        public sealed class HullSpec
        {
            public string name;
            public float[] pos, rot;
            public float length, width, height;
            public int seed;
        }

        public sealed class Target
        {
            public string ship, callsign;
            public float[] pos, rot;
            public float speedFraction, throttle, loop;
            public List<InputKey> keys = new List<InputKey>();
        }

        public sealed class Place
        {
            public float[] pos, rot;
            public float speedFraction;
        }

        public sealed class InputKey
        {
            public float t;
            public float? pitch, yaw, roll, throttle;
            public bool? afterburner, targetNext, matchSpeed;
            public Place place;

            /// <summary>This key's inputs over <paramref name="from"/> (fields it does not name keep their value).</summary>
            public ControlInput ToInput(ControlInput from = default)
            {
                var c = from;
                if (pitch.HasValue) c.Pitch = pitch.Value;
                if (yaw.HasValue) c.Yaw = yaw.Value;
                if (roll.HasValue) c.Roll = roll.Value;
                if (throttle.HasValue) c.Throttle = throttle.Value;
                if (afterburner.HasValue) c.Buttons = afterburner.Value ? c.Buttons | ControlButtons.Afterburner : c.Buttons & ~ControlButtons.Afterburner;
                if (targetNext == true) c.Buttons |= ControlButtons.TargetNext;
                if (matchSpeed == true) c.Buttons |= ControlButtons.MatchSpeed;
                return c;
            }
        }

        public sealed class Demo
        {
            public string about;
            public float loop;
            public List<InputKey> keys = new List<InputKey>();
        }

        public static PlaytestData Load()
        {
            var asset = Resources.Load<TextAsset>(FlightPlaytest.ResourcePath) ?? throw new FileNotFoundException($"Resources/{FlightPlaytest.ResourcePath}.json is missing");
            PlaytestData d;
            try
            {
                d = JsonConvert.DeserializeObject<PlaytestData>(asset.text, new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
            }
            catch (Exception e) { throw new InvalidDataException($"Resources/{FlightPlaytest.ResourcePath}.json: {e.Message}"); }
            if (d == null || d.format != Format) throw new InvalidDataException($"Resources/{FlightPlaytest.ResourcePath}.json: format '{d?.format}', expected '{Format}'");
            return d;
        }
    }
}
