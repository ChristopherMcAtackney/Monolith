using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Targeting
{
    /// <summary>
    /// Minimal target selection, enough for match speed and the HUD bracket (order <see cref="SimOrder.Targeting"/>, owner
    /// of <see cref="ShipState.Target"/>). Any piloted ship: <see cref="ControlButtons.TargetReticle"/> picks the ship
    /// nearest the nose (within <see cref="AheadConeDegrees"/>), <see cref="ControlButtons.TargetNext"/> cycles through
    /// the others nearest first. A target that no longer exists is dropped. Hostility, subsystems and lock-on come later.
    /// </summary>
    public sealed class TargetingSystem : ISimSystem
    {
        /// <summary>How far off the nose "target ahead" looks, degrees.</summary>
        public const float AheadConeDegrees = 30f;

        readonly GameWorld _world;
        readonly List<ShipState> _candidates = new List<ShipState>();

        public TargetingSystem(GameWorld world) { _world = world; }

        public int Order => SimOrder.Targeting;

        public void Step(in SimFrame frame)
        {
            var ships = _world.Ships;
            var pilots = _world.Pilots;
            for (int i = 0; i < ships.All.Count; i++)
            {
                var s = ships.All[i];
                if (s.Target != GameWorld.NoShip && ships.Get(s.Target) == null) s.Target = GameWorld.NoShip;
                var now = pilots.Current(s.Id);
                var before = pilots.Previous(s.Id);
                if (now.Pressed(before, ControlButtons.TargetReticle)) s.Target = Ahead(s);
                else if (now.Pressed(before, ControlButtons.TargetNext)) s.Target = Next(s);
            }
        }

        int Ahead(ShipState self)
        {
            int best = GameWorld.NoShip;
            float bestAngle = AheadConeDegrees;
            foreach (var o in _world.Ships.All)
            {
                if (o == self) continue;
                float a = Vector3.Angle(self.Forward, o.Position - self.Position);
                if (a < bestAngle) { bestAngle = a; best = o.Id; }
            }
            return best != GameWorld.NoShip ? best : self.Target;
        }

        int Next(ShipState self)
        {
            _candidates.Clear();
            foreach (var o in _world.Ships.All)
                if (o != self) _candidates.Add(o);
            if (_candidates.Count == 0) return GameWorld.NoShip;
            _candidates.Sort((a, b) =>
            {
                int c = (a.Position - self.Position).sqrMagnitude.CompareTo((b.Position - self.Position).sqrMagnitude);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });
            int at = _candidates.FindIndex(c => c.Id == self.Target);
            return _candidates[(at + 1) % _candidates.Count].Id;
        }
    }
}
