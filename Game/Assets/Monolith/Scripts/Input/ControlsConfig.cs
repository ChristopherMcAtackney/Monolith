using System;
using System.IO;
using Monolith.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace Monolith.Input
{
    /// <summary>
    /// Pad and keyboard mappings, dead zones and response curves (Resources/Input/controls.json, format
    /// "monolith-controls/1"; -mnControls path reads a file instead, which is how the playtest build lets the user tune
    /// them). Control names are Input System paths on the Gamepad or Keyboard ("rightStick", "leftStick/x",
    /// "buttonSouth", "dpad/up", "tab", "1"). Unknown fields are errors so a typo cannot silently do nothing.
    /// </summary>
    public sealed class ControlsConfig
    {
        public const string Format = "monolith-controls/1";
        public const string ResourcePath = "Input/controls";

        public string format;
        public string about;
        public PadConfig pad = new PadConfig();
        public KeyboardConfig keyboard = new KeyboardConfig();

        public sealed class StickShape
        {
            public string stick;
            public string control;
            public float deadZone = 0.1f;
            public float outerDeadZone = 0.04f;
            public float exponent = 1f;
            public bool pushForwardIsNoseDown = true;
            public bool rightIsRollRight = true;
            public float ratePerSecond = 0.8f;
            public string why;
        }

        public sealed class PadButtons
        {
            public string afterburner, primaryFire, targetAhead, targetNext, countermeasure, matchSpeed, orders,
                secondaryFire, missileFire, throttleFull, throttleZero, throttleThird, throttleTwoThirds;
        }

        public sealed class SystemButtons
        {
            public string pause, testKnock, resume, respawn, quit;
        }

        public sealed class PadConfig
        {
            public StickShape aim = new StickShape();
            public StickShape roll = new StickShape();
            public StickShape throttle = new StickShape();
            public PadButtons buttons = new PadButtons();
            public bool afterburnerToggle = true;
            public string afterburnerToggleWhy;
            public SystemButtons system = new SystemButtons();
        }

        public sealed class KeyboardConfig
        {
            public string pitchDown, pitchUp, yawLeft, yawRight, rollLeft, rollRight, throttleUp, throttleDown;
            public float throttleRatePerSecond = 0.8f;
            public string throttleZero, throttleThird, throttleTwoThirds, throttleFull, afterburner, matchSpeed,
                targetNext, targetAhead, primaryFire, why;
            public SystemButtons system = new SystemButtons();
        }

        /// <summary>From -mnControls when given (and present), else Resources. Returns the config and where it came from.</summary>
        public static ControlsConfig Load(out string source)
        {
            string path = CommandLine.Get("-mnControls");
            if (!string.IsNullOrEmpty(path))
            {
                if (File.Exists(path))
                {
                    source = Path.GetFullPath(path);
                    return Parse(File.ReadAllText(path), source);
                }
                MnLog.Warn($"controls file '{path}' not found; using the built-in controls");
            }
            var asset = Resources.Load<TextAsset>(ResourcePath) ?? throw new FileNotFoundException($"Resources/{ResourcePath}.json is missing");
            source = $"Resources/{ResourcePath}.json";
            return Parse(asset.text, source);
        }

        public static ControlsConfig Parse(string json, string where)
        {
            ControlsConfig c;
            try
            {
                c = JsonConvert.DeserializeObject<ControlsConfig>(json,
                    new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
            }
            catch (Exception e) { throw new InvalidDataException($"{where}: {e.Message}"); }
            if (c == null || c.format != Format) throw new InvalidDataException($"{where}: format is '{c?.format}', expected '{Format}'");
            return c;
        }
    }

    /// <summary>Stick shaping: dead zones and response curves. Pure maths (documented in controls.json's "why" notes).</summary>
    public static class StickShaping
    {
        /// <summary>
        /// Radial dead zone and curve for a two-axis stick: the deflection's length past the dead zone is rescaled to 0..1
        /// (so output starts at 0 at the dead zone's edge, no jump), reaches 1 <paramref name="outer"/> short of the
        /// gate, and is raised to <paramref name="exponent"/>. Direction is kept, so diagonals aim where they point.
        /// </summary>
        public static Vector2 Radial(Vector2 v, float dead, float outer, float exponent)
        {
            float m = v.magnitude;
            if (m <= dead || m < 1e-6f) return Vector2.zero;
            float t = Mathf.Clamp01((m - dead) / Mathf.Max(1e-4f, 1f - dead - outer));
            return v / m * Mathf.Pow(t, exponent);
        }

        /// <summary>The same for one axis on its own (axial dead zone).</summary>
        public static float Axial(float x, float dead, float outer, float exponent)
        {
            float a = Mathf.Abs(x);
            if (a <= dead) return 0f;
            float t = Mathf.Clamp01((a - dead) / Mathf.Max(1e-4f, 1f - dead - outer));
            return Mathf.Sign(x) * Mathf.Pow(t, exponent);
        }
    }
}
