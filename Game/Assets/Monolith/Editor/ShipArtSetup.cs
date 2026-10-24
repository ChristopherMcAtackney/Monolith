using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Monolith.Core;
using Monolith.ShipArt;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine;

namespace Monolith.EditorTools
{
    /// <summary>
    /// Turns the art pipeline's output (art_src/PIPELINE.md) into game assets, idempotently:
    ///   tools\unity.ps1 Monolith.EditorTools.ShipArtSetup.Run        (Setup.Project calls it too)
    /// For every Resources/ShipArt/&lt;id&gt;.ship.json written by a model script it
    ///   1. makes the HDRP material assets named in the model's material file (textures from Assets/Monolith/Art/...), including the
    ///      HDRP decal material the hull's scorch marks use,
    ///   2. builds Resources/ShipArt/&lt;id&gt;.prefab from the imported FBX: materials assigned by slot name, a LODGroup from the
    ///      &lt;id&gt;_LOD0..n objects (the intact SUB_* parts draw at LOD0 and LOD1), the collision proxy as a MeshCollider,
    ///      a ShipSubsystem on every SUB_* empty (intact, damaged and destroyed looks, debris), ShipFxHook on FX_*, HullDamageSite on DMG_site_*,
    ///      and ShipDamage on the root (the interface the combat system calls),
    ///   3. checks the prefab against the manifest (subsystem positions and facing, so a wrong FBX axis conversion fails loudly).
    /// Runtime code never creates these materials: they are assets, so a player build keeps their shader variants.
    /// </summary>
    public static class ShipArtSetup
    {
        public const string ArtRoot = "Assets/Monolith/Art";
        public const string ResRoot = "Assets/Monolith/Resources/" + ShipArtCatalog.Folder;

        public static void Run() => Batch.Run("ShipArtSetup", Ensure);

