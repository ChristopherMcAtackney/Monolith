using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.AI
{
    /// <summary>
    /// One pilot class (FS2's AI classes in ai.tbl): the four skill knobs per difficulty level. Accuracy is 0..1, the other three 0..100 as in
    /// FS2's table. <see cref="Fearless"/> marks a machine: courage has nothing to act on, so it never peels off when hurt (design section 7:
    /// Earth's drones, "no fear, no breaking off when hurt").
    /// </summary>
    public sealed class SkillProfile
    {
        public string Id, About;
        public float[] Accuracy, Evasion, Courage, Patience;
        public bool Fearless;
        /// <summary>
        /// Flies at its ship's full turn rate and fires at its guns' full rate whatever the difficulty (the ascended aces); every other pilot on
        /// Earth's side turns and fires slower on the easier levels, as FS2's do.
        /// </summary>
        public bool Unhandicapped;
        /// <summary>The FS2 AI class it stands for (ai.tbl's index: 0 Coward .. 6 General); FS2 adds more to a lower class's missile reload.</summary>
        public int AiClass;
    }

    /// <summary>What changes with the difficulty level (FS2's five skill levels and the tables its code keeps per level).</summary>
    public sealed class DifficultyTables
    {
        public string[] Levels;
        public int Default = 2;
        public float[] TurnTimeScale, AimingError, InRangeTime, RetargetSeconds, CmeasureChance, HostileFireDelay, FriendlyFireDelay;
        public float[] PredictDelay, PlayerDamageScale, PlayerPartDamageScale, WingDamageScale;
        public int[] MaxPlayerAttackers, MaxPlayerHomers;
        /// <summary>FS2's chance an Earth pilot may fire homing missiles at the player: of seven ten-second windows, this many plus one allow it.</summary>
        public int[] MissileChanceOnPlayer;
        /// <summary>Capital-ship turrets: how many of one ship's turrets may have the player (or any other one target) as their target at once.</summary>
        public int[] TurretsOnPlayer, TurretsOnTarget;
        /// <summary>Capital-ship turrets: the longest a gunner's picture of its target goes unrefreshed, and how long a target must stay in a turret's arc before its aim is at its best.</summary>
        public float[] TurretAimUpdate, TurretSettleSeconds;
        public int Count => Levels.Length;
    }

    /// <summary>The AI's own tuning constants (each with its source in the data file).</summary>
    public sealed class AiTuning
    {
        public float AimErrorMetres, TurretAimError, FlakSpreadMetres, FlakLeadSeconds;
        public float PeelHullNoCourage, PeelHullFullCourage, PeelMaxSeconds;
        public float PatienceHoldSeconds, PatienceConeBlend;
        public float TailRange, TailNoseDegrees, TailAspectDegrees;
        public float AvoidMargin, AvoidLookAheadMin, AvoidLookAheadMax;
        public float GunRangeFraction, SensorRange, FlakEnvelope;
        /// <summary>The afterburner in a chase (FS2's ai_chase): lit for a target ahead (dot over <see cref="AbChaseCone"/>) beyond
        /// <see cref="AbChaseMin"/> m that opens the range faster than <see cref="AbChaseOpening"/> m/s or burns itself, or beyond
        /// <see cref="AbChaseFar"/>; out under <see cref="AbChaseStop"/> m or when closing faster than <see cref="AbOvershootClosing"/> nearby.
        /// <see cref="AbEscapeChance"/> x evasion: an evasion with the attacker on the tail is a burn away. <see cref="AbReserve"/>: the
        /// share of the tank a pilot keeps before it lights the burner for a chase.</summary>
        public float AbChaseCone, AbChaseMin, AbChaseFar, AbChaseOpening, AbChaseStop, AbOvershootClosing, AbEscapeChance, AbReserve;
        /// <summary>FS2's AI classes in ai.tbl (for the secondary-reload autoscale by class).</summary>
        public int Fs2AiClasses;
        /// <summary>Capital-ship turrets: each turret's field of fire, degrees off the turret's up (FS2's turret fov), by kind.</summary>
        public float TurretArcGun, TurretArcFlak, TurretArcBeam;
        /// <summary>Capital-ship turrets: aim error (x) on a target that has only just come into the turret's arc; seconds between target choices.</summary>
        public float TurretFreshError, TurretRetargetSeconds;
    }

    /// <summary>
    /// A pilot's skill for one ship, resolved from its profile, the difficulty level and its side: everything the brain reads. The four knobs
    /// are normalised to 0..1 here (FS2's 0..100 / 100).
    /// </summary>
    public struct Skill
    {
        public string Key;
        public int Level;
        public float Accuracy, Evasion, Courage, Patience;
        public bool Fearless;
        /// <summary>The share of the ship's turn rate the pilot uses (1 / FS2's turn time scale for Earth, 1 for our side and the aces).</summary>
        public float TurnScale;
        public float AimingError, InRangeTime, RetargetSeconds, CmeasureChance, FireDelay;
        /// <summary>Seconds the pilot's picture of its target is out of date (FS2's Skill_level_delay; Earth's side only, never an unhandicapped ace).</summary>
        public float PredictDelay;
        /// <summary>Seconds FS2 adds to every missile reload for the pilot's AI class (lower classes wait longer).</summary>
        public float SecondaryClassDelay;
        public int MaxPlayerAttackers, MaxPlayerHomers, MissileChanceOnPlayer;
        /// <summary>Turret gunners: turrets of one ship on the player / on any other target at once; seconds between fresh looks at the target; seconds to settle the aim.</summary>
        public int TurretsOnPlayer, TurretsOnTarget;
        public float TurretAimUpdate, TurretSettle;
    }

    /// <summary>Resources/AI/ai.json (format "monolith-ai/1"): difficulty tables, the AI's tuning and the pilot profiles.</summary>
    public sealed class AiData
    {
        public const string Format = "monolith-ai/1";
        public const string ResourcePath = "AI/ai";

        public DifficultyTables Difficulty;
        public AiTuning Tuning;
        public readonly Dictionary<string, SkillProfile> Profiles = new Dictionary<string, SkillProfile>();

        static AiData _default;
        public static AiData Default
        {
            get
            {
                if (_default != null) return _default;
                var asset = Resources.Load<TextAsset>(ResourcePath) ?? throw new FileNotFoundException($"Resources/{ResourcePath}.json is missing");
                return _default = Parse(asset.text, $"Resources/{ResourcePath}.json");
            }
        }

        public SkillProfile Profile(string id) =>
            Profiles.TryGetValue(id ?? "", out var p) ? p : throw new KeyNotFoundException($"no AI profile '{id}' in Resources/{ResourcePath}.json");

        public int ClampLevel(int level) => Mathf.Clamp(level, 0, Difficulty.Count - 1);

        /// <summary>The level whose name or index is <paramref name="nameOrIndex"/> ("medium", "Medium", "2"), else the default.</summary>
        public int LevelOf(string nameOrIndex)
        {
            if (string.IsNullOrEmpty(nameOrIndex)) return Difficulty.Default;
            if (int.TryParse(nameOrIndex, out int i)) return ClampLevel(i);
            string want = nameOrIndex.Trim().Replace('_', ' ').ToLowerInvariant();
            for (int k = 0; k < Difficulty.Count; k++) if (Difficulty.Levels[k].ToLowerInvariant() == want) return k;
            return Difficulty.Default;
        }

        /// <summary>
        /// A pilot's skill: <paramref name="hostile"/> is true for Earth's side (FS2 slows enemy turns and gun rates on easy levels; our side's
        /// pilots get the friendly fire-rate table and their full turn rate).
        /// </summary>
        public Skill Resolve(string profileId, int level, bool hostile)
        {
            var p = Profile(profileId);
            var d = Difficulty;
            level = ClampLevel(level);
            return new Skill
            {
                Key = $"{p.Id}@{d.Levels[level].ToLowerInvariant().Replace(' ', '_')}",
                Level = level,
                Accuracy = Mathf.Clamp01(p.Accuracy[level]),
                Evasion = Mathf.Clamp01(p.Evasion[level] / 100f),
                Courage = Mathf.Clamp01(p.Courage[level] / 100f),
                Patience = Mathf.Clamp01(p.Patience[level] / 100f),
                Fearless = p.Fearless,
                TurnScale = hostile && !p.Unhandicapped ? 1f / Mathf.Max(1f, d.TurnTimeScale[level]) : 1f,
                AimingError = d.AimingError[level],
                InRangeTime = d.InRangeTime[level],
                RetargetSeconds = d.RetargetSeconds[level],
                CmeasureChance = d.CmeasureChance[level],
                FireDelay = p.Unhandicapped ? 1f : hostile ? d.HostileFireDelay[level] : d.FriendlyFireDelay[level],
                PredictDelay = hostile && !p.Unhandicapped ? d.PredictDelay[level] : 0f,
                SecondaryClassDelay = (Tuning.Fs2AiClasses - p.AiClass + 1) * 0.5f,
                MaxPlayerAttackers = d.MaxPlayerAttackers[level],
                MaxPlayerHomers = d.MaxPlayerHomers[level],
                MissileChanceOnPlayer = d.MissileChanceOnPlayer[level],
                TurretsOnPlayer = d.TurretsOnPlayer[level],
                TurretsOnTarget = d.TurretsOnTarget[level],
                // Our side's gunners are not handicapped by the level (FS2's handicaps are mercy on the player): they look and settle as the best.
                TurretAimUpdate = hostile && !p.Unhandicapped ? d.TurretAimUpdate[level] : Min(d.TurretAimUpdate),
                TurretSettle = hostile && !p.Unhandicapped ? d.TurretSettleSeconds[level] : Min(d.TurretSettleSeconds),
            };
        }

        static float Min(float[] a) { float m = float.MaxValue; foreach (var x in a) m = Mathf.Min(m, x); return m; }

        /// <summary>The same skill with one knob forced (the arena's knob sweeps).</summary>
        public static Skill WithKnob(Skill s, string knob, float value)
        {
            switch ((knob ?? "").ToLowerInvariant())
            {
                case "accuracy": s.Accuracy = Mathf.Clamp01(value); break;
                case "evasion": s.Evasion = Mathf.Clamp01(value); break;
                case "courage": s.Courage = Mathf.Clamp01(value); s.Fearless = false; break;
                case "patience": s.Patience = Mathf.Clamp01(value); break;
                default: throw new ArgumentException($"no knob '{knob}' (accuracy, evasion, courage, patience)");
            }
            s.Key += $"+{knob}={value:0.##}";
            return s;
        }

        // ------------------------------------------------------------------------------------------------ parsing

        public static AiData Parse(string json, string where)
        {
            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception e) { throw new InvalidDataException($"{where}: not valid JSON ({e.Message})"); }
            if ((string)root["format"] != Format) throw new InvalidDataException($"{where}: format is '{(string)root["format"]}', expected '{Format}'");

            var data = new AiData();
            var dj = root["difficulty"] as JObject ?? throw new InvalidDataException($"{where}: no 'difficulty' block");
            var levels = (dj["levels"] as JArray)?.ToObject<string[]>() ?? throw new InvalidDataException($"{where}: difficulty.levels is missing");
            int n = levels.Length;
            var t = new Table(dj["tables"] as JObject, where + " difficulty.tables", n);
            data.Difficulty = new DifficultyTables
            {
                Levels = levels,
                Default = Mathf.Clamp((int?)dj["default"] ?? n / 2, 0, n - 1),
                TurnTimeScale = t.Floats("turnTimeScale", 0.1f, 10f),
                AimingError = t.Floats("aimingError", 0f, 20f),
                InRangeTime = t.Floats("inRangeTime", -5f, 10f),
                RetargetSeconds = t.Floats("retargetSeconds", 0.2f, 120f),
                CmeasureChance = t.Floats("cmeasureChance", 0f, 2f),
                HostileFireDelay = t.Floats("hostileFireDelay", 0.1f, 20f),
                FriendlyFireDelay = t.Floats("friendlyFireDelay", 0.1f, 20f),
                MaxPlayerAttackers = Array.ConvertAll(t.Floats("maxPlayerAttackers", 1f, 999f), f => Mathf.RoundToInt(f)),
                MaxPlayerHomers = Array.ConvertAll(t.Floats("maxPlayerHomers", 0f, 999f), f => Mathf.RoundToInt(f)),
                MissileChanceOnPlayer = Array.ConvertAll(t.Floats("missileChanceOnPlayer", 0f, 99f), f => Mathf.RoundToInt(f)),
                PredictDelay = t.Floats("predictDelay", 0f, 10f),
                PlayerDamageScale = t.Floats("playerDamageScale", 0f, 10f),
                PlayerPartDamageScale = t.Floats("playerPartDamageScale", 0f, 10f),
                WingDamageScale = t.Floats("wingDamageScale", 0f, 10f),
                TurretsOnPlayer = Array.ConvertAll(t.Floats("turretsOnPlayer", 1f, 999f), f => Mathf.RoundToInt(f)),
                TurretsOnTarget = Array.ConvertAll(t.Floats("turretsOnTarget", 1f, 999f), f => Mathf.RoundToInt(f)),
                TurretAimUpdate = t.Floats("turretAimUpdate", 0f, 10f),
                TurretSettleSeconds = t.Floats("turretSettleSeconds", 0f, 60f),
            };
            t.RejectUnknown();

            var tj = new Table(root["tuning"] as JObject, where + " tuning", 1);
            data.Tuning = new AiTuning
            {
                AimErrorMetres = tj.Float("aimErrorMetres", 0f, 1000f),
                TurretAimError = tj.Float("turretAimError", 0f, 1f),
                FlakSpreadMetres = tj.Float("flakSpreadMetres", 0f, 1000f),
                FlakLeadSeconds = tj.Float("flakLeadSeconds", 0f, 5f),
                PeelHullNoCourage = tj.Float("peelHullNoCourage", 0f, 1f),
                PeelHullFullCourage = tj.Float("peelHullFullCourage", 0f, 1f),
                PeelMaxSeconds = tj.Float("peelMaxSeconds", 1f, 600f),
                PatienceHoldSeconds = tj.Float("patienceHoldSeconds", 0f, 10f),
                PatienceConeBlend = tj.Float("patienceConeBlend", 0f, 1f),
                TailRange = tj.Float("tailRange", 10f, 10000f),
                TailNoseDegrees = tj.Float("tailNoseDegrees", 1f, 180f),
                TailAspectDegrees = tj.Float("tailAspectDegrees", 1f, 180f),
                AvoidMargin = tj.Float("avoidMargin", 0f, 1000f),
                AvoidLookAheadMin = tj.Float("avoidLookAheadMin", 0.1f, 30f),
                AvoidLookAheadMax = tj.Float("avoidLookAheadMax", 0.1f, 60f),
                GunRangeFraction = tj.Float("gunRangeFraction", 0.1f, 1.5f),
                SensorRange = tj.Float("sensorRange", 100f, 100000f),
                FlakEnvelope = tj.Float("flakEnvelope", 0f, 10000f),
                Fs2AiClasses = Mathf.RoundToInt(tj.Float("fs2AiClasses", 1f, 100f)),
                AbChaseCone = tj.Float("abChaseCone", -1f, 1f),
                AbChaseMin = tj.Float("abChaseMin", 0f, 10000f),
                AbChaseFar = tj.Float("abChaseFar", 0f, 10000f),
                AbChaseOpening = tj.Float("abChaseOpening", -100f, 1000f),
                AbChaseStop = tj.Float("abChaseStop", 0f, 10000f),
                AbOvershootClosing = tj.Float("abOvershootClosing", 0f, 1000f),
                AbEscapeChance = tj.Float("abEscapeChance", 0f, 1f),
                AbReserve = tj.Float("abReserve", 0f, 1f),
                TurretArcGun = tj.Float("turretArcGun", 1f, 180f),
                TurretArcFlak = tj.Float("turretArcFlak", 1f, 180f),
                TurretArcBeam = tj.Float("turretArcBeam", 1f, 180f),
                TurretFreshError = tj.Float("turretFreshError", 1f, 20f),
                TurretRetargetSeconds = tj.Float("turretRetargetSeconds", 0.1f, 30f),
            };
            tj.RejectUnknown();

            var pj = root["profiles"] as JObject ?? throw new InvalidDataException($"{where}: no 'profiles' block");
            foreach (var prop in pj.Properties())
            {
                var o = prop.Value as JObject ?? throw new InvalidDataException($"{where}: profile '{prop.Name}' is not an object");
                var pt = new Table(o, $"{where} profiles.{prop.Name}", n, "about", "fearless", "unhandicapped");
                var p = new SkillProfile
                {
                    Id = prop.Name,
                    About = (string)o["about"] ?? "",
                    Fearless = (bool?)o["fearless"] ?? false,
                    Unhandicapped = (bool?)o["unhandicapped"] ?? false,
                    Accuracy = pt.Floats("accuracy", 0f, 1f),
                    Evasion = pt.Floats("evasion", 0f, 100f),
                    Courage = pt.Floats("courage", 0f, 100f),
                    Patience = pt.Floats("patience", 0f, 100f),
                    AiClass = Mathf.RoundToInt(pt.Float("aiClass", 0f, data.Tuning.Fs2AiClasses - 1)),
                };
                pt.RejectUnknown();
                data.Profiles[p.Id] = p;
            }
            if (data.Profiles.Count == 0) throw new InvalidDataException($"{where}: no profiles");
            return data;
        }

        /// <summary>Entries of the form { "value": number or [one per level], "unit": "...", "fs2" or "source": "..." }.</summary>
        sealed class Table
        {
            readonly JObject _o;
            readonly string _where;
            readonly int _n;
            readonly HashSet<string> _read = new HashSet<string>();

            public Table(JObject o, string where, int n, params string[] plain)
            {
                _o = o ?? throw new InvalidDataException($"{where}: block is missing");
                _where = where; _n = n;
                foreach (var p in plain) _read.Add(p);
            }

            JToken Value(string key)
            {
                _read.Add(key);
                var e = _o[key] as JObject ?? throw new InvalidDataException($"{_where}: '{key}' is missing");
                if (string.IsNullOrWhiteSpace((string)e["fs2"]) && string.IsNullOrWhiteSpace((string)e["source"]))
                    throw new InvalidDataException($"{_where}: '{key}' needs an 'fs2' or a 'source' note");
                return e["value"] ?? throw new InvalidDataException($"{_where}: '{key}' has no value");
            }

            public float[] Floats(string key, float min, float max)
            {
                var v = Value(key);
                float[] r;
                if (v is JArray a) r = a.ToObject<float[]>();
                else r = new[] { v.Value<float>() };
                if (r.Length == 1 && _n > 1) { float x = r[0]; r = new float[_n]; for (int i = 0; i < _n; i++) r[i] = x; }
                if (r.Length != _n) throw new InvalidDataException($"{_where}: '{key}' has {r.Length} values, expected {_n} (one per difficulty level)");
                foreach (var x in r) if (float.IsNaN(x) || x < min || x > max) throw new InvalidDataException($"{_where}: '{key}' value {x} is outside {min}..{max}");
                return r;
            }

            public float Float(string key, float min, float max) => Floats(key, min, max)[0];

            public void RejectUnknown()
            {
                foreach (var p in _o.Properties())
                    if (!_read.Contains(p.Name)) throw new InvalidDataException($"{_where}: unknown entry '{p.Name}'");
            }
        }
    }
}
