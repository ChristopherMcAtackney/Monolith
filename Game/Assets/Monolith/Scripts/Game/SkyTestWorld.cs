using Monolith.Core;
using Monolith.Sky;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.Game
{
    /// <summary>
    /// A test scene for the sky: a camera, a plain grey hull (a box and a sphere) at the origin, and <see cref="SkyRig"/> switched on.
    /// A capture plan asks for it with <c>"scene": "sky"</c> (gauntlet/shots/sky.json) and may set any <see cref="SkyOptions"/> field in <c>"sceneOptions"</c>. <c>"scene": "sky_off"</c> is the same scene
    /// with the sky not drawn (black background), which is the baseline for measuring what the sky costs. Stand-in until the
    /// playtest scene switches the sky on itself with one <c>SkyRig.Enable(camera)</c>.
    /// </summary>
    public static class SkyTestWorld
    {
        public const string SceneOn = "sky";
        public const string SceneOff = "sky_off";
        public const string HullMaterial = "MonolithSky/MN_TestHull";
        public const string HullLightsMaterial = "MonolithSky/MN_TestHullLights";

        public static SkyRig Rig { get; private set; }

        /// <summary>Builds the scene when <paramref name="scene"/> names it; returns false (and builds nothing) otherwise.</summary>
        public static bool TryBuild(GameWorld world, Transform parent, string scene, string optionsJson = null)
        {
            bool on = scene == SceneOn, off = scene == SceneOff;
            if (!on && !off) return false;

            var root = new GameObject("SkyTestWorld").transform;
            root.SetParent(parent, false);

            var cam = MakeCamera(root);
            world.MainCamera = cam;
            var options = new SkyOptions();
            if (!string.IsNullOrEmpty(optionsJson)) JsonConvert.PopulateObject(optionsJson, options);    // the plan's "sceneOptions" override SkyOptions fields by name
            Rig = SkyRig.Enable(cam, root, options);
            if (off) cam.GetComponent<HDAdditionalCameraData>().clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;

            var mat = Resources.Load<Material>(HullMaterial);
            if (mat == null) throw new System.InvalidOperationException($"Resources/{HullMaterial}.mat is missing: run tools\\unity.ps1 Monolith.EditorTools.SkySetup.Run");
            MakeShape("HullBody", PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(30f, 6f, 8f), mat, root);
            MakeShape("HullDome", PrimitiveType.Sphere, new Vector3(-9f, 5f, 0f), new Vector3(9f, 9f, 9f), mat, root);
            MakeShape("HullFin", PrimitiveType.Cube, new Vector3(10f, 6.5f, 0f), new Vector3(8f, 7f, 1.2f), mat, root);

            // Emissive light strips (window rows and a fin light) on both long sides, so bloom on lights can be judged next to the Sun.
            var lights = Resources.Load<Material>(HullLightsMaterial);
            if (lights == null) throw new System.InvalidOperationException($"Resources/{HullLightsMaterial}.mat is missing: run tools/unity.ps1 Monolith.EditorTools.SkySetup.Run");
            foreach (float z in new[] { -4.04f, 4.04f })
                for (int i = 0; i < 6; i++)
                    MakeShape("HullLight", PrimitiveType.Cube, new Vector3(-12f + i * 2.2f, 0.4f, z), new Vector3(1.2f, 0.5f, 0.1f), lights, root);
            MakeShape("HullFinLight", PrimitiveType.Cube, new Vector3(10f, 10.2f, 0f), new Vector3(0.6f, 0.6f, 1.4f), lights, root);
            return true;
        }

        static Camera MakeCamera(Transform root)
        {
            var go = new GameObject("MainCamera") { tag = "MainCamera" };
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(0f, 0f, -90f);
            go.transform.rotation = Quaternion.identity;

            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100000f;
            cam.allowHDR = true;

            var hd = go.AddComponent<HDAdditionalCameraData>();
            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            hd.backgroundColorHDR = Color.black;
            hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            hd.stopNaNs = true;
            hd.dithering = true;
            return cam;
        }

        static void MakeShape(string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, Transform root)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            Object.Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }
}
