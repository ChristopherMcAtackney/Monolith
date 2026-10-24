using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.ShipArt
{
    /// <summary>
    /// What a model script wrote beside the FBX (Resources/ShipArt/&lt;id&gt;.ship.json): size, triangle counts per LOD, the
    /// subsystems with pivot, facing and radius, and the running lights. All positions are in the ship's own space, Unity's axes
    /// (x right, y up, z forward). Read by ShipArtSetup to build the prefab and by anything that wants the numbers without
    /// loading the model.
    /// </summary>
    public sealed class ShipManifest
    {
        public string Id, Name;
        public Vector3 BoundsMin, BoundsMax, Size;
        public float Length;
        public int[] LodTriangles = new int[0];
        public int SubsystemTriangles, CollisionTriangles;
        public List<SubsystemInfo> Subsystems = new List<SubsystemInfo>();
        public List<LightInfo> Lights = new List<LightInfo>();

        public sealed class SubsystemInfo
        {
            public string Name, Kind;
            public float Radius, Hp;
            public Vector3 Position, Forward, Up;
            public string[] Tags = new string[0];
        }

        public sealed class LightInfo
        {
            public Vector3 Position;
            public string Kind;
            public float[] Colour;
            public float Size;
            public string Blink;
        }

        public static ShipManifest Parse(string json)
        {
            var root = JObject.Parse(json);
            var m = new ShipManifest
            {
                Id = (string)root["id"],
                Name = (string)root["name"],
                BoundsMin = V3(root["bounds"]["min"]),
                BoundsMax = V3(root["bounds"]["max"]),
                Size = V3(root["bounds"]["size"]),
                Length = (float)root["bounds"]["length"],
                SubsystemTriangles = (int?)root["lods"]["0"]["subsystemTriangles"] ?? 0,
                CollisionTriangles = (int?)root["collisionTriangles"] ?? 0,
            };
            var tris = new List<int>();
            for (int i = 0; root["lods"][i.ToString()] != null; i++) tris.Add((int)root["lods"][i.ToString()]["triangles"]);
            m.LodTriangles = tris.ToArray();
            foreach (var s in (JArray)root["subsystems"])
                m.Subsystems.Add(new SubsystemInfo
                {
                    Name = (string)s["name"], Kind = (string)s["kind"], Radius = (float)s["radius"], Hp = (float)s["hp"],
                    Position = V3(s["position"]), Forward = V3(s["forward"]), Up = V3(s["up"]),
                    Tags = s["tags"] == null ? new string[0] : s["tags"].ToObject<string[]>(),
                });
            if (root["lights"] is JArray ls)
                foreach (var l in ls)
                    m.Lights.Add(new LightInfo
                    {
                        Position = V3(l["pos"]), Kind = (string)l["kind"], Colour = l["colour"].ToObject<float[]>(),
                        Size = (float)l["size"], Blink = (string)l["blink"],
                    });
            return m;
        }

        static Vector3 V3(JToken t) => new Vector3((float)t[0], (float)t[1], (float)t[2]);
    }
}
