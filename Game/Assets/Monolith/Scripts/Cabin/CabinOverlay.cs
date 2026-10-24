using System.IO;
using Monolith.Core;
using Monolith.Hud;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.Cabin
{
    /// <summary>
    /// What the cabin draws on the screen itself, in the HUD's own line style: the minimal prompt for what the pilot is looking at and what the context button does there
    /// (a key cap and a few words, with a bar while a hold is under way), and, in the seat, the battle map the stand button's double tap switches to. Drawn after
    /// post-processing like the HUD, so it shows in captures.
    /// </summary>
    [DefaultExecutionOrder(120)]
    public sealed class CabinOverlay : MonoBehaviour
    {
        GameWorld _world;
        CabinSystem _cab;
        Camera _cam;
        HudLines _lines;
        float _promptFade;

        public static CabinOverlay Create(GameWorld world, CabinSystem cab, Camera cam, Transform parent)
        {
            var go = new GameObject("CabinOverlay");
            go.transform.SetParent(parent, false);
            var o = go.AddComponent<CabinOverlay>();
            o._world = world; o._cab = cab; o._cam = cam;
            o._lines = new HudLines();
            var shader = Resources.Load<Shader>(FlightHud.ShaderPath) ?? throw new FileNotFoundException($"Resources/{FlightHud.ShaderPath}.shader is missing");
            var volume = go.AddComponent<CustomPassVolume>();
            volume.isGlobal = true;
            volume.injectionPoint = CustomPassInjectionPoint.AfterPostProcess;
            volume.priority = -2f;
            volume.customPasses.Add(new OverlayPass { name = "MN Cabin overlay", Material = new Material(shader) { name = "MN_CabinOverlayLines" }, Lines = o._lines, Camera = cam });
            return o;
        }

        void LateUpdate()
        {
            _lines.Clear();
            if (_cam != null && _cab != null && !_world.Death.Active) Draw(_cam.pixelWidth, _cam.pixelHeight);      // nothing over the death sequence
            _lines.Commit();
        }

        void Draw(float w, float h)
        {
            var b = _cab.Board;
            float s = h / 1080f;
            float lw = Mathf.Max(1.5f, 2f * s);
            var ink = FlightHud.Ink;
            var dim = new Color(ink.r, ink.g, ink.b, ink.a * 0.45f);
            var L = _lines;
            if (b.MapOpen && b.State == CabinBoard.Phase.Seated)
            {
                BlipMap.Draw(L, _world, new Vector2(w * 0.5f, h * 0.5f), h * 0.40f, lw, ink, Time.unscaledTime);
                L.Text("BATTLE MAP", new Vector2(48f * s, 48f * s), 22f * s, lw * 1.1f, ink);
                L.Text("DOUBLE TAP " + CabinSystem.Keys.Stand + " TO CLOSE", new Vector2(48f * s, 84f * s), 12f * s, lw * 0.75f, dim);
            }
            bool want = !string.IsNullOrEmpty(b.Prompt) && !_cab.Frozen;
            _promptFade = Mathf.MoveTowards(_promptFade, want ? 1f : 0f, Time.unscaledDeltaTime * 6f);
            if (_promptFade > 0.01f && !string.IsNullOrEmpty(b.Prompt)) DrawPrompt(L, w, h, s, lw, ink, b);
        }

        void DrawPrompt(HudLines L, float w, float h, float s, float lw, Color ink, CabinBoard b)
        {
            float k = _promptFade;
            var col = new Color(ink.r, ink.g, ink.b, ink.a * k);
            var dim = new Color(ink.r, ink.g, ink.b, ink.a * 0.5f * k);
            float th = 17f * s;
            string text = b.Prompt.ToUpperInvariant();
            string key = b.PromptKey;
            float keyW = string.IsNullOrEmpty(key) ? 0f : Mathf.Max(30f * s, HudText.Width(key, 14f * s) + 18f * s);
            float textW = HudText.Width(text, th);
            float gap = keyW > 0f ? 14f * s : 0f;
            float total = keyW + gap + textW;
            float x = w * 0.5f - total * 0.5f, y = h * 0.80f;
            if (keyW > 0f)
            {
                var a = new Vector2(x, y - 6f * s); var c = new Vector2(x + keyW, y + th + 6f * s);
                L.Rect(a, c, lw, col);
                L.Text(key, new Vector2(x + keyW * 0.5f, y + (th - 14f * s) * 0.5f), 14f * s, lw * 0.9f, col, TextAlign.Center);
            }
            L.Text(text, new Vector2(x + keyW + gap, y), th, lw * 1.0f, col);
            if (b.PromptProgress >= 0f)
            {
                float by = y + th + 18f * s;
                var a = new Vector2(w * 0.5f - total * 0.5f, by); var c = new Vector2(w * 0.5f + total * 0.5f, by + 7f * s);
                L.Rect(a, c, lw * 0.8f, dim);
                if (b.PromptProgress > 0.005f) L.Fill(a + Vector2.one * 2f * s, new Vector2(a.x + (c.x - a.x) * Mathf.Clamp01(b.PromptProgress) - 2f * s, c.y - 2f * s), col);
            }
        }

        sealed class OverlayPass : CustomPass
        {
            public Material Material;
            public HudLines Lines;
            public Camera Camera;
            readonly MaterialPropertyBlock _props = new MaterialPropertyBlock();
            static readonly int MatrixId = Shader.PropertyToID("_MN_Matrix");
            static readonly int ScreenId = Shader.PropertyToID("_MN_Screen");

            protected override bool executeInSceneView => false;

            protected override void Execute(CustomPassContext ctx)
            {
                if (ctx.hdCamera.camera != Camera || Material == null) return;
                float w = ctx.hdCamera.actualWidth, h = ctx.hdCamera.actualHeight;
                var m = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(0f, w, h, 0f, -1f, 1f), true);
                CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ClearFlag.None);
                _props.SetMatrix(MatrixId, m);
                _props.SetVector(ScreenId, new Vector4(w, h, 1f / w, 1f / h));
                _props.SetFloat(HudCollapse.DimId, 0f);
                ctx.cmd.DrawMesh(Lines.Mesh, Matrix4x4.identity, Material, 0, 2, _props);
            }
        }
    }

    /// <summary>Text measure for the stroke font (the HUD's: 4 x 8 glyphs, 6 units apart).</summary>
    public static class HudText
    {
        public static float Width(string s, float height) => StrokeFont.Measure(s) * (height / StrokeFont.Height);
    }
}
