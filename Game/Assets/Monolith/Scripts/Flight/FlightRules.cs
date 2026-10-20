using System.IO;
using UnityEngine;

namespace Monolith.Flight
{
    /// <summary>Flight constants shared by every class (Resources/Flight/flight_rules.json, each with its source).</summary>
    public sealed class FlightRules
    {
        public const string Format = "monolith-flight-rules/1";
        public const string ResourcePath = "Flight/flight_rules";

        public float AutoBankFraction;
        public float RampShortcutDivisor;
        public float RampSnapTimeConstant;
        public float AfterburnerMinFuel;
        public float AfterburnerRestartLockout;
        public float AfterburnerRechargeScale;
        public float KnockTime;
        public float KnockDampFactor;
        public float KnockFullSpeedChange;
        public float KnockSpin;
        public float ShockTurnDampFactor;
        public float ShockShake;
        public float MaxTurnRate;
        public float MatchSpeedCancel;

        static FlightRules _default;

        /// <summary>The rules shipped in Resources (cached).</summary>
        public static FlightRules Default
        {
            get
            {
                if (_default != null) return _default;
                var asset = Resources.Load<TextAsset>(ResourcePath);
                if (asset == null) throw new FileNotFoundException($"Resources/{ResourcePath}.json is missing");
                return _default = Parse(asset.text, $"Resources/{ResourcePath}.json");
            }
        }

        public static FlightRules Parse(string json, string where)
        {
            var root = DataFile.Open(json, Format, where);
            var f = new DataFile.Fields(root, "rules", where);
            var r = new FlightRules
            {
                AutoBankFraction = f.Float("autoBankFraction", 0f, 2f),
                RampShortcutDivisor = f.Float("rampShortcutDivisor", 0.001f, 1000f),
                RampSnapTimeConstant = f.Float("rampSnapTimeConstant", 0f, 1f),
                AfterburnerMinFuel = f.Float("afterburnerMinFuel", 0f, 100000f),
                AfterburnerRestartLockout = f.Float("afterburnerRestartLockout", 0f, 60f),
                AfterburnerRechargeScale = f.Float("afterburnerRechargeScale", 0f, 100f),
                KnockTime = f.Float("knockTime", 0.01f, 60f),
                KnockDampFactor = f.Float("knockDampFactor", 1f, 1000f),
                KnockFullSpeedChange = f.Float("knockFullSpeedChange", 0.01f, 100000f),
                KnockSpin = f.Float("knockSpin", 0f, 100f),
                ShockTurnDampFactor = f.Float("shockTurnDampFactor", 1f, 1000f),
                ShockShake = f.Float("shockShake", 0f, 100f),
                MaxTurnRate = f.Float("maxTurnRate", 0.01f, 1000f),
                MatchSpeedCancel = f.Float("matchSpeedCancel", 0f, 1f),
            };
            f.RejectUnknown();
            return r;
        }
    }
}
