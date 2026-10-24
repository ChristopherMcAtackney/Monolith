using System;
using System.IO;
using Monolith.Core;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.RenderQuality
{
    /// <summary>
    /// Builds and owns the shared render-quality profile (see <see cref="RenderQualityOptions"/>): one global volume holding the Sun's cascade layout, contact shadows, ambient
    /// occlusion and micro shadows, plus the settings on the Sun light itself that a volume cannot reach (shadow-map resolution, contact shadows at the light, ray-traced shadows).
    /// <c>SkyRig</c> creates it next to the Sun, so every scene that switches the sky on gets it; no scene builds a shadow volume of its own any more.
    /// Everything it creates is a child of its own GameObject. Change <see cref="Options"/> and call <see cref="Apply"/> to retune at run time.
    /// </summary>
    public sealed class RenderQuality : MonoBehaviour
    {
        public RenderQualityOptions Options { get; private set; }

        HDAdditionalLightData _sun;
        VolumeProfile _profile;
        HDShadowSettings _shadows;
        ContactShadows _contact;
        ScreenSpaceAmbientOcclusion _ao;
        MicroShadowing _micro;

        /// <summary>Build the profile for <paramref name="sun"/>'s scene. Returns null when <see cref="RenderQualityOptions.Enabled"/> is off.</summary>
        public static RenderQuality Enable(Transform parent, HDAdditionalLightData sun, RenderQualityOptions options)
        {
            options = options ?? new RenderQualityOptions();
            // "-mnQuality {json}" or "-mnQuality file.json": any option by name, merged over the scene's, so any scene can be timed or looked at with a feature off without a rebuild.
            var over = CommandLine.Get("-mnQuality");
            if (!string.IsNullOrWhiteSpace(over))
            {
                try { JsonConvert.PopulateObject(over.TrimStart().StartsWith("{") ? over : File.ReadAllText(over), options); }
                catch (Exception e) { MnLog.Warn($"RENDERQ -mnQuality ignored: {e.Message}"); }
            }
            if (!options.Enabled) return null;
            var go = new GameObject("MonolithRenderQuality");
            if (parent != null) go.transform.SetParent(parent, false);
            var rq = go.AddComponent<RenderQuality>();
            rq.Build(sun, options);
            return rq;
        }

        void Build(HDAdditionalLightData sun, RenderQualityOptions options)
        {
            Options = options;
            _sun = sun;

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "MN_RenderQualityProfile";
            _shadows = _profile.Add<HDShadowSettings>(true);
            _contact = _profile.Add<ContactShadows>(true);
            _ao = _profile.Add<ScreenSpaceAmbientOcclusion>(true);
            _micro = _profile.Add<MicroShadowing>(true);

            var v = new GameObject("QualityVolume");
            v.transform.SetParent(transform, false);
            var volume = v.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 6f;         // above the Sun volumes' defaults, below the Sky's own (20 and 21), which hold no shadow or occlusion settings
            volume.sharedProfile = _profile;

            Apply();
            var p = Asset?.currentPlatformRenderPipelineSettings;
            MnLog.Info($"RENDERQ on: sun shadows {options.ShadowCascades}x{options.ShadowResolution} to {options.ShadowDistance:0} m " +
                       $"(asset cap {(p?.hdShadowInitParams.maxDirectionalShadowMapResolution ?? 0)}, filter {(p?.hdShadowInitParams.shadowFilteringQuality.ToString() ?? "?")}), " +
                       $"contact {options.ContactShadows}, AO {(options.AmbientOcclusion ? (options.RayTracedAo ? "ray traced" : "screen space") : "off")}, micro {options.MicroShadows}, " +
                       $"sun shadows {(options.RayTracedSunShadows ? "ray traced" : "maps")}, ray tracing {(RayTracingAvailable ? "available" : "not available")}");
        }

        /// <summary>True when the pipeline asset supports ray tracing and the device can do it (Direct3D 12 on a DXR GPU).</summary>
        public static bool RayTracingAvailable =>
            SystemInfo.supportsRayTracing && Asset != null && Asset.currentPlatformRenderPipelineSettings.supportRayTracing;

        static HDRenderPipelineAsset Asset => GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;

        /// <summary>Push <see cref="Options"/> into the volume and the Sun. Call after changing an option.</summary>
        public void Apply()
        {
            var o = Options;

            // ---- the Sun's cascades: set from the far end inward so each value is inside the clamp of its neighbours
            int cascades = Mathf.Clamp(o.ShadowCascades, 1, 4);
            _shadows.maxShadowDistance.Override(o.ShadowDistance);
            _shadows.cascadeShadowSplitCount.Override(cascades);
            _shadows.cascadeShadowSplit2.Override(1f);
            _shadows.cascadeShadowSplit1.Override(1f);
            _shadows.cascadeShadowSplit0.Override(Mathf.Clamp(o.ShadowSplit0, 0.001f, 0.9f));
            _shadows.cascadeShadowSplit1.Override(Mathf.Clamp(o.ShadowSplit1, o.ShadowSplit0, 0.95f));
            _shadows.cascadeShadowSplit2.Override(Mathf.Clamp(o.ShadowSplit2, o.ShadowSplit1, 0.99f));
            _shadows.cascadeShadowBorder0.Override(o.ShadowCascadeBorder);
            _shadows.cascadeShadowBorder1.Override(o.ShadowCascadeBorder);
            _shadows.cascadeShadowBorder2.Override(o.ShadowCascadeBorder);
            _shadows.cascadeShadowBorder3.Override(0f);

            // ---- contact shadows: the volume sets the reach, the light says whether it takes part (it does not by default)
            _contact.quality.Override(ScalableSettingLevelParameter.LevelCount);
            _contact.enable.Override(o.ContactShadows);
            _contact.length.Override(o.ContactShadowLength);
            _contact.opacity.Override(o.ContactShadowOpacity);
            _contact.maxDistance.Override(o.ContactShadowDistance);
            _contact.fadeDistance.Override(o.ContactShadowFade);
            _contact.sampleCount = 16;

            // ---- ambient occlusion
            _ao.quality.Override(ScalableSettingLevelParameter.LevelCount);
            _ao.rayTracing.Override(o.RayTracedAo && RayTracingAvailable);
            _ao.intensity.Override(o.AmbientOcclusion ? o.AoIntensity : 0f);
            _ao.radius.Override(o.AoRadius);
            _ao.directLightingStrength.Override(o.AoDirectLighting);
            _ao.specularOcclusion.Override(o.AoSpecularOcclusion);
            _ao.temporalAccumulation.Override(true);
            _ao.stepCount = o.AoSteps;
            _ao.fullResolution = o.AoFullResolution;
            _ao.maximumRadiusInPixels = o.AoMaxRadiusPixels;
            _ao.bilateralUpsample = true;
            _ao.directionCount = 2;
            _ao.rayLength = o.RayTracedAoLength;
            _ao.sampleCount = o.RayTracedAoSamples;
            _ao.denoise = true;
            _ao.denoiserRadius = 0.5f;

            // ---- micro shadows
            _micro.enable.Override(o.MicroShadows);
            _micro.opacity.Override(o.MicroShadowOpacity);

            // ---- the Sun itself
            if (_sun != null)
            {
                _sun.SetShadowResolution(o.ShadowResolution);
                _sun.useContactShadow.@override = o.ContactShadows;
                bool rt = o.RayTracedSunShadows && RayTracingAvailable;
                _sun.useScreenSpaceShadows = rt;
                _sun.useRayTracedShadows = rt;
            }
        }

        void OnDestroy()
        {
            if (_profile != null) Destroy(_profile);
        }
    }
}
