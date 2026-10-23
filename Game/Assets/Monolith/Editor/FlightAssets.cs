using System.IO;
using Monolith.Core;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;

namespace Monolith.EditorTools
{
    /// <summary>
    /// The flight playtest's HDRP/Lit base materials. The player build only keeps the shader variants of materials it
    /// ships, so each kind of surface the game builds at runtime needs an asset with the right maps switched on:
    ///   MN_FlightLit    plain Lit (running lights, windows: constant emission)
    ///   MN_FlightPlated Lit with a normal map and a mask map (plated hulls: textures are generated at boot)
    ///   MN_FlightGlow   Lit with an emissive colour map (exhaust gradients)
    /// The tiny placeholder textures here only switch the keywords on; the game swaps in its own.
    /// </summary>
    public static class FlightAssets
    {
        public static void Ensure(string dir)
        {
            Directory.CreateDirectory(dir);
            Hdrp.EnsureLit(dir + "/MN_FlightLit.mat", new Color(0.6f, 0.6f, 0.62f), 0.3f, 0.45f);

            var flatNormal = EnsureTexture(dir + "/MN_FlatNormal.asset", new Color(0.5f, 0.5f, 1f, 1f), true);
            var flatMask = EnsureTexture(dir + "/MN_FlatMask.asset", new Color(0f, 1f, 0f, 0.5f), true);
            var white = EnsureTexture(dir + "/MN_WhiteEmissive.asset", Color.white, false);

            var plated = Hdrp.EnsureLit(dir + "/MN_FlightPlated.mat", Color.white, 0f, 0.5f);
            plated.SetTexture("_NormalMap", flatNormal);
            plated.SetTexture("_MaskMap", flatMask);
            HDShaderUtils.ResetMaterialKeywords(plated);
            EditorUtility.SetDirty(plated);

            var glow = Hdrp.EnsureLit(dir + "/MN_FlightGlow.mat", Color.black, 0f, 0.5f);
            glow.SetTexture("_EmissiveColorMap", white);
            glow.SetColor("_EmissiveColor", Color.white);
            HDShaderUtils.ResetMaterialKeywords(glow);
            EditorUtility.SetDirty(glow);

            MnLog.Info($"FLIGHT materials: plated keywords [{string.Join(" ", plated.shaderKeywords)}], glow keywords [{string.Join(" ", glow.shaderKeywords)}]");
        }

        static Texture2D EnsureTexture(string path, Color c, bool linear)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t != null) return t;
            t = new Texture2D(4, 4, TextureFormat.RGBA32, false, linear) { name = Path.GetFileNameWithoutExtension(path) };
            var px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = c;
            t.SetPixels(px);
            t.Apply();
            AssetDatabase.CreateAsset(t, path);
            MnLog.Info("created " + path);
            return t;
        }
    }
}
