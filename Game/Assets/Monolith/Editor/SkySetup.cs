using Monolith.Core;
using UnityEditor;
using UnityEngine;

namespace Monolith.EditorTools
{
    /// <summary>
    /// Editor-side set-up for the sky (Game/Assets/Monolith/Sky): import settings for the Milky Way map and the plain grey
    /// test-hull material the sky test scene uses. <see cref="SkyTextureImport"/> enforces the map's settings on every
    /// import, so a fresh clone gets them without anyone running anything; <c>SkySetup.Run</c> checks the assets exist and
    /// are set up (idempotent):  tools\unity.ps1 Monolith.EditorTools.SkySetup.Run
    /// </summary>
    public static class SkySetup
    {
        public const string Dir = "Assets/Monolith/Sky/Resources/MonolithSky";
        public const string MilkyWayPath = Dir + "/milkyway.exr";
        public const string StarsPath = Dir + "/stars.bytes";
        public const string HullMaterialPath = Dir + "/MN_TestHull.mat";
        public const string HullLightsPath = Dir + "/MN_TestHullLights.mat";

        public static void Run() => Batch.Run("SkySetup", Ensure);

        public static void Ensure()
        {
            foreach (var path in new[] { MilkyWayPath, StarsPath, Dir + "/MonolithSky.shader" })
                if (AssetDatabase.LoadMainAssetAtPath(path) == null) throw new System.IO.FileNotFoundException("sky asset missing: " + path);

            // Plain grey painted metal: enough shine to catch the Sun and the cold fill. The lights are emissive (nits), bright enough to bloom.
            Hdrp.EnsureLit(HullMaterialPath, new Color(0.50f, 0.50f, 0.52f), 0.5f, 0.5f);
            var lights = Hdrp.EnsureLit(HullLightsPath, new Color(0.05f, 0.05f, 0.05f), 0f, 0.3f);
            SetEmissive(lights, new Color(0.78f, 0.90f, 1.00f), 60000f);

            var importer = (TextureImporter)AssetImporter.GetAtPath(MilkyWayPath);
            if (SkyTextureImport.Apply(importer)) importer.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(MilkyWayPath);
            MnLog.Info($"SkySetup: Milky Way map {tex.width}x{tex.height}, imported as {tex.format}, {tex.mipmapCount} mips");
        }

        static void SetEmissive(Material m, Color colour, float nits)
        {
            m.SetFloat("_UseEmissiveIntensity", 1f);
            m.SetFloat("_EmissiveIntensityUnit", 0f);
            m.SetFloat("_EmissiveIntensity", nits);
            m.SetColor("_EmissiveColorLDR", colour);
            m.SetColor("_EmissiveColor", colour * nits);
            UnityEditor.Rendering.HighDefinition.HDShaderUtils.ResetMaterialKeywords(m);
            EditorUtility.SetDirty(m);
        }
    }

    /// <summary>Forces the Milky Way map's import settings: linear data, mipmapped, wrapping around in RA only, compressed for HDR (BC6H).</summary>
    public sealed class SkyTextureImport : AssetPostprocessor
    {
        public override uint GetVersion() => 1;

        void OnPreprocessTexture()
        {
            if (assetPath == SkySetup.MilkyWayPath) Apply((TextureImporter)assetImporter);
        }

        /// <summary>Sets the importer; returns true if anything changed.</summary>
        public static bool Apply(TextureImporter ti)
        {
            bool changed = false;
            void Set<T>(T current, T wanted, System.Action<T> set)
            {
                if (!System.Collections.Generic.EqualityComparer<T>.Default.Equals(current, wanted)) { set(wanted); changed = true; }
            }
            Set(ti.textureType, TextureImporterType.Default, v => ti.textureType = v);
            Set(ti.textureShape, TextureImporterShape.Texture2D, v => ti.textureShape = v);
            Set(ti.sRGBTexture, false, v => ti.sRGBTexture = v);
            Set(ti.mipmapEnabled, true, v => ti.mipmapEnabled = v);
            Set(ti.filterMode, FilterMode.Trilinear, v => ti.filterMode = v);
            Set(ti.wrapModeU, TextureWrapMode.Repeat, v => ti.wrapModeU = v);
            Set(ti.wrapModeV, TextureWrapMode.Clamp, v => ti.wrapModeV = v);
            Set(ti.anisoLevel, 0, v => ti.anisoLevel = v);
            Set(ti.maxTextureSize, 4096, v => ti.maxTextureSize = v);
            Set(ti.npotScale, TextureImporterNPOTScale.None, v => ti.npotScale = v);
            Set(ti.alphaSource, TextureImporterAlphaSource.None, v => ti.alphaSource = v);
            Set(ti.isReadable, false, v => ti.isReadable = v);
            Set(ti.textureCompression, TextureImporterCompression.Compressed, v => ti.textureCompression = v);
            Set(ti.compressionQuality, 50, v => ti.compressionQuality = v);
            return changed;
        }
    }
}
