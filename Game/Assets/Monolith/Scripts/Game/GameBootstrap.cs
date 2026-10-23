using Monolith.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Monolith.Game
{
    /// <summary>
    /// The entry point. The only scene in the build (Assets/Monolith/Scenes/Boot.unity) is empty; once it has loaded,
    /// this builds the whole game from code (<see cref="GameRoot"/>). Command line, all optional:
    ///   -mnCapture plan.json -mnOut dir   capture stills and perf runs, then quit (see Capture/CaptureRunner)
    ///   -mnOnly a,b  -mnKind stills|perf  narrow a capture to some shots, or to one kind
    ///   -mnLogEvents                      log every GameEvents event as an [MN] line
    ///   -mnScene flight                   the flight playtest (with -mnShip class, -mnPilot demo, -mnControls file, -mnFps n)
    ///   -mnFlightScenarios f.json -mnShip class -mnOut dir   write flight traces for tools/fs2ref/compare.py, then quit
    /// </summary>
    public static class GameBootstrap
    {
        public const string BootSceneName = "Boot";

        static GameRoot _root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void OnAfterSceneLoad()
        {
            if (SceneManager.GetActiveScene().name != BootSceneName) return;
            Boot();
        }

        public static GameRoot Boot()
        {
            if (_root != null) return _root;
            var go = new GameObject("Monolith");
            Object.DontDestroyOnLoad(go);
            _root = go.AddComponent<GameRoot>();
            _root.Build();
            return _root;
        }
    }
}
