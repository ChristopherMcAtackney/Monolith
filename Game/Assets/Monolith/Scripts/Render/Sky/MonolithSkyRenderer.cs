using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.Sky
{
    /// <summary>
    /// Draws the Monolith sky for HDRP. Created by HDRP (by reflection, from <see cref="MonolithSky.GetSkyRendererType"/>).
    /// For the lighting cubemap it draws the Milky Way and the ambient fill; for the camera it draws the same plus the Sun
    /// and one quad per catalogue star. Every per-frame value goes through a property block so it is captured with the draw.
    /// </summary>
    public sealed class MonolithSkyRenderer : SkyRenderer
    {
        const string ShaderPath = "MonolithSky/MonolithSky";
        const string MilkyWayPath = "MonolithSky/milkyway";
        const int PassBake = 0, PassRender = 1, PassStars = 2;
        const float MilkyWayTexelRadians = 2f * Mathf.PI / 4096f;      // the map is 4096 texels around the equator

        Material _material;
        Texture2D _milkyWay;
        StarCatalog _stars;
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();

        static readonly int IdPixelCoordToViewDir = Shader.PropertyToID("_PixelCoordToViewDirWS");
        static readonly int IdMilkyWay = Shader.PropertyToID("_MilkyWay");
        static readonly int IdSkyToWorld = Shader.PropertyToID("_SkyToWorld");
        static readonly int IdWorldToSky = Shader.PropertyToID("_WorldToSky");
        static readonly int IdSkyViewProj = Shader.PropertyToID("_SkyViewProj");
        static readonly int IdMilkyWayParams = Shader.PropertyToID("_MilkyWayParams");
        static readonly int IdBlackLift = Shader.PropertyToID("_BlackLift");
        static readonly int IdMilkyWayTint = Shader.PropertyToID("_MilkyWayTint");
        static readonly int IdAmbientFill = Shader.PropertyToID("_AmbientFill");
        static readonly int IdAmbientShape = Shader.PropertyToID("_AmbientShape");
        static readonly int IdSunDirWS = Shader.PropertyToID("_SunDirWS");
        static readonly int IdNitsPerUnit = Shader.PropertyToID("_NitsPerUnit");
        static readonly int IdSunPx = Shader.PropertyToID("_SunPx");
        static readonly int IdSunColor = Shader.PropertyToID("_SunColor");
        static readonly int IdSunCore = Shader.PropertyToID("_SunCore");
        static readonly int IdStarPosMag = Shader.PropertyToID("_StarPosMag");
        static readonly int IdStarColor = Shader.PropertyToID("_StarColor");
        static readonly int IdStarParams = Shader.PropertyToID("_StarParams");
        static readonly int IdStarShape = Shader.PropertyToID("_StarShape");
        static readonly int IdStarHalo = Shader.PropertyToID("_StarHalo");

        public MonolithSkyRenderer()
        {
            SupportDynamicSunLight = false;     // the Sun is drawn by this renderer; moving the light must not re-bake the lighting cubemap
        }

        public override void Build()
        {
            var shader = Resources.Load<Shader>(ShaderPath) ?? throw new System.InvalidOperationException($"Resources/{ShaderPath}.shader is missing");
            _material = CoreUtils.CreateEngineMaterial(shader);
            _milkyWay = Resources.Load<Texture2D>(MilkyWayPath) ?? throw new System.InvalidOperationException($"Resources/{MilkyWayPath}.exr is missing (run tools/blender.ps1 tools/sky/build_milkyway.py)");
            _stars = StarCatalog.Acquire();
            Monolith.Core.MnLog.Info($"SKY renderer built: Milky Way {_milkyWay.width}x{_milkyWay.height} {_milkyWay.graphicsFormat}, {_stars.Count} stars");
            _material.SetTexture(IdMilkyWay, _milkyWay);
            _material.SetBuffer(IdStarPosMag, _stars.PosMag);
            _material.SetBuffer(IdStarColor, _stars.Color);
        }

        public override void Cleanup()
        {
            CoreUtils.Destroy(_material);
            _material = null;
            _stars?.Release();
            _stars = null;
        }

        // Colours go in as plain numbers: SetColor may convert from gamma to linear, and these are already linear.
        static Vector4 AsVector(Color c) => new Vector4(c.r, c.g, c.b, 1f);

        public override void RenderSky(BuiltinSkyParameters bp, bool renderForCubemap, bool renderSunDisk)
        {
            var sky = bp.skySettings as MonolithSky;
            if (sky == null) return;
            var cmd = bp.commandBuffer;
            var skyToWorld = SkyMath.SkyToWorld(sky.skyRotationEuler.value);

            _block.Clear();
            _block.SetMatrix(IdPixelCoordToViewDir, bp.pixelCoordToViewDirMatrix);
            _block.SetMatrix(IdSkyToWorld, skyToWorld);
            _block.SetMatrix(IdWorldToSky, skyToWorld.inverse);
            _block.SetFloat(IdNitsPerUnit, sky.nitsPerUnit.value);
            var mw = sky.milkyWay.value;
            var tint = AsVector(sky.milkyWayTint.value);
            tint.w = mw.z;
            _block.SetVector(IdMilkyWayTint, tint);
            var ambient = sky.ambientColor.value;
            _block.SetVector(IdAmbientFill, AsVector(ambient) * sky.ambientNits.value);
            _block.SetVector(IdAmbientShape, sky.ambientShape.value);
            Vector3 sunDirWorld = sky.sunDirection.value.normalized;
            _block.SetVector(IdSunDirWS, new Vector4(sunDirWorld.x, sunDirWorld.y, sunDirWorld.z, 0f));

            if (renderForCubemap)
            {
                // Lighting only. The face size sets how blurry a map texel may be (the cubemap is small).
                int face = bp.colorBuffer != null && bp.colorBuffer.rt != null ? bp.colorBuffer.rt.width : 256;
                _block.SetVector(IdMilkyWayParams, new Vector4(mw.x, mw.y, 0.5f * Mathf.PI / Mathf.Max(face, 16), MilkyWayTexelRadians));
                CoreUtils.DrawFullScreen(cmd, _material, _block, PassBake);
                return;
            }

            var cam = bp.hdCamera.camera;
            float fovRad = cam.fieldOfView * Mathf.Deg2Rad;
            _block.SetVector(IdMilkyWayParams, new Vector4(mw.x, mw.y, fovRad / Mathf.Max(bp.screenSize.y, 1f), MilkyWayTexelRadians));
            _block.SetVector(IdBlackLift, AsVector(sky.blackLift.value));

            // The camera's own view-projection, so stars land exactly where geometry does (TAA jitter included).
            var viewProj = bp.hdCamera.mainViewConstants.viewProjMatrix;
            _block.SetMatrix(IdSkyViewProj, viewProj);

            // The Sun: where it lands on screen, if it is in front of the camera at all.
            Vector3 sunDir = sunDirWorld;
            Vector4 sunClip = viewProj * new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f);
            float sunValid = sunClip.w > 1e-3f ? 1f : 0f;
            Vector2 sunPx = Vector2.zero;
            if (sunValid > 0f)
            {
                float nx = sunClip.x / sunClip.w, ny = sunClip.y / sunClip.w;
                sunPx = new Vector2((nx * 0.5f + 0.5f) * bp.screenSize.x, (0.5f - ny * 0.5f) * bp.screenSize.y);   // SV_Position: origin top left
            }
            _block.SetVector(IdSunPx, new Vector4(sunPx.x, sunPx.y, sunValid, 0f));
            _block.SetVector(IdSunColor, AsVector(sky.sunColor.value));
            _block.SetVector(IdSunCore, sky.sunCore.value);

            var starParams = sky.starParams.value;
            _block.SetVector(IdStarParams, starParams);
            _block.SetVector(IdStarShape, sky.starShape.value);
            _block.SetVector(IdStarHalo, sky.starHalo.value);

            CoreUtils.DrawFullScreen(cmd, _material, _block, PassRender);

            int count = _stars.CountBrighterThan(starParams.w);
            if (count > 0) cmd.DrawProcedural(Matrix4x4.identity, _material, PassStars, MeshTopology.Triangles, count * 6, 1, _block);
        }
    }
}
