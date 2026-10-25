using System;
using System.IO;
using System.Linq;
using Monolith.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;

namespace Monolith.EditorTools
{
    /// <summary>
    /// Turns the comet models' output (art_src/comets, art_src/oort_art/build_comet_textures.py) into game assets, idempotently:
    ///   tools\unity.ps1 Monolith.EditorTools.CometArtSetup.Run        (Setup.Project calls it too)
    /// From Resources/CometArt/comets.json (written by build_comets.py) it makes the ONE layered HDRP material the comets share (crust, dust and ice, blended by the mesh's vertex colour:
    /// R the dust's weight, G the ice's), and for each model a prefab in the same folder: the FBX's LOD0..n objects as a LODGroup with the material on every one. The mission's ice field
    /// (Game/Mission/CometField) loads the prefabs and scatters them. Runtime code never creates the material from a shader: it is an asset, so a player build keeps its shader variants.
    /// </summary>
    public static class CometArtSetup
    {
        public const string ResRoot = "Assets/Monolith/Resources/CometArt";
        public const string ArtRoot = "Assets/Monolith/Art/Comets";

        public static void Run() => Batch.Run("CometArtSetup", Ensure);

        public static void Ensure()
        {
            string manifestPath = ResRoot + "/comets.json";
            if (!File.Exists(manifestPath)) { MnLog.Info("COMETART no " + manifestPath + " (nothing to do)"); return; }
            AssetDatabase.Refresh();
            var doc = JObject.Parse(File.ReadAllText(manifestPath));
            var mat = EnsureSurface((string)doc["materialSet"], (string)doc["material"]);
            foreach (var m in (JArray)doc["models"]) BuildModel((JObject)m, mat);
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------------------------------ the material

        static Material EnsureSurface(string materialsJsonPath, string blenderName)
        {
            var spec = JObject.Parse(File.ReadAllText(materialsJsonPath));
            var m = ((JArray)spec["materials"]).FirstOrDefault(x => (string)x["blender"] == blenderName) ?? throw new Exception($"{materialsJsonPath} has no material '{blenderName}'");
            string dir = Path.GetDirectoryName(materialsJsonPath).Replace('\\', '/');
            string matDir = ResRoot + "/Materials";
            Directory.CreateDirectory(matDir);
            string path = $"{matDir}/{(string)m["name"]}.mat";
            var shader = Shader.Find((string)m["shader"] ?? "HDRP/LayeredLit") ?? throw new InvalidOperationException("shader " + (string)m["shader"] + " not found");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); MnLog.Info("created " + path); }
            mat.shader = shader;
            var layers = (JArray)m["layers"];
            mat.SetFloat("_LayerCount", layers.Count);
            mat.SetFloat("_VertexColorMode", (string)m["vertexColors"] == "multiply" ? 1f : (string)m["vertexColors"] == "add" ? 2f : 0f);
            mat.SetFloat("_UseMainLayerInfluence", 0f);
            mat.SetFloat("_UseHeightBasedBlend", 0f);
            mat.SetFloat("_UseDensityMode", 0f);
            bool aa = false;
            for (int i = 0; i < layers.Count; i++)
            {
                var L = layers[i];
                mat.SetColor($"_BaseColor{i}", ShipArtSetup.Col(L["baseColor"], Color.white));
                ShipArtSetup.Tex(mat, $"_BaseColorMap{i}", dir, (string)L["baseMap"]);
                ShipArtSetup.Tex(mat, $"_NormalMap{i}", dir, (string)L["normalMap"]);
                ShipArtSetup.Tex(mat, $"_MaskMap{i}", dir, (string)L["maskMap"]);
                ShipArtSetup.Tex(mat, $"_DetailMap{i}", dir, (string)L["detailMap"]);
                mat.SetFloat($"_NormalScale{i}", (float?)L["normalScale"] ?? 1f);
                ShipArtSetup.Remap(mat, $"_MetallicRemap", null);
                SetRemap(mat, $"_MetallicRemap", i, L["metallicRemap"]);
                SetRemap(mat, $"_SmoothnessRemap", i, L["smoothnessRemap"]);
                SetRemap(mat, $"_AORemap", i, L["aoRemap"]);
                mat.SetFloat($"_UVBase{i}", 0f);
                mat.SetColor($"_UVMappingMask{i}", new Color(1f, 0f, 0f, 0f));
                var tiling = L["tiling"] != null ? new Vector2((float)L["tiling"][0], (float)L["tiling"][1]) : Vector2.one;
                mat.SetTextureScale($"_BaseColorMap{i}", tiling);
                if (L["detailMap"] != null && L["detailMap"].Type != JTokenType.Null)
                {
                    int uvSet = Mathf.RoundToInt((float?)L["uvDetail"] ?? 1f);
                    mat.SetFloat($"_UVDetail{i}", uvSet);
                    mat.SetColor($"_UVDetailsMappingMask{i}", new Color(uvSet == 0 ? 1f : 0f, uvSet == 1 ? 1f : 0f, uvSet == 2 ? 1f : 0f, uvSet == 3 ? 1f : 0f));
                    mat.SetFloat($"_LinkDetailsWithBase{i}", 0f);
                    mat.SetFloat($"_DetailAlbedoScale{i}", (float?)L["detailAlbedo"] ?? 1f);
                    mat.SetFloat($"_DetailNormalScale{i}", (float?)L["detailNormal"] ?? 1f);
                    mat.SetFloat($"_DetailSmoothnessScale{i}", (float?)L["detailSmoothness"] ?? 1f);
                    var dt = L["detailTiling"] != null ? new Vector2((float)L["detailTiling"][0], (float)L["detailTiling"][1]) : Vector2.one;
                    mat.SetTextureScale($"_DetailMap{i}", dt);
                }
                aa |= (bool?)L["specularAA"] == true;
            }
            if (aa)
            {
                mat.SetFloat("_EnableGeometricSpecularAA", 1f);
                mat.SetFloat("_SpecularAAScreenSpaceVariance", 0.2f);
                mat.SetFloat("_SpecularAAThreshold", 0.2f);
            }
            mat.enableInstancing = (bool?)m["enableInstancing"] ?? false;
            HDShaderUtils.ResetMaterialKeywords(mat);
            EditorUtility.SetDirty(mat);
            MnLog.Info($"COMETART material {(string)m["name"]}: {layers.Count} layers, keywords [{string.Join(" ", mat.shaderKeywords)}]");
            return mat;
        }

