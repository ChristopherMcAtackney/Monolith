using System;
using System.Collections.Generic;
using System.IO;
using Monolith.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace Monolith.Juice
{
    /// <summary>
    /// Everything the user tunes in the juice sandbox, saved as <c>juice.json</c> next to the game (format "monolith-juice/1").
    /// Load order: <c>-mnJuice file</c>, then juice.json next to Monolith.exe, then the shipped defaults
    /// (Resources/Juice/juice_defaults.json), then the numbers below. What the user saves becomes the game's defaults: copy it over
    /// the shipped file. The rules behind the defaults (design section 9): Medium is the default; shake and flashes have an
    /// intensity and an off switch; success gets the loudest reaction.
    /// </summary>
    public sealed class JuiceSettings
    {
        public const string Format = "monolith-juice/1";
        public const string ResourcePath = "Juice/juice_defaults";
        public const string FileName = "juice.json";

        public static readonly string[] PresetNames = { "Low", "Medium", "High", "Extreme" };

        public string format = Format;
        public string about = "Juice settings. Presets set the five levels (shake, rumble, flash, size, density); the switches turn effects on and off. Edit by hand or in the sandbox panel.";
        /// <summary>Low, Medium, High, Extreme, or Custom once a level was moved by hand.</summary>
        public string preset = "Medium";
        public Effects effects = new Effects();
        public Levels levels = new Levels();
        public HitStopSettings hitStop = new HitStopSettings();
        /// <summary>How slow the sandbox's slow-motion toggle runs the game (1 = real time).</summary>
        public float slowMo = 0.25f;

        public sealed class Effects
        {
            public bool bolts = true, muzzleFlash = true, beams = true, flak = true, missileTrails = true;
            public bool shieldRipples = true, hullHits = true, explosions = true, debris = true, shockwave = true;
            public bool lights = true, screenFlash = true, cameraShake = true, rumble = true, sound = true;
        }

        public sealed class Levels
        {
            /// <summary>Camera shake (trauma) scale. 0 is off; the accessibility setting for shake.</summary>
            public float shake = 1f;
            /// <summary>Pad rumble scale.</summary>
            public float rumble = 1f;
            /// <summary>Brightness of flashes, bolts and glows, and the edge flashes. The accessibility setting for flashes.</summary>
            public float flash = 1f;
            /// <summary>Size of explosions and shockwaves.</summary>
            public float size = 1f;
            /// <summary>How many sparks, debris pieces and fire sprites an effect makes.</summary>
            public float density = 1f;
            /// <summary>How hard a shockwave shoves the player's ship through the knock queue (0 = not at all).</summary>
            public float shove = 1f;
            /// <summary>Thickness of laser bolts and of beams.</summary>
            public float boltWidth = 1f, beamWidth = 1f;
            /// <summary>Master volume of the placeholder sounds (0 to 1).</summary>
            public float volume = 0.8f;
        }

        public sealed class HitStopSettings
        {
            /// <summary>Off by default: a switch to try. A tiny freeze on kills the player makes.</summary>
            public bool enabled = false;
            /// <summary>How long the freeze lasts for a big kill, in real milliseconds.</summary>
            public float ms = 70f;
            /// <summary>The game speed during the freeze (0.05 = nearly stopped).</summary>
            public float scale = 0.05f;
        }

        // ---- presets ----------------------------------------------------------------------------------------------------

        static readonly Dictionary<string, float[]> PresetLevels = new Dictionary<string, float[]>
        {
            //                shake  rumble flash  size   density
            ["Low"]     = new[] { 0.45f, 0.45f, 0.60f, 0.80f, 0.60f },
            ["Medium"]  = new[] { 1.00f, 1.00f, 1.00f, 1.00f, 1.00f },
            ["High"]    = new[] { 1.50f, 1.40f, 1.30f, 1.20f, 1.40f },
            ["Extreme"] = new[] { 2.20f, 1.90f, 1.70f, 1.50f, 2.00f },
        };

        public void ApplyPreset(string name)
        {
            if (!PresetLevels.TryGetValue(name, out var v)) return;
            preset = name;
            levels.shake = v[0]; levels.rumble = v[1]; levels.flash = v[2]; levels.size = v[3]; levels.density = v[4];
        }

        /// <summary>The preset's name when the five levels match one, else "Custom".</summary>
        public string DetectPreset()
        {
            foreach (var kv in PresetLevels)
            {
                var v = kv.Value;
                if (Near(levels.shake, v[0]) && Near(levels.rumble, v[1]) && Near(levels.flash, v[2]) && Near(levels.size, v[3]) && Near(levels.density, v[4]))
                    return kv.Key;
            }
            return "Custom";
        }

        static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.011f;

        // ---- master helpers (every effect asks these) ---------------------------------------------------------------------

        /// <summary>Shake scale: zero when camera shake is off.</summary>
        public float ShakeScale => effects.cameraShake ? Mathf.Max(0f, levels.shake) : 0f;
        public float RumbleScale => effects.rumble ? Mathf.Max(0f, levels.rumble) : 0f;
        public float FlashScale => Mathf.Max(0f, levels.flash);

        // ---- IO ---------------------------------------------------------------------------------------------------------

        /// <summary>Where Save writes: juice.json beside the game, or the -mnJuice file.</summary>
        public static string UserPath
        {
            get
            {
                string arg = CommandLine.Get("-mnJuice");
                if (!string.IsNullOrEmpty(arg)) return Path.GetFullPath(arg);
                string dir = Path.GetDirectoryName(Application.dataPath) ?? ".";
                return Path.Combine(dir, FileName);
            }
        }

        /// <summary>Captures never write the user's file.</summary>
        public static bool ReadOnly => CommandLine.Has("-mnCapture") || CommandLine.Has("-mnJuiceReadOnly");

        public string Source { get; private set; } = "built-in numbers";

        public static JuiceSettings Load()
        {
            string path = UserPath;
            JuiceSettings s = null;
            string source = null;
            if (File.Exists(path))
            {
                try { s = Parse(File.ReadAllText(path), path); source = path; }
                catch (Exception e) { MnLog.Warn($"JUICE could not read {path}: {e.Message}; using the defaults instead"); }
            }
            if (s == null)
            {
                var asset = Resources.Load<TextAsset>(ResourcePath);
                if (asset != null)
                {
                    try { s = Parse(asset.text, "Resources/" + ResourcePath + ".json"); source = "Resources/" + ResourcePath + ".json"; }
                    catch (Exception e) { MnLog.Warn($"JUICE shipped defaults are broken: {e.Message}"); }
                }
            }
            s ??= new JuiceSettings();
            s.Source = source ?? "built-in numbers";
            s.preset = s.DetectPreset();
            return s;
        }

        public static JuiceSettings Parse(string json, string where)
        {
            JuiceSettings s;
            try { s = JsonConvert.DeserializeObject<JuiceSettings>(json); }
            catch (Exception e) { throw new InvalidDataException($"{where}: {e.Message}"); }
            if (s == null || s.format != Format) throw new InvalidDataException($"{where}: format is '{s?.format}', expected '{Format}'");
            s.effects ??= new Effects();
            s.levels ??= new Levels();
            s.hitStop ??= new HitStopSettings();
            return s;
        }

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);

        /// <summary>Write the settings to <see cref="UserPath"/> (not in capture mode). Returns true when written.</summary>
        public bool Save()
        {
            if (ReadOnly) return false;
            try
            {
                preset = DetectPreset();
                string path = UserPath;
                File.WriteAllText(path, ToJson());
                Source = path;
                return true;
            }
            catch (Exception e)
            {
                MnLog.Warn($"JUICE could not save juice.json: {e.Message}");
                return false;
            }
        }

        /// <summary>Back to the shipped defaults (does not delete the user's file until Save).</summary>
        public void ResetToDefaults()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            JuiceSettings d = null;
            if (asset != null) { try { d = Parse(asset.text, ResourcePath); } catch (Exception) { } }
            d ??= new JuiceSettings();
            effects = d.effects; levels = d.levels; hitStop = d.hitStop; slowMo = d.slowMo; preset = DetectPreset();
        }
    }
}
