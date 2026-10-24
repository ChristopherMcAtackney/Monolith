using Monolith.Core;
using Monolith.Hud;
using Monolith.Loading;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.Game
{
    /// <summary>
    /// The loading screen held still for a capture: <c>"scene": "loading"</c> with <c>sceneOptions</c> <c>{ "progress": 0.42, "status": "Loading ships", "time": 4.0, "style": "halo", "splash": "oort_splash" }</c>.
    /// A real start cannot be photographed (the window is not shown while a capture runs), so this draws the very same screen (<see cref="LoadingScreen.DrawFrame"/>) on the capture camera at a chosen
    /// progress, status and moment of its settle, in any style and at any size and shape of screen. <c>"keys": [[t, progress, "status", settleTime], ...]</c> moves it along as the capture's clock runs, so one
    /// plan can photograph several moments (each still's warm-up picks its moment).
    /// </summary>
    public static class LoadingTestWorld
    {
        public const string SceneName = "loading";

        public static bool TryBuild(GameWorld world, Transform parent, string scene, string optionsJson = null)
        {
            if (scene != SceneName) return false;
            var root = new GameObject("LoadingTestWorld").transform;
            root.SetParent(parent, false);
            var go = new GameObject("MainCamera") { tag = "MainCamera" };
            go.transform.SetParent(root, false);
            var cam = go.AddComponent<Camera>();
            cam.allowHDR = false;
            var hd = go.AddComponent<HDAdditionalCameraData>();
            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            hd.backgroundColorHDR = Color.black;
            world.MainCamera = cam;

            var o = string.IsNullOrEmpty(optionsJson) ? new JObject() : JObject.Parse(optionsJson);
            string style = (string)o["style"];
            if (!string.IsNullOrEmpty(style)) HudStyles.Set(style);
            var screen = LoadingScreen.Show(root, (string)o["splash"]);
            screen.FixedProgress = (float)(o["progress"] ?? 0.4f);
            screen.FixedStatus = (string)o["status"] ?? "Loading ships";
            screen.FixedTime = (float)(o["time"] ?? 5f);
            screen.AttachTo(cam);
            if (o["keys"] is JArray keys && keys.Count > 0) go.AddComponent<Keys>().Init(screen, keys);
            return true;
        }

        /// <summary>Steps the screen through the plan's keys by the capture clock: the last key whose time has come.</summary>
        sealed class Keys : MonoBehaviour
        {
            LoadingScreen _screen;
            readonly System.Collections.Generic.List<(float t, float p, string status, float settle)> _k = new System.Collections.Generic.List<(float, float, string, float)>();

            public void Init(LoadingScreen screen, JArray keys)
            {
                _screen = screen;
                foreach (JArray k in keys) _k.Add(((float)k[0], (float)k[1], (string)k[2], k.Count > 3 ? (float)k[3] : -1f));
                _k.Sort((a, b) => a.t.CompareTo(b.t));
                Apply();
            }

            void Update() => Apply();

            void Apply()
            {
                if (_screen == null) return;
                int at = 0;
                for (int i = 0; i < _k.Count; i++) if (_k[i].t <= Time.time + 1e-4f) at = i;
                var k = _k[at];
                _screen.FixedProgress = k.p; _screen.FixedStatus = k.status;
                if (k.settle >= 0f) _screen.FixedTime = k.settle + (Time.time - k.t);
            }
        }
    }
}
