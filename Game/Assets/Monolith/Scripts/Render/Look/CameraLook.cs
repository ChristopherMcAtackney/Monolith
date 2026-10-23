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

        CustomPassVolume _lensVolume, _gradeVolume;
        LensPass _lens;
        GradePass _grade;

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
            Apply();
            MnLog.Info($"LOOK on: intensity {options.Intensity}, glare {(options.Glare ? "on" : "off")}, grain {options.GrainAmount}");
        }

        CustomPassVolume MakeVolume(string name, CustomPassInjectionPoint point, CustomPass pass)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var v = go.AddComponent<CustomPassVolume>();
            v.isGlobal = true;
            v.injectionPoint = point;
            v.targetCamera = Camera;
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
        }

        void LateUpdate() => Apply();     // cheap, and keeps Options edits live

        /// <summary>Remove the look from the camera.</summary>
        public void Disable() => Destroy(gameObject);
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
            IdDust = Shader.PropertyToID("_Dust"), IdSeed = Shader.PropertyToID("_Seed"), IdDustTex = Shader.PropertyToID("_LensDust");

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
        static readonly int IdGrade = Shader.PropertyToID("_Grade"), IdFloor = Shader.PropertyToID("_Floor"),
            IdGradeMisc = Shader.PropertyToID("_GradeMisc"), IdSource = Shader.PropertyToID("_LookSource");

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            var shader = Resources.Load<Shader>("MonolithLook/MonolithLens") ?? throw new System.InvalidOperationException("Resources/MonolithLook/MonolithLens.shader is missing");
            _material = CoreUtils.CreateEngineMaterial(shader);
            // A full-float copy of the picture to read from while writing back over it (the custom colour buffer is only 8 bits).
            _copy = RTHandles.Alloc(Vector2.one, TextureXR.slices, dimension: TextureXR.dimension,
                colorFormat: UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat, useDynamicScale: true, name: "MN_LookCopy");
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
            mpb.SetVector(IdGrade, new Vector4(o.GrainAmount, o.GrainSizePx, o.GrainChroma, Time.frameCount % 4096));
            mpb.SetVector(IdFloor, new Vector4(o.FloorColor.r, o.FloorColor.g, o.FloorColor.b, o.Vignette));
            mpb.SetVector(IdGradeMisc, new Vector4(o.Intensity, 0f, 0f, 0f));
            CoreUtils.DrawFullScreen(ctx.cmd, _material, mpb, 1);
        }

        protected override void Cleanup()
        {
            CoreUtils.Destroy(_material);
            _copy?.Release();
            _copy = null;
        }
    }
}
