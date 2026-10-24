using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.Hud
{
    /// <summary>
    /// Draws a <see cref="HudLines"/> mesh in pixel space on top of the finished, tone-mapped frame (after post-processing, so captures show it as the player sees it): the HUD, and the
    /// cabin's on-screen map and prompts. The cabin collapses the HUD into its own displays (<see cref="HudCollapse"/>); a pass that is part of the HUD obeys it.
    /// </summary>
    public sealed class HudPass : CustomPass
    {
        public const string ShaderPath = "Shaders/MN_Lines";

        public Material Material;
        public HudLines Lines;
        public Camera Camera;
        /// <summary>Shrink toward the dash and fade with the cabin's collapse (the HUD does; the cabin's own overlay does not).</summary>
        public bool ObeyCollapse = true;
        readonly MaterialPropertyBlock _props = new MaterialPropertyBlock();
        static readonly int MatrixId = Shader.PropertyToID("_MN_Matrix");
        static readonly int ScreenId = Shader.PropertyToID("_MN_Screen");

        protected override bool executeInSceneView => false;

        /// <summary>Make a global custom-pass volume on <paramref name="host"/> that draws <paramref name="lines"/> through <paramref name="camera"/>; <paramref name="priority"/> orders the passes (higher first).</summary>
        public static HudPass Attach(GameObject host, Camera camera, HudLines lines, string name, float priority, bool obeyCollapse = true)
        {
            var shader = Resources.Load<Shader>(ShaderPath) ?? throw new FileNotFoundException($"Resources/{ShaderPath}.shader is missing");
            var volume = host.AddComponent<CustomPassVolume>();
            volume.isGlobal = true;
            volume.injectionPoint = CustomPassInjectionPoint.AfterPostProcess;
            volume.priority = priority;
            var pass = new HudPass { name = name, Material = new Material(shader) { name = "MN_" + name.Replace(' ', '_') }, Lines = lines, Camera = camera, ObeyCollapse = obeyCollapse };
            volume.customPasses.Add(pass);
            return pass;
        }

        protected override void Execute(CustomPassContext ctx)
        {
            if (ctx.hdCamera.camera != Camera || Material == null || Lines == null) return;
            float w = ctx.hdCamera.actualWidth, h = ctx.hdCamera.actualHeight;
            float collapse = ObeyCollapse ? HudCollapse.Amount : 0f;
            if (collapse >= 0.999f) return;
            var m = HudCollapse.Matrix(w, h, collapse);
            CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ClearFlag.None);
            _props.SetMatrix(MatrixId, m);
            _props.SetVector(ScreenId, new Vector4(w, h, 1f / w, 1f / h));
            _props.SetFloat(HudCollapse.DimId, HudCollapse.Dim(collapse));
            Lines.Draw(ctx.cmd, Material, _props);
        }
    }
}
