using System;

namespace Monolith.Core
{
    /// <summary>
    /// What a pilot wants this tick. Flight and weapons read nothing else, so the source can be the player's pad, the AI,
    /// a scripted test or (later) a remote player. Axes are -1..1: Pitch +1 is nose up, Yaw +1 is nose right, Roll +1 is
    /// roll right. Throttle is the 0..1 setting (absolute, not a rate). Append new buttons; never reorder them.
    /// </summary>
    [Flags]
    public enum ControlButtons : uint
    {
        None = 0,
        PrimaryFire = 1u << 0,
        SecondaryFire = 1u << 1,
        MissileFire = 1u << 2,
        Countermeasure = 1u << 3,
        Afterburner = 1u << 4,
        MatchSpeed = 1u << 5,
        TargetNext = 1u << 6,
        TargetAttacker = 1u << 7,
        TargetReticle = 1u << 8,
        SubsystemNext = 1u << 9,
        Orders = 1u << 10,
        BattleMap = 1u << 11,
        Power = 1u << 12,
    }

    public struct ControlInput
    {
        public float Pitch, Yaw, Roll, Throttle;
        public ControlButtons Buttons;

        public bool Held(ControlButtons b) => (Buttons & b) == b && b != ControlButtons.None;

        /// <summary>Held now and not held in <paramref name="previous"/> (the tick before).</summary>
        public bool Pressed(in ControlInput previous, ControlButtons b) => Held(b) && !previous.Held(b);

        /// <summary>This input with every axis forced into range and NaN turned to 0, so a bad source cannot poison a ship.</summary>
        public ControlInput Sanitized()
        {
            Pitch = Axis(Pitch); Yaw = Axis(Yaw); Roll = Axis(Roll);
            Throttle = float.IsNaN(Throttle) ? 0f : Math.Max(0f, Math.Min(1f, Throttle));
            return this;
        }

        static float Axis(float v) => float.IsNaN(v) ? 0f : Math.Max(-1f, Math.Min(1f, v));
    }
}
