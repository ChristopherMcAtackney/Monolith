using System;
using System.IO;
using Monolith.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace Monolith.Cabin
{
    /// <summary>
    /// The cabin's numbers (Resources/Cabin/cabin.json, format "monolith-cabin-config/1"; <c>-mnCabin file</c> reads a file instead, which is how a playtest folder
    /// lets them be tuned without a rebuild): how the pilot walks and looks, how long each physical action takes, what the eye's exposure does in each light, what the
    /// controls are. Unknown fields are errors, so a typo cannot silently do nothing. Every value says where it came from in its "why".
    /// </summary>
    public sealed class CabinConfig
    {
        public const string Format = "monolith-cabin-config/1";
        public const string ResourcePath = "Cabin/cabin";

        public string format, about;
        public Body body = new Body();
        public Timing timing = new Timing();
        public Exposure exposure = new Exposure();
        public Lighting light = new Lighting();
        public Scan scan = new Scan();
        public Controls controls = new Controls();
        public Rack rack = new Rack();

        /// <summary>What the weapon rack offers when a mission does not say (weapon ids from Resources/Combat/weapons.json), and what it starts loaded with.</summary>
        public sealed class Rack
        {
            public System.Collections.Generic.List<string> guns = new System.Collections.Generic.List<string> { "needle", "hammer" };
            public System.Collections.Generic.List<string> missiles = new System.Collections.Generic.List<string> { "missile_heat", "missile_lock", "torpedo_heavy" };
            public System.Collections.Generic.List<string> defaultGuns = new System.Collections.Generic.List<string> { "needle", "hammer" };
            public System.Collections.Generic.List<string> defaultMissiles = new System.Collections.Generic.List<string> { "missile_heat", "missile_lock" };
            public string why;
        }

        public sealed class Body
        {
            /// <summary>Where the seated eye sits in the ship's frame (x right, y up, z forward); the same as view.json's camera eye, so sitting down puts the view exactly where the flight view is.</summary>
            public float[] shipEye = { 0f, 0.4f, 1.5f };
            public float walkSpeed = 1.25f, backSpeed = 0.9f, strafeSpeed = 1.0f, accel = 9f;
            /// <summary>The pilot's body as a circle on the floor (m).</summary>
            public float radius = 0.26f;
            /// <summary>Eye height above the floor standing (m).</summary>
            public float eyeStand = 1.64f;
            public float lookYawRate = 140f, lookPitchRate = 95f, pitchMax = 75f;
            public float bobHeight = 0.013f, bobSide = 0.007f, strideMeters = 0.62f;
            public float stepSmooth = 0.09f;
            public float fovStanding = 62f;
            public string why;
        }

        public sealed class Timing
        {
            public float standHoldSeconds = 0.55f, riseSeconds = 1.7f, lowerSeconds = 1.5f;
            public float breakerSeconds = 1.5f, relightSeconds = 2.4f;
            public float repairSeconds = 5.0f, repairDecayPerSecond = 0.25f, repairSlipSeconds = 0.7f, repairNeedsLookSeconds = 0.25f;
            public float rackSwapSeconds = 1.1f, tapSeconds = 0.35f;
            /// <summary>A ship that has just gone dark is still warm: a scan within this many seconds of the breaker still finds it.</summary>
            public float coolSeconds = 5.0f;
            public string why;
        }

        public sealed class Exposure
        {
            /// <summary>EV100 of the sky's exposure in each state of the cabin (14 seated: the flight view, unchanged).</summary>
            public float seated = 14f, lit = 8.0f, emergency = 6.5f, alert = 6.5f, dark = 7.0f, blackout = 5.0f;
            public float openSeconds = 1.8f, closeSeconds = 0.8f;
            public string why;
        }

        public sealed class Lighting
        {
            /// <summary>The Sun through the canopy's tinted panes, in lux on the cabin (the Sun itself is 100,000: the glazing passes a share of it).</summary>
            public float canopySunUnits = 1.1f;
            /// <summary>How much of the sky's cold fill the lit scene keeps while the pilot is up (the cabin's walls would drown in it at the cabin's exposure).</summary>
            public float ambientScale = 0.04f;
            /// <summary>The share of that fill that is left in a full blackout (the sky's ambient is absolute light, and an eye opened to the dark would otherwise see it grow eightfold).</summary>
            public float ambientBlackout = 0.25f;
            public float instrumentScale = 1f, emergencyScale = 1f, workScale = 1f;
            /// <summary>Emissive nits of a display in each state.</summary>
            /// <summary>The light of a module knitting back in, on a mid-grey surface half a metre from the panel.</summary>
            public float knitUnits = 1.2f;
            public float displayUnits = 1.6f, displayUnitsDim = 0.25f, glyphUnits = 1.3f, lampUnits = 3.0f, emergencyLampUnits = 2.2f;
            public float flickerSeconds = 0.45f;
            public string why;
        }

        public sealed class Scan
        {
            /// <summary>Where in a sweep the beam crosses the ship (0..1), how wide the swing is (degrees) and how long the pass lasts (seconds), and the light it throws (lux in the cabin).</summary>
            public float contactAt = 0.5f, swingDegrees = 75f, passSeconds = 3.4f, units = 1.7f;
            /// <summary>The hull groans as a sweep nears: seconds before the contact the first creak, and how many.</summary>
            public float creakLeadSeconds = 4f;
            public string why;
        }

        public sealed class Controls
        {
            public PadMap pad = new PadMap();
            public KeyMap keyboard = new KeyMap();
            public string why;
        }

        public sealed class PadMap
        {
            public string move = "leftStick", look = "rightStick", context = "buttonSouth", back = "buttonEast", standSit = "select";
            public float deadZone = 0.18f, lookExponent = 1.6f, moveExponent = 1.2f, mapDoubleTapSeconds = 0.4f;
        }

        public sealed class KeyMap
        {
            public string forward = "w", backward = "s", left = "a", right = "d", lookLeft = "leftArrow", lookRight = "rightArrow", lookUp = "upArrow", lookDown = "downArrow",
                context = "e", back = "q", standSit = "z", map = "b", launch = "enter";
        }

        public static CabinConfig Load(out string source)
        {
            string path = CommandLine.Get("-mnCabin");
            if (!string.IsNullOrEmpty(path))
            {
                if (File.Exists(path))
                {
                    source = Path.GetFullPath(path);
                    return Parse(File.ReadAllText(path), source);
                }
                MnLog.Warn($"cabin config '{path}' not found; using the built-in numbers");
            }
            var asset = Resources.Load<TextAsset>(ResourcePath) ?? throw new FileNotFoundException($"Resources/{ResourcePath}.json is missing");
            source = $"Resources/{ResourcePath}.json";
            return Parse(asset.text, source);
        }

        public static CabinConfig Parse(string json, string where)
        {
            CabinConfig c;
            try { c = JsonConvert.DeserializeObject<CabinConfig>(json, new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error }); }
            catch (Exception e) { throw new InvalidDataException($"{where}: {e.Message}"); }
            if (c == null || c.format != Format) throw new InvalidDataException($"{where}: format is '{c?.format}', expected '{Format}'");
            return c;
        }

        /// <summary>The seated eye in the ship's frame.</summary>
        public Vector3 ShipEye => new Vector3(body.shipEye[0], body.shipEye[1], body.shipEye[2]);
    }
}
