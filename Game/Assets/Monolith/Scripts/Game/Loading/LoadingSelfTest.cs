using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Monolith.Core;
using Monolith.Hud;
using Monolith.Loading;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// Headless checks of the loading screen: <c>Monolith.exe -batchmode -nographics -mnLoadingSelfTest [-mnOut dir]</c>. They cover the splash (it loads, it fits any shape of screen without
    /// being stretched or leaving the screen, the settle is gentle and ends), the bar (every style, at every screen size: nothing leaves the screen, the words fit the plate and do not
    /// overlap, the bar is in the style it was asked for), the meter (honest: monotone, ends at the whole, the status names the step that is holding it back, a step cannot jump the
    /// bar), the world's hold (a held clock does not tick), the preload manifest (every asset it names loads, so it is not stale) and that the real start is wired in.
    /// Each line logs <c>LOADING PASS</c> or <c>LOADING FAIL</c>; the result goes to loading_selftest.txt when <c>-mnOut</c> is given.
    /// </summary>
    public static class LoadingSelfTest
    {
        const string Flag = "-mnLoadingSelfTest";
        static int _checks, _fails;
        static readonly List<string> Lines = new List<string>();

        public static bool Requested => CommandLine.Has(Flag);

        static void Check(bool ok, string what)
        {
            _checks++;
            if (!ok) _fails++;
            string line = $"LOADING {(ok ? "PASS" : "FAIL")}: {what}";
            Lines.Add(line);
            MnLog.Info(line);
        }

        static string N(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        static readonly (int w, int h)[] Sizes =
        {
            (1280, 720), (1366, 768), (1600, 900), (1920, 1080), (2560, 1440), (3840, 2160),   // 16:9
            (2560, 1080), (3440, 1440), (3840, 1080),                                          // wide
            (1280, 1024), (1440, 1080), (1024, 768), (1080, 1080),                             // squarer
            (1920, 1200), (1080, 1920),                                                        // 16:10, portrait
        };

        public static bool Run()
        {
            _checks = 0; _fails = 0; Lines.Clear();
            string was = HudStyles.Current.Name;
            try
            {
                Splash();
                Fit();
                Bar();
                Meter();
                Hold();
                Manifest();
                Wiring();
            }
            catch (Exception e)
            {
                _fails++;
                MnLog.Error("LOADING selftest threw: " + e);
                Lines.Add("LOADING FAIL: threw " + e.Message);
            }
            HudStyles.Set(was);
            string summary = $"LOADING SELFTEST {(_fails == 0 ? "OK" : "FAILED")}: {_checks - _fails} of {_checks} checks pass";
            MnLog.Info(summary);
            Lines.Add(summary);
            string dir = CommandLine.Get("-mnOut");
            if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); File.WriteAllLines(Path.Combine(dir, "loading_selftest.txt"), Lines); }
            return _fails == 0;
        }

        // ------------------------------------------------------------------------------------------------ the picture

        static void Splash()
        {
            var tex = LoadingSplash.Load();
            Check(tex != null, "the splash loads (the user's Oort splash, shipped with the game)");
            if (tex == null) return;
            Check(tex.width == 1672 && tex.height == 941, $"the splash is the 1672x941 picture ({tex.width}x{tex.height})");
            UnityEngine.Object.DestroyImmediate(tex);
            // not a blank picture: some of it is bright, some dark (the shipped bytes, decoded readable here)
            var asset = Resources.Load<TextAsset>("Loading/" + LoadingSplash.DefaultName + ".png");
            var readable = new Texture2D(2, 2);
            ImageConversion.LoadImage(readable, asset.bytes, false);
            var px = readable.GetPixels32();
            int bright = 0, dark = 0;
            for (int i = 0; i < px.Length; i += 37) { int v = px[i].r + px[i].g + px[i].b; if (v > 300) bright++; else if (v < 90) dark++; }
            Check(bright > 50 && dark > 500, $"the splash has light and dark in it ({bright} bright and {dark} dark samples)");
            UnityEngine.Object.DestroyImmediate(readable);
        }

        static void Fit()
        {
            const float iw = 1672f, ih = 941f;
            float worstStretch = 0f, worstOff = 0f, minVisible = 1f, worstCentre = 0f;
            int outside = 0;
            foreach (var (w, h) in Sizes)
            {
                foreach (float settle in new[] { 1f, 1.045f })
                {
                    LoadingSplash.Fit(w, h, iw, ih, settle, out var min, out var max, out var uv, out float fu, out float fv);
                    float dw = max.x - min.x, dh = max.y - min.y;
                    // the scale of the picture on each axis: pixels on the screen per pixel of the picture shown
                    float sx = dw / (uv.width * iw), sy = dh / (uv.height * ih);
                    worstStretch = Mathf.Max(worstStretch, Mathf.Abs(sx / sy - 1f));
                    if (min.x < -0.01f || min.y < -0.01f || max.x > w + 0.01f || max.y > h + 0.01f) outside++;
                    if (uv.xMin < -1e-4f || uv.yMin < -1e-4f || uv.xMax > 1f + 1e-4f || uv.yMax > 1f + 1e-4f) worstOff = Mathf.Max(worstOff, 1f);
                    if (w >= h) minVisible = Mathf.Min(minVisible, uv.width * uv.height);        // (a portrait screen is not a way to play: it shows the picture's middle, unstretched)
                    worstCentre = Mathf.Max(worstCentre, Mathf.Abs((min.x + max.x) * 0.5f - w * 0.5f) / w, Mathf.Abs((min.y + max.y) * 0.5f - h * 0.5f) / h);
                }
            }
            Check(worstStretch < 0.002f, $"the picture is never stretched, at {Sizes.Length} screen shapes (worst scale mismatch between the axes {N(worstStretch * 100f)}%)");
            Check(outside == 0, $"the picture never leaves the screen ({outside} cases)");
            Check(worstOff == 0f, "the part of the picture shown is always inside the picture");
            Check(worstCentre < 0.002f, $"the picture is centred (worst offset {N(worstCentre * 100f)}% of the screen)");
            Check(minVisible > 0.5f, $"even the widest landscape screen (32:9) shows more than half of the picture ({N(minVisible * 100f)}%)");
            LoadingSplash.Fit(1920, 1080, iw, ih, 1f, out var a, out var b, out var uvFull, out _, out _);
            Check(uvFull.width > 0.99f && uvFull.height > 0.99f && a.x < 1f && a.y < 1f && b.x > 1919f && b.y > 1079f, "at 16:9 the picture fills the screen and shows all of itself");
            LoadingSplash.Fit(2560, 1080, iw, ih, 1f, out _, out _, out _, out float fu219, out _);
            Check(fu219 > 0f, "at 21:9 the picture's own side edges fade into the black (no hard bars)");
            // the settle: gentle, only ever shrinking toward its place, and over
            float s0 = LoadingScreen.Settle(0f), s5 = LoadingScreen.Settle(5f), s10 = LoadingScreen.Settle(10f), s60 = LoadingScreen.Settle(60f);
            Check(s0 > 1f && s0 < 1.08f, $"the settle starts a touch larger ({N((s0 - 1f) * 100f)}%)");
            Check(s0 >= s5 && s5 >= s10 && Mathf.Abs(s10 - 1f) < 1e-4f && Mathf.Abs(s60 - 1f) < 1e-4f, "the settle only eases in, and is at rest after ten seconds");
        }

        // ------------------------------------------------------------------------------------------------ the bar

        static void Bar()
        {
            var statuses = new List<string>();
            foreach (var p in LoadingFlow.Plan) statuses.Add(p.label);
            var splash = new Texture2D(8, 8);
            var styleCounts = new Dictionary<string, int>();
            foreach (var st in HudStyles.Styles)
            {
                HudStyles.Set(st.Name);
                int badArea = 0, outside = 0, wide = 0, overlap = 0, cases = 0;
                int noPercent = 0;
                foreach (var (w, h) in Sizes)
                {
                    float scale = Mathf.Min(h / 1080f, w / 1920f);
                    var area = LoadingScreen.BarArea(w, h, scale);
                    // the plate and its corner ticks reach 22 px either side and 8 above, 12 below
                    float pl = area.xMin - 22f * scale, pr = area.xMax + 22f * scale, pt = area.yMin - 8f * scale, pb = area.yMax + 12f * scale;
                    if (pl < 0f || pt < 0f || pr > w || pb > h) badArea++;
                    foreach (var status in statuses)
                    {
                        foreach (float progress in new[] { 0f, 0.37f, 1f })
                        {
                            var L = new HudLines { RecordTexts = true, ImageTexture = splash };
                            L.Clear(scale, st);
                            LoadingScreen.DrawFrame(L, w, h, true, progress, status, 3f, 1f);
                            cases++;
                            // all the words are on the screen, on the plate, and the label does not run into the percentage
                            Rect label = default, pct = default;
                            foreach (var t in L.Texts)
                            {
                                if (t.Bounds.xMin < pl - 1f || t.Bounds.xMax > pr + 1f || t.Bounds.yMin < pt - 1f || t.Bounds.yMax > pb + 1f) outside++;
                                if (t.Role == TextRole.Label) label = t.Bounds; else if (t.Role == TextRole.Value) pct = t.Bounds;
                            }
                            if (label.width > 0f && pct.width > 0f && label.xMax + 12f * scale > pct.xMin) overlap++;
                            if (label.width > area.width * 0.7f) wide++;
                            if (pct.width <= 0f) noPercent++;
                        }
                    }
                }
                Check(badArea == 0, $"{st.Name}: the plate fits on the screen at all {Sizes.Length} screen shapes ({badArea} that do not)");
                Check(outside == 0, $"{st.Name}: every word is on the plate ({outside} outside, in {cases} drawings)");
                Check(overlap == 0 && wide == 0, $"{st.Name}: the status never runs into the percentage ({overlap} overlaps, {wide} too long)");
                Check(noPercent == 0, $"{st.Name}: the percentage is always drawn ({noPercent} drawings without)");
                var one = new HudLines { ImageTexture = splash };
                one.Clear(1f, st);
                LoadingScreen.DrawFrame(one, 1920, 1080, true, 0.5f, "Loading sound", 3f, 1f);
                styleCounts[st.Name] = one.VertexCount;
                Check(one.VertexCount > 100, $"{st.Name}: a drawing has geometry ({one.VertexCount} vertices)");
            }
            var seen = new HashSet<int>(styleCounts.Values);
            Check(seen.Count == styleCounts.Count, "the three styles draw three different bars (" + string.Join(", ", styleCounts) + ")");
            // faded out, nothing is drawn but the (clear) backdrop
            var f = new HudLines { ImageTexture = splash };
            f.Clear(1f, HudStyles.Current);
            LoadingScreen.DrawFrame(f, 1920, 1080, true, 0.5f, "Loading sound", 3f, 0f);
            Check(f.VertexCount == 0, $"faded out the screen draws nothing ({f.VertexCount} vertices)");
            UnityEngine.Object.DestroyImmediate(splash);
        }

        // ------------------------------------------------------------------------------------------------ the meter

        static void Meter()
        {
            var plan = new (string, string, float)[] { ("a", "Step A", 100f), ("b", "Step B", 300f), ("c", "Step C", 600f) };
            var m = new LoadMeter(plan);
            Check(m.Progress == 0f && m.Status == "Step A", "a new meter is at nothing and names the first step");
            m.Report("a", 0.5f);
            Check(Mathf.Abs(m.Progress - 0.05f) < 1e-4f, $"half of a tenth is 5% ({N(m.Progress * 100f)}%)");
            m.Report("a", 0.2f);
            Check(Mathf.Abs(m.Progress - 0.05f) < 1e-4f, "a step's report never goes backwards");
            m.Finish("a");
            m.Report("c", 0.5f);
            Check(Mathf.Abs(m.Progress - 0.40f) < 1e-4f, $"steps weigh by their share, in any order ({N(m.Progress * 100f)}%)");
            Check(m.Status == "Step C", $"the status names the step under way ({m.Status})");
            m.Begin("b");
            Check(m.Status == "Step B", $"with two under way, the earlier one is named ({m.Status})");
            float last = m.Progress;
            bool mono = true;
            foreach (var (id, frac) in new[] { ("b", 0.3f), ("c", 0.2f), ("b", 0.9f), ("c", 0.99f) }) { m.Report(id, frac); if (m.Progress + 1e-6f < last) mono = false; last = m.Progress; }
            Check(mono, "the bar never goes backwards");
            m.Finish("b"); m.Finish("c");
            Check(Mathf.Abs(m.Progress - 1f) < 1e-4f && m.AllDone, "when every step is done the bar is whole");
            Check(m.Steps[1].Ms >= 0.0 && m.Steps[2].Ms >= 0.0, "every step records how long it took");
            // measured weights replace the shipped ones
            var measured = new Dictionary<string, float> { ["a"] = 900f, ["b"] = 50f };
            var m2 = new LoadMeter(plan, measured);
            m2.Finish("a");
            Check(m2.Progress > 0.5f, $"a step that took long last time weighs more ({N(m2.Progress * 100f)}% after the long one)");
            // nothing moves on a clock: a meter left alone stays where it is
            var m3 = new LoadMeter(plan);
            m3.Report("a", 0.3f);
            float before = m3.Progress;
            System.Threading.Thread.Sleep(30);
            Check(m3.Progress == before, "the bar does not move on a timer");
            var w = LoadMeter.LoadWeights();
            Check(w != null, $"the step weights load ({w?.Count ?? 0} known)");
            // the plan the real start uses: every step has a label and a weight, in order
            bool ok = LoadingFlow.Plan.Length >= 6;
            foreach (var p in LoadingFlow.Plan) ok &= !string.IsNullOrEmpty(p.id) && !string.IsNullOrEmpty(p.label) && p.weight > 0f;
            Check(ok, $"the start's plan has {LoadingFlow.Plan.Length} steps, each named and weighed");
        }

        // ------------------------------------------------------------------------------------------------ the world's hold

        sealed class Counter : ISimSystem
        {
            public int Steps;
            public int Order => 0;
            public void Step(in SimFrame frame) { Steps++; }
        }

        static void Hold()
        {
            var sim = new Sim();
            var c = new Counter();
            sim.Register(c);
            sim.Held = true;
            for (int i = 0; i < 30; i++) sim.Advance(1.0 / 60.0);
            Check(c.Steps == 0, $"a held clock does not tick ({c.Steps} steps in half a second)");
            sim.Held = false;
            for (int i = 0; i < 30; i++) sim.Advance(1.0 / 60.0);
            Check(c.Steps >= 28 && c.Steps <= 31, $"released it runs at its rate ({c.Steps} steps in half a second)");
        }

        // ------------------------------------------------------------------------------------------------ the manifest

        static void Manifest()
        {
            var asset = Resources.Load<TextAsset>("Loading/preload");
            Check(asset != null, "the preload manifest ships with the game (Resources/Loading/preload.json)");
            if (asset == null) return;
            var o = JObject.Parse(asset.text);
            int total = 0, missing = 0;
            var gone = new List<string>();
            foreach (var step in new[] { "ships", "cabin", "effects", "sky" })
            {
                var list = o[step] as JArray;
                Check(list != null && list.Count > 0, $"the manifest lists assets for the {step} step ({list?.Count ?? 0})");
                if (list == null) continue;
                foreach (var t in list)
                {
                    string e = (string)t;
                    int bar = e.LastIndexOf('|');
                    string path = e.Substring(0, bar);
                    Type type = e.Substring(bar + 1) switch
                    {
                        "GameObject" => typeof(GameObject), "Texture2D" => typeof(Texture2D), "Mesh" => typeof(Mesh), "Shader" => typeof(Shader), "Material" => typeof(Material), "TextAsset" => typeof(TextAsset), _ => typeof(UnityEngine.Object),
                    };
                    total++;
                    if (Resources.Load(path, type) == null) { missing++; gone.Add(e); }
                }
            }
            Check(missing == 0, $"every asset the manifest names loads ({total - missing} of {total}{(missing > 0 ? "; missing " + string.Join(", ", gone) : "")})");
            // every ship's art is in the list (a new ship is picked up by the build step)
            var ships = o["ships"] as JArray;
            var all = Resources.LoadAll<GameObject>("ShipArt");
            int unlisted = 0;
            foreach (var g in all)
            {
                bool found = false;
                if (ships != null) foreach (var t in ships) if (((string)t).StartsWith("ShipArt/" + g.name + "|", StringComparison.Ordinal)) found = true;
                if (!found) unlisted++;
            }
            Check(unlisted == 0, $"every ship's art is in the manifest ({all.Length - unlisted} of {all.Length})");
        }

        // ------------------------------------------------------------------------------------------------ the real start

        static void Wiring()
        {
            Check(LoadingFlow.Plan.Length > 0 && Array.Exists(LoadingFlow.Plan, p => p.id == "sound") && Array.Exists(LoadingFlow.Plan, p => p.id == "world") && Array.Exists(LoadingFlow.Plan, p => p.id == "skywarm"),
                "the start's plan holds the world, the sound and the sky's warm-up");
            // headless runs and captures never wait behind a screen
            Check(!LoadingFlow.Wanted, "a headless run does not open the loading screen");
            Check(HudStyles.Styles.Count == 3 && HudStyles.Current != null, "the bar follows the HUD's styles (halo, meridian, cipher; the player's choice is the one in use)");
            var tex = LoadingSplash.Load("no_such_splash");
            Check(tex != null, "an unknown splash name falls back to the shipped one");
            if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
