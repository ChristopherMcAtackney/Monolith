using System.Collections.Generic;
using System.IO;
using Monolith.ShipArt;
using UnityEditor;
using UnityEngine;

namespace Monolith.EditorTools
{
    /// <summary>
    /// Bakes what the juice cannot make at run time. Ship models import with unreadable meshes (a built player cannot cut them), so a
    /// capital ship's hull could not break into pieces; this cuts each hull carcass mesh here, in the editor where it can be read, into
    /// jagged sections along the ship's length and saves them as one asset per ship (<c>Resources/Juice/Sections/&lt;ship&gt;.sections.asset</c>).
    /// The juice swaps them in for the intact hull when the ship breaks up (see <c>HullSections</c>).
    /// <para>Run: <c>powershell -NoProfile -ExecutionPolicy Bypass -File tools\unity.ps1 Monolith.EditorTools.JuiceSetup.BakeSections</c>
    /// after the ship models change. A section mesh is named <c>&lt;source mesh&gt;#&lt;its vertex count&gt;#&lt;section&gt;</c>, so a stale bake is never used.</para>
    /// </summary>
    public static class JuiceSetup
    {
        public const int Sections = 8;
        const string OutDir = "Assets/Monolith/Resources/Juice/Sections";
        const string ShipPrefabs = "Assets/Monolith/Resources/ShipArt";

