using Monolith.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.Game
{
    /// <summary>
    /// The smoke scene, built from code: a space camera, one hard sun, and a sphere and a box to catch its light.
    /// Stand-in until the Render system owns the look; it exists to prove the HDRP set-up, the capture harness and the
    /// sim clock end to end. Materials are assets made by Setup.Project (Resources/Smoke) so the player build keeps
    /// their shaders.
    /// </summary>
    public static class SmokeWorld
    {
        /// <summary>The sun's illuminance. Real sunlight at Earth is ~100,000 lux; readability beats realism out here.</summary>
        public const float SunLux = 100000f;
        /// <summary>Fixed exposure (EV100) at which a mid-grey surface in full sun lands at a mid tone.</summary>
        public const float ExposureEv100 = 14f;
        /// <summary>A faint cold fill (about 2% of the sun) so the unlit side of a ship still reads against black space.</summary>
        public static readonly Color StarlightColor = new Color(0.45f, 0.52f, 0.70f);
        public const float StarlightNits = 1000f;

        public static Transform Build(GameWorld world, Transform parent)
        {
            var root = new GameObject("SmokeWorld").transform;
            root.SetParent(parent, false);

            world.MainCamera = MakeCamera(root);
            MakeSun(root);
            MakeVolume(root);

            var sphere = MakeShape("Sphere", PrimitiveType.Sphere, "Smoke/MN_SmokeSphere", new Vector3(-1.7f, 0f, 0f), Vector3.one * 2f, root);
            var box = MakeShape("Box", PrimitiveType.Cube, "Smoke/MN_SmokeBox", new Vector3(1.9f, 0f, 0f), Vector3.one * 1.7f, root);

            var spin = box.gameObject.AddComponent<SmokeSpinner>();
            spin.Init(world, Quaternion.Euler(18f, 25f, 0f), new Vector3(0f, 20f, 0f));
            world.Sim.Register(spin);
            return root;
        }

        static Camera MakeCamera(Transform root)
        {
            var go = new GameObject("MainCamera") { tag = "MainCamera" };
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(0f, 1.6f, -9f);
            go.transform.rotation = Quaternion.Euler(9f, 0f, 0f);

            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100000f;      // capital ships and planets: far is cheap with HDRP's reversed-Z depth
            cam.allowHDR = true;

            var hd = go.AddComponent<HDAdditionalCameraData>();
            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;     // black space, no sky to render
            hd.backgroundColorHDR = Color.black;
            hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            hd.stopNaNs = true;
            hd.dithering = true;
            return cam;
        }

        static void MakeSun(Transform root)
        {
            var go = new GameObject("Sun");
            go.transform.SetParent(root, false);
            go.transform.rotation = Quaternion.Euler(30f, -60f, 0f);   // from the right, above and a little behind the camera, so the sphere shows a terminator

            var hdl = go.AddHDLight(HDLightTypeAndShape.Directional);
            hdl.SetIntensity(SunLux, LightUnit.Lux);
            hdl.angularDiameter = 0.1f;      // a hard sun: tiny disc, crisp shadows
            hdl.EnableShadows(true);
        }

        static void MakeVolume(Transform root)
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "MN_SpaceProfile";

            // The sky is never drawn (the camera clears to black); it only supplies the starlight fill.
            var env = profile.Add<VisualEnvironment>(true);
            env.skyType.Override((int)SkyType.Gradient);
            env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            var sky = profile.Add<GradientSky>(true);
            sky.top.Override(StarlightColor);
            sky.middle.Override(StarlightColor);
            sky.bottom.Override(StarlightColor);
            sky.skyIntensityMode.Override(SkyIntensityMode.Multiplier);
            sky.multiplier.Override(StarlightNits);

            var exposure = profile.Add<Exposure>(true);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(ExposureEv100);

            var tonemap = profile.Add<Tonemapping>(true);
            tonemap.mode.Override(TonemappingMode.ACES);

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.12f);

            var go = new GameObject("SpaceVolume");
            go.transform.SetParent(root, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;
        }

        static Transform MakeShape(string name, PrimitiveType type, string material, Vector3 pos, Vector3 scale, Transform root)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            Object.Destroy(go.GetComponent<Collider>());

            var mat = Resources.Load<Material>(material);
            if (mat == null) throw new System.InvalidOperationException($"Resources/{material} is missing: run tools\\unity.ps1 Monolith.EditorTools.Setup.Project, then build");
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }
    }

    /// <summary>Turns a shape at a fixed rate on the sim clock and draws it interpolated: the pattern Flight will use for ships.</summary>
    public sealed class SmokeSpinner : MonoBehaviour, ISimSystem
    {
        GameWorld _world;
        Quaternion _base, _previous, _current;
        Vector3 _degPerSec;

        public int Order => SimOrder.Flight;

        public void Init(GameWorld world, Quaternion baseRotation, Vector3 degPerSec)
        {
            _world = world;
            _base = _previous = _current = baseRotation;
            _degPerSec = degPerSec;
            transform.rotation = baseRotation;
        }

        public void Step(in SimFrame frame)
        {
            _previous = _current;
            _current = _base * Quaternion.Euler(_degPerSec * (frame.Time + frame.Dt));
        }

        void LateUpdate() => transform.rotation = Quaternion.Slerp(_previous, _current, _world.Alpha);
    }
}
