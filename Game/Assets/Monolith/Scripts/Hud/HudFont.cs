using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Hud
{
    /// <summary>
    /// One HUD typeface as a signed-distance-field atlas (<c>Resources/Fonts/&lt;id&gt;.sdf.bytes</c> and <c>.fontmeta.json</c>, baked from a TrueType file by
    /// <c>tools/hud/make_font_atlas.py</c>; every typeface is under the SIL Open Font License, see <c>docs/licences/fonts.md</c>). The shader (<c>MN_Lines</c> pass 4) turns the
    /// distance into a one-pixel edge at any size, so the type is crisp from the smallest label to the largest banner. Metrics are in atlas pixels at <see cref="Size"/> pixels to
    /// the em; callers scale by <c>em / Size</c>. The atlas texture is made on first use, so the metrics alone can be read headless.
    /// </summary>
    public sealed class HudFont
    {
        public struct Glyph
        {
            public int X, Y, W, H;           // the cell in the atlas (row 0 at the top), including the distance spread
            public float Adv;                // advance, atlas px
            public float Bx, By;             // the cell's left edge and top edge relative to the pen on the baseline (By up)
            public bool Visible => W > 0;
        }

        public string Id { get; private set; }
        public string Family { get; private set; }
        public string Licence { get; private set; }
        public int Size { get; private set; }
        public int Spread { get; private set; }
        public int AtlasW { get; private set; }
        public int AtlasH { get; private set; }
        public float Ascent { get; private set; }
        public float Descent { get; private set; }
        public float CapHeight { get; private set; }
        public float XHeight { get; private set; }
        public int GlyphCount => _glyphs.Count;

        readonly Dictionary<char, Glyph> _glyphs = new Dictionary<char, Glyph>();
        readonly Glyph[] _ascii = new Glyph[128];          // the same glyphs for the characters the HUD mostly prints, without a dictionary look-up
        readonly bool[] _hasAscii = new bool[128];
        byte[] _bytes;
        Texture2D _tex;
        Glyph _fallback;

        static readonly Dictionary<string, HudFont> Cache = new Dictionary<string, HudFont>();

        /// <summary>Cap height as a share of the em (about 0.7): the type scale is set in cap heights, so labels line up across typefaces.</summary>
        public float CapRatio => CapHeight / Size;

        /// <summary>Load (and keep) a font by id, or throw if its files are missing.</summary>
        public static HudFont Get(string id)
        {
            if (Cache.TryGetValue(id, out var f)) return f;
            var meta = Resources.Load<TextAsset>("Fonts/" + id + ".fontmeta") ?? throw new FileNotFoundException($"Resources/Fonts/{id}.fontmeta.json is missing (tools/hud/make_font_atlas.py makes it)");
            var bytes = Resources.Load<TextAsset>("Fonts/" + id + ".sdf") ?? throw new FileNotFoundException($"Resources/Fonts/{id}.sdf.bytes is missing");
            f = Parse(meta.text, bytes.bytes);
            Cache[id] = f;
            return f;
        }

        /// <summary>Read a font from its metrics text and atlas bytes (also what the checks use on the files straight off the disk).</summary>
        public static HudFont Parse(string metaJson, byte[] atlas)
        {
            var j = JObject.Parse(metaJson);
            if ((string)j["format"] != "monolith-hud-font/1") throw new InvalidDataException("not a monolith-hud-font/1 file");
            var f = new HudFont
            {
                Id = (string)j["id"], Family = (string)j["family"], Licence = (string)j["licence"],
                Size = (int)j["size"], Spread = (int)j["spread"], AtlasW = (int)j["atlasW"], AtlasH = (int)j["atlasH"],
                Ascent = (float)j["ascent"], Descent = (float)j["descent"], CapHeight = (float)j["capHeight"], XHeight = (float)j["xHeight"],
            };
            if (atlas.Length != f.AtlasW * f.AtlasH) throw new InvalidDataException($"atlas is {atlas.Length} bytes, expected {f.AtlasW * f.AtlasH}");
            f._bytes = atlas;
            foreach (var p in (JObject)j["glyphs"])
            {
                var g = (JObject)p.Value;
                var gl = new Glyph { X = (int)g["x"], Y = (int)g["y"], W = (int)g["w"], H = (int)g["h"], Adv = (float)g["adv"], Bx = (float)g["bx"], By = (float)g["by"] };
                f._glyphs[p.Key[0]] = gl;
                if (p.Key[0] < 128) { f._ascii[p.Key[0]] = gl; f._hasAscii[p.Key[0]] = true; }
            }
            if (!f._glyphs.TryGetValue('?', out f._fallback)) f._fallback = default;
            return f;
        }

        public bool Has(char c) => _glyphs.ContainsKey(c);

        /// <summary>The glyph for a character; a missing one shows as the font's question mark.</summary>
        public Glyph Get(char c)
        {
            if (c < 128 && _hasAscii[c]) return _ascii[c];
            return _glyphs.TryGetValue(c, out var g) ? g : (_glyphs.TryGetValue(char.ToUpperInvariant(c), out g) ? g : _fallback);
        }

        public float Advance(char c) => Get(c).Adv;

        /// <summary>The atlas as a texture (trilinear, clamped, with mips: a distance field averages cleanly), made on first use.</summary>
        public Texture2D Texture
        {
            get
            {
                if (_tex != null) return _tex;
                // Unity's first row is the bottom one; the atlas file's first row is the top.
                var flipped = new byte[_bytes.Length];
                for (int y = 0; y < AtlasH; y++) System.Buffer.BlockCopy(_bytes, y * AtlasW, flipped, (AtlasH - 1 - y) * AtlasW, AtlasW);
                _tex = new Texture2D(AtlasW, AtlasH, TextureFormat.R8, true, true) { name = "MN_HudFont_" + Id, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 1, hideFlags = HideFlags.HideAndDontSave };
                _tex.SetPixelData(flipped, 0);          // the base level only; Apply builds the rest of the chain
                _tex.Apply(true, true);
                return _tex;
            }
        }

        /// <summary>The width of a string at <paramref name="em"/> pixels to the em, with <paramref name="tracking"/> (em) added after every character but the last.</summary>
        public float Measure(string s, float em, float tracking = 0f)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            float k = em / Size, w = 0f;
            for (int i = 0; i < s.Length; i++) w += Get(s[i]).Adv * k + (i < s.Length - 1 ? tracking * em : 0f);
            return w;
        }
    }
}