        static void SetRemap(Material m, string prefix, int layer, JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return;
            m.SetFloat($"{prefix}Min{layer}", (float)t[0]);
            m.SetFloat($"{prefix}Max{layer}", (float)t[1]);
        }

        // ------------------------------------------------------------------------------------------ the prefabs

        static void BuildModel(JObject m, Material mat)
        {
            string id = (string)m["id"];
            string fbxPath = $"{ArtRoot}/{(string)m["fbx"]}.fbx";
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceSynchronousImport);
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath) ?? throw new FileNotFoundException($"{fbxPath} not found: run art_src/comets/build_comets.py");
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
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var shared = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                    for (int i = 0; i < shared.Length; i++) shared[i] = mat;
                    r.sharedMaterials = shared;
                    r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    r.receiveShadows = true;
                }
                float[] heights = m["lodScreen"].ToObject<float[]>();
                var lodRenderers = new System.Collections.Generic.List<Renderer[]>();
                for (int i = 0; ; i++)
                {
                    var t = go.transform.Find($"{id}_LOD{i}");
                    if (t == null) break;
                    lodRenderers.Add(new[] { t.GetComponent<Renderer>() });
                }
                if (lodRenderers.Count == 0) throw new Exception($"{id}: no {id}_LOD0 object in the FBX");
                var group = go.AddComponent<LODGroup>();
                var lods = new LOD[lodRenderers.Count];
                for (int i = 0; i < lods.Length; i++) lods[i] = new LOD(heights[Mathf.Min(i, heights.Length - 1)], lodRenderers[i]);
                group.SetLODs(lods);
                group.fadeMode = LODFadeMode.None;
                group.RecalculateBounds();
                string prefabPath = $"{ResRoot}/{id}.prefab";
                PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                var tri = lodRenderers.Select(l => TriangleCount(l[0])).ToArray();
                MnLog.Info($"COMETART {id}: prefab {prefabPath}, {lodRenderers.Count} LODs (triangles {string.Join("/", tri)}; manifest {string.Join("/", m["triangles"].ToObject<int[]>())}), bounds {group.size:0.0} m, bound radius {(float)m["bound"]} m");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        static int TriangleCount(Renderer r)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return 0;
            long n = 0;
            for (int i = 0; i < mf.sharedMesh.subMeshCount; i++) n += mf.sharedMesh.GetIndexCount(i);
            return (int)(n / 3);
        }
    }
}
