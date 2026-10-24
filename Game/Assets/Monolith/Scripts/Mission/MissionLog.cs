using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Mission
{
    /// <summary>
    /// The mission's timeline (design section 7, measured pacing): every arrival, death, departure, event fired, objective, radio line and
    /// music change with its time, and once a second the pacing (threats alive, hostiles on the player, the nearest hostile, whether it is quiet,
    /// hulls). Written as events.csv and pacing.csv, summarised in <see cref="Summary"/> and <see cref="ToJson"/>.
    /// </summary>
    public sealed class MissionLog
    {
        public struct Entry { public float T; public string Kind, Text; }

        /// <summary>Quiet: no hostile fighter alive, or none within this range and none on the player (metres).</summary>
        public float QuietRange = 2500f;
        public readonly List<Entry> Entries = new List<Entry>();
        readonly StringBuilder _csv = new StringBuilder();
        readonly List<string> _hullNames = new List<string>();
        float _t0, _quietSince = -1f;

        public float LongestQuiet { get; private set; }
        public float QuietSeconds { get; private set; }
        public float FightSeconds { get; private set; }
        public int MaxEngaging { get; private set; }
        public int MaxThreats { get; private set; }
        public float SumThreats { get; private set; }
        public int Samples { get; private set; }
        public float AvgThreats => Samples > 0 ? SumThreats / Samples : 0f;
        public float NearestMin { get; private set; } = float.MaxValue;

        public void Begin(float t0, IEnumerable<string> hulls, IEnumerable<string> ranges = null)
        {
            _t0 = t0; _quietSince = -1f; LongestQuiet = QuietSeconds = FightSeconds = SumThreats = 0f; MaxEngaging = MaxThreats = Samples = 0; NearestMin = float.MaxValue;
            Entries.Clear(); _csv.Clear(); _hullNames.Clear();
            _hullNames.AddRange(hulls);
            _csv.Append("t,threats,engaging_player,nearest_m,quiet,fighting,player_hull");
            foreach (var h in _hullNames) _csv.Append(',').Append(Col(h)).Append("_hull");
            _csv.Append(",music");
            if (ranges != null) foreach (var r in ranges) _csv.Append(',').Append(Col(r)).Append("_m");
            _csv.AppendLine();
        }

        static string Col(string s) => s.Replace(' ', '_').Replace(",", "").ToLowerInvariant();

        public void Add(float t, string kind, string text) => Entries.Add(new Entry { T = t - _t0, Kind = kind, Text = text });

        public void Sample(float t, bool fighting, int threats, int engaging, float nearest, float playerHull, IList<float> hulls, string music, IList<float> ranges = null)
        {
            bool quiet = threats == 0 || (nearest > QuietRange && engaging == 0);
            _csv.Append(string.Format(CultureInfo.InvariantCulture, "{0:0},{1},{2},{3:0},{4},{5},{6:0.00}", t - _t0, threats, engaging, nearest < 1e8f ? nearest : -1f, quiet ? 1 : 0, fighting ? 1 : 0, playerHull));
            for (int i = 0; i < hulls.Count; i++) _csv.Append(string.Format(CultureInfo.InvariantCulture, ",{0:0.00}", hulls[i]));
            _csv.Append(',').Append(music);
            if (ranges != null) foreach (var r in ranges) _csv.Append(string.Format(CultureInfo.InvariantCulture, ",{0:0}", r));
            _csv.AppendLine();
            if (!fighting) { _quietSince = -1f; return; }
            Samples++; FightSeconds += 1f; SumThreats += threats;
            MaxEngaging = Mathf.Max(MaxEngaging, engaging); MaxThreats = Mathf.Max(MaxThreats, threats);
            if (nearest < NearestMin) NearestMin = nearest;
            if (quiet)
            {
                QuietSeconds += 1f;
                if (_quietSince < 0f) _quietSince = t;
                LongestQuiet = Mathf.Max(LongestQuiet, t - _quietSince + 1f);
            }
            else _quietSince = -1f;
        }

        public string Summary => string.Format(CultureInfo.InvariantCulture,
            "fight {0:0} s, longest quiet {1:0} s, quiet {2:0} s total, threats max {3} avg {4:0.0}, most on the player at once {5}",
            FightSeconds, LongestQuiet, QuietSeconds, MaxThreats, AvgThreats, MaxEngaging);

        public string EventsCsv()
        {
            var sb = new StringBuilder("t,kind,what\n");
            foreach (var e in Entries) sb.Append(e.T.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(e.Kind).Append(",\"").Append((e.Text ?? "").Replace("\"", "'")).Append("\"\n");
            return sb.ToString();
        }

        public void Write(string dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "pacing.csv"), _csv.ToString());
            File.WriteAllText(Path.Combine(dir, "events.csv"), EventsCsv());
        }

        public JObject ToJson() => new JObject
        {
            ["fightSeconds"] = FightSeconds, ["longestQuiet"] = LongestQuiet, ["quietSeconds"] = QuietSeconds, ["maxThreats"] = MaxThreats,
            ["avgThreats"] = System.Math.Round(AvgThreats, 2), ["maxEngaging"] = MaxEngaging,
        };
    }
}
