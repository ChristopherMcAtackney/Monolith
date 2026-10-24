using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Monolith.View
{
    /// <summary>
    /// FS2-style motion dust: specks fixed in world space within a radius of the eye, drawn as short streaks along the
    /// player's velocity so speed reads anywhere. Specks that fall out of the radius come back on the far side, ahead of
    /// the direction of travel. Rebuilt every frame from the interpolated camera pose, camera-relative (pass 1 of
    /// Hidden/Monolith/Lines). Repeatable: a seeded generator, stepped only when a speck respawns.
    /// </summary>
    public sealed class SpaceDust
    {
        readonly ViewSettings.DustSettings _s;
        readonly Vector3[] _points;
        readonly System.Random _rng;
        readonly Mesh _mesh;
        readonly List<Vector3> _pos;
        readonly List<Vector4> _other, _along;
        readonly List<Color> _col;

        public Mesh Mesh => _mesh;
        public float HalfWidthPx => _s.halfWidthPx;

        public SpaceDust(ViewSettings.DustSettings settings, Vector3 around)
        {
            _s = settings;
            int n = Mathf.Max(0, settings.count);
            _points = new Vector3[n];
            _rng = new System.Random(settings.seed);
            for (int i = 0; i < n; i++) _points[i] = around + InsideSphere() * settings.radius;

            _mesh = new Mesh { name = "MN_SpaceDust" };
            _mesh.MarkDynamic();
            _pos = new List<Vector3>(n * 4);
            _other = new List<Vector4>(n * 4);
            _along = new List<Vector4>(n * 4);
            _col = new List<Color>(n * 4);
            for (int i = 0; i < n * 4; i++) { _pos.Add(Vector3.zero); _other.Add(Vector4.zero); _along.Add(Vector4.zero); _col.Add(Color.clear); }
            _mesh.SetVertices(_pos);
            _mesh.SetUVs(0, _other);
            _mesh.SetUVs(1, _along);
            _mesh.SetColors(_col);
            var idx = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                int v = i * 4, k = i * 6;
                idx[k] = v; idx[k + 1] = v + 1; idx[k + 2] = v + 2;
                idx[k + 3] = v + 2; idx[k + 4] = v + 1; idx[k + 5] = v + 3;
            }
            _mesh.SetIndices(idx, MeshTopology.Triangles, 0, false);
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
        }

        /// <summary>Rebuild the streaks for an eye at <paramref name="eye"/> looking along <paramref name="forward"/>, moving at <paramref name="velocity"/>.</summary>
        public void Update(Vector3 eye, Vector3 forward, Vector3 velocity, float near, float stretch = 1f, float brighten = 1f)
        {
            float r = _s.radius, r2 = r * r;
            float speed = velocity.magnitude;
            Vector3 travel = speed > 1f ? velocity / speed : Vector3.zero;
            Vector3 tailOffset = velocity * (_s.streakSeconds * stretch);   // where the speck was, relative to us, a moment ago (the boost draws it longer)
            float minAhead = near * 4f;
            var baseCol = new Color(0.78f, 0.80f, 0.86f) * (_s.brightness * brighten);

            for (int i = 0; i < _points.Length; i++)
            {
                Vector3 rel = _points[i] - eye;
                if (rel.sqrMagnitude > r2)
                {
                    Vector3 d = OnSphere();
                    if (Vector3.Dot(d, travel) < 0f) d = -d;            // come back ahead of us, not behind
                    _points[i] = eye + d * (r * (0.92f + 0.08f * (float)_rng.NextDouble()));
                    rel = _points[i] - eye;
                }
                Vector3 head = rel, tail = rel + tailOffset;
                float dist = rel.magnitude;
                float fade = Mathf.Clamp01((r - dist) / (0.25f * r)) * Mathf.Clamp01((dist - _s.nearFade) / _s.nearFade);
                bool visible = fade > 0f && Vector3.Dot(head, forward) > minAhead && Vector3.Dot(tail, forward) > minAhead;
                int v = i * 4;
                if (!visible)
                {
                    Vector3 park = forward * 10f;
                    for (int j = 0; j < 4; j++) { _pos[v + j] = park; _other[v + j] = new Vector4(park.x, park.y, park.z, 0f); _col[v + j] = Color.clear; }
                    continue;
                }
                var c = baseCol;
                c.a = fade * fade;
                _pos[v] = head; _other[v] = new Vector4(tail.x, tail.y, tail.z, -1f); _along[v] = new Vector4(-1f, 0f, 0f, 0f);
                _pos[v + 1] = head; _other[v + 1] = new Vector4(tail.x, tail.y, tail.z, 1f); _along[v + 1] = new Vector4(-1f, 0f, 0f, 0f);
                _pos[v + 2] = tail; _other[v + 2] = new Vector4(head.x, head.y, head.z, -1f); _along[v + 2] = new Vector4(1f, 0f, 0f, 0f);
                _pos[v + 3] = tail; _other[v + 3] = new Vector4(head.x, head.y, head.z, 1f); _along[v + 3] = new Vector4(1f, 0f, 0f, 0f);
                _col[v] = _col[v + 1] = _col[v + 2] = _col[v + 3] = c;
            }
            _mesh.SetVertices(_pos);
            _mesh.SetUVs(0, _other);
            _mesh.SetUVs(1, _along);
            _mesh.SetColors(_col);
        }

        Vector3 InsideSphere()
        {
            while (true)
            {
                var p = new Vector3((float)_rng.NextDouble() * 2f - 1f, (float)_rng.NextDouble() * 2f - 1f, (float)_rng.NextDouble() * 2f - 1f);
                if (p.sqrMagnitude <= 1f) return p;
            }
        }

        Vector3 OnSphere()
        {
            while (true)
            {
                var p = InsideSphere();
                float m = p.magnitude;
                if (m > 0.05f) return p / m;
            }
        }
    }
}
