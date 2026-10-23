using System.IO;
using Monolith.Core;
using Monolith.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Monolith.EditorTools
{
    /// <summary>
    /// Idempotent project configuration, safe to re-run any time:
    ///   tools\unity.ps1 Monolith.EditorTools.Setup.Project
    /// HDRP asset and global settings, linear colour, the Input System, player settings (1920x1080 windowed, Windows x64),
    /// the smoke materials, and the empty Boot scene as the only scene in the build.
    /// </summary>
    public static class Setup
    {
        public const string Root = "Assets/Monolith";
        public const string SettingsDir = Root + "/Settings";
        public const string HdrpAssetPath = SettingsDir + "/Monolith_HDRP.asset";
        public const string SmokeMaterialsDir = Root + "/Resources/Smoke";
        public const string FlightMaterialsDir = Root + "/Resources/Flight";
        public const string BootScenePath = Root + "/Scenes/" + GameBootstrap.BootSceneName + ".unity";

        static readonly string[] Folders =
        {
            Root + "/Scripts/Core", Root + "/Scripts/Capture", Root + "/Scripts/Game", Root + "/Editor",
            Root + "/Scenes", SettingsDir, SmokeMaterialsDir, FlightMaterialsDir,
        };

        public static void Project() => Batch.Run("Setup.Project", ProjectStep);

        /// <summary>Setup and a player build in one Unity session.</summary>
        public static void All() => Batch.Chain("Setup.All", ProjectStep, Build.BuildPlayerStep);

        public static void ProjectStep()
        {
            foreach (var f in Folders) Directory.CreateDirectory(f);
            AssetDatabase.Refresh();

            var hdrp = Hdrp.EnsureAsset();
            Hdrp.Assign(hdrp);
            var globals = Hdrp.EnsureGlobalSettings();

            // Smoke scene surfaces (the scene itself is built from code at boot, see Game/SmokeWorld).
            Hdrp.EnsureLit(SmokeMaterialsDir + "/MN_SmokeSphere.mat", new Color(0.72f, 0.72f, 0.74f), 0f, 0.55f);
            Hdrp.EnsureLit(SmokeMaterialsDir + "/MN_SmokeBox.mat", new Color(0.55f, 0.36f, 0.22f), 0f, 0.40f);
            // The flight playtest's base materials (Game/PlaceholderShips makes its ship materials from instances of them).
            FlightAssets.Ensure(FlightMaterialsDir);

            EnsureBootScene();
            ConfigurePlayer();

            AssetDatabase.SaveAssets();
            MnLog.Info($"SETUP pipeline={AssetDatabase.GetAssetPath(GraphicsSettings.defaultRenderPipeline)} globals={AssetDatabase.GetAssetPath(globals)} " +
                       $"colorSpace={PlayerSettings.colorSpace} input=newOnly target={EditorUserBuildSettings.activeBuildTarget} " +
                       $"scenes=[{string.Join(", ", System.Array.ConvertAll(EditorBuildSettings.scenes, s => s.path))}]");
        }

        static void EnsureBootScene()
        {
            if (!File.Exists(BootScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, BootScenePath);
                MnLog.Info("created " + BootScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootScenePath, true) };
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "ChristopherMcAtackney";
            PlayerSettings.productName = "Monolith";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.forceSingleInstance = false;
            PlayerSettings.enableFrameTimingStats = true;     // capture perf runs read GPU time from it
            PlayerSettings.SplashScreen.show = false;         // ignored on Personal licences; harmless
            PlayerSettings.SplashScreen.showUnityLogo = false;

            // Input System package only (0 old, 1 new, 2 both): code written for this project uses the package.
            var ps = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            var handler = ps.FindProperty("activeInputHandler") ?? throw new System.Exception("PlayerSettings has no activeInputHandler (Unity version drift?)");
            handler.intValue = 1;
            ps.ApplyModifiedPropertiesWithoutUndo();

            EditorSettings.serializationMode = SerializationMode.ForceText;
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            EditorUserBuildSettings.development = false;
        }
    }
}
