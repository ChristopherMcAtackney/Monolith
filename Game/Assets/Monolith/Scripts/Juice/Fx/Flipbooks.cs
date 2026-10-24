using System;
using System.Collections.Generic;
using UnityEngine;

namespace Monolith.Juice
{
    /// <summary>
    /// The library of simulated explosions. Each one is a fluid simulation (Blender's Mantaflow, scripted in <c>art_src/juice_fx</c>) rendered into a flipbook
    /// atlas, a grid of square frames in one RGBA8 texture: R the burning gas, G the smoke (both integrated along the view), B and A the slope of the smoke's
    /// surface (so the engine can light it). The atlases are loaded from <c>Resources/Juice/Flip</c> and copied into one texture array, so every flipbook on
    /// screen is drawn by the one effect pass in one draw call.
    /// </summary>
    public static class FlipLibrary
    {
        [Serializable]
        public sealed class Meta
        {
            public string name;
            public int grid, frame, frames;
            public float[] times;
            public float seconds, startSeconds, fireScale, smokeScale, radius90, domainMetres;
        }

        public sealed class Variant
        {
            public Meta M;
            public int Slice;
            public string Name => M.name;
            /// <summary>"fire" or "smoke": the family a blast picks from.</summary>
            public string Family => M.name.StartsWith("smoke") ? "smoke" : "fire";
            /// <summary>Half the width of the sprite in metres for a blast whose fireball is <paramref name="radius"/> metres across its radius.</summary>
            public float HalfSize(float radius) => radius / Mathf.Max(0.55f, M.radius90);
            public int Grid => M.grid;
        }

        static readonly List<Variant> _all = new List<Variant>();
        static Texture2DArray _array;
        static bool _loaded;

        public static string Report { get; private set; } = "flipbooks not loaded";
        public static IReadOnlyList<Variant> All { get { Load(); return _all; } }
        public static Texture2DArray Array { get { Load(); return _array; } }
        public static bool Available { get { Load(); return _all.Count > 0 && _array != null; } }

        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                var metas = Resources.LoadAll<TextAsset>("Juice/Flip");
                var found = new List<KeyValuePair<Meta, Texture2D>>();
                foreach (var ta in metas)
                {
                    var m = JsonUtility.FromJson<Meta>(ta.text);
                    if (m == null || string.IsNullOrEmpty(m.name) || m.times == null || m.times.Length < 2) continue;
                    var tex = Resources.Load<Texture2D>("Juice/Flip/" + m.name);
                    if (tex == null) { Debug.LogWarning($"JUICE flipbook '{m.name}' has no atlas"); continue; }
                    found.Add(new KeyValuePair<Meta, Texture2D>(m, tex));
                }
                if (found.Count == 0) { Report = "no flipbooks in Resources/Juice/Flip"; return; }
                found.Sort((a, b) => string.CompareOrdinal(a.Key.name, b.Key.name));
                var first = found[0].Value;
                var keep = new List<KeyValuePair<Meta, Texture2D>>();
                foreach (var kv in found)
                    if (kv.Value.width == first.width && kv.Value.height == first.height && kv.Value.format == first.format && kv.Value.mipmapCount == first.mipmapCount) keep.Add(kv);
                    else Debug.LogWarning($"JUICE flipbook '{kv.Key.name}' differs in size or format from '{found[0].Key.name}', skipped");
                int fw = first.width, fh = first.height; var ff = first.format;
                _array = new Texture2DArray(fw, fh, keep.Count, ff, first.mipmapCount > 1, true)
                { name = "MN_FlipArray", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
                long bytes = 0;
                for (int i = 0; i < keep.Count; i++)
                {
                    Graphics.CopyTexture(keep[i].Value, 0, _array, i);
                    bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(keep[i].Value);
                    _all.Add(new Variant { M = keep[i].Key, Slice = i });
                    Resources.UnloadAsset(keep[i].Value);
                }
                bytes = (long)fw * fh * keep.Count * (ff == TextureFormat.DXT1 ? 1 : ff == TextureFormat.BC7 || ff == TextureFormat.DXT5 ? 2 : 8) / 2;
                Report = $"{keep.Count} flipbooks, {fw}x{fh} {ff} each, {bytes / 1048576f:0.0} MB of video memory in all";
            }
            catch (Exception e)
            {
                Report = "flipbooks failed to load: " + e.GetType().Name + " " + e.Message + " " + e.StackTrace;
                Debug.LogError("JUICE " + Report);
            }
        }

        public static Variant Find(string name)
        {
            Load();
            foreach (var v in _all) if (v.M.name == name) return v;
            return null;
        }

        /// <summary>A random variant of a family ("fire" or "smoke"), or null when there are none.</summary>
        public static Variant Pick(string family, FxRng rng)
        {
            Load();
            int n = 0;
            foreach (var v in _all) if (v.Family == family) n++;
            if (n == 0) return null;
            int k = rng.Int(0, n);
            foreach (var v in _all) if (v.Family == family && k-- == 0) return v;
            return null;
        }

        /// <summary>The flipbook frame (a float: the whole part is the frame, the rest the blend toward the next) at <paramref name="u"/>, 0 to 1 through its life.</summary>
        public static float FrameAt(Variant v, float u)
        {
            var t = v.M.times;
            float time = Mathf.Lerp(v.M.startSeconds, v.M.seconds, Mathf.Clamp01(u));
            int hi = t.Length - 1;
            if (time >= t[hi]) return hi;
            int lo = 0;
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (t[mid] <= time) lo = mid; else hi = mid; }
            float span = Mathf.Max(1e-4f, t[lo + 1] - t[lo]);
            return lo + Mathf.Clamp01((time - t[lo]) / span);
        }
    }
}
