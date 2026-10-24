using System.IO;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Loading
{
    /// <summary>
    /// The loading screen's picture: where it comes from and how it sits on a screen of any shape. The splash is a PNG shipped as bytes (<c>Resources/Loading/&lt;name&gt;.png.bytes</c>:
    /// the user's own art, <c>art_src/ui/Oort_Splash.png</c>), and a <c>Loading/splash.png</c> beside the game replaces it (the player's own, like the Audio folder). It is never stretched:
    /// it is scaled the same both ways to fill the screen, but by no more than <see cref="CropLimit"/> of the way to filling it on the shorter axis, so a very wide or a squarer screen
    /// gets a little black at the sides (the picture's own edge fades into it) rather than losing a third of the art.
    /// </summary>
    public static class LoadingSplash
    {
        public const string DefaultName = "oort_splash";

        /// <summary>How much of a cover fit is kept: 1 crops as much as it takes to fill the screen; below 1 letterboxes the difference.</summary>
        public const float CropLimit = 0.86f;

        /// <summary>Load a splash by name (a player's file beside the game wins). Null when there is none.</summary>
        public static Texture2D Load(string name = null)
        {
            string userFile = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Loading", "splash.png");
            byte[] bytes = null;
            string source = null;
            try
            {
                if (File.Exists(userFile)) { bytes = File.ReadAllBytes(userFile); source = userFile; }
            }
            catch (System.Exception e) { MnLog.Warn("LOADING could not read " + userFile + ": " + e.Message); }
            if (bytes == null)
            {
                var asset = Resources.Load<TextAsset>("Loading/" + (name ?? DefaultName) + ".png") ?? Resources.Load<TextAsset>("Loading/" + DefaultName + ".png");
                if (asset == null) return null;
                bytes = asset.bytes; source = "Resources/Loading/" + asset.name;
            }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, false) { name = "MN_Splash", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            if (!ImageConversion.LoadImage(tex, bytes, true)) { Object.Destroy(tex); MnLog.Warn("LOADING the splash is not a picture: " + source); return null; }
            MnLog.Info($"LOADING splash {tex.width}x{tex.height} from {source}");
            return tex;
        }

        /// <summary>
        /// Where a picture of <paramref name="iw"/> x <paramref name="ih"/> goes on a screen of <paramref name="w"/> x <paramref name="h"/>, centred, scaled the same both ways (and a touch more by
        /// <paramref name="settle"/>, the slow settle): the pixel rectangle it covers (clipped to the screen), the part of the picture that shows (texture space, v up), and how much of its width and height
        /// its own edges fade over (only where they are inside the screen).
        /// </summary>
        public static void Fit(float w, float h, float iw, float ih, float settle, out Vector2 min, out Vector2 max, out Rect uv, out float featherU, out float featherV)
        {
            float cover = Mathf.Max(w / iw, h / ih), contain = Mathf.Min(w / iw, h / ih);
            float scale = Mathf.Max(contain, cover * CropLimit) * Mathf.Max(0.5f, settle);
            float dw = iw * scale, dh = ih * scale;
            float x0 = (w - dw) * 0.5f, y0 = (h - dh) * 0.5f;
            min = new Vector2(Mathf.Max(0f, x0), Mathf.Max(0f, y0));
            max = new Vector2(Mathf.Min(w, x0 + dw), Mathf.Min(h, y0 + dh));
            float u0 = (min.x - x0) / dw, u1 = (max.x - x0) / dw;
            float vTop = 1f - (min.y - y0) / dh, vBottom = 1f - (max.y - y0) / dh;
            uv = Rect.MinMaxRect(u0, vBottom, u1, vTop);
            featherU = x0 > 1f ? 0.05f : 0f;
            featherV = y0 > 1f ? 0.07f : 0f;
        }
    }
}
