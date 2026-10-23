using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Monolith.Capture
{
    /// <summary>
    /// A capture plan (gauntlet\shots\*.json): stills at fixed camera poses and optional real-time performance runs.
    /// Field names are case-insensitive; an unknown field aborts the run so a typo cannot silently do nothing.
    /// <code>
    /// { "defaults": { "width": 1920, "height": 1080, "warmup": 1.0 },
    ///   "stills": [ { "name": "front", "pos": [0, 1, -9], "rot": [6, 0, 0], "fov": 40 } ],
    ///   "perf":   [ { "name": "smoke", "seconds": 10 } ] }
    /// </code>
    /// </summary>
    public sealed class CapturePlan
    {
        /// <summary>Which test scene the game should build for this plan (null: the game's default). The game, not the capture code, knows the names.</summary>
        public string scene;
        /// <summary>Free-form settings for that scene (a JSON object), read by the game when it builds the scene. The capture code never looks inside.</summary>
        public Newtonsoft.Json.Linq.JObject sceneOptions;
        public PlanDefaults defaults = new PlanDefaults();
        public List<StillShot> stills = new List<StillShot>();
        public List<PerfRun> perf = new List<PerfRun>();

        /// <summary>
        /// The plan's "scene" and "sceneOptions" (as JSON text) without validating the rest: the game builds its world before the
        /// runner loads the plan. Both are null if absent or if the file is unreadable (the runner reports bad plans itself).
        /// </summary>
        public static void PeekScene(string path, out string scene, out string optionsJson)
        {
            scene = optionsJson = null;
            try
            {
                var root = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));
                scene = root["scene"]?.ToString();
                optionsJson = root["sceneOptions"]?.ToString();
            }
            catch (Exception) { }
        }

        public static CapturePlan Load(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException($"capture plan not found: {path}");
            var settings = new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error };
            var plan = JsonConvert.DeserializeObject<CapturePlan>(File.ReadAllText(path), settings)
                       ?? throw new InvalidDataException("capture plan is empty");
            plan.Validate();
            return plan;
        }

        void Validate()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in stills) Check(s, names, "still");
            foreach (var p in perf)
            {
                Check(p, names, "perf run");
                if (p.seconds <= 0f) throw new InvalidDataException($"perf run '{p.name}': seconds must be above 0");
            }
            if (defaults.width <= 0 || defaults.height <= 0) throw new InvalidDataException("defaults: width and height must be above 0");
        }

        static void Check(ShotBase s, HashSet<string> names, string kind)
        {
            if (string.IsNullOrWhiteSpace(s.name) || !Regex.IsMatch(s.name, @"^[A-Za-z0-9_\-\.]+$"))
                throw new InvalidDataException($"{kind} name '{s.name}' must be letters, digits, _ - . only (it becomes a file name)");
            if (!names.Add(s.name)) throw new InvalidDataException($"duplicate name '{s.name}' (stills and perf runs share one namespace)");
            Vec(s.pos, "pos", s.name); Vec(s.rot, "rot", s.name); Vec(s.lookAt, "lookAt", s.name);
        }

        static void Vec(float[] v, string field, string shot)
        {
            if (v != null && v.Length != 3) throw new InvalidDataException($"'{shot}': {field} needs exactly 3 numbers");
        }
    }

    public sealed class PlanDefaults
    {
        public int width = 1920;
        public int height = 1080;
        /// <summary>Seconds of game time to run before a still is taken (lets the sim, exposure and AA settle).</summary>
        public float warmup = 1f;
        /// <summary>Seconds of real time to run before a perf run starts measuring.</summary>
        public float perfWarmup = 2f;
        public int seed = 1234;
    }

    /// <summary>Fields shared by stills and perf runs. A missing camera field keeps the scene camera's own value.</summary>
    public abstract class ShotBase
    {
        public string name;
        /// <summary>Camera position in world space (metres).</summary>
        public float[] pos;
        /// <summary>Camera rotation as Euler degrees (pitch, yaw, roll). Ignored when lookAt is given.</summary>
        public float[] rot;
        /// <summary>Point to look at (world space); overrides rot.</summary>
        public float[] lookAt;
        /// <summary>Vertical field of view in degrees.</summary>
        public float? fov;
        public float? warmup;
        public int? seed;
    }

    public sealed class StillShot : ShotBase
    {
        public int? width;
        public int? height;
    }

    public sealed class PerfRun : ShotBase
    {
        /// <summary>Seconds of real time to measure.</summary>
        public float seconds = 10f;
        /// <summary>Window size for the run (default: the plan defaults, normally 1920x1080).</summary>
        public int? width;
        public int? height;
    }
}
