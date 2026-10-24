using System;
using System.IO;
using System.Reflection;
using Monolith.Core;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.EditorTools
{
    /// <summary>
    /// Creates and wires HDRP by script, the work the HDRP Wizard does by hand: the pipeline asset, the global settings,
    /// and the graphics/quality assignments. Idempotent, and it throws if a serialized field it sets has moved
    /// (the HDRP version is pinned at 14.0.11, so drift means a package change).
    /// </summary>
    public static class Hdrp
    {
        public static HDRenderPipelineAsset EnsureAsset()
        {
            var asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(Setup.HdrpAssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
                AssetDatabase.CreateAsset(asset, Setup.HdrpAssetPath);
                MnLog.Info("created " + Setup.HdrpAssetPath);
            }

            var so = new SerializedObject(asset);
            // Trim what a space game does not use (fewer shader variants, faster builds); everything else stays at HDRP's defaults.
            Set(so, "m_RenderPipelineSettings.supportSubsurfaceScattering", false);
            Set(so, "m_RenderPipelineSettings.supportVolumetrics", false);
            // Screen-space reflections: polished metal (the corvette's chrome) reflects the lit plates next to it. Used through a volume override (SkyRig).
            Set(so, "m_RenderPipelineSettings.supportSSR", true);
            // Sun shadows (RenderQuality): the cascade resolution cap (HDRP's 2048 would silently cut the 4096 the profile asks for) and the high filter (PCSS on directional lights).
            Set(so, "m_RenderPipelineSettings.hdShadowInitParams.maxDirectionalShadowMapResolution", 4096);
            Set(so, "m_RenderPipelineSettings.hdShadowInitParams.shadowFilteringQuality", (int)HDShadowFilteringQuality.High);
            // Ray tracing (RenderQuality's ray-traced AO and Sun shadows, Direct3D 12 on a DXR GPU only; the asset flag costs nothing elsewhere).
            Set(so, "m_RenderPipelineSettings.supportRayTracing", true);
            Set(so, "m_RenderPipelineSettings.hdShadowInitParams.supportScreenSpaceShadows", true);    // ray-traced Sun shadows are screen-space shadows
            // Light layers: the cabin is lit by its own lights (a share of the Sun comes in through the canopy as a light of the cabin's own), not by the Sun itself.
            Set(so, "m_RenderPipelineSettings.supportLightLayers", true);
            // Full float colour: dark gradients and bright bloom are the whole picture here, so no 11-bit shortcut.
            Set(so, "m_RenderPipelineSettings.colorBufferFormat", (int)RenderPipelineSettings.ColorBufferFormat.R16G16B16A16);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        /// <summary>Make the asset the project's pipeline, in Graphics settings and in every quality level.</summary>
        public static void Assign(HDRenderPipelineAsset asset)
        {
            GraphicsSettings.defaultRenderPipeline = asset;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        /// <summary>
        /// HDRP keeps its resources and default volume in a global settings asset. HDRP creates one itself after
        /// the first domain reload; this makes sure it exists and is registered, whichever happened first.
        /// </summary>
        public static RenderPipelineGlobalSettings EnsureGlobalSettings()
        {
            // The class is internal to HDRP, so reach it by name.
            var type = typeof(HDRenderPipeline).Assembly.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings")
                       ?? throw new MissingMemberException("HDRenderPipelineGlobalSettings moved (HDRP version drift)");
            var ensure = type.GetMethod("Ensure", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null)
                         ?? throw new MissingMethodException("HDRenderPipelineGlobalSettings.Ensure(bool) moved (HDRP version drift)");
            var settings = ensure.Invoke(null, new object[] { true }) as RenderPipelineGlobalSettings;
            if (settings == null || GraphicsSettings.GetSettingsForRenderPipeline<HDRenderPipeline>() == null)
                throw new InvalidOperationException("HDRP global settings could not be created or registered");
            // The ray tracing shaders and resources the asset's "Realtime Ray Tracing" needs (the asset inspector does this when the box is ticked).
            var rt = type.GetMethod("EnsureRayTracingResources", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null)
                     ?? throw new MissingMethodException("HDRenderPipelineGlobalSettings.EnsureRayTracingResources(bool) moved (HDRP version drift)");
            rt.Invoke(settings, new object[] { false });
            EditorUtility.SetDirty(settings);
            return settings;
        }

        /// <summary>An HDRP/Lit material asset with the given surface, with its keywords set up the way HDRP's inspector would.</summary>
        public static Material EnsureLit(string path, Color baseColor, float metallic, float smoothness)
        {
            var shader = Shader.Find("HDRP/Lit") ?? throw new InvalidOperationException("shader HDRP/Lit not found");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
                MnLog.Info("created " + path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", baseColor);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            HDShaderUtils.ResetMaterialKeywords(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void Set(SerializedObject so, string path, object value)
        {
            var p = so.FindProperty(path) ?? throw new Exception($"{so.targetObject.GetType().Name} has no serialized field '{path}' (HDRP version drift?)");
            switch (value)
            {
                case bool b: p.boolValue = b; break;
                case int i: p.intValue = i; break;
                case float f: p.floatValue = f; break;
                default: throw new ArgumentException(path);
            }
        }
    }
}
