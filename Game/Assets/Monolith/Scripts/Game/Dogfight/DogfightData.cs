using System.Collections.Generic;
using System.IO;
using Monolith.AI;
using Newtonsoft.Json;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>Resources/Dogfight/dogfight.json (format "monolith-dogfight/1"): the dogfight loop's layout, waves and rules.</summary>
    public sealed class DogfightData
    {
        public const string Format = "monolith-dogfight/1";
        public const string ResourcePath = "Dogfight/dogfight";

        public string format, about, difficulty;
        public Pilot player = new Pilot();
        public List<Pilot> wingmen = new List<Pilot>();
        public Capital friendly = new Capital(), corvette = new Capital();
        public Sourced waveCap = new Sourced(), nextWaveAt = new Sourced(), waveDelay = new Sourced(), firstWaveAt = new Sourced();
        public List<float[]> arrivals = new List<float[]>();
        public string arrivalsWhy;
        public List<Wave> waves = new List<Wave>();
        public BomberWing bombers = new BomberWing();
        public LastGuard lastGuard;

        public sealed class Pilot
        {
            public string ship = "vanguard_fighter", callsign, profile;
            public float[] pos, rot;
            public float speedFraction = 0.5f, throttle = 0.6f;
            /// <summary>Missile loads that differ from the ship's own (weapon id to rounds): wingmen fly lighter racks than the player's.</summary>
            public Dictionary<string, int> ammo;
            public string ammoWhy;
        }

        public sealed class HullShape { public float length = 400f, width = 100f, height = 80f; public int seed = 1; }

        public sealed class Orders
        {
            public float standoff = 2600f, band = 400f, bearing = 70f, throttle = 0.3f, turretWaitScale = 1f, beamWaitScale = 1f;
            public string turretProfile = "earth_turret";
            /// <summary>Beams held at the start (FS2's beam-lock); the dogfight frees them when the corvette's beam turrets are gone.</summary>
            public bool holdBeams;
            public string holdBeamsWhy;
            public Dictionary<string, string> weapons = new Dictionary<string, string>();

            public CapitalOrders ToOrders() => new CapitalOrders
            {
                Standoff = standoff, Band = band, Bearing = bearing, Throttle = throttle, TurretProfile = turretProfile,
                TurretWaitScale = turretWaitScale, BeamWaitScale = beamWaitScale, HoldBeams = holdBeams, Weapons = new Dictionary<string, string>(weapons ?? new Dictionary<string, string>()),
            };
        }

        public sealed class Capital
        {
            public string ship, callsign, hullScaleWhy, weaponsWhy;
            public float[] pos, rot;
            public float hullScale = 1f;
            public HullShape hull;
            public Orders orders = new Orders();
        }

        /// <summary>A number with its source, or one per difficulty level.</summary>
        public sealed class Sourced
        {
            public object value;
            public string source, fs2;
            public float At(int level)
            {
                if (value is Newtonsoft.Json.Linq.JArray a) return a[Mathf.Clamp(level, 0, a.Count - 1)].ToObject<float>();
                return System.Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        public sealed class Group
        {
            public string @class, profile, goal, name;
            public int count = 1, torpedoes = -1;
        }

        public sealed class Wave
        {
            public string name;
            public List<Group> ships = new List<Group>();
            public int Count { get { int n = 0; foreach (var g in ships) n += g.count; return n; } }
        }

        public sealed class BomberWing
        {
            public string name = "Censer", torpedoesWhy;
            public int withWave = 2;
            public float delay = 20f;
            public float[] pos;
            public Group bomber = new Group(), escort = new Group();
        }

        /// <summary>
        /// Earth's last guard (FS2's wing waves): once every wave is in, the bombers sent, no Earth fighter left and the Warden still alive,
        /// a <see cref="count"/>-ship wave jumps in <see cref="distance"/> m off the player <see cref="delay"/> s later and goes for it; each next wave
        /// <see cref="waveDelay"/> s after the last one is destroyed, up to <see cref="waves"/>. Mirrors the mission file's wing of that name.
        /// </summary>
        public sealed class LastGuard
        {
            public string name = "Compline", @class = "earth_interceptor", profile = "drone", why;
            public int count = 2, waves = 20;
            public float delay = 10f, waveDelay = 15f, distance = 1800f;
        }

        public static DogfightData Load()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath) ?? throw new FileNotFoundException($"Resources/{ResourcePath}.json is missing");
            DogfightData d;
            try { d = JsonConvert.DeserializeObject<DogfightData>(asset.text, new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error }); }
            catch (System.Exception e) { throw new InvalidDataException($"Resources/{ResourcePath}.json: {e.Message}"); }
            if (d == null || d.format != Format) throw new InvalidDataException($"Resources/{ResourcePath}.json: format must be {Format}");
            if (d.waves.Count == 0 || d.arrivals.Count == 0) throw new InvalidDataException($"Resources/{ResourcePath}.json: needs waves and arrivals");
            return d;
        }

        public static Vector3 V3(float[] a) => a != null && a.Length == 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
    }
}
