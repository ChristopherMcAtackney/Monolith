using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Monolith.Audio;
using Monolith.Core;
using Monolith.HudUi;
using Monolith.Start;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Profiling;

namespace Monolith.Game
{
    /// <summary>
    /// The start screen's checks: <c>Monolith.exe [-batchmode -nographics] -mnStartSelfTest [-mnOut dir] [-mnStartTour oort:Hard:pause,flight::pause,...] [-mnNoLoadingScreen]</c>. First the logic with no devices
    /// (what the screen lists by display name, including a Missions folder beside the game, the levels, every key, what is remembered, what a choice adds to the command line, which starts are plain). Then
    /// the real thing through a virtual Xbox pad and keyboard: the player's start goes to the start screen, a scenario and a difficulty are chosen, it launches, the player returns (the pause screen's
    /// quit, a mission's debrief, F10), chooses another, and so on round the tour, and at the end quits. After every return the numbers are taken: the scenario's world must be gone (no
    /// game objects, roots, event listeners or audio system left over; memory and object counts not growing from one round trip to the next) and the same scenario launched twice must build the same
    /// world (the same sim systems and event listeners). Windowed it also shows the loading screens and measures the audio voices (<c>-mnAudioMute</c> keeps it quiet).
    /// </summary>
    public static class StartScreenSelfTest
    {
        const string Flag = "-mnStartSelfTest";
        public static bool Requested => CommandLine.Has(Flag);
        /// <summary>No loading screens between the screens (a windowed run of the checks that does not need to show them).</summary>
        public static bool NoLoading => CommandLine.Has("-mnStartNoLoading");

        static int _checks, _fails;
        static readonly List<string> Lines = new List<string>();

        static void Check(bool ok, string what)
        {
            _checks++;
            if (!ok) _fails++;
            string line = $"START {(ok ? "PASS" : "FAIL")}: {what}";
            Lines.Add(line);
            MnLog.Info(line);
        }

        static void Note(string what)
        {
            Lines.Add("START " + what);
            MnLog.Info("START " + what);
        }

        static string N(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        public static void Start(GameRoot root)
        {
            StartSettings.FileOverride = Path.Combine(Path.GetTempPath(), "mn_start_test_" + System.Diagnostics.Process.GetCurrentProcess().Id + "_start.json");
            StartSettings.AllowBatch = true;
            root.StartCoroutine(Run(root));
        }

        static IEnumerator Run(GameRoot root)
        {
            _checks = 0; _fails = 0; Lines.Clear();
            bool threw = false;
            try { Logic(); }
            catch (Exception e) { threw = true; _fails++; Lines.Add("START FAIL: the logic checks threw " + e.Message); MnLog.Error("START selftest threw: " + e); }
            var tour = Flow(root);
            while (true)
            {
                bool more;
                try { more = tour.MoveNext(); }
                catch (Exception e) { threw = true; _fails++; Lines.Add("START FAIL: the flow checks threw " + e.Message); MnLog.Error("START selftest threw: " + e); break; }
                if (!more) break;
                yield return tour.Current;
            }
            string summary = $"START SELFTEST {(_fails == 0 && !threw ? "OK" : "FAILED")}: {_checks - _fails} of {_checks} checks pass";
            MnLog.Info(summary);
            Lines.Add(summary);
            string dir = CommandLine.Get("-mnOut");
            if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); File.WriteAllLines(Path.Combine(dir, "start_selftest.txt"), Lines); }
            try { if (File.Exists(StartSettings.FilePath)) File.Delete(StartSettings.FilePath); } catch (Exception) { /* a temp file */ }
            StartFlow.QuitHook = null;
            Application.Quit(_fails == 0 && !threw ? 0 : 1);
        }

        // ------------------------------------------------------------------------------------------------ the logic, with no devices

