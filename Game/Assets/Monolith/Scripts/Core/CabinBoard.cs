using UnityEngine;

namespace Monolith.Core
{
    /// <summary>
    /// What the cabin publishes for everyone else to read (owner: Cabin; the view puts the camera where <see cref="EyeNow"/> says, the HUD collapses with
    /// <see cref="HudCollapse"/>, the sky follows <see cref="ExposureEv"/>, the audio listens for the <see cref="CabinEvents"/>). <see cref="GameWorld.Cabin"/> is
    /// null when there is no cabin (the flight playtest, the sandbox); every reader must cope with that.
    /// </summary>
    public sealed class CabinBoard
    {
        public enum Phase { Seated = 0, Rising = 1, Standing = 2, Lowering = 3 }

        /// <summary>The pilot's state. Nobody flies the ship in any state but <see cref="Phase.Seated"/> (the ship holds its heading and speed).</summary>
        public Phase State = Phase.Seated;
        public bool Standing => State != Phase.Seated;
        /// <summary>The main breaker: false is "gone dark" (no HUD, no weapons, the ship drifts).</summary>
        public bool Powered = true;
        /// <summary>The lighting the cabin is in: normal, dark (lamps out, displays dim), emergency (red), alert (red, pulsing), blackout (the breaker is out).</summary>
        public string Lights = "normal";
        /// <summary>0 seated .. 1 standing, eased (the view rising out of the seat).</summary>
        public float Stand01;
        /// <summary>0 .. 1: how far the HUD has collapsed into the cabin's displays (1 = gone). The HUD passes read it.</summary>
        public float HudCollapse;
        /// <summary>The view is the cabin's: the cockpit camera follows <see cref="EyeNow"/> / <see cref="RotNow"/> instead of the seated eye.</summary>
        public bool OwnsCamera;
        /// <summary>The eye in the SHIP's frame (x right, y up, z forward, metres from the ship's centre) and its rotation relative to the ship, at the last two ticks.</summary>
        public Vector3 EyePrev, EyeNow;
        public Quaternion RotPrev = Quaternion.identity, RotNow = Quaternion.identity;
        /// <summary>Vertical field of view the cabin wants (0: leave the camera's own).</summary>
        public float Fov;
        /// <summary>How far the cabin wants the sky's exposure moved from the scene's own (EV, 0 seated; negative the darker the cabin is: the eye opens), and how much of the sky's cold ambient fill reaches the lit scene (1 seated; the cabin's walls would drown in it).</summary>
        public float ExposureShift, AmbientScale = 1f;
        /// <summary>The luminance (nits) that shows as 1.0 on screen at the exposure the eye has now: the cabin's lights and glows are set in screen units, so they read the same however far the eye has opened.</summary>
        public float NitsPerUnit = 1.2f * 16384f;
        /// <summary>Before launch: the sim is held, the cabin runs on its own clock, the briefing is on the plan table.</summary>
        public bool Prelaunch;
        /// <summary>The seat's switch to the battle map is on (the HUD hides, the map draws).</summary>
        public bool MapOpen;
        /// <summary>The minimal prompt for what the pilot is looking at and what the context button does there (null: none), and how far a hold has come (0..1, -1: not a hold).</summary>
        public string Prompt, PromptKey;
        public float PromptProgress = -1f;
        /// <summary>The pilot's hands are doing something: what and how far (the hands animate from this).</summary>
        public string Action;
        public float ActionProgress;
        /// <summary>A hull hit or shove just landed (0..1, fading): the lights flicker and the hands slip with it.</summary>
        public float Jolt;
        /// <summary>The ship's own scan state for the lights and audio: 0 nothing, 1 a sweep is crossing the canopy.</summary>
        public float ScanLight;
        /// <summary>Which way the sweeping scanner's light comes from, in the ship's frame (a unit vector from the cabin toward the scanner, swinging as the sweep crosses).</summary>
        public Vector3 ScanDirShip = Vector3.forward;
        /// <summary>Bumped on every change worth redrawing for.</summary>
        public int Version;

        public Vector3 Eye(float alpha) => Vector3.LerpUnclamped(EyePrev, EyeNow, alpha);
        public Quaternion Rot(float alpha) => Quaternion.SlerpUnclamped(RotPrev, RotNow, alpha);
    }

    /// <summary>
    /// Event ids the cabin raises (with <see cref="GameEvents"/>) for the audio, the juice and the HUD to listen to. The sounds are the audio system's to make.
    /// <code>
    ///   id                  Subject                  Value                   Meaning
    ///   cabin_stand         "stand" / "sit"          0 start, 1 done         the pilot gets up out of the seat, or sits back down
    ///   cabin_step          the floor's material     the step's speed 0..1   one footfall (floor, tread, riser)
    ///   cabin_power         "off" / "on"             1 on, 0 off             the main breaker was thrown (the clunk), then the HUD dies or boots
    ///   cabin_lights        the effective state      -                       the cabin lighting changed
    ///   cabin_scan_contact  "hit" / "miss"           the scan's seconds      a scan sweep crossed the ship and found it (hit) or swept past
    ///   cabin_creak         "hull"                   0..1 how loud           the hull groans (scans, hits, the ship settling)
    ///   cabin_breath        "in" / "out"             0..1 how deep           the pilot's breathing (louder, slower when dark and scanned)
    ///   cabin_repair        "start" / "progress" / "done" / "slip"   0..1 progress   a hand repair
    ///   cabin_loadout       the weapon id            slot                    the rack was reloaded (a module pulled and seated)
    ///   cabin_table         "page" / "map"           page number             the plan table changed page
    ///   cabin_jolt          what hit                 0..1                    something shoved the cabin
    /// </code>
    /// </summary>
    public static class CabinEvents
    {
        public const string Stand = "cabin_stand";
        public const string Step = "cabin_step";
        public const string Power = "cabin_power";
        public const string Lights = "cabin_lights";
        public const string ScanContact = "cabin_scan_contact";
        public const string Creak = "cabin_creak";
        public const string Breath = "cabin_breath";
        public const string Repair = "cabin_repair";
        public const string Loadout = "cabin_loadout";
        public const string Table = "cabin_table";
        public const string Jolt = "cabin_jolt";
    }
}
