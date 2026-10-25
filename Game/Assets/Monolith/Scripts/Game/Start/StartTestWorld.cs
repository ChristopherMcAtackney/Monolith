using Monolith.Core;
using Monolith.Hud;
using Monolith.Start;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.Game
{
    /// <summary>
    /// The start screen held still for a capture: <c>"scene": "start"</c> with <c>sceneOptions</c> <c>{ "style": "halo", "scenario": "oort", "level": "Hard", "focus": "level", "fade": 1.0 }</c>. A real start cannot be
    /// photographed (the window is not shown while a capture runs), so this draws the very same screen on the capture camera, in any style and at any size and shape of screen.
    /// </summary>
    public static class StartTestWorld
    {
        public const string SceneName = "start";

        public static bool TryBuild(GameWorld world, Transform parent, string scene, string optionsJson = null)
        {
            if (scene != SceneName) return false;
            var root = new GameObject("StartTestWorld").transform;
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
            var menu = StartCatalog.Build(useSaved: false);
            if (string.Equals((string)o["scenario"], "quit", System.StringComparison.OrdinalIgnoreCase)) menu.Index = menu.QuitIndex; else menu.Select((string)o["scenario"]);
            menu.SelectLevel((string)o["level"]);
            menu.LevelFocus = string.Equals((string)o["focus"], "level", System.StringComparison.OrdinalIgnoreCase) && menu.LevelApplies;
            var screen = StartScreen.Create(menu, false);
            screen.transform.SetParent(root, false);
            screen.FixedFade = (float)(o["fade"] ?? 1f);
            screen.AttachTo(cam);
            return true;
        }
    }
}
