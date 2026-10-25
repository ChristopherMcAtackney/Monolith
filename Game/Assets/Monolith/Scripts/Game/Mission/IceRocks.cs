using System;
using System.Collections.Generic;
using Monolith.Mission;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// Bodies of ice for the Oort Cloud, made in code until the Blender ones exist: a displaced, cratered icosphere (seeded, so the same numbers
    /// always make the same rock), stretched per axis, with a dark dusty crust broken by bright veins and patches of exposed ice (comet nuclei are
    /// among the darkest things in the solar system; the ice is what catches the light). Used for a mission's scenery (drawn only: nothing collides
    /// with it or shoots it) and for the comet nucleus a placeholder station is built into (<see cref="MissionFile.LookDef"/>). The material is an
    /// instance of the editor-made plated HDRP/Lit asset (Resources/Flight/MN_FlightPlated), as the placeholder hulls' are.
    /// </summary>
    public static class IceRocks
    {
        static Material _mat;
        static readonly Dictionary<int, Mesh> Meshes = new Dictionary<int, Mesh>();

        public static Material Material
        {
            get
            {
                if (_mat != null) return _mat;
                // The comet outpost's own crust (ShipArt's MN_OortRock: art_src/oort_art), when it is there: the field's rocks then are the same dark dusty stuff as the nucleus the
                // station is built into, a little paler, instead of the generated pale ice below. Only the surface: the shapes, the placement and the spin stay this file's.
                var crust = Resources.Load<Material>("ShipArt/Materials/MN_OortRock");
                if (crust != null)
                {
                    _mat = new Material(crust) { name = "MN_Ice" };
                    _mat.SetColor("_BaseColor", new Color(1.35f, 1.35f, 1.4f));
                    return _mat;
                }
                var plated = Resources.Load<Material>("Flight/MN_FlightPlated") ?? throw new InvalidOperationException("Resources/Flight/MN_FlightPlated is missing: run Setup.Project, then build");
                Textures(512, 31, out var albedo, out var normal, out var mask);
                _mat = new Material(plated) { name = "MN_Ice" };
                _mat.SetColor("_BaseColor", new Color(0.92f, 0.95f, 1f));
                _mat.SetTexture("_BaseColorMap", albedo);
                _mat.SetTexture("_NormalMap", normal);
                _mat.SetTexture("_MaskMap", mask);
                _mat.SetFloat("_NormalScale", 1.4f);
                _mat.SetFloat("_MetallicRemapMin", 0f); _mat.SetFloat("_MetallicRemapMax", 0f);
                _mat.SetFloat("_SmoothnessRemapMin", 0f); _mat.SetFloat("_SmoothnessRemapMax", 0.85f);
                _mat.SetFloat("_AORemapMin", 0.2f); _mat.SetFloat("_AORemapMax", 1f);
                return _mat;
            }
        }

        /// <summary>A rock of ice about <paramref name="radius"/> metres, stretched per axis, as a child of <paramref name="parent"/>.</summary>
        public static GameObject Rock(string name, float radius, Vector3 stretch, int seed, Transform parent)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = MeshFor(seed);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Material;
            go.transform.localScale = Vector3.Scale(stretch, Vector3.one * radius);
            return go;
        }

        /// <summary>Build a mission's scenery: its single rocks and its fields, under one root. Rocks turn slowly (drawn only).</summary>
        public static Transform Scenery(MissionFile.SceneryDef sc, Transform parent)
        {
            var root = new GameObject("Scenery").transform;
            root.SetParent(parent, false);
            int n = 0;
            // The Blender comets (CometField) when they are built; the generated rocks remain as the fallback. The rocks' places, sizes, stretches and spins come from the mission and its random
            // stream and are the same either way: what a rock looks like is chosen by a stream of its own (CometField.Make), never by this one.
            bool comets = CometField.Available;
            foreach (var k in sc.Rocks)
            {
                var go = comets ? CometField.Make("Ice " + (++n), k.Radius, k.Stretch, 7001 + k.Seed * 13 + n, root) : Rock("Ice " + (++n), k.Radius, k.Stretch, k.Seed, root);
                if (Monolith.Core.CommandLine.Has("-mnLogScenery")) Monolith.Core.MnLog.Info($"SCENERY single {n} at {k.At.x:0.#} {k.At.y:0.#} {k.At.z:0.#} radius {k.Radius:0.#} stretch {k.Stretch.x:0.##} {k.Stretch.y:0.##} {k.Stretch.z:0.##} seed {k.Seed}");
                go.transform.position = k.At;
                go.transform.rotation = Quaternion.Euler(k.Seed * 37f % 360f, k.Seed * 71f % 360f, k.Seed * 13f % 360f);
                Spin.Add(go, k.Spin, k.Seed);
            }
            foreach (var f in sc.Fields)
            {
                var rng = new System.Random(f.Seed);
                float R() => (float)rng.NextDouble();
                int placed = 0, tries = 0;
                while (placed < f.Count && tries++ < f.Count * 20)
                {
                    var p = f.Centre + new Vector3((R() - 0.5f) * f.Size.x, (R() - 0.5f) * f.Size.y, (R() - 0.5f) * f.Size.z);
                    // Many small, few large: a power law between the bounds.
                    float radius = Mathf.Lerp(f.RadiusMin, f.RadiusMax, Mathf.Pow(R(), 3.2f));
                    bool clear = true;
                    foreach (var c in f.Clear) if (Vector3.Distance(p, new Vector3(c.x, c.y, c.z)) < c.w + radius) { clear = false; break; }
                    if (!clear) continue;
                    var stretch = new Vector3(0.75f + 0.5f * R(), 0.7f + 0.4f * R(), 0.75f + 0.6f * R());
                    var go = comets ? CometField.Make("Ice " + (++n), radius, stretch, 9001 + f.Seed * 977 + placed, root) : Rock("Ice " + (++n), radius, stretch, f.Seed * 131 + placed % 12, root);
                    if (Monolith.Core.CommandLine.Has("-mnLogScenery")) Monolith.Core.MnLog.Info($"SCENERY field {n} at {p.x:0.#} {p.y:0.#} {p.z:0.#} radius {radius:0.#} stretch {stretch.x:0.##} {stretch.y:0.##} {stretch.z:0.##} seed {f.Seed * 131 + placed % 12}");
                    go.transform.position = p;
                    go.transform.rotation = Quaternion.Euler(R() * 360f, R() * 360f, R() * 360f);
                    Spin.Add(go, f.Spin * (0.3f + R()) * (R() < 0.5f ? -1f : 1f), f.Seed + placed);
                    placed++;
                }
            }
            return root;
        }

        // ------------------------------------------------------------------------------------------------ the mesh

        static Mesh MeshFor(int seed)
        {
            if (Meshes.TryGetValue(seed, out var m) && m != null) return m;
            m = Build(seed);
            Meshes[seed] = m;
            return m;
        }

        /// <summary>A unit-radius rock: an icosphere (four subdivisions) pushed in and out by layered noise, with a few craters pressed into it.</summary>
        static Mesh Build(int seed)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            Icosphere(4, verts, tris);
            var rng = new System.Random(seed * 7919 + 17);
            float R() => (float)rng.NextDouble();
            var off = new Vector3(R() * 100f, R() * 100f, R() * 100f);
            int craters = 3 + rng.Next(6);
            var cDir = new Vector3[craters]; var cSize = new float[craters]; var cDepth = new float[craters];
            for (int i = 0; i < craters; i++) { cDir[i] = new Vector3(R() * 2f - 1f, R() * 2f - 1f, R() * 2f - 1f).normalized; cSize[i] = 0.18f + 0.35f * R(); cDepth[i] = 0.05f + 0.1f * R(); }
            float lumpy = 0.18f + 0.14f * R();
            for (int i = 0; i < verts.Count; i++)
            {
                var d = verts[i];
                float h = 1f + lumpy * (Fbm(d * 1.1f + off, 5) - 0.5f) * 2f;
                for (int c = 0; c < craters; c++)
                {
                    float a = Vector3.Angle(d, cDir[c]) * Mathf.Deg2Rad;
                    float t = a / cSize[c];
                    if (t < 1.25f) h -= cDepth[c] * (t < 1f ? (1f - t * t) : 0f) - cDepth[c] * 0.35f * Mathf.Exp(-Mathf.Pow((t - 1f) * 6f, 2f));   // bowl and raised rim
                }
                verts[i] = d * h;
            }
            // Smooth normals from the welded mesh, then every triangle gets its own corners: each is projected on the one plane its face looks at
            // most (a triplanar look without a triplanar shader). Picking the plane per corner instead smeared the texture into bands wherever a
            // triangle's corners chose different planes.
            var welded = new Mesh();
            welded.SetVertices(verts);
            welded.SetTriangles(tris, 0);
            welded.RecalculateNormals();
            var smooth = welded.normals;
            UnityEngine.Object.Destroy(welded);
            var fv = new List<Vector3>(tris.Count); var fn = new List<Vector3>(tris.Count); var fuv = new List<Vector2>(tris.Count); var ft = new List<int>(tris.Count);
            for (int t = 0; t < tris.Count; t += 3)
            {
                Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], c = verts[tris[t + 2]];
                var n = Vector3.Cross(b - a, c - a);
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                int axis = ax > ay && ax > az ? 0 : ay > az ? 1 : 2;
                for (int k = 0; k < 3; k++)
                {
                    var v = verts[tris[t + k]]; var p = v * 1.6f;
                    fv.Add(v); fn.Add(smooth[tris[t + k]]); ft.Add(t + k);
                    fuv.Add(axis == 0 ? new Vector2(p.z, p.y) : axis == 1 ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y));
                }
            }
            var mesh = new Mesh { name = "MN_IceRock_" + seed };
            mesh.SetVertices(fv);
            mesh.SetNormals(fn);
            mesh.SetUVs(0, fuv);
            var fuv1 = new List<Vector2>(fuv.Count); foreach (var q in fuv) fuv1.Add(q * 36f);      // the detail map's set (the outpost's crust material reads it): a rock is a few metres to a few hundred across, so one scale for all
            mesh.SetUVs(1, fuv1);
            mesh.SetTriangles(ft, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void Icosphere(int subdivisions, List<Vector3> verts, List<int> tris)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            foreach (var v in new[] { new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0), new Vector3(0, -1, t), new Vector3(0, 1, t),
                                      new Vector3(0, -1, -t), new Vector3(0, 1, -t), new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) })
                verts.Add(v.normalized);
            tris.AddRange(new[] { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                                  3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 });
            for (int s = 0; s < subdivisions; s++)
            {
                var mid = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (mid.TryGetValue(key, out int i)) return i;
                    verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
                    mid[key] = verts.Count - 1;
                    return verts.Count - 1;
                }
                var next = new List<int>(tris.Count * 4);
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                tris.Clear(); tris.AddRange(next);
            }
        }

        // ------------------------------------------------------------------------------------------------ noise

        static float Hash(int x, int y, int z)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + z * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777216f;
            }
        }

        static float Value(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
            float fx = p.x - x, fy = p.y - y, fz = p.z - z;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
            float L(float a, float b, float t) => a + (b - a) * t;
            return L(L(L(Hash(x, y, z), Hash(x + 1, y, z), fx), L(Hash(x, y + 1, z), Hash(x + 1, y + 1, z), fx), fy),
                     L(L(Hash(x, y, z + 1), Hash(x + 1, y, z + 1), fx), L(Hash(x, y + 1, z + 1), Hash(x + 1, y + 1, z + 1), fx), fy), fz);
        }

        static float Fbm(Vector3 p, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++) { sum += amp * Value(p); norm += amp; p *= 2.03f; amp *= 0.5f; }
            return sum / norm;
        }

        // ------------------------------------------------------------------------------------------------ the textures

        /// <summary>
        /// One tiling tile of comet surface: a dark dusty crust (grey-brown, matte, pitted) with bright, smoother ice in veins and patches where it has
        /// cracked or been scoured. Albedo (sRGB), tangent-space normal from the height, and the HDRP mask (metallic 0, AO, -, smoothness).
        /// </summary>
        static void Textures(int size, int seed, out Texture2D albedo, out Texture2D normal, out Texture2D mask)
        {
            var height = new float[size * size];
            var ice = new float[size * size];
            var o = new Vector3(seed * 3.1f, seed * 1.7f, seed * 5.3f);
            float P(int x, int y, float f, int oct) =>
                // Tile by sampling a torus in 4D folded into 3D: cos/sin on both axes.
                Fbm(new Vector3(Mathf.Cos(x * 2f * Mathf.PI / size) * f, Mathf.Sin(x * 2f * Mathf.PI / size) * f + Mathf.Cos(y * 2f * Mathf.PI / size) * f * 0.7f, Mathf.Sin(y * 2f * Mathf.PI / size) * f) + o, oct);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float big = P(x, y, 2.2f, 4), fine = P(x, y, 9f, 3);
                    float vein = 1f - Mathf.Abs(P(x, y, 3.5f, 4) - 0.5f) * 2f;               // ridged: thin bright lines
                    float i = Mathf.Clamp01((vein - 0.82f) * 6f) + Mathf.Clamp01((big - 0.62f) * 4f);
                    ice[y * size + x] = Mathf.Clamp01(i);
                    height[y * size + x] = big * 0.6f + fine * 0.4f - ice[y * size + x] * 0.12f;
                }
            albedo = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = "MN_IceAlbedo", wrapMode = TextureWrapMode.Repeat };
            normal = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = "MN_IceNormal", wrapMode = TextureWrapMode.Repeat };
            mask = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = "MN_IceMask", wrapMode = TextureWrapMode.Repeat };
            var a = new Color32[size * size]; var n = new Color32[size * size]; var m = new Color32[size * size];
            var crustA = new Color(0.20f, 0.18f, 0.16f); var crustB = new Color(0.30f, 0.27f, 0.24f); var iceC = new Color(0.78f, 0.86f, 0.95f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int k = y * size + x;
                    float h = height[k], i = ice[k];
                    var crust = Color.Lerp(crustA, crustB, h);
                    var c = Color.Lerp(crust, iceC, i);
                    a[k] = c;
                    float hx = height[y * size + (x + 1) % size] - height[y * size + (x + size - 1) % size];
                    float hy = height[((y + 1) % size) * size + x] - height[((y + size - 1) % size) * size + x];
                    var nv = new Vector3(-hx * 6f, -hy * 6f, 1f).normalized;
                    n[k] = new Color(nv.x * 0.5f + 0.5f, nv.y * 0.5f + 0.5f, nv.z * 0.5f + 0.5f, 1f);
                    float ao = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(h * 1.4f));
                    m[k] = new Color(0f, ao, 0f, Mathf.Lerp(0.12f, 0.8f, i));
                }
            albedo.SetPixels32(a); albedo.Apply(true, true);
            normal.SetPixels32(n); normal.Apply(true, true);
            mask.SetPixels32(m); mask.Apply(true, true);
        }

        /// <summary>A rock's slow tumble (drawn only).</summary>
        sealed class Spin : MonoBehaviour
        {
            Vector3 _axis; float _degPerSec;
            public static void Add(GameObject go, float degPerSec, int seed)
            {
                if (Mathf.Abs(degPerSec) < 1e-3f) return;
                var s = go.AddComponent<Spin>();
                var r = new System.Random(seed);
                s._axis = new Vector3((float)r.NextDouble() - 0.5f, (float)r.NextDouble() - 0.5f, (float)r.NextDouble() - 0.5f).normalized;
                s._degPerSec = degPerSec;
            }
            void Update() => transform.Rotate(_axis, _degPerSec * Time.deltaTime, Space.World);
        }
    }
}
