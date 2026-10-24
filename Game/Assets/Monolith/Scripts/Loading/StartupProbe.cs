using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Monolith.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Loading
{
    /// <summary>
    /// What a start costs, measured the same way with the loading screen and without it (<c>-mnNoLoadingScreen</c>): milestones in milliseconds since the process started
    /// (<c>[MN] STARTUP +1234 ms: label</c>), and, once the game is flyable, every frame's time for the next seconds: the worst, the slow ones and when they came.
    /// <c>-mnStartupProbe [seconds]</c> turns it on (10 s by default); <c>-mnStartupQuit</c> ends the run when the window is over; <c>-mnOut dir</c> also writes <c>startup.json</c>.
    /// </summary>
    public static class StartupProbe
    {
        static readonly DateTime ProcessStart = SafeStart();
        static readonly List<(string label, double ms)> Marks = new List<(string, double)>();
        static double _flyableMs = -1;
        /// <summary>Seconds (unscaled game time) since the game became flyable, or -1 before.</summary>
        public static float SecondsFlying => _flyableMs < 0 ? -1f : (float)((SinceLaunchMs - _flyableMs) / 1000.0);

        public static bool Enabled => CommandLine.Has("-mnStartupProbe");
        public static float WindowSeconds => Mathf.Max(1f, CommandLine.GetFloat("-mnStartupProbe", 10f));

        static DateTime SafeStart()
        {
            try { return Process.GetCurrentProcess().StartTime; } catch { return DateTime.Now.AddMilliseconds(-Time.realtimeSinceStartup * 1000.0); }
        }

        /// <summary>Milliseconds since the process started.</summary>
        public static double SinceLaunchMs => (DateTime.Now - ProcessStart).TotalMilliseconds;

        public static void Mark(string label)
        {
            double ms = SinceLaunchMs;
            Marks.Add((label, ms));
            if (Enabled) MnLog.Info($"STARTUP +{ms.ToString("0", CultureInfo.InvariantCulture)} ms: {label}");
        }

        /// <summary>The game is flyable now: note it and start timing the frames.</summary>
        public static void Flyable(string how = "flyable")
        {
            Mark(how);
            _flyableMs = SinceLaunchMs;
            if (!Enabled) return;
            var go = new GameObject("StartupProbe");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Sampler>().Begin(WindowSeconds);
        }

        /// <summary>Flyable after a few frames have been shown (the no-loading-screen start: the first frame is the one that stalls).</summary>
        public static void FlyableAfterFrames(int frames)
        {
            if (!Enabled) return;
            var go = new GameObject("StartupProbeWait");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Waiter>().Frames = frames;
        }

        sealed class Waiter : MonoBehaviour
        {
            public int Frames;
            int _n;
            void Update()
            {
                _n++;
                MnLog.Info($"STARTUP frame {_n} after the build took {(Time.unscaledDeltaTime * 1000f).ToString("0", CultureInfo.InvariantCulture)} ms");
                if (--Frames <= 0) { Flyable("flyable (frames shown)"); Destroy(gameObject); }
            }
        }

        sealed class Sampler : MonoBehaviour
        {
            readonly List<float> _ms = new List<float>(4096);
            readonly List<float> _at = new List<float>(4096);
            float _t, _window;
            bool _done;
            int _gc0, _gc1, _gc2;

            public void Begin(float seconds) { _window = seconds; _gc0 = GC.CollectionCount(0); _gc1 = GC.CollectionCount(1); _gc2 = GC.CollectionCount(2); }

            void Update()
            {
                if (_done) return;
                float dt = Time.unscaledDeltaTime;
                _t += dt;
                _ms.Add(dt * 1000f); _at.Add(_t);
                int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
                if (dt > 0.02f)
                    MnLog.Info($"STARTUP slow frame {(dt * 1000f).ToString("0.0", CultureInfo.InvariantCulture)} ms at {_t.ToString("0.00", CultureInfo.InvariantCulture)} s (frame {Time.frameCount}): collections this frame gen0 +{g0 - _gc0}, gen1 +{g1 - _gc1}, gen2 +{g2 - _gc2}");
                _gc0 = g0; _gc1 = g1; _gc2 = g2;
                if (_t >= _window) Finish();
            }

            void Finish()
            {
                _done = true;
                var sorted = new List<float>(_ms);
                sorted.Sort();
                int n = sorted.Count;
                float worst = sorted[n - 1], p99 = sorted[Mathf.Min(n - 1, (int)(n * 0.99f))], avg = 0f;
                foreach (var v in sorted) avg += v;
                avg /= n;
                int over33 = 0, over50 = 0, over100 = 0;
                foreach (var v in sorted) { if (v > 33.4f) over33++; if (v > 50f) over50++; if (v > 100f) over100++; }
                // the five worst, in the order they came
                var idx = new List<int>();
                for (int i = 0; i < n; i++) idx.Add(i);
                idx.Sort((a, b) => _ms[b].CompareTo(_ms[a]));
                var top = new List<(float ms, float at)>();
                for (int i = 0; i < Mathf.Min(5, idx.Count); i++) top.Add((_ms[idx[i]], _at[idx[i]]));
                string tops = string.Join(", ", top.ConvertAll(x => $"{x.ms.ToString("0.0", CultureInfo.InvariantCulture)} ms at {x.at.ToString("0.00", CultureInfo.InvariantCulture)} s"));
                MnLog.Info($"STARTUP first {_window.ToString("0.#", CultureInfo.InvariantCulture)} s of flight: {n} frames, mean {avg.ToString("0.00", CultureInfo.InvariantCulture)} ms, p99 {p99.ToString("0.0", CultureInfo.InvariantCulture)} ms, worst {worst.ToString("0.0", CultureInfo.InvariantCulture)} ms; frames over 33 ms: {over33}, over 50 ms: {over50}, over 100 ms: {over100}; worst five: {tops}");
                string dir = CommandLine.Get("-mnOut");
                if (!string.IsNullOrEmpty(dir))
                {
                    try
                    {
                        Directory.CreateDirectory(dir);
                        var marks = new JArray();
                        foreach (var m in Marks) marks.Add(new JObject { ["label"] = m.label, ["ms"] = Math.Round(m.ms, 0) });
                        var o = new JObject
                        {
                            ["flyableMs"] = Math.Round(_flyableMs, 0), ["marks"] = marks, ["windowSeconds"] = _window, ["frames"] = n, ["meanMs"] = Math.Round(avg, 2),
                            ["p99Ms"] = Math.Round(p99, 1), ["worstMs"] = Math.Round(worst, 1), ["over33"] = over33, ["over50"] = over50, ["over100"] = over100,
                            ["worstFive"] = new JArray(top.ConvertAll(x => (JToken)new JObject { ["ms"] = Math.Round(x.ms, 1), ["at"] = Math.Round(x.at, 2) })),
                            ["gpu"] = SystemInfo.graphicsDeviceName, ["resolution"] = Screen.width + "x" + Screen.height,
                        };
                        File.WriteAllText(Path.Combine(dir, "startup.json"), o.ToString());
                    }
                    catch (Exception e) { MnLog.Warn("STARTUP could not write startup.json: " + e.Message); }
                }
                if (CommandLine.Has("-mnStartupQuit")) Application.Quit(0);
            }
        }
    }
}
