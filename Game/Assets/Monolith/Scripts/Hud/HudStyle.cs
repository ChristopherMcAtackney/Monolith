using System;
using System.Collections.Generic;
using System.IO;
using Monolith.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Hud
{
    public enum ReticleKind { Ring, Cross, Brackets }

    /// <summary>
    /// One look for everything the player's tech draws: the HUD, the battle map, the cabin's displays. Single-colour line graphics with a restrained glow, one accent for warnings,
    /// terse type, and a hint of the aliens' glyphs as ornament only. A style is data (colour, typeface, arc geometry, glyph density); the HUD draws the same readings in every
    /// style, so the player picks the one they read best. Sizes are in 1080p pixels and scale with the screen. Switch at run time with <c>-mnHudStyle name</c>, in the pause
    /// screen, or with F9; the choice is kept in <c>hud.json</c> beside the game.
    /// </summary>
    public sealed class HudStyle
    {
        public string Name, Title, About;
        /// <summary>Typeface id (Resources/Fonts) and its weight bias in pixels (the shader thickens or thins the edge: 0 is the font's own weight).</summary>
        public string FontId;
        public float Bold;
        /// <summary>The ink (linear, drawn after tone mapping), a second tone for the cabin's table and panels, and the one accent that means warning.</summary>
        public Color Ink, Ink2, Warn;
        /// <summary>Alpha of secondary lines and type, and of the empty track of a gauge.</summary>
        public float Dim = 0.62f, Faint = 0.40f;

        // ---- type: cap heights in 1080p pixels (Label, Body, Value, Display), and tracking in ems
        public float CapLabel = 13f, CapBody = 16.5f, CapValue = 23f, CapDisplay = 48f;
        public float TrackLabel = 0.14f, TrackBody = 0.04f, TrackValue = 0.03f, TrackDisplay = 0.02f;

        // ---- light: glow radius (px at 1080p), glow strength, the dark keyline that keeps lines readable over a bright hull
        public float GlowRadius = 2.8f, GlowStrength = 0.34f, KeyPx = 1.1f, KeyAlpha = 0.45f;

        // ---- the two main gauges
        /// <summary>Radius of the thick band's centre line round the reticle, half the sweep (degrees), thickness of the thick and the thin band, the gap between them, and the cells the thick one is cut into (0: one piece).</summary>
        public float ArcR = 236f, ArcHalfSweep = 46f, ArcThick = 20f, ArcThin = 7f, ArcGap = 7f;
        public int ArcCells = 26;
        public float ArcCellGap = 2.2f;
        /// <summary>A bezel of fine scale ticks outside the gauge.</summary>
        public bool ArcBezel;
        /// <summary>Glyph ornament: 0 none .. 1 dense (a row of glyph marks along the gauges and beside the headings; never in place of a number).</summary>
        public float Glyphs = 0.15f;
        public ReticleKind Reticle = ReticleKind.Ring;
        /// <summary>Panel headings carry a rule under them.</summary>
        public bool HeadingRule = true;

        /// <summary>A colour as it looks on screen (sRGB 0..255) to the linear value the HUD's render target wants: the buffer is sRGB-encoded after tone mapping, so a linear 0.5 would come out pale.</summary>
        public static Color Srgb(int r, int g, int b, float a = 1f)
        {
            var c = new Color(r / 255f, g / 255f, b / 255f, 1f).linear;
            c.a = a;
            return c;
        }

        public Color InkA(float a) => new Color(Ink.r, Ink.g, Ink.b, Ink.a * a);
        public Color WarnA(float a) => new Color(Warn.r, Warn.g, Warn.b, Warn.a * a);

        HudFont _font;
        public HudFont Font => _font ??= HudFont.Get(FontId);
    }

    /// <summary>The styles, the one in use, and where the player's choice is kept.</summary>
    public static class HudStyles
    {
        public const string Default = "halo";

        static readonly List<HudStyle> All = new List<HudStyle>
        {
            // Halo: amber, Oxanium (squared, a little alien), thick segmented arcs close round the reticle, the fewest ornaments.
            new HudStyle
            {
                Name = "halo", Title = "HALO",
                About = "Amber. Oxanium type. Thick segmented arcs close round the reticle, like cells of a battery. Sparest.",
                FontId = "oxanium", Bold = 0.10f,
                Ink = HudStyle.Srgb(255, 172, 56, 0.96f), Ink2 = HudStyle.Srgb(255, 208, 120, 0.96f), Warn = HudStyle.Srgb(255, 58, 44, 0.98f),
                ArcR = 232f, ArcHalfSweep = 44f, ArcThick = 21f, ArcThin = 8f, ArcGap = 7f, ArcCells = 24, ArcCellGap = 2.4f, Glyphs = 0.12f, Reticle = ReticleKind.Ring,
            },
            // Meridian: pale cyan, B612 (drawn for aircraft cockpit screens), long continuous arcs with a fine scale, no ornament: the plainest to read.
            new HudStyle
            {
                Name = "meridian", Title = "MERIDIAN",
                About = "Pale cyan. B612 type, drawn for cockpit screens. Wide continuous arcs with a fine scale. No ornament.",
                FontId = "b612", Bold = 0.18f,
                Ink = HudStyle.Srgb(124, 226, 255, 0.96f), Ink2 = HudStyle.Srgb(196, 244, 255, 0.96f), Warn = HudStyle.Srgb(255, 140, 30, 0.98f),
                CapLabel = 13.5f, CapBody = 17f, CapValue = 24f, CapDisplay = 50f, TrackLabel = 0.12f,
                ArcR = 292f, ArcHalfSweep = 54f, ArcThick = 13f, ArcThin = 6f, ArcGap = 6f, ArcCells = 0, ArcBezel = true, Glyphs = 0f, Reticle = ReticleKind.Cross,
            },
            // Cipher: green-teal, Jura (thin, rounded-square), heavy plate arcs with glyph rows: the closest to the aliens' glowing consoles.
            new HudStyle
            {
                Name = "cipher", Title = "CIPHER",
                About = "Green-teal. Jura type. Heavy plate arcs with rows of alien glyphs, kept to ornament. Most atmosphere.",
                FontId = "jura", Bold = 0.32f,
                Ink = HudStyle.Srgb(88, 255, 200, 0.96f), Ink2 = HudStyle.Srgb(170, 255, 228, 0.96f), Warn = HudStyle.Srgb(255, 70, 50, 0.98f),
                CapLabel = 13.5f, CapBody = 17f, CapValue = 24f, CapDisplay = 50f, TrackLabel = 0.16f, TrackBody = 0.05f,
                GlowRadius = 3.2f, GlowStrength = 0.36f,
                ArcR = 256f, ArcHalfSweep = 38f, ArcThick = 28f, ArcThin = 9f, ArcGap = 8f, ArcCells = 0, Glyphs = 0.85f, Reticle = ReticleKind.Brackets,
            },
        };

        static HudStyle _current;
        static float _textScale = -1f;

        /// <summary>The player's size for all HUD type (1 is the design size): <c>-mnHudText 1.15</c>, or "textScale" in hud.json.</summary>
        public static float TextScale
        {
            get
            {
                if (_textScale > 0f) return _textScale;
                float v = CommandLine.GetFloat("-mnHudText", -1f);
                if (v <= 0f)
                {
                    try { if (File.Exists(SettingsFile)) v = (float?)JObject.Parse(File.ReadAllText(SettingsFile))["textScale"] ?? 1f; }
                    catch { v = 1f; }
                }
                _textScale = Mathf.Clamp(v <= 0f ? 1f : v, 0.7f, 1.6f);
                return _textScale;
            }
            set => _textScale = Mathf.Clamp(value, 0.7f, 1.6f);
        }

        public static IReadOnlyList<HudStyle> Styles => All;
        public static IEnumerable<string> Names { get { foreach (var s in All) yield return s.Name; } }

        public static string SettingsFile => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "hud.json");

        /// <summary>The style in use: -mnHudStyle, else hud.json, else halo.</summary>
        public static HudStyle Current
        {
            get
            {
                if (_current != null) return _current;
                string want = CommandLine.Get("-mnHudStyle");
                if (string.IsNullOrEmpty(want)) want = ReadSaved();
                _current = Find(want) ?? Find(Default);
                if (!string.IsNullOrEmpty(want) && Find(want) == null) MnLog.Warn($"HUD style '{want}' is not one of: {string.Join(", ", Names)}; using {_current.Name}");
                return _current;
            }
        }

        public static HudStyle Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var s in All) if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        /// <summary>Switch to a style by name (true if there is one). With <paramref name="save"/> the choice is written to hud.json (never in a capture or a check).</summary>
        public static bool Set(string name, bool save = false)
        {
            var s = Find(name);
            if (s == null) return false;
            _current = s;
            if (save) Save();
            return true;
        }

        /// <summary>Step to the next (or previous) style, wrapping.</summary>
        public static HudStyle Cycle(int dir = 1, bool save = true)
        {
            int i = All.IndexOf(Current);
            _current = All[((i + dir) % All.Count + All.Count) % All.Count];
            if (save) Save();
            return _current;
        }

        static string ReadSaved()
        {
            try { return File.Exists(SettingsFile) ? (string)JObject.Parse(File.ReadAllText(SettingsFile))["style"] : null; }
            catch (Exception e) { MnLog.Warn("HUD could not read hud.json: " + e.Message); return null; }
        }

        static void Save()
        {
            if (CommandLine.Has("-mnCapture") || Application.isBatchMode) return;
            try { File.WriteAllText(SettingsFile, new JObject { ["style"] = _current.Name, ["textScale"] = Math.Round(TextScale, 2) }.ToString()); }
            catch (Exception e) { MnLog.Warn("HUD could not save hud.json: " + e.Message); }
        }
    }
}
