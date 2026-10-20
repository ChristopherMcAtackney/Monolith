using UnityEngine;

namespace Monolith.Core
{
    /// <summary>
    /// Shared, read-mostly game state. Systems read what they need from here (and listen on <see cref="GameEvents"/>)
    /// instead of referencing each other's assemblies. Each field names its owner, the one place allowed to write it.
    /// Add a field only when a second system needs to see it.
    /// </summary>
    public sealed class GameWorld
    {
        public const int NoShip = -1;

        /// <summary>The running game's world (owner: Game/GameRoot).</summary>
        public static GameWorld Current { get; private set; }

        public static void SetCurrent(GameWorld world) => Current = world;

        public GameWorld(Sim sim)
        {
            Sim = sim;
            Pilots = new Pilots();
            Ships = new ShipRegistry();
            Knocks = new KnockQueue();
        }

        /// <summary>The fixed-rate clock (owner: Game/GameRoot).</summary>
        public Sim Sim { get; }

        /// <summary>Per-ship control inputs (owner: Core/Pilots; sources attach through <see cref="Pilots.Attach"/>).</summary>
        public Pilots Pilots { get; }

        /// <summary>Every ship's published flight state (owner: Flight; <see cref="ShipState.Target"/> owner: Targeting).</summary>
        public ShipRegistry Ships { get; }

        /// <summary>Shoves waiting to be applied to ships (owner: anyone adds; Flight drains each tick).</summary>
        public KnockQueue Knocks { get; }

        public int Tick => Sim.Tick;
        public float Time => Sim.Time;
        /// <summary>Render interpolation, see <see cref="Sim"/>.</summary>
        public float Alpha => Sim.Alpha;

        /// <summary>The camera the player sees through (owner: whichever system builds the view; Game's smoke world for now).</summary>
        public Camera MainCamera;

        /// <summary>The ship the local player flies, or <see cref="NoShip"/> (owner: Game/GameRoot).</summary>
        public int LocalPlayerShip = NoShip;
    }
}
