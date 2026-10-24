using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Core
{
    public enum WeaponKind { Gun, Flak, HeatMissile, LockMissile, Torpedo, Decoy, Beam }

    /// <summary>
    /// One weapon, from Resources/Combat/weapons.json (format "monolith-weapons/1"). FS2's tables are the model: every number carries the
    /// FS2 field it was copied from or a note saying why it is ours, and tools/combat/check_weapons.py compares the copied ones with
    /// the real table. Damage maths (see the Damage system): against a shield <c>Damage * ShieldMult</c> comes off the shield; what is
    /// left goes to the hull as <c>Damage * HullMult</c> and, if the hit was near a part, to that part as <c>Damage * SubsystemMult</c>.
    /// </summary>
    public sealed class WeaponDef
    {
        public string Id, Name, Role, Note;
        public WeaponKind Kind;
        /// <summary>Name of the FS2 weapon the table values come from (documentation).</summary>
        public string Fs2Weapon;

        /// <summary>Muzzle speed relative to the firing ship, m/s (the ship's own velocity is added, as in FS2).</summary>
        public float Speed;
        /// <summary>Seconds between shots from one weapon.</summary>
        public float FireWait;
        public float Damage;
        public float ShieldMult = 1f, HullMult = 1f, SubsystemMult = 1f;
        /// <summary>Seconds a shot lives (guns, flak, missiles) or a decoy lives (minimum).</summary>
        public float Life = 2f;
        /// <summary>Longest life of a decoy (it lives a random time between <see cref="Life"/> and this).</summary>
        public float LifeMax;
        /// <summary>Weapon energy per shot (guns only).</summary>
        public float Energy;

        /// <summary>Blast: full damage inside <see cref="BlastInner"/> metres of the detonation, falling to nothing at <see cref="BlastOuter"/>.</summary>
        public float BlastInner, BlastOuter;
        /// <summary>Speed of the expanding shockwave (0 = none); a shockwave shoves ships nearby through the flight model's knock hook.</summary>
        public float Shockwave;
        /// <summary>FS2's $Blast Force: shove strength (kN s) at the centre of the blast.</summary>
        public float BlastForce;

        // Guided weapons.
        /// <summary>Seconds for the missile to turn a full circle at best (2 pi / TurnTime rad/s).</summary>
        public float TurnTime = 1f;
        /// <summary>Heat seekers only track a target inside this cone around their nose (full angle, degrees).</summary>
        public float ViewCone = 100f;
        /// <summary>Seconds on target to lock; 0 for fire and forget.</summary>
        public float LockTime;
        /// <summary>Half angle of the cone round the nose the target must stay in for a lock to build, degrees.</summary>
        public float LockCone = 12f;
        /// <summary>Time to reach full speed, seconds.</summary>
        public float AccelTime = 0.6f;
        /// <summary>Hit points of a torpedo (they can be shot down); 0 = cannot be shot down.</summary>
        public float Hp;
        /// <summary>0..1 chance a decoy diverts this missile when released within range.</summary>
        public float DecoyChance;
        /// <summary>A decoy fools missiles inside this range at the moment it is released, metres.</summary>
        public float DecoyRange;

        // Beams (capital turrets, fired through FireRequest): they charge, burn for Life seconds sweeping the target, then end.
        /// <summary>Seconds the beam charges before it fires (FS2 +Warmup).</summary>
        public float BeamWarmup;
        /// <summary>Seconds the beam takes to fade after it stops (FS2 +Warmdown; the juice draws the fade).</summary>
        public float BeamWarmdown;
        /// <summary>How far a beam reaches, metres (FS2 +Weapon Range).</summary>
        public float BeamRange;
        /// <summary>Seconds between damage ticks while a beam burns: each tick does <see cref="Damage"/> to whatever it touches.</summary>
        public float BeamDamageInterval = 0.17f;
        /// <summary>Parts within this many metres of where the beam touches a hull take its part damage (the beam is fat).</summary>
        public float BeamCutRadius = 18f;

        /// <summary>How far the weapon reaches: speed x life (a beam: its range).</summary>
        public float Range => Kind == WeaponKind.Beam ? BeamRange : Speed * Life;
        public bool IsBeam => Kind == WeaponKind.Beam;
        public float TurnRate => TurnTime > 0.01f ? 2f * Mathf.PI / TurnTime : 100f;
        public bool IsGuided => Kind == WeaponKind.HeatMissile || Kind == WeaponKind.LockMissile || Kind == WeaponKind.Torpedo;
        public bool NeedsLock => LockTime > 0f;
        /// <summary>The weapon id the juice and sound know the bolt by (the event's subject).</summary>
        public string EventId => Id;
    }

    /// <summary>Loads weapons from Resources/Combat/weapons.json (cached). Ids are lower-case snake_case.</summary>
    public static class WeaponCatalog
    {
        public const string Format = "monolith-weapons/1";
        public const string ResourcePath = "Combat/weapons";

        static Dictionary<string, WeaponDef> _byId;
        static List<WeaponDef> _all;

        public static IReadOnlyList<WeaponDef> All { get { Ensure(); return _all; } }

        public static WeaponDef Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            Ensure();
            return _byId.TryGetValue(id, out var w) ? w : null;
        }

        public static WeaponDef Require(string id) =>
            Get(id) ?? throw new KeyNotFoundException($"no weapon '{id}' in Resources/{ResourcePath}.json");

        /// <summary>Replace the catalogue (tests).</summary>
        public static void Load(string json, string where)
        {
            var list = Parse(json, where);
            _all = list;
            _byId = new Dictionary<string, WeaponDef>();
            foreach (var w in list) _byId[w.Id] = w;
        }

        static void Ensure()
        {
            if (_byId != null) return;
            var asset = Resources.Load<TextAsset>(ResourcePath) ?? throw new FileNotFoundException($"Resources/{ResourcePath}.json is missing");
            Load(asset.text, $"Resources/{ResourcePath}.json");
        }

        public static List<WeaponDef> Parse(string json, string where)
        {
            var root = SourcedFields.Open(json, Format, where);
            var list = new List<WeaponDef>();
            if (!(root["weapons"] is JObject weapons)) throw new InvalidDataException($"{where}: no 'weapons' block");
            foreach (var p in weapons.Properties())
            {
                string w = $"{where} weapons.{p.Name}";
                var o = p.Value as JObject ?? throw new InvalidDataException($"{w}: not an object");
                var f = new SourcedFields(o["fields"] as JObject, w);
                var def = new WeaponDef
                {
                    Id = p.Name,
                    Name = (string)o["name"] ?? p.Name,
                    Role = (string)o["role"] ?? "",
                    Note = (string)o["note"],
                    Fs2Weapon = (string)o["fs2Weapon"],
                    Kind = ParseKind((string)o["kind"], w),
                    Speed = f.Float("speed", 0f, 20000f),
                    FireWait = f.Float("fireWait", 0.01f, 600f),
                    Damage = f.Float("damage", 0f, 100000f),
                    ShieldMult = f.Float("shieldMult", 0f, 10f, 1f),
                    HullMult = f.Float("hullMult", 0f, 10f, 1f),
                    SubsystemMult = f.Float("subsystemMult", 0f, 10f, 1f),
                    Life = f.Float("life", 0.05f, 600f),
                    LifeMax = f.Float("lifeMax", 0f, 600f, 0f),
                    Energy = f.Float("energy", 0f, 1000f, 0f),
                    BlastInner = f.Float("blastInner", 0f, 5000f, 0f),
                    BlastOuter = f.Float("blastOuter", 0f, 5000f, 0f),
                    Shockwave = f.Float("shockwave", 0f, 1000f, 0f),
                    BlastForce = f.Float("blastForce", 0f, 100000f, 0f),
                    TurnTime = f.Float("turnTime", 0.05f, 100f, 1f),
                    ViewCone = f.Float("viewCone", 1f, 360f, 100f),
                    LockTime = f.Float("lockTime", 0f, 60f, 0f),
                    LockCone = f.Float("lockCone", 1f, 90f, 12f),
                    AccelTime = f.Float("accelTime", 0.01f, 30f, 0.6f),
                    Hp = f.Float("hp", 0f, 100000f, 0f),
                    DecoyChance = f.Float("decoyChance", 0f, 1f, 0f),
                    DecoyRange = f.Float("decoyRange", 0f, 5000f, 0f),
                    BeamWarmup = f.Float("beamWarmupMs", 0f, 60000f, 0f) * 0.001f,
                    BeamWarmdown = f.Float("beamWarmdownMs", 0f, 60000f, 0f) * 0.001f,
                    BeamRange = f.Float("beamRange", 0f, 100000f, 0f),
                    BeamDamageInterval = f.Float("beamDamageInterval", 0.02f, 5f, 0.17f),
                    BeamCutRadius = f.Float("beamCutRadius", 0f, 500f, 18f),
                };
                f.RejectUnknown();
                if (def.Kind != WeaponKind.Decoy && def.Speed <= 0f) throw new InvalidDataException($"{w}: speed must be above 0");
                if (def.Kind == WeaponKind.Beam && def.BeamRange <= 0f) throw new InvalidDataException($"{w}: a beam needs a beamRange");
                list.Add(def);
            }
            if (list.Count == 0) throw new InvalidDataException($"{where}: no weapons");
            return list;
        }

        static WeaponKind ParseKind(string s, string where)
        {
            switch ((s ?? "").ToLowerInvariant())
            {
                case "gun": return WeaponKind.Gun;
                case "flak": return WeaponKind.Flak;
                case "heat": return WeaponKind.HeatMissile;
                case "lock": return WeaponKind.LockMissile;
                case "torpedo": return WeaponKind.Torpedo;
                case "decoy": return WeaponKind.Decoy;
                case "beam": return WeaponKind.Beam;
                default: throw new InvalidDataException($"{where}: kind '{s}' is not gun, flak, heat, lock, torpedo, decoy or beam");
            }
        }
    }
}
