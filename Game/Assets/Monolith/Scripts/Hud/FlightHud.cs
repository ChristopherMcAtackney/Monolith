using System.Globalization;
using System.IO;
using Monolith.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.Hud
{
    /// <summary>
    /// The minimal flight HUD: centre reticle, speed with the throttle setting, afterburner fuel, and the target's
    /// bracket, range and speed. Reads only <see cref="GameWorld"/> (the local player's <see cref="ShipState"/>). Drawn
    /// after post-processing, on top of everything, at the camera's pixel size, so captures show it as the player does.
    /// Placeholder look; the layout is FS2's (speed and throttle left of the reticle, fuel right).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class FlightHud : MonoBehaviour
    {
        public const string ShaderPath = "Shaders/MN_Lines";

        /// <summary>The HUD colour (linear, drawn after tone mapping): a warm, glyph-like amber.</summary>
        public static readonly Color Ink = new Color(1.0f, 0.62f, 0.20f, 0.92f);

        GameWorld _world;
        Camera _cam;
        HudLines _lines;

        /// <summary>Lines shown centred on screen (pause menu, help); null or empty for none (owner: whoever runs the session).</summary>
        public string[] Overlay;
        /// <summary>A short line at the bottom (device warnings, debug notes).</summary>
        public string Footer;
        /// <summary>Draw the target bracket and its range (off when the combat HUD draws the target itself).</summary>
        public bool ShowTarget = true;

        public static FlightHud Create(GameWorld world, Camera camera, Transform parent)
        {
            var go = new GameObject("FlightHud");
            go.transform.SetParent(parent, false);
            var hud = go.AddComponent<FlightHud>();
            hud._world = world;
            hud._cam = camera;
            hud._lines = new HudLines();

            var shader = Resources.Load<Shader>(ShaderPath) ?? throw new FileNotFoundException($"Resources/{ShaderPath}.shader is missing");
            var volume = go.AddComponent<CustomPassVolume>();
            volume.isGlobal = true;
            volume.injectionPoint = CustomPassInjectionPoint.AfterPostProcess;
            volume.priority = 0f;      // after the view's stars and dust
            volume.customPasses.Add(new HudPass
            {
                name = "MN HUD",
                Material = new Material(shader) { name = "MN_HudLines" },
                Lines = hud._lines,
                Camera = camera,
            });
            return hud;
        }

        void LateUpdate()
        {
            _lines.Clear();
            if (_cam != null) Draw(_cam.pixelWidth, _cam.pixelHeight);
            _lines.Commit();
        }

        void Draw(float w, float h)
        {
            float s = h / 1080f;
            float lw = Mathf.Max(1.5f, 2f * s);
            var c = new Vector2(w * 0.5f, h * 0.5f);
            var ink = Ink;
            var dim = new Color(ink.r, ink.g, ink.b, ink.a * 0.35f);
            var L = _lines;

            // Reticle: four arcs on the diagonals and a centre dot.
            float rr = 17f * s;
            for (int i = 0; i < 4; i++)
            {
                float mid = Mathf.PI * 0.25f + i * Mathf.PI * 0.5f;
                L.Arc(c, rr, mid - 0.45f, mid + 0.45f, lw, ink, 6);
            }
            L.Fill(c - Vector2.one * (1.5f * s), c + Vector2.one * (1.5f * s), ink);

            var ship = _world.Ships.Get(_world.LocalPlayerShip);
            if (ship != null)
            {
                float top = Mathf.Max(1f, ship.AfterburnerSpeed);
                float barH = 220f * s, barW = 10f * s, gap = 270f * s;
                float y0 = c.y - barH * 0.5f, y1 = c.y + barH * 0.5f;

                // Speed (left): bar fills to the speed; a tick marks normal top speed; a chevron marks the throttle setting.
                float lx = c.x - gap;
                L.Rect(new Vector2(lx - barW, y0), new Vector2(lx, y1), lw * 0.75f, dim);
                float sp = Mathf.Clamp01(ship.ForwardSpeed / top);
                L.Fill(new Vector2(lx - barW + 2f * s, y1 - (barH - 2f * s) * sp), new Vector2(lx - 2f * s, y1 - 2f * s), ink);
                float yTop = y1 - barH * Mathf.Clamp01(ship.TopSpeed / top);
                L.Line(new Vector2(lx - barW - 6f * s, yTop), new Vector2(lx + 4f * s, yTop), lw * 0.75f, dim);
                float yThr = y1 - barH * Mathf.Clamp01(ship.Throttle * ship.TopSpeed / top);
                var chev = new Vector2(lx + 6f * s, yThr);
                L.Line(chev + new Vector2(10f * s, -7f * s), chev, lw, ink);
                L.Line(chev, chev + new Vector2(10f * s, 7f * s), lw, ink);
                L.Text(Mathf.RoundToInt(Mathf.Max(0f, ship.ForwardSpeed)).ToString(CultureInfo.InvariantCulture),
                       new Vector2(lx - barW - 12f * s, c.y - 12f * s), 24f * s, lw, ink, TextAlign.Right);
                L.Text("M/S", new Vector2(lx - barW - 12f * s, c.y + 18f * s), 12f * s, lw * 0.75f, dim, TextAlign.Right);
                L.Text("SPD", new Vector2(lx - barW * 0.5f, y1 + 12f * s), 12f * s, lw * 0.75f, dim, TextAlign.Center);
                L.Text("THR " + Mathf.RoundToInt(ship.Throttle * 100f).ToString(CultureInfo.InvariantCulture),
                       new Vector2(lx - barW * 0.5f, y1 + 32f * s), 12f * s, lw * 0.75f, dim, TextAlign.Center);
                if (ship.MatchingSpeed) L.Text("MATCH", new Vector2(lx - barW * 0.5f, y0 - 28f * s), 14f * s, lw, ink, TextAlign.Center);

                // Afterburner fuel (right).
                float rx = c.x + gap;
                L.Rect(new Vector2(rx, y0), new Vector2(rx + barW, y1), lw * 0.75f, dim);
                float fuel = ship.FuelMax > 0f ? Mathf.Clamp01(ship.Fuel / ship.FuelMax) : 0f;
                L.Fill(new Vector2(rx + 2f * s, y1 - (barH - 2f * s) * fuel), new Vector2(rx + barW - 2f * s, y1 - 2f * s),
                       ship.AfterburnerOn ? ink : new Color(ink.r, ink.g, ink.b, ink.a * 0.7f));
                L.Text("AB", new Vector2(rx + barW * 0.5f, y1 + 12f * s), 12f * s, lw * 0.75f, dim, TextAlign.Center);
                if (ship.AfterburnerOn) L.Text("BURN", new Vector2(rx + barW * 0.5f, y0 - 28f * s), 14f * s, lw, ink, TextAlign.Center);

                if (ShowTarget) DrawTarget(ship, c, s, lw, ink, dim);
                L.Text(ship.ClassName, new Vector2(24f * s, 24f * s), 12f * s, lw * 0.75f, dim);
            }

            if (Overlay != null && Overlay.Length > 0)
            {
                float y = c.y - Overlay.Length * 16f * s;
                for (int i = 0; i < Overlay.Length; i++)
                {
                    bool title = i == 0;
                    L.Text(Overlay[i] ?? "", new Vector2(c.x, y), (title ? 28f : 14f) * s, title ? lw * 1.2f : lw * 0.8f, ink, TextAlign.Center);
                    y += (title ? 56f : 28f) * s;
                }
            }
            if (!string.IsNullOrEmpty(Footer)) L.Text(Footer, new Vector2(c.x, h - 48f * s), 12f * s, lw * 0.75f, dim, TextAlign.Center);
        }

        void DrawTarget(ShipState ship, Vector2 c, float s, float lw, Color ink, Color dim)
        {
            var t = _world.Ships.Get(ship.Target);
            if (t == null) return;
            float a = _world.Alpha;
            Vector3 tp = t.RenderPosition(a);
            float dist = Vector3.Distance(tp, _cam.transform.position);
            Vector3 sp = _cam.WorldToScreenPoint(tp);
            float w = _cam.pixelWidth, h = _cam.pixelHeight;
            string range = dist >= 1000f ? (dist / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " KM" : Mathf.RoundToInt(dist) + " M";
            string speed = Mathf.RoundToInt(Mathf.Max(0f, t.ForwardSpeed)) + " M/S";
            var p = new Vector2(sp.x, h - sp.y);
            bool onScreen = sp.z > 0f && p.x > 0f && p.x < w && p.y > 0f && p.y < h;
            if (onScreen)
            {
                // Corner brackets sized by the target's apparent size, never smaller than 18 px.
                float px = t.Radius / Mathf.Max(1f, sp.z) * (h * 0.5f / Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
                float r = Mathf.Max(18f * s, px * 1.2f), k = r * 0.45f;
                for (int i = 0; i < 4; i++)
                {
                    float sx = (i & 1) == 0 ? -1f : 1f, sy = (i & 2) == 0 ? -1f : 1f;
                    var corner = p + new Vector2(sx * r, sy * r);
                    _lines.Line(corner, corner - new Vector2(sx * k, 0f), lw, ink);
                    _lines.Line(corner, corner - new Vector2(0f, sy * k), lw, ink);
                }
                _lines.Text(range, new Vector2(p.x + r + 8f * s, p.y - r), 12f * s, lw * 0.75f, ink);
                _lines.Text(speed, new Vector2(p.x + r + 8f * s, p.y - r + 20f * s), 12f * s, lw * 0.75f, dim);
            }
            else
            {
                // Off screen: an arrow on a ring round the reticle, pointing the way to turn.
                Vector3 local = _cam.transform.InverseTransformPoint(tp);
                var dir = new Vector2(local.x, -local.y);
                if (dir.sqrMagnitude < 1e-6f) dir = Vector2.down;
                dir.Normalize();
                float ring = 150f * s;
                var tip = c + dir * (ring + 14f * s);
                var n = new Vector2(-dir.y, dir.x);
                _lines.Line(tip, c + dir * ring + n * 9f * s, lw, ink);
                _lines.Line(tip, c + dir * ring - n * 9f * s, lw, ink);
                _lines.Text(range, c + dir * (ring + 30f * s) + new Vector2(0f, -6f * s), 12f * s, lw * 0.75f, ink, TextAlign.Center);
            }
        }
    }

    /// <summary>Draws the HUD mesh in pixel space on top of the finished, tone-mapped frame.</summary>
    sealed class HudPass : CustomPass
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
            ctx.cmd.DrawMesh(Lines.Mesh, Matrix4x4.identity, Material, 0, 2, _props);
        }
    }
}
