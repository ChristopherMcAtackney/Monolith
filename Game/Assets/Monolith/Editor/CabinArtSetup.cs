using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Monolith.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.EditorTools
{
    /// <summary>
    /// Turns the cabin model script's output (art_src/PIPELINE.md, "The cabin") into game assets, idempotently:
    ///   tools\unity.ps1 Monolith.EditorTools.CabinArtSetup.Run        (Setup.Project calls it too)
    /// For each of <c>Assets/Monolith/Art/Cabin/*.materials.json</c> it makes the HDRP materials (textures beside it), and for each model that has a layout file under
    /// <c>Resources/Cabin/&lt;id&gt;.layout.json</c> it builds <c>Resources/Cabin/&lt;id&gt;.prefab</c> from the imported FBX: materials assigned by slot name, every renderer on the
    /// cabin's rendering layer (so the cabin's own lights light it and the Sun does not: the canopy's share of the Sun is a light of the cabin's own), and shadow
    /// rules (glass and screens cast none). Runtime code never makes these materials: they are assets, so a player build keeps their shader variants.
    /// </summary>
    public static class CabinArtSetup
    {
        public const string ArtDir = "Assets/Monolith/Art/Cabin";
        public const string ResDir = "Assets/Monolith/Resources/Cabin";
        /// <summary>The rendering layer (HDRP light layer 1) the cabin lives on, as a mask.</summary>
        public const uint CabinLayerMask = 1u << 1;

        public static void Run() => Batch.Run("CabinArtSetup", Ensure);

        public static void Ensure()
        {
            Directory.CreateDirectory(ResDir);
            AssetDatabase.Refresh();
            if (!Directory.Exists(ArtDir)) { MnLog.Info("CABINART no Art/Cabin folder (nothing to do)"); return; }
            var mats = new Dictionary<string, Material>();
            foreach (var f in Directory.GetFiles(ArtDir, "*.materials.json"))
                foreach (var kv in EnsureMaterials(f.Replace('\\', '/'))) mats[kv.Key] = kv.Value;
            foreach (var layout in Directory.GetFiles(ResDir, "*.layout.json"))
            {
                string id = Path.GetFileName(layout).Replace(".layout.json", "");
                BuildPrefab(id, mats);
            }
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------------------------------ materials

        static Dictionary<string, Material> EnsureMaterials(string path)
        {
            var spec = JObject.Parse(File.ReadAllText(path));
            string dir = Path.GetDirectoryName(path).Replace('\\', '/');
            string matDir = ResDir + "/Materials";
            Directory.CreateDirectory(matDir);
            var set = new Dictionary<string, Material>();
            foreach (var m in (JArray)spec["materials"])
            {
                string name = (string)m["name"];
                var mat = Hdrp.EnsureLit($"{matDir}/{name}.mat", Col(m["baseColor"], Color.white), (float?)m["metallic"] ?? 0f, (float?)m["smoothness"] ?? 0.5f);
                Tex(mat, "_BaseColorMap", dir, (string)m["baseMap"]);
                Tex(mat, "_NormalMap", dir, (string)m["normalMap"]);
                Tex(mat, "_MaskMap", dir, (string)m["maskMap"]);
                Tex(mat, "_DetailMap", dir, (string)m["detailMap"]);
                Tex(mat, "_EmissiveColorMap", dir, (string)m["emissiveMap"]);
                mat.SetFloat("_NormalScale", (float?)m["normalScale"] ?? 1f);
                Remap(mat, "_MetallicRemap", m["metallicRemap"]);
                Remap(mat, "_SmoothnessRemap", m["smoothnessRemap"]);
                Remap(mat, "_AORemap", m["aoRemap"]);
                if (m["detailMap"] != null && m["detailMap"].Type != JTokenType.Null)
                {
                    mat.SetFloat("_UVDetail", (float?)m["uvDetail"] ?? 1f);
                    mat.SetFloat("_LinkDetailsWithBase", 0f);
                    int uvSet = Mathf.RoundToInt((float?)m["uvDetail"] ?? 1f);
                    mat.SetColor("_UVDetailsMappingMask", new Color(uvSet == 0 ? 1f : 0f, uvSet == 1 ? 1f : 0f, uvSet == 2 ? 1f : 0f, uvSet == 3 ? 1f : 0f));
                    mat.SetFloat("_DetailAlbedoScale", (float?)m["detailAlbedo"] ?? 1f);
                    mat.SetFloat("_DetailNormalScale", (float?)m["detailNormal"] ?? 1f);
                    mat.SetFloat("_DetailSmoothnessScale", (float?)m["detailSmoothness"] ?? 1f);
                    var tiling = m["detailTiling"] != null ? new Vector2((float)m["detailTiling"][0], (float)m["detailTiling"][1]) : Vector2.one;
                    mat.SetTextureScale("_DetailMap", tiling);
                }
                if ((bool?)m["specularAA"] == true)
                {
                    mat.SetFloat("_EnableGeometricSpecularAA", 1f);
                    mat.SetFloat("_SpecularAAScreenSpaceVariance", 0.2f);
                    mat.SetFloat("_SpecularAAThreshold", 0.2f);
                }
                float nits = (float?)m["emissiveNits"] ?? 0f;
                var emCol = Col(m["emissiveColor"], Color.white);
                mat.SetFloat("_UseEmissiveIntensity", 1f);
                mat.SetFloat("_EmissiveIntensityUnit", 0f);
                mat.SetFloat("_EmissiveIntensity", nits);
                mat.SetColor("_EmissiveColorLDR", emCol);
                mat.SetColor("_EmissiveColor", emCol * nits);
                if ((bool?)m["doubleSided"] == true) { mat.SetFloat("_DoubleSidedEnable", 1f); mat.SetFloat("_DoubleSidedNormalMode", 1f); }
                if ((string)m["surface"] == "glass")
                {
                    // The canopy: a thin tinted pane, alpha-blended, keeping its reflections (the lit cabin shows in it at night), no depth write.
                    mat.SetFloat("_SurfaceType", 1f);
                    mat.SetFloat("_BlendMode", 0f);
                    mat.SetFloat("_ZWrite", 0f);
                    mat.SetFloat("_TransparentZWrite", 0f);
                    mat.SetFloat("_EnableFogOnTransparent", 0f);
                    mat.SetFloat("_EnableBlendModePreserveSpecularLighting", 1f);
                    mat.SetFloat("_AlphaCutoffEnable", 0f);
                    mat.SetFloat("_ReceivesSSR", 0f);
                    mat.SetFloat("_DoubleSidedEnable", 1f);
                    mat.SetFloat("_DoubleSidedNormalMode", 1f);
                    mat.SetFloat("_TransparentCullMode", 0f);
                    var bc = Col(m["baseColor"], Color.white); bc.a = (float?)m["alpha"] ?? 0.07f;
                    mat.SetColor("_BaseColor", bc);
                }
                HDShaderUtils.ResetMaterialKeywords(mat);
                EditorUtility.SetDirty(mat);
                set[(string)m["blender"]] = mat;
            }
            MnLog.Info($"CABINART {Path.GetFileName(path)}: {set.Count} materials");
            return set;
        }

        static Color Col(JToken t, Color fallback)
        {
            if (t == null || t.Type == JTokenType.Null) return fallback;
            return new Color((float)t[0], (float)t[1], (float)t[2], 1f).linear;
        }

        static void Remap(Material m, string prefix, JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return;
            m.SetFloat(prefix + "Min", (float)t[0]);
            m.SetFloat(prefix + "Max", (float)t[1]);
        }

        static void Tex(Material m, string prop, string dir, string file)
        {
            if (string.IsNullOrEmpty(file)) { m.SetTexture(prop, null); return; }
            string path = $"{dir}/{file}.png";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.Default);
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path) ?? throw new FileNotFoundException("texture missing (run the texture script): " + path);
            m.SetTexture(prop, t);
        }

        // ------------------------------------------------------------------------------------------ prefab

        static void BuildPrefab(string id, Dictionary<string, Material> mats)
        {
            string fbxPath = AssetDatabase.FindAssets($"{id} t:Model", new[] { ArtDir }).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == id)
                             ?? throw new FileNotFoundException($"{id}.fbx not found under {ArtDir}: run the model script");
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            var go = UnityEngine.Object.Instantiate(fbx);
            go.name = id;
            var inner = go.transform.Find(id);
            if (inner != null && inner != go.transform)
            {
                inner.SetParent(null, true);
                UnityEngine.Object.DestroyImmediate(go);
                go = inner.gameObject;
            }
            try
            {
                int renderers = 0, tris = 0;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var shared = r.sharedMaterials;
                    for (int i = 0; i < shared.Length; i++)
                    {
                        string n = shared[i] != null ? shared[i].name : "";
                        if (!mats.TryGetValue(n, out var mine)) throw new Exception($"{id}: renderer {r.name} slot {i} uses material '{n}', not in the material files");
                        shared[i] = mine;
                    }
                    r.sharedMaterials = shared;
                    r.lightProbeUsage = LightProbeUsage.Off;
                    r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    r.renderingLayerMask = CabinLayerMask;
                    bool glass = shared.Length > 0 && shared.All(m => m.name == "MN_CabGlass");
                    bool screen = r.name.StartsWith("DISPLAY_", StringComparison.Ordinal);
                    r.shadowCastingMode = glass || screen ? ShadowCastingMode.Off : ShadowCastingMode.TwoSided;
                    r.receiveShadows = !glass;
                    renderers++;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) for (int i = 0; i < mf.sharedMesh.subMeshCount; i++) tris += (int)(mf.sharedMesh.GetIndexCount(i) / 3);
                }
                string prefabPath = $"{ResDir}/{id}.prefab";
                PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                MnLog.Info($"CABINART {id}: prefab {prefabPath}, {renderers} renderers, {tris} triangles");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
