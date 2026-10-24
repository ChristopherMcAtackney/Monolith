using System;
using Monolith.AI;
using Monolith.Core;
using Monolith.Flight;
using Monolith.Targeting;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// A bare world with the real systems and the AI, nothing drawn, stepped as fast as the machine allows: the AI self-test, the arena and the
    /// dogfight soak run in it (headless player: <c>-batchmode -nographics</c>).
    /// </summary>
    public sealed class AiLab : IDisposable
    {
        public readonly GameWorld W;
        public readonly FlightSystem Flight;
        public readonly CombatRig Rig;
        public readonly AiDirector Ai;
        public readonly TurretGunnery Guns;
        int _errors;
        public int Errors => _errors;

        public AiLab(int level = -1)
        {
            GameEvents.Reset();
            W = new GameWorld(new Sim());
            GameWorld.SetCurrent(W);
            W.Sim.Register(W.Pilots);
            Flight = new FlightSystem(W);
            W.Sim.Register(new TargetingSystem(W));
            W.Sim.Register(Flight);
            Rig = CombatRig.Create(W, Flight);
            Ai = new AiDirector(W, AiData.Default, level);
            W.Sim.Register(Ai);
            Guns = new TurretGunnery(Ai);
            W.Sim.Register(Guns);
            Application.logMessageReceived += OnLog;
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) _errors++;
        }

        public void Dispose()
        {
            Application.logMessageReceived -= OnLog;
            Ai.Dispose();
            GameEvents.Reset();
        }

        /// <summary>A fighter (or bomber) flown by the AI, or by nobody when <paramref name="profile"/> is null.</summary>
        public ShipState Fighter(string cls, int team, Vector3 pos, Quaternion rot, float speedFraction = 0.6f, string profile = "drone",
                                 AiRole role = AiRole.Fighter, string name = null, string statsKey = null)
        {
            var klass = ShipCatalog.Load(cls);
            var st = Flight.Spawn(klass, pos, rot, speedFraction * klass.TopSpeed, speedFraction, team, name ?? $"{cls} {W.Ships.Count + 1}");
            Rig.Spawn(st, CombatCatalog.Get(cls) != null ? cls : null);
            if (profile != null) Ai.AddPilot(st.Id, profile, role, true, statsKey);
            return st;
        }

        public void Run(float seconds, Func<bool> stop = null)
        {
            int n = Mathf.RoundToInt(seconds * Sim.Hz);
            for (int i = 0; i < n; i++)
            {
                W.Sim.StepOnce();
                if (stop != null && (i % 6) == 0 && stop()) return;
            }
        }
    }
}
