using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Flight
{
    /// <summary>
    /// One ship class's flight numbers (Resources/Ships/&lt;id&gt;.json, format "monolith-ship-class/1"). Every value in
    /// the file is <c>{ "value": ..., "unit": "...", "source": "..." }</c> so where a number came from travels with it.
    /// Times are exponential time constants in seconds unless named otherwise.
    /// </summary>
    public sealed class ShipClass
    {
        public const string Format = "monolith-ship-class/1";

        public string Id, Name, Role;
        public float TopSpeed;
        public float ReverseSpeed;
        /// <summary>Seconds for a full 360 degree turn about each axis: pitch, yaw, roll.</summary>
        public Vector3 RotationTime;
        public float RotDamp;
        public float SideDamp;
        public float ForwardAccel, ForwardDecel;
        /// <summary>Seconds (time constant) of the retro-thruster brake (the pilot clicks R3): much quicker than <see cref="ForwardDecel"/>. 0: the class has no retro thrusters and the brake slows it on its ordinary slow-down.</summary>
        public float BrakeDecel;
        public bool HasAfterburner;
        public float AfterburnerSpeed, AfterburnerAccel, AfterburnerFuel, AfterburnerBurnRate, AfterburnerRechargeRate;
        public float Mass;
        /// <summary>Width, height, length in metres.</summary>
        public Vector3 Size;

        /// <summary>Maximum turn rate per axis in rad/s (2 pi / rotation time): pitch, yaw, roll.</summary>
        public Vector3 MaxTurnRate => new Vector3(
            2f * Mathf.PI / RotationTime.x, 2f * Mathf.PI / RotationTime.y, 2f * Mathf.PI / RotationTime.z);

        public float Radius => 0.5f * Size.magnitude;

        public static ShipClass Parse(string json, string where)
        {
            var root = DataFile.Open(json, Format, where);
            var f = new DataFile.Fields(root, "flight", where);
            var c = new ShipClass
            {
                Id = DataFile.Str(root, "id", where),
                Name = DataFile.Str(root, "name", where),
                Role = (string)root["role"] ?? "",
                TopSpeed = f.Float("topSpeed", 0f, 10000f),
                ReverseSpeed = f.Float("reverseSpeed", 0f, 10000f),
                RotationTime = f.Vec3("rotationTime", 0.01f, 10000f),
                RotDamp = f.Float("rotDamp", 0f, 100f),
                SideDamp = f.Float("sideDamp", 0f, 100f),
                ForwardAccel = f.Float("forwardAccel", 0f, 1000f),
                ForwardDecel = f.Float("forwardDecel", 0f, 1000f),
                BrakeDecel = f.FloatOr("brakeDecel", 0f, 0f, 1000f),
                HasAfterburner = f.Bool("afterburner"),
                AfterburnerSpeed = f.Float("afterburnerSpeed", 0f, 10000f),
                AfterburnerAccel = f.Float("afterburnerAccel", 0f, 1000f),
                AfterburnerFuel = f.Float("afterburnerFuel", 0f, 100000f),
                AfterburnerBurnRate = f.Float("afterburnerBurnRate", 0f, 100000f),
                AfterburnerRechargeRate = f.Float("afterburnerRechargeRate", 0f, 100000f),
                Mass = f.Float("mass", 1f, 1e12f),
                Size = f.Vec3("size", 0.1f, 100000f),
            };
            f.RejectUnknown();
            return c;
        }

        public override string ToString() =>
            $"{Name} ({Id}): top {TopSpeed} m/s, AB {AfterburnerSpeed} m/s, turn {RotationTime.x}/{RotationTime.y}/{RotationTime.z} s, " +
            $"rotdamp {RotDamp}, damp {SideDamp}, accel {ForwardAccel}/{ForwardDecel}";
    }

    /// <summary>Loads ship classes from Resources/Ships. Accepts an id ("gtf_ulysses") or a name ("GTF Ulysses", "GTF_Ulysses").</summary>
    public static class ShipCatalog
    {
        public const string Folder = "Ships";
        static readonly Dictionary<string, ShipClass> Cache = new Dictionary<string, ShipClass>();

        public static string ToId(string nameOrId) =>
            (nameOrId ?? "").Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');

        public static ShipClass Load(string nameOrId)
        {
            string id = ToId(nameOrId);
            if (Cache.TryGetValue(id, out var c)) return c;
            var asset = Resources.Load<TextAsset>(Folder + "/" + id);
            if (asset == null) throw new FileNotFoundException($"no ship class '{nameOrId}' (looked for Resources/{Folder}/{id}.json)");
            c = ShipClass.Parse(asset.text, $"Resources/{Folder}/{id}.json");
            if (ToId(c.Id) != id) throw new InvalidDataException($"Resources/{Folder}/{id}.json says its id is '{c.Id}'");
            Cache[id] = c;
            return c;
        }
    }

    /// <summary>Small helpers for the "value with a source" data files.</summary>
    public static class DataFile
    {
        public static JObject Open(string json, string format, string where)
        {
            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception e) { throw new InvalidDataException($"{where}: not valid JSON ({e.Message})"); }
            string got = (string)root["format"];
            if (got != format) throw new InvalidDataException($"{where}: format is '{got}', expected '{format}'");
            return root;
        }

        public static string Str(JObject o, string key, string where)
        {
            string s = (string)o[key];
            if (string.IsNullOrWhiteSpace(s)) throw new InvalidDataException($"{where}: '{key}' is missing");
            return s;
        }

        /// <summary>A block of { name: { value, unit, source } } entries. Every entry must say where its value came from.</summary>
        public sealed class Fields
        {
            readonly JObject _block;
            readonly string _where;
            readonly HashSet<string> _read = new HashSet<string>();

            public Fields(JObject root, string blockName, string where)
            {
                _block = root[blockName] as JObject ?? throw new InvalidDataException($"{where}: no '{blockName}' block");
                _where = where + " " + blockName;
            }

            JToken Value(string key)
            {
                _read.Add(key);
                var entry = _block[key] as JObject ?? throw new InvalidDataException($"{_where}: '{key}' is missing (each entry is {{ value, unit, source }})");
                if (string.IsNullOrWhiteSpace((string)entry["source"])) throw new InvalidDataException($"{_where}: '{key}' has no source note");
                return entry["value"] ?? throw new InvalidDataException($"{_where}: '{key}' has no value");
            }

            public float Float(string key, float min, float max)
            {
                var t = Value(key);
                if (t.Type != JTokenType.Float && t.Type != JTokenType.Integer) throw new InvalidDataException($"{_where}: '{key}' must be a number");
                float v = t.Value<float>();
                if (float.IsNaN(v) || v < min || v > max) throw new InvalidDataException($"{_where}: '{key}' = {v} is outside {min}..{max}");
                return v;
            }

            /// <summary>An entry that may be left out (then <paramref name="fallback"/>); when present it is read, checked and needs its source note like any other.</summary>
            public float FloatOr(string key, float fallback, float min, float max)
            {
                if (_block[key] == null) { _read.Add(key); return fallback; }
                return Float(key, min, max);
            }

            public bool Bool(string key)
            {
                var t = Value(key);
                if (t.Type != JTokenType.Boolean) throw new InvalidDataException($"{_where}: '{key}' must be true or false");
                return t.Value<bool>();
            }

            public Vector3 Vec3(string key, float min, float max)
            {
                if (!(Value(key) is JArray a) || a.Count != 3) throw new InvalidDataException($"{_where}: '{key}' must be [x, y, z]");
                var v = new Vector3(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>());
                for (int i = 0; i < 3; i++)
                    if (float.IsNaN(v[i]) || v[i] < min || v[i] > max) throw new InvalidDataException($"{_where}: '{key}'[{i}] = {v[i]} is outside {min}..{max}");
                return v;
            }

            /// <summary>Call after reading: a misspelt entry is an error, not silently ignored.</summary>
            public void RejectUnknown()
            {
                foreach (var p in _block.Properties())
                    if (!_read.Contains(p.Name)) throw new InvalidDataException($"{_where}: unknown entry '{p.Name}'");
            }
        }
    }
}
