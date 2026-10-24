using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>Resources/Combat/range.json (format "monolith-combat-range/1"): where everything in the combat range starts.</summary>
    public sealed class RangeData
    {
        public const string Format = "monolith-combat-range/1";
        public const string ResourcePath = "Combat/range";

        public string format, about;
        public PlayerSpec player = new PlayerSpec();
        public float respawnSeconds = 7f, capitalRespawnSeconds = 30f;
        public CapitalSpec capital;
        public List<DroneSpec> drones = new List<DroneSpec>();
        public List<EmplacementSpec> emplacements = new List<EmplacementSpec>();

        public sealed class PlayerSpec
        {
            public string ship = "vanguard_fighter";
            public float[] pos = { 0f, 0f, 0f }, rot = { 0f, 0f, 0f };
            public float speedFraction = 0.4f, throttle = 0.4f;
        }

        public sealed class CapitalSpec
        {
            public string ship = "earth_corvette", callsign = "Warden";
            public int team = 1;
            public float[] pos, rot;
            public float hullScale = 1f;
            public string hullScaleWhy;
        }

        public sealed class DroneSpec
        {
            public string @class, callsign;
            public float[] pos, rot;
            public float speedFraction, throttle, loop;
            public List<PlaytestData.InputKey> keys = new List<PlaytestData.InputKey>();
        }

        public sealed class EmplacementSpec
        {
            public string def, callsign;
            public float[] pos;
            public float range = 1400f, scatter = 5f, missileEvery, waitScale = 1.5f;
        }

        public static RangeData Load()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath) ?? throw new FileNotFoundException($"Resources/{ResourcePath}.json is missing");
            RangeData d;
            try { d = JsonConvert.DeserializeObject<RangeData>(asset.text, new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error }); }
            catch (Exception e) { throw new InvalidDataException($"Resources/{ResourcePath}.json: {e.Message}"); }
            if (d == null || d.format != Format) throw new InvalidDataException($"Resources/{ResourcePath}.json: format '{d?.format}', expected '{Format}'");
            return d;
        }
    }
}
