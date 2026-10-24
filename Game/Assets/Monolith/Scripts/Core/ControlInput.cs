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
        /// <summary>Held: fire the fast gun.</summary>
        PrimaryFire = 1u << 0,
        /// <summary>Held: fire the heavy gun.</summary>
        SecondaryFire = 1u << 1,
        /// <summary>Held: fire the selected missile (it fires when its launcher is ready and, for lock-on weapons, the lock is made).</summary>
        MissileFire = 1u << 2,
        /// <summary>Held or tapped: release a decoy (one per half second while held).</summary>
        Countermeasure = 1u << 3,
        Afterburner = 1u << 4,
        MatchSpeed = 1u << 5,
        /// <summary>Tap: the next hostile, nearest first.</summary>
        TargetNext = 1u << 6,
        /// <summary>Tap: the nearest hostile that is attacking this ship (shooting at it or with a missile on it).</summary>
        TargetAttacker = 1u << 7,
        /// <summary>Tap: the ship nearest the nose.</summary>
        TargetReticle = 1u << 8,
        /// <summary>Tap: the next part of the targeted ship (turrets, engines, sensors...), then back to the whole ship.</summary>
        SubsystemNext = 1u << 9,
        /// <summary>Held: the quick menu is open (power picks and wingman orders are read from the buttons below while it is).</summary>
        Orders = 1u << 10,
        BattleMap = 1u << 11,
        /// <summary>Tap: step the power choice balanced, shields, guns, engines.</summary>
        Power = 1u << 12,
        /// <summary>Tap: the next missile type (heat-seeker, lock-on, torpedo).</summary>
        CycleMissile = 1u << 13,
        /// <summary>Taps from the quick menu: set the power choice.</summary>
        PowerShields = 1u << 14,
        PowerGuns = 1u << 15,
        PowerEngines = 1u << 16,
        PowerBalanced = 1u << 17,
        /// <summary>Taps from the quick menu, reserved for the wingman orders (the AI builder wires them).</summary>
        OrderAttack = 1u << 18,
        OrderDisable = 1u << 19,
        OrderCover = 1u << 20,
        OrderForm = 1u << 21,
    }

    public struct ControlInput
    {
        public float Pitch, Yaw, Roll, Throttle;
        public ControlButtons Buttons;
        /// <summary>Target this ship now (0 = no request). How the AI and scripts pick a target; the buttons above are the player's way.</summary>
        public int TargetShip;
        /// <summary>With <see cref="TargetShip"/> or on its own: target the part with this index plus one on the targeted ship (0 = leave it).</summary>
        public int TargetPartPlusOne;
        /// <summary>Select this missile slot plus one (0 = leave it); the AI's way to pick a weapon without cycling.</summary>
        public int SelectMissilePlusOne;

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
