using System;
using System.Collections;
using Monolith.Core;
using Monolith.Loading;
using Monolith.Start;
using UnityEngine;

namespace Monolith.Game
{
    /// <summary>
    /// How a plain start goes: the loading screen, then the start screen (scenario and difficulty), then the scenario behind the loading screen again, then, when a mission ends or the player quits
    /// from the pause screen, the start screen once more (the scenario's world is taken down first: <see cref="GameRoot.TearDownPlay"/>) and so on until Quit. A start with a mission or a scene on
    /// the command line, a capture or a check never comes here: it plays that and closes the game as it always did.
    /// </summary>
    public static class StartFlow
    {
        public enum Phase { None, Boot, Menu, Launching, Playing, Returning, Ended }

        public static Phase State { get; private set; }
        /// <summary>The player came by the start screen: a scene's Quit and a mission's end go back to it instead of closing the game.</summary>
        public static bool Active { get; private set; }
        /// <summary>How many scenarios have been launched (the checks).</summary>
        public static int Launches { get; private set; }
        public static string LastError { get; private set; }

        static bool _menuRequested;

        /// <summary>Flags that mean "this run is a check or a capture, or it names what to play": any of them and there is no start screen.</summary>
        static readonly string[] NotPlain = { "-mnMission", "-mnScene", "-mnCapture", "-mnStartupProbe", "-mnStartupDrive", "-mnNoStartScreen", "-mnJuiceSelfTest", "-mnPanelShot", "-mnRumbleTest", "-mnPanelDrive", "-mnPadDrive", "-mnDeathTest" };

        /// <summary>True for a plain launch (a window, no mission, scene, capture or test flag), or when the start screen's own check asks for it.</summary>
        public static bool Wanted => StartScreenSelfTest.Requested || IsPlain(Application.isBatchMode);

        /// <summary>A plain launch: not a headless run, and none of the flags that name what to play or that check or photograph the game.</summary>
        public static bool IsPlain(bool batchMode)
        {
            if (batchMode) return false;
            foreach (var f in NotPlain) if (CommandLine.Has(f)) return false;
            return true;
        }

        /// <summary>What the start screen's Quit does, when something else is to see it (the check: it ends the run itself); null closes the game.</summary>
        public static Action QuitHook;

        /// <summary>The word for what B does on a scene's pause, briefing and debrief screens: back to the start screen, or the end of the game.</summary>
        public static string QuitWord => Active ? "MENU" : "QUIT";

        /// <summary>What a scene does when the player quits (the pause screen's B, a mission's debrief): to the start screen when the player came from it, else the game closes.</summary>
        public static void QuitOrMenu(int exitCode = 0)
        {
            if (Active) RequestMenu();
            else Application.Quit(exitCode);
        }

        public static void RequestMenu()
        {
            if (Active && (State == Phase.Playing || State == Phase.Launching)) _menuRequested = true;
        }

        static bool Build(GameRoot root)
        {
            try { root.BuildPlay(); return true; }
            catch (Exception e) { LastError = e.Message; MnLog.Error("START the scenario could not be built: " + e); return false; }
        }

        public static IEnumerator Run(GameRoot root)
        {
            Active = true;
            State = Phase.Boot;
            bool loading = LoadingFlow.Wanted && !StartScreenSelfTest.NoLoading;
            var menu = StartCatalog.Build(useSaved: !StartScreenSelfTest.Requested);
            StartScreen screen = null;
            Camera MakeMenu() { screen = StartScreen.Create(menu, Application.isBatchMode); return screen.Camera; }

            if (loading) yield return LoadingFlow.RunBoot(root, MakeMenu);
            else MakeMenu();

            while (true)
            {
                State = Phase.Menu;
                StartupProbe.Mark("start screen ready");
                while (screen != null && screen.Result == MenuResult.None) yield return null;
                if (screen == null || screen.Result == MenuResult.Quit)
                {
                    State = Phase.Ended;
                    MnLog.Info("START the player quit from the start screen");
                    if (QuitHook != null) QuitHook(); else Application.Quit(0);
                    yield break;
                }

                var entry = menu.Chosen;
                string level = menu.LevelName;
                StartSettings.Save(entry.Id, entry.Kind == EntryKind.Mission ? level : menu.LevelName);
                State = Phase.Launching;
                Launches++;
                _menuRequested = false;
                CommandLine.SetOverlay(StartCatalog.FlagsFor(entry, level));
                MnLog.Info($"START launching {entry.Id}{(entry.Kind == EntryKind.Mission ? " at " + level : "")}");
                bool ok = false;
                if (loading)
                    yield return LoadingFlow.RunLaunch(root, () => Build(root), () => { if (screen != null) UnityEngine.Object.Destroy(screen.gameObject); }, b => { ok = b; if (b) State = Phase.Playing; });
                else
                {
                    if (screen != null) UnityEngine.Object.Destroy(screen.gameObject);
                    yield return null;
                    ok = Build(root);
                }
                screen = null;
                if (ok)
                {
                    State = Phase.Playing;
                    while (!_menuRequested) yield return null;
                    _menuRequested = false;
                }
                else MnLog.Error("START the scenario did not start; back to the start screen");

                // back: the start screen goes up first (its black camera covers the scenario), then the scenario's world is taken down behind it
                State = Phase.Returning;
                menu.LevelFocus = false;
                screen = StartScreen.Create(menu, Application.isBatchMode);
                yield return null;
                yield return null;
                yield return root.TearDownPlay(screen.gameObject);
            }
        }
    }
}
