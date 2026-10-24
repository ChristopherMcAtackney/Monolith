using Monolith.Core;
using Monolith.Hud;
using UnityEngine;

namespace Monolith.Cabin
{
    /// <summary>
    /// What the cabin draws on the screen itself, in the HUD's own type and line style: the minimal prompt for what the pilot is looking at and what the context button does there
    /// (a key cap and a few words, with a bar while a hold is under way). The seat's battle map is the HUD's (it draws the map while <see cref="CabinBoard.MapOpen"/> is on, so the
    /// radar, the warnings and the radio stay in view). Drawn after post-processing like the HUD, so it shows in captures.
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
            HudPass.Attach(go, cam, o._lines, "MN Cabin overlay", -2f, false);
            return o;
        }

        void LateUpdate()
        {
            float h = _cam != null ? _cam.pixelHeight : 1080f;
            _lines.Clear(h / 1080f);
            if (_cam != null && _cab != null) Draw(_cam.pixelWidth, _cam.pixelHeight);
            _lines.Commit();
        }

        void Draw(float w, float h)
        {
            var b = _cab.Board;
            bool want = !string.IsNullOrEmpty(b.Prompt) && !_cab.Frozen;
            _promptFade = Mathf.MoveTowards(_promptFade, want ? 1f : 0f, Time.unscaledDeltaTime * 6f);
            if (_promptFade > 0.01f && !string.IsNullOrEmpty(b.Prompt)) DrawPrompt(_lines, w, h, b);
        }

        void DrawPrompt(HudLines L, float w, float h, CabinBoard b)
        {
            float s = L.Scale, f = _promptFade;
            var ink = L.Ink;
            var col = new Color(ink.r, ink.g, ink.b, ink.a * f);
            var dim = new Color(ink.r, ink.g, ink.b, ink.a * 0.5f * f);
            float cap = L.Cap(TextRole.Value);
            string text = b.Prompt.ToUpperInvariant();
            string key = b.PromptKey;
            float capKey = L.Cap(TextRole.Label);
            float keyW = string.IsNullOrEmpty(key) ? 0f : Mathf.Max(36f * s, L.Measure(key, capKey, L.Tracking(TextRole.Label)) + 24f * s);
            float textW = L.Measure(text, cap, L.Tracking(TextRole.Value));
            float gap = keyW > 0f ? 16f * s : 0f;
            float total = keyW + gap + textW;
            float x = w * 0.5f - total * 0.5f, y = h * 0.80f;
            float lw = L.Lw();
            if (keyW > 0f)
            {
                var a = new Vector2(x, y - 9f * s); var c = new Vector2(x + keyW, y + cap + 9f * s);
                L.Fill(a, c, HudLines.Shade(0.55f * f));
                L.Rect(a, c, lw, col);
                L.Text(key, new Vector2(x + keyW * 0.5f, y + (cap - capKey) * 0.5f), capKey, col, TextAlign.Center, L.Tracking(TextRole.Label), TextRole.Label);
            }
            L.Text(text, new Vector2(x + keyW + gap, y), cap, col, TextAlign.Left, L.Tracking(TextRole.Value), TextRole.Value);
            if (b.PromptProgress >= 0f)
            {
                float by = y + cap + 26f * s;
                var a = new Vector2(w * 0.5f - total * 0.5f, by); var c = new Vector2(w * 0.5f + total * 0.5f, by + 9f * s);
                L.Rect(a, c, lw * 0.8f, dim);
                if (b.PromptProgress > 0.005f) L.Fill(a + Vector2.one * 2.5f * s, new Vector2(a.x + (c.x - a.x) * Mathf.Clamp01(b.PromptProgress) - 2.5f * s, c.y - 2.5f * s), col);
            }
        }
    }
}
