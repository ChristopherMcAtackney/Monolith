# Pipeline: edit, compile, build, capture

Everything runs headless from the repo root `C:\Users\chris\Monolith`. Every command works the same from PowerShell
and bash. Never start Unity any other way: `tools\unity.ps1` holds the project lock, and nobody opens the editor.

| Step | Command | Typical time |
|---|---|---|
| Compile check | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\unity.ps1 -CompileOnly` | 6 s warm, ~50 s after a package change |
| Project set-up (idempotent) | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\unity.ps1 Monolith.EditorTools.Setup.Project` | 7 s |
| Build player | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\unity.ps1 Monolith.EditorTools.Build.Player` | 12 s after a script edit, ~90 s cold (HDRP shaders) |
| Capture a plan | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\capture.ps1 -Plan gauntlet\shots\smoke.json -Out gauntlet\captures\smoke\r1` | 4 s boot + 0.3 s per still + the perf seconds |
| Blender script | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\blender.ps1 art_src\Viper\build_viper.py` | 2-5 s + your work |

The whole loop is **edit, Build.Player, capture** (~30 s); the build compiles too. Use `-CompileOnly` straight after
writing files. Fresh clone: run `-CompileOnly` (imports packages), then `Setup.Project`, then `Build.Player`.

## unity.ps1

```
tools\unity.ps1 -CompileOnly
tools\unity.ps1 <Namespace.Class.Method> [-TimeoutMin 45] [-NoGraphics] [-Full] [-- -mnYourArg value ...]
```
- Waits for the `unity` lock (`tools\.locks\unity.lock` holds the holder's PID). Stale locks and orphaned batch Unity
  processes are cleared automatically; a Unity Editor GUI open on `Game\` blocks it with a message.
- Summary: exit code, each `error CS` with file(line,col), exceptions, `[MN]` lines, the build result. The full log path
  is printed (`Game\Logs\unity_*.log`, last 40 kept).
- Exit codes: Unity's own (1 on compile errors or a thrown editor method), 2 = compile errors with Unity exiting 0,
  3 = lock timeout or an editor GUI is open, 4 = timed out (killed).
- Editor entry points live in `Game\Assets\Monolith\Editor` (namespace `Monolith.EditorTools`). Wrap the body in
  `Batch.Run("Name", () => {...})`: it logs `[MN] Name OK`, saves assets, and exits 1 on any exception. Read extra
  arguments with `Monolith.Core.CommandLine.Get("-mnYourArg")`.
- Built in: `Setup.Project` (HDRP asset and global settings, linear colour, Input System only, player settings, smoke
  materials, the Boot scene as the only scene) | `Setup.All` (Setup.Project + Build.Player in one session) |
  `Build.Player` (`-- -mnDev` for a development build) writes `Game\Builds\Win\Monolith.exe`.

## Capture plans (`gauntlet\shots\*.json`)

```json
{ "defaults": { "width": 1920, "height": 1080, "warmup": 1.0, "perfWarmup": 2.0 },
  "stills": [ { "name": "front", "pos": [0, 1.6, -9], "rot": [9, 0, 0], "fov": 40 },
              { "name": "side",  "pos": [-7, 1.2, -5.5], "lookAt": [0, 0, 0], "warmup": 2.0, "width": 1280, "height": 720 } ],
  "perf":   [ { "name": "smoke", "seconds": 10, "pos": [0, 1.6, -9], "rot": [9, 0, 0] } ] }
```
- A still: camera `pos` and `rot` (Euler degrees) or `lookAt`, `fov` (vertical degrees), `warmup` (game seconds run before
  the shot), `width`/`height`, `seed`. A missing camera field keeps the scene camera's own value. Names become file names.
  A misspelt field aborts the run.
- A perf run: the same camera fields plus `seconds` (real time, after `warmup`/`perfWarmup`). It plays in real time on a
  1920x1080 window with vsync off.
- Stills render offscreen at the exact resolution with game time fixed at one 60 Hz tick per frame, so reruns give the
  same picture. The window shows black while capturing; that is normal.
- Output in `-Out` (default `gauntlet\captures\<plan>_<stamp>\`, git-ignored): `<still>.png`, `perf_<run>.json`,
  `capture_manifest.json`, `player.log`. A perf JSON holds `avgFps`, `low1PctFps`, `worstFrameMs`, p50/p95/p99 ms,
  GPU and CPU ms, `simTicks` and `simHzMeasured` (should read ~60), and the GPU name.
- Every capture starts with `CoreSelfTest` (clock rate and order, events, pilots); its result is in the log and manifest.
- `capture.ps1` options: `-Only a,b` (some shots), `-Kind stills|perf`, `-Out`, `-Width/-Height` (window), `-TimeoutMin 15`,
  `-Exe`. Exit codes: 0 ok, 1 a shot or the self-test failed, 2 bad plan, 3 lock timeout, 4 timed out, 5 no build.
  A plan with perf runs also waits for the `unity` lock so a build cannot skew the numbers; builds hold the `player` lock.
- Smoke-plan baseline (RTX 4070 Ti, 1920x1080): about 1,080 fps average, 1% low about 540 fps, GPU 0.7 ms.

Player flags: `-mnCapture plan.json -mnOut dir [-mnOnly a,b] [-mnKind stills|perf] [-mnLogEvents]`.

## Blender

`tools\blender.ps1 <script.py> [-Blend file.blend] [-TimeoutMin 20] [-- script args]` runs
`blender -b --factory-startup` (Blender 5.2). `tools\` is put on `sys.path`, `MN_ROOT` is set, and a Python exception gives
a non-zero exit plus the traceback in the summary. Logs go to `Game\Logs\blender_*.log`. Keep scripts and `.blend` files in
`art_src\<model>\`; never put `.blend` files under `Game\Assets` (Unity would try to import them with Blender).
Shared Blender helpers will live in `tools\blender_lib\` when the first model script needs them.

## Gotchas

- **HDRP creates a global settings asset itself** at `Game\Assets\HDRPDefaultResources\` the first time the package loads;
  `Setup.Project` finds and registers it. Do not delete it.
- **Materials the player needs** must be assets (made by `Setup.Project`, kept under a `Resources` folder), not
  `Shader.Find` at runtime, or the build strips their shader variants. See `Hdrp.EnsureLit`.
- **Long runs and tool timeouts:** a foreground tool call dies at 10 min. Run cold builds or big captures in the
  background. If your shell is killed, the next `unity.ps1` waits for the orphaned Unity (up to 20 min) before it starts.
- **Shared compile:** every builder's scripts compile together. Errors in someone else's files usually mean they are
  mid-edit: wait and retry. Never leave your own files broken.
- **Determinism:** gameplay uses the sim tick (`SimFrame.Time`), not `UnityEngine.Time`. `UnityEngine.Random` is seeded per
  shot; `System.Random` is not.
- **Resolution:** in a capture, `Screen.width/height` is the window, not the shot. Use `camera.pixelWidth`.
- **Unity Personal** shows a splash for about 2 s at player start. It does not affect captures.
- **Disk:** `Game\Library` is ~2 GB and each cold shader build adds more. Clean old `gauntlet\captures\` runs.