        public static void BakeSections() => Batch.Run("JuiceSetup.BakeSections", () =>
        {
            Directory.CreateDirectory(OutDir);
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { ShipPrefabs }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try { BakeShip(root, Path.GetFileNameWithoutExtension(path)); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.Refresh();
        });

        static bool IsHull(MeshRenderer r, HashSet<Transform> subs, Transform root)
        {
            if (r.name.Contains("_LOD1") || r.name.Contains("_LOD2") || r.name.EndsWith("_COL")) return false;
            for (var t = r.transform; t != null && t != root; t = t.parent) if (subs.Contains(t)) return false;
            return true;
        }

        static void BakeShip(GameObject root, string shipId)
        {
            var subs = new HashSet<Transform>();
            foreach (var s in root.GetComponentsInChildren<ShipSubsystem>(true)) subs.Add(s.transform);
            var rends = new List<MeshRenderer>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                if (IsHull(r, subs, root.transform)) rends.Add(r);
            }
            if (rends.Count == 0) { Debug.Log($"[MN] JuiceSetup: {shipId} has no hull meshes, skipped"); return; }

            // The ship's length range, along its local z, over every hull vertex.
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
            float zmin = float.MaxValue, zmax = float.MinValue;
            foreach (var r in rends)
            {
                var m = r.GetComponent<MeshFilter>().sharedMesh;
                var l2r = toRoot * r.transform.localToWorldMatrix;
                foreach (var v in m.vertices) { float z = l2r.MultiplyPoint3x4(v).z; if (z < zmin) zmin = z; if (z > zmax) zmax = z; }
            }
            float len = Mathf.Max(1f, zmax - zmin);

            var meshes = new List<Mesh>();
            int tris = 0;
            foreach (var r in rends)
            {
                var src = r.GetComponent<MeshFilter>().sharedMesh;
                var l2r = toRoot * r.transform.localToWorldMatrix;
                var verts = src.vertices; var norms = src.normals; var tans = src.tangents; var uv0 = src.uv; var uv1 = src.uv2;
                bool hasN = norms.Length == verts.Length, hasT = tans.Length == verts.Length, hasU0 = uv0.Length == verts.Length, hasU1 = uv1.Length == verts.Length;
                int subCount = src.subMeshCount;
                var builds = new Build[Sections];
                for (int sm = 0; sm < subCount; sm++)
                {
                    var idx = src.GetTriangles(sm);
                    for (int t = 0; t < idx.Length; t += 3)
                    {
                        int a = idx[t], b = idx[t + 1], c = idx[t + 2];
                        Vector3 cen = l2r.MultiplyPoint3x4((verts[a] + verts[b] + verts[c]) / 3f);
                        float k = (cen.z - zmin) / len + Jag(cen.x, cen.y, cen.z) * 0.085f;
                        int bin = Mathf.Clamp(Mathf.FloorToInt(k * Sections), 0, Sections - 1);
                        var bd = builds[bin] ?? (builds[bin] = new Build(subCount));
                        bd.AddTri(sm, verts, norms, tans, uv0, uv1, hasN, hasT, hasU0, hasU1, a, b, c);
                        tris++;
                    }
                }
                for (int bin = 0; bin < Sections; bin++)
                {
                    if (builds[bin] == null) continue;
                    var mesh = builds[bin].ToMesh($"{src.name}#{src.vertexCount}#{bin}");
                    meshes.Add(mesh);
                }
            }
            if (meshes.Count == 0) return;
            string asset = $"{OutDir}/{shipId}.sections.asset";
            if (File.Exists(asset)) AssetDatabase.DeleteAsset(asset);
            AssetDatabase.CreateAsset(meshes[0], asset);
            for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], meshes[0]);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MN] JuiceSetup: {shipId}: {rends.Count} hull meshes, {tris} triangles, cut into {meshes.Count} section meshes ({Sections} sections along {len:0} m) -> {asset}");
        }

        /// <summary>Smooth value noise in -1..1, so the cuts between sections are ragged.</summary>
        static float Jag(float x, float y, float z)
        {
            float v = Val(x * 0.045f, y * 0.06f, z * 0.02f) * 0.65f + Val(x * 0.13f + 7.3f, y * 0.17f + 2.1f, z * 0.06f + 4.4f) * 0.35f;
            return v * 2f - 1f;
        }

        static float Hash(int x, int y, int z)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + z * 1274126177;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffff) / 65535f;
            }
        }

        static float Val(float x, float y, float z)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y), zi = Mathf.FloorToInt(z);
            float fx = x - xi, fy = y - yi, fz = z - zi;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
            float c00 = Mathf.Lerp(Hash(xi, yi, zi), Hash(xi + 1, yi, zi), fx), c10 = Mathf.Lerp(Hash(xi, yi + 1, zi), Hash(xi + 1, yi + 1, zi), fx);
            float c01 = Mathf.Lerp(Hash(xi, yi, zi + 1), Hash(xi + 1, yi, zi + 1), fx), c11 = Mathf.Lerp(Hash(xi, yi + 1, zi + 1), Hash(xi + 1, yi + 1, zi + 1), fx);
            return Mathf.Lerp(Mathf.Lerp(c00, c10, fy), Mathf.Lerp(c01, c11, fy), fz);
        }

        /// <summary>One section of one mesh: the triangles that fall in it, with the source's vertices (shared where the source shares them).</summary>
        sealed class Build
        {
            readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>();
            readonly List<Vector4> _t = new List<Vector4>();
            readonly List<Vector2> _u0 = new List<Vector2>(), _u1 = new List<Vector2>();
            readonly Dictionary<int, int> _map = new Dictionary<int, int>();
            readonly List<int>[] _tri;
            bool _anyT, _anyU0, _anyU1, _anyN;

            public Build(int subMeshes)
            {
                _tri = new List<int>[subMeshes];
                for (int i = 0; i < subMeshes; i++) _tri[i] = new List<int>();
            }

            int Vert(int i, Vector3[] v, Vector3[] n, Vector4[] t, Vector2[] u0, Vector2[] u1, bool hasN, bool hasT, bool hasU0, bool hasU1)
            {
                if (_map.TryGetValue(i, out int id)) return id;
                id = _v.Count;
                _map[i] = id;
                _v.Add(v[i]); _n.Add(hasN ? n[i] : Vector3.up); _t.Add(hasT ? t[i] : new Vector4(1, 0, 0, 1)); _u0.Add(hasU0 ? u0[i] : Vector2.zero); _u1.Add(hasU1 ? u1[i] : Vector2.zero);
                return id;
            }

            public void AddTri(int sm, Vector3[] v, Vector3[] n, Vector4[] t, Vector2[] u0, Vector2[] u1, bool hasN, bool hasT, bool hasU0, bool hasU1, int a, int b, int c)
            {
                _anyN |= hasN; _anyT |= hasT; _anyU0 |= hasU0; _anyU1 |= hasU1;
                _tri[sm].Add(Vert(a, v, n, t, u0, u1, hasN, hasT, hasU0, hasU1));
                _tri[sm].Add(Vert(b, v, n, t, u0, u1, hasN, hasT, hasU0, hasU1));
                _tri[sm].Add(Vert(c, v, n, t, u0, u1, hasN, hasT, hasU0, hasU1));
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = _v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                m.SetVertices(_v);
                if (_anyN) m.SetNormals(_n);
                if (_anyT) m.SetTangents(_t);
                if (_anyU0) m.SetUVs(0, _u0);
                if (_anyU1) m.SetUVs(1, _u1);
                m.subMeshCount = _tri.Length;
                for (int i = 0; i < _tri.Length; i++) m.SetTriangles(_tri[i], i);
                m.RecalculateBounds();
                MeshUtility.SetMeshCompression(m, ModelImporterMeshCompression.Medium);     // a third of the size on disk; centimetres of precision are plenty for a hull in pieces
                return m;
            }
        }
    }

    /// <summary>
    /// How the explosion flipbook atlases (<c>Resources/Juice/Flip/*.png</c>, made by <c>art_src/juice_fx/build_flip.py</c>) import: data, not colour (linear, no mipmaps
    /// that would bleed one frame into the next), clamped, compressed with the high-quality format (BC7: the four channels hold independent data), one size for all so
    /// they can share one texture array.
    /// </summary>
    sealed class JuiceFlipImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Replace((char)92, (char)47).Contains("/Resources/Juice/Flip/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = false;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.isReadable = false;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = false;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = 4096;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.crunchedCompression = false;
            var ps = ti.GetDefaultPlatformTextureSettings();
            ps.maxTextureSize = 4096;
            ps.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SetPlatformTextureSettings(ps);
        }
    }
}