        public static void Ensure()
        {
            Directory.CreateDirectory(ResRoot);
            AssetDatabase.Refresh();
            var manifests = Directory.Exists(ResRoot) ? Directory.GetFiles(ResRoot, "*.ship.json") : new string[0];
            if (manifests.Length == 0) { MnLog.Info("SHIPART no ship manifests under " + ResRoot + " (nothing to do)"); return; }
            foreach (var file in manifests) BuildShip(file.Replace('\\', '/'));
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------------------------------ materials

        sealed class MaterialSet
        {
            public readonly Dictionary<string, Material> ByBlenderName = new Dictionary<string, Material>();
            public Material Decal;
        }

        static MaterialSet EnsureMaterials(string materialsJsonPath)
        {
            var spec = JObject.Parse(File.ReadAllText(materialsJsonPath));
            string dir = Path.GetDirectoryName(materialsJsonPath).Replace('\\', '/');
            string matDir = ResRoot + "/Materials";
            Directory.CreateDirectory(matDir);
            var set = new MaterialSet();
            foreach (var m in (JArray)spec["materials"])
            {
                string name = (string)m["name"];
                if ((string)m["shader"] == "HDRP/Unlit") { set.ByBlenderName[(string)m["blender"]] = EnsureUnlitGlow(m, dir, matDir); continue; }
                var baseCol = Col(m["baseColor"], Color.white);
                var mat = Hdrp.EnsureLit($"{matDir}/{name}.mat", baseCol, (float?)m["metallic"] ?? 0f, (float?)m["smoothness"] ?? 0.5f);
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
                    // The material inspector derives this from _UVDetail; a script has to set it itself.
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
                    mat.SetFloat("_EnableGeometricSpecularAA", 1f);       // tames the glitter of sun on tiny bevels and fine scratches
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
                if ((string)m["surface"] == "alpha")
                {
                    // Overlays that lie on the plates (grime, scuffs, repainted patches): alpha-blended, no depth write, lit like the hull around them.
                    mat.SetFloat("_SurfaceType", 1f);
                    mat.SetFloat("_BlendMode", 0f);
                    mat.SetFloat("_ZWrite", 0f);
                    mat.SetFloat("_TransparentZWrite", 0f);
                    mat.SetFloat("_EnableFogOnTransparent", 0f);
                    mat.SetFloat("_EnableBlendModePreserveSpecularLighting", 1f);
                    mat.SetFloat("_AlphaCutoffEnable", 0f);
                    mat.SetFloat("_ReceivesSSR", 0f);
                }
                HDShaderUtils.ResetMaterialKeywords(mat);
                EditorUtility.SetDirty(mat);
                set.ByBlenderName[(string)m["blender"]] = mat;
                MnLog.Info($"SHIPART material {name}: keywords [{string.Join(" ", mat.shaderKeywords)}] detail mask {mat.GetColor("_UVDetailsMappingMask")}");
            }
            if (spec["decal"] is JObject dec) set.Decal = EnsureDecal(dec, dir, matDir);
            return set;
        }

        /// <summary>An emissive-only HDRP/Unlit surface, additive and double sided: engine plumes. Light, not a solid.</summary>
        static Material EnsureUnlitGlow(JToken m, string dir, string matDir)
        {
            string name = (string)m["name"];
            string path = $"{matDir}/{name}.mat";
            var shader = Shader.Find("HDRP/Unlit") ?? throw new InvalidOperationException("shader HDRP/Unlit not found");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); MnLog.Info("created " + path); }
            mat.shader = shader;
            mat.SetColor("_UnlitColor", Color.black);
            Tex(mat, "_EmissiveColorMap", dir, (string)m["emissiveMap"]);
            float nits = (float?)m["emissiveNits"] ?? 0f;
            mat.SetFloat("_UseEmissiveIntensity", 1f);
            mat.SetFloat("_EmissiveIntensityUnit", 0f);
            mat.SetFloat("_EmissiveIntensity", nits);
            mat.SetColor("_EmissiveColorLDR", Color.white);
            mat.SetColor("_EmissiveColor", Color.white * nits);
            mat.SetFloat("_SurfaceType", 1f);                  // transparent
            mat.SetFloat("_BlendMode", 1f);                    // additive
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_TransparentZWrite", 0f);
            mat.SetFloat("_DoubleSidedEnable", 1f);
            mat.SetFloat("_TransparentCullMode", 0f);
            mat.SetFloat("_EnableFogOnTransparent", 0f);
            HDShaderUtils.ResetMaterialKeywords(mat);
            EditorUtility.SetDirty(mat);
            MnLog.Info($"SHIPART unlit glow {name}: keywords [{string.Join(" ", mat.shaderKeywords)}] queue {mat.renderQueue} blend {mat.GetFloat("_SrcBlend")}/{mat.GetFloat("_DstBlend")}");
            return mat;
        }

