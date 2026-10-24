using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Monolith.Juice
{
    /// <summary>The quad kinds of Resources/Juice/MN_Fx.shader.</summary>
    public enum FxKind
    {
        Glow = 0, Fire = 1, Streak = 2, Ring = 3, Flare = 4, Scorch = 5, Beam = 7, Flip = 8,
    }

    /// <summary>One vertex of an effect quad (see the layout notes at the top of MN_Fx.shader).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FxVertex
    {
        public Vector3 Pos;
        public Vector4 Col, T0, T1, T2, T3, T4;
    }

    /// <summary>
    /// A growing list of effect quads in one dynamic mesh, rebuilt every frame: <see cref="Begin"/>, any number of
    /// <see cref="Quad"/> calls, then <see cref="Upload"/> (all on the main thread, before the frame renders). One draw call.
    /// </summary>
    public sealed class FxBatch
    {
        static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord4, VertexAttributeFormat.Float32, 4),
        };

        readonly FxVertex[] _v;
        readonly int _capacity;
        int _count;
        public readonly Mesh Mesh;

        public int Count => _count;
        public int Capacity => _capacity;
        /// <summary>Quads dropped because the batch was full (a sign to raise the capacity).</summary>
        public int Dropped { get; private set; }

        public FxBatch(string name, int quadCapacity)
        {
            _capacity = Mathf.Min(quadCapacity, 16000);       // 16-bit indices
            _v = new FxVertex[_capacity * 4];
            Mesh = new Mesh { name = name };
            Mesh.MarkDynamic();
            Mesh.SetVertexBufferParams(_capacity * 4, Layout);
            Mesh.SetIndexBufferParams(_capacity * 6, IndexFormat.UInt16);
            var idx = new ushort[_capacity * 6];
            for (int i = 0; i < _capacity; i++)
            {
                int v = i * 4, k = i * 6;
                idx[k] = (ushort)v; idx[k + 1] = (ushort)(v + 1); idx[k + 2] = (ushort)(v + 2);
                idx[k + 3] = (ushort)(v + 2); idx[k + 4] = (ushort)(v + 1); idx[k + 5] = (ushort)(v + 3);
            }
            Mesh.SetIndexBufferData(idx, 0, 0, idx.Length);
            Mesh.subMeshCount = 1;
            Mesh.SetSubMesh(0, new SubMeshDescriptor(0, 0), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            Mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2e6f);
        }

        public void Begin()
        {
            _count = 0;
            Dropped = 0;
        }

        /// <summary>
        /// Add one quad. <paramref name="t1"/>: for oriented kinds (streak, beam) xyz = unit axis and w = half length; for billboards
        /// x = half height (0 = square) and w = rotation in radians. <paramref name="halfWidth"/> in metres.
        /// </summary>
        public void Quad(Vector3 worldCentre, Vector4 colour, FxKind kind, float seed, Vector4 t1, float halfWidth, float age,
                         float p1, float p2, float soft = 0f, float nearStart = 0f, float nearRange = 0f, float palette = 0f, Vector4 t4 = default)
        {
            if (_count >= _capacity) { Dropped++; return; }
            int i = _count * 4;
            var t2 = new Vector4(halfWidth, age, p1, p2);
            var t3 = new Vector4(soft, nearStart, nearRange, palette);
            float k = (float)kind;
            for (int c = 0; c < 4; c++)
            {
                _v[i + c] = new FxVertex
                {
                    Pos = worldCentre, Col = colour,
                    T0 = new Vector4((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, k, seed),
                    T1 = t1, T2 = t2, T3 = t3, T4 = t4,
                };
            }
            _count++;
        }

        public void Upload()
        {
            if (_count > 0) Mesh.SetVertexBufferData(_v, 0, 0, _count * 4, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
            Mesh.SetSubMesh(0, new SubMeshDescriptor(0, _count * 6), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
        }
    }
}
