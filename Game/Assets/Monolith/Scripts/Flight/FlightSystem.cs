using System;
using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Flight
{
    /// <summary>
    /// Moves every ship once per tick (order <see cref="SimOrder.Flight"/>) from its pilot's <see cref="ControlInput"/>
    /// (<see cref="GameWorld.Pilots"/>), applies pending <see cref="GameWorld.Knocks"/>, and publishes each ship's
    /// <see cref="ShipState"/> in <see cref="GameWorld.Ships"/>. Who flies a ship (pad, AI, script, remote) never matters here.
    /// </summary>
    public sealed class FlightSystem : ISimSystem
    {
        readonly GameWorld _world;
        readonly FlightRules _rules;
        readonly List<Entry> _ships = new List<Entry>();
        readonly Dictionary<int, Entry> _byId = new Dictionary<int, Entry>();
        readonly List<ShipKnock> _knocks = new List<ShipKnock>();

        sealed class Entry
        {
            public ShipFlight Model;
            public ShipState State;
        }

        public FlightSystem(GameWorld world, FlightRules rules = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _rules = rules ?? FlightRules.Default;
        }

        public int Order => SimOrder.Flight;
        public FlightRules Rules => _rules;

        /// <summary>Create a ship flying along its nose at <paramref name="speed"/> m/s; returns its published state (Id is the ship id).</summary>
        public ShipState Spawn(ShipClass cls, Vector3 position, Quaternion rotation, float speed = 0f, float throttle = 0f,
                               int team = 0, string callsign = null)
        {
            int id = _world.Ships.NewId();
            var model = new ShipFlight(cls, _rules, id);
            model.Place(position, rotation, speed, throttle);
            var state = new ShipState
            {
                Id = id,
                ClassId = cls.Id,
                ClassName = cls.Name,
                Callsign = callsign ?? cls.Name,
                Team = team,
                Radius = cls.Radius,
                Mass = cls.Mass,
                TopSpeed = cls.TopSpeed,
                AfterburnerSpeed = cls.HasAfterburner ? cls.AfterburnerSpeed : cls.TopSpeed,
                FuelMax = cls.HasAfterburner ? cls.AfterburnerFuel : 0f,
            };
            var e = new Entry { Model = model, State = state };
            Publish(e, true);
            _ships.Add(e);
            _byId.Add(id, e);
            _world.Ships.Add(state);
            return state;
        }

        public void Despawn(int id)
        {
            if (!_byId.TryGetValue(id, out var e)) return;
            _byId.Remove(id);
            _ships.Remove(e);
            _world.Ships.Remove(id);
            foreach (var other in _world.Ships.All)
                if (other.Target == id) other.Target = GameWorld.NoShip;
        }

        /// <summary>Move a ship somewhere at once (respawn, scripted cuts); views will not smear across the jump.</summary>
        public void Place(int id, Vector3 position, Quaternion rotation, float speed, float throttle)
        {
            if (!_byId.TryGetValue(id, out var e)) return;
            e.Model.Place(position, rotation, speed, throttle);
            Publish(e, true);
        }

        /// <summary>The model behind a ship (tests and the scenario runner), or null.</summary>
        public ShipFlight Model(int id) => _byId.TryGetValue(id, out var e) ? e.Model : null;

        public void Step(in SimFrame frame)
        {
            _knocks.Clear();
            _world.Knocks.Drain(_knocks);
            foreach (var k in _knocks)
                if (_byId.TryGetValue(k.Ship, out var hit)) hit.Model.ApplyKnock(k);

            var pilots = _world.Pilots;
            for (int i = 0; i < _ships.Count; i++)
            {
                var e = _ships[i];
                var s = e.State;
                float targetSpeed = float.NaN;
                if (s.Target != GameWorld.NoShip)
                {
                    var t = _world.Ships.Get(s.Target);
                    if (t != null) targetSpeed = t.ForwardSpeed;
                }
                s.PreviousPosition = e.Model.Position;
                s.PreviousRotation = e.Model.Rotation;
                e.Model.SpeedScale = s.SpeedScale;                // damaged engines and the power choice (Damage owns it)
                e.Model.Step(pilots.Current(s.Id), pilots.Previous(s.Id), targetSpeed, frame.Dt);
                Publish(e, false);
            }
        }

        static void Publish(Entry e, bool jump)
        {
            var m = e.Model;
            var s = e.State;
            s.Position = m.Position;
            s.Rotation = m.Rotation;
            if (jump)
            {
                s.PreviousPosition = m.Position;
                s.PreviousRotation = m.Rotation;
            }
            s.Velocity = m.Velocity;
            s.AngularVelocity = m.Spin;
            s.ForwardSpeed = m.LocalVelocity.z;
            s.Throttle = m.Throttle;
            s.MatchingSpeed = m.Matching;
            s.AfterburnerOn = m.AfterburnerOn;
            s.Fuel = m.Fuel;
            s.Knock = m.KnockFraction;
        }
    }
}
