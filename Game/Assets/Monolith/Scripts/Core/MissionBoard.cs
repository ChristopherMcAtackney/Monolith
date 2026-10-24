using System.Collections.Generic;

namespace Monolith.Core
{
    /// <summary>
    /// What the running mission publishes for everyone else to read (owner: Mission; the HUD, the music and later the cabin's plan table read
    /// it): the directives (what to do right now), the objectives (primary, secondary, bonus), the music state, the cabin prompt and the result.
    /// <see cref="GameWorld.Mission"/> is null when no mission is running.
    /// </summary>
    public sealed class MissionBoard
    {
        public enum State { Active = 0, Done = 1, Failed = 2 }

        /// <summary>One line of the directives list: an event's directive text while the event is live, then done or failed for a few seconds.</summary>
        public sealed class Directive
        {
            public string Text;
            public State State;
            /// <summary>Sim time the line got its current state.</summary>
            public float Since;
        }

        public sealed class Objective
        {
            public string Id, Kind, Text;
            public State State;
            /// <summary>Hidden objectives (TIE Fighter's secret bonus goals) stay off the list until they are met.</summary>
            public bool Hidden;
        }

        public string Id, Name;
        public float StartedAt;
        public readonly List<Directive> Directives = new List<Directive>();
        public readonly List<Objective> Objectives = new List<Objective>();
        /// <summary>The music state (FS2's music.tbl states: ambient, battle1..3, victory, failure, death) and the track the mission names for it.</summary>
        public string Music = "ambient", MusicTrack;
        /// <summary>The cabin's call to leave the seat (a stub until the cabin exists): shown as a prompt while set.</summary>
        public string Prompt;
        public bool Ended;
        /// <summary>"victory", "defeat" or "neutral" once ended; <see cref="ResultText"/> is the banner ("AREA CLEAR").</summary>
        public string Result, ResultText;
        /// <summary>Bumped on every change, so a reader can skip work when nothing moved.</summary>
        public int Version;
    }

    /// <summary>
    /// Event ids the mission system raises (with <see cref="GameEvents"/>), for the juice, the HUD, the music and the cabin to listen to.
    /// <code>
    ///   id                 Entity          Other    Position / Direction        Value                  Subject
    ///   jump_in            arriving ship   -        its centre / its heading    its radius m           size class (fighter, bomber, corvette, capital)
    ///   jump_out           leaving ship    -        its centre / its heading    its radius m           size class
    ///   mission_event      -               -        -                           times fired so far     the mission event's name
    ///   objective_changed  -               -        -                           1 done, -1 failed, 0 pending   the objective's id
    ///   music_state        -               -        -                           -                      the state (ambient, battle1, battle2, battle3, victory, failure, death)
    ///   music_sting        -               -        -                           -                      enemy_arrival, friendly_arrival, goal_complete, goal_failed
    ///   radio_voice        speaker         -        -                           -                      the voice clip named by the message (none play yet)
    ///   mission_end        -               -        -                           1 victory, -1 defeat, 0 neutral   the banner text
    ///   cabin_scan         scanning ship   -        its centre                  seconds                "start" (a scan sweep; the cabin is a stub for now)
    ///   cabin_call_out     -               -        -                           -                      the prompt (leave the seat)
    ///   cabin_knock_out    ship            -        -                           part index + 1         the part's name (it needs a hand repair)
    ///   cabin_lights       -               -        -                           -                      the lighting state (normal, dark, emergency, alert)
    /// </code>
    /// The radio itself goes out as <see cref="CombatEvents.RadioLine"/>, like the AI's.
    /// </summary>
    public static class MissionEvents
    {
        public const string JumpIn = "jump_in";
        public const string JumpOut = "jump_out";
        public const string EventFired = "mission_event";
        public const string ObjectiveChanged = "objective_changed";
        public const string MusicState = "music_state";
        public const string MusicSting = "music_sting";
        public const string RadioVoice = "radio_voice";
        public const string MissionEnd = "mission_end";
        public const string CabinScan = "cabin_scan";
        public const string CabinCallOut = "cabin_call_out";
        public const string CabinKnockOut = "cabin_knock_out";
        public const string CabinLights = "cabin_lights";
    }
}
