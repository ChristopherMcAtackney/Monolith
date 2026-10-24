using System.Collections.Generic;
using System.IO;
using Monolith.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.AI
{
    /// <summary>
    /// Radio lines from Resources/AI/radio.json (placeholders, data): raises <c>radio_line</c> with the speaker and the text, which the HUD
    /// shows. Lines of one situation are used in turn; one speaker says at most one line every couple of seconds.
    /// </summary>
    public sealed class AiRadio
    {
        public const string ResourcePath = "AI/radio";
        readonly AiDirector _ai;
        readonly Dictionary<string, string[]> _lines = new Dictionary<string, string[]>();
        readonly Dictionary<string, int> _next = new Dictionary<string, int>();
        readonly Dictionary<int, float> _lastBy = new Dictionary<int, float>();

        /// <summary>Lines said so far (checks read it).</summary>
        public readonly List<string> Said = new List<string>();

        public AiRadio(AiDirector ai)
        {
            _ai = ai;
            var asset = Resources.Load<TextAsset>(ResourcePath) ?? throw new FileNotFoundException($"Resources/{ResourcePath}.json is missing");
            var root = JObject.Parse(asset.text);
            if ((string)root["format"] != "monolith-radio/1") throw new InvalidDataException($"Resources/{ResourcePath}.json: format must be monolith-radio/1");
            foreach (var p in ((JObject)root["lines"]).Properties()) _lines[p.Name] = p.Value.ToObject<string[]>();
        }

        public bool Has(string situation) => _lines.ContainsKey(situation);

        /// <summary>Say a line of <paramref name="situation"/> as <paramref name="speaker"/> (a ship, or <see cref="GameWorld.NoShip"/> with a name).</summary>
        public string Say(string situation, int speaker, string speakerName = null, string arg = null, bool force = false)
        {
            if (!_lines.TryGetValue(situation, out var set) || set.Length == 0) return null;
            float now = _ai.World.Time;
            if (!force && speaker != GameWorld.NoShip && _lastBy.TryGetValue(speaker, out float last) && now - last < 2.5f) return null;
            _next.TryGetValue(situation, out int i);
            _next[situation] = i + 1;
            string text = set[i % set.Length];
            if (arg != null) text = text.Replace("{0}", arg);
            string who = speakerName ?? _ai.World.Ships.Get(speaker)?.Callsign ?? "";
            string line = (who.Length > 0 ? who + ": " : "") + text;
            if (speaker != GameWorld.NoShip) _lastBy[speaker] = now;
            Said.Add(line);
            GameEvents.Raise(CombatEvents.RadioLine, line.ToUpperInvariant(), 0f, speaker);
            MnLog.Info("RADIO " + line);
            return line;
        }
    }
}
