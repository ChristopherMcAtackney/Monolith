using UnityEngine;

namespace Monolith.Core
{
    /// <summary>
    /// The player's death sequence, published for everyone else to read (owner: Juice/Fx/DeathFx; <see cref="GameWorld.Death"/> is never null).
    /// While <see cref="Active"/> the cabin is not drawn (the sequence has its own, torn to pieces), the HUD and the prompts are off, the mission is
    /// held from ending, and the view is the sequence's: the cockpit camera takes <see cref="Position"/>, <see cref="Rotation"/> and <see cref="Fov"/>
    /// while <see cref="CameraActive"/> is true. The camera is always the player's eye (first person throughout, never an external view). When it is over
    /// <see cref="Finished"/> is true and the screen stays black (<see cref="CameraActive"/> stays on) until the world is reset for the next run.
    /// </summary>
    public sealed class DeathBoard
    {
        /// <summary>The sequence is playing.</summary>
        public bool Active;
        /// <summary>The camera pose below is the view (true from the hit until the world is reset).</summary>
        public bool CameraActive;
        /// <summary>The sequence has played to its end (or was skipped): the failure flow may show.</summary>
        public bool Finished;
        /// <summary>The player may skip it now (a second or two after the hit): A or Start.</summary>
        public bool Skippable;
        public Vector3 Position, Velocity;
        public Quaternion Rotation = Quaternion.identity;
        /// <summary>Vertical field of view, degrees.</summary>
        public float Fov = 55f;
        /// <summary>Seconds since the hit, and the phase: "", "hit", "breach", "tumble", "dying", "dark".</summary>
        public float Time;
        public string Phase = "";
        /// <summary>Why: "gunfire", "blast", "beam" or "collision".</summary>
        public string Cause = "";
        /// <summary>How dark the picture is (0 clear, 1 black): what is drawn after post-processing (the motion dust) fades with it.</summary>
        public float Dark;
        /// <summary>How many sequences have played this run of the game (tests read it).</summary>
        public int Plays;
    }

    /// <summary>
    /// Event ids the death sequence raises (with <see cref="GameEvents"/>), in the order they come, for the audio builder to hang sounds on (the juice makes
    /// a first set through <c>IJuiceAudio</c>; the audio system may replace or add to them). Subject is the cause for <see cref="Start"/>; Value says how much.
    /// <code>
    ///   death_start            the ship was destroyed: subject = cause (gunfire, blast, beam, collision), value = length in seconds
    ///   death_canopy_crack     the canopy fractures (0 s)            value 0..1 how hard
    ///   death_hull_scream      the hull begins to tear (0.2 s)       value = seconds it lasts
    ///   death_decompression    the canopy blows out, the air goes (about 0.5 s): the loudest moment
    ///   death_tumble           flung clear and spinning (0.7 s)      value = rad/s
    ///   death_breath           the pilot's own breathing (1.4 s)     value = seconds it lasts
    ///   death_heartbeat        one beat, many                        value 1 strong .. 0.3 faint
    ///   death_heartbeat_stop   the last beat has gone (5.2 s)
    ///   death_silence          nothing (5.4 s)
    ///   death_skip             the player skipped it
    ///   death_end              over: the failure flow follows
    /// </code>
    /// </summary>
    public static class DeathEvents
    {
        public const string Start = "death_start";
        public const string CanopyCrack = "death_canopy_crack";
        public const string HullScream = "death_hull_scream";
        public const string Decompression = "death_decompression";
        public const string Tumble = "death_tumble";
        public const string Breath = "death_breath";
        public const string Heartbeat = "death_heartbeat";
        public const string HeartbeatStop = "death_heartbeat_stop";
        public const string Silence = "death_silence";
        public const string Skip = "death_skip";
        public const string End = "death_end";
    }
}
