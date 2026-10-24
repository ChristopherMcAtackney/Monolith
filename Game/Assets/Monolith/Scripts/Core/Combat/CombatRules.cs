using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Core
{
    /// <summary>
    /// Constants shared by the combat systems (Resources/Combat/rules.json, format "monolith-combat-rules/1", every value with its source).
    /// Weapon numbers are in weapons.json; per-ship numbers in Combat/ships/&lt;class&gt;.json.
    /// </summary>
    public sealed class CombatRules
    {
        public const string Format = "monolith-combat-rules/1";
        public const string ResourcePath = "Combat/rules";

        // Shields
        public float ShieldRegenPerSecond, ShieldRegenDelay, ShieldBubbleScale;
        // Energy and the power choice
        public float WeaponEnergyLevel, PowerFavoured, PowerUnfavoured, PowerEngineSpeedBonus, PowerEngineAfterburnerBoost;
        // Self-healing (the diaspora's living tech)
        public float HealDelay, HealHullPerSecond, HealPartPerSecond;
        // Parts
        public float PartDamagedBelow, PartReach, EngineLimpFraction, ReactorDownEnergy;
        // Collisions
        public float CollisionEnergyToDamage, CollisionRestitution, CollisionMinSeparation, CollisionPairCooldown, CollisionMaxKnockSpeed, CollisionMinClosing;
        // Missiles, locks, warnings, decoys
        public float WarningRange, LockLoseSeconds, LockDecayMultiplier, MissileMinArmTime, MissileTrackLoseSeconds;
        // Guns
        public float GunConvergenceRange, FlakProximity;

        static CombatRules _default;

        public static CombatRules Default
        {
            get
            {
                if (_default != null) return _default;
                var asset = Resources.Load<TextAsset>(ResourcePath) ?? throw new FileNotFoundException($"Resources/{ResourcePath}.json is missing");
                return _default = Parse(asset.text, $"Resources/{ResourcePath}.json");
            }
        }

        public static CombatRules Parse(string json, string where)
        {
            var root = SourcedFields.Open(json, Format, where);
            var f = new SourcedFields(root["rules"] as JObject, where + " rules");
            var r = new CombatRules
            {
                ShieldRegenPerSecond = f.Float("shieldRegenPerSecond", 0f, 10f),
                ShieldRegenDelay = f.Float("shieldRegenDelay", 0f, 60f),
                ShieldBubbleScale = f.Float("shieldBubbleScale", 0.01f, 10f),
                WeaponEnergyLevel = f.Float("weaponEnergyLevel", 0f, 10f),
                PowerFavoured = f.Float("powerFavoured", 0f, 10f),
                PowerUnfavoured = f.Float("powerUnfavoured", 0f, 10f),
                PowerEngineSpeedBonus = f.Float("powerEngineSpeedBonus", 0f, 1f),
                PowerEngineAfterburnerBoost = f.Float("powerEngineAfterburnerBoost", 0f, 10f),
                HealDelay = f.Float("healDelay", 0f, 600f),
                HealHullPerSecond = f.Float("healHullPerSecond", 0f, 1f),
                HealPartPerSecond = f.Float("healPartPerSecond", 0f, 1f),
                PartDamagedBelow = f.Float("partDamagedBelow", 0f, 1f),
                PartReach = f.Float("partReach", 0f, 20f),
                EngineLimpFraction = f.Float("engineLimpFraction", 0f, 1f),
                ReactorDownEnergy = f.Float("reactorDownEnergy", 0f, 1f),
                CollisionEnergyToDamage = f.Float("collisionEnergyToDamage", 0f, 1f),
                CollisionRestitution = f.Float("collisionRestitution", 0f, 1f),
                CollisionMinSeparation = f.Float("collisionMinSeparation", 0f, 100f),
                CollisionPairCooldown = f.Float("collisionPairCooldown", 0f, 60f),
                CollisionMaxKnockSpeed = f.Float("collisionMaxKnockSpeed", 0f, 1000f),
                CollisionMinClosing = f.Float("collisionMinClosing", 0f, 100f),
                WarningRange = f.Float("warningRange", 0f, 100000f),
                LockLoseSeconds = f.Float("lockLoseSeconds", 0f, 60f),
                LockDecayMultiplier = f.Float("lockDecayMultiplier", 0f, 100f),
                MissileMinArmTime = f.Float("missileMinArmTime", 0f, 10f),
                MissileTrackLoseSeconds = f.Float("missileTrackLoseSeconds", 0f, 10f),
                GunConvergenceRange = f.Float("gunConvergenceRange", 10f, 100000f),
                FlakProximity = f.Float("flakProximity", 0f, 1000f),
            };
            f.RejectUnknown();
            return r;
        }
    }
}
