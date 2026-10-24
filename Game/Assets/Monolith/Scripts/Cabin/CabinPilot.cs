using System;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Cabin
{
    /// <summary>
    /// Wraps the player's own pilot source: while the pilot sits in the seat and the ship has power it passes the pad's input through; while the pilot is up (or the
    /// ship is dark) nobody flies, and the ship holds its last heading and speed (no stick, the throttle setting where it was, the afterburner as it was).
    /// </summary>
    public sealed class CabinPilot : IPilotSource
    {
        readonly GameWorld _world;
        readonly IPilotSource _inner;
        readonly CabinSystem _cabin;
        readonly Action<int> _resync;
        bool _away;

        /// <param name="resync">Called when the pilot takes the stick again (the pad's throttle setting picks up from the ship's: <c>PlayerPilot.Attach</c>).</param>
        public CabinPilot(GameWorld world, IPilotSource inner, CabinSystem cabin, Action<int> resync = null)
        {
            _world = world; _inner = inner; _cabin = cabin; _resync = resync;
        }

        public ControlInput Sample(in SimFrame frame)
        {
            if (!_cabin.PilotAway)
            {
                if (_away) { _away = false; _resync?.Invoke(_world.LocalPlayerShip); }
                return _inner.Sample(frame);
            }
            _away = true;
            // No stick, and the throttle set to the speed the ship has now, so its speed stays what it was (a throttle setting would let it carry on speeding up or slowing).
            var hold = default(ControlInput);
            var ship = _world.Ships.Get(_world.LocalPlayerShip);
            if (ship != null)
            {
                float top = Mathf.Max(1f, ship.TopSpeed * Mathf.Max(0.05f, ship.SpeedScale));
                hold.Throttle = Mathf.Clamp01(Mathf.Max(0f, ship.ForwardSpeed) / top);
                if (ship.AfterburnerOn && ship.ForwardSpeed > ship.TopSpeed * ship.SpeedScale) hold.Buttons |= ControlButtons.Afterburner;
            }
            return hold;
        }
    }
}
