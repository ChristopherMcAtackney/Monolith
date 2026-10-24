using System.Collections.Generic;
using Monolith.Audio;
using Monolith.Core;
using Monolith.Loading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Monolith.Game
{
    /// <summary>
    /// For measuring a start (<c>-mnStartupProbe -mnStartupDrive</c>): once the game is flyable, a virtual keyboard does what a player does in the first seconds, so the hitches that only come the
    /// first time something is used show up in the frame times: the afterburner, the guns, a missile, the countermeasure, the brake, the pause screen. (Run it without <c>-mnAutopilot</c>.)
    /// </summary>
    public sealed class StartupDrive : MonoBehaviour
    {
        // seconds after flyable, how long, which key
        static readonly (float at, float hold, Key key)[] Script =
        {
            (1.0f, 1.6f, Key.Tab),          // afterburner
            (1.4f, 0.1f, Key.T),            // next target
            (2.5f, 2.2f, Key.Space),        // guns
            (3.2f, 0.1f, Key.X),            // missile
            (4.2f, 0.1f, Key.LeftShift),    // secondary
            (4.8f, 0.1f, Key.V),            // countermeasure
            (5.6f, 1.2f, Key.LeftCtrl),     // brake
            (6.9f, 0.9f, Key.Z),            // stand up out of the seat (where the mission lets the pilot)
            (7.5f, 0.1f, Key.Escape),       // the pause screen
            (8.6f, 0.1f, Key.Enter),        // back out of it
        };

        Keyboard _kb;
        InputSettings.BackgroundBehavior _old;
        readonly HashSet<Key> _down = new HashSet<Key>();
        readonly List<Key> _scratch = new List<Key>();

        public static void Attach()
        {
            var go = new GameObject("StartupDrive");
            DontDestroyOnLoad(go);
            go.AddComponent<StartupDrive>();
        }

        void Start()
        {
            _old = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _kb = InputSystem.AddDevice<Keyboard>("MnStartupKeys");
        }

        bool _soundNoted;

        void Update()
        {
            if (!_soundNoted && AudioSystem.Active != null && AudioSystem.Active.Lib != null && AudioSystem.Active.Lib.Ready) { _soundNoted = true; StartupProbe.Mark("sound decoded"); }
            float t = StartupProbe.SecondsFlying;
            if (t < 0f || _kb == null) return;
            _scratch.Clear();
            foreach (var s in Script) if (t >= s.at && t < s.at + s.hold) _scratch.Add(s.key);
            bool changed = _scratch.Count != _down.Count;
            if (!changed) foreach (var k in _scratch) if (!_down.Contains(k)) { changed = true; break; }
            if (!changed) return;
            _down.Clear();
            foreach (var k in _scratch) _down.Add(k);
            InputSystem.QueueStateEvent(_kb, new KeyboardState(_scratch.ToArray()));
        }

        void OnDestroy()
        {
            if (_kb != null) InputSystem.RemoveDevice(_kb);
            InputSystem.settings.backgroundBehavior = _old;
        }
    }
}
