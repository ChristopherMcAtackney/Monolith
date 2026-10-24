using System;
using Monolith.Capture;
using Monolith.Core;
using Monolith.Flight;
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

        public void Build()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 1;

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
            // Audio checks: the score, the effects, the radio and the mix, through the real audio system rendered offline. They need frames, so they run as a coroutine and quit when done.
            if (AudioSelfTest.Requested) { AudioSelfTest.Start(this); return; }

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
            else if (!ShipShowcase.TryBuild(World, transform, scene, sceneOptions) && !SkyTestWorld.TryBuild(World, transform, scene, sceneOptions)) SmokeWorld.Build(World, transform);

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
            var pipeline = GraphicsSettings.currentRenderPipeline;
            MnLog.Info($"BOOT ok: Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}), " +
                       $"pipeline {(pipeline != null ? pipeline.GetType().Name : "none")}, {Sim.SystemCount} sim systems at {Sim.Hz} Hz");
        }

        void OnDestroy()
        {
            _eventLog?.Dispose();
            if (World != null && GameWorld.Current == World) GameWorld.SetCurrent(null);
        }
    }
}
