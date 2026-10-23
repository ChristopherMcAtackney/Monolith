using UnityEngine;

namespace Monolith.Look
{
    /// <summary>
    /// What the camera does to the picture: the lens (glare, streaks, ghosts, dust on the canopy) and the film (a graded black floor, a mild
    /// vignette, fine grain). Brightness numbers are in "display units at the reference exposure" (1.0 is the brightest an 8-bit picture
    /// shows before tonemapping), like the sky's. <see cref="Intensity"/> scales every effect at once; 0 is the same as <see cref="Enabled"/> = false.
    /// The defaults are meant never to hurt readability: nothing here darkens a ship or hides a HUD-sized detail.
    /// </summary>
    [System.Serializable]
    public sealed class LookOptions
    {
        public bool Enabled = true;
        /// <summary>Master scale for everything below, 0 to 1 (above 1 exaggerates).</summary>
        public float Intensity = 1f;

        // ---- the film: runs after tonemapping, on the whole picture -----------------------------------------------------------
        /// <summary>The colour the darkest areas rise to (display-encoded values, so 0.02 is about 5 of 255, added to black and fading out as a pixel brightens): a graded floor, not a flat zero.</summary>
        public Color FloorColor = new Color(0.008f, 0.020f, 0.036f);
        /// <summary>How much the corners lose, 0 to 1.</summary>
        public float Vignette = 0.10f;
        /// <summary>Grain strength as a standard deviation in display-encoded values (0.01 is about 2.5 of 255), strongest in shadows and midtones.</summary>
        public float GrainAmount = 0.009f;
        /// <summary>Grain clump size in pixels, and how much of it differs per colour channel (0 = monochrome, 1 = fully coloured).</summary>
        public float GrainSizePx = 1.25f;
        public float GrainChroma = 0.3f;

        // ---- the lens: runs before post-processing, in HDR ---------------------------------------------------------------------
        public bool Glare = true;
        /// <summary>Set by whatever owns the Sun (the sky rig): where it is in world space, its colour, and the nits of one display unit at the reference exposure.</summary>
        public Vector3 SunDirection = Vector3.forward;
        public bool HasSun = false;
        public Color SunColor = Color.white;
        public float NitsPerUnit = 19660.8f;

        /// <summary>The wide veil that lifts the whole frame around the Sun: energy, and size as a fraction of the screen height. Cool lavender.</summary>
        public float VeilEnergy = 60000f;
        public float VeilSize = 0.40f;
        public Color VeilTint = new Color(0.62f, 0.74f, 1.0f);
        /// <summary>The haze: a very wide, very faint glow that lifts even the corners of the frame when the Sun is in view (energy, size as a fraction of the screen height).</summary>
        public float HazeEnergy = 70000f;
        public float HazeSize = 1.0f;
        /// <summary>The wings (mid-size power-law glow) and the tight halo.</summary>
        public float WingEnergy = 6500f;
        public float WingSize = 0.055f;
        public float HaloEnergy = 1100f;
        public float HaloSize = 0.010f;
        /// <summary>Red glows a little tighter and blue a little wider than green by this fraction: the colour fringe.</summary>
        public float Fringe = 0.12f;
        /// <summary>How much of the glare is left when the Sun itself is hidden behind something (the scatter around a hull edge).</summary>
        public float HiddenSunGlare = 0.22f;

        /// <summary>The thin pale anamorphic streak: peak, thickness and reach (fractions of the screen height), and how uneven it is along its length (0 = constant).</summary>
        public float StreakPeak = 0.5f;
        public float StreakThickness = 0.0025f;
        public float StreakReach = 0.10f;
        public float StreakUneven = 0.75f;
        /// <summary>The broad tinted streaks (one on the Sun, two fainter siblings above and below): peak, thickness, reach, spacing.</summary>
        public float BroadPeak = 0.26f;
        public float BroadThickness = 0.010f;
        public float BroadReach = 0.24f;
        public float BroadSpacing = 0.07f;
        public Color BroadTint = new Color(0.30f, 0.78f, 1.0f);

        /// <summary>Reflections of the Sun between lens surfaces, on the line through the middle of the picture: 0 = none.</summary>
        public float Ghosts = 0.12f;
        /// <summary>Dust and smudges on the lens or canopy that the glare lights: dust, bokeh discs, and the scale of the dust map (1 = one map across the frame).</summary>
        public float Dust = 1.0f;
        public float DustBokeh = 0.4f;
        public float DustScale = 1.0f;
        public int Seed = 7;
    }
}