        static void Logic()
        {
            string dir = Path.Combine(Path.GetTempPath(), "mn_start_logic_" + System.Diagnostics.Process.GetCurrentProcess().Id);
            string missions = Path.Combine(dir, "Missions");
            try
            {
                Directory.CreateDirectory(missions);

                // what it lists: the game's own missions by the name written in the file, in the order of the design
                var builtIn = StartCatalog.Missions(Path.Combine(dir, "nothing here"));
                var names = new List<string>();
                foreach (var m in builtIn) names.Add(m.Name);
                Check(string.Join("|", names) == "The Dogfight|The Oculus|The Crossing|Dead Water|Jupiter Skirmish (test)", $"the missions are listed by their display names, in order: {string.Join(", ", names)}");
                bool blurbs = true;
                foreach (var m in builtIn) if (string.IsNullOrEmpty(m.Detail) || m.Detail.Length > 300) blurbs = false;
                Check(blurbs, "each mission has a line from its own briefing to read (two sentences at most)");

                // a Missions folder beside the game: an extra mission is listed after the game's, a file with a built-in's name replaces it, a broken file is listed with what is wrong
                File.WriteAllText(Path.Combine(missions, "my_extra.mission.json"), "{ \"format\": \"monolith-mission/1\", \"id\": \"my_extra\", \"name\": \"The Test Run\", \"briefing\": { \"title\": \"x\", \"stages\": [\"A mission someone wrote. It has two sentences. And a third.\"] } }");
                File.WriteAllText(Path.Combine(missions, "oort.mission.json"), "{ \"name\": \"The Oculus (my edit)\", \"briefing\": { \"stages\": [\"Edited.\"] } }");
                File.WriteAllText(Path.Combine(missions, "broken.mission.json"), "{ this is not json");
                var beside = StartCatalog.Missions(missions);
                var bn = new List<string>();
                foreach (var m in beside) bn.Add(m.Id + "=" + m.Name);
                Check(beside.Count == 7 && beside[0].Name == "The Dogfight" && beside[1].Name == "The Oculus (my edit)" && beside[1].Beside, $"a file in the Missions folder replaces the built-in mission of its name, in its place: {string.Join(", ", bn)}");
                Check(beside.Exists(m => m.Name == "The Test Run" && m.Detail == "A mission someone wrote. It has two sentences."), "a mission added in the Missions folder is listed by its display name with the first two sentences of its briefing");
                Check(beside.Exists(m => m.Id == "broken" && m.Detail != null && m.Detail.StartsWith("This file could not be read")), "a broken file is listed by its file name and says it cannot be read");

                // the whole menu
                var menu = StartCatalog.Build(missions, useSaved: false);
                int missionsN = 0, sandboxN = 0, quitN = 0;
                foreach (var e in menu.Entries) { if (e.Kind == EntryKind.Mission) missionsN++; else if (e.Kind == EntryKind.Sandbox) sandboxN++; else quitN++; }
                Check(missionsN == 7 && sandboxN == 3 && quitN == 1 && menu.Entries[menu.Entries.Count - 1].Kind == EntryKind.Quit, $"the screen lists {missionsN} missions, {sandboxN} sandboxes and a quit row, quit last");
                var sb = new List<string>();
                foreach (var e in menu.Entries) if (e.Kind == EntryKind.Sandbox) sb.Add(e.Title);
                Check(string.Join("|", sb) == "Free flight|Combat range|Explosions sandbox", $"the sandboxes are free flight, the combat range and the explosions sandbox ({string.Join(", ", sb)})");
                Check(string.Join("|", menu.Levels) == "Very easy|Easy|Medium|Hard|Insane" && menu.LevelName == "Medium", $"the levels are {string.Join(", ", menu.Levels)}, and Medium is the one chosen to begin with");
                Check(menu.Stat.Count == 5 && Mathf.Approximately(menu.Stat[0], 0.25f) && Mathf.Approximately(menu.Stat[4], 1f), $"the second gauge reads the AI table's damage to the player: {string.Join(", ", menu.Stat.ConvertAll(v => N(v)))}");

                // every key, in a menu of the plain game's shape
                var m1 = StartCatalog.Build(Path.Combine(dir, "nothing here"), useSaved: false);
                bool ok = m1.Index == 0 && !m1.LevelFocus;
                ok &= m1.Press(MenuKey.Down) == MenuResult.None && m1.Index == 1;
                ok &= m1.Press(MenuKey.Up) == MenuResult.None && m1.Press(MenuKey.Up) == MenuResult.None && m1.Index == m1.QuitIndex;
                ok &= m1.Press(MenuKey.Down) == MenuResult.None && m1.Index == 0;
                Check(ok, "up and down move through the scenarios and wrap round (up from the first lands on quit)");
                m1.Index = 1;
                ok = m1.Press(MenuKey.Accept) == MenuResult.None && m1.LevelFocus;
                ok &= m1.Press(MenuKey.Down) == MenuResult.None && m1.Level == 3;
                ok &= m1.Press(MenuKey.Up) == MenuResult.None && m1.Press(MenuKey.Up) == MenuResult.None && m1.Level == 1;
                ok &= m1.Press(MenuKey.Left) == MenuResult.None && !m1.LevelFocus && m1.Level == 1;
                ok &= m1.Press(MenuKey.Right) == MenuResult.None && m1.LevelFocus;
                ok &= m1.Press(MenuKey.Back) == MenuResult.None && !m1.LevelFocus;
                Check(ok, "A on a mission moves the cursor to the levels; up and down and left, right and B move between and within the lists");
                m1.Index = 1; m1.SelectLevel("Hard");
                m1.Press(MenuKey.Accept);
                var launch = m1.Press(MenuKey.Accept);
                Check(launch == MenuResult.Launch && m1.Chosen.Id == "oort" && m1.LevelName == "Hard", $"A on a level launches the mission ({m1.Chosen?.Id} at {m1.LevelName})");
                m1.LevelFocus = false; m1.Index = m1.Entries.FindIndex(e => e.Id == "flight");
                launch = m1.Press(MenuKey.Accept);
                Check(launch == MenuResult.Launch && m1.Chosen.Id == "flight" && !m1.LevelApplies && m1.Press(MenuKey.Right) == MenuResult.None && !m1.LevelFocus, "A on a sandbox launches it at once, and a sandbox has no level to move to");
                m1.Index = 0;
                ok = m1.Press(MenuKey.Back) == MenuResult.None && m1.Index == m1.QuitIndex;
                ok &= m1.Press(MenuKey.Back) == MenuResult.Quit;
                m1.Index = m1.QuitIndex;
                ok &= m1.Press(MenuKey.Accept) == MenuResult.Quit;
                Check(ok, "B from the scenarios goes to the quit row, B or A there quits");

                // what is drawn from it
                var hm = new HudMenu();
                m1.LevelFocus = false; m1.Index = 0; m1.Build(hm); m1.Sync(hm);
                Check(hm.Rows.Count == 11 && hm.Rows[0].Heading && hm.Rows[0].Label == "Missions" && hm.Rows[6].Heading && hm.Rows[6].Label == "Sandboxes" && hm.Rows[10].Quit && hm.Selected == 1, $"the HUD's menu data has two headings, the entries and the quit row ({hm.Rows.Count} rows, the cursor on row {hm.Selected})");
                m1.Index = 5; m1.Sync(hm);
                Check(hm.Selected == 7 && !hm.LevelApplies && hm.Description == m1.Selected.Detail, "the cursor's row follows past the heading, and a sandbox says the levels do not apply");

                // what is remembered, beside the game
                StartSettings.Save("oort", "Hard");
                StartSettings.Load(out string sc, out string lv);
                Check(sc == "oort" && lv == "Hard", "the last scenario and difficulty are written to start.json and read back");
                var m2 = StartCatalog.Build(missions, useSaved: true);
                Check(m2.Selected.Id == "oort" && m2.LevelName == "Hard", "the screen opens with the cursor on the last scenario and level");
                File.WriteAllText(StartSettings.FilePath, "{ nonsense");
                StartSettings.Load(out sc, out lv);
                var m3 = StartCatalog.Build(missions, useSaved: true);
                Check(sc == null && lv == null && m3.LevelName == "Medium" && m3.Index == 0, "an unreadable start.json means the first scenario and Medium");
                File.WriteAllText(StartSettings.FilePath, "{ \"scenario\": \"gone\", \"difficulty\": \"Impossible\" }");
                var m4 = StartCatalog.Build(missions, useSaved: true);
                Check(m4.Index == 0 && m4.LevelName == "Medium", "a remembered scenario that no longer exists, or a level that is not one, is ignored");

                // what a choice adds to the command line, and that the real flags are still read
                Check(string.Join(" ", StartCatalog.FlagsFor(m1.Entries[1], "Hard")) == "-mnMission oort -mnDifficulty Hard" && string.Join(" ", StartCatalog.FlagsFor(m1.Entries[5], "Hard")) == "-mnScene flight", "a mission launches as -mnMission and -mnDifficulty, a sandbox as -mnScene");
                var wasArgs = Environment.GetCommandLineArgs();
                try
                {
                    CommandLine.SetArgs(new[] { "Monolith.exe", "-mnDifficulty", "Easy", "-mnSkipBriefing" });
                    CommandLine.SetOverlay("-mnMission", "oort", "-mnDifficulty", "Hard");
                    Check(CommandLine.Get("-mnMission") == "oort" && CommandLine.Get("-mnDifficulty") == "Hard" && CommandLine.Has("-mnSkipBriefing"), "the flags a launch adds are read before the real ones, and the real ones still apply");
                    CommandLine.ClearOverlay();
                    Check(CommandLine.Get("-mnMission") == null && CommandLine.Get("-mnDifficulty") == "Easy", "taking them off leaves the command line as it was");
                    CommandLine.SetArgs(new[] { "Monolith.exe" });
                    Check(StartFlow.IsPlain(false) && !StartFlow.IsPlain(true), "a plain launch (a window, no flag) goes to the start screen; a headless one never does");
                    bool allSkip = true;
                    foreach (var f in new[] { "-mnMission oort", "-mnScene flight", "-mnCapture plan.json", "-mnPadDrive", "-mnDeathTest", "-mnStartupProbe 10", "-mnNoStartScreen" })
                    {
                        var a = new List<string> { "Monolith.exe" };
                        a.AddRange(f.Split(' '));
                        CommandLine.SetArgs(a.ToArray());
                        if (StartFlow.IsPlain(false)) { allSkip = false; MnLog.Info("START detail: '" + f + "' is taken for a plain launch"); }
                    }
                    Check(allSkip, "a mission, a scene, a capture, a test flag or -mnNoStartScreen on the command line skips the start screen");
                    CommandLine.SetArgs(new[] { "Monolith.exe", "-mnDifficulty", "Hard", "-mnNoLoadingScreen", "-mnAudioLog", "-mnHudStyle", "cipher" });
                    Check(StartFlow.IsPlain(false), "difficulty, the loading screen, audio and HUD style flags do not stop a launch being plain");
                    CommandLine.SetArgs(new[] { "Monolith.exe", "-mnDifficulty", "Hard" });
                    Check(StartCatalog.Build(missions, useSaved: false).LevelName == "Hard", "-mnDifficulty on a plain launch chooses the level the screen starts on");
                }
                finally { CommandLine.SetArgs(wasArgs); CommandLine.ClearOverlay(); }
            }
            finally
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception) { /* a temp folder */ }
                try { if (File.Exists(StartSettings.FilePath)) File.Delete(StartSettings.FilePath); } catch (Exception) { /* a temp file */ }
            }
        }

        // ------------------------------------------------------------------------------------------------ the numbers

        sealed class Snap
        {
            public long Mono, Total, Gfx;
            public int Objects, GameObjects, Roots, Listeners;
            public Dictionary<string, int> Types = new Dictionary<string, int>();
            public Dictionary<string, int> Names = new Dictionary<string, int>();
        }

        static Snap Take()
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var s = new Snap
            {
                Mono = Profiler.GetMonoUsedSizeLong(), Total = Profiler.GetTotalAllocatedMemoryLong(), Gfx = Profiler.GetAllocatedMemoryForGraphicsDriver(),
                GameObjects = UnityEngine.Object.FindObjectsOfType<GameObject>().Length, Roots = GameRoot.RootIds().Count, Listeners = GameEvents.SubscriberCount,
            };
            var all = Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
            s.Objects = all.Length;
            foreach (var o in all)
            {
                string n = o.GetType().Name;
                s.Types.TryGetValue(n, out int c);
                s.Types[n] = c + 1;
                string nn = n + ":" + o.name;
                s.Names.TryGetValue(nn, out int c2);
                s.Names[nn] = c2 + 1;
            }
            return s;
        }

        static string Mb(long bytes) => (bytes / 1048576f).ToString("0.0", CultureInfo.InvariantCulture);

        static string Growth(Snap from, Snap to, int top)
        {
            var list = new List<KeyValuePair<string, int>>();
            foreach (var kv in to.Types) { from.Types.TryGetValue(kv.Key, out int c); if (kv.Value != c) list.Add(new KeyValuePair<string, int>(kv.Key, kv.Value - c)); }
            list.Sort((a, b) => Math.Abs(b.Value).CompareTo(Math.Abs(a.Value)));
            var parts = new List<string>();
            for (int i = 0; i < list.Count && i < top; i++) parts.Add($"{list[i].Key} {(list[i].Value > 0 ? "+" : "")}{list[i].Value}");
            return parts.Count == 0 ? "none" : string.Join(", ", parts);
        }

        /// <summary>The objects that came since <paramref name="from"/>, by type and name (what a round trip left behind).</summary>
        static string NameGrowth(Snap from, Snap to, int top)
        {
            var list = new List<KeyValuePair<string, int>>();
            foreach (var kv in to.Names) { from.Names.TryGetValue(kv.Key, out int c); if (kv.Value > c) list.Add(new KeyValuePair<string, int>(kv.Key, kv.Value - c)); }
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            var parts = new List<string>();
            for (int i = 0; i < list.Count && i < top; i++) parts.Add($"{list[i].Key} +{list[i].Value}");
            return parts.Count == 0 ? "none" : string.Join(", ", parts);
        }

        // ------------------------------------------------------------------------------------------------ the pad and the keyboard

        static IEnumerator Wait(float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds) yield return null;
        }

        static IEnumerator WaitFor(Func<bool> cond, float seconds, string what)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!cond() && Time.realtimeSinceStartup - t0 < seconds) yield return null;
            Check(cond(), $"{what} (within {N(Time.realtimeSinceStartup - t0)} s)");
        }

        static IEnumerator Hold(Gamepad pad, float seconds, GamepadState state)
        {
            InputSystem.QueueStateEvent(pad, state);
            yield return Wait(seconds);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return Wait(0.09f);
        }

        static IEnumerator Tap(Gamepad pad, params GamepadButton[] buttons) => Hold(pad, 0.12f, new GamepadState(buttons));

        static IEnumerator Key(Keyboard kb, Key key)
        {
            InputSystem.QueueStateEvent(kb, new KeyboardState(key));
            yield return Wait(0.12f);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return Wait(0.09f);
        }

        /// <summary>One step in a list: by the D-pad, the left stick or the arrow keys.</summary>
        static IEnumerator Step(Gamepad pad, Keyboard kb, int mode, bool down)
        {
            if (mode == 0) yield return Tap(pad, down ? GamepadButton.DpadDown : GamepadButton.DpadUp);
            else if (mode == 1) yield return Hold(pad, 0.12f, new GamepadState { leftStick = new Vector2(0f, down ? -1f : 1f) });
            else yield return Key(kb, down ? UnityEngine.InputSystem.Key.DownArrow : UnityEngine.InputSystem.Key.UpArrow);
        }

        sealed class Trip { public string Id, Level, How; }

        static List<Trip> ParseTour()
        {
            string spec = CommandLine.Get("-mnStartTour", "oort:Hard:pause,flight::pause,dogfight:Easy:end,range::pause,juice::f10,cabin:Insane:pause,escort:Very easy:pause,oort:Hard:pause");
            var list = new List<Trip>();
            foreach (var part in spec.Split(','))
            {
                var f = part.Split(':');
                list.Add(new Trip { Id = f[0], Level = f.Length > 1 && f[1].Length > 0 ? f[1] : "Medium", How = f.Length > 2 && f[2].Length > 0 ? f[2] : "pause" });
            }
            return list;
        }

        // ------------------------------------------------------------------------------------------------ the tour

        static IEnumerator Flow(GameRoot root)
        {
            var oldBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            bool quitSeen = false;
            StartFlow.QuitHook = () => quitSeen = true;
            yield return WaitFor(() => StartFlow.State == StartFlow.Phase.Menu && StartScreen.Current != null, 180f, "a plain start comes to the start screen");
            if (StartScreen.Current == null) yield break;
            Note($"mode: {(Application.isBatchMode ? "headless" : "windowed")}, loading screens {(LoadingScreenShown() ? "shown" : "skipped")}, audio {(AudioSystem.Active != null ? "on" : "none at the start screen")}");
            Check(GameWorld.Current == null && root.World == null && AudioSystem.Active == null, "at the start screen there is no world, no clock and no audio system yet");
            var pad = InputSystem.AddDevice<Gamepad>("MnStartPad");
            var kb = InputSystem.AddDevice<Keyboard>("MnStartKeys");
            yield return Wait(0.5f);
            var baseline = Take();
            Note($"baseline at the start screen: {baseline.Objects} objects, {baseline.GameObjects} game objects, {baseline.Roots} roots, mono {Mb(baseline.Mono)} MB, allocated {Mb(baseline.Total)} MB, graphics driver {Mb(baseline.Gfx)} MB, {baseline.Listeners} event listeners");

            var tour = ParseTour();
            var last = baseline;
            var seen = new Dictionary<string, (int systems, int listeners)>();
            var ranBefore = new HashSet<string>();
            for (int i = 0; i < tour.Count; i++)
            {
                var trip = tour[i];
                var menu = StartScreen.Current.Menu;
                int target = menu.Entries.FindIndex(e => string.Equals(e.Id, trip.Id, StringComparison.OrdinalIgnoreCase));
                if (target < 0) { Check(false, $"trip {i + 1}: there is a scenario called '{trip.Id}'"); continue; }
                var entry = menu.Entries[target];
                int mode = i % 3;      // the D-pad, the left stick, the arrow keys, in turn
                string how = mode == 0 ? "D-pad" : mode == 1 ? "left stick" : "arrow keys";
                int n = menu.Entries.Count;

                // choose it: up or down to the scenario (the shorter way round), A, then the level, A
                int guard = 0;
                while (menu.Index != target && guard++ < 24)
                {
                    int fwd = ((target - menu.Index) % n + n) % n;
                    yield return Step(pad, kb, mode, fwd <= n / 2);
                }
                Check(menu.Index == target, $"trip {i + 1}: the {how} moves the cursor to {entry.Title}");
                if (entry.Kind == EntryKind.Mission)
                {
                    int lvl = menu.Levels.FindIndex(l => string.Equals(l, trip.Level, StringComparison.OrdinalIgnoreCase));
                    if (mode == 2) yield return Key(kb, UnityEngine.InputSystem.Key.Enter); else yield return Tap(pad, GamepadButton.South);
                    Check(menu.LevelFocus, $"trip {i + 1}: A (Enter) on a mission moves to the difficulty list");
                    guard = 0;
                    while (menu.Level != lvl && guard++ < 12) yield return Step(pad, kb, mode, lvl > menu.Level);
                    Check(menu.Level == lvl, $"trip {i + 1}: the cursor reaches {trip.Level}");
                }
                int phaseBefore = StartFlow.Launches;
                if (mode == 2) yield return Key(kb, UnityEngine.InputSystem.Key.Enter); else yield return Tap(pad, GamepadButton.South);
                Check(StartFlow.Launches == phaseBefore + 1, $"trip {i + 1}: A (Enter) launches {entry.Title}{(entry.Kind == EntryKind.Mission ? " at " + trip.Level : "")}");
                yield return WaitFor(() => StartFlow.State == StartFlow.Phase.Playing && GameWorld.Current != null && StartScreen.Current == null, 120f, $"trip {i + 1}: the scenario is built and playing");

                // is it what was chosen
                var w = GameWorld.Current;
                if (w != null)
                {
                    if (entry.Kind == EntryKind.Mission)
                    {
                        float expect = menu.Stat[menu.Levels.FindIndex(l => string.Equals(l, trip.Level, StringComparison.OrdinalIgnoreCase))];
                        Check(w.Mission != null && string.Equals(w.Mission.Id, entry.Id, StringComparison.OrdinalIgnoreCase) && Mathf.Approximately(w.PlayerDamageScale, expect),
                            $"trip {i + 1}: the mission running is {w.Mission?.Id} with the damage scale of {trip.Level} ({N(w.PlayerDamageScale)} = {N(expect)})");
                    }
                    else Check(w.LocalPlayerShip != GameWorld.NoShip && w.Mission == null, $"trip {i + 1}: the sandbox {entry.Id} is running with a player's ship and no mission");
                }

                // a few seconds of play, with the voices counted
                int peakVoices = 0;
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 2.5f) { var a = AudioSystem.Active; if (a != null) peakVoices = Math.Max(peakVoices, a.Mixer.ActiveVoices); yield return null; }
                int systems = root.World != null ? root.Sim.SystemCount : -1, listeners = GameEvents.SubscriberCount;
                string key = entry.Id + ":" + (entry.Kind == EntryKind.Mission ? trip.Level : "");
                string audio = AudioSystem.Active != null ? $"{peakVoices} voices at most" : "no audio in a headless run";
                Note($"trip {i + 1} ({entry.Title}) playing: {systems} sim systems, {listeners} event listeners, {audio}");
                if (seen.TryGetValue(key, out var before))
                    Check(before.systems == systems && before.listeners == listeners, $"trip {i + 1}: {entry.Title} built the same world as last time ({systems} sim systems and {listeners} listeners, then {before.systems} and {before.listeners})");
                else seen[key] = (systems, listeners);

                // back to the start screen: the pause screen's quit, a mission's debrief, or F10
                var scene = UnityEngine.Object.FindObjectOfType<MissionScene>();
                if (trip.How == "end" && scene != null && scene.Runtime != null)
                {
                    yield return Tap(pad, GamepadButton.South);          // the briefing's launch
                    yield return Wait(1.0f);
                    scene.Runtime.End("victory", "TEST");
                    yield return WaitFor(() => scene.Runtime.Ended, 10f, $"trip {i + 1}: the mission ends");
                    yield return Wait(0.6f);
                    yield return Tap(pad, GamepadButton.East);           // the debrief's B
                }
                else if (trip.How == "f10") yield return Key(kb, UnityEngine.InputSystem.Key.F10);
                else
                {
                    yield return Tap(pad, GamepadButton.Start);
                    yield return Wait(0.35f);
                    yield return Tap(pad, GamepadButton.East);
                }
                yield return WaitFor(() => StartFlow.State == StartFlow.Phase.Menu && StartScreen.Current != null, 60f, $"trip {i + 1}: back at the start screen ({trip.How})");
                yield return Wait(1.0f);
                var snap = Take();
                bool gone = GameWorld.Current == null && root.World == null && AudioSystem.Active == null && CommandLine.Get("-mnMission") == null && CommandLine.Get("-mnScene") == null && Mathf.Approximately(Time.timeScale, 1f);
                Check(gone, $"trip {i + 1}: no world, no audio system, the command line and the clock as they were");
                Check(snap.GameObjects == baseline.GameObjects && snap.Roots == baseline.Roots && snap.Listeners == baseline.Listeners,
                    $"trip {i + 1}: {snap.GameObjects} game objects, {snap.Roots} roots and {snap.Listeners} event listeners at the start screen (baseline {baseline.GameObjects}, {baseline.Roots}, {baseline.Listeners})");
                Note($"trip {i + 1} back: {snap.Objects} objects ({(snap.Objects - baseline.Objects >= 0 ? "+" : "")}{snap.Objects - baseline.Objects} on the baseline, {(snap.Objects - last.Objects >= 0 ? "+" : "")}{snap.Objects - last.Objects} on the last), mono {Mb(snap.Mono)} MB ({Mb(snap.Mono - baseline.Mono)} on the baseline), allocated {Mb(snap.Total)} MB ({Mb(snap.Total - baseline.Total)}), graphics driver {Mb(snap.Gfx)} MB ({Mb(snap.Gfx - baseline.Gfx)}); objects by type vs the baseline: {Growth(baseline, snap, 6)}");
                Note($"trip {i + 1}: objects that came since the last trip: {NameGrowth(last, snap, 10)}");
                // a scenario that has run before has nothing left to warm: what it leaves at the start screen is what the trip before left (the first time of each kind loads the caches the game keeps on purpose)
                if (ranBefore.Contains(key))
                    Check(snap.Objects <= last.Objects + 10 && snap.Mono <= last.Mono + 3 * 1048576 && snap.Total <= last.Total + 8 * 1048576,
                        $"trip {i + 1}: {entry.Title} again leaves nothing more behind than the trip before (objects {snap.Objects - last.Objects:+0;-0;0}, mono {Mb(snap.Mono - last.Mono)} MB, allocated {Mb(snap.Total - last.Total)} MB, graphics driver {Mb(snap.Gfx - last.Gfx)} MB)");
                ranBefore.Add(key);
                last.Names = null;          // (the names of the trip before are not kept: the check must not hold on to what it is counting)
                last = snap;
                Check(File.Exists(StartSettings.FilePath), $"trip {i + 1}: start.json is there");
            }

            // the last choice is remembered
            StartSettings.Load(out string sc, out string lv);
            var final = tour[tour.Count - 1];
            Check(string.Equals(sc, final.Id, StringComparison.OrdinalIgnoreCase), $"start.json remembers the last scenario ({sc})");

            // quit: B goes to the quit row, A quits
            yield return Tap(pad, GamepadButton.East);
            Check(StartScreen.Current != null && StartScreen.Current.Menu.Selected.Kind == EntryKind.Quit, "B at the start screen goes to the quit row");
            yield return Tap(pad, GamepadButton.South);
            yield return WaitFor(() => quitSeen, 10f, "A on the quit row quits");
            InputSystem.settings.backgroundBehavior = oldBackground;
            if (pad != null) InputSystem.RemoveDevice(pad);
            if (kb != null) InputSystem.RemoveDevice(kb);
        }

        static bool LoadingScreenShown() => !Application.isBatchMode && !NoLoading && !CommandLine.Has("-mnNoLoadingScreen");
    }
}
