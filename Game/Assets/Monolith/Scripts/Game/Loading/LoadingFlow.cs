using System.Collections;
using System.Collections.Generic;
using Monolith.Audio;
using Monolith.Cabin;
using Monolith.Core;
using Monolith.Juice;
using Monolith.Loading;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Monolith.Game
{
    /// <summary>
    /// How a played start goes: the loading screen first (from the very first frame the game can draw), then the same world the game always built, put up in steps and warmed up behind the
    /// screen, then a short fade into it. Every step is real work with a real count: the assets read (an <see cref="ResourceRequest"/>'s progress), the world built, the sound decoded (files
    /// done of files to do), the sky's wide look round (frames drawn), the cabin shown once (frames), the picture settling (frames until they are even). The world's clock is held until
    /// the fade starts, so nothing in the mission happens behind the screen; the pad and keyboard are switched off until then, so nothing is pressed behind it either.
    /// Skipped for captures, checks and headless runs, and with <c>-mnNoLoadingScreen</c> (the old start, for comparing).
    /// </summary>
    public static class LoadingFlow
    {
        /// <summary>The runs that check or photograph the game by driving it from the command line (their clocks and their pictures are not to be held behind a screen).</summary>
        static readonly string[] Drivers = { "-mnCapture", "-mnNoLoadingScreen", "-mnJuiceSelfTest", "-mnPanelShot", "-mnRumbleTest", "-mnPanelDrive", "-mnPadDrive", "-mnDeathTest" };

        public static bool Wanted
        {
            get
            {
                if (Application.isBatchMode) return false;
                foreach (var f in Drivers) if (CommandLine.Has(f)) return false;
                return true;
            }
        }

        /// <summary>The steps, in order, with the ids the meter keys on, what the status line says, and a weight to start from (the measured ones replace these).</summary>
        public static readonly (string id, string label, float weight)[] Plan =
        {
            ("start", "Starting up", 40f),
            ("ships", "Loading ships", 3400f),
            ("cabin", "Loading the cabin", 120f),
            ("effects", "Loading effects", 300f),
            ("sky", "Loading the sky", 40f),
            ("world", "Building the world", 900f),
            ("deathcut", "Preparing the cabin", 850f),
            ("sound", "Loading sound", 4850f),
            ("skywarm", "Warming up the sky", 25f),
            ("cabinwarm", "Warming up the cabin", 45f),
            ("settle", "Settling", 85f),
        };

        static readonly List<InputDevice> Switched = new List<InputDevice>();

        public static IEnumerator Run(GameRoot root)
        {
            var screen = LoadingScreen.Show(null);
            var meter = new LoadMeter(Plan, LoadMeter.LoadWeights());
            screen.Meter = meter;
            var oldPriority = Application.backgroundLoadingPriority;
            Application.backgroundLoadingPriority = ThreadPriority.High;      // let asset loads take most of a frame while a screen is up
            SwitchOffInput();
            bool released = false;
            try
            {
                yield return null;                                             // two frames: the screen is on the display before any real work begins
                yield return null;
                StartupProbe.Mark("loading screen on display");
                meter.Finish("start");

                // ---- the sound decodes from here on, alongside everything else (it is the longest single piece of work)
                var sound = AudioRig.BeginPreload(root);
                if (sound != null) { meter.Begin("sound"); root.StartCoroutine(TrackSound(meter, sound)); }

                // ---- real asset loads, off the build's critical path
                yield return Preload(meter, "ships", Manifest("ships"));
                yield return Preload(meter, "cabin", Manifest("cabin"));
                yield return Preload(meter, "effects", Manifest("effects"));
                yield return Preload(meter, "sky", Manifest("sky"));
                StartupProbe.Mark("assets loaded");

                // ---- the world, built in one go (the status is on the display first)
                meter.Begin("world");
                yield return null;
                bool built = true;
                try { root.BuildPlay(); }
                catch (System.Exception e) { built = false; MnLog.Error("LOADING the world could not be built: " + e); }
                if (!built) yield break;                                       // (the finally below takes the screen down and gives the pad back, so a broken mission is not hidden behind it)
                root.Sim.Held = true;
                if (root.World.MainCamera != null) screen.AttachTo(root.World.MainCamera);
                meter.Finish("world");
                Application.backgroundLoadingPriority = oldPriority;           // the build is done: frames can be quick again for the rest
                var death = Object.FindObjectOfType<JuiceDirector>()?.Death;   // the cabin is cut into pieces ahead of time, a part a frame: do it now, behind the screen
                death?.PrepareSoon();

                // ---- warm it up behind the screen: the sound decodes, the sky is looked at all round, the cabin is shown once, the frames even out
                yield return WaitForSound(meter, sound);
                StartupProbe.Mark("sound ready");
                yield return WarmDeath(meter, death);
                yield return WarmSky(meter);
                yield return WarmCabin(meter);
                yield return Settle(meter);
                StartupProbe.Mark("warmed up");

                // ---- into the mission: the bar is seen full, then the sim and the pad are given back as the screen fades
                meter.SaveStats();
                yield return screen.FillUp(0.7f);
                Held.Clear();
                root.Sim.Held = false;
                SwitchOnInput();
                released = true;
                StartupProbe.Flyable();
                yield return screen.FadeOut(CommandLine.GetFloat("-mnLoadingFade", 0.55f));
                MnLog.Info($"LOADING done in {meter.ElapsedMs:0} ms: " + string.Join(", ", System.Array.ConvertAll(Plan, p => p.id + " " + Ms(meter, p.id))));
            }
            finally
            {
                Application.backgroundLoadingPriority = oldPriority;
                if (!released) { SwitchOnInput(); if (root.World != null) root.Sim.Held = false; }
                if (LoadingScreen.Current != null) LoadingScreen.Current.Close();
            }
        }

        static string Ms(LoadMeter m, string id)
        {
            foreach (var s in m.Steps) if (s.Id == id) return s.Ms.ToString("0") + " ms";
            return "?";
        }

        // ------------------------------------------------------------------------------------------------ the assets

        /// <summary>The assets a step reads, as "Resources path|Type" (a manifest the build writes: see Editor/LoadingManifest), or none.</summary>
        static List<string> Manifest(string step)
        {
            var list = new List<string>();
            var asset = Resources.Load<TextAsset>("Loading/preload");
            if (asset == null) return list;
            try
            {
                var o = Newtonsoft.Json.Linq.JObject.Parse(asset.text);
                if (o[step] is Newtonsoft.Json.Linq.JArray a) foreach (var t in a) list.Add((string)t);
            }
            catch (System.Exception e) { MnLog.Warn("LOADING the preload manifest is not readable: " + e.Message); }
            return list;
        }

        static System.Type TypeOf(string name)
        {
            switch (name)
            {
                case "GameObject": return typeof(GameObject);
                case "Texture2D": return typeof(Texture2D);
                case "Mesh": return typeof(Mesh);
                case "Shader": return typeof(Shader);
                case "Material": return typeof(Material);
                case "TextAsset": return typeof(TextAsset);
                default: return typeof(Object);
            }
        }

        /// <summary>Held until the build is done: the assets the steps ask for, so building the world finds them already in memory.</summary>
        static readonly List<Object> Held = new List<Object>();

        /// <summary>Load the assets of a step one after another, asynchronously, the screen drawing between them; the step's fraction is assets done (and the one in flight's own progress) of assets to do.</summary>
        static IEnumerator Preload(LoadMeter meter, string step, List<string> entries)
        {
            meter.Begin(step);
            for (int i = 0; i < entries.Count; i++)
            {
                string e = entries[i];
                int bar = e.LastIndexOf('|');
                string path = bar < 0 ? e : e.Substring(0, bar);
                var type = bar < 0 ? typeof(Object) : TypeOf(e.Substring(bar + 1));
                var req = Resources.LoadAsync(path, type);
                while (!req.isDone)
                {
                    meter.Report(step, (i + req.progress) / entries.Count);
                    yield return null;
                }
                if (req.asset != null) Held.Add(req.asset);
                else MnLog.Warn("LOADING could not preload " + e);
                meter.Report(step, (i + 1f) / entries.Count);
            }
            meter.Finish(step);
        }

        // ------------------------------------------------------------------------------------------------ the warm-ups

        /// <summary>Report the decode's progress (files done of files to do) every frame while it runs, whatever else the load is doing.</summary>
        static IEnumerator TrackSound(LoadMeter meter, AudioLibrary lib)
        {
            while (!lib.Ready)
            {
                meter.Report("sound", lib.FilesTotal > 0 ? Mathf.Min(0.999f, (float)lib.FilesLoaded / lib.FilesTotal) : 0f);
                yield return null;
            }
        }

        /// <summary>Wait for the sound to be decoded (it has been since the load began, so this is usually over already), then end the step.</summary>
        static IEnumerator WaitForSound(LoadMeter meter, AudioLibrary lib)
        {
            lib = lib ?? AudioSystem.Active?.Lib;
            float t0 = Time.unscaledTime;
            while (lib != null && !lib.Ready && Time.unscaledTime - t0 < 30f)
            {
                meter.Report("sound", lib.FilesTotal > 0 ? Mathf.Min(0.999f, (float)lib.FilesLoaded / lib.FilesTotal) : 0f);
                yield return null;
            }
            meter.Finish("sound");
        }

        /// <summary>The cabin cut (the pieces its death tears into): a part of the model a frame, so it takes as many frames as it has parts; the screen waits for it.</summary>
        static IEnumerator WarmDeath(LoadMeter meter, DeathFx death)
        {
            meter.Begin("deathcut");
            int frames = 0;
            while (death != null && !death.Prepared && frames < 600)
            {
                frames++;
                meter.Report("deathcut", death.PrepareProgress);
                yield return null;
            }
            meter.Finish("deathcut");
        }

        static IEnumerator WarmSky(LoadMeter meter)
        {
            meter.Begin("skywarm");
            int frames = 0;
            while ((SkyWarmUp.Pending > 0 || frames < 2) && frames < 120)
            {
                frames++;
                meter.Report("skywarm", Mathf.Clamp01(frames / (float)(SkyWarmUp.Frames + 2)));
                yield return null;
            }
            yield return null;
            meter.Finish("skywarm");
        }

        static IEnumerator WarmCabin(LoadMeter meter)
        {
            meter.Begin("cabinwarm");
            var rig = CabinRig.Current;
            if (rig != null)
            {
                const int Frames = 4;
                rig.WarmShow = true;
                for (int i = 0; i < Frames; i++) { meter.Report("cabinwarm", (i + 1f) / Frames); yield return null; }
                rig.WarmShow = false;
                yield return null;
            }
            meter.Finish("cabinwarm");
        }

        /// <summary>Wait for the frames to even out (the first draws of everything are over) behind the screen: a few quick frames in a row.</summary>
        static IEnumerator Settle(LoadMeter meter)
        {
            meter.Begin("settle");
            int calm = 0, frames = 0;
            const int Need = 12;
            while (calm < Need && frames < 240)
            {
                yield return null;
                frames++;
                float ms = Time.unscaledDeltaTime * 1000f;
                calm = ms < 40f ? calm + 1 : 0;
                meter.Report("settle", Mathf.Max(calm / (float)Need, 0f));
            }
            meter.Finish("settle");
        }

        // ------------------------------------------------------------------------------------------------ nothing is pressed behind the screen

        static void SwitchOffInput()
        {
            Switched.Clear();
            foreach (var d in InputSystem.devices)
                if (d.enabled && (d is Gamepad || d is Keyboard || d is Mouse || d is Joystick)) { InputSystem.DisableDevice(d); Switched.Add(d); }
        }

        static void SwitchOnInput()
        {
            foreach (var d in Switched) if (d != null && d.added && !d.enabled) InputSystem.EnableDevice(d);
            Switched.Clear();
        }
    }
}
