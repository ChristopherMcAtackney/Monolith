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
            Combat = new CombatRegistry();
            Damage = new DamageQueue();
            Fire = new FireQueue();
        }

        /// <summary>The fixed-rate clock (owner: Game/GameRoot).</summary>
        public Sim Sim { get; }

        /// <summary>Per-ship control inputs (owner: Core/Pilots; sources attach through <see cref="Pilots.Attach"/>).</summary>
        public Pilots Pilots { get; }

        /// <summary>Every ship's published flight state (owner: Flight; <see cref="ShipState.Target"/> owner: Targeting).</summary>
        public ShipRegistry Ships { get; }

        /// <summary>Shoves waiting to be applied to ships (owner: anyone adds; Flight drains each tick).</summary>
        public KnockQueue Knocks { get; }

        /// <summary>Every ship's combat state: shield, hull, parts, ammo, lock, warnings, lead (owners are named on <see cref="ShipCombat"/>'s fields).</summary>
        public CombatRegistry Combat { get; }

        /// <summary>Damage waiting to be resolved this tick (owner: weapons and collisions add; Damage drains).</summary>
        public DamageQueue Damage { get; }

        /// <summary>Turret and mount fire requests (owner: AI, scripts and turret controllers add; Weapons drains).</summary>
        public FireQueue Fire { get; }

        public int Tick => Sim.Tick;
        public float Time => Sim.Time;
        /// <summary>Render interpolation, see <see cref="Sim"/>.</summary>
        public float Alpha => Sim.Alpha;

        /// <summary>The camera the player sees through (owner: whichever system builds the view; Game's smoke world for now).</summary>
        public Camera MainCamera;

        /// <summary>The ship the local player flies, or <see cref="NoShip"/> (owner: Game/GameRoot).</summary>
        public int LocalPlayerShip = NoShip;

        /// <summary>
        /// The difficulty's share of weapon damage the local player's ship takes, and on top of that of the damage to its parts (FS2's
        /// Skill_level_player_damage_scale and Skill_level_subsys_damage_scale; owner: AI, from the difficulty level; Damage reads them).
        /// </summary>
        public float PlayerDamageScale = 1f, PlayerPartDamageScale = 1f;

        /// <summary>The running mission's directives, objectives, music state and result, or null (owner: Mission; the HUD reads it).</summary>
        public MissionBoard Mission;

        /// <summary>The cabin's published state (where the pilot is, the view, the lights, the HUD's collapse), or null when there is no cabin (owner: Cabin).</summary>
        public CabinBoard Cabin;

        /// <summary>
        /// The boost's view effects (owner: Juice/BoostFx writes them every frame; the cockpit view and the capture camera read them):
        /// degrees added to the field of view, and how many times longer and brighter the motion dust is drawn. 0, 1, 1 when not boosting.
        /// </summary>
        public float FovKick;
        public float DustStretch = 1f, DustBright = 1f;
    }
}
