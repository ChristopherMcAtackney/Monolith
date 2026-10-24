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

            GameEvents.Reset();
            World = new GameWorld(new Sim());
            GameWorld.SetCurrent(World);
            Sim.Register(World.Pilots);
            gameObject.AddComponent<SimDriver>().Sim = Sim;

            if (CommandLine.Has("-mnLogEvents")) _eventLog = GameEvents.SubscribeAll(e => MnLog.Info("event " + e));

            // Flight and targeting run in every scene; the scene decides whether there are any ships.
            var flight = new FlightSystem(World);
            Sim.Register(new TargetingSystem(World));
            Sim.Register(flight);

            // A capture plan may ask for a particular test scene ("scene": "sky", "flight"); else -mnScene; else the smoke world.
            string scene = null, sceneOptions = null;
            if (CommandLine.Has("-mnCapture")) CapturePlan.PeekScene(CommandLine.Get("-mnCapture"), out scene, out sceneOptions);
            scene ??= CommandLine.Get("-mnScene");
            if (string.Equals(scene, FlightPlaytest.SceneName, StringComparison.OrdinalIgnoreCase)) FlightPlaytest.Build(World, flight, transform, sceneOptions);
            else if (string.Equals(scene, JuiceSandbox.SceneName, StringComparison.OrdinalIgnoreCase)) JuiceSandbox.Build(World, flight, transform, sceneOptions);
            else if (!ShipShowcase.TryBuild(World, transform, scene, sceneOptions) && !SkyTestWorld.TryBuild(World, transform, scene, sceneOptions)) SmokeWorld.Build(World, transform);

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