        static Material EnsureDecal(JObject d, string dir, string matDir)
        {
            string name = (string)d["name"];
            string path = $"{matDir}/{name}.mat";
            var shader = Shader.Find("HDRP/Decal") ?? throw new InvalidOperationException("shader HDRP/Decal not found");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); MnLog.Info("created " + path); }
            mat.shader = shader;
            Tex(mat, "_BaseColorMap", dir, (string)d["baseMap"]);
            Tex(mat, "_NormalMap", dir, (string)d["normalMap"]);
            Tex(mat, "_MaskMap", dir, (string)d["maskMap"]);
            Tex(mat, "_EmissiveColorMap", dir, (string)d["emissiveMap"]);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_DecalBlend", 1f);
            mat.SetFloat("_NormalBlendSrc", 1f);          // normal and mask blend follow the mask map's blue (opacity)
            mat.SetFloat("_MaskBlendSrc", 1f);
            mat.SetFloat("_AffectAlbedo", 1f);
            mat.SetFloat("_AffectNormal", 1f);
            mat.SetFloat("_AffectMetal", 1f);
            mat.SetFloat("_AffectAO", 1f);
            mat.SetFloat("_AffectSmoothness", 1f);
            bool em = (bool?)d["affectEmission"] == true;
            mat.SetFloat("_AffectEmission", em ? 1f : 0f);
            float nits = (float?)d["emissiveNits"] ?? 0f;
            mat.SetFloat("_UseEmissiveIntensity", 1f);
            mat.SetFloat("_EmissiveIntensityUnit", 0f);
            mat.SetFloat("_EmissiveIntensity", nits);
            mat.SetColor("_EmissiveColorLDR", Color.white);
            mat.SetColor("_EmissiveColor", Color.white * nits);
            HDShaderUtils.ResetMaterialKeywords(mat);
            EditorUtility.SetDirty(mat);
            MnLog.Info($"SHIPART decal material {name}: keywords [{string.Join(" ", mat.shaderKeywords)}]");
            return mat;
        }

        static Color Col(JToken t, Color fallback)
        {
            if (t == null || t.Type == JTokenType.Null) return fallback;
            return new Color((float)t[0], (float)t[1], (float)t[2], 1f).linear;     // the file holds sRGB display values
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

        static Vector3 V3(JToken t) => new Vector3((float)t[0], (float)t[1], (float)t[2]);

        static void BuildShip(string manifestPath)
        {
            var manifestJson = File.ReadAllText(manifestPath);
            var manifest = ShipManifest.Parse(manifestJson);
            var raw = JObject.Parse(manifestJson);
            string id = manifest.Id;
            string matFile = (string)raw["materialSet"] ?? throw new Exception($"{id}: manifest has no materialSet");
            var mats = EnsureMaterials(matFile);

            string fbxPath = AssetDatabase.FindAssets($"{id} t:Model", new[] { ArtRoot }).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == id)
                             ?? throw new FileNotFoundException($"{id}.fbx not found under {ArtRoot}: run the model script");
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            var go = UnityEngine.Object.Instantiate(fbx);
            go.name = id;
            // Unity wraps a model that has more than one top-level node in a container named after the file; the model's own root is its child.
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
                    var shared = r.sharedMaterials;
                    for (int i = 0; i < shared.Length; i++)
                    {
                        string n = shared[i] != null ? shared[i].name : "";
                        if (!mats.ByBlenderName.TryGetValue(n, out var mine)) throw new Exception($"{id}: renderer {r.name} slot {i} uses material '{n}', not in the material file");
                        shared[i] = mine;
                    }
                    r.sharedMaterials = shared;
                    r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                    bool glow = shared.Length > 0 && shared[0] != null && (shared[0].name == "MN_EarthPlume" || shared[0].name == "MN_OortJet");
                    r.shadowCastingMode = glow ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                    r.receiveShadows = !glow;
                }

                // Subsystems first: their intact renderers join the LODs, the variants and debris stay out of them.
                var intactRenderers = new List<Renderer>();
                int found = 0, variants = 0, debris = 0;
                foreach (var s in manifest.Subsystems)
                {
                    var t = go.transform.Find(s.Name) ?? throw new Exception($"{id}: manifest subsystem {s.Name} is not in the FBX");
                    float err = (t.position - s.Position).magnitude;
                    float ang = Vector3.Angle(t.forward, s.Forward);
                    if (err > 0.02f || ang > 0.1f) throw new Exception($"{id}: {s.Name} sits at {t.position} facing {t.forward}, manifest says {s.Position} / {s.Forward}: the FBX axis conversion is wrong");
                    var sub = t.gameObject.AddComponent<ShipSubsystem>();
                    sub.Kind = s.Kind; sub.Radius = s.Radius; sub.Hp = s.Hp; sub.Tags = s.Tags;
                    var info = raw["subsystems"].First(x => (string)x["name"] == s.Name);
                    GameObject[] Parts(string variant) => info["parts"][variant] == null ? new GameObject[0]
                        : info["parts"][variant].Select(n => (t.Find((string)n) ?? throw new Exception($"{id}: part {n} missing")).gameObject).ToArray();
                    var intact = Parts("intact"); var damaged = Parts("damaged"); var destroyed = Parts("destroyed");
                    var pieces = new List<DebrisPiece>();
                    foreach (var dj in (JArray)info["debris"])
                    {
                        var dt = t.Find((string)dj["name"]) ?? throw new Exception($"{id}: debris {dj["name"]} missing");
                        pieces.Add(new DebrisPiece { Piece = dt, Origin = V3(dj["position"]), Velocity = V3(dj["velocity"]), SpinDegPerSec = V3(dj["spin"]) });
                    }
                    sub.Configure(intact, damaged, destroyed, pieces.ToArray());
                    if (s.Kind == ShipSubsystem.Engine)
                    {
                        // A live engine: its glowing core and its flare flicker and follow the throttle (EngineGlow).
                        var glow = t.gameObject.AddComponent<EngineGlow>();
                        glow.FollowShip = (bool?)raw["engineFollowsShip"] == true;
                        var lights = new List<HDAdditionalLightData>(); var candela = new List<float>();
                        if (raw["engineLights"]?[s.Name] is JObject spec)
                        {
                            // Light from the engine on the hull round the nozzle and the bell: a cold point light just outside the throat, no shadows.
                            var lg = new GameObject("SPILL");
                            lg.transform.SetParent(t, false);
                            lg.transform.localPosition = new Vector3(0f, 0f, (float)spec["z"]);
                            var data = lg.AddHDLight(HDLightTypeAndShape.Point);
                            var light = lg.GetComponent<Light>();
                            light.color = new Color(0.58f, 0.76f, 1f);
                            light.range = (float)spec["range"];
                            data.shapeRadius = (float?)spec["radius"] ?? 0.15f;
                            if (spec["fade"] != null) data.fadeDistance = (float)spec["fade"];       // small ships: the light is only worth drawing near the camera
                            data.EnableShadows(false);
                            data.SetIntensity((float)spec["candela"], LightUnit.Candela);
                            lights.Add(data); candela.Add((float)spec["candela"]);
                        }
                        glow.Configure(RenderersOf(t, s.Name + "__core"), RenderersOf(t, s.Name + "__plume"), lights.ToArray(), candela.ToArray());
                    }
                    foreach (var g in intact) intactRenderers.AddRange(g.GetComponentsInChildren<Renderer>(true));
                    variants += damaged.Length + destroyed.Length;
                    debris += pieces.Count;
                    found++;
                }

                // LOD group: LODn = the <id>_LODn renderer; the intact subsystem parts join LOD0 and LOD1 (LOD2 has them baked in as boxes).
                float[] heights = raw["lodScreenHeights"] != null ? raw["lodScreenHeights"].ToObject<float[]>() : new[] { 0.16f, 0.05f, 0.012f };
                var lodObjects = new List<Renderer[]>();
                for (int i = 0; ; i++)
                {
                    var t = go.transform.Find($"{id}_LOD{i}");
                    if (t == null) break;
                    var list = new List<Renderer> { t.GetComponent<Renderer>() };
                    if (i <= 1) list.AddRange(intactRenderers);
                    lodObjects.Add(list.ToArray());
                }
                if (lodObjects.Count == 0) throw new Exception($"{id}: no {id}_LOD0 object in the FBX");
                var group = go.AddComponent<LODGroup>();
                var lods = new LOD[lodObjects.Count];
                for (int i = 0; i < lods.Length; i++) lods[i] = new LOD(heights[Mathf.Min(i, heights.Length - 1)], lodObjects[i]);
                group.SetLODs(lods);
                group.fadeMode = LODFadeMode.None;
                group.RecalculateBounds();

                // Collision proxy.
                var col = go.transform.Find($"{id}_COL");
                if (col != null)
                {
                    var mf = col.GetComponent<MeshFilter>();
                    var mr = col.GetComponent<MeshRenderer>();
                    var mc = col.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    UnityEngine.Object.DestroyImmediate(mr);
                    UnityEngine.Object.DestroyImmediate(mf);
                }

                // FX hooks and hull damage sites.
                int hooks = 0, sites = 0;
                foreach (var h in (JArray)raw["hooks"])
                {
                    var t = go.transform.Find((string)h["name"]) ?? throw new Exception($"{id}: hook {h["name"]} missing");
                    var hook = t.gameObject.AddComponent<ShipFxHook>();
                    hook.Kind = (string)h["kind"]; hook.Size = (float)h["size"];
                    hooks++;
                }
                foreach (var sj in (JArray)raw["damageSites"])
                {
                    var t = go.transform.Find((string)sj["name"]) ?? throw new Exception($"{id}: damage site {sj["name"]} missing");
                    var site = t.gameObject.AddComponent<HullDamageSite>();
                    var breach = t.Find((string)sj["name"] + "__breach");
                    site.Configure(breach != null ? breach.gameObject : null, (float)sj["radius"]);
                    sites++;
                }

                // The interface the combat system calls.
                var dmg = go.AddComponent<ShipDamage>();
                var pts = new List<Vector3>(); var nrm = new List<Vector3>();
                if (raw["damagePoints"] is JArray dp) foreach (var p in dp) { pts.Add(V3(p["p"])); nrm.Add(V3(p["n"])); }
                dmg.Configure(mats.Decal, pts.ToArray(), nrm.ToArray(), (float?)raw["scorchScale"] ?? 1f);

                string prefabPath = $"{ResRoot}/{id}.prefab";
                PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                var tri = lodObjects.Select(l => TriangleCount(l[0])).ToArray();
                MnLog.Info($"SHIPART {id}: prefab {prefabPath}, {lodObjects.Count} LODs (hull triangles {string.Join("/", tri)}; manifest {string.Join("/", manifest.LodTriangles)}), " +
                           $"{found} subsystems ({variants} wreck/damaged parts, {debris} debris pieces), {hooks} FX hooks, {sites} damage sites, {pts.Count} scorch points, " +
                           $"{mats.ByBlenderName.Count} materials, bounds {group.size:0.0} m, length {manifest.Length} m");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        static Renderer[] RenderersOf(Transform parent, string childName)
        {
            var c = parent.Find(childName);
            return c == null ? new Renderer[0] : c.GetComponentsInChildren<Renderer>(true);
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

    /// <summary>Import rules for art under Assets/Monolith/Art: ship FBXs (axis baked, our own materials, no animation) and the
    /// generated PBR textures (by suffix: _albedo and _emissive are colour, _normal a normal map, _mask and _detail linear data;
    /// decal atlases (names with "damage") clamp and keep their alpha).</summary>
    public sealed class ShipArtImport : AssetPostprocessor
    {
        public override uint GetVersion() => 6;

        static bool InArt(string path) => path.StartsWith(ShipArtSetup.ArtRoot + "/", StringComparison.OrdinalIgnoreCase);

        void OnPreprocessModel()
        {
            if (!InArt(assetPath) || !assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return;
            var m = (ModelImporter)assetImporter;
            m.bakeAxisConversion = true;               // Blender's axes are written as they are; Unity turns them into +z bow, +x starboard, +y up
            m.useFileScale = true;
            m.globalScale = 1f;
            m.importAnimation = false;
            m.animationType = ModelImporterAnimationType.None;
            m.importCameras = false;
            m.importLights = false;
            m.importBlendShapes = false;
            m.importVisibility = false;
            m.importNormals = ModelImporterNormals.Import;
            m.importTangents = ModelImporterTangents.CalculateMikk;
            m.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            m.materialLocation = ModelImporterMaterialLocation.InPrefab;
            m.meshCompression = ModelImporterMeshCompression.Off;
            m.isReadable = assetPath.IndexOf("/Cabin/", StringComparison.OrdinalIgnoreCase) >= 0;      // the cabin model is cut into pieces at run time (Juice/DeathFx); ships are not
            m.optimizeMeshPolygons = true;
            m.optimizeMeshVertices = true;
            m.weldVertices = true;
            m.generateSecondaryUV = false;
            m.preserveHierarchy = true;
            m.indexFormat = ModelImporterIndexFormat.UInt32;
        }

        void OnPreprocessTexture()
        {
            if (!InArt(assetPath) || !assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return;
            var t = (TextureImporter)assetImporter;
            string name = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            bool color = name.EndsWith("_albedo") || name.EndsWith("_emissive");
            bool normal = name.EndsWith("_normal");
            bool decal = name.Contains("damage");
            t.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            t.sRGBTexture = color;
            t.alphaSource = TextureImporterAlphaSource.FromInput;
            t.alphaIsTransparency = (decal || name.Contains("wear")) && name.EndsWith("_albedo");
            t.mipmapEnabled = true;
            t.streamingMipmaps = false;
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 16;
            t.wrapMode = decal ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            t.maxTextureSize = 4096;
            t.npotScale = TextureImporterNPOTScale.None;
            t.isReadable = false;
            t.textureCompression = TextureImporterCompression.CompressedHQ;
            t.compressionQuality = 100;
        }
    }
}
