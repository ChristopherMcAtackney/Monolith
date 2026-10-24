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
        /// <summary>The boost (afterburner) treatment: the view widening, the jolt, the lens, the dust, the flame and the sound.</summary>
        public BoostSettings boost = new BoostSettings();
        /// <summary>The player's death sequence (first person: the cabin tears apart, you are flung out, you die).</summary>
        public DeathSettings death = new DeathSettings();
        /// <summary>The retro brake (R3): the forward lurch, the view narrowing, the flare at the nose, the thump and hiss, the rumble.</summary>
        public BrakeSettings brake = new BrakeSettings();
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

        /// <summary>
        /// The boost's feel (<see cref="BoostFx"/>). Each piece has its own level, 0 turns it off; <c>intensity</c> scales them all together
        /// (the accessibility knob for the boost: the view widening and the edge blur are the parts a sensitive player may want gone, so
        /// <c>fovHold</c>, <c>fovPunch</c> and <c>lens</c> can be zeroed on their own). The shake and rumble switches in the main settings still apply.
        /// </summary>
        public sealed class BoostSettings
        {
            /// <summary>Off leaves the afterburner as it was: speed only, the old steady hum, the audio system's roar.</summary>
            public bool enabled = true;
            /// <summary>Everything the boost adds, together (0 to 2).</summary>
            public float intensity = 1f;
            /// <summary>Degrees the view widens by while the burner burns (the cabin's standing view is 13 wider than the cockpit's), and the extra thrown out on ignition that settles back.</summary>
            public float fovHold = 12f, fovPunch = 8f;
            /// <summary>Ignition camera shake and pad rumble (the jolt, then the steady hum), as multiples of the defaults.</summary>
            public float shake = 1f, rumble = 1f;
            /// <summary>Radial blur and colour fringing toward the edges (the middle and the target stay sharp).</summary>
            public float lens = 1f;
            /// <summary>How much longer and brighter the motion dust is drawn.</summary>
            public float streaks = 1f;
            /// <summary>Size of the flame on ships that burn, and the engine glow at the rim of the picture.</summary>
            public float flare = 1f;
            /// <summary>Volume of the ignition whoomph and the empty-tank sputter.</summary>
            public float sound = 1f;
        }

        /// <summary>
        /// The retro brake's feel (<see cref="BrakeFx"/>), the boost's mirror image: the view narrows where the boost widens. Each piece has its own level, 0 turns it off;
        /// <c>intensity</c> scales them all together. The shake and rumble switches in the main settings still apply.
        /// </summary>
        public sealed class BrakeSettings
        {
            /// <summary>Off leaves the brake as physics only: the ship stops, nothing else happens.</summary>
            public bool enabled = true;
            public float intensity = 1f;
            /// <summary>Degrees the view narrows by while the retros fire, and the extra thrown in on the click that settles back (the boost's kick in reverse; 0 keeps the view steady).</summary>
            public float fovHold = 3f, fovPunch = 5f;
            /// <summary>The forward lurch of the view (1 = about 0.17 m forward and a degree and a half of nod at cruise speed; 0 turns it off).</summary>
            public float lurch = 1f;
            /// <summary>Camera shake and pad rumble.</summary>
            public float shake = 1f, rumble = 1f;
            /// <summary>Size of the retro flare at the nose.</summary>
            public float flare = 1f;
            /// <summary>Volume of the thump and the hiss.</summary>
            public float sound = 1f;
        }

        public sealed class DeathSettings
        {
            /// <summary>Off: no sequence, the plain "ship lost" as before.</summary>
            public bool enabled = true;
            /// <summary>The flashes, the shake and the pad in it (0 to 2). The shake and rumble switches in the main settings still apply.</summary>
            public float intensity = 1f;
            /// <summary>How fast the view tumbles once you are flung out (0 to 1.5; 0 keeps the view steady: the setting for anyone the spinning makes ill).</summary>
            public float tumble = 1f;
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
        [JsonIgnore] public float ShakeScale => effects.cameraShake ? Mathf.Max(0f, levels.shake) : 0f;
        [JsonIgnore] public float RumbleScale => effects.rumble ? Mathf.Max(0f, levels.rumble) : 0f;
        [JsonIgnore] public float FlashScale => Mathf.Max(0f, levels.flash);

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

        [JsonIgnore] public string Source { get; private set; } = "built-in numbers";

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
            s.boost ??= new BoostSettings();
            s.death ??= new DeathSettings();
            s.brake ??= new BrakeSettings();
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
                about = "Your juice settings, saved by the sandbox as you tune. Copy this file over Game/Assets/Monolith/Resources/Juice/juice_defaults.json to make them the game's defaults.";
                var l = levels;
                l.shake = R(l.shake); l.rumble = R(l.rumble); l.flash = R(l.flash); l.size = R(l.size); l.density = R(l.density);
                l.shove = R(l.shove); l.boltWidth = R(l.boltWidth); l.beamWidth = R(l.beamWidth); l.volume = R(l.volume);
                hitStop.ms = R(hitStop.ms); hitStop.scale = R(hitStop.scale); slowMo = R(slowMo);
                var b = boost;
                b.intensity = R(b.intensity); b.fovHold = R(b.fovHold); b.fovPunch = R(b.fovPunch); b.shake = R(b.shake); b.rumble = R(b.rumble);
                b.lens = R(b.lens); b.streaks = R(b.streaks); b.flare = R(b.flare); b.sound = R(b.sound);
                death.intensity = R(death.intensity); death.tumble = R(death.tumble);
                var k = brake;
                k.intensity = R(k.intensity); k.fovHold = R(k.fovHold); k.fovPunch = R(k.fovPunch); k.lurch = R(k.lurch); k.shake = R(k.shake); k.rumble = R(k.rumble);
                k.flare = R(k.flare); k.sound = R(k.sound);
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

        static float R(float v) => (float)System.Math.Round(v, 3);

        /// <summary>Back to the shipped defaults (does not delete the user's file until Save).</summary>
        public void ResetToDefaults()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            JuiceSettings d = null;
            if (asset != null) { try { d = Parse(asset.text, ResourcePath); } catch (Exception) { } }
            d ??= new JuiceSettings();
            effects = d.effects; levels = d.levels; hitStop = d.hitStop; boost = d.boost; death = d.death; brake = d.brake; slowMo = d.slowMo; preset = DetectPreset();
        }
    }
}
