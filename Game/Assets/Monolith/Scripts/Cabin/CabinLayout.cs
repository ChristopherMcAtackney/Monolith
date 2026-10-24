using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Cabin
{
    /// <summary>
    /// The cabin's plan as the model script wrote it (Resources/Cabin/cabin.layout.json, format "monolith-cabin/1"), read without the model or Blender:
    /// where the seated eye is, the walkable floor and its steps, what the pilot cannot walk through, every interactable (where to stand, where to look),
    /// every display and every light. Cabin space, Unity axes: x right, y up, z forward, metres.
    /// </summary>
    public sealed class CabinLayout
    {
        public const string Format = "monolith-cabin/1";
        public const string ResourcePath = "Cabin/cabin.layout";

        public struct Level { public float X0, X1, Z0, Z1, Y; }
        public struct Circle { public float X, Z, R; }

        public sealed class Interactable
        {
            public string Id, Kind, Bay, BayKind, Grip;
            public Vector3 Pos;
            public Vector2 Stand;
            public float FaceYaw, Radius;
            public bool Crouch;
        }

        public sealed class DisplayDef { public string Name; public int W, H; public float Aspect; }

        public sealed class LightDef
        {
            public string Name, Kind, Group;
            public Vector3 Pos, SpotDir;
            public Color Colour;
            /// <summary>How bright it makes a mid-grey surface a metre away, in screen units (1.0 is white at the eye's exposure).</summary>
            public float Glow, Range, SpotAngle;
            public bool Shadow;
        }

        public Vector3 Eye;
        public Vector3 StandAt;
        public float StandYaw, EyeStand, EyeCrouch;
        public Vector3 BoundsMin, BoundsMax;
        public readonly List<Level> Levels = new List<Level>();
        public readonly List<Circle> Obstacles = new List<Circle>();
        public readonly List<Interactable> Interactables = new List<Interactable>();
        public readonly List<DisplayDef> Displays = new List<DisplayDef>();
        public readonly List<LightDef> Lights = new List<LightDef>();
        public readonly Dictionary<string, (Vector3 pos, Vector3 forward, Vector3 up)> Nodes = new Dictionary<string, (Vector3, Vector3, Vector3)>();

        public static CabinLayout Load(string where = null)
        {
            string text;
            if (where != null && File.Exists(where)) text = File.ReadAllText(where);
            else text = (Resources.Load<TextAsset>(ResourcePath) ?? throw new FileNotFoundException($"Resources/{ResourcePath}.json is missing (run the cabin model script)")).text;
            return Parse(text, where ?? $"Resources/{ResourcePath}.json");
        }

        public static CabinLayout Parse(string json, string where)
        {
            var r = JObject.Parse(json);
            if ((string)r["format"] != Format) throw new InvalidDataException($"{where}: format is '{r["format"]}', expected '{Format}'");
            var l = new CabinLayout();
            l.Eye = V3(r["eye"]["pos"]);
            l.StandAt = V3(r["stand"]["pos"]);
            l.StandYaw = (float)r["stand"]["faceYawDeg"];
            l.EyeStand = (float)r["eyeHeights"]["stand"];
            l.EyeCrouch = (float)r["eyeHeights"]["crouch"];
            l.BoundsMin = V3(r["bounds"]["min"]); l.BoundsMax = V3(r["bounds"]["max"]);
            foreach (var j in (JArray)r["levels"]) l.Levels.Add(new Level { X0 = (float)j["x0"], X1 = (float)j["x1"], Z0 = (float)j["z0"], Z1 = (float)j["z1"], Y = (float)j["y"] });
            foreach (var j in (JArray)r["obstacles"]) l.Obstacles.Add(new Circle { X = (float)j["x"], Z = (float)j["z"], R = (float)j["r"] });
            foreach (var j in (JArray)r["interactables"])
                l.Interactables.Add(new Interactable
                {
                    Id = (string)j["id"], Kind = (string)j["kind"], Bay = (string)j["bay"], BayKind = (string)j["baykind"], Grip = (string)j["grip"],
                    Pos = V3(j["pos"]), Stand = new Vector2((float)j["stand"][0], (float)j["stand"][1]), FaceYaw = (float)j["faceYawDeg"], Radius = (float)j["radius"],
                    Crouch = (bool?)j["crouch"] ?? false,
                });
            foreach (var j in (JArray)r["displays"]) l.Displays.Add(new DisplayDef { Name = (string)j["name"], W = (int)j["w"], H = (int)j["h"], Aspect = (float)j["aspect"] });
            foreach (var j in (JArray)r["lights"])
            {
                var c = j["colour"];
                var d = new LightDef
                {
                    Name = (string)j["name"], Kind = (string)j["kind"], Group = (string)j["group"], Pos = V3(j["pos"]), Colour = new Color((float)c[0], (float)c[1], (float)c[2], 1f),
                    Glow = (float)j["glow"], Range = (float)j["range"], Shadow = (bool?)j["shadow"] ?? false,
                };
                if (j["spot"] != null) { d.SpotDir = V3(j["spot"]["dir"]); d.SpotAngle = (float)j["spot"]["angle"]; }
                l.Lights.Add(d);
            }
            foreach (JProperty p in ((JObject)r["nodes"]).Properties())
                if (p.Value["frame"] != null) l.Nodes[p.Name] = (V3(p.Value["frame"]["pos"]), V3(p.Value["frame"]["forward"]), V3(p.Value["frame"]["up"]));
            return l;
        }

        static Vector3 V3(JToken t) => new Vector3((float)t[0], (float)t[1], (float)t[2]);

        public Interactable Find(string id)
        {
            foreach (var i in Interactables) if (i.Id == id) return i;
            return null;
        }

        /// <summary>The floor height at a point (the highest level that holds it), or NaN off the floor.</summary>
        public float FloorAt(float x, float z)
        {
            float best = float.NaN;
            foreach (var l in Levels)
                if (x >= l.X0 && x <= l.X1 && z >= l.Z0 && z <= l.Z1 && (float.IsNaN(best) || l.Y > best)) best = l.Y;
            return best;
        }

        /// <summary>Can a body of this radius stand here: on the floor, with its whole circle, and clear of every obstacle.</summary>
        public bool Walkable(float x, float z, float radius)
        {
            // The circle's four extremes must all be on the floor (the floor is a union of rectangles), plus the centre.
            if (float.IsNaN(FloorAt(x, z))) return false;
            if (float.IsNaN(FloorAt(x + radius, z)) || float.IsNaN(FloorAt(x - radius, z)) || float.IsNaN(FloorAt(x, z + radius)) || float.IsNaN(FloorAt(x, z - radius))) return false;
            foreach (var o in Obstacles)
            {
                float dx = x - o.X, dz = z - o.Z, rr = o.R + radius;
                if (dx * dx + dz * dz < rr * rr) return false;
            }
            return true;
        }
    }
}
