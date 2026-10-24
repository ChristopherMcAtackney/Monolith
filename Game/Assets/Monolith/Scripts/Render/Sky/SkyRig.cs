using Monolith.Core;
using Monolith.Look;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.Sky
{
    /// <summary>
    /// Switches the Monolith sky on for a scene in one call: <c>SkyRig.Enable(camera)</c>. It makes the Sun (an HDRP directional
    /// light, the scene's hard key light), a global volume that selects the sky and sets the ambient fill, and (optionally)
    /// a second volume with a fixed exposure, ACES tonemapping and bloom. It also points the camera at the sky instead of
    /// clearing to black. Change <see cref="Options"/> and call <see cref="Apply"/> to retune at run time.
    /// Everything it creates is a child of its own GameObject, so destroying that (or calling <see cref="Disable"/>) removes the sky.
    /// </summary>
    public sealed class SkyRig : MonoBehaviour
    {
        public SkyOptions Options { get; private set; }
        public Camera Camera { get; private set; }
        /// <summary>The Sun's directional light.</summary>
        public Light Sun { get; private set; }
        /// <summary>World-space unit vector from the viewer toward the Sun.</summary>
        public Vector3 SunDirection { get; private set; }

        HDAdditionalLightData _sunData;
        MonolithSky _sky;
        Exposure _exposure;
        Tonemapping _tonemapping;
        Bloom _bloom;
        ScreenSpaceReflection _ssr;
        VolumeProfile _skyProfile, _lookProfile;
        Volume _skyVolume, _lookVolume;
        CameraLook _look;
        RenderQuality.RenderQuality _quality;
        HDAdditionalCameraData _cameraData;
        HDAdditionalCameraData.ClearColorMode _previousClear;

        /// <summary>Turn the sky on for this camera. Returns the rig; keep it if you want to retune or turn the sky off later.</summary>
        public static SkyRig Enable(Camera camera, Transform parent = null, SkyOptions options = null)
        {
            if (camera == null) throw new System.ArgumentNullException(nameof(camera));
            var go = new GameObject("MonolithSky");
            if (parent != null) go.transform.SetParent(parent, false);
            var rig = go.AddComponent<SkyRig>();
            rig.Build(camera, options ?? new SkyOptions());
            return rig;
        }

        void Build(Camera camera, SkyOptions options)
        {
            Camera = camera;
            Options = options;

            _cameraData = camera.GetComponent<HDAdditionalCameraData>() ?? camera.gameObject.AddComponent<HDAdditionalCameraData>();
            _previousClear = _cameraData.clearColorMode;
            _cameraData.clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;
            camera.allowHDR = true;

            // The Sun.
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform, false);
            _sunData = sunGo.AddHDLight(HDLightTypeAndShape.Directional);
            Sun = sunGo.GetComponent<Light>();

            // The shared render-quality profile: the Sun's cascades and shadow resolution, contact shadows, ambient occlusion, micro shadows.
            _quality = RenderQuality.RenderQuality.Enable(transform, _sunData, options.Quality);

            // The sky: a volume that picks the Monolith sky and lights ships from it.
            _skyProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _skyProfile.name = "MN_SkyProfile";
            var env = _skyProfile.Add<VisualEnvironment>(true);
            env.skyType.Override(MonolithSky.UniqueId);
            env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            _sky = _skyProfile.Add<MonolithSky>(true);
            _skyVolume = MakeVolume("SkyVolume", _skyProfile, 20f);

            // Exposure and look.
            if (options.ConfigureExposure || options.ConfigureLook)
            {
                _lookProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                _lookProfile.name = "MN_SkyLookProfile";
                if (options.ConfigureExposure) _exposure = _lookProfile.Add<Exposure>(true);
                if (options.ConfigureLook)
                {
                    _tonemapping = _lookProfile.Add<Tonemapping>(true);
                    _bloom = _lookProfile.Add<Bloom>(true);
                    _ssr = _lookProfile.Add<ScreenSpaceReflection>(true);
                }
                _lookVolume = MakeVolume("LookVolume", _lookProfile, 21f);
            }

            // The lens and film: the Sun's glare needs to know where the Sun is, so the rig owns it.
            if (options.CameraLook) _look = CameraLook.Enable(camera, transform, options.Look);

            Apply();
            MnLog.Info($"SKY on: sun {SunDirection.x:0.000},{SunDirection.y:0.000},{SunDirection.z:0.000}, " +
                       $"{(options.ConfigureExposure ? options.Exposure + " EV " + options.ExposureEv100 : "scene exposure")}, limit V {options.LimitMagnitude}");
        }

        Volume MakeVolume(string name, VolumeProfile profile, float priority)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = priority;
            v.sharedProfile = profile;
            return v;
        }

        /// <summary>Push <see cref="Options"/> into the light and the volumes. Call after changing an option.</summary>
        public void Apply()
        {
            var o = Options;

            SunDirection = SkyMath.EclipticToWorld(o.SunEclipticLongitude, o.SunEclipticLatitude, o.SkyRotationEuler);
            Sun.transform.rotation = Quaternion.LookRotation(-SunDirection, Vector3.up);
            Sun.color = o.SunColor;
            _sunData.SetIntensity(o.SunLux, LightUnit.Lux);
            _sunData.angularDiameter = o.SunLightAngularDiameterDeg;
            _sunData.EnableShadows(o.SunShadows);
            if (_quality != null) _quality.Apply();

            float nitsPerUnit = 1.2f * Mathf.Pow(2f, o.ExposureEv100);
            float lum = Mathf.Max(0.2126f * o.SunColor.r + 0.7152f * o.SunColor.g + 0.0722f * o.SunColor.b, 1e-4f);   // the Sun point is luminance-normalised: its energy sets the brightness
            _sky.sunDirection.Override(SunDirection);
            _sky.skyRotationEuler.Override(o.SkyRotationEuler);
            _sky.sunColor.Override(new Color(o.SunColor.r / lum, o.SunColor.g / lum, o.SunColor.b / lum, 1f));
            _sky.sunCore.Override(new Vector4(o.SunCore, o.SunCoreSigmaPx, 0f, 0f));
                _sky.starParams.Override(new Vector4(o.StarGain, o.StarGamma, o.StarReferenceMagnitude, o.LimitMagnitude));
            _sky.starShape.Override(new Vector4(o.StarSigmaPx, o.StarSaturation, o.StarHaloFraction, 0f));
            _sky.starExposure.Override(new Vector4(o.StarExposureL0, o.StarExposurePower, o.MilkyWayExposure, o.StarThinning));
            _sky.starVariety.Override(new Vector4(o.StarBrightnessJitter, o.StarSizeJitter, o.StarGlintRate, o.StarGlintReachPx));
            _sky.starHalo.Override(new Vector4(o.StarHaloSizeDimPx, o.StarHaloSizeBrightPx, o.StarStreak, o.StarStreakReachPx));
            _sky.milkyWay.Override(new Vector4(o.MilkyWayGain, o.MilkyWayContrast, o.MilkyWaySaturation, 0f));
            _sky.milkyWayTint.Override(o.MilkyWayTint);
            _sky.blackLift.Override(o.BlackLift);
            _sky.ambientColor.Override(o.AmbientColor);
            _sky.ambientNits.Override(o.AmbientNits);
            _sky.ambientShape.Override(new Vector4(o.AmbientAwayFromSun, o.AmbientMilkyWay, 0f, 0f));
            _sky.nitsPerUnit.Override(nitsPerUnit);

            if (_look != null)
            {
                _look.SetSun(SunDirection, new Color(o.SunColor.r / lum, o.SunColor.g / lum, o.SunColor.b / lum, 1f), nitsPerUnit);
                _look.Apply();
            }

            if (_exposure != null)
            {
                if (o.Exposure == SkyExposure.Fixed)
                {
                    _exposure.mode.Override(ExposureMode.Fixed);
                    _exposure.fixedExposure.Override(o.ExposureEv100);
                }
                else
                {
                    _exposure.mode.Override(ExposureMode.Automatic);
                    _exposure.limitMin.Override(o.AutoExposureMinEv);
                    _exposure.limitMax.Override(o.AutoExposureMaxEv);
                    _exposure.meteringMode.Override(MeteringMode.CenterWeighted);
                }
            }
            if (_tonemapping != null) _tonemapping.mode.Override(TonemappingMode.ACES);
            if (_ssr != null)
            {
                _ssr.enabled.Override(o.ScreenSpaceReflections);
                _ssr.reflectSky.Override(true);
                _ssr.minSmoothness = 0.5f;
                _ssr.smoothnessFadeStart = 0.7f;
                _ssr.depthBufferThickness.Override(0.2f);
            }
            if (_bloom != null)
            {
                _bloom.intensity.Override(o.BloomIntensity);
                _bloom.threshold.Override(o.BloomThreshold);
                _bloom.scatter.Override(o.BloomScatter);
            }
        }

        // The cabin (Monolith.Cabin, through GameWorld.Cabin): while the pilot is up the eye opens to the dim cabin (the exposure drops, so lit things outside burn out as they
        // would), and the sky's cold fill is cut for the lit scene (it would flood the cabin). The sky's own look (stars, Milky Way) is exposure-compensated below, so space
        // keeps its brightness on screen.
        float _cabinShift = float.NaN, _cabinAmbient = float.NaN;

        void LateUpdate()
        {
            var cab = GameWorld.Current?.Cabin;
            float shift = cab != null ? cab.ExposureShift : 0f, amb = cab != null ? cab.AmbientScale : 1f;
            if (Mathf.Approximately(shift, _cabinShift) && Mathf.Approximately(amb, _cabinAmbient)) return;
            if (float.IsNaN(_cabinShift) && cab == null) { _cabinShift = shift; _cabinAmbient = amb; return; }
            _cabinShift = shift; _cabinAmbient = amb;
            ApplyCabinLook(shift, amb);
        }

        void ApplyCabinLook(float shift, float ambientScale)
        {
            var o = Options;
            float ev = o.ExposureEv100 + (o.Exposure == SkyExposure.Fixed ? shift : 0f);
            float nitsPerUnit = 1.2f * Mathf.Pow(2f, ev);
            float lum = Mathf.Max(0.2126f * o.SunColor.r + 0.7152f * o.SunColor.g + 0.0722f * o.SunColor.b, 1e-4f);
            _sky.nitsPerUnit.Override(nitsPerUnit);
            _sky.ambientNits.Override(o.AmbientNits * ambientScale);
            if (_look != null) { _look.SetSun(SunDirection, new Color(o.SunColor.r / lum, o.SunColor.g / lum, o.SunColor.b / lum, 1f), nitsPerUnit); _look.Apply(); }
            if (_exposure != null && o.Exposure == SkyExposure.Fixed) _exposure.fixedExposure.Override(ev);
        }

        /// <summary>Remove the sky and put the camera back to clearing to black.</summary>
        public void Disable()
        {
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_cameraData != null) _cameraData.clearColorMode = _previousClear;
            if (_skyProfile != null) Destroy(_skyProfile);
            if (_lookProfile != null) Destroy(_lookProfile);
        }
    }
}
