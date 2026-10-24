using UnityEngine;

namespace Monolith.RenderQuality
{
    /// <summary>
    /// The shared render-quality profile: what every scene gets for shadows, occlusion and contact detail, from one place. <c>SkyRig.Enable</c> builds it (a global volume
    /// plus the Sun's own shadow settings), so a scene needs no shadow volume of its own. Defaults are the game's; a capture plan can override any field by name under
    /// <c>sky.Quality</c>. Round 4 of the corvette: three critics said the hull read "flat and evenly lit", and the cause was here, not in the paint: no scene had any ambient
    /// occlusion, the Sun's cascades were 512 pixels each (HDRP's default) over 3 to 6 km, and the contact shadows the scenes asked for were never switched on at the light.
    /// </summary>
    [System.Serializable]
    public sealed class RenderQualityOptions
    {
        /// <summary>False leaves the scene's own volumes alone (the profile builds nothing).</summary>
        public bool Enabled = true;

        // ---- sun shadows -------------------------------------------------------------------------------------------------------
        /// <summary>How far from the camera the Sun's shadows reach (m). Capital hulls are kilometres long and sit hundreds of metres from the camera.</summary>
        public float ShadowDistance = 6000f;
        /// <summary>1 to 4 cascades.</summary>
        public int ShadowCascades = 4;
        /// <summary>
        /// Where the cascades end, as fractions of <see cref="ShadowDistance"/> (the last one always ends at 1). HDRP's defaults (0.05, 0.15, 0.30) put the first cascade at 300 m for
        /// 6 km, so a close-up on a hull 60 m away sat in a map with 30 cm texels. These end at 60 m, 240 m and 960 m: the first holds a close-up at about 1.5 cm a texel, the
        /// second a ship-length view at 6 cm, the third a whole corvette at 24 cm, and the last the far capital ships.
        /// </summary>
        public float ShadowSplit0 = 0.01f, ShadowSplit1 = 0.04f, ShadowSplit2 = 0.16f;
        /// <summary>Resolution of each cascade (pixels). The light's own default is 512; the pipeline asset caps it (Hdrp.EnsureAsset raises the cap to 4096).</summary>
        public int ShadowResolution = 4096;
        /// <summary>Fade at the end of each cascade (0 to 1 of the cascade): hides the seam between maps of different texel sizes.</summary>
        public float ShadowCascadeBorder = 0.1f;

        // ---- contact shadows (a screen-space ray march from each pixel toward the Sun: the fine shadow the maps are too coarse for) --------------------
        public bool ContactShadows = true;
        /// <summary>Length of each ray (m): how far from a pixel an occluder can be and still shadow it.</summary>
        public float ContactShadowLength = 1.2f;
        /// <summary>Contact shadows are drawn out to this distance from the camera (m), fading over <see cref="ContactShadowFade"/>.</summary>
        public float ContactShadowDistance = 500f;
        public float ContactShadowFade = 100f;
        public float ContactShadowOpacity = 1f;

        // ---- ambient occlusion ----------------------------------------------------------------------------------------------------
        /// <summary>
        /// Screen-space ambient occlusion (HDRP's ground-truth AO). It darkens the fill light in crevices, under ribs and where plates meet. <see cref="AoDirectLighting"/> carries some
        /// of it onto the Sun's light too, which is not physical but is what makes a sunlit trench read as a trench when the fill there is already small.
        /// </summary>
        public bool AmbientOcclusion = true;
        public float AoIntensity = 2.0f;
        /// <summary>World radius of the occlusion (m).</summary>
        public float AoRadius = 3.0f;
        /// <summary>0 to 1: how much of the AO also darkens direct (Sun) light.</summary>
        public float AoDirectLighting = 0.9f;
        /// <summary>0 to 1: how much AO also removes specular (shine in a crevice is as dark as the crevice).</summary>
        public float AoSpecularOcclusion = 1f;
        public int AoSteps = 12;
        public bool AoFullResolution = false;
        public int AoMaxRadiusPixels = 96;
        /// <summary>True to run the AO as ray-traced (needs Direct3D 12, a ray-tracing GPU and the asset's support flag): see <see cref="RayTracing"/>.</summary>
        public bool RayTracedAo = false;
        public float RayTracedAoLength = 4f;
        public int RayTracedAoSamples = 4;

        // ---- micro-shadowing and light-level options ----------------------------------------------------------------------------------
        /// <summary>HDRP's micro shadows: the material's own occlusion (the mask map's green channel) shadows the Sun's light across fine detail the shadow maps cannot hold.</summary>
        public bool MicroShadows = true;
        public float MicroShadowOpacity = 1f;
        /// <summary>True to render the Sun's shadows by ray tracing (hard, exact, no maps). Needs Direct3D 12 and a ray-tracing GPU.</summary>
        public bool RayTracedSunShadows = false;
    }
}
