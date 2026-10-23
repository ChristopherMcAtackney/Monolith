using Monolith.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.Look
{
    /// <summary>
    /// The camera look, switched on for one camera: <c>CameraLook.Enable(camera)</c>. Two HDRP custom passes that run only for that camera:
    /// the lens (before post-processing: the Sun's glare, streaks, ghosts and the dust they light) and the film (after post-processing:
    /// a graded black floor, vignette, grain). Change <see cref="Options"/> and call <see cref="Apply"/>; <see cref="LookOptions.Enabled"/>
    /// and <see cref="LookOptions.Intensity"/> turn it off or down.
    /// It works on the 3D picture only. A HUD or menu drawn by a UI canvas in screen-space overlay is composited after the camera and never
    /// sees the grain; anything that must not get it has to be drawn that way (or by a second camera this look is not enabled for).
    /// </summary>
    public sealed class CameraLook : MonoBehaviour
    {
        public LookOptions Options { get; private set; }
        public Camera Camera { get; private set; }

        CustomPassVolume _lensVolume, _gradeVolume, _meterVolume;
        LensPass _lens;
        GradePass _grade;
        MeterPass _meter;

        public static CameraLook Enable(Camera camera, Transform parent = null, LookOptions options = null)
        {
            if (camera == null) throw new System.ArgumentNullException(nameof(camera));
            var go = new GameObject("MonolithCameraLook");
            if (parent != null) go.transform.SetParent(parent, false);
            var look = go.AddComponent<CameraLook>();
            look.Build(camera, options ?? new LookOptions());
            return look;
        }

        void Build(Camera camera, LookOptions options)
        {
            Camera = camera;
            Options = options;

            _lens = new LensPass { name = "Monolith Lens", owner = this };
            _lensVolume = MakeVolume("Lens", CustomPassInjectionPoint.BeforePostProcess, _lens);
            _grade = new GradePass { name = "Monolith Film", owner = this };
            _gradeVolume = MakeVolume("Film", CustomPassInjectionPoint.AfterPostProcess, _grade);
            _meter = new MeterPass { name = "Monolith Meter", owner = this };
            _meterVolume = MakeVolume("Meter", CustomPassInjectionPoint.BeforePostProcess, _meter, 10f);     // after the lens pass, so the Sun glare counts
            Apply();
            MnLog.Info($"LOOK on: intensity {options.Intensity}, glare {(options.Glare ? "on" : "off")}, grain {options.GrainAmount}");
        }

        CustomPassVolume MakeVolume(string name, CustomPassInjectionPoint point, CustomPass pass, float priority = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var v = go.AddComponent<CustomPassVolume>();
            v.isGlobal = true;
            v.injectionPoint = point;
            v.targetCamera = Camera;
            v.priority = priority;
            v.customPasses.Add(pass);
            return v;
        }

        /// <summary>Tell the look where the Sun is (the sky rig does this); a look without a Sun has no glare, only the film.</summary>
        public void SetSun(Vector3 worldDirectionToSun, Color color, float nitsPerUnit)
        {
            Options.SunDirection = worldDirectionToSun.normalized;
            Options.SunColor = color;
            Options.NitsPerUnit = nitsPerUnit;
            Options.HasSun = true;
        }

        /// <summary>Switch the passes on or off after changing <see cref="Options"/>.</summary>
        public void Apply()
        {
            bool on = Options.Enabled && Options.Intensity > 0f;
            if (_lensVolume != null) _lensVolume.enabled = on && Options.Glare && Options.HasSun;
            if (_gradeVolume != null) _gradeVolume.enabled = on;
            if (_meterVolume != null) _meterVolume.enabled = on && Options.SceneMeter;
        }

        void LateUpdate() => Apply();     // cheap, and keeps Options edits live

        /// <summary>Remove the look from the camera.</summary>
        public void Disable() => Destroy(gameObject);

        void OnDestroy() => Shader.SetGlobalTexture(MeterPass.SceneKeyId, Texture2D.blackTexture);     // no meter: the sky shows every star it has
    }

    /// <summary>The lens: glare added to the HDR picture before post-processing.</summary>
    sealed class LensPass : CustomPass
    {
        public CameraLook owner;
        Material _material;
        static readonly int IdSunPx = Shader.PropertyToID("_SunPx"), IdSunTint = Shader.PropertyToID("_SunTint"),
            IdGlareScale = Shader.PropertyToID("_GlareScale"), IdVeil = Shader.PropertyToID("_Veil"), IdWing = Shader.PropertyToID("_Wing"),
            IdVeilTint = Shader.PropertyToID("_VeilTint"), IdStreakThin = Shader.PropertyToID("_StreakThin"),
            IdStreakBroad = Shader.PropertyToID("_StreakBroad"), IdStreakTint = Shader.PropertyToID("_StreakTint"),
            IdCrystal = Shader.PropertyToID("_Crystal"), IdDust = Shader.PropertyToID("_Dust"), IdSeed = Shader.PropertyToID("_Seed"), IdDustTex = Shader.PropertyToID("_LensDust");

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            var shader = Resources.Load<Shader>("MonolithLook/MonolithLens") ?? throw new System.InvalidOperationException("Resources/MonolithLook/MonolithLens.shader is missing");
            _material = CoreUtils.CreateEngineMaterial(shader);
        }

        protected override void Execute(CustomPassContext ctx)
        {
            if (owner == null || ctx.hdCamera.camera != owner.Camera) return;
            var o = owner.Options;
            if (!o.Enabled || !o.Glare || !o.HasSun || o.Intensity <= 0f) return;

            // Where the Sun lands on screen (the camera's own view-projection, jitter included), in SV_Position pixels.
            var vp = ctx.hdCamera.mainViewConstants.viewProjMatrix;
            Vector4 clip = vp * new Vector4(o.SunDirection.x, o.SunDirection.y, o.SunDirection.z, 0f);
            if (clip.w < 1e-3f) return;     // the Sun is behind the camera
            var size = ctx.hdCamera.screenSize;
            var px = new Vector4((clip.x / clip.w * 0.5f + 0.5f) * size.x, (0.5f - clip.y / clip.w * 0.5f) * size.y, 1f, 0f);

            var mpb = ctx.propertyBlock;
            mpb.SetVector(IdSunPx, px);
            mpb.SetVector(IdSunTint, Vec(o.SunColor));
            mpb.SetVector(IdGlareScale, new Vector4(o.Intensity * o.NitsPerUnit, o.HiddenSunGlare, o.Fringe, o.Ghosts));
            mpb.SetVector(IdVeil, new Vector4(o.VeilEnergy, o.VeilSize, o.WingEnergy, o.WingSize));
            mpb.SetVector(IdWing, new Vector4(o.HaloEnergy, o.HaloSize, o.HazeEnergy, o.HazeSize));
            mpb.SetVector(IdVeilTint, Vec(o.VeilTint));
            mpb.SetVector(IdStreakThin, new Vector4(o.StreakPeak, o.StreakThickness, o.StreakReach, o.StreakUneven));
            mpb.SetVector(IdStreakBroad, new Vector4(o.BroadPeak, o.BroadThickness, o.BroadReach, o.BroadSpacing));
            mpb.SetVector(IdStreakTint, Vec(o.BroadTint));
            mpb.SetVector(IdDust, new Vector4(o.Dust, o.DustBokeh, o.DustScale, 0f));
            mpb.SetVector(IdSeed, new Vector4(o.Seed * 3.17f, 0f, 0f, 0f));
            mpb.SetVector(IdCrystal, new Vector4(o.Crystal, o.CrystalSize, o.CrystalSpikeLength, 0f));
            mpb.SetTexture(IdDustTex, DustTexture.Get());
            CoreUtils.DrawFullScreen(ctx.cmd, _material, mpb, 0);
        }

        protected override void Cleanup() => CoreUtils.Destroy(_material);

        static Vector4 Vec(Color c) => new Vector4(c.r, c.g, c.b, 1f);
    }

    /// <summary>The film: floor, vignette and grain on the finished picture.</summary>
    sealed class GradePass : CustomPass
    {
        public CameraLook owner;
        Material _material;
        RTHandle _copy;
        Texture2D _mottle;
        static readonly int IdMottle = Shader.PropertyToID("_Mottle");
        static readonly int IdGrade = Shader.PropertyToID("_Grade"), IdFloor = Shader.PropertyToID("_Floor"),
            IdGradeMisc = Shader.PropertyToID("_GradeMisc"), IdSource = Shader.PropertyToID("_LookSource");

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            var shader = Resources.Load<Shader>("MonolithLook/MonolithLens") ?? throw new System.InvalidOperationException("Resources/MonolithLook/MonolithLens.shader is missing");
            _material = CoreUtils.CreateEngineMaterial(shader);
            _mottle = MakeMottle(owner != null ? owner.Options : null);
            // A full-float copy of the picture to read from while writing back over it (the custom colour buffer is only 8 bits).
            _copy = RTHandles.Alloc(Vector2.one, TextureXR.slices, dimension: TextureXR.dimension,
                colorFormat: UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat, useDynamicScale: true, name: "MN_LookCopy");
        }

        /// <summary>The slow pattern in the black, once: 160 x 90, four channels (brightness drift with a finer layer on top, and three hue drifts), smooth noise, fixed.</summary>
        static Texture2D MakeMottle(LookOptions o)
        {
            const int w = 160, h = 90;
            float size = Mathf.Max(o != null ? o.MottleSizePx : 160f, 20f) / 1920f * w;        // cells across at 1920 px wide
            float seed = (o != null ? o.Seed : 7) * 13.7f;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float N(float scale, float off) => Mathf.PerlinNoise(x / (size * scale) + seed + off, y / (size * scale) + seed * 0.6f + off * 0.7f);
                    float common = Mathf.Clamp01(0.5f + (N(1f, 0f) - 0.5f) * 1.05f + (N(0.37f, 19f) - 0.5f) * 0.6f);
                    px[y * w + x] = new Color32((byte)(common * 255f), (byte)(N(1.3f, 41f) * 255f), (byte)(N(0.9f, 77f) * 255f), (byte)(N(1.1f, 12f) * 255f));
                }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true) { name = "MN_Mottle", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        protected override void Execute(CustomPassContext ctx)
        {
            if (owner == null || ctx.hdCamera.camera != owner.Camera) return;
            var o = owner.Options;
            if (!o.Enabled || o.Intensity <= 0f) return;

            CustomPassUtils.Copy(ctx, ctx.cameraColorBuffer, _copy);
            SetRenderTargetAuto(ctx.cmd);

            var mpb = ctx.propertyBlock;
            mpb.SetTexture(IdSource, _copy);
            mpb.SetTexture(IdMottle, _mottle);
            mpb.SetVector(IdGrade, new Vector4(o.GrainAmount, o.GrainSizePx, o.GrainChroma, Time.frameCount % 4096));
            mpb.SetVector(IdFloor, new Vector4(o.FloorColor.r, o.FloorColor.g, o.FloorColor.b, o.Vignette));
            mpb.SetVector(IdGradeMisc, new Vector4(o.Intensity, o.Mottle, o.MottleSizePx, 0f));
            CoreUtils.DrawFullScreen(ctx.cmd, _material, mpb, 1);
        }

        protected override void Cleanup()
        {
            CoreUtils.Destroy(_material);
            _copy?.Release();
            _copy = null;
            if (_mottle != null) Object.Destroy(_mottle);
        }
    }

    /// <summary>
    /// The meter: how much light is in the frame, as one smoothed number in a 1 x 1 texture (<c>_MnSceneKey</c>, the mean of the square root
    /// of luminance). The sky reads it on the next frame. It jumps straight to the new reading after a cut (the camera moved far or turned
    /// a long way in one frame), so a quick change of view does not inherit the previous view's iris.
    /// </summary>
    sealed class MeterPass : CustomPass
    {
        public static readonly int SceneKeyId = Shader.PropertyToID("_MnSceneKey");
        public CameraLook owner;
        Material _material;
        RenderTexture _grid;
        readonly RenderTexture[] _key = new RenderTexture[2];
        int _current;
        bool _hasReading;
        Vector3 _lastPos;
        Quaternion _lastRot;
        static readonly int IdSrc = Shader.PropertyToID("_MeterSrc"), IdSmall = Shader.PropertyToID("_MeterSmall"), IdPrev = Shader.PropertyToID("_MeterPrev"),
            IdOut = Shader.PropertyToID("_MeterOut"), IdTime = Shader.PropertyToID("_MeterTime");

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            var shader = Resources.Load<Shader>("MonolithLook/MonolithLens") ?? throw new System.InvalidOperationException("Resources/MonolithLook/MonolithLens.shader is missing");
            _material = CoreUtils.CreateEngineMaterial(shader);
            _grid = new RenderTexture(32, 18, 0, RenderTextureFormat.RFloat) { name = "MN_MeterGrid", filterMode = FilterMode.Point };
            for (int i = 0; i < 2; i++) _key[i] = new RenderTexture(1, 1, 0, RenderTextureFormat.RFloat) { name = "MN_MeterKey" + i, filterMode = FilterMode.Point };
            _grid.Create(); _key[0].Create(); _key[1].Create();
        }

        protected override void Execute(CustomPassContext ctx)
        {
            if (owner == null || ctx.hdCamera.camera != owner.Camera) return;
            var o = owner.Options;
            if (!o.Enabled || !o.SceneMeter) return;

            var t = ctx.hdCamera.camera.transform;
            bool cut = !_hasReading || (t.position - _lastPos).sqrMagnitude > 50f * 50f || Quaternion.Angle(t.rotation, _lastRot) > 20f;
            _lastPos = t.position; _lastRot = t.rotation;

            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var mpb = ctx.propertyBlock;
            mpb.SetTexture(IdSrc, ctx.cameraColorBuffer.rt);
            mpb.SetVector(IdOut, new Vector4(32f, 18f, 0f, 0f));
            CoreUtils.SetRenderTarget(ctx.cmd, _grid);
            CoreUtils.DrawFullScreen(ctx.cmd, _material, mpb, 2);

            int next = 1 - _current;
            mpb.SetTexture(IdSmall, _grid);
            mpb.SetTexture(IdPrev, _key[_current]);
            mpb.SetVector(IdTime, new Vector4(1f - Mathf.Exp(-dt / Mathf.Max(o.MeterAttackSeconds, 1e-3f)), 1f - Mathf.Exp(-dt / Mathf.Max(o.MeterReleaseSeconds, 1e-3f)), cut ? 1f : 0f, 0f));
            CoreUtils.SetRenderTarget(ctx.cmd, _key[next]);
            CoreUtils.DrawFullScreen(ctx.cmd, _material, mpb, 3);
            _current = next;
            _hasReading = true;
            ctx.cmd.SetGlobalTexture(SceneKeyId, _key[_current]);
            SetRenderTargetAuto(ctx.cmd);
            if (o.LogMeter && Time.frameCount % 10 == 0)
                UnityEngine.Rendering.AsyncGPUReadback.Request(_key[_current], 0, req =>
                {
                    if (req.hasError) return;
                    float key = req.GetData<float>()[0];
                    float L = key * key;
                    MnLog.Info($"METER key {key:0.0000} frame brightness {L:0.00000} catch at L0 0.0035: {1f / (1f + L / 0.0035f):0.00}");
                });
        }

        protected override void Cleanup()
        {
            CoreUtils.Destroy(_material);
            if (_grid != null) _grid.Release();
            foreach (var k in _key) if (k != null) k.Release();
            Shader.SetGlobalTexture(SceneKeyId, Texture2D.blackTexture);
        }
    }
}
