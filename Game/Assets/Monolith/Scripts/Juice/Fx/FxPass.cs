using System.Collections.Generic;
using System.IO;
using Monolith.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.Juice
{
    /// <summary>One shield bubble to draw this frame: its pose, size, colour and the hits rippling over it.</summary>
    public sealed class ShieldDraw
    {
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.identity;
        public Vector3 Radii = Vector3.one;
        public Vector3 Colour;
        public float Rim, RippleSpeed = 3.2f, RippleWidth = 0.22f, HexScale = 9f;
        /// <summary>Unit direction (xyz) in the sphere's own space and age in seconds (w, negative = unused).</summary>
        public readonly Vector4[] Hits = new Vector4[8];
    }

    /// <summary>Everything the effect pass draws this frame. Filled by the director in LateUpdate, read by <see cref="FxPass"/> at render time.</summary>
    public sealed class FxFrame
    {
        public FxBatch Add, Alpha;
        public readonly List<ShieldDraw> Shields = new List<ShieldDraw>();
        public float Time;
        public float NitsPerUnit = 19660.8f;
        public float MinStreakPx = 2.0f;
        public Vector3 EdgeColour;
        public Vector4 EdgeParams = new Vector4(0.55f, 1.0f, 0.5f, 0f);
        /// <summary>The Sun for lit smoke: direction toward it (w = 1 when there is one), the radiance of a white surface facing it, and the sky's fill, all in display units.</summary>
        public Vector4 SunDir = new Vector4(0f, 1f, 0f, 0f);
        public Vector4 SunCol = new Vector4(1.6f, 1.55f, 1.4f, 0f);
        public Vector4 Fill = new Vector4(0.065f, 0.07f, 0.085f, 0f);
        public bool Enabled = true;

        /// <summary>
        /// The boost's lens (pass 4 of MN_Fx, see <see cref="BoostFx"/>): a radial blur and colour fringe that grow toward the rim of the picture
        /// (<see cref="BoostBlur"/> is the zoom-blur span at the rim as a fraction of the distance from the centre, <see cref="BoostChroma"/> the
        /// red/blue split likewise), and a faint glow at the rim (display units). Nothing inside <see cref="BoostZone"/>.x (a fraction of the half
        /// diagonal) is touched, nor a disc round the target (<see cref="BoostSafe"/>: x, y on screen 0..1 with y down, z radius in screen heights).
        /// </summary>
        public float BoostBlur, BoostChroma;
        public Vector3 BoostGlow;
        public Vector4 BoostZone = new Vector4(0.34f, 0.98f, 0f, 0f);
        public Vector3 BoostSafe = new Vector3(0.5f, 0.5f, 0f);
        public bool BoostActive => BoostBlur > 0.0015f || BoostChroma > 0.0003f || BoostGlow.sqrMagnitude > 1e-6f;

        /// <summary>The death overlay (pass 5 of MN_Fx, see <see cref="DeathFx"/>): A = iris radius, softness, black, cracks; B = impact point (screen, y down), crack growth, seed; C = light added (rgb), red at the iris edge.</summary>
        public bool DeathActive;
        public Vector4 DeathA = new Vector4(2f, 0.2f, 0f, 0f), DeathB = new Vector4(0.5f, 0.4f, 0f, 1f), DeathC;
    }

    /// <summary>
    /// The one HDRP custom pass that draws every juice effect into the HDR colour buffer before post-processing (so bloom
    /// spreads it), depth-tested against the scene. Camera-relative maths, as the view's dust pass does.
    /// </summary>
    public sealed class FxPass : CustomPass
    {
        public const string ShaderPath = "Juice/MN_Fx";

        public FxFrame Frame;
        public Camera Camera;
        Material _material;
        readonly MaterialPropertyBlock _props = new MaterialPropertyBlock();
        Mesh _shieldMesh;

        static readonly int IdMatrix = Shader.PropertyToID("_MN_Matrix"), IdRight = Shader.PropertyToID("_MN_CamRight"),
            IdUp = Shader.PropertyToID("_MN_CamUp"), IdFwd = Shader.PropertyToID("_MN_CamFwd"), IdEye = Shader.PropertyToID("_MN_Eye"),
            IdScreen = Shader.PropertyToID("_MN_Screen"), IdParams = Shader.PropertyToID("_MN_Params"),
            IdObject = Shader.PropertyToID("_MN_Object"), IdObjectRot = Shader.PropertyToID("_MN_ObjectRot"),
            IdRadii = Shader.PropertyToID("_ShieldRadii"), IdShieldColour = Shader.PropertyToID("_ShieldColor"),
            IdShieldParams = Shader.PropertyToID("_ShieldParams"), IdShieldHit = Shader.PropertyToID("_ShieldHit"),
            IdEdgeColour = Shader.PropertyToID("_MN_EdgeColor"), IdEdgeParams = Shader.PropertyToID("_MN_EdgeParams"),
            IdNoise = Shader.PropertyToID("_MN_Noise3D"), IdFlip = Shader.PropertyToID("_MN_Flip"), IdSunDir = Shader.PropertyToID("_MN_SunDir"),
            IdSunCol = Shader.PropertyToID("_MN_SunCol"), IdFill = Shader.PropertyToID("_MN_Fill");

        protected override bool executeInSceneView => false;

        RTHandle _copy;                                   // the boost lens reads the picture from here
        static readonly int IdSceneCopy = Shader.PropertyToID("_MN_SceneCopy"), IdBoostA = Shader.PropertyToID("_MN_BoostA"),
            IdBoostZone = Shader.PropertyToID("_MN_BoostZone"), IdBoostSafe = Shader.PropertyToID("_MN_BoostSafe"), IdBoostGlow = Shader.PropertyToID("_MN_BoostGlow");
        static readonly int IdDeathA = Shader.PropertyToID("_MN_DeathA"), IdDeathB = Shader.PropertyToID("_MN_DeathB"), IdDeathC = Shader.PropertyToID("_MN_DeathC");

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            var shader = Resources.Load<Shader>(ShaderPath) ?? throw new FileNotFoundException($"Resources/{ShaderPath}.shader is missing");
            _material = new Material(shader) { name = "MN_Fx", hideFlags = HideFlags.HideAndDontSave };
            _shieldMesh = SphereMesh.Icosphere(3);
            _copy = RTHandles.Alloc(Vector2.one, TextureXR.slices, dimension: TextureXR.dimension, colorFormat: UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat,
                                    useDynamicScale: true, name: "MN boost lens copy");
        }

        /// <summary>The boost lens: copy the picture, then redraw the rim of it blurred along the radius and fringed, leaving the middle and the target alone.</summary>
        void DrawBoostLens(CustomPassContext ctx, float w, float h)
        {
            CustomPassUtils.Copy(ctx, ctx.cameraColorBuffer, _copy);
            var p = _props;
            p.Clear();
            p.SetTexture(IdSceneCopy, _copy);
            p.SetVector(IdScreen, new Vector4(w, h, 1f / w, 1f / h));
            p.SetVector(IdParams, new Vector4(Frame.NitsPerUnit, 1f, 1f, Frame.Time));
            p.SetVector(IdBoostA, new Vector4(Frame.BoostBlur, Frame.BoostChroma, 0f, 0f));
            p.SetVector(IdBoostZone, Frame.BoostZone);
            p.SetVector(IdBoostSafe, Frame.BoostSafe);
            p.SetVector(IdBoostGlow, Frame.BoostGlow);
            CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ClearFlag.None);
            ctx.cmd.DrawProcedural(Matrix4x4.identity, _material, 4, MeshTopology.Triangles, 3, 1, p);
        }

        protected override void Execute(CustomPassContext ctx)
        {
            var cam = ctx.hdCamera.camera;
            if (cam != Camera || _material == null || Frame == null || !Frame.Enabled) return;
            float w = ctx.hdCamera.actualWidth, h = ctx.hdCamera.actualHeight;
            var tr = cam.transform;
            var view = cam.worldToCameraMatrix;
            view.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
            var m = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true) * view;
            float focal = h * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Vector3 eye = tr.position;

            // The boost's lens first, so the flames and flashes drawn next stay crisp on top of it.
            if (Frame.BoostActive && _copy != null) DrawBoostLens(ctx, w, h);

            var p = _props;
            p.Clear();
            p.SetMatrix(IdMatrix, m);
            p.SetVector(IdRight, tr.right);
            p.SetVector(IdUp, tr.up);
            p.SetVector(IdFwd, tr.forward);
            p.SetVector(IdEye, eye);
            p.SetVector(IdScreen, new Vector4(w, h, 1f / w, 1f / h));
            p.SetTexture(IdNoise, FxNoise.Get());
            var flipArray = FlipLibrary.Array;
            p.SetTexture(IdFlip, flipArray != null ? (Texture)flipArray : Texture2D.blackTexture);
            p.SetVector(IdSunDir, Frame.SunDir);
            p.SetVector(IdSunCol, Frame.SunCol);
            p.SetVector(IdFill, Frame.Fill);
            p.SetVector(IdParams, new Vector4(Frame.NitsPerUnit, focal, Frame.MinStreakPx * Mathf.Max(1f, h / 1080f), Frame.Time));

            CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ctx.cameraDepthBuffer, ClearFlag.None);
            if (Frame.Alpha.Count > 0) ctx.cmd.DrawMesh(Frame.Alpha.Mesh, Matrix4x4.identity, _material, 0, 1, p);
            if (Frame.Add.Count > 0) ctx.cmd.DrawMesh(Frame.Add.Mesh, Matrix4x4.identity, _material, 0, 0, p);

            for (int i = 0; i < Frame.Shields.Count; i++)
            {
                var s = Frame.Shields[i];
                var rot = Matrix4x4.Rotate(s.Rotation);
                var obj = Matrix4x4.TRS(s.Position - eye, s.Rotation, s.Radii);
                p.SetMatrix(IdObject, obj);
                p.SetMatrix(IdObjectRot, rot);
                p.SetVector(IdRadii, s.Radii);
                p.SetVector(IdShieldColour, s.Colour);
                p.SetVector(IdShieldParams, new Vector4(s.Rim, s.RippleSpeed, s.RippleWidth, s.HexScale));
                p.SetVectorArray(IdShieldHit, s.Hits);
                ctx.cmd.DrawMesh(_shieldMesh, Matrix4x4.identity, _material, 0, 2, p);
            }

            if (Frame.EdgeColour.sqrMagnitude > 1e-6f)
            {
                p.SetVector(IdEdgeColour, Frame.EdgeColour);
                p.SetVector(IdEdgeParams, Frame.EdgeParams);
                ctx.cmd.DrawProcedural(Matrix4x4.identity, _material, 3, MeshTopology.Triangles, 3, 1, p);
            }

            if (Frame.DeathActive)
            {
                p.SetVector(IdDeathA, Frame.DeathA);
                p.SetVector(IdDeathB, Frame.DeathB);
                p.SetVector(IdDeathC, Frame.DeathC);
                ctx.cmd.DrawProcedural(Matrix4x4.identity, _material, 5, MeshTopology.Triangles, 3, 1, p);
            }
        }

        protected override void Cleanup()
        {
            if (_material != null) Object.Destroy(_material);
            if (_shieldMesh != null) Object.Destroy(_shieldMesh);
            if (_copy != null) { RTHandles.Release(_copy); _copy = null; }
        }
    }

    /// <summary>A unit icosphere (the shield bubble's mesh; the shader scales it to the ship's ellipsoid).</summary>
    public static class SphereMesh
    {
        public static Mesh Icosphere(int subdivisions)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            var tris = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            var cache = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(key, out int m)) return m;
                v.Add(((v[a] + v[b]) * 0.5f).normalized);
                cache[key] = v.Count - 1;
                return v.Count - 1;
            }
            for (int s = 0; s < subdivisions; s++)
            {
                var next = new List<int>(tris.Count * 4);
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                tris = next;
            }
            // The base icosahedron's faces wind outward when seen from outside with Unity's clockwise-front convention flipped:
            // flip so Cull Back keeps the outside.
            for (int i = 0; i < tris.Count; i += 3) { int tmp = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = tmp; }
            var mesh = new Mesh { name = "MN_ShieldSphere", indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(v);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2.1f);
            return mesh;
        }
    }
}
