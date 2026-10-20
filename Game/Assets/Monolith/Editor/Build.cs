using System;
using System.IO;
using System.Linq;
using Monolith.Core;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Monolith.EditorTools
{
    public static class Build
    {
        public static string OutPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Win", "Monolith.exe"));

        /// <summary>tools\unity.ps1 Monolith.EditorTools.Build.Player   (add -- -mnDev for a development build)</summary>
        public static void Player() => Batch.Run("Build.Player", BuildPlayerStep);

        public static void BuildPlayerStep()
        {
            // The build is exactly one scene: the empty Boot scene. Everything else is constructed from code.
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length != 1 || scenes[0] != Setup.BootScenePath)
                throw new Exception($"build settings must hold only {Setup.BootScenePath} (found: {string.Join(", ", scenes)}); run Setup.Project");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);

            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutPath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = CommandLine.Has("-mnDev") ? BuildOptions.Development : BuildOptions.None,
            };
            MnLog.Info($"BUILD start: {scenes.Length} scene(s): {string.Join(", ", scenes.Select(Path.GetFileNameWithoutExtension))}");
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            foreach (var step in report.steps)
            foreach (var m in step.messages)
                if (m.type == LogType.Error || m.type == LogType.Exception) MnLog.Error($"build: {m.content}");
            MnLog.Info($"BUILD {s.result} in {s.totalTime.TotalSeconds:0.0}s, {s.totalSize / 1048576.0:0.0} MB, {s.totalErrors} errors, {s.totalWarnings} warnings -> {OutPath}");
            if (s.result != BuildResult.Succeeded) throw new Exception($"player build {s.result}");
        }
    }
}
