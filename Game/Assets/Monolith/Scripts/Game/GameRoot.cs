using System;
using System.Collections;
using System.Collections.Generic;
using Monolith.Audio;
using Monolith.Capture;
using Monolith.Core;
using Monolith.Flight;
using Monolith.Loading;
using Monolith.Start;
using Monolith.Targeting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Monolith.Game
{
    /// <summary>
    /// The composition root: the one place that creates the long-lived pieces and wires systems to the clock.
    /// A new system is a new folder and assembly; add it to Monolith.Game.asmdef, create it in <see cref="Build"/> and
    /// register its <see cref="ISimSystem"/> with <see cref="Sim"/>. Systems never reference each other: they share
    /// <see cref="GameWorld"/> and <see cref="GameEvents"/>.
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        public GameWorld World { get; private set; }
        public Sim Sim => World.Sim;

        IDisposable _eventLog;
        // what a scenario switch in one run puts back (see TearDownPlay)
        HashSet<int> _baselineRoots;
        int _baseVSync;
        int _baseFrameRate = -1;

        public void Build()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 1;
            _baseVSync = 1;
            _baseFrameRate = Application.targetFrameRate;

            // Flight-trace mode: replay the feel scenarios through the flight model, write traces, quit. No world needed.
            if (CommandLine.Has(FlightScenarioRunner.Flag))
            {
                int code = FlightScenarioRunner.RunFromCommandLine();
                Application.Quit(code);
                return;
            }

            // Combat self-test: the real sim, flight model, weapons, damage and data, headless; log lines say what passed. Then quit.
            if (CombatSelfTest.Requested)
            {
                bool ok = CombatSelfTest.Run();
                Application.Quit(ok ? 0 : 1);
                return;
            }

            // AI checks, the AI-against-AI arena and the dogfight soak: headless, on their own worlds; log lines say what happened. Then quit.
            if (AiSelfTest.Requested) { bool ok = AiSelfTest.Run(); Application.Quit(ok ? 0 : 1); return; }
            if (AiArena.Requested) { Application.Quit(AiArena.RunFromCommandLine()); return; }
            if (DogfightSoak.Requested) { Application.Quit(DogfightSoak.RunFromCommandLine()); return; }
            // Mission checks: the self-test, a mission auto-played by the AI against its expected event log, the dogfight's port against the original.
            if (MissionSelfTest.Requested) { bool ok = MissionSelfTest.Run(); Application.Quit(ok ? 0 : 1); return; }
            if (MissionAutoPlay.Requested) { Application.Quit(MissionAutoPlay.RunFromCommandLine()); return; }
            if (DogfightPortCheck.Requested) { Application.Quit(DogfightPortCheck.RunFromCommandLine()); return; }
            // The cabin's checks (the pilot standing, walking, the breaker, scans, the hand repair, the rack; the cabin mission played through).
            if (CabinSelfTest.Requested) { bool ok = CabinSelfTest.Run(); Application.Quit(ok ? 0 : 1); return; }
            // The HUD's checks (type, data interface, layout at every size, geometry, pause screens, cost): headless.
            if (HudSelfTest.Requested) { bool ok = HudSelfTest.Run(); Application.Quit(ok ? 0 : 1); return; }
            // The loading screen's checks (the splash's fit, the bar in every style, the honest meter, the held clock, the preload manifest): headless.
            if (LoadingSelfTest.Requested) { bool ok = LoadingSelfTest.Run(); Application.Quit(ok ? 0 : 1); return; }
            // Audio checks: the score, the effects, the radio and the mix, through the real audio system rendered offline. They need frames, so they run as a coroutine and quit when done.
            if (AudioSelfTest.Requested) { AudioSelfTest.Start(this); return; }

            // A played start (not a check or a capture) opens behind the loading screen, which builds the same world in steps and warms it up before the player sees it.
            StartupProbe.Mark("boot scene loaded, building");
            // A plain start (no mission, scene, capture or check on the command line) goes to the start screen: scenario and difficulty, then the scenario behind the loading screen.
            if (StartScreenSelfTest.Requested) StartScreenSelfTest.Start(this);
            if (StartFlow.Wanted) { StartCoroutine(StartFlow.Run(this)); return; }
            if (LoadingFlow.Wanted) { StartCoroutine(LoadingFlow.Run(this)); return; }
            BuildPlay();
            StartupProbe.FlyableAfterFrames(2);
        }

        /// <summary>Everything a played start builds: the clock, the world, the systems and the scene. The loading screen calls it between its own steps; without the loading screen it is the whole start.</summary>
        public void BuildPlay()
        {
            StartupProbe.Mark("world build begins");
            _baselineRoots = RootIds();
            if (StartupProbe.Enabled && CommandLine.Has("-mnStartupDrive")) StartupDrive.Attach();
            GameEvents.Reset();
            World = new GameWorld(new Sim());
            GameWorld.SetCurrent(World);
            Sim.Register(World.Pilots);
            gameObject.AddComponent<SimDriver>().Sim = Sim;

            AudioRig.Install(World, transform);
            if (CommandLine.Has("-mnLogEvents")) _eventLog = GameEvents.SubscribeAll(e => MnLog.Info("event " + e));

            // Flight and targeting run in every scene; the scene decides whether there are any ships.
            var flight = new FlightSystem(World);
            Sim.Register(new TargetingSystem(World));
            Sim.Register(flight);
            // The combat systems (weapons, collisions, damage, aim) run in every scene; a scene gives them ships with CombatRig.Spawn.
            var combat = CombatRig.Create(World, flight);

            // A capture plan may ask for a particular test scene ("scene": "sky", "flight"); else -mnScene; else the smoke world.
            string scene = null, sceneOptions = null;
            if (CommandLine.Has("-mnCapture")) CapturePlan.PeekScene(CommandLine.Get("-mnCapture"), out scene, out sceneOptions);
            scene ??= CommandLine.Get("-mnScene");
            if (scene == null && CommandLine.Has("-mnMission")) scene = MissionScene.SceneName;
            if (string.Equals(scene, FlightPlaytest.SceneName, StringComparison.OrdinalIgnoreCase)) FlightPlaytest.Build(World, flight, transform, sceneOptions);
            else if (string.Equals(scene, JuiceSandbox.SceneName, StringComparison.OrdinalIgnoreCase)) JuiceSandbox.Build(World, flight, transform, sceneOptions);
            else if (string.Equals(scene, CombatRange.SceneName, StringComparison.OrdinalIgnoreCase)) CombatRange.Build(World, flight, combat, transform, sceneOptions);
            else if (string.Equals(scene, DogfightScene.SceneName, StringComparison.OrdinalIgnoreCase)) DogfightScene.Build(World, flight, combat, transform, sceneOptions);
            else if (string.Equals(scene, MissionScene.SceneName, StringComparison.OrdinalIgnoreCase)) MissionScene.Build(World, flight, combat, transform, sceneOptions);
            else if (!ShipShowcase.TryBuild(World, transform, scene, sceneOptions) && !SkyTestWorld.TryBuild(World, transform, scene, sceneOptions) && !LoadingTestWorld.TryBuild(World, transform, scene, sceneOptions) && !StartTestWorld.TryBuild(World, transform, scene, sceneOptions)) SmokeWorld.Build(World, transform);

            StartupProbe.Mark("scene built");
            AudioRig.EnsureSystem(World, transform);       // scenes without a juice director get their sound here

            if (CommandLine.Has("-mnCapture"))
            {
                // Capture mode from the very first frame: one tick per frame at fixed game time, so which tick a shot is
                // taken on does not depend on how quickly this machine booted.
                Sim.OneTickPerFrame = true;
                Time.captureDeltaTime = 1f / Sim.Hz;
                gameObject.AddComponent<CaptureRunner>();
            }

            GameEvents.Raise(EventIds.BootComplete);
            StartupProbe.Mark("world built");
            var pipeline = GraphicsSettings.currentRenderPipeline;
            MnLog.Info($"BOOT ok: Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}), " +
                       $"pipeline {(pipeline != null ? pipeline.GetType().Name : "none")}, {Sim.SystemCount} sim systems at {Sim.Hz} Hz");
        }

        // ------------------------------------------------------------------------------------------------ taking a scenario down (the start screen's return)

        /// <summary>The instance ids of every top-level object in every loaded scene, including the ones that survive a scene change.</summary>
        public static HashSet<int> RootIds()
        {
            var ids = new HashSet<int>();
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go.scene.IsValid() && go.transform.parent == null) ids.Add(go.GetInstanceID());
            return ids;
        }

        /// <summary>
        /// Everything <see cref="BuildPlay"/> made, gone: the scenario's objects (everything under this object, and every top-level object made since the world began, but not <paramref name="keep"/>, the
        /// screen already up), the world, the clock, every event listener, the audio system (its decoded library kept for the next scenario), the global state a scene sets (the cursor, the frame rate, the
        /// clock's speed), and then the assets nothing uses any more. The caller has put a screen up first, so no frame is drawn without a camera.
        /// </summary>
        public IEnumerator TearDownPlay(GameObject keep = null)
        {
            if (World != null) World.Sim.Held = true;
            var audio = AudioSystem.Active;
            if (audio != null && audio.Lib != null && audio.Lib.Ready) AudioRig.Preloaded = audio.Lib;      // the decoded sound outlives the audio system: the next one takes it over
            foreach (var d in GetComponents<SimDriver>()) Destroy(d);
            foreach (var d in GetComponents<StartupDrive>()) Destroy(d);
            _eventLog?.Dispose(); _eventLog = null;
            var doomed = new List<GameObject>();
            foreach (Transform t in transform) doomed.Add(t.gameObject);
            if (_baselineRoots != null)
                foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                    if (go.scene.IsValid() && go.transform.parent == null && go != gameObject && go != keep && !_baselineRoots.Contains(go.GetInstanceID())) doomed.Add(go);
            foreach (var go in doomed) if (go != null) Destroy(go);
            yield return null;                      // the destroys happen at the end of the frame, each object's OnDestroy running then
            yield return null;
            GameEvents.Reset();
            GameWorld.SetCurrent(null);
            World = null;
            _baselineRoots = null;
            CommandLine.ClearOverlay();
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
            QualitySettings.vSyncCount = _baseVSync;
            Application.targetFrameRate = _baseFrameRate;
            // what nothing uses any more is unloaded, twice with frames between: the render pipeline lets go of a destroyed camera's history, and of the volume profiles it was reading, a frame or two later
            yield return Resources.UnloadUnusedAssets();
            for (int i = 0; i < 3; i++) yield return null;
            yield return Resources.UnloadUnusedAssets();
            GC.Collect();
            yield return null;
            MnLog.Info("START the scenario is taken down");
        }

        void OnDestroy()
        {
            _eventLog?.Dispose();
            if (World != null && GameWorld.Current == World) GameWorld.SetCurrent(null);
        }
    }
}
