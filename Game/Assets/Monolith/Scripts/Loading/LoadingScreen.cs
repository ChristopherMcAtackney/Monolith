using System.Collections;
using Monolith.Core;
using Monolith.Hud;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.Loading
{
    /// <summary>
    /// The loading screen: the splash picture full screen (never stretched, see <see cref="LoadingSplash"/>), a slow settle, and a loading bar in the HUD's own style (whichever style the player has
    /// chosen: cells, a fine scale, or a plate with glyphs) with a short status line in the HUD's type. It draws with the HUD kit's canvas through a custom pass after post-processing, on a
    /// camera of its own until the game has one and then on the game's camera, so it covers the world while the world is built and warmed up behind it and fades off the finished frame at the end.
    /// Progress is a <see cref="LoadMeter"/> (real steps); the screen only shows it.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class LoadingScreen : MonoBehaviour
    {
        public static LoadingScreen Current { get; private set; }
        /// <summary>A loading screen is up (for anything that must not run behind it).</summary>
        public static bool Active => Current != null;

        /// <summary>Where the progress and the status come from.</summary>
        public LoadMeter Meter;
        /// <summary>A fixed progress and status instead of a meter (the capture scene, the checks): a negative progress uses the meter.</summary>
        public float FixedProgress = -1f;
        public string FixedStatus;
        /// <summary>A fixed moment of the settle (seconds since the screen came up), for a capture; negative uses the clock.</summary>
        public float FixedTime = -1f;
        /// <summary>1 while it covers the screen, falling to 0 as it fades off.</summary>
        public float Fade { get; private set; } = 1f;
        public Texture2D Splash { get; private set; }

        HudLines _lines;
        HudPass _pass;
        Camera _ownCam;
        float _shown;               // the bar as drawn: it eases toward the real progress and never runs ahead of it
        float _t0;
        bool _fading;

        public static LoadingScreen Show(Transform parent = null, string splash = null)
        {
            if (Current != null) return Current;
            var go = new GameObject("LoadingScreen");
            if (parent != null) go.transform.SetParent(parent, false); else DontDestroyOnLoad(go);
            var s = go.AddComponent<LoadingScreen>();
            s.Begin(splash);
            return s;
        }

        void Begin(string splash)
        {
            Current = this;
            _t0 = Time.unscaledTime;
            Splash = LoadingSplash.Load(splash);
            _lines = new HudLines { ImageTexture = Splash };

            // a camera of its own: until the game builds one this is the only thing to draw, and it clears to black (no flash of an empty scene)
            var camGo = new GameObject("LoadingCamera");
            camGo.transform.SetParent(transform, false);
            _ownCam = camGo.AddComponent<Camera>();
            _ownCam.clearFlags = CameraClearFlags.SolidColor;
            _ownCam.backgroundColor = Color.black;
            _ownCam.cullingMask = 0;
            _ownCam.depth = 1000f;
            _ownCam.allowHDR = false;
            _ownCam.allowMSAA = false;
            _ownCam.useOcclusionCulling = false;
            var data = camGo.AddComponent<HDAdditionalCameraData>();
            data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            data.backgroundColorHDR = Color.black;
            data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            _pass = HudPass.Attach(gameObject, _ownCam, _lines, "MN Loading", -50f, false);
            MnLog.Info($"LOADING screen up, style {HudStyles.Current.Name}");
        }

        /// <summary>The game has its camera: draw on that one from now on (the loading camera is turned off).</summary>
        public void AttachTo(Camera cam)
        {
            if (cam == null || _pass == null) return;
            _pass.Camera = cam;
            if (_ownCam != null) _ownCam.enabled = false;
        }

        /// <summary>When all the work is done, let the bar catch up with it (it eases toward the real figure) so the screen is seen full for a beat before it fades; never longer than <paramref name="maxSeconds"/>.</summary>
        public IEnumerator FillUp(float maxSeconds)
        {
            float t = 0f;
            while (_shown < 0.995f && t < maxSeconds) { t += Time.unscaledDeltaTime; yield return null; }
            yield return new WaitForSecondsRealtime(0.15f);
        }

        /// <summary>Fade the whole screen off over <paramref name="seconds"/> and then take it down.</summary>
        public IEnumerator FadeOut(float seconds)
        {
            _fading = true;
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                Fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds));
                yield return null;
            }
            Fade = 0f;
            _lines.Clear();
            _lines.Commit();
            yield return null;
            Close();
        }

        public void Close()
        {
            if (Current == this) Current = null;
            if (this != null && gameObject != null) Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            if (Splash != null) Destroy(Splash);
            _lines?.Dispose();
        }

        void LateUpdate()
        {
            if (_pass == null) return;
            var cam = _pass.Camera != null ? _pass.Camera : _ownCam;
            float w = cam != null ? cam.pixelWidth : Screen.width, h = cam != null ? cam.pixelHeight : Screen.height;
            if (w < 2f || h < 2f) { w = Screen.width; h = Screen.height; }
            float target = FixedProgress >= 0f ? FixedProgress : Meter != null ? Meter.Progress : 0f;
            string status = FixedStatus ?? (Meter != null ? Meter.Status : "");
            float dt = Time.unscaledDeltaTime;
            if (FixedProgress >= 0f) _shown = FixedProgress;
            else
            {
                _shown = Mathf.Min(target, _shown + (target - _shown) * (1f - Mathf.Exp(-9f * dt)) + 0.02f * dt);
                if (target >= 0.999f) _shown = Mathf.MoveTowards(_shown, 1f, dt * 2f);
            }
            float time = FixedTime >= 0f ? FixedTime : Time.unscaledTime - _t0;
            var style = HudStyles.Current;
            _lines.Clear(Mathf.Min(h / 1080f, w / 1920f), style);
            DrawFrame(_lines, w, h, Splash != null, _shown, status, time, Fade);
            _lines.Commit();
        }

        // ------------------------------------------------------------------------------------------------ the picture

        /// <summary>The slow settle: the picture starts a little larger and eases to its place over about ten seconds.</summary>
        public static float Settle(float time) => 1f + 0.045f * (1f - EaseOut(Mathf.Clamp01(time / 10f)));

        static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);

        /// <summary>Draw the whole screen into <paramref name="L"/> (already cleared with the scale and the style): black, the splash, then the bar and the status. A pure function of its arguments.</summary>
        public static void DrawFrame(HudLines L, float w, float h, bool hasSplash, float progress, string status, float time, float fade)
        {
            fade = Mathf.Clamp01(fade);
            L.Backdrop(Vector2.zero, new Vector2(w, h), new Color(0f, 0f, 0f, fade));
            if (hasSplash && L.ImageTexture != null)
            {
                float intro = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / 0.6f));
                LoadingSplash.Fit(w, h, L.ImageTexture.width, L.ImageTexture.height, Settle(time), out var min, out var max, out var uv, out float fu, out float fv);
                L.Image(min, max, uv, fu, fv, new Color(1f, 1f, 1f, fade * intro));
            }
            DrawBar(L, w, h, progress, status, time, fade);
        }

        // ------------------------------------------------------------------------------------------------ the bar, in the HUD's style

        /// <summary>The panel the bar and its words sit in (pixels), for the checks.</summary>
        public static Rect BarArea(float w, float h, float s) => new Rect(w * 0.5f - 330f * s, h - 168f * s, 660f * s, 96f * s);

        static void DrawBar(HudLines L, float w, float h, float progress, string status, float time, float fade)
        {
            if (fade <= 0.003f) return;
            var st = L.Style;
            float s = L.Scale;
            var area = BarArea(w, h, s);
            Color Ink(float a) => new Color(L.Ink.r, L.Ink.g, L.Ink.b, L.Ink.a * a * fade);
            Color Shade(float a) => HudLines.Shade(a * fade);
            progress = Mathf.Clamp01(progress);
            float lw = L.Lw();
            float x0 = area.xMin, x1 = area.xMax, yBar = area.yMax - 22f * s;

            // the words: what is being loaded, and how far (over a faint plate so they read over any picture)
            L.Fill(new Vector2(x0 - 22f * s, area.yMin - 8f * s), new Vector2(x1 + 22f * s, area.yMax + 12f * s), Shade(0.46f));
            L.Rect(new Vector2(x0 - 22f * s, area.yMin - 8f * s), new Vector2(x1 + 22f * s, area.yMax + 12f * s), lw * 0.6f, Ink(0.30f));
            float k = 11f * s;
            foreach (var corner in new[] { new Vector2(x0 - 22f * s, area.yMin - 8f * s), new Vector2(x1 + 22f * s, area.yMin - 8f * s), new Vector2(x0 - 22f * s, area.yMax + 12f * s), new Vector2(x1 + 22f * s, area.yMax + 12f * s) })
            {
                float sx = corner.x < w * 0.5f ? 1f : -1f, sy = corner.y < h * 0.5f ? 1f : -1f;
                L.Line(corner, corner + new Vector2(sx * k, 0f), lw, Ink(0.95f));
                L.Line(corner, corner + new Vector2(0f, sy * k), lw, Ink(0.95f));
            }
            string text = (status ?? "").ToUpperInvariant();
            float capV = L.Cap(TextRole.Value), capL = L.Cap(TextRole.Label);
            float ty = yBar - capV - 18f * s;
            L.Type(text, new Vector2(x0, ty + (capV - capL) * 0.5f), TextRole.Label, Ink(0.92f));
            L.Type(Mathf.RoundToInt(progress * 100f).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%", new Vector2(x1, ty), TextRole.Value, Ink(1f), TextAlign.Right);
            int glyphs = Mathf.RoundToInt(st.Glyphs * 4f);
            if (glyphs > 0)
            {
                float tw = L.Measure(text, TextRole.Label);
                L.GlyphRun(new Vector2(x0 + tw + 18f * s, ty + (capV - capL) * 0.5f), glyphs, capL * 0.95f, 5, lw * 0.6f, Ink(0.5f));
            }

            float bw = x1 - x0, bh = 14f * s;
            var track = Ink(st.Faint);
            if (st.ArcCells > 0)
            {
                // Halo: cells, like the gauges
                int n = 44;
                float gap = 2.6f * s, cw = (bw - gap * (n - 1)) / n;
                float front = progress * n;
                for (int i = 0; i < n; i++)
                {
                    var a = new Vector2(x0 + i * (cw + gap), yBar);
                    var b = a + new Vector2(cw, bh);
                    float f = Mathf.Clamp01(front - i);
                    if (f >= 0.5f) L.Fill(a, b, Ink(1f));
                    else
                    {
                        L.Fill(a, b, track);
                        if (f > 0.05f) L.Fill(a, new Vector2(a.x + cw * f * 2f, b.y), Ink(1f));
                    }
                }
            }
            else if (st.ArcBezel)
            {
                // Meridian: a fine scale and a thin line with a pointer
                float ly = yBar + bh * 0.5f;
                L.Line(new Vector2(x0, ly), new Vector2(x1, ly), Mathf.Max(2f, 3f * s), track);
                if (progress > 0.002f) L.Line(new Vector2(x0, ly), new Vector2(x0 + bw * progress, ly), Mathf.Max(3f, 5f * s), Ink(1f));
                for (int i = 0; i <= 50; i++)
                {
                    float x = x0 + bw * i / 50f;
                    bool major = i % 10 == 0, mid = i % 5 == 0;
                    L.Line(new Vector2(x, ly - 8f * s), new Vector2(x, ly - (major ? 18f : mid ? 14f : 11f) * s), lw * (major ? 0.9f : 0.6f), Ink(major ? 0.8f : 0.45f));
                }
                float px = x0 + bw * progress;
                L.FillTri(new Vector2(px, ly + 7f * s), new Vector2(px - 6f * s, ly + 17f * s), new Vector2(px + 6f * s, ly + 17f * s), Ink(1f));
            }
            else
            {
                // Cipher: a plate with the fill set into it
                var a = new Vector2(x0, yBar - 2f * s); var b = new Vector2(x1, yBar + bh + 2f * s);
                L.Fill(a, b, Ink(st.Faint * 0.6f));
                L.Line(a, new Vector2(b.x, a.y), lw * 0.6f, Ink(0.5f));
                L.Line(new Vector2(a.x, b.y), b, lw * 0.6f, Ink(0.5f));
                if (progress > 0.002f) L.Fill(new Vector2(x0 + 3f * s, yBar + 1f * s), new Vector2(x0 + 3f * s + (bw - 6f * s) * progress, yBar + bh - 1f * s), Ink(1f));
                L.Line(new Vector2(x0 + 3f * s + (bw - 6f * s) * progress, yBar - 6f * s), new Vector2(x0 + 3f * s + (bw - 6f * s) * progress, yBar + bh + 6f * s), lw * 1.2f, Ink(1f));
                L.GlyphRun(new Vector2(x1 - 6f * 12f * s, yBar + bh + 10f * s), 6, 11f * s, 9, lw * 0.6f, Ink(0.45f));
            }
        }
    }
}
