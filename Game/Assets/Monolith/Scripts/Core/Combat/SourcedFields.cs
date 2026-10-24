using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Core
{
    /// <summary>
    /// Reads a block of <c>{ "name": { "value": ..., "unit": "...", "fs2": "$Field", "source": "..." } }</c> entries (the combat data files).
    /// Every entry must say where its number came from: <c>fs2</c> names the FS2 table field it was copied from (tools/combat/check_weapons.py
    /// reads FS2's table and compares), or <c>source</c> says in words why it is ours. Misspelt or unused entries are errors.
    /// </summary>
    public sealed class SourcedFields
    {
        readonly JObject _block;
        readonly string _where;
        readonly HashSet<string> _read = new HashSet<string>();

        public SourcedFields(JObject block, string where)
        {
            _block = block ?? throw new InvalidDataException($"{where}: block is missing");
            _where = where;
        }

        public string Where => _where;
        public bool Has(string key) => _block[key] is JObject;

        JToken Entry(string key)
        {
            _read.Add(key);
            var entry = _block[key] as JObject ?? throw new InvalidDataException($"{_where}: '{key}' is missing (each entry is {{ value, unit, fs2 or source }})");
            if (string.IsNullOrWhiteSpace((string)entry["fs2"]) && string.IsNullOrWhiteSpace((string)entry["source"]))
                throw new InvalidDataException($"{_where}: '{key}' has neither an 'fs2' field name nor a 'source' note");
            return entry["value"] ?? throw new InvalidDataException($"{_where}: '{key}' has no value");
        }

        public float Float(string key, float min, float max)
        {
            var t = Entry(key);
            if (t.Type != JTokenType.Float && t.Type != JTokenType.Integer) throw new InvalidDataException($"{_where}: '{key}' must be a number");
            float v = t.Value<float>();
            if (float.IsNaN(v) || v < min || v > max) throw new InvalidDataException($"{_where}: '{key}' = {v} is outside {min}..{max}");
            return v;
        }

        /// <summary>A number that may be left out (then <paramref name="fallback"/>).</summary>
        public float Float(string key, float min, float max, float fallback) => Has(key) ? Float(key, min, max) : fallback;

        public int Int(string key, int min, int max, int fallback)
        {
            if (!Has(key)) return fallback;
            var t = Entry(key);
            if (t.Type != JTokenType.Integer) throw new InvalidDataException($"{_where}: '{key}' must be a whole number");
            int v = t.Value<int>();
            if (v < min || v > max) throw new InvalidDataException($"{_where}: '{key}' = {v} is outside {min}..{max}");
            return v;
        }

        public bool Bool(string key, bool fallback)
        {
            if (!Has(key)) return fallback;
            var t = Entry(key);
            if (t.Type != JTokenType.Boolean) throw new InvalidDataException($"{_where}: '{key}' must be true or false");
            return t.Value<bool>();
        }

        public string Str(string key, string fallback = null)
        {
            if (!Has(key)) return fallback;
            return (string)Entry(key);
        }

        public Vector3 Vec3(string key, float min, float max)
        {
            if (!(Entry(key) is JArray a) || a.Count != 3) throw new InvalidDataException($"{_where}: '{key}' must be [x, y, z]");
            var v = new Vector3(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>());
            for (int i = 0; i < 3; i++)
                if (float.IsNaN(v[i]) || v[i] < min || v[i] > max) throw new InvalidDataException($"{_where}: '{key}'[{i}] = {v[i]} is outside {min}..{max}");
            return v;
        }

        public void RejectUnknown()
        {
            foreach (var p in _block.Properties())
                if (!_read.Contains(p.Name)) throw new InvalidDataException($"{_where}: unknown entry '{p.Name}'");
        }

        // ---------------------------------------------------------------- plain helpers for the non-sourced parts of a file

        public static Vector3 V3(JToken t, string where)
        {
            if (!(t is JArray a) || a.Count != 3) throw new InvalidDataException($"{where}: expected [x, y, z]");
            return new Vector3(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>());
        }

        public static JObject Open(string json, string format, string where)
        {
            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception e) { throw new InvalidDataException($"{where}: not valid JSON ({e.Message})"); }
            string got = (string)root["format"];
            if (got != format) throw new InvalidDataException($"{where}: format is '{got}', expected '{format}'");
            return root;
        }
    }
}
