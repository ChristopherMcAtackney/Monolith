using System;
using System.Collections.Generic;
using System.IO;
using Monolith.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Loading
{
    /// <summary>
    /// The loading bar's honest progress. The load is a plan of named steps (each a real piece of work: an asset load, the world's construction, the sound's decode, a
    /// warm-up), each with a weight; a step reports how far it has come (a fraction from a real count: files decoded, frames drawn, an asset operation's progress) and the
    /// bar is the weights of the work done over the weights of all of it. Nothing advances on a timer. A weight is how long the step took last time on this machine
    /// (<c>loading_stats.json</c> beside the game, written when a load ends), or the shipped figure (<c>Resources/Loading/loading.json</c>) the first time, so the bar moves
    /// at about the pace the loading really goes. Steps may overlap (the sound decodes while the sky warms up).
    /// </summary>
    public sealed class LoadMeter
    {
        public sealed class Step
        {
            public string Id, Label;
            public float Weight, Frac;
            public bool Started, Done;
            public double StartedAt, Ms;
        }

        readonly List<Step> _steps = new List<Step>();
        readonly Dictionary<string, Step> _byId = new Dictionary<string, Step>();
        float _total;
        readonly double _t0;

        public IReadOnlyList<Step> Steps => _steps;

        public LoadMeter(IEnumerable<(string id, string label, float weight)> plan, IDictionary<string, float> measured = null)
        {
            _t0 = Now();
            foreach (var p in plan)
            {
                float w = p.weight;
                if (measured != null && measured.TryGetValue(p.id, out float m) && m > 0f) w = Mathf.Max(5f, m);
                var s = new Step { Id = p.id, Label = p.label, Weight = Mathf.Max(1f, w) };
                _steps.Add(s); _byId[p.id] = s; _total += s.Weight;
            }
        }

        static double Now() => System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        public void Begin(string id)
        {
            if (!_byId.TryGetValue(id, out var s) || s.Started) return;
            s.Started = true; s.StartedAt = Now();
        }

        /// <summary>How far a step has come (0..1); it begins the step if it has not begun.</summary>
        public void Report(string id, float frac)
        {
            if (!_byId.TryGetValue(id, out var s) || s.Done) return;
            if (!s.Started) Begin(id);
            s.Frac = Mathf.Max(s.Frac, Mathf.Clamp01(frac));
        }

        public void Finish(string id)
        {
            if (!_byId.TryGetValue(id, out var s) || s.Done) return;
            if (!s.Started) Begin(id);
            s.Done = true; s.Frac = 1f; s.Ms = Now() - s.StartedAt;
        }

        /// <summary>0..1: the weight of the work done over the weight of all of it.</summary>
        public float Progress
        {
            get
            {
                float done = 0f;
                foreach (var s in _steps) done += s.Weight * (s.Done ? 1f : s.Frac);
                return _total > 0f ? Mathf.Clamp01(done / _total) : 0f;
            }
        }

        public bool AllDone { get { foreach (var s in _steps) if (!s.Done) return false; return true; } }

        /// <summary>What the status line says: the first step that has begun and is not done (the one holding the bar back), else the next one to begin, else the last.</summary>
        public string Status
        {
            get
            {
                foreach (var s in _steps) if (s.Started && !s.Done) return s.Label;
                foreach (var s in _steps) if (!s.Done) return s.Label;
                return _steps.Count > 0 ? _steps[_steps.Count - 1].Label : "";
            }
        }

        /// <summary>Milliseconds since the meter was made.</summary>
        public double ElapsedMs => Now() - _t0;

        // ------------------------------------------------------------------------------------------------ what each step cost last time

        public static string StatsFile => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "loading_stats.json");

        /// <summary>The steps' durations from the last load on this machine (ms by step id), or the shipped figures.</summary>
        public static Dictionary<string, float> LoadWeights()
        {
            var w = new Dictionary<string, float>();
            try
            {
                var asset = Resources.Load<TextAsset>("Loading/loading");
                if (asset != null) Read(JObject.Parse(asset.text)["weights"] as JObject, w);
                if (File.Exists(StatsFile)) Read(JObject.Parse(File.ReadAllText(StatsFile))["steps"] as JObject, w);
            }
            catch (Exception e) { MnLog.Warn("LOADING could not read the step weights: " + e.Message); }
            return w;
        }

        static void Read(JObject o, Dictionary<string, float> into)
        {
            if (o == null) return;
            foreach (var p in o) if (p.Value.Type == JTokenType.Float || p.Value.Type == JTokenType.Integer) into[p.Key] = (float)p.Value;
        }

        /// <summary>Write what each step took, so the next load's bar goes at this machine's pace (not in captures or headless runs).</summary>
        public void SaveStats()
        {
            if (CommandLine.Has("-mnCapture") || Application.isBatchMode) return;
            try
            {
                // a load that is only part of a start (the start screen's two halves) keeps what the other half wrote
                JObject steps = null;
                try { if (File.Exists(StatsFile)) steps = JObject.Parse(File.ReadAllText(StatsFile))["steps"] as JObject; } catch (Exception) { steps = null; }
                steps ??= new JObject();
                foreach (var s in _steps) if (s.Done) steps[s.Id] = Math.Round(s.Ms, 1);
                File.WriteAllText(StatsFile, new JObject { ["steps"] = steps, ["totalMs"] = Math.Round(ElapsedMs, 0) }.ToString());
            }
            catch (Exception e) { MnLog.Warn("LOADING could not save loading_stats.json: " + e.Message); }
        }
    }
}
