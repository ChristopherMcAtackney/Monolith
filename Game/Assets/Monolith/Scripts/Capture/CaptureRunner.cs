using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Monolith.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace Monolith.Capture
{
    /// <summary>
    /// The built player's capture mode:  -mnCapture plan.json -mnOut dir [-mnOnly a,b] [-mnKind stills|perf]
    /// Runs the plan against the running game (the shots use GameWorld.MainCamera), writes PNGs, perf_*.json and
    /// capture_manifest.json into the output folder, then quits: exit 0 ok, 1 a shot or the self-test failed, 2 bad plan.
    /// Stills render offscreen at the exact resolution with fixed game time (one sim tick per frame), so a rerun gives
    /// the same picture. Perf runs play in real time on the window, vsync off.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public sealed class CaptureRunner : MonoBehaviour
    {
        sealed class Result
        {
            public string name, kind, status, file, camera, error;
            public int? width, height, simTick, logErrors;
            public float? warmup;
            public double? seconds;
            public string firstLogError;
            public PerfStats perf;
        }

        readonly List<Result> _results = new List<Result>();
        Result _current;

        void OnEnable() => Application.logMessageReceived += OnLog;
        void OnDisable() => Application.logMessageReceived -= OnLog;

        void OnLog(string msg, string stack, LogType type)
        {
            if (_current == null || (type != LogType.Exception && type != LogType.Error)) return;
            _current.logErrors = (_current.logErrors ?? 0) + 1;
            if (_current.firstLogError == null) _current.firstLogError = msg;
        }

        IEnumerator Start()
        {
            var total = Stopwatch.StartNew();
            string planPath = CommandLine.Get("-mnCapture");
            string outDir = Path.GetFullPath(CommandLine.Get("-mnOut", "captures"));
            CapturePlan plan;
            try
            {
                Directory.CreateDirectory(outDir);
                plan = CapturePlan.Load(planPath);
            }
            catch (Exception e)
            {
                MnLog.Error($"CAPTURE could not start: {e.Message}");
                Application.Quit(2);
                yield break;
            }

            var only = new HashSet<string>(CommandLine.GetList("-mnOnly"), StringComparer.OrdinalIgnoreCase);
            string kind = CommandLine.Get("-mnKind");
            bool wantStills = kind == null || kind == "stills";
            bool wantPerf = kind == null || kind == "perf";
            var stills = wantStills ? plan.stills.FindAll(s => only.Count == 0 || only.Contains(s.name)) : new List<StillShot>();
            var perfs = wantPerf ? plan.perf.FindAll(p => only.Count == 0 || only.Contains(p.name)) : new List<PerfRun>();
            MnLog.Info($"CAPTURE {stills.Count} still(s), {perfs.Count} perf run(s) from {planPath} -> {outDir}");

            // Let the boot settle: the sim has ticked and the first frame has rendered.
            yield return null;
            yield return null;

            var world = GameWorld.Current;
            var cam = world?.MainCamera;
            if (cam == null)
            {
                MnLog.Error("CAPTURE the game has no GameWorld.MainCamera to shoot with");
                Application.Quit(2);
                yield break;
            }

            bool selfTest = CoreSelfTest.Run();
            var basePose = new Pose(cam.transform.position, cam.transform.rotation);
            float baseFov = cam.fieldOfView;

            foreach (var s in stills)
            {
                var r = Begin(s.name, "still");
                var sw = Stopwatch.StartNew();
                Exception err = null;
                yield return Guard(RunStill(s, plan.defaults, outDir, r, cam, basePose, baseFov), e => err = e);
                End(r, err, sw);
            }

            foreach (var p in perfs)
            {
                var r = Begin(p.name, "perf");
                var sw = Stopwatch.StartNew();
                Exception err = null;
                yield return Guard(RunPerf(p, plan.defaults, outDir, r, cam, basePose, baseFov), e => err = e);
                End(r, err, sw);
            }

            LeaveCaptureMode();
            WriteManifest(outDir, planPath, selfTest);
            int failed = _results.FindAll(x => x.status != "ok").Count;
            MnLog.Info($"CAPTURE DONE ok={_results.Count - failed} failed={failed} selftest={(selfTest ? "ok" : "FAILED")} in {total.Elapsed.TotalSeconds:0.0}s manifest={Path.Combine(outDir, "capture_manifest.json")}");
            yield return null;
            Application.Quit(failed > 0 || !selfTest ? 1 : 0);
        }

        Result Begin(string name, string kind)
        {
            var r = new Result { name = name, kind = kind, status = "pending" };
            _results.Add(r);
            _current = r;
            return r;
        }

        void End(Result r, Exception err, Stopwatch sw)
        {
            r.seconds = Math.Round(sw.Elapsed.TotalSeconds, 2);
            r.simTick = GameWorld.Current?.Tick;
            string tail = r.logErrors > 0 ? $" ({r.logErrors} log errors, first: {r.firstLogError})" : "";
            if (err != null)
            {
                r.status = "failed";
                r.error = err.GetBaseException().Message;
                MnLog.Error($"{r.kind.ToUpperInvariant()} {r.name} FAILED: {err.GetBaseException()}");
            }
            else
            {
                r.status = "ok";
                if (r.kind == "still") MnLog.Info($"SHOT {r.name} ok {r.width}x{r.height} cam [{r.camera}] -> {r.file} ({r.seconds:0.0}s){tail}");
                else
                {
                    var f = r.perf;
                    MnLog.Info($"PERF {r.name} ok {r.width}x{r.height} avg {f.avgFps} fps, 1% low {f.low1PctFps} fps, worst {f.worstFrameMs} ms over {f.frames} frames -> {r.file}{tail}");
                }
            }
            _current = null;
        }

        // ---------------------------------------------------------------- stills

        IEnumerator RunStill(StillShot s, PlanDefaults d, string outDir, Result r, Camera cam, Pose basePose, float baseFov)
        {
            int w = s.width ?? d.width, h = s.height ?? d.height;
            float warmup = s.warmup ?? d.warmup;
            r.width = w; r.height = h; r.warmup = warmup;
            EnterCaptureMode(s.seed ?? d.seed);

            RenderTexture rt = null;
            Texture2D tex = null;
            CaptureCamera rig = null;
            try
            {
                rig = new CaptureCamera(cam, s, basePose, baseFov);
                r.camera = rig.Description;
                // sRGB target: the pipeline writes linear values and the hardware encodes them, as it does for the screen.
                rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "MN_Capture" };
                rt.Create();
                cam.targetTexture = rt;

                int frames = Mathf.Max(1, Mathf.RoundToInt(warmup * Sim.Hz));
                for (int i = 0; i < frames; i++) yield return null;
                yield return new WaitForEndOfFrame();

                tex = new Texture2D(w, h, TextureFormat.RGB24, false, false);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                tex.Apply(false);
                RenderTexture.active = prev;

                r.file = Path.Combine(outDir, s.name + ".png");
                File.WriteAllBytes(r.file, ImageConversion.EncodeToPNG(tex));
                Pixels.CheckNotBlank(tex, s.name);
            }
            finally
            {
                if (cam != null) cam.targetTexture = null;
                rig?.Dispose();
                if (rt != null) { rt.Release(); Destroy(rt); }
                if (tex != null) Destroy(tex);
            }
        }

        // ---------------------------------------------------------------- perf

        IEnumerator RunPerf(PerfRun p, PlanDefaults d, string outDir, Result r, Camera cam, Pose basePose, float baseFov)
        {
            int w = p.width ?? d.width, h = p.height ?? d.height;
            float warmup = p.warmup ?? d.perfWarmup;
            r.width = w; r.height = h; r.warmup = warmup;
            EnterRealtimeMode(p.seed ?? d.seed);
            if (Screen.width != w || Screen.height != h)
            {
                Screen.SetResolution(w, h, FullScreenMode.Windowed);
                for (int i = 0; i < 3; i++) yield return null;
            }

            using (var rig = new CaptureCamera(cam, p, basePose, baseFov))
            {
                r.camera = rig.Description;
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < warmup) yield return null;

                var sim = GameWorld.Current.Sim;
                int tick0 = sim.Tick;
                var ms = new List<double>(16384);
                double gpuSum = 0, cpuSum = 0; int gpuN = 0;
                var timings = new FrameTiming[1];
                var sw = Stopwatch.StartNew();
                double last = 0;
                // -mnPerfSync: the camera renders into a target of its own, and each frame waits for the GPU to finish it (a one-pixel read-back queued behind the work). On a
                // machine whose desktop session is disconnected the window's frames are never rendered or presented, the plain frame time is only the CPU's submit time and the
                // GPU's timer reads nonsense; this makes each frame cost what it really costs (the CPU's and the GPU's work together, no overlap, no present).
                bool sync = CommandLine.Has("-mnPerfSync");
                RenderTexture syncRt = null;
                if (sync)
                {
                    syncRt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "MN_PerfSync" };
                    syncRt.Create();
                    cam.targetTexture = syncRt;
                }
                while (true)
                {
                    yield return null;
                    if (sync)
                    {
                        yield return new WaitForEndOfFrame();
                        UnityEngine.Rendering.AsyncGPUReadback.Request(syncRt, 0, 0, 1, 0, 1, 0, 1, TextureFormat.RGBA32).WaitForCompletion();
                    }
                    double now = sw.Elapsed.TotalMilliseconds;
                    if (now - last > 120.0 && ms.Count > 0) MnLog.Info($"PERF hitch: a {now - last:0} ms frame at {now / 1000.0:0.00} s of the run");
                    ms.Add(now - last);
                    last = now;
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0)
                    {
                        gpuSum += timings[0].gpuFrameTime; cpuSum += timings[0].cpuMainThreadFrameTime; gpuN++;
                    }
                    if (now >= p.seconds * 1000.0) break;
                }

                if (syncRt != null) { cam.targetTexture = null; syncRt.Release(); UnityEngine.Object.Destroy(syncRt); }
                if (sync && ms.Count > 10) ms.RemoveAt(0);          // the first frame's time includes setting the synced target up
                var stats = PerfStats.From(ms);
                r.perf = stats;
                int ticks = sim.Tick - tick0;
                r.file = Path.Combine(outDir, "perf_" + p.name + ".json");
                var doc = new Dictionary<string, object>
                {
                    ["name"] = p.name,
                    ["avgFps"] = stats.avgFps,
                    ["low1PctFps"] = stats.low1PctFps,
                    ["worstFrameMs"] = stats.worstFrameMs,
                    ["p50Ms"] = stats.p50Ms,
                    ["p95Ms"] = stats.p95Ms,
                    ["p99Ms"] = stats.p99Ms,
                    ["frames"] = stats.frames,
                    ["seconds"] = stats.seconds,
                    ["simTicks"] = ticks,
                    ["simHzMeasured"] = Math.Round(ticks / stats.seconds, 2),
                    ["gpuAvgMs"] = gpuN > 0 ? (object)Math.Round(gpuSum / gpuN, 3) : null,
                    ["cpuMainAvgMs"] = gpuN > 0 ? (object)Math.Round(cpuSum / gpuN, 3) : null,
                    ["resolution"] = $"{Screen.width}x{Screen.height}",
                    ["camera"] = r.camera,
                    ["gpu"] = SystemInfo.graphicsDeviceName,
                    ["graphicsApi"] = SystemInfo.graphicsDeviceType.ToString(),
                    ["cpu"] = SystemInfo.processorType,
                    ["gpuSynced"] = sync,
                    ["vSyncCount"] = QualitySettings.vSyncCount,
                    ["unity"] = Application.unityVersion,
                    ["written"] = DateTime.Now.ToString("s"),
                    ["notes"] = "low1PctFps = 1000 / mean of the slowest 1% of frame times; frame times are wall-clock between frames after warm-up",
                };
                File.WriteAllText(r.file, JsonConvert.SerializeObject(doc, Formatting.Indented));
            }
        }

        // ---------------------------------------------------------------- modes

        static void EnterCaptureMode(int seed)
        {
            var sim = GameWorld.Current.Sim;
            Time.captureDeltaTime = 1f / Sim.Hz;     // game time moves 1/60 s per frame whatever the wall clock does
            sim.OneTickPerFrame = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            UnityEngine.Random.InitState(seed);
        }

        static void EnterRealtimeMode(int seed)
        {
            var sim = GameWorld.Current.Sim;
            Time.captureDeltaTime = 0f;
            sim.OneTickPerFrame = false;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            UnityEngine.Random.InitState(seed);
        }

        static void LeaveCaptureMode()
        {
            Time.captureDeltaTime = 0f;
            var world = GameWorld.Current;
            if (world != null) world.Sim.OneTickPerFrame = false;
        }

        void WriteManifest(string outDir, string planPath, bool selfTest)
        {
            var doc = new
            {
                plan = planPath,
                written = DateTime.Now.ToString("s"),
                gpu = SystemInfo.graphicsDeviceName,
                unity = Application.unityVersion,
                selfTest = selfTest ? "ok" : "failed",
                results = _results,
            };
            File.WriteAllText(Path.Combine(outDir, "capture_manifest.json"),
                JsonConvert.SerializeObject(doc, Formatting.Indented, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Runs a coroutine tree (nested IEnumerators flattened) and reports the first exception instead of dying silently.</summary>
        static IEnumerator Guard(IEnumerator body, Action<Exception> onError)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(body);
            while (stack.Count > 0)
            {
                object cur;
                try
                {
                    if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                    cur = stack.Peek().Current;
                }
                catch (Exception e) { onError(e); yield break; }
                if (cur is IEnumerator nested) { stack.Push(nested); continue; }
                yield return cur;
            }
        }

        static class Pixels
        {
            /// <summary>A shot that is one flat colour is almost always a broken scene, so say so in the log.</summary>
            public static void CheckNotBlank(Texture2D tex, string name)
            {
                var px = tex.GetPixels32();
                int step = Mathf.Max(1, px.Length / 4096);
                int max = 0, lit = 0, n = 0;
                for (int i = 0; i < px.Length; i += step)
                {
                    int v = Mathf.Max(px[i].r, Mathf.Max(px[i].g, px[i].b));
                    if (v > max) max = v;
                    if (v > 24) lit++;
                    n++;
                }
                if (max < 8) MnLog.Warn($"still '{name}' is black (brightest sample {max}/255)");
                else if (lit == n) MnLog.Warn($"still '{name}' has no dark pixels at all (every sample above 24/255)");
            }
        }
    }
}
