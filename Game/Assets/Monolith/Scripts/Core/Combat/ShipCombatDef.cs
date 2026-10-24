using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Core
{
    /// <summary>One named, separately targetable and destroyable part, as data (ship json) or as read from a model by the visual adapter.</summary>
    public sealed class PartDef
    {
        public string Name, Kind;
        public Vector3 Position, Forward = Vector3.forward;
        public float Radius = 2f;
        /// <summary>Hit points as a percentage of the hull (FS2's way: a ship's subsystems are a share of its hull).</summary>
        public float HpPercent = 10f;
        /// <summary>Relative hit points from a model (ShipSubsystem.Hp: 1 is a typical part); multiplies the kind's share.</summary>
        public float HpWeight = 1f;
        /// <summary>For gun mounts: which of the ship's guns this part feeds ("primary" or "heavy").</summary>
        public string Gun;
        /// <summary>For turrets: the weapon it fires.</summary>
        public string Weapon;
    }

    /// <summary>What a kind of part is worth when a model supplies the parts (a corvette's flak battery, an engine, the bridge).</summary>
    public sealed class PartKindDef
    {
        public string Kind, Weapon;
        public float HpPercent = 1f;
    }

    /// <summary>
    /// One ship class's combat numbers (Resources/Combat/ships/&lt;class id&gt;.json, format "monolith-combat-ship/1"): defence, power,
    /// weapons carried, the hit shape and the named parts. Keyed by the same id as the flight class. Spawn a combat ship with
    /// <c>DamageSystem.Register</c>; ships without a file get <see cref="CombatCatalog.Fallback"/>.
    /// </summary>
    public sealed class ShipCombatDef
    {
        public const string Format = "monolith-combat-ship/1";

        public string Id, Name, SizeClass = CombatEvents.SizeFighter, Note;
        public float Hull = 100f;
        /// <summary>FS2's shield total (spread over four quadrants there); one bubble gets this times the shield bubble scale rule.</summary>
        public float ShieldTotal;
        /// <summary>FS2 $Power Output: scales how fast weapon energy comes back.</summary>
        public float PowerOutput = 2f;
        public float WeaponEnergy = 80f;

        public List<HitBox> Boxes = new List<HitBox>();
        public Vector3 ShieldCentre, ShieldRadii;
        public float BoundRadius;

        public string PrimaryGun, HeavyGun;
        public Vector3[] PrimaryHardpoints = new Vector3[0], HeavyHardpoints = new Vector3[0];
        public List<KeyValuePair<string, int>> Missiles = new List<KeyValuePair<string, int>>();
        public int Decoys;

        public List<PartDef> Parts = new List<PartDef>();
        public Dictionary<string, PartKindDef> PartKinds = new Dictionary<string, PartKindDef>();

        /// <summary>1 normally; the range shrinks a capital hull so two torpedoes can finish it (parts shrink with it, being a share of the hull).</summary>
        public float HullScale = 1f;

        public float HullMax => Hull * HullScale;
        public float ShieldMax(CombatRules rules) => ShieldTotal * rules.ShieldBubbleScale;

        public ShipCombatDef Clone()
        {
            var c = (ShipCombatDef)MemberwiseClone();
            c.Boxes = new List<HitBox>(Boxes);
            c.Missiles = new List<KeyValuePair<string, int>>(Missiles);
            c.Parts = new List<PartDef>(Parts);
            c.PartKinds = new Dictionary<string, PartKindDef>(PartKinds);
            return c;
        }

        public static ShipCombatDef Parse(string json, string where)
        {
            var root = SourcedFields.Open(json, Format, where);
            var d = new ShipCombatDef
            {
                Id = (string)root["id"] ?? throw new InvalidDataException($"{where}: 'id' is missing"),
                Name = (string)root["name"],
                SizeClass = (string)root["sizeClass"] ?? CombatEvents.SizeFighter,
                Note = (string)root["note"],
            };
            if (d.SizeClass != CombatEvents.SizeFighter && d.SizeClass != CombatEvents.SizeBomber && d.SizeClass != CombatEvents.SizeCorvette && d.SizeClass != CombatEvents.SizeCapital)
                throw new InvalidDataException($"{where}: sizeClass '{d.SizeClass}' is not fighter, bomber, corvette or capital");

            var def = new SourcedFields(root["defense"] as JObject, where + " defense");
            d.Hull = def.Float("hull", 1f, 1e8f);
            d.ShieldTotal = def.Float("shield", 0f, 1e6f);
            d.PowerOutput = def.Float("powerOutput", 0f, 1000f);
            d.WeaponEnergy = def.Float("weaponEnergy", 0f, 100000f);
            d.Decoys = def.Int("decoys", 0, 1000, 0);
            def.RejectUnknown();

            var geo = root["geometry"] as JObject ?? throw new InvalidDataException($"{where}: no 'geometry' block");
            foreach (var b in (JArray)geo["boxes"] ?? new JArray())
                d.Boxes.Add(new HitBox { Centre = SourcedFields.V3(b["centre"], where + " box centre"), Half = SourcedFields.V3(b["half"], where + " box half") });
            if (d.Boxes.Count == 0) throw new InvalidDataException($"{where}: geometry needs at least one box");
            if (geo["shieldRadii"] != null) d.ShieldRadii = SourcedFields.V3(geo["shieldRadii"], where + " shieldRadii");
            if (geo["shieldCentre"] != null) d.ShieldCentre = SourcedFields.V3(geo["shieldCentre"], where + " shieldCentre");
            d.BoundRadius = (float?)geo["boundRadius"] ?? 0f;

            if (root["arms"] is JObject arms)
            {
                d.PrimaryGun = (string)arms["primary"];
                d.HeavyGun = (string)arms["heavy"];
                d.PrimaryHardpoints = Points(arms["primaryHardpoints"], where);
                d.HeavyHardpoints = Points(arms["heavyHardpoints"], where);
                if (arms["missiles"] is JObject ms)
                    foreach (var p in ms.Properties()) d.Missiles.Add(new KeyValuePair<string, int>(p.Name, (int)p.Value));
                d.Decoys = (int?)arms["decoys"] ?? d.Decoys;
            }

            if (root["parts"] is JArray parts)
                foreach (JObject p in parts)
                {
                    var pd = new PartDef
                    {
                        Name = (string)p["name"] ?? throw new InvalidDataException($"{where}: a part has no name"),
                        Kind = (string)p["kind"] ?? throw new InvalidDataException($"{where}: part '{(string)p["name"]}' has no kind"),
                        Position = SourcedFields.V3(p["pos"], where + " part pos"),
                        Radius = (float?)p["radius"] ?? 2f,
                        HpPercent = (float?)p["hpPercent"] ?? 10f,
                        Gun = (string)p["gun"],
                        Weapon = (string)p["weapon"],
                    };
                    if (p["forward"] != null) pd.Forward = SourcedFields.V3(p["forward"], where + " part forward").normalized;
                    d.Parts.Add(pd);
                }
            if (root["partKinds"] is JObject kinds)
                foreach (var p in kinds.Properties())
                    d.PartKinds[p.Name] = new PartKindDef { Kind = p.Name, HpPercent = (float?)p.Value["hpPercent"] ?? 1f, Weapon = (string)p.Value["weapon"] };
            return d;
        }

        static Vector3[] Points(JToken t, string where)
        {
            if (!(t is JArray a)) return new Vector3[0];
            var r = new Vector3[a.Count];
            for (int i = 0; i < r.Length; i++) r[i] = SourcedFields.V3(a[i], where + " hardpoint");
            return r;
        }
    }

    /// <summary>Loads combat ship definitions from Resources/Combat/ships by flight class id (cached).</summary>
    public static class CombatCatalog
    {
        public const string Folder = "Combat/ships";
        static readonly Dictionary<string, ShipCombatDef> Cache = new Dictionary<string, ShipCombatDef>();

        public static string ToId(string nameOrId) => (nameOrId ?? "").Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');

        /// <summary>The definition for a flight class id, or null when there is no file.</summary>
        public static ShipCombatDef Get(string classId)
        {
            string id = ToId(classId);
            if (Cache.TryGetValue(id, out var c)) return c;
            var asset = Resources.Load<TextAsset>($"{Folder}/{id}");
            if (asset == null) return null;
            c = ShipCombatDef.Parse(asset.text, $"Resources/{Folder}/{id}.json");
            if (ToId(c.Id) != id) throw new InvalidDataException($"Resources/{Folder}/{id}.json says its id is '{c.Id}'");
            Cache[id] = c;
            return c;
        }

        /// <summary>The definition, or a plain one built from the ship's size so any flight class can fight (the AI builder spawns whatever it likes).</summary>
        public static ShipCombatDef GetOrFallback(string classId, Vector3 size, float radius)
        {
            return Get(classId) ?? Fallback(classId, size, radius);
        }

        public static ShipCombatDef Fallback(string classId, Vector3 size, float radius)
        {
            bool capital = radius > 60f;
            var d = new ShipCombatDef
            {
                Id = ToId(classId), Name = classId,
                SizeClass = radius < 12f ? CombatEvents.SizeFighter : radius < 40f ? CombatEvents.SizeBomber : radius < 160f ? CombatEvents.SizeCorvette : CombatEvents.SizeCapital,
                Hull = capital ? 20000f : 180f, ShieldTotal = capital ? 0f : 380f, PowerOutput = 2f, WeaponEnergy = 80f,
                Note = "fallback: no Resources/Combat/ships file for this class",
            };
            d.Boxes.Add(new HitBox { Centre = Vector3.zero, Half = size * 0.45f });
            d.BoundRadius = radius;
            if (!capital)
            {
                d.PrimaryGun = "needle"; d.HeavyGun = "hammer";
                d.PrimaryHardpoints = new[] { new Vector3(-size.x * 0.29f, -0.35f, size.z * 0.26f), new Vector3(size.x * 0.29f, -0.35f, size.z * 0.26f) };
                d.HeavyHardpoints = new[] { new Vector3(0f, -0.9f, size.z * 0.31f) };
                d.Decoys = 10;
            }
            return d;
        }
    }
}
