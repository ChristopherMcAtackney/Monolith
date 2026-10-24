using System.Collections.Generic;
using UnityEngine;

namespace Monolith.Juice
{
    /// <summary>
    /// The baked break-up pieces of ship hulls (made in the editor by <c>JuiceSetup.BakeSections</c>, because ship models import with
    /// unreadable meshes that a built player cannot cut): for a hull mesh, the jagged sections along the ship's length. Looked up by the
    /// source mesh's name and vertex count, so a ship model that changed since the bake is simply not found and the juice falls back.
    /// </summary>
    public static class HullSections
    {
        public const int MaxSections = 8;
        static Dictionary<string, Mesh[]> _sets;

        static void Load()
        {
            _sets = new Dictionary<string, Mesh[]>();
            foreach (var m in Resources.LoadAll<Mesh>("Juice/Sections"))
            {
                int cut = m.name.LastIndexOf('#');
                if (cut <= 0 || !int.TryParse(m.name.Substring(cut + 1), out int bin) || bin < 0 || bin >= MaxSections) continue;
                string key = m.name.Substring(0, cut);
                if (!_sets.TryGetValue(key, out var set)) _sets[key] = set = new Mesh[MaxSections];
                set[bin] = m;
            }
        }

        /// <summary>The sections cut from <paramref name="source"/>, indexed by section (entries are null where that section holds none of it), or null when there is no bake for it.</summary>
        public static Mesh[] Get(Mesh source)
        {
            if (source == null) return null;
            if (_sets == null) Load();
            return _sets.TryGetValue($"{source.name}#{source.vertexCount}", out var set) ? set : null;
        }

        /// <summary>Do the one-time work for a ship's hull now (loading the baked cuts, doubling them), at spawn rather than mid-battle.</summary>
        public static void Warm(IReadOnlyList<Renderer> hull)
        {
            if (hull == null) return;
            foreach (var r in hull)
            {
                var mf = r != null ? r.GetComponent<MeshFilter>() : null;
                var set = mf != null ? Get(mf.sharedMesh) : null;
                if (set == null) continue;
                foreach (var m in set) if (m != null) WithInnerSkin(m);
            }
        }

        static readonly Dictionary<Mesh, Mesh> _doubled = new Dictionary<Mesh, Mesh>();

        /// <summary>
        /// The section with a second skin just under its surface facing inward, so a torn-off piece of hull has two faces and a little
        /// thickness instead of vanishing when you look into it from the break. Made once per mesh and kept.
        /// </summary>
        public static Mesh WithInnerSkin(Mesh m)
        {
            if (_doubled.TryGetValue(m, out var d) && d != null) return d;
            var v = m.vertices; var n = m.normals; var t = m.tangents; var u0 = m.uv; var u1 = m.uv2;
            int vc = v.Length;
            bool hasN = n.Length == vc, hasT = t.Length == vc, hasU0 = u0.Length == vc, hasU1 = u1.Length == vc;
            var nv = new Vector3[vc * 2]; var nn = new Vector3[vc * 2]; var nt = new Vector4[vc * 2]; var n0 = new Vector2[vc * 2]; var n1 = new Vector2[vc * 2];
            for (int i = 0; i < vc; i++)
            {
                Vector3 nrm = hasN ? n[i] : Vector3.up;
                nv[i] = v[i]; nn[i] = nrm;
                nv[i + vc] = v[i] - nrm * 0.35f; nn[i + vc] = -nrm;
                if (hasT) { nt[i] = t[i]; nt[i + vc] = t[i]; }
                if (hasU0) { n0[i] = u0[i]; n0[i + vc] = u0[i]; }
                if (hasU1) { n1[i] = u1[i]; n1[i + vc] = u1[i]; }
            }
            d = new Mesh { name = m.name + "+inner", indexFormat = vc * 2 > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            d.vertices = nv; d.normals = nn;
            if (hasT) d.tangents = nt;
            if (hasU0) d.uv = n0;
            if (hasU1) d.uv2 = n1;
            d.subMeshCount = m.subMeshCount;
            for (int sm = 0; sm < m.subMeshCount; sm++)
            {
                var tris = m.GetTriangles(sm);
                var all = new int[tris.Length * 2];
                for (int i = 0; i < tris.Length; i++) all[i] = tris[i];
                for (int i = 0; i < tris.Length; i += 3) { all[tris.Length + i] = tris[i] + vc; all[tris.Length + i + 1] = tris[i + 2] + vc; all[tris.Length + i + 2] = tris[i + 1] + vc; }
                d.SetTriangles(all, sm);
            }
            d.RecalculateBounds();
            _doubled[m] = d;
            return d;
        }
    }
}
