using System;
using System.Collections.Generic;

namespace Monolith.Core
{
    /// <summary>One fixed simulation step. Gameplay reads time from here, never from UnityEngine.Time.</summary>
    public readonly struct SimFrame
    {
        public readonly int Tick;
        public SimFrame(int tick) { Tick = tick; }
        public float Dt => Sim.Dt;
        /// <summary>Seconds of simulated time at the start of this step.</summary>
        public float Time => Tick * Sim.Dt;
    }

    /// <summary>Anything that advances with the fixed-rate simulation. Systems talk through GameWorld and GameEvents.</summary>
    public interface ISimSystem
    {
        /// <summary>Lower runs first. Use the bands in <see cref="SimOrder"/>; equal orders run in registration order.</summary>
        int Order { get; }
        void Step(in SimFrame frame);
    }

    /// <summary>Shared ordering bands so independent systems slot in without coordinating.</summary>
    public static class SimOrder
    {
        public const int Pilots = 100;        // every ship's ControlInput is filled (player pad, AI, script, remote)
        public const int Targeting = 200;     // target selection reacts to this tick's buttons
        public const int Flight = 300;        // ships move
        public const int Weapons = 400;       // guns, missiles, beams
        public const int Collision = 500;     // hits, damage, destruction
        public const int Mission = 600;       // triggers and objectives react to what just happened
        public const int Presentation = 900;  // camera, HUD, audio cues read the settled state
        public const int Late = 1000;
    }

    /// <summary>
    /// The fixed-rate clock: <see cref="Hz"/> ticks per second whatever the frame rate, running registered systems in
    /// <see cref="ISimSystem.Order"/>. Rendering interpolates with <see cref="Alpha"/>: draw
    /// Lerp(previous tick's state, current tick's state, Alpha). A plain class, driven once per rendered frame by
    /// <see cref="SimDriver"/> (or by hand in tests).
    /// </summary>
    public sealed class Sim
    {
        public const int Hz = 60;
        public const float Dt = 1f / Hz;
        /// <summary>A long frame (window drag, breakpoint) is cut to this much simulated time instead of spiralling.</summary>
        public const double MaxCatchUpSeconds = 0.25;

        public int Tick { get; private set; }
        public float Time => Tick * Dt;
        /// <summary>0..1: how far real time has moved from the current tick towards the next. Always 1 in capture mode.</summary>
        public float Alpha { get; private set; } = 1f;
        /// <summary>Slow motion or fast forward (juice sandbox, debugging). 0 freezes the clock.</summary>
        public float TimeScale { get; set; } = 1f;
        /// <summary>Capture mode: every rendered frame is exactly one tick and real time is ignored, so runs are repeatable.</summary>
        public bool OneTickPerFrame { get; set; }

        readonly List<ISimSystem> _systems = new List<ISimSystem>();
        readonly Dictionary<ISimSystem, int> _faults = new Dictionary<ISimSystem, int>();
        ISimSystem[] _ordered = Array.Empty<ISimSystem>();
        bool _dirty;
        double _accumulator;

        public int SystemCount => _systems.Count;

        public void Register(ISimSystem system)
        {
            if (system == null || _systems.Contains(system)) return;
            _systems.Add(system);
            _dirty = true;
        }

        public void Unregister(ISimSystem system)
        {
            if (_systems.Remove(system)) _dirty = true;
        }

        /// <summary>Call once per rendered frame with the real (unscaled) frame time in seconds.</summary>
        public void Advance(double realDt)
        {
            if (OneTickPerFrame)
            {
                StepOnce();
                Alpha = 1f;
                return;
            }
            if (TimeScale <= 0f) return;
            _accumulator += Math.Min(realDt, MaxCatchUpSeconds) * TimeScale;
            while (_accumulator >= Dt)
            {
                StepOnce();
                _accumulator -= Dt;
            }
            Alpha = (float)(_accumulator / Dt);
        }

        /// <summary>Run exactly one tick now.</summary>
        public void StepOnce()
        {
            if (_dirty) Rebuild();
            var frame = new SimFrame(Tick);
            var ordered = _ordered;
            for (int i = 0; i < ordered.Length; i++)
            {
                var s = ordered[i];
                try { s.Step(frame); }
                catch (Exception e) { ReportFault(s, e); }
            }
            Tick++;
        }

        void Rebuild()
        {
            // Stable by registration index, so equal orders are deterministic.
            var keyed = new List<KeyValuePair<int, ISimSystem>>(_systems.Count);
            for (int i = 0; i < _systems.Count; i++) keyed.Add(new KeyValuePair<int, ISimSystem>(i, _systems[i]));
            keyed.Sort((a, b) =>
            {
                int c = a.Value.Order.CompareTo(b.Value.Order);
                return c != 0 ? c : a.Key.CompareTo(b.Key);
            });
            _ordered = new ISimSystem[keyed.Count];
            for (int i = 0; i < keyed.Count; i++) _ordered[i] = keyed[i].Value;
            _dirty = false;
        }

        void ReportFault(ISimSystem s, Exception e)
        {
            _faults.TryGetValue(s, out int n);
            _faults[s] = ++n;
            if (n <= 3) MnLog.Error($"sim system {s.GetType().Name} threw on tick {Tick} (fault {n}): {e}");
        }
    }
}
