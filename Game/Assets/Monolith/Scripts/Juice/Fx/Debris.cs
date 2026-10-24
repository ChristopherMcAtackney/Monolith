using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Monolith.Juice
{
    /// <summary>One lit, tumbling piece of a broken ship: a section of hull, a chunk, a shard.</summary>
    public sealed class DebrisPiece
    {
        public GameObject Go;
        public Transform T;
        public Renderer[] Rends;
        public bool[] Glows;                 // which renderers take the heat glow (the ones that are not lights already)
        /// <summary>This piece's own copies of the materials that glow (a material property block on an HDRP renderer renders wrongly, so heat goes on copies).</summary>
        public readonly List<Material> HeatMats = new List<Material>();
        public Vector3 Vel, SpinAxis;
        public float SpinRate;
        public float Age, Life, Heat, HeatLife, HeatPeak;
        public float Radius;
        public Vector3 LocalCentre;
        public readonly List<Vector3> Burn = new List<Vector3>();     // local points that keep burning and venting
        public float BurnLeft, BurnTotal, BurnRate, BurnSize;
        public float TrailRate, TrailLeft, Carry;
        public readonly List<Mesh> Meshes = new List<Mesh>();
        public bool Dead;
        public int Group;
    }

    /// <summary>
    /// Pieces of ships: the placeholder (or later real) visual is cut into lit sections by Voronoi cells, so the actual hull
    /// breaks along jagged lines and the pieces keep their materials, windows and lights, then each piece tumbles, glows
    /// hot, vents and burns for a while. Sections can be cut again (the final blast shatters them).
    /// </summary>
    public sealed class Debris
    {
        static readonly int IdEmissive = Shader.PropertyToID("_EmissiveColor");

        readonly JuiceContext _c;
        readonly Emitters _fx;
        readonly List<DebrisPiece> _pieces = new List<DebrisPiece>(128);
        readonly HashSet<Material> _heatCopies = new HashSet<Material>();
        readonly Transform _root;
        readonly HashSet<string> _unreadableWarned = new HashSet<string>();
        public int MaxPieces = 220;
        public static float LastSliceMs; public static int LastSliceTris;

        public int Count => _pieces.Count;
        public IReadOnlyList<DebrisPiece> Pieces => _pieces;

        public Debris(JuiceContext c, Emitters fx)
        {
            _c = c; _fx = fx;
            var go = new GameObject("JuiceDebris");
            go.transform.SetParent(c.Root, false);
            _root = go.transform;
        }

        // ------------------------------------------------------------------------------------------------ slicing

        sealed class PartBuild
        {
            public Renderer Source;
            public Material[] Materials;
            public List<Vector3> V = new List<Vector3>();
            public List<Vector3> N = new List<Vector3>();
            public List<Vector4> T = new List<Vector4>();
            public List<Vector2> UV = new List<Vector2>();
            public List<int>[] Tris;
        }

        sealed class CellBuild
        {
            public readonly Dictionary<Renderer, PartBuild> Parts = new Dictionary<Renderer, PartBuild>();
            public Vector3 Sum; public int Count;
        }

        /// <summary>
        /// Cut <paramref name="source"/> into one piece per seed (world positions): each triangle goes to its nearest seed. The
        /// source's renderers are switched off (the caller destroys the source). Pieces start where the source was, moving with
        /// <paramref name="inherit"/>. Returns the pieces (fewer than seeds when a cell caught no triangles).
        /// </summary>
        public List<DebrisPiece> Slice(Transform source, IList<Vector3> seeds, Vector3 inherit, int group)
        {
            var result = new List<DebrisPiece>();
            var swSlice = System.Diagnostics.Stopwatch.StartNew();
            int triCount = 0, unreadable = 0; string firstUnreadable = null;
            var rends = source.GetComponentsInChildren<MeshRenderer>(false);
            var cells = new CellBuild[seeds.Count];
            for (int i = 0; i < cells.Length; i++) cells[i] = new CellBuild();
            Quaternion rot = source.rotation;
            Quaternion inv = Quaternion.Inverse(rot);

            foreach (var r in rends)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || !r.enabled) continue;
                var mesh = mf.sharedMesh;
                if (!mesh.isReadable)
                {
                    unreadable++; if (firstUnreadable == null) firstUnreadable = mesh.name;
                    r.enabled = false;
                    continue;
                }
                var verts = mesh.vertices; var norms = mesh.normals; var tans = mesh.tangents; var uvs = mesh.uv;
                bool hasN = norms.Length == verts.Length, hasT = tans.Length == verts.Length, hasUV = uvs.Length == verts.Length;
                var l2w = r.transform.localToWorldMatrix;
                var wv = new Vector3[verts.Length];
                for (int i = 0; i < verts.Length; i++) wv[i] = l2w.MultiplyPoint3x4(verts[i]);
                int subs = mesh.subMeshCount;
                var mats = r.sharedMaterials;
                for (int sm = 0; sm < subs; sm++)
                {
                    var tris = mesh.GetTriangles(sm);
                    for (int t = 0; t < tris.Length; t += 3)
                    {
                        int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                        triCount++;
                        Vector3 cen = (wv[a] + wv[b] + wv[c]) * (1f / 3f);
                        int best = 0; float bd = float.MaxValue;
                        for (int s = 0; s < seeds.Count; s++)
                        {
                            float d = (cen - seeds[s]).sqrMagnitude;
                            if (d < bd) { bd = d; best = s; }
                        }
                        var cell = cells[best];
                        if (!cell.Parts.TryGetValue(r, out var part))
                        {
                            part = new PartBuild { Source = r, Materials = new[] { mats[Mathf.Min(sm, mats.Length - 1)] }, Tris = new List<int>[1] };
                            part.Tris[0] = new List<int>();
                            cell.Parts[r] = part;
                        }
                        // Sub-meshes beyond the first would need their own material slot; the placeholder ships have one each.
                        // Each triangle goes in twice: the outside skin, and a second skin just under it facing inward, so a torn-off
                        // piece of hull has two faces and a little thickness instead of vanishing when you see it from behind.
                        Vector3 fn = Vector3.Cross(wv[b] - wv[a], wv[c] - wv[a]);
                        fn = fn.sqrMagnitude > 1e-12f ? fn.normalized : Vector3.up;
                        var rt = r.transform;
                        AddVert(part, wv[a], hasN ? rt.TransformDirection(norms[a]) : Vector3.up, hasT ? TanOf(rt, tans[a]) : new Vector4(1, 0, 0, 1), hasUV ? uvs[a] : Vector2.zero);
                        AddVert(part, wv[b], hasN ? rt.TransformDirection(norms[b]) : Vector3.up, hasT ? TanOf(rt, tans[b]) : new Vector4(1, 0, 0, 1), hasUV ? uvs[b] : Vector2.zero);
                        AddVert(part, wv[c], hasN ? rt.TransformDirection(norms[c]) : Vector3.up, hasT ? TanOf(rt, tans[c]) : new Vector4(1, 0, 0, 1), hasUV ? uvs[c] : Vector2.zero);
                        Vector3 off = fn * 0.5f;
                        AddVert(part, wv[c] - off, -fn, hasT ? TanOf(rt, tans[c]) : new Vector4(1, 0, 0, 1), hasUV ? uvs[c] : Vector2.zero);
                        AddVert(part, wv[b] - off, -fn, hasT ? TanOf(rt, tans[b]) : new Vector4(1, 0, 0, 1), hasUV ? uvs[b] : Vector2.zero);
                        AddVert(part, wv[a] - off, -fn, hasT ? TanOf(rt, tans[a]) : new Vector4(1, 0, 0, 1), hasUV ? uvs[a] : Vector2.zero);
                        cell.Sum += cen; cell.Count++;
                    }
                }
                r.enabled = false;
            }

            for (int k = 0; k < cells.Length; k++)
            {
                var cell = cells[k];
                if (cell.Count == 0) continue;
                Vector3 pivot = cell.Sum / cell.Count;
                var go = new GameObject("piece");
                go.transform.SetParent(_root, false);
                go.transform.SetPositionAndRotation(pivot, rot);
                var piece = new DebrisPiece { Go = go, T = go.transform, Vel = inherit, Group = group };
                var rl = new List<Renderer>(); var glows = new List<bool>();
                float rad = 0f;
                foreach (var part in cell.Parts.Values)
                {
                    int n = part.V.Count;
                    var lv = new Vector3[n]; var ln = new Vector3[n]; var lt = new Vector4[n];
                    for (int i = 0; i < n; i++)
                    {
                        lv[i] = inv * (part.V[i] - pivot);
                        ln[i] = inv * part.N[i];
                        var t4 = part.T[i];
                        var tw = inv * new Vector3(t4.x, t4.y, t4.z);
                        lt[i] = new Vector4(tw.x, tw.y, tw.z, t4.w);
                        float m = lv[i].magnitude; if (m > rad) rad = m;
                    }
                    var m2 = new Mesh { name = "MN_Debris", indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                    m2.SetVertices(lv); m2.SetNormals(ln); m2.SetTangents(lt); m2.SetUVs(0, part.UV);
                    m2.SetTriangles(part.Tris[0], 0, true);
                    piece.Meshes.Add(m2);
                    var child = new GameObject("part");
                    child.transform.SetParent(go.transform, false);
                    child.AddComponent<MeshFilter>().sharedMesh = m2;
                    var mr = child.AddComponent<MeshRenderer>();
                    var mats = part.Materials;
                    bool glowsThis = true;
                    foreach (var mat0 in mats)
                        if (mat0 != null && !_heatCopies.Contains(mat0) && mat0.HasProperty(IdEmissive))
                        {
                            // A material that is already one of our heat copies glows by design; any other with an emissive colour is a light (windows, lamps).
                            var ec0 = mat0.GetColor(IdEmissive);
                            if (ec0.r + ec0.g + ec0.b > 0.01f) glowsThis = false;
                        }
                    if (glowsThis)
                    {
                        mats = (Material[])mats.Clone();
                        for (int mi = 0; mi < mats.Length; mi++)
                            if (mats[mi] != null) { mats[mi] = new Material(mats[mi]) { name = mats[mi].name.Replace(" (heat)", "") + " (heat)" }; piece.HeatMats.Add(mats[mi]); _heatCopies.Add(mats[mi]); }
                    }
                    mr.sharedMaterials = mats;
                    mr.shadowCastingMode = part.Source.shadowCastingMode;
                    mr.receiveShadows = part.Source.receiveShadows;
                    rl.Add(mr);
                    bool emissive = false;
                    foreach (var mat in part.Materials)
                        if (mat != null && mat.HasProperty(IdEmissive)) { var ec = mat.GetColor(IdEmissive); emissive |= ec.r + ec.g + ec.b > 0.01f; }
                    glows.Add(!emissive);
                }
                piece.Rends = rl.ToArray(); piece.Glows = glows.ToArray();
                piece.Radius = Mathf.Max(1f, rad);
                piece.Life = 40f;
                _pieces.Add(piece);
                result.Add(piece);
            }
            LastSliceMs = (float)swSlice.Elapsed.TotalMilliseconds; LastSliceTris = triCount;
            if (unreadable > 0 && _unreadableWarned.Add(firstUnreadable))
                MnLog.Warn($"JUICE cut: {unreadable} mesh(es) are not readable (Read/Write is off in their import settings, as ship models import) and vanish whole instead of breaking; first: '{firstUnreadable}'");
            return result;
        }

        static void AddVert(PartBuild part, Vector3 v, Vector3 n, Vector4 t, Vector2 uv)
        {
            part.Tris[0].Add(part.V.Count);
            part.V.Add(v); part.N.Add(n); part.T.Add(t); part.UV.Add(uv);
        }

        static Vector4 TanOf(Transform t, Vector4 v)
        {
            var d = t.TransformDirection(new Vector3(v.x, v.y, v.z));
            return new Vector4(d.x, d.y, d.z, v.w);
        }

        /// <summary>Run the cutting code once on a throwaway cube so the first real break-up does not pay for compiling it (a visible hitch).</summary>
        public void WarmUp()
        {
            var shader = Resources.Load<Shader>(FxPass.ShaderPath);
            if (shader == null) return;
            var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            var go = new GameObject("warm");
            go.transform.SetParent(_root, false);
            go.transform.position = Vector3.down * 1e5f;
            var mesh = new Mesh();
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < 24; i++)
            {
                var a = Random.onUnitSphere;
                v.Add(a); v.Add(a + Vector3.right * 0.3f); v.Add(a + Vector3.up * 0.3f);
                t.Add(i * 3); t.Add(i * 3 + 1); t.Add(i * 3 + 2);
            }
            mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateTangents();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            var seeds = MakeSeeds(go.transform, LocalBounds(go.transform), 3, false);
            var pieces = Slice(go.transform, seeds, Vector3.zero, 0);
            foreach (var p in pieces) { Launch(p, Vector3.zero, 0.1f, 0.5f, 1f, 1f); ApplyHeat(p); Remove(p); }
            _pieces.Clear();
            Object.Destroy(go); Object.Destroy(mesh); Object.Destroy(mat);
        }

        // ------------------------------------------------------------------------------------------------ pieces from a ship

        /// <summary>World-space seeds spread through the visual's bounds: along its length for a long ship, anywhere for a small one.</summary>
        public List<Vector3> MakeSeeds(Transform visual, Bounds localBounds, int count, bool alongLength)
        {
            var seeds = new List<Vector3>(count);
            var ext = localBounds.extents; var cen = localBounds.center;
            for (int i = 0; i < count; i++)
            {
                Vector3 local;
                if (alongLength)
                {
                    float t = (i + 0.5f) / count;
                    float z = Mathf.Lerp(-ext.z, ext.z, t) + (_c.Rng.F() - 0.5f) * (2f * ext.z / count) * 0.45f;
                    local = cen + new Vector3((_c.Rng.F() - 0.5f) * ext.x * 0.9f, (_c.Rng.F() - 0.5f) * ext.y * 0.9f, z);
                }
                else
                {
                    local = cen + new Vector3((_c.Rng.F() * 2f - 1f) * ext.x, (_c.Rng.F() * 2f - 1f) * ext.y, (_c.Rng.F() * 2f - 1f) * ext.z) * 0.85f;
                }
                seeds.Add(visual.TransformPoint(local));
            }
            return seeds;
        }

        /// <summary>The bounds of everything the visual draws, in the visual's own space.</summary>
        public static Bounds LocalBounds(Transform visual)
        {
            bool any = false; var b = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var mf in visual.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                var m = visual.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        /// <summary>Give a freshly cut piece its drift, spin and heat.</summary>
        public void Launch(DebrisPiece p, Vector3 vel, float spinRate, float heat, float heatLife, float life)
        {
            p.Vel = vel;
            p.SpinAxis = _c.Rng.OnSphere();
            p.SpinRate = spinRate;
            p.Heat = p.HeatPeak = heat; p.HeatLife = Mathf.Max(0.1f, heatLife);
            p.Life = life;
            p.TrailRate = 0f;
        }

        public void Burn(DebrisPiece p, Vector3 worldPoint, float seconds, float rate, float size)
        {
            p.Burn.Add(p.T.InverseTransformPoint(worldPoint));
            p.BurnLeft = Mathf.Max(p.BurnLeft, seconds); p.BurnTotal = Mathf.Max(p.BurnTotal, seconds);
            p.BurnRate = rate; p.BurnSize = size;
        }

        /// <summary>Cut a piece again into <paramref name="parts"/> smaller ones flung outward from <paramref name="blastCentre"/>.</summary>
        public List<DebrisPiece> Shatter(DebrisPiece parent, int parts, Vector3 blastCentre, float speedMin, float speedMax, float heat)
        {
            var bounds = LocalBounds(parent.T);
            var seeds = MakeSeeds(parent.T, bounds, parts, false);
            var kids = Slice(parent.T, seeds, parent.Vel, parent.Group);
            foreach (var k in kids)
            {
                Vector3 dir = (k.T.position - blastCentre);
                dir = dir.sqrMagnitude < 1f ? _c.Rng.OnSphere() : dir.normalized;
                dir = (dir + _c.Rng.OnSphere() * 0.45f).normalized;
                Launch(k, parent.Vel + dir * _c.Rng.Range(speedMin, speedMax), _c.Rng.Range(0.25f, 1.1f), heat, 3.6f, 30f);
                k.TrailRate = 26f; k.TrailLeft = 3.2f;
            }
            Remove(parent);
            return kids;
        }

        /// <summary>
        /// Hand a whole ship visual over to the debris system: its parts move under a new holder (the old root is left empty, for
        /// whoever owns it to destroy) and keep drifting, intact, until cut.
        /// </summary>
        public DebrisPiece Adopt(Transform visual, Vector3 velocity)
        {
            var go = new GameObject("adopted");
            go.transform.SetParent(_root, false);
            go.transform.SetPositionAndRotation(visual.position, visual.rotation);
            var kids = new List<Transform>();
            foreach (Transform ch in visual) kids.Add(ch);
            foreach (var ch in kids) ch.SetParent(go.transform, true);
            var rends = go.GetComponentsInChildren<Renderer>();
            var p = new DebrisPiece { Go = go, T = go.transform, Vel = velocity, Rends = rends, Glows = new bool[rends.Length], Life = 60f };
            _pieces.Add(p);
            return p;
        }

        /// <summary>
        /// Take a free-flying chunk of a real ship (a wrecked part's debris, already cut loose from the ship) under the effects' control:
        /// it drifts and tumbles from now on, and can trail fire.
        /// </summary>
        public DebrisPiece AdoptChunk(Transform piece, Vector3 velocity, Vector3 spinDegPerSec, float life)
        {
            piece.SetParent(_root, true);
            piece.gameObject.SetActive(true);
            var rends = piece.GetComponentsInChildren<Renderer>();
            Bounds b = default; bool any = false;
            foreach (var r in rends) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            var p = new DebrisPiece { Go = piece.gameObject, T = piece, Vel = velocity, Rends = rends, Glows = new bool[rends.Length], Life = life };
            p.Radius = any ? b.extents.magnitude : 2f;
            p.LocalCentre = any ? piece.InverseTransformPoint(b.center) : Vector3.zero;
            p.SpinAxis = spinDegPerSec.sqrMagnitude > 1e-4f ? spinDegPerSec.normalized : _c.Rng.OnSphere();
            p.SpinRate = spinDegPerSec.magnitude * Mathf.Deg2Rad;
            _pieces.Add(p);
            return p;
        }

        public void Remove(DebrisPiece p)
        {
            if (p.Dead) return;
            p.Dead = true;
            if (p.Go != null) Object.Destroy(p.Go);
            foreach (var m in p.Meshes) if (m != null) Object.Destroy(m);
            p.Meshes.Clear();
            foreach (var mat in p.HeatMats) if (mat != null) { _heatCopies.Remove(mat); Object.Destroy(mat); }
            p.HeatMats.Clear();
        }

        public void Update(float dt)
        {
            for (int i = _pieces.Count - 1; i >= 0; i--)
            {
                var p = _pieces[i];
                if (p.Dead || p.Go == null) { _pieces.RemoveAt(i); continue; }
                p.Age += dt;
                if (p.Age > p.Life) { Remove(p); _pieces.RemoveAt(i); continue; }
                p.T.position += p.Vel * dt;
                if (p.SpinRate != 0f) p.T.rotation = Quaternion.AngleAxis(p.SpinRate * Mathf.Rad2Deg * dt, p.SpinAxis) * p.T.rotation;

                // Heat: glows and fades; hot pieces shed sparks.
                if (p.Heat > 0f)
                {
                    p.Heat = Mathf.Max(0f, p.HeatPeak * Mathf.Pow(1f - Mathf.Clamp01(p.Age / p.HeatLife), 1.6f));
                    ApplyHeat(p);
                }
                if (p.TrailLeft > 0f)
                {
                    p.TrailLeft -= dt;
                    p.Carry += p.TrailRate * dt * _c.Settings.levels.density;
                    while (p.Carry >= 1f)
                    {
                        p.Carry -= 1f;
                        Vector3 at = p.T.TransformPoint(p.LocalCentre) + _c.Rng.OnSphere() * p.Radius * 0.6f;
                        _fx.Fireball(at, p.Vel * 0.95f + _c.Rng.OnSphere() * 6f, p.Radius * 0.06f + 1.2f, p.Radius * 0.14f + 3f, _c.Rng.Range(0.4f, 0.8f), 1.1f, 4.0f, 0f, 1.2f, 0.9f);
                        if (_c.Rng.Chance(0.5f)) _fx.Sparks(at, p.Vel, Vector3.up, 360f, 1, 12f, 55f, 0.4f, 0.9f, 0.13f, SparkStyle.Hot, 0.4f);
                    }
                }
                if (p.BurnLeft > 0f && p.Burn.Count > 0)
                {
                    p.BurnLeft -= dt;
                    float k = Mathf.Clamp01(p.BurnLeft / p.BurnTotal);
                    p.Carry += p.BurnRate * k * dt * _c.Settings.levels.density * p.Burn.Count;
                    while (p.Carry >= 1f)
                    {
                        p.Carry -= 1f;
                        Vector3 at = p.T.TransformPoint(p.Burn[_c.Rng.Int(0, p.Burn.Count)]) + _c.Rng.OnSphere() * p.BurnSize * 0.35f;
                        _fx.Fireball(at, p.Vel + _c.Rng.OnSphere() * 5f, p.BurnSize * 0.5f, p.BurnSize * 1.2f, _c.Rng.Range(0.5f, 0.9f), 1.3f * (0.55f + 0.45f * k), 4.5f, 0f, 1.2f, 0.8f);
                        if (_c.Rng.Chance(0.4f)) _fx.Sparks(at, p.Vel, Vector3.up, 360f, 2, 15f, 70f, 0.4f, 1.0f, 0.15f, SparkStyle.Hot, 0.4f);
                    }
                }
            }
        }

        static readonly bool NoHeat = CommandLine.Has("-mnJuiceNoHeat"), DebugHeat = CommandLine.Has("-mnJuiceDebugHeat");
        static int _heatLogs;

        void ApplyHeat(DebrisPiece p)
        {
            if (NoHeat) return;
            float h = Mathf.Clamp01(p.Heat);
            float T = 0.30f + 0.5f * h;
            var col = new Color(Mathf.Clamp01(T * 2.4f), Mathf.Clamp01((T - 0.38f) * 1.6f), Mathf.Clamp01((T - 0.85f) * 2.2f));
            // Modest: a hot hull glows dull orange, it does not become a light.
            float nits = 19660f * 0.85f * h * h * _c.Bright;
            var heat = col * nits;
            if (DebugHeat && _heatLogs++ < 6) MnLog.Info($"JUICE heat piece: h {h:0.000}, emissive nits {nits:0}, colour {col}, copies {p.HeatMats.Count}");
            for (int i = 0; i < p.HeatMats.Count; i++)
                if (p.HeatMats[i] != null) p.HeatMats[i].SetColor(IdEmissive, heat);
        }

        public void Clear()
        {
            foreach (var p in _pieces) Remove(p);
            _pieces.Clear();
        }
    }
}
