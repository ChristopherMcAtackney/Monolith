using UnityEngine;

namespace Monolith.Sky
{
    public enum SkyExposure
    {
        /// <summary>One fixed exposure: lit hulls stay at a steady brightness and space stays black. The default.</summary>
        Fixed,
        /// <summary>HDRP's automatic exposure, clamped to a narrow range (the camera iris).</summary>
        Automatic,
    }

    /// <summary>
    /// Everything about the sky that a scene may want to change. Defaults are the Oort Cloud look. Brightness numbers are in
    /// "display units at the reference exposure": 1.0 is the brightest value an 8-bit picture can show (before the tonemapper),
    /// at <see cref="ExposureEv100"/>. They scale with the camera's exposure like real light does, so stars brighten if the
    /// iris opens.
    /// </summary>
    [System.Serializable]
    public sealed class SkyOptions
    {
        // ---- the Sun (about 2,000 AU out: a point, magnitude about -10) ------------------------------------------------
        /// <summary>Where the Sun is among the stars, as ecliptic longitude/latitude in degrees seen from the viewer.</summary>
        public float SunEclipticLongitude = 152f;
        public float SunEclipticLatitude = 5f;
        /// <summary>Extra rotation of the whole sky (stars, Milky Way and Sun together), Euler degrees.</summary>
        public Vector3 SkyRotationEuler = Vector3.zero;
        /// <summary>The directional light's illuminance. Real sunlight at 1 AU is about 100,000 lux; readability beats realism out here.</summary>
        public float SunLux = 100000f;
        /// <summary>Colour of the sunlight and of the Sun point. A gentle warm white: the Sun is about 5,800 K, and the cold fill from the sky is what makes shadows read.</summary>
        public Color SunColor = new Color(1.0f, 0.97f, 0.92f);
        /// <summary>Angular diameter of the light for shadow softness: a hard sun.</summary>
        public float SunLightAngularDiameterDeg = 0.1f;
        public bool SunShadows = true;
        /// <summary>The point-star core (peak is many times white; the tonemapper and bloom turn it into a bright dot).</summary>
        public float SunCore = 150f;
        public float SunCoreSigmaPx = 0.8f;
        // The Sun's glare (veil, wings, streaks, ghosts, dust) is part of the camera look: see Look below.

        // ---- stars and the Milky Way ---------------------------------------------------------------------------------------
        /// <summary>Stars fainter than this are not drawn (V magnitude). The catalogue goes to about 9.</summary>
        public float LimitMagnitude = 8.8f;
        /// <summary>Energy of a magnitude-<see cref="StarReferenceMagnitude"/> star, in display units.</summary>
        public float StarGain = 0.7f;
        public float StarReferenceMagnitude = 6f;
        /// <summary>1 follows real flux ratios; lower compresses the range between Sirius and the faintest stars so both stay visible.</summary>
        public float StarGamma = 1.0f;
        /// <summary>Standard deviation of a star's core in screen pixels (0.75: a star's peak varies by about 1.45 times between the best and worst sub-pixel position; 0.65 gives 1.65 and crisper dots; 1.0 gives 1.25 and soft ones).</summary>
        public float StarSigmaPx = 0.75f;
        /// <summary>0 = every star white, 1 = true colours, above 1 = exaggerated.</summary>
        public float StarSaturation = 1.0f;
        /// <summary>How much of a bright star's light goes into its halo (0 to 1) and the halo scale in pixels for dim and for very bright stars.</summary>
        public float StarHaloFraction = 0.8f;
        public float StarHaloSizeDimPx = 0.9f;
        public float StarHaloSizeBrightPx = 7f;
        /// <summary>The brightest stars get a faint horizontal streak: strength (0 = none) and reach in pixels.</summary>
        public float StarStreak = 0.5f;
        public float StarStreakReachPx = 6f;
        /// <summary>The map is 0 to 1; this scales it to display units. About 0.55 with the contrast below shows the band and its dust lanes and leaves the faint background nearly black.</summary>
        public float MilkyWayGain = 0.55f;
        /// <summary>Exponent on the map before the gain: above 1 deepens the dark sky and the dust lanes against the bright core.</summary>
        public float MilkyWayContrast = 1.5f;
        /// <summary>Colour multiplier on the Milky Way (the map is warm; the show is graded cold).</summary>
        public Color MilkyWayTint = new Color(0.78f, 0.93f, 1.15f);
        /// <summary>0 = grey, 1 = the map own colours (warm, with brown dust).</summary>
        public float MilkyWaySaturation = 0.7f;
        /// <summary>A lift under everything before tonemapping, display units. The ACES toe swallows it; the graded floor is the camera look's (Look.FloorColor).</summary>
        public Color BlackLift = new Color(0f, 0f, 0f);

        // ---- light from the sky on ships (never seen directly) ---------------------------------------------------------------
        /// <summary>
        /// The cold fill that keeps a hull's shadow side readable: its colour and its brightest radiance in nits. It is baked into HDRP's
        /// environment lighting, so every lit surface in the scene gets it (diffuse from the ambient probe, shine from the reflection
        /// cubemap). The tonemapper's toe swallows low values, so a fill that reads as a dim blue-grey needs thousands of nits here.
        /// </summary>
        public Color AmbientColor = new Color(0.55f, 0.62f, 0.78f);
        public float AmbientNits = 5500f;
        /// <summary>0 = the fill is the same all round; 0.7 makes it 1.7 times stronger facing away from the Sun and 0.3 times toward it, which gives a shadow side form.</summary>
        public float AmbientAwayFromSun = 0.7f;
        /// <summary>How much of the Milky Way's own light joins the fill (1 = as drawn). The band brightens the fill on the side it lies.</summary>
        public float AmbientMilkyWay = 1f;

        // ---- exposure and look -------------------------------------------------------------------------------------------------
        /// <summary>Add an exposure override (fixed or automatic) so ships stay readable. Turn off if the scene sets its own.</summary>
        public bool ConfigureExposure = true;
        public SkyExposure Exposure = SkyExposure.Fixed;
        /// <summary>The fixed exposure, and the reference for the brightness numbers above. At 14 a mid-grey surface in the Sun lands mid-tone.</summary>
        public float ExposureEv100 = 14f;
        public float AutoExposureMinEv = 12.5f;
        public float AutoExposureMaxEv = 15f;
        /// <summary>Add tonemapping (ACES) and bloom overrides. Turn off if the scene sets its own.</summary>
        public bool ConfigureLook = true;
        public float BloomIntensity = 0.3f;
        /// <summary>Bloom starts above this (display units): lit hulls stay clean, sun glints on shiny surfaces, emissive lights and the brightest stars glow.</summary>
        public float BloomThreshold = 0.9f;
        public float BloomScatter = 0.65f;

        // ---- the camera look (lens glare, dust, grain, graded black) ----------------------------------------------------------------
        /// <summary>Switch the camera look on with the sky: the Sun's glare needs the Sun, so the rig feeds it. Its options (Enabled, Intensity and the rest) are here.</summary>
        public bool CameraLook = true;
        public Monolith.Look.LookOptions Look = new Monolith.Look.LookOptions();
    }
}
