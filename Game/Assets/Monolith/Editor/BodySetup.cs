using UnityEditor;
using UnityEngine;

namespace Monolith.EditorTools
{
    /// <summary>Checks the backdrop planets' maps are imported the way <see cref="BodyTextureImport"/> says (idempotent): tools/unity.ps1 Monolith.EditorTools.BodySetup.Run logs each map's format and size.</summary>
    public static class BodySetup
    {
        public static void Run() => Batch.Run("BodySetup", Ensure);

        public static void Ensure()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { BodyTextureImport.Folder.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                if (BodyTextureImport.Apply(importer)) importer.SaveAndReimport();
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex.format == TextureFormat.RGBA32 || tex.format == TextureFormat.RGB24)       // never compressed: import it again from scratch
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                    tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }
                Monolith.Core.MnLog.Info($"BodySetup: {path} {tex.width}x{tex.height}, {tex.format}, {tex.mipmapCount} mips, {UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex) / 1048576f:0.0} MB");
            }
        }
    }

    /// <summary>
    /// Import settings for the backdrop planets' maps (<c>Sky/Resources/MonolithSky/Bodies/*_map.jpg</c>), enforced on every import so a fresh clone gets them:
    /// colour (sRGB) data, mipmapped, trilinear with the most anisotropy (the planet's limb is a long thin footprint), wrapping around in longitude only, compressed
    /// as BC7 at high quality (smooth gradients without banding), up to 8192 wide.
    /// </summary>
    public sealed class BodyTextureImport : AssetPostprocessor
    {
        public override uint GetVersion() => 1;

        public const string Folder = "Assets/Monolith/Sky/Resources/MonolithSky/Bodies/";

        static bool IsBodyMap(string path) => path.StartsWith(Folder) && path.Contains("_map.");

        void OnPreprocessTexture()
        {
            if (IsBodyMap(assetPath)) Apply((TextureImporter)assetImporter);
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
            Set(ti.sRGBTexture, true, v => ti.sRGBTexture = v);
            Set(ti.mipmapEnabled, true, v => ti.mipmapEnabled = v);
            Set(ti.filterMode, FilterMode.Trilinear, v => ti.filterMode = v);
            Set(ti.wrapModeU, TextureWrapMode.Repeat, v => ti.wrapModeU = v);
            Set(ti.wrapModeV, TextureWrapMode.Clamp, v => ti.wrapModeV = v);
            Set(ti.anisoLevel, 16, v => ti.anisoLevel = v);
            Set(ti.maxTextureSize, 8192, v => ti.maxTextureSize = v);
            Set(ti.npotScale, TextureImporterNPOTScale.None, v => ti.npotScale = v);
            Set(ti.alphaSource, TextureImporterAlphaSource.None, v => ti.alphaSource = v);
            Set(ti.isReadable, false, v => ti.isReadable = v);
            Set(ti.textureCompression, TextureImporterCompression.CompressedHQ, v => ti.textureCompression = v);
            Set(ti.compressionQuality, 100, v => ti.compressionQuality = v);
            Set(ti.streamingMipmaps, false, v => ti.streamingMipmaps = v);
            return changed;
        }
    }
}
